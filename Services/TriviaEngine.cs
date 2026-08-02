using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpotifyTrivia.Models;

namespace SpotifyTrivia.Services
{
    public class TriviaEngine : ITriviaEngine
    {
        public async Task<List<TriviaQuestionModel>> CreateTriviaQuestions(List<TrackModel> tracks, int numberOfQuestions = 10)
        {
            var playableTracks = tracks
            .Where(t => !string.IsNullOrEmpty(t.Uri))
            .ToList();

            if (playableTracks.Count < 4)
            {
                throw new InvalidOperationException("Not enough tracks in the current playlist.");
            }

            var quizQuestions = new List<TriviaQuestionModel>();

            var targetPool = new List<TrackModel>(playableTracks);
            Shuffle(targetPool);

            var selectedTarget = targetPool.Take(numberOfQuestions).ToList();

            foreach (var target in selectedTarget)
            {
                string correctAnswer = $"{target.Title} - {target.Artist}";

                var wrongAnswers = playableTracks
                    .Where(t => t.Id != target.Id)
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
                    TargetTrackId = target.Id,
                    Uri = target.Uri,
                    AlbumCoverUrl = target.AlbumCoverUrl ?? string.Empty,
                    CorrectAnswer = correctAnswer,
                    AnswerChoices = choices
                });
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