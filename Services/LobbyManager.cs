using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services
{
    public enum LobbySessionEndReason
    {
        CompletedNormally,
        Disbanded,
        Error
    }

    public class LobbyManager : ILobbyManager
    {
        private readonly ConcurrentDictionary<string, LobbyModel> _lobbies = new();
        private readonly ITriviaEngine _triviaEngine;
        private readonly IBroadcaster _lobbyBroadcaster;
        private readonly LobbySettingsModel _settings;
        private readonly ConcurrentDictionary<string, (string lobbyCode, string playerId)> _connectionMap = new();
        private readonly ILogger<LobbyManager> _logger;
        
        public LobbyManager(ITriviaEngine triviaEngine, IBroadcaster lobbyBroadcaster, ILogger<LobbyManager> logger)
        {
            _triviaEngine = triviaEngine;
            _lobbyBroadcaster = lobbyBroadcaster;
            _settings = new LobbySettingsModel();
            _logger = logger;
        }

        public LobbyModel CreateLobby(string hostPlayerId, string hostPlayerName, string hostAccessToken)
        {
            string code = GenerateLobbyCode();

            var lobby = new LobbyModel
            {
                Code = code,
                PlayerHostId = hostPlayerId,
                HostDisplayName = hostPlayerName,
                HostSpotifyAccessToken = hostAccessToken,
            };

            var host = new PlayerModel
            {
                PlayerId = hostPlayerId,
                DisplayName = hostPlayerName,
            };

            lobby.Players[hostPlayerId] = host;
            _lobbies[code] = lobby;

            return lobby;
        }

        public LobbyModel? GetLobby(string code) =>
            _lobbies.TryGetValue(code, out var lobby) ? lobby : null;

        public bool TryAddPlayer(string code, string playerId, string displayName, string connectionId, out PlayerModel? player)
        {
            player = null;
            if (!_lobbies.TryGetValue(code, out var lobby)) return false;

            bool isReturningPlayer = lobby.Players.ContainsKey(playerId);

            if (lobby.State != LobbyState.Waiting && !isReturningPlayer)
                return false;

            player = lobby.Players.GetOrAdd(playerId, _ => new PlayerModel
            {
                PlayerId = playerId,
                DisplayName = displayName
            });

            player.ConnectionId = connectionId;
            player.IsConnected = true;
            _connectionMap[connectionId] = (code, playerId);

            return true;
        }

        public void MarkPlayerConnection(string code, string playerId, bool isConnected, string connectionId)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return;
            if (!lobby.Players.TryGetValue(playerId, out var player)) return;

            player.IsConnected = isConnected;
            player.ConnectionId = isConnected ? connectionId : null;
        }

        public async Task<AnswerResultModel> RecordPlayerAnswerAsync(string code, string playerId, int choiceIndex)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return new AnswerResultModel { Success = false };

            await lobby.StateLock.WaitAsync();
            try
            {
                if (lobby.State != LobbyState.Question) return new AnswerResultModel { Success = false };
                if (!lobby.Players.TryGetValue(playerId, out var player)) return new AnswerResultModel { Success = false };
                if (player.HasAnsweredCurrentQuestion) return new AnswerResultModel { Success = false };

                var currentQuestion = lobby.Questions[lobby.CurrentQuestionIndex];
                int correctIndex = currentQuestion.AnswerChoices.IndexOf(currentQuestion.CorrectAnswer);
                bool isCorrect = choiceIndex == correctIndex;

                player.HasAnsweredCurrentQuestion = true;
                player.LastAnswerCorrect = isCorrect;
                player.LastAnswerSubmittedUtc = DateTime.UtcNow;

                if (isCorrect)
                {
                    player.Score += CalculateScore(lobby.RoundStartedAtUtc, player.LastAnswerSubmittedUtc.Value, _settings.RoundDurationSeconds);
                }

                return new AnswerResultModel
                {
                    Success = true,
                    WasCorrect = isCorrect,
                    SubmittedIndex = choiceIndex,
                    CorrectIndex = correctIndex
                };
            }
            finally
            {
                lobby.StateLock.Release();
            }
        }

        public void RemovePlayer(string code, string playerId)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return;

            lobby.Players.TryRemove(playerId, out _);

            var staleConnections = _connectionMap
                .Where(kvp => kvp.Value.lobbyCode == code && kvp.Value.playerId == playerId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connId in staleConnections)
            {
                _connectionMap.TryRemove(connId, out _);
            }
        }

        public async Task StartSessionAsync(string code, List<TrackModel> tracks, int questionCount)
        {
            if (!_lobbies.TryGetValue(code, out var lobby)) return;

            lobby.Questions = await _triviaEngine.CreateTriviaQuestions(tracks, questionCount);
            lobby.SessionLoopCts = new CancellationTokenSource();

            _ = RunSessionLoop(lobby, lobby.SessionLoopCts.Token);
        }

        public (string lobbyCode, string playerId)? GetConnectionMapping(string connectionId)
        {
            return _connectionMap.TryGetValue(connectionId, out var mapping) ? mapping : null;
        }

        public void RemoveConnectionMapping(string connectionId)
        {
            _connectionMap.TryRemove(connectionId, out _);
        }

        public void DisbandLobby(string code)
        {
            if (!_lobbies.TryRemove(code, out var lobby)) return;

            lobby.SessionLoopCts?.Cancel();

            var staleConnecctions = _connectionMap
                .Where(kvp => kvp.Value.lobbyCode == code)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var connId in staleConnecctions)
            {
                _connectionMap.TryRemove(connId, out _);
            }
        }

        private string GenerateLobbyCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            string code;
            do
            {
                code = new string(Enumerable.Range(0, 5)
                    .Select(_ => chars[Random.Shared.Next(chars.Length)])
                    .ToArray());
            } while (_lobbies.ContainsKey(code));

            return code;
        }

        private int CalculateScore(DateTime roundStartedAtUtc, DateTime playerAnsweredAtUtc, double roundDurationSeconds)
        {
            double elapsedSeconds = (playerAnsweredAtUtc -  roundStartedAtUtc).TotalSeconds;

            double score = 100 * (1 - elapsedSeconds / roundDurationSeconds);
            return Math.Clamp((int)Math.Round(score), 1, 100);
        }

        private async Task RunSessionLoop(LobbyModel lobby, CancellationToken ct)
        {
            LobbySessionEndReason endReason = LobbySessionEndReason.CompletedNormally;

            try
            {
                for (int i = 0; i < lobby.Questions.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    await lobby.StateLock.WaitAsync(ct);
                    try
                    {
                        lobby.CurrentQuestionIndex = i;
                        lobby.State = LobbyState.Countdown;
                        foreach (var p in lobby.Players.Values)
                        {
                            p.HasAnsweredCurrentQuestion = false;
                            p.LastAnswerCorrect = null;
                        }
                    }
                    finally { lobby.StateLock.Release(); }

                    await _lobbyBroadcaster.BroadcastCountdownStart(lobby.Code, _settings.CountdownSeconds);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.CountdownSeconds), ct);

                    var question = lobby.Questions[i];
                    await lobby.StateLock.WaitAsync(ct);
                    try
                    {
                        lobby.State = LobbyState.Question;
                        lobby.RoundStartedAtUtc = DateTime.UtcNow;
                    }
                    finally { lobby.StateLock.Release(); }

                    await _lobbyBroadcaster.BroadcastRoundStarted(lobby.Code, question, lobby.RoundStartedAtUtc, _settings.RoundDurationSeconds, questionNumber: i + 1, totalQuestions: lobby.Questions.Count);
                    await Task.Delay(TimeSpan.FromSeconds(_settings.RoundDurationSeconds), ct);

                    await lobby.StateLock.WaitAsync(ct);
                    try { lobby.State = LobbyState.Reveal; }
                    finally { lobby.StateLock.Release(); }

                    await _lobbyBroadcaster.BroadcastRoundEnded(lobby.Code, question.CorrectAnswer, lobby.Players.Values.ToList());
                    await Task.Delay(TimeSpan.FromSeconds(_settings.RevealSeconds), ct);
                }
            }
            catch (OperationCanceledException)
            {
                endReason = LobbySessionEndReason.Disbanded;
            }
            catch (Exception ex)
            {
                endReason = LobbySessionEndReason.Error;
                _logger.LogError(ex, "Lobby session loop failed unexpectedly for lobby {Code}", lobby.Code);
            }

            await HandleSessionEnd(lobby, endReason);
        }

        private async Task HandleSessionEnd(LobbyModel lobby, LobbySessionEndReason reason)
        {
            switch (reason)
            {
                case LobbySessionEndReason.CompletedNormally:
                    lobby.State = LobbyState.Finished;
                    var leaderboard = lobby.Players.Values
                        .OrderByDescending(p => p.Score)
                        .ToList();
                    await _lobbyBroadcaster.BroadcastGameEnded(lobby.Code, leaderboard);
                    break;

                case LobbySessionEndReason.Disbanded:
                    break;

                case LobbySessionEndReason.Error:
                    await _lobbyBroadcaster.BroadcastLobbyDisbanded(lobby.Code);
                    _lobbies.TryRemove(lobby.Code, out _);
                    break;
            }    
        }
    }
}
