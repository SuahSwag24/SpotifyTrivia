using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;
using SpotifyTrivia.Models.Singleplayer;
using SpotifyTrivia.Services.GameModes;

namespace SpotifyTrivia.Services.Singleplayer
{
    public record SpResult<T>(T? Value, int Status = 200, string? Error = null)
    {
        public bool Ok => Error == null;
    }

    public record SingleplayerStartParams(
        string PlayerId,
        string DisplayName,
        string AccessToken,
        string? RefreshToken,
        string PlaylistId,
        GameModeType GameMode,
        int QuestionCount,
        int RoundDurationSeconds,
        int? SampleSize,
        AlbumCoverVisibility BlurAlbum,
        AnswerStyle AnswerStyle
    );

    public record SingleplayerStartResult(string SessionId, int TotalQuestions, string? RefreshedAccessToken);

    public class SingleplayerService : ISingleplayerService
    {
        private const double AnswerGraceSeconds = 1.5;
        private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(3); // Session lifetime limiter set to 3 hours

        private readonly ConcurrentDictionary<string, SingleplayerSessionModel> _sessions = new();
        private readonly ISpotifyService _spotifyService;
        private readonly IGameModeFactory _gameModeFactory;
        private readonly ILogger<SingleplayerService> _logger;

        public SingleplayerService(ISpotifyService spotifyService, IGameModeFactory gameModeFactory, ILogger<SingleplayerService> logger)
        {
            _spotifyService = spotifyService;
            _gameModeFactory = gameModeFactory;
            _logger = logger;
        }
        async Task<SpResult<SingleplayerStartResult>> ISingleplayerService.StartAsync(SingleplayerStartParams p)
        {
            //  Delete expired or old sessions from same player
            foreach (var old in _sessions.Values.Where(s => s.PlayerId == p.PlayerId || DateTime.UtcNow - s.CreatedAtUtc > SessionLifetime))
            {
                _sessions.TryRemove(old.Id, out _);
            }

            var mode = _gameModeFactory.GetGameMode(p.GameMode);
            string token = p.AccessToken;
            string? refreshedToken = null;
            int total = 0;
            List<TrackModel> tracks;

            try
            {
                var fetch = await _spotifyService.GetPlaylistTracksAsync(token, p.RefreshToken, p.PlaylistId, p.SampleSize, 0);
                refreshedToken = fetch.RefreshedAccessToken;
                total = fetch.Total;
                tracks = fetch.Data ?? new List<TrackModel>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Singleplayer track fetch failed for playlist {PlaylistId}", p.PlaylistId);
                return new(default, 502, "Couldn't load that playlist. Try another.");
            }

            if (tracks.Count < 4)
            {
                return new(default, 422, "Not enough tracks in that playlist to start.");
            }

            async Task<List<TriviaQuestionModel>?> TryGenerate(List<TrackModel> pool)
            {
                try
                {
                    return await mode.GenerateQuestionsAsync(pool, p.QuestionCount, new HashSet<string>(), Array.Empty<string>(), p.RoundDurationSeconds);
                }
                catch (PlaylistExhaustedException) { return null; };
            }

            var questions = await TryGenerate(tracks);

            if (questions == null && p.SampleSize is int sample && total > sample)
            {
                try
                {
                    int offset = Random.Shared.Next(0, total - sample + 1);
                    var retry = await _spotifyService.GetPlaylistTracksAsync(refreshedToken ?? token, p.RefreshToken, p.PlaylistId, p.SampleSize, offset);
                    refreshedToken = retry.RefreshedAccessToken ?? refreshedToken;
                    questions = await TryGenerate(retry.Data ?? new List<TrackModel>());
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Singleplayer retry fetch failed for playlist {PlaylistId}", p.PlaylistId);
                }
            }

            if (questions == null)
                return new(default, 422, "Couldn't fund enough playable tracks for the game.");

            var session = new SingleplayerSessionModel
            {
                PlayerId = p.PlayerId,
                DisplayName = p.DisplayName,
                GameMode = p.GameMode,
                AnswerStyle = p.AnswerStyle,
                BlurAlbum = p.BlurAlbum,
                RoundDurationSeconds = p.RoundDurationSeconds,
                Questions = questions
            };

            _sessions[session.Id] = session;

            return new(new SingleplayerStartResult(session.Id, questions.Count, refreshedToken));
        }

        SpResult<object> ISingleplayerService.Prepare(string sessionId, string playerId, int round)
        {
            if (!TryGet(sessionId, playerId, round, out var s, out int idx, out var err)) return new(default, err!.Value.Status, err.Value.Message);
            var q = s.Questions[idx];
            return new(new { q.Prompt, q.PreviewUrl, RoundId = $"{s.Id}-{round}" });
        }

        SpResult<object> ISingleplayerService.Begin(string sessionId, string playerId, int round)
        {
            if (!TryGet(sessionId, playerId, round, out var s, out int idx, out var err)) return new(default, err!.Value.Status, err.Value.Message);

            lock (s.Sync)
            {
                bool resume = s.CurrentIndex == idx && s.RoundState == SingleplayerRoundState.Active;
                bool next = s.CurrentIndex == idx - 1 && (s.CurrentIndex == -1 || s.RoundState == SingleplayerRoundState.Ended);
                if (!resume && !next) return new(default, 409, "Round out of order.");

                if (next)
                {
                    s.CurrentIndex = idx;
                    s.RoundState = SingleplayerRoundState.Active;
                    s.AnsweredCurrent = false;
                    s.RoundStartedAtUtc = DateTime.UtcNow;
                }

                var q = s.Questions[idx];
                return new(new
                {
                    q.PreviewUrl,
                    q.AlbumCoverUrl,
                    AnswerChoices = q.ChoicesFor(s.AnswerStyle),
                    AnswerStyle = s.AnswerStyle.ToString(),
                    StartedAtUtc = s.RoundStartedAtUtc,
                    DurationSeconds = s.RoundDurationSeconds,
                    QuestionNumber = round,
                    TotalQuestions = s.Questions.Count,
                    BlurAlbum = s.BlurAlbum.ToString(),
                    AlreadyAnswered = s.AnsweredCurrent
                });
            }
        }

