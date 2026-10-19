using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services.GameModes
{
    public class ClassicGuessSongGameMode : ISearchAnswerGameMode
    {
        private readonly IDeezerService _deezerService;
        private const double SELFCONTRIBUTIONPENALTYMULTIPLIER = 0.8;
        private const double FAIRDISTRIBUTIONWEIGHTAGE = 0.6;
        private const double ARTISTONLYSCOREMULTIPLIER = 0.4;

        public ClassicGuessSongGameMode(IDeezerService deezerService)
        {
            _deezerService = deezerService;
        }

        public GameModeType ModeType => GameModeType.ClassicGuessSong;

        public async Task<List<TriviaQuestionModel>> GenerateQuestionsAsync(List<TrackModel> tracks, int numberOfQuestions, HashSet<string> excludedTrackIds, IEnumerable<string> lobbyPlayerIds, int roundDurationSeconds)
        {
            var shuffledPool = new List<TrackModel>(tracks)
                .Where(t => !excludedTrackIds.Contains(t.Id))
                .ToList();

            if (shuffledPool.Count < 4)
            {
                throw new PlaylistExhaustedException("Not enough remaining unplayed tracks to generate more questions.");
            }

            shuffledPool = BuildFairShuffledPool(shuffledPool, lobbyPlayerIds, numberOfQuestions);

            int maxAttempts = Math.Min(shuffledPool.Count, numberOfQuestions * 3);

            var quizQuestions = new List<TriviaQuestionModel>();

            for (int i = 0; i < maxAttempts; i++)
            {
                if (quizQuestions.Count >= numberOfQuestions)
                {
                    break;
                }

                var candidate = shuffledPool[i];

                var isrc = candidate.Isrc;
                if (string.IsNullOrEmpty(isrc)) continue;

                var deezerTrack = await _deezerService.GetTrackByIsrcAsync(isrc);
                if (deezerTrack == null || string.IsNullOrEmpty(deezerTrack.Preview)) continue;
                var previewUrl = deezerTrack.Preview;

                string correctAnswer = $"{candidate.Title} - {candidate.Artist}";

                var wrongAnswers = tracks
                    .Where(t => t.Id != candidate.Id)
                    .ToList();

                Shuffle(wrongAnswers);

                var choices = wrongAnswers
                    .Take(3)
                    .Select(t => $"{t.Title} - {t.Artist}")
                    .ToList();

                choices.Add(correctAnswer);
                Shuffle(choices);

                quizQuestions.Add(new TriviaQuestionModel
                {
                    TargetTrackId = candidate.Id,
                    PreviewUrl = previewUrl,
                    AlbumCoverUrl = candidate.AlbumCoverUrl ?? string.Empty,
                    SongTitle = candidate.Title,
                    ArtistName = candidate.Artist,
                    Prompt = "Name the Song & Artist",
                    CorrectAnswer = correctAnswer,
                    AnswerChoices = choices,
                    SpotifyUrl = candidate.SpotifyUrl ?? "",
                    ContributedByPlayerIds = candidate.ContributedByPlayerIds,
                    Isrc = isrc,
                    DeezerTrackId = deezerTrack.Id,
                    DeezerArtistId = deezerTrack.Artist?.Id ?? 0,
                    DeezerTitle = deezerTrack.Title ?? string.Empty
                }); ;

                excludedTrackIds.Add(candidate.Id);
            }

            if (quizQuestions.Count < numberOfQuestions)
            {
                throw new PlaylistExhaustedException("Not enough playable tracks remaining to generate a full round.");
            }

            return quizQuestions;
        }

        public AnswerResultModel EvaluateAnswer(
            TriviaQuestionModel question,
            int choiceIndex,
            DateTime roundStartedAtUtc,
            DateTime answeredAtUtc,
            double roundDurationSeconds,
            string playerId)
        {
            int correctIndex = question.AnswerChoices.IndexOf(question.CorrectAnswer);
            bool isCorrect = choiceIndex == correctIndex;
            bool isSelfContributed = isCorrect && question.ContributedByPlayerIds.Contains(playerId);

            int score = 0;
            if (isCorrect)
            {
                score = CalculateScore(roundStartedAtUtc, answeredAtUtc, roundDurationSeconds, isSelfContributed);
            }

            return new AnswerResultModel
            {
                Success = true,
                WasCorrect = isCorrect,
                SubmittedIndex = choiceIndex,
                CorrectIndex = correctIndex,
                CorrectAnswerText = question.CorrectAnswer,
                AwardedScore = score,
                WasSelfContributionPenalty = isSelfContributed
            };
        }
        public AnswerResultModel EvaluateSearchAnswer(TriviaQuestionModel question, SearchAnswerModel answer, DateTime roundStartedAtUtc, DateTime answeredAtUtc, double roundDurationSeconds, string playerId)
        {
            bool artistMatch = answer.DeezerArtistId != 0 && answer.DeezerArtistId == question.DeezerArtistId;

            bool trackMatch = (answer.DeezerTrackId != 0 && answer.DeezerTrackId == question.DeezerTrackId)
                || (!string.IsNullOrEmpty(answer.Isrc) && string.Equals(answer.Isrc, question.Isrc, StringComparison.OrdinalIgnoreCase))
                || (artistMatch && answer.Title.Length > 0 && NormalizeTitle(answer.Title) == NormalizeTitle(question.DeezerTitle));

            bool artistOnly = !trackMatch && artistMatch;
            bool scored = trackMatch || artistOnly;
            bool isSelfContributed = scored && question.ContributedByPlayerIds.Contains(playerId);

            int score = scored ? CalculateScore(roundStartedAtUtc, answeredAtUtc, roundDurationSeconds, isSelfContributed, trackMatch ? 1.0 : ARTISTONLYSCOREMULTIPLIER) : 0;

            return new AnswerResultModel
            {
                Success = true,
                WasCorrect = trackMatch,
                WasArtistCorrect = artistMatch,
                SubmittedIndex = -1,
                CorrectIndex = -1,
                CorrectAnswerText = question.CorrectAnswer,
                AwardedScore = score,
                WasSelfContributionPenalty = isSelfContributed
            };
        }

        private void Shuffle<T>(IList<T> list)
        {
            //  Fisher-Yates Shuffler
            int n = list.Count;
            while (n > 1)
            {
                n--;

                int k = Random.Shared.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }

        private List<TrackModel> BuildFairShuffledPool(List<TrackModel> trackPool, IEnumerable<string> lobbyPlayerIds, int numberOfQuestions)
        {
            int roundRobinCount = (int)Math.Ceiling(numberOfQuestions * FAIRDISTRIBUTIONWEIGHTAGE);
            int freeForAllCount = numberOfQuestions - roundRobinCount;

            var picked = new HashSet<string>();

            //  Shuffle contributor queues
            var playerIds = lobbyPlayerIds.ToList();
            Shuffle(playerIds);

            var playerQueues = playerIds
                .Select(pid =>
                {
                    var tracks = trackPool.Where(t => t.ContributedByPlayerIds.Contains(pid)).ToList();
                    Shuffle(tracks);
                    return new Queue<TrackModel>(tracks);
                })
                .Where(q => q.Count > 0)
                .ToList();

            //  Partition 1: Round-Robin, allows uneven amounts of song and cycles through players.
            var roundRobinPartition = new List<TrackModel>();
            while (roundRobinPartition.Count < roundRobinCount && playerQueues.Any(q => q.Count > 0))
            {
                foreach (var queue in playerQueues)
                {
                    if (roundRobinPartition.Count >= roundRobinCount) break;

                    while (queue.Count > 0 && picked.Contains(queue.Peek().Id))
                        queue.Dequeue();

                    if (queue.Count == 0) continue;

                    var track = queue.Dequeue();
                    picked.Add(track.Id);
                    roundRobinPartition.Add(track);
                }
            }

            //  Partition 2: Free-For-All, random and no-fair weighting
            var freeForAllPool = trackPool.Where(t => !picked.Contains(t.Id)).ToList();
            Shuffle(freeForAllPool);

            var freeForAllPartition = freeForAllPool.Take(freeForAllCount).ToList();
            foreach (var t in freeForAllPartition) picked.Add(t.Id);

            // Everything else becomes the retry buffer for the maxAttempts loop
            var buffer = trackPool.Where(t => !picked.Contains(t.Id)).ToList();
            Shuffle(buffer);

            var questionPool = new List<TrackModel>();
            questionPool.AddRange(roundRobinPartition);
            questionPool.AddRange(freeForAllPartition);
            Shuffle(questionPool); // mix the two segments so consumption order isn't RR-first

            questionPool.AddRange(buffer);
            return questionPool;
        }

        private int CalculateScore(DateTime roundStartedAtUtc, DateTime playerAnsweredAtUtc, double roundDurationSeconds, bool isSelfContributed, double scoreMultiplier = 1.0)
        {
            double elapsedSeconds = (playerAnsweredAtUtc - roundStartedAtUtc).TotalSeconds;
            double score = 100 * (1 - elapsedSeconds / roundDurationSeconds) * scoreMultiplier;

            if (isSelfContributed)
            {
                score *= SELFCONTRIBUTIONPENALTYMULTIPLIER;
            }

            return Math.Clamp((int)Math.Round(score), 1, 100);
        }

        private static readonly Regex TrailingBracket =
            new(@"\s*[\(\[（【][^\)\]）】]*[\)\]）】]\s*$", RegexOptions.Compiled);
        private static readonly Regex TrailingDashSuffix =
            new(@"\s+-\s+[^-]*(remaster|live|version|ver\.|edit|mix|mono|stereo|acoustic)[^-]*$",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static string NormalizeTitle(string title)
        {
            var original = title.Normalize(NormalizationForm.FormKC).Trim();   // FormKC folds full-width forms
            var s = original;
            string previous;
            do
            {
                previous = s;
                s = TrailingBracket.Replace(s, "");
                s = TrailingDashSuffix.Replace(s, "");
            } while (s != previous && s.Length > 0);

            return (s.Length == 0 ? original : s).ToLowerInvariant().Trim();
        }
    }
}