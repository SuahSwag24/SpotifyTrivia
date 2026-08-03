using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpotifyTrivia.Models;

namespace SpotifyTrivia.Services
{
    public class TriviaEngine : ITriviaEngine
    {
        private readonly IDeezerService _deezerService;

        public TriviaEngine(IDeezerService deezerService)
        {
            _deezerService = deezerService;
        }

        public async Task<List<TriviaQuestionModel>> CreateTriviaQuestions(List<TrackModel> tracks, int numberOfQuestions = 10)
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
                    CorrectAnswer = correctAnswer,
                    AnswerChoices = choices
                });

                usedTrackIds.Add(candidate.Id);
            }

            if (quizQuestions.Count == 0)
            {
                throw new InvalidOperationException("Couldn't find playable previews for any tracks in this playlist.");
            }

            return quizQuestions;
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