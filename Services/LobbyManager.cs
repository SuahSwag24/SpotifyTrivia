using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc.Controllers;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;
using SpotifyTrivia.Services.GameModes;

namespace SpotifyTrivia.Services
{
    public enum LobbySessionEndReason
    {
        CompletedNormally,
        Disbanded,
        Error
    }

    public class LobbyManager : ILobbyManager
    {
        private readonly ConcurrentDictionary<string, LobbyModel> _lobbies = new();
        private readonly IGameModeFactory _gameModeFactory;
        private readonly IBroadcaster _lobbyBroadcaster;
        private readonly LobbySettingsModel _settings;
        private readonly ConcurrentDictionary<string, (string lobbyCode, string playerId)> _connectionMap = new();
        private readonly ILogger<LobbyManager> _logger;
        private readonly ISpotifyService _spotifyService;
        
        public LobbyManager(IGameModeFactory gameModeFactory, IBroadcaster lobbyBroadcaster, ILogger<LobbyManager> logger, ISpotifyService spotifyService, LobbySettingsModel settings)
        {
            _gameModeFactory = gameModeFactory;
            _lobbyBroadcaster = lobbyBroadcaster;
            _settings = settings;
            _logger = logger;
            _spotifyService = spotifyService;
        }

        public LobbyModel CreateLobby(string hostPlayerId, string hostPlayerName, string hostAccessToken, string? hostRefreshToken)
        {
            string code = GenerateLobbyCode();

            var lobby = new LobbyModel
            {
                Code = code,
                PlayerHostId = hostPlayerId,
                HostDisplayName = hostPlayerName,
                HostSpotifyAccessToken = hostAccessToken,
                HostSpotifyRefreshToken = hostRefreshToken
            };

            var host = new PlayerModel
            {
                PlayerId = hostPlayerId,
                DisplayName = hostPlayerName,
                SpotifyAccessToken = hostAccessToken,
                SpotifyRefreshToken = hostRefreshToken
            };

            lobby.Players[hostPlayerId] = host;
            _lobbies[code] = lobby;

            return lobby;
        }

        public LobbyModel? GetLobby(string code) =>
            _lobbies.TryGetValue(code, out var lobby) ? lobby : null;

        public bool TryAddPlayer(string code, string playerId, string displayName, string connectionId, out PlayerModel? player, out bool isNewPlayer)
        {
            player = null;
            isNewPlayer = false;
            if (!_lobbies.TryGetValue(code, out var lobby)) return false;

            isNewPlayer = !lobby.Players.ContainsKey(playerId);

            if (lobby.State == LobbyState.Finished && isNewPlayer) return false;

            if (isNewPlayer && lobby.Players.Count >= lobby.MaxPlayers) return false;

            player = lobby.Players.GetOrAdd(playerId, _ => new PlayerModel
            {
                PlayerId = playerId,
                DisplayName = displayName,
                JoinStatus = lobby.State == LobbyState.Waiting
                    ? PlayerJoinStatus.Active
                    : PlayerJoinStatus.PendingJoin
            });

            player.ConnectionId = connectionId;
            player.IsConnected = true;
            _connectionMap[connectionId] = (code, playerId);

            return true;
        }

        public void MarkPlayerConnection(string code, string playerId, bool isConnected, string connectionId)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return;
            if (!lobby.Players.TryGetValue(playerId, out var player)) return;

            player.IsConnected = isConnected;
            player.ConnectionId = isConnected ? connectionId : null;
            player.Status = isConnected
                ? PlayerStatus.Active
                : PlayerStatus.Disconnected;
        }

        public void MarkPlayerAsLeft(string code, string playerId)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return;
            if (!lobby.Players.TryGetValue(playerId, out var player)) return;

            player.IsConnected = false;
            player.ConnectionId = null;
            player.Status = PlayerStatus.Disconnected;

            var staleConnections = _connectionMap
                .Where(kvp => kvp.Value.lobbyCode == code && kvp.Value.playerId == playerId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connectionId in staleConnections)
            {
                _connectionMap.TryRemove(connectionId, out _);
            }
        }

        public async Task<AnswerResultModel> RecordPlayerAnswerAsync(string code, string playerId, int choiceIndex)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return new AnswerResultModel { Success = false };

            await lobby.StateLock.WaitAsync();
            try
            {
                if (lobby.State != LobbyState.Question) return new AnswerResultModel { Success = false };
                if (!lobby.Players.TryGetValue(playerId, out var player)) return new AnswerResultModel { Success = false };
                if (player.HasAnsweredCurrentQuestion) return new AnswerResultModel { Success = false };

                var answeredAtUtc = DateTime.UtcNow;

                var currentQuestion = lobby.Questions[lobby.CurrentQuestionIndex];
                var gameMode = _gameModeFactory.GetGameMode(lobby.GameMode);

                var result = gameMode.EvaluateAnswer(
                    currentQuestion,
                    choiceIndex,
                    lobby.RoundStartedAtUtc,
                    answeredAtUtc,
                    lobby.RoundDurationSeconds,
                    playerId
                );

                player.HasAnsweredCurrentQuestion = true;
                player.LastAnswerCorrect = result.WasCorrect;
                player.LastAnswerSubmittedUtc = DateTime.UtcNow;
                player.LastAnswerPenalized = result.WasSelfContributionPenalty;

                if (result.WasCorrect)
                {
                    player.Score += result.AwardedScore;
                }

                player.AnswerHistory.Add(result);

                return result;
            }
            finally
            {
                lobby.StateLock.Release();
            }
        }

        public async Task<bool> InvalidatePlayerAnswerAsync(string code, string playerId)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return false;

            await lobby.StateLock.WaitAsync();
            try
            {
                if (lobby.State != LobbyState.Question) return true;

                if (!lobby.Players.TryGetValue(playerId, out var player)) return false;
                if (player.JoinStatus != PlayerJoinStatus.Active) return !player.HasAnsweredCurrentQuestion;
                if (player.HasAnsweredCurrentQuestion)
                {
                    player.Status = PlayerStatus.Locked;
                    return false;
                }

                var question = lobby.Questions[lobby.CurrentQuestionIndex];
                player.HasAnsweredCurrentQuestion = true;
                player.LastAnswerCorrect = false;
                player.LastAnswerSubmittedUtc = DateTime.UtcNow;
                player.AnswerHistory.Add(new AnswerResultModel
                {
                    Success = true,
                    WasCorrect = false,
                    SubmittedIndex = -1,
                    CorrectIndex = question.AnswerChoices.IndexOf(question.CorrectAnswer),
                    CorrectAnswerText = question.CorrectAnswer,
                    AwardedScore = 0
                });
                player.Status = PlayerStatus.Locked;

                return false;
            }
            finally
            {
                lobby.StateLock.Release();
            }
        }

        public void RemovePlayer(string code, string playerId)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return;

            lobby.Players.TryRemove(playerId, out _);

            var staleConnections = _connectionMap
                .Where(kvp => kvp.Value.lobbyCode == code && kvp.Value.playerId == playerId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connId in staleConnections)
            {
                _connectionMap.TryRemove(connId, out _);
            }
        }

        public async Task StartSessionAsync(string code, List<TrackModel> tracks, int questionCount, int roundDurationSeconds)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return;

            var mode = _gameModeFactory.GetGameMode(lobby.GameMode);

            var lobbyPlayerIds = lobby.Players.Values
                .Where(p => p.JoinStatus == PlayerJoinStatus.Active)
                .Select(p => p.PlayerId);
            var cancellationToken = lobby.LifetimeCts.Token;

            lobby.CachedTracks = tracks;

            try
            {
                try
                {
                    lobby.Questions = await GenerateLobbyQuestionsAsync(mode, tracks, questionCount, lobby.PlayedTrackIds, lobbyPlayerIds, roundDurationSeconds, cancellationToken);
                }
                catch (PlaylistExhaustedException) when (CanRetryWithFreshSample(lobby))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var freshTracks = await RefetchSample(lobby);
                    cancellationToken.ThrowIfCancellationRequested();
                    lobby.CachedTracks = freshTracks;
                    lobby.Questions = await GenerateLobbyQuestionsAsync(mode, freshTracks, questionCount, lobby.PlayedTrackIds, lobbyPlayerIds, roundDurationSeconds, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            lobby.SessionLoopCts?.Cancel();
            lobby.SessionLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lobby.RoundDurationSeconds = roundDurationSeconds > 0 ? roundDurationSeconds : _settings.RoundDurationSeconds;

            if (lobby.GameMode == GameModeType.StemGuess)
            {
                _ = RunStemSessionLoop(lobby, lobby.SessionLoopCts.Token);
            }
            else
            {
                _ = RunSessionLoop(lobby, lobby.SessionLoopCts.Token);
            }
        }

        public async Task ContinueSessionAsync(string code, List<TrackModel> tracks)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return;

            var mode = _gameModeFactory.GetGameMode(lobby.GameMode);

            var lobbyPlayerIds = lobby.Players.Values
                .Where(p => p.JoinStatus == PlayerJoinStatus.Active)
                .Select(p => p.PlayerId);
            var cancellationToken = lobby.LifetimeCts.Token;

            try
            {
                try
                {
                    lobby.Questions = await GenerateLobbyQuestionsAsync(mode, tracks, lobby.NumberOfQuestions, lobby.PlayedTrackIds, lobbyPlayerIds, 15, cancellationToken);
                }
                catch (PlaylistExhaustedException) when (CanRetryWithFreshSample(lobby))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var freshTracks = await RefetchSample(lobby);
                    cancellationToken.ThrowIfCancellationRequested();
                    lobby.CachedTracks = freshTracks;
                    lobby.Questions = await GenerateLobbyQuestionsAsync(mode, freshTracks, lobby.NumberOfQuestions, lobby.PlayedTrackIds, lobbyPlayerIds, 15, cancellationToken);
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            foreach (var p in lobby.Players.Values)
            {
                p.AnswerHistory.Clear();
                p.HasAnsweredCurrentQuestion = false;
                p.LastAnswerPenalized = false;
                p.LastAnswerCorrect = null;
            }

            lobby.SessionLoopCts?.Cancel();
            lobby.SessionLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            if (lobby.GameMode == GameModeType.StemGuess)
            {
                _ = RunStemSessionLoop(lobby, lobby.SessionLoopCts.Token);
            }
            else
            {
                _ = RunSessionLoop(lobby, lobby.SessionLoopCts.Token);
            }
        }

        public (string lobbyCode, string playerId)? GetConnectionMapping(string connectionId)
        {
            return _connectionMap.TryGetValue(connectionId, out var mapping) ? mapping : null;
        }

        public void RemoveConnectionMapping(string connectionId)
        {
            _connectionMap.TryRemove(connectionId, out _);
        }

        public void DisbandLobby(string code)
        {
            if (!_lobbies.TryRemove(code, out var lobby)) return;

            lobby.LifetimeCts.Cancel();
            lobby.SessionLoopCts?.Cancel();

            var staleConnecctions = _connectionMap
                .Where(kvp => kvp.Value.lobbyCode == code)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connId in staleConnecctions)
            {
                _connectionMap.TryRemove(connId, out _);
            }
        }
        
        public bool ResetLobbyToWaiting(string code)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return false;

            lobby.State = LobbyState.Waiting;
            lobby.Questions = new List<TriviaQuestionModel>();
            lobby.CurrentQuestionIndex = 0;
            lobby.SelectedPlaylistId = null;
            lobby.SelectedPlaylistName = null;
            lobby.PlayedTrackIds.Clear();

            foreach (var p in lobby.Players.Values)
            {
                p.Score = 0;
                p.HasAnsweredCurrentQuestion = false;
                p.LastAnswerCorrect = null;
                p.JoinStatus = PlayerJoinStatus.Active;
                p.LastAnswerPenalized = false;
                p.AnswerHistory.Clear();
            }

            return true;
        }

        public void KickPlayer(string code, string playerId)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return;

            lobby.Players.TryRemove(playerId, out _);

            var staleConnections = _connectionMap
                .Where(kvp => kvp.Value.lobbyCode == code && kvp.Value.playerId == playerId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connectionId in staleConnections)
            {
                _connectionMap.TryRemove(connectionId, out _);
            }
        }

        public async Task MarkRoundAsReadyAsync(string code, string playerId, string roundId)
        {
            var lobby = GetLobby(code);
            if (lobby == null) return;

            await lobby.StateLock.WaitAsync();
            try
            {
                if (lobby.State != LobbyState.PreparingRound ||
                    lobby.CurrentRoundId != roundId ||
                    !lobby.Players.TryGetValue(playerId, out var player) ||
                    player.JoinStatus != PlayerJoinStatus.Active ||
                    !player.IsConnected)
                {
                    return;
                }

                lobby.RequiredRoundReadyPlayerIds.Add(playerId);
                lobby.ReadyRoundPlayerIds.Add(playerId);

                if (lobby.RequiredRoundReadyPlayerIds.IsSubsetOf(lobby.ReadyRoundPlayerIds))
                {
                    lobby.RoundReadiness?.TrySetResult(true);
                }
            }
            finally
            {
                lobby.StateLock.Release();
            }
        }

        private string GenerateLobbyCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            string code;
            do
            {
                code = new string(Enumerable.Range(0, 5)
                    .Select(_ => chars[Random.Shared.Next(chars.Length)])
                    .ToArray());
            } while (_lobbies.ContainsKey(code));

            return code;
        }

        private async Task RunSessionLoop(LobbyModel lobby, CancellationToken ct)
        {
            LobbySessionEndReason endReason = LobbySessionEndReason.CompletedNormally;

            try
            {
                for (int i = 0; i < lobby.Questions.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    string roundId;
                    Task readinessTask;

                    List<PlayerModel> promoted = new();
                    await lobby.StateLock.WaitAsync(ct);

                    try
                    {
                        promoted = lobby.Players.Values
                            .Where(p => p.JoinStatus == PlayerJoinStatus.PendingJoin)
                            .ToList();

                        foreach (var p in promoted)
                        {
                            p.JoinStatus = PlayerJoinStatus.Active;

                            //  Fill missed round history
                            for (int missed = 0; missed < i; missed++)
                            {
                                p.AnswerHistory.Add(new AnswerResultModel
                                {
                                    Success = true,
                                    WasCorrect = false,
                                    SubmittedIndex = -1,
                                    CorrectIndex = lobby.Questions[missed].AnswerChoices.IndexOf(lobby.Questions[missed].CorrectAnswer),
                                    CorrectAnswerText = lobby.Questions[missed].CorrectAnswer,
                                    AwardedScore = 0
                                });
                            }
                        }
                    }
                    finally { lobby.StateLock.Release(); }

                    if (promoted.Count > 0)
                    {
                        await _lobbyBroadcaster.BroadcastPlayerJoining(lobby.Code, promoted.Select(p => p.DisplayName).ToList());
                        await Task.Delay(TimeSpan.FromSeconds(_settings.JoinGraceSeconds), ct);

                        foreach (var p in promoted)
                        {
                            if (p.ConnectionId != null)
                            {
                                await _lobbyBroadcaster.SendPromotedToActive(p.ConnectionId);
                            }
                        }
                    }

                    await lobby.StateLock.WaitAsync(ct);
                    try
                    {
                        lobby.CurrentQuestionIndex = i;
                        lobby.State = LobbyState.PreparingRound;
                        
                        roundId = Guid.NewGuid().ToString("N");
                        lobby.CurrentRoundId = roundId;

                        lobby.RequiredRoundReadyPlayerIds = lobby.Players.Values
                            .Where(p => p.JoinStatus == PlayerJoinStatus.Active
                                && p.IsConnected
                                && p.ConnectionId != null)
                            .Select(p => p.PlayerId)
                            .ToHashSet();

                        lobby.ReadyRoundPlayerIds.Clear();

                        var readiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        lobby.RoundReadiness = readiness;
                        readinessTask = readiness.Task;

                        if (lobby.RequiredRoundReadyPlayerIds.Count == 0)
                        {
                            readiness.TrySetResult(true);
                        }

                        foreach (var p in lobby.Players.Values)
                        {
                            if (p.Status == PlayerStatus.Disconnected)
                            {
                                lobby.Players.Remove(p.PlayerId, out _);
                                await _lobbyBroadcaster.BroadcastPlayerLeft(lobby.Code, p.PlayerId, p.DisplayName);
                                continue;
                            }

                            p.HasAnsweredCurrentQuestion = false;
                            p.LastAnswerCorrect = null;
                            p.LastAnswerPenalized = false;
                            p.Status = PlayerStatus.Active;
                            await _lobbyBroadcaster.BroadcastPlayerStatusChanged(lobby.Code, p.PlayerId, p.Status);
                        }
                    }
                    finally { lobby.StateLock.Release(); }

                    var question = lobby.Questions[i];

                    await _lobbyBroadcaster.BroadcastRoundPreparing(lobby.Code, question.Prompt, question.PreviewUrl, roundId);

                    try
                    {
                        await readinessTask.WaitAsync(TimeSpan.FromSeconds(_settings.AudioReadyTimeoutSeconds), ct);
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogInformation(
                            "Audio readiness timed out for round {RoundId} in lobby {LobbyCode}",
                            roundId,
                            lobby.Code);
                    }

                    await lobby.StateLock.WaitAsync(ct);
                    try
                    {
                        lobby.State = LobbyState.Countdown;
                        lobby.CountdownStartedAtUtc = DateTime.UtcNow;
                    }
                    finally { lobby.StateLock.Release(); }

                    await _lobbyBroadcaster.BroadcastCountdownStart(
                        lobby.Code,
                        _settings.CountdownSeconds,
                        lobby.CountdownStartedAtUtc,
                        question.Prompt,
                        question.PreviewUrl,
                        roundId);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.CountdownSeconds), ct);

                    await lobby.StateLock.WaitAsync(ct);
                    try
                    {
                        lobby.State = LobbyState.Question;
                        lobby.RoundStartedAtUtc = DateTime.UtcNow;
                    }
                    finally { lobby.StateLock.Release(); }

                    await _lobbyBroadcaster.BroadcastRoundStarted(lobby.Code, question, lobby.RoundStartedAtUtc, lobby.RoundDurationSeconds, questionNumber: i + 1, totalQuestions: lobby.Questions.Count, blurAlbum: lobby.BlurAlbum);
                    await Task.Delay(TimeSpan.FromSeconds(lobby.RoundDurationSeconds), ct);

                    await lobby.StateLock.WaitAsync(ct);
                    try 
                    { 
                        lobby.State = LobbyState.Reveal; 

                        foreach (var p in lobby.Players.Values)
                        {
                            if (!p.HasAnsweredCurrentQuestion)
                            {
                                p.AnswerHistory.Add(new AnswerResultModel
                                {
                                    Success = true,
                                    WasCorrect = false,
                                    SubmittedIndex = -1,
                                    CorrectIndex = question.AnswerChoices.IndexOf(question.CorrectAnswer),
                                    CorrectAnswerText = question.CorrectAnswer,
                                    AwardedScore = 0
                                });
                            }
                        }
                    }
                    finally { lobby.StateLock.Release(); }

                    await _lobbyBroadcaster.BroadcastRoundEnded(lobby.Code, question.CorrectAnswer, lobby.Players.Values.ToList(), question.AlbumCoverUrl);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.RevealSeconds), ct);
                }
            }
            catch (OperationCanceledException)
            {
                endReason = LobbySessionEndReason.Disbanded;
                _logger.LogInformation("Lobby session loop canceled for lobby {Code}", lobby.Code);
            }
            catch (Exception ex)
            {
                endReason = LobbySessionEndReason.Error;
                _logger.LogError(ex, "Lobby session loop failed unexpectedly for lobby {Code}", lobby.Code);
            }

            _logger.LogInformation("Lobby session loop ending for lobby {Code} with reason {Reason}", lobby.Code, endReason);
            await HandleSessionEnd(lobby, endReason);
        }

        //  Separate function for Stem GameMode game flow
        private async Task RunStemSessionLoop(LobbyModel lobby, CancellationToken ct)
        {
            LobbySessionEndReason endReason = LobbySessionEndReason.CompletedNormally;

            try
            {
                for (int i = 0; i < lobby.Questions.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    string roundId;
                    Task readinessTask;

                    List<PlayerModel> promoted = new();
                    await lobby.StateLock.WaitAsync(ct);

                    try
                    {
                        promoted = lobby.Players.Values
                            .Where(p => p.JoinStatus == PlayerJoinStatus.PendingJoin)
                            .ToList();

                        foreach (var p in promoted)
                        {
                            p.JoinStatus = PlayerJoinStatus.Active;

                            for (int missed = 0; missed < i; missed++)
                            {
                                p.AnswerHistory.Add(new AnswerResultModel
                                {
                                    Success = true,
                                    WasCorrect = false,
                                    SubmittedIndex = -1,
                                    CorrectIndex = lobby.Questions[missed].AnswerChoices.IndexOf(lobby.Questions[missed].CorrectAnswer),
                                    CorrectAnswerText = lobby.Questions[missed].CorrectAnswer,
                                    AwardedScore = 0
                                });
                            }
                        }
                    }
                    finally
                    {
                        lobby.StateLock.Release();
                    }

                    if (promoted.Count > 0)
                    {
                        await _lobbyBroadcaster.BroadcastPlayerJoining(lobby.Code, promoted.Select(p => p.DisplayName).ToList());
                        await Task.Delay(TimeSpan.FromSeconds(_settings.JoinGraceSeconds), ct);

                        foreach (var p in promoted)
                        {
                            if (p.ConnectionId != null)
                            {
                                await _lobbyBroadcaster.SendPromotedToActive(p.ConnectionId);
                            }
                        }
                    }

                    await lobby.StateLock.WaitAsync(ct);
                    try
                    {
                        lobby.CurrentQuestionIndex = i;
                        lobby.State = LobbyState.PreparingRound;

                        roundId = Guid.NewGuid().ToString("N");
                        lobby.CurrentRoundId = roundId;

                        lobby.RequiredRoundReadyPlayerIds = lobby.Players.Values
                            .Where(p => p.JoinStatus == PlayerJoinStatus.Active
                                && p.IsConnected
                                && p.ConnectionId != null)
                            .Select(p => p.PlayerId)
                            .ToHashSet();

                        lobby.ReadyRoundPlayerIds.Clear();

                        var readiness = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        lobby.RoundReadiness = readiness;
                        readinessTask = readiness.Task;

                        if (lobby.RequiredRoundReadyPlayerIds.Count == 0)
                        {
                            readiness.TrySetResult(true);
                        }

                        foreach (var p in lobby.Players.Values)
                        {
                            if (p.Status == PlayerStatus.Disconnected)
                            {
                                lobby.Players.Remove(p.PlayerId, out _);
                                await _lobbyBroadcaster.BroadcastPlayerLeft(lobby.Code, p.PlayerId, p.DisplayName);
                                continue;
                            }

                            p.HasAnsweredCurrentQuestion = false;
                            p.LastAnswerCorrect = null;
                            p.LastAnswerPenalized = false;
                            p.Status = PlayerStatus.Active;
                            await _lobbyBroadcaster.BroadcastPlayerStatusChanged(lobby.Code, p.PlayerId, p.Status);
                        }
                    }
                    finally
                    {
                        lobby.StateLock.Release();
                    }

                    var question = lobby.Questions[i];

                    await _lobbyBroadcaster.BroadcastRoundPreparing(lobby.Code, question.Prompt, question.PreviewUrl, roundId, question.StemUrls);

                    var stemsReadyDeadline = DateTime.UtcNow.AddSeconds(_settings.StemReadyTimeoutSeconds);
                    while (question.StemRevealOrder.Count == 0 && DateTime.UtcNow < stemsReadyDeadline)
                    {
                        await Task.Delay(500, ct);
                    }

                    if (question.StemRevealOrder.Count == 0)
                    {
                        _logger.LogWarning("Stem separation timed out for round {RoundId} in lobby {LobbyCode}; falling back to full preview", roundId, lobby.Code);
                        // Fallback: treat it like a single-layer reveal (full mix only), so the round doesn't hang indefinitely. Adjust if you'd rather skip the question entirely.
                        question.StemRevealOrder = new List<string> { "drums", "bass", "other", "vocals" };
                    }

                    try
                    {
                        await readinessTask.WaitAsync(TimeSpan.FromSeconds(_settings.AudioReadyTimeoutSeconds), ct);
                    }
                    catch (TimeoutException)
                    {
                        _logger.LogInformation("Audio readiness timed out for round {RoundId} in lobby {LobbyCode}", roundId, lobby.Code);
                    }

                    await lobby.StateLock.WaitAsync(ct);
                    try
                    {
                        lobby.State = LobbyState.Countdown;
                        lobby.CountdownStartedAtUtc = DateTime.UtcNow;
                    }
                    finally { lobby.StateLock.Release(); }

                    await _lobbyBroadcaster.BroadcastCountdownStart(lobby.Code, _settings.CountdownSeconds, lobby.CountdownStartedAtUtc, question.Prompt, question.PreviewUrl, roundId);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.CountdownSeconds), ct);

                    await lobby.StateLock.WaitAsync(ct);
                    try
                    {
                        lobby.State = LobbyState.Question;
                        lobby.RoundStartedAtUtc = DateTime.UtcNow;
                    }
                    finally { lobby.StateLock.Release(); }

                    //  Stem Game Mode Exclusive: Incremental layer-by-layer reveal
                    await _lobbyBroadcaster.BroadcastRoundStarted(lobby.Code, question, lobby.RoundStartedAtUtc, lobby.RoundDurationSeconds, questionNumber: i + 1, totalQuestions: lobby.Questions.Count, blurAlbum: lobby.BlurAlbum);

                    int layerCount = question.StemRevealOrder.Count;
                    double layerIntervalSeconds = lobby.RoundDurationSeconds / (double)layerCount;

                    await _lobbyBroadcaster.BroadcastRevealLayer(lobby.Code, roundId, layerIndex: 0, stem: question.StemRevealOrder[0]);

                    for (int layer = 1; layer < layerCount; layer++)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(layerIntervalSeconds), ct);
                        ct.ThrowIfCancellationRequested();
                        await _lobbyBroadcaster.BroadcastRevealLayer(lobby.Code, roundId, layerIndex: layer, stem: question.StemRevealOrder[layer]);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(layerIntervalSeconds), ct);

                    await lobby.StateLock.WaitAsync(ct);
                    try
                    {
                        lobby.State = LobbyState.Reveal;

                        foreach (var p in lobby.Players.Values)
                        {
                            if (!p.HasAnsweredCurrentQuestion)
                            {
                                p.AnswerHistory.Add(new AnswerResultModel
                                {
                                    Success = true,
                                    WasCorrect = false,
                                    SubmittedIndex = -1,
                                    CorrectIndex = question.AnswerChoices.IndexOf(question.CorrectAnswer),
                                    CorrectAnswerText = question.CorrectAnswer,
                                    AwardedScore = 0
                                });
                            }
                        }
                    }
                    finally { lobby.StateLock.Release(); }

                    await _lobbyBroadcaster.BroadcastRoundEnded(lobby.Code, question.CorrectAnswer, lobby.Players.Values.ToList(), question.AlbumCoverUrl);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.RevealSeconds), ct);
                }
            }
            catch (OperationCanceledException)
            {
                endReason = LobbySessionEndReason.Disbanded;
                _logger.LogInformation("Stem lobby session loop canceled for lobby {Code}", lobby.Code);
            }
            catch (Exception ex)
            {
                endReason = LobbySessionEndReason.Error;
                _logger.LogError(ex, "Stem lobby session loop failed unexpectedly for lobby {Code}", lobby.Code);
            }

            _logger.LogInformation("Stem lobby session loop ending for lobby {Code} with reason {Reason}", lobby.Code, endReason);
            await HandleSessionEnd(lobby, endReason);
        }

        private async Task HandleSessionEnd(LobbyModel lobby, LobbySessionEndReason reason)
        {
            switch (reason)
            {
                case LobbySessionEndReason.CompletedNormally:
                    lobby.State = LobbyState.Finished;
                    var leaderboard = lobby.Players.Values
                        .OrderByDescending(p => p.Score)
                        .ToList();

                    var songResults = lobby.Questions.Select(q => (object)new

                    {
                        songTitle = q.SongTitle,
                        artistName = q.ArtistName,
                        spotifyUrl = q.SpotifyUrl,
                        albumCoverUrl = q.AlbumCoverUrl,
                        previewUrl = q.PreviewUrl,
                        contributedBy = q.ContributedByPlayerIds
                            .Select(id => lobby.Players.TryGetValue(id, out var p) ? p.DisplayName : null)
                            .Where(name => name != null)
                            .ToList()
                    }).ToList();

                    await _lobbyBroadcaster.BroadcastGameEnded(lobby.Code, leaderboard, songResults);
                    break;

                case LobbySessionEndReason.Disbanded:
                    break;

                case LobbySessionEndReason.Error:
                    _logger.LogWarning("Disbanding lobby {Code} because its session loop failed", lobby.Code);
                    await _lobbyBroadcaster.BroadcastLobbyDisbanded(lobby.Code);
                    _lobbies.TryRemove(lobby.Code, out _);
                    break;
            }    
        }

        private async Task<List<TrackModel>> RefetchPlaylistWindow(LobbyModel lobby, int offset)
        {
            var result = await _spotifyService.GetPlaylistTracksAsync(
                lobby.HostSpotifyAccessToken, lobby.HostSpotifyRefreshToken, lobby.SelectedPlaylistId!,
                sampleSize: lobby.SampleSize, offset: offset);

            if (result.RefreshedAccessToken != null)
            {
                lobby.HostSpotifyAccessToken = result.RefreshedAccessToken;
                if (lobby.Players.TryGetValue(lobby.PlayerHostId, out var host))
                {
                    host.SpotifyAccessToken = result.RefreshedAccessToken;
                }
            }

            lobby.PlaylistTotal = result.Total;
            lobby.LastFetchOffset = offset;

            var tracks = result.Data ?? new List<TrackModel>();

            var spotifyIdToPlayerId = lobby.Players.Values
                .Where(p => !string.IsNullOrEmpty(p.SpotifyUserId))
                .ToDictionary(p => p.SpotifyUserId!, p => p.PlayerId);

            foreach (var track in tracks)
            {
                if (!string.IsNullOrEmpty(track.AddedBySpotifyUserId) &&
                    spotifyIdToPlayerId.TryGetValue(track.AddedBySpotifyUserId, out var matchedPlayerId))
                {
                    track.ContributedByPlayerIds.Add(matchedPlayerId);
                }
            }

            return tracks;
        }

        private async Task<List<TrackModel>> RefetchLikedSongsForAllPlayers(LobbyModel lobby)
        {
            var eligiblePlayers = lobby.Players.Values
                .Where(p => !string.IsNullOrEmpty(p.SpotifyAccessToken)
                            && lobby.SampleSize.HasValue
                            && p.LikedSongsLastOffset + lobby.SampleSize.Value < p.LikedSongsTotal)
                .ToList();

            var perPlayerResult = await Task.WhenAll(
                eligiblePlayers.Select(async p =>
                {
                    try
                    {
                        var nextOffset = p.LikedSongsLastOffset + lobby.SampleSize!.Value;

                        var result = await _spotifyService.GetLikedSongsAsync(
                            p.SpotifyAccessToken, p.SpotifyRefreshToken,
                            sampleSize: lobby.SampleSize, offset: nextOffset);

                        if (result.RefreshedAccessToken != null)
                        {
                            p.SpotifyAccessToken = result.RefreshedAccessToken;
                        }

                        p.LikedSongsTotal = result.Total;
                        p.LikedSongsLastOffset = nextOffset;

                        var playerTracks = result.Data ?? new List<TrackModel>();

                        foreach (var t in playerTracks)
                        {
                            t.ContributedByPlayerIds.Add(p.PlayerId);
                        }

                        return playerTracks;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to refetch liked songs for player {PlayerId} in Lobby {Lobby}", p.PlayerId, lobby.Code);
                        return new List<TrackModel>();
                    }
                })
            );

            return perPlayerResult
                .SelectMany(t => t)
                .GroupBy(t => t.Id)
                .Select(g =>
                {
                    var merged = g.First();
                    merged.ContributedByPlayerIds = g.SelectMany(t => t.ContributedByPlayerIds).Distinct().ToList();
                    return merged;
                })
                .ToList();
        }

        private Task<List<TriviaQuestionModel>> GenerateLobbyQuestionsAsync(
            IGameMode mode,
            List<TrackModel> tracks,
            int questionCount,
            HashSet<string> excludedTrackIds,
            IEnumerable<string> lobbyPlayerIds,
            int roundDurationSeconds,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (mode is StemGuessGameMode stemGuessMode)
            {
                return stemGuessMode.GenerateQuestionsAsync(
                    tracks,
                    questionCount,
                    excludedTrackIds,
                    lobbyPlayerIds,
                    roundDurationSeconds,
                    cancellationToken);
            }

            return mode.GenerateQuestionsAsync(tracks, questionCount, excludedTrackIds, lobbyPlayerIds, roundDurationSeconds);
        }

        private bool CanRetryWithFreshSample(LobbyModel lobby)
        {
            if (!lobby.SampleSize.HasValue) return false;

            if (lobby.SelectedPlaylistId == "__liked_songs__")
            {
                return lobby.Players.Values.Any(p =>
                    !string.IsNullOrEmpty(p.SpotifyAccessToken)
                    && p.LikedSongsLastOffset + lobby.SampleSize!.Value < p.LikedSongsTotal);
            }

            if (lobby.SelectedPlaylistId == "__recent_songs__")
            {
                return false; // no sampling/offset applies to this source
            }

            return lobby.PlaylistTotal > 0
                && lobby.LastFetchOffset + lobby.SampleSize.Value < lobby.PlaylistTotal;
        }

        private async Task<List<TrackModel>> RefetchSample(LobbyModel lobby)
        {
            return lobby.SelectedPlaylistId == "__liked_songs__"
                ? await RefetchLikedSongsForAllPlayers(lobby)
                : await RefetchPlaylistWindow(lobby, lobby.LastFetchOffset + lobby.SampleSize!.Value);
        }
    }
}
