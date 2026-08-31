using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services.GameModes
{
    public class ClassicGuessSongGameMode : IGameMode
    {
        private readonly IDeezerService _deezerService;

        public ClassicGuessSongGameMode(IDeezerService deezerService)
        {
            _deezerService = deezerService;
        }

        public GameModeType ModeType => GameModeType.ClassicGuessSong;

        public async Task<List<TriviaQuestionModel>> GenerateQuestionsAsync(List<TrackModel> tracks, int numberOfQuestions)
        {
            if (tracks.Count < 4)
            {
                throw new InvalidOperationException("Not enough tracks in the current playlist");
            }

            var shuffledPool = new List<TrackModel>(tracks);
            Shuffle(shuffledPool);

            int maxAttempts = Math.Min(shuffledPool.Count, numberOfQuestions * 3);

            var quizQuestions = new List<TriviaQuestionModel>();
            var usedTrackIds = new HashSet<string>();

            for (int i = 0; i < maxAttempts; i++)
            {
                if (quizQuestions.Count >= numberOfQuestions)
                {
                    break;
                }

                var candidate = shuffledPool[i];

                var previewUrl = await _deezerService.GetPreviewUrlAsync(candidate.Artist, candidate.Title);
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
                    SpotifyUrl = candidate.SpotifyUrl ?? ""
                });

                //  TODO: Duplication prevention (same for Guess Artist)
                usedTrackIds.Add(candidate.Id);
            }

            if (quizQuestions.Count == 0)
            {
                throw new InvalidOperationException("Couldn't find playable previews for any tracks in this playlist.");
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
            //  Fisher-Yates Shuffler
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