using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing.Matching;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services.GameModes
{
    public class GuessArtistGameMode : IGameMode
    {
        private readonly IDeezerService _deezerService;
        private const double SELFCONTRIBUTIONPENALTYMULTIPLIER = 0.8;
        private const double FAIRDISTRIBUTIONWEIGHTAGE = 0.6;

        public GuessArtistGameMode(IDeezerService deezerService)
        {
            _deezerService = deezerService;
        }

        public GameModeType ModeType => GameModeType.GuessArtist;

        public async Task<List<TriviaQuestionModel>> GenerateQuestionsAsync(List<TrackModel> tracks, int numberOfQuestions, HashSet<string> excludedTrackIds, IEnumerable<string> lobbyPlayerIds)
        {
            var shuffledPool = new List<TrackModel>(tracks)
                .Where(t => !excludedTrackIds.Contains(t.Id))
                .ToList();

            if (shuffledPool.Count < numberOfQuestions)
            {
                throw new PlaylistExhaustedException("Not enough remaining unplayed tracks to generate more questions.");
            }

            // Need at least 4 distinct artists, otherwise distractors can't be built
            var distinctArtistCount = tracks.Select(t => t.Artist).Distinct().Count();
            if (distinctArtistCount < 4)
            {
                throw new InvalidOperationException("Not enough distinct artists in this playlist for Guess Artist mode.");
            }

            shuffledPool = BuildFairShuffledPool(shuffledPool, lobbyPlayerIds, numberOfQuestions);

            int maxAttempts = Math.Min(shuffledPool.Count, numberOfQuestions * 3);
            var quizQuestions = new List<TriviaQuestionModel>();

            for (int i = 0; i < maxAttempts && quizQuestions.Count < numberOfQuestions; i++)
            {
                var candidate = shuffledPool[i];

                var isrc = candidate.Isrc;
                if (string.IsNullOrEmpty(isrc)) continue;

                var previewUrl = await _deezerService.GetPreviewUrlAsync(isrc);
                if (string.IsNullOrEmpty(previewUrl)) continue;

                string correctAnswer = candidate.Artist;

                // Dedup by artist so we don't get 3 wrong choices that are all the same artist
                var wrongArtists = tracks
                    .Select(t => t.Artist)
                    .Distinct()
                    .Where(a => a != candidate.Artist)
                    .ToList();
                Shuffle(wrongArtists);

                if (wrongArtists.Count < 3) continue; // not enough unique distractors for this candidate

                var choices = wrongArtists.Take(3).ToList();
                choices.Add(correctAnswer);
                Shuffle(choices);

                quizQuestions.Add(new TriviaQuestionModel
                {
                    TargetTrackId = candidate.Id,
                    PreviewUrl = previewUrl,
                    AlbumCoverUrl = candidate.AlbumCoverUrl ?? string.Empty,
                    SongTitle = candidate.Title,
                    ArtistName = candidate.Artist,
                    Prompt = "Guess the Artist",
                    CorrectAnswer = correctAnswer,
                    AnswerChoices = choices,
                    SpotifyUrl = candidate.SpotifyUrl ?? "",
                    ContributedByPlayerIds = candidate.ContributedByPlayerIds
                });

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

        private void Shuffle<T>(IList<T> list)
        {
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
            while (roundRobinPartition.Count < roundRobinCount && playerQueues.Any(q => q.Count > 0));
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