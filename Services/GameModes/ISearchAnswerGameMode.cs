using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services.GameModes
{
    public interface ISearchAnswerGameMode : IGameMode
    {
        AnswerResultModel EvaluateSearchAnswer(
            TriviaQuestionModel question,
            SearchAnswerModel answer,
            DateTime roundStartedAtUtc,
            DateTime answeredAtUtc,
            double roundDurationSeconds,
            string playerId
        );
    }
}
