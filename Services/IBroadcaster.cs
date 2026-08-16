using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services
{
    public interface IBroadcaster
    {
        Task BroadcastCountdownStart(string lobbyCode, int seconds);
        Task BroadcastRoundStarted(string lobbyCode, TriviaQuestionModel question, DateTime gameStartedAtUtc, int durationSeconds, int questionNumber, int totalQuestions);
        Task BroadcastRoundEnded(string lobbyCode, string correctAnswer, List<PlayerModel> players);
        Task BroadcastGameEnded(string lobbyCode, List<PlayerModel> leaderboardScores);
        Task BroadcastPlayerJoined(string lobbyCode, PlayerModel player);
        Task BroadcastPlayerLeft(string lobbyCode, string playerId);
        Task BroadcastLobbyDisbanded(string lobbyCode);
    }
}
