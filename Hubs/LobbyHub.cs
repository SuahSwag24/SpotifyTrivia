using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;
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

        public async Task StartGame(string lobbyCode)
        {
            var lobby = _lobbyManager.GetLobby(lobbyCode);
            if (lobby == null) return;

            if (!IsHost(lobby))
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Only the host can start the game." });
                return;
            }

            if (lobby.State != LobbyState.Waiting)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Game already started." });
                return;
            }

            if (string.IsNullOrEmpty(lobby.SelectedPlaylistId))
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Select a playlist before starting." });
                return;
            }

            List<TrackModel> tracks;
            try
            {
                tracks = await _spotifyService.GetPlaylistTracksAsync(lobby.HostSpotifyAccessToken, lobby.SelectedPlaylistId);
            }
            catch (Exception)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Couldn't load that playlist. Try another." });
                return;
            }

            if (tracks == null || tracks.Count < 4)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Playlist doesn't have enough tracks to play." });
                return;
            }

            await _lobbyManager.StartSessionAsync(lobbyCode, tracks);
        }

        public async Task SubmitAnswer(string lobbyCode, string playerId, int answerIndex)
        {
            var result = await _lobbyManager.RecordPlayerAnswerAsync(lobbyCode, playerId, answerIndex);
            if (!result.Success) return;
            await Clients.Caller.SendAsync("AnswerResult", result);
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

        public async Task SelectPlaylist(string lobbyCode, string playlistId)
        {
            var lobby = _lobbyManager.GetLobby(lobbyCode);
            if (lobby == null) return;

            if (!IsHost(lobby))
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Only the host can select a playlist." });
                return;
            }

            if (lobby.State != LobbyState.Waiting)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Game already started." });
                return;
            }

            lobby.SelectedPlaylistId = playlistId;
            await Clients.Group(lobbyCode).SendAsync("PlaylistSelected", new { PlaylistId = playlistId });
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
                    _lobbyManager.MarkPlayerConnection(lobbyCode, playerId, isConnected: false, Context.ConnectionId);
                    _lobbyManager.RemoveConnectionMapping(Context.ConnectionId);

                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5));

                        var stillLobby = _lobbyManager.GetLobby(lobbyCode);
                        if (stillLobby == null) return; // already cleaned up

                        if (stillLobby.Players.TryGetValue(playerId, out var hostPlayer) && !hostPlayer.IsConnected)
                        {
                            await _broadcaster.BroadcastLobbyDisbanded(lobbyCode);
                            _lobbyManager.DisbandLobby(lobbyCode);
                        }
                    });
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

        private bool IsHost(LobbyModel lobby)
        {
            var mapping = _lobbyManager.GetConnectionMapping(Context.ConnectionId);
            return mapping != null && mapping.Value.playerId == lobby.PlayerHostId;
        }
    }
}
