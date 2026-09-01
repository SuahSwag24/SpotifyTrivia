using System;
using System.Collections.Generic;
using System.Runtime;
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
        private readonly LobbySettingsModel _settings;

        public LobbyHub(ILobbyManager lobbyManager, ISpotifyService spotifyService, IBroadcaster broadcaster, LobbySettingsModel settings)
        {
            _lobbyManager = lobbyManager;
            _spotifyService = spotifyService;
            _broadcaster = broadcaster;
            _settings = settings;
        }

        public async Task JoinLobby(string lobbyCode, string playerId, string displayName)
        {
            bool joined = _lobbyManager.TryAddPlayer(lobbyCode, playerId, displayName, Context.ConnectionId, out var player, out bool isNewPlayer);
            if (!joined || player == null) return;

            await Groups.AddToGroupAsync(Context.ConnectionId, lobbyCode);

            if (player.JoinStatus == PlayerJoinStatus.PendingJoin)
            {
                await Clients.Caller.SendAsync("JoinStatus", new { status = "pendingJoin" });
            }
            else
            {
                await Clients.Caller.SendAsync("JoinStatus", new { status = "active" });
            }

            if (isNewPlayer)
            {
                await _broadcaster.BroadcastPlayerJoined(lobbyCode, player);
            }
        }

        public async Task StartGame(string lobbyCode, int questionCount, int roundDurationSeconds)
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

            await _broadcaster.BroadcastPreparingGame(lobbyCode);

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

            try
            {
                await _lobbyManager.StartSessionAsync(lobbyCode, tracks, questionCount, roundDurationSeconds);
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = ex.Message });
            }
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
                lobby.Players.TryGetValue(playerId, out var player);
                var displayName = player?.DisplayName ?? "A player";

                _lobbyManager.RemovePlayer(lobbyCode, playerId);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, lobbyCode);
                await _broadcaster.BroadcastPlayerLeft(lobbyCode, playerId, displayName);
            }
        }

        public async Task SelectPlaylist(string lobbyCode, string playlistId, string playlistName)
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
            lobby.SelectedPlaylistName = playlistName;
            await Clients.Group(lobbyCode).SendAsync("PlaylistSelected", new { PlaylistId = playlistId, Name = playlistName });
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

        public async Task RequestGamePhase(string lobbyCode)
        {
            var lobby = _lobbyManager.GetLobby(lobbyCode);
            if (lobby == null) return;

            switch (lobby.State)
            {
                case LobbyState.Countdown:
                    await Clients.Caller.SendAsync("CountdownStarted", new
                    {
                        Seconds = _settings.CountdownSeconds,
                        StartedAtUtc = lobby.CountdownStartedAtUtc
                    });
                    break;

                case LobbyState.Question:
                    var question = lobby.Questions[lobby.CurrentQuestionIndex];
                    await Clients.Caller.SendAsync("RoundStarted", new
                    {
                        question.PreviewUrl,
                        question.AlbumCoverUrl,
                        question.AnswerChoices,
                        StartedAtUtc = lobby.RoundStartedAtUtc,
                        DurationSeconds = lobby.RoundDurationSeconds,
                        QuestionNumber = lobby.CurrentQuestionIndex + 1,
                        TotalQuestions = lobby.Questions.Count
                    });
                    break;
            }
        }

        public async Task ReturnToLobby(string lobbyCode)
        {
            var lobby = _lobbyManager.GetLobby(lobbyCode);
            if (lobby == null) return;

            if (!IsHost(lobby))
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Only the host can return to the lobby." });
                return;
            }

            if (lobby.State != LobbyState.Finished)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Game hasn't ended yet." });
                return;
            }

            _lobbyManager.ResetLobbyToWaiting(lobbyCode);
            await Clients.Group(lobbyCode).SendAsync("ReturnedToLobby");
        }

        public async Task SelectGameMode(string lobbyCode, GameModeType mode)
        {
            var lobby = _lobbyManager.GetLobby(lobbyCode);
            if (lobby == null) return;

            if (!IsHost(lobby))
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Only the host can select the gamemode." });
                return;
            }

            if (lobby.State != LobbyState.Waiting)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Game is has already started." });
                return;
            }

            lobby.GameMode = mode;
            await Clients.Group(lobbyCode).SendAsync("GameModeSelected", new { Mode = mode.ToString() });
        }

        private bool IsHost(LobbyModel lobby)
        {
            var mapping = _lobbyManager.GetConnectionMapping(Context.ConnectionId);
            return mapping != null && mapping.Value.playerId == lobby.PlayerHostId;
        }
    }
}
