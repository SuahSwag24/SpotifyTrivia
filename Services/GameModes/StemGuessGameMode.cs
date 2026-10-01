using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;
using SpotifyTrivia.Services.Stems;

namespace SpotifyTrivia.Services.GameModes
{
    public class StemGuessGameMode : IGameMode
    {
        private readonly IDeezerService _deezerService;
        private readonly StemPipeline _stemPipeline;
        private const double SELFCONTRIBUTIONPENALTYMULTIPLIER = 0.8;
        private const double FAIRDISTRIBUTIONWEIGHTAGE = 0.6;
        private readonly ConcurrentDictionary<string, TriviaQuestionModel> _pendingByJobId;

        public StemGuessGameMode(IDeezerService deezerService, StemPipeline stemPipeline, PendingStemQuestions pendingStemQuestions)
        {
            _deezerService = deezerService;
            _stemPipeline = stemPipeline;
            _pendingByJobId = pendingStemQuestions.Map;
        }

        public GameModeType ModeType => GameModeType.StemGuess;
        public async Task<List<TriviaQuestionModel>> GenerateQuestionsAsync(List<TrackModel> tracks, int numberOfQuestions, HashSet<string> excludedTrackIds, IEnumerable<string> lobbyPlayerIds)
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

                var previewUrl = await _deezerService.GetPreviewUrlAsync(isrc);
                if (string.IsNullOrEmpty(previewUrl))
                {
                    continue;
                }

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

                var jobId = Guid.NewGuid().ToString("N");

                var question = new TriviaQuestionModel
                {
                    TargetTrackId = candidate.Id,
                    PreviewUrl = previewUrl,
                    AlbumCoverUrl = candidate.AlbumCoverUrl ?? string.Empty,
                    SongTitle = candidate.Title,
                    ArtistName = candidate.Artist,
                    Prompt = "Use the Stems to Guess the Song",
                    CorrectAnswer = correctAnswer,
                    AnswerChoices = choices,
                    SpotifyUrl = candidate.SpotifyUrl ?? "",
                    ContributedByPlayerIds = candidate.ContributedByPlayerIds,
                    StemUrls = new()
                    {
                        $"/api/stems/{jobId}/drums",
                        $"/api/stems/{jobId}/bass",
                        $"/api/stems/{jobId}/other",
                        $"/api/stems/{jobId}/vocals",
                    }
                };

                quizQuestions.Add(question);
                _pendingByJobId[jobId] = question;
                _stemPipeline.Enqueue(new StemJob(jobId, previewUrl, StartSec: 5, DurationSec: 15));

                excludedTrackIds.Add(candidate.Id);
            }

            if (quizQuestions.Count < numberOfQuestions)
            {
                throw new PlaylistExhaustedException("Not enough playable tracks remaining.");
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

                //  Future scoring strategy
                //  int layerIndex = GetRevealedLayerIndex(question, roundStartedAtUtc, answeredAtUtc);
                //  double layerMultiplier = GetLayerMultiplier(layerIndex, question.StemRevealOrder.Count);
                //  score = (int)Math.Round(score * layerMultiplier);
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

        // Which stem layer was audible when the player answered.
        // Mirrors the fixed offsets used for BroadcastRevealLayer (0s, 5s, 10s, ...).
        private int GetRevealedLayerIndex(TriviaQuestionModel question, DateTime roundStartedAtUtc, DateTime answeredAtUtc)
        {
            double elapsedSec = (answeredAtUtc - roundStartedAtUtc).TotalSeconds;
            int layerCount = question.StemRevealOrder.Count; // e.g. 4: drums, bass, other, vocals
            double layerIntervalSec = question.StemDurationMs / 1000.0 / layerCount;

            int layerIndex = (int)(elapsedSec / layerIntervalSec);
            return Math.Clamp(layerIndex, 0, layerCount - 1);
        }

        // Earlier guesses (fewer layers revealed) score higher.
        // layerIndex 0 = only first stem playing; last index = full mix.
        private double GetLayerMultiplier(int layerIndex, int layerCount)
        {
            if (layerCount <= 1) return 1.0;
            // e.g. 4 layers -> 1.0, 0.85, 0.7, 0.55
            return 1.0 - (0.15 * layerIndex);
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

        private int CalculateScore(DateTime roundStartedAtUtc, DateTime playerAnsweredAtUtc, double roundDurationSeconds, bool isSelfContributed)
        {
            double elapsedSeconds = (playerAnsweredAtUtc - roundStartedAtUtc).TotalSeconds;
            double score = 100 * (1 - elapsedSeconds / roundDurationSeconds);

            if (isSelfContributed)
            {
                score *= SELFCONTRIBUTIONPENALTYMULTIPLIER;
            }

            return Math.Clamp((int)Math.Round(score), 1, 100);
        }
    }
}
