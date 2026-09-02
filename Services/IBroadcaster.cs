using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services
{
    public interface IBroadcaster
    {
        Task BroadcastPreparingGame(string lobbyCode);
        Task BroadcastCountdownStart(string lobbyCode, int seconds, DateTime startedAtUtc, string prompt);
        Task BroadcastRoundStarted(string lobbyCode, TriviaQuestionModel question, DateTime gameStartedAtUtc, int durationSeconds, int questionNumber, int totalQuestions);
        Task BroadcastRoundEnded(string lobbyCode, string correctAnswer, List<PlayerModel> players);
        Task BroadcastGameEnded(string lobbyCode, List<PlayerModel> leaderboardScores, List<object> songResult);
        Task BroadcastPlayerJoined(string lobbyCode, PlayerModel player);
        Task BroadcastPlayerLeft(string lobbyCode, string playerId, string displayName);
        Task BroadcastLobbyDisbanded(string lobbyCode);
        Task BroadcastPlayerJoining(string code, List<string> list);
        Task SendPromotedToActive(string connectionId);
    }
}
