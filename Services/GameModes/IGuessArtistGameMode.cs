using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;

namespace SpotifyTrivia.Services.GameModes
{
    public interface IGuessArtistGameMode
    {
        Task<List<TriviaQuestionModel>> CreateTriviaQuestions(List<TrackModel> tracks, int numberOfQuestions = 10);
    }
}
