using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using SpotifyTrivia.Services;

namespace SpotifyTrivia.Hubs
{
    public class LobbyHub : Hub
    {
        private readonly ILobbyManager _lobbyManager;
        private readonly ISpotifyService _spotifyService;
        private readonly IBroadcaster _broadcaster;

        public LobbyHub(ILobbyManager lobbyManager, ISpotifyService spotifyService, IBroadcaster broadcaster)
        {
            _lobbyManager = lobbyManager;
            _spotifyService = spotifyService;
            _broadcaster = broadcaster;
        }

        public async Task JoinLobby(string lobbyCode, string playerId, string displayName)
        {
            bool joined = _lobbyManager.TryAddPlayer(lobbyCode, playerId, displayName, Context.ConnectionId, out var player);
            if (!joined || player == null) return;

            await Groups.AddToGroupAsync(Context.ConnectionId, lobbyCode);
            await _broadcaster.BroadcastPlayerJoined(lobbyCode, player);
        }

        public async Task StartGame(string lobbyCode, string playlistId)
        {
            var lobby = _lobbyManager.GetLobby(lobbyCode);
            if (lobby == null) return;

            var mapping = _lobbyManager.GetConnectionMapping(Context.ConnectionId);
            if (mapping == null || mapping.Value.playerId != lobby.PlayerHostId)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Only the host can start the game." });
                return;
            }

            var tracks = await _spotifyService.GetPlaylistTracksAsync(lobby.HostSpotifyAccessToken, playlistId);
            await _lobbyManager.StartSessionAsync(lobbyCode, tracks);
        }

        public async Task SubmitAnswer(string lobbyCode, string playerId, int answerIndex)
        {
            await _lobbyManager.RecordPlayerAnswerAsync(lobbyCode, playerId, answerIndex);
        }

        public async Task LeaveLobby(string lobbyCode, string playerId)
        {
            var lobby = _lobbyManager.GetLobby(lobbyCode);
            if (lobby == null) return;

            if (lobby.PlayerHostId == playerId)
            {
                await _broadcaster.BroadcastLobbyDisbanded(lobbyCode);
                _lobbyManager.DisbandLobby(lobbyCode);
            }
            else
            {
                _lobbyManager.RemovePlayer(lobbyCode, playerId);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, lobbyCode);
                await _broadcaster.BroadcastPlayerLeft(lobbyCode, playerId);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var mapping = _lobbyManager.GetConnectionMapping(Context.ConnectionId);
            if (mapping != null)
            {
                var (lobbyCode, playerId) = mapping.Value;
                var lobby = _lobbyManager.GetLobby(lobbyCode);

                if (lobby != null && lobby.PlayerHostId == playerId)
                {
                    await _broadcaster.BroadcastLobbyDisbanded(lobbyCode);
                    _lobbyManager.DisbandLobby(lobbyCode);
                }
                else
                {
                    _lobbyManager.MarkPlayerConnection(lobbyCode, playerId, isConnected: false, Context.ConnectionId);
                    _lobbyManager.RemoveConnectionMapping(Context.ConnectionId);
                    await Clients.Group(lobbyCode).SendAsync("PlayerDisconnected", new { PlayerId = playerId });
                }
            }

            await base.OnDisconnectedAsync(exception);
        }
    }
}
