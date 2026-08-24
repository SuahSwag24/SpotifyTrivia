using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Services
{
    public interface ILobbyManager
    {
        LobbyModel CreateLobby(string hostPlayerId, string hostPlayerName, string hostAccessToken);
        LobbyModel? GetLobby(string code);
        bool TryAddPlayer(string code, string playerId, string displayName, string connectionId, out PlayerModel? player, out bool isNewPlayer);
        void RemovePlayer(string code, string playerId);
        void MarkPlayerConnection(string code, string playerId, bool isConnected, string connectionId);
        Task StartSessionAsync(string code, List<TrackModel> tracks, int questionCount);
        Task<AnswerResultModel> RecordPlayerAnswerAsync(string code, string playerId, int choiceIndex);
        (string lobbyCode, string playerId)? GetConnectionMapping(string connectionId);
        void RemoveConnectionMapping(string connectionId);
        void DisbandLobby(string code);
    }
}