        SpResult<AnswerResultModel> ISingleplayerService.Answer(string sessionId, string playerId, int round, int choiceIndex, SearchAnswerModel? searchAnswer)
        {
            if (!TryGet(sessionId, playerId, round, out var s, out int idx, out var err)) return new(default, err!.Value.Status, err.Value.Message);

            lock (s.Sync)
            {
                if (s.CurrentIndex != idx || s.RoundState != SingleplayerRoundState.Active)
                    return new(default, 409, "That round isn't active.");
                if (s.AnsweredCurrent)
                    return new(default, 409, "Already answered.");

                var now = DateTime.UtcNow;
                if ((now - s.RoundStartedAtUtc).TotalSeconds > s.RoundDurationSeconds + AnswerGraceSeconds)
                    return new(default, 409, "Time's up.");

                var q = s.Questions[idx];
                var mode = _gameModeFactory.GetGameMode(s.GameMode);
                AnswerResultModel result;

                if (s.AnswerStyle == AnswerStyle.Search)
                {
                    if (searchAnswer == null || mode is not ISearchAnswerGameMode searchMode)
                        return new(default, 400, "A search answer is required.");
                    result = searchMode.EvaluateSearchAnswer(q, searchAnswer, s.RoundStartedAtUtc, now, s.RoundDurationSeconds, s.PlayerId);
                }
                else
                {
                    if (choiceIndex < 0 || choiceIndex >= q.AnswerChoices.Count)
                        return new(default, 400, "Invalid choice.");
                    result = mode.EvaluateAnswer(q, choiceIndex, s.RoundStartedAtUtc, now, s.RoundDurationSeconds, s.PlayerId);
                }

                s.AnsweredCurrent = true;
                s.Score += result.AwardedScore;
                s.History.Add(result);
                return new(result);
            }
        }

        SpResult<object> ISingleplayerService.End(string sessionId, string playerId, int round)
        {
            if (!TryGet(sessionId, playerId, round, out var s, out int idx, out var err)) return new(default, err!.Value.Status, err.Value.Message);

            lock (s.Sync)
            {
                if (s.CurrentIndex != idx || s.RoundState == SingleplayerRoundState.Idle)
                    return new(default, 409, "That round hasn't started.");

                var q = s.Questions[idx];

                if (s.RoundState == SingleplayerRoundState.Active)
                {
                    //  Don't hand out the answer early to someone who hasn't answered
                    var elapsed = (DateTime.UtcNow - s.RoundStartedAtUtc).TotalSeconds;
                    if (!s.AnsweredCurrent && elapsed < s.RoundDurationSeconds - 1)
                        return new(default, 409, "Round still running.");

                    if (!s.AnsweredCurrent)
                    {
                        s.History.Add(new AnswerResultModel
                        {
                            Success = true,
                            WasCorrect = false,
                            SubmittedIndex = -1,
                            CorrectIndex = q.AnswerChoices.IndexOf(q.CorrectAnswer),
                            CorrectAnswerText = q.CorrectAnswer,
                            AwardedScore = 0
                        });
                        s.AnsweredCurrent = true;
                    }
                    s.RoundState = SingleplayerRoundState.Ended;
                }

                var last = s.History[^1];
                return new(new
                {
                    CorrectAnswer = q.CorrectAnswer,
                    AlbumCoverUrl = q.AlbumCoverUrl,
                    SpotifyUrl = q.SpotifyUrl,
                    IsFinalRound = round == s.Questions.Count,
                    Players = new[]
                    {
                        new
                        {
                            s.PlayerId,
                            s.DisplayName,
                            s.Score,
                            ScoreDelta = last.AwardedScore,
                            LastAnswerCorrect = (bool?)last.WasCorrect,
                            LastAnswerArtistCorrect = last.WasArtistCorrect,
                            LastAnswerPenalized = last.WasSelfContributionPenalty
                        }
                    }
                });
            }
        }

        SpResult<object> ISingleplayerService.Summary(string sessionId, string playerId)
        {
            if (!_sessions.TryGetValue(sessionId, out var s) || s.PlayerId != playerId)
                return new(default, 404, "Game not found.");

            lock (s.Sync)
            {
                if (s.History.Count < s.Questions.Count || s.RoundState != SingleplayerRoundState.Ended)
                    return new(default, 409, "Game isn't finished.");

                return new(new
                {
                    s.Score,
                    TotalQuestions = s.Questions.Count,
                    CorrectCount = s.History.Count(h => h.WasCorrect),
                    Rounds = s.Questions.Select((q, i) => new
                    {
                        q.SongTitle,
                        q.ArtistName,
                        q.AlbumCoverUrl,
                        q.SpotifyUrl,
                        Result = s.History[i]
                    })
                });
            }
        }

        private bool TryGet(string sessionId, string playerId, int round, out SingleplayerSessionModel session, out int index, out (int Status, string Message)? error)
        {
            session = null!;
            index = round - 1;
            error = null;

            if (!_sessions.TryGetValue(sessionId, out var s) || s.PlayerId != playerId)
            {
                error = (404, "Game not found.");
                return false;
            }
            if (index < 0 || index >= s.Questions.Count)
            {
                error = (400, "Invalid round.");
                return false;
            }

            session = s;
            return true;
        }
    }
}
