using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services.GameModes
{
    public class GuessArtistGameMode : IGameMode
    {
        private readonly IDeezerService _deezerService;

        public GuessArtistGameMode(IDeezerService deezerService)
        {
            _deezerService = deezerService;
        }

        public GameModeType ModeType => GameModeType.GuessArtist;

        public async Task<List<TriviaQuestionModel>> GenerateQuestionsAsync(List<TrackModel> tracks, int numberOfQuestions)
        {
            if (tracks.Count < 4)
            {
                throw new InvalidOperationException("Not enough tracks in the current playlist");
            }

            // Need at least 4 distinct artists, otherwise distractors can't be built
            var distinctArtistCount = tracks.Select(t => t.Artist).Distinct().Count();
            if (distinctArtistCount < 4)
            {
                throw new InvalidOperationException("Not enough distinct artists in this playlist for Guess Artist mode.");
            }

            var shuffledPool = new List<TrackModel>(tracks);
            Shuffle(shuffledPool);

            int maxAttempts = Math.Min(shuffledPool.Count, numberOfQuestions * 3);
            var quizQuestions = new List<TriviaQuestionModel>();

            for (int i = 0; i < maxAttempts && quizQuestions.Count < numberOfQuestions; i++)
            {
                var candidate = shuffledPool[i];

                var previewUrl = await _deezerService.GetPreviewUrlAsync(candidate.Artist, candidate.Title);
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
                    Prompt = "Guess the Artist",
                    CorrectAnswer = correctAnswer,
                    AnswerChoices = choices
                });
            }

            if (quizQuestions.Count == 0)
            {
                throw new InvalidOperationException("Couldn't generate any Guess Artist questions for this playlist.");
            }

            return quizQuestions;
        }

        public AnswerResultModel EvaluateAnswer(
            TriviaQuestionModel question,
            int choiceIndex,
            DateTime roundStartedAtUtc,
            DateTime answeredAtUtc,
            double roundDurationSeconds)
        {

            int correctIndex = question.AnswerChoices.IndexOf(question.CorrectAnswer);
            bool isCorrect = choiceIndex == correctIndex;

            int score = 0;
            if (isCorrect)
            {
                double elapsedSeconds = (answeredAtUtc - roundStartedAtUtc).TotalSeconds;
                score = Math.Clamp((int)Math.Round(100 * (1 - elapsedSeconds / roundDurationSeconds)), 1, 100);
            }

            return new AnswerResultModel
            {
                Success = true,
                WasCorrect = isCorrect,
                SubmittedIndex = choiceIndex,
                CorrectIndex = correctIndex,
                CorrectAnswerText = question.CorrectAnswer,
                AwardedScore = score
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
    }
}