using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services.Singleplayer
{
    public interface ISingleplayerService
    {
        Task<SpResult<SingleplayerStartResult>> StartAsync(SingleplayerStartParams p);
        SpResult<object> Prepare(string sessionId, string playerId, int round);
        SpResult<object> Begin(string sessionId, string playerId, int round);
        SpResult<AnswerResultModel> Answer(string sessionId, string playerId, int round, int choiceIndex, SearchAnswerModel? searchAnswer);
        SpResult<object> End(string sessionId, string playerId, int round);
        SpResult<object> Summary(string sessionId, string playerId);
    }
}
