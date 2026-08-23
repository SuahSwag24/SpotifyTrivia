using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using SpotifyTrivia.Hubs;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services
{
    public class Broadcaster : IBroadcaster
    {
        private readonly IHubContext<LobbyHub> _hubContext;

        public Broadcaster (IHubContext<LobbyHub> hubContext)
        {
            _hubContext = hubContext;
        }

        public Task BroadcastCountdownStart(string lobbyCode, int seconds, DateTime startedAtUtc)
        {
            return _hubContext.Clients.Group(lobbyCode)
                .SendAsync("CountdownStarted", new { Seconds = seconds, StartedAtUtc = startedAtUtc });
        }

        public Task BroadcastRoundStarted(string lobbyCode, TriviaQuestionModel question, DateTime gameStartedAtUtc, int durationSeconds, int questionNumber, int totalQuestions)
        {
            var payload = new
            {
                question.PreviewUrl,
                question.AlbumCoverUrl,
                question.AnswerChoices,
                StartedAtUtc = gameStartedAtUtc,
                DurationSeconds = durationSeconds,
                QuestionNumber = questionNumber,
                TotalQuestions = totalQuestions
            };
            return _hubContext.Clients.Group(lobbyCode).SendAsync("RoundStarted", payload);
        }
        
        public Task BroadcastRoundEnded(string lobbyCode, string correctAnswer, List<PlayerModel> players)
        {
            var payload = new
            {
                CorrectAnswer = correctAnswer,
                Players = players.Select(p => new
                {
                    p.PlayerId,
                    p.DisplayName,
                    p.Score,
                    p.LastAnswerCorrect,
                })
            };

            return _hubContext.Clients.Group(lobbyCode).SendAsync("RoundEnded", payload);
        }

        public Task BroadcastGameEnded(string lobbyCode, List<PlayerModel> finalLeaderboard)
        {
            var payload = finalLeaderboard.Select(p => new
            {
                p.PlayerId,
                p.DisplayName,
                p.Score
            });

            return _hubContext.Clients.Group(lobbyCode).SendAsync("GameEnded", payload);
        }

        public Task BroadcastPlayerJoined(string lobbyCode, PlayerModel player)
        {
            var payload = new { player.PlayerId, player.DisplayName };
            return _hubContext.Clients.Group(lobbyCode).SendAsync("PlayerJoined", payload);
        }

        public Task BroadcastPlayerLeft(string lobbyCode, string playerId, string displayName)
        {
            return _hubContext.Clients.Group(lobbyCode).SendAsync("PlayerLeft", new { PlayerId = playerId, DisplayName = displayName });
        }

        public Task BroadcastLobbyDisbanded(string lobbyCode)
        {
            return _hubContext.Clients.Group(lobbyCode).SendAsync("LobbyDisbanded");
        }

        public Task BroadcastPlayerJoining(string lobbyCode, List<string> displayNames)
        {
            return _hubContext.Clients.Group(lobbyCode)
                .SendAsync("PlayerJoining", new { DisplayNames = displayNames });
        }

        public Task SendPromotedToActive(string connectionId)
        {
            return _hubContext.Clients.Client(connectionId).SendAsync("PromotedToActive");
        }
    }
}
