using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;

namespace SpotifyTrivia.Services
{
    public interface ITriviaEngine
    {
        Task<List<TriviaQuestionModel>> CreateTriviaQuestions(List<TrackModel> tracks, int numberOfQuestions = 10);
    }
}
