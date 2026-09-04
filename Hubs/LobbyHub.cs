using System;
using System.Collections.Generic;
using System.Runtime;
using System.Text;
using Microsoft.AspNetCore.SignalR;
using SpotifyTrivia.Models;
using SpotifyTrivia.Models.Multiplayer;
using SpotifyTrivia.Services;
using SpotifyTrivia.Services.Dtos;
using SpotifyTrivia.Services.GameModes;

namespace SpotifyTrivia.Hubs
{
    public class LobbyHub : Hub
    {
        private readonly ILobbyManager _lobbyManager;
        private readonly ISpotifyService _spotifyService;
        private readonly IBroadcaster _broadcaster;
        private readonly LobbySettingsModel _settings;
        private readonly ILogger<LobbyHub> _logger;

        public LobbyHub(ILobbyManager lobbyManager, ISpotifyService spotifyService, IBroadcaster broadcaster, LobbySettingsModel settings, ILogger<LobbyHub> logger)
        {
            _lobbyManager = lobbyManager;
            _spotifyService = spotifyService;
            _broadcaster = broadcaster;
            _settings = settings;
            _logger = logger;
        }

        public async Task JoinLobby(string lobbyCode, string playerId, string displayName)
        {
            bool joined = _lobbyManager.TryAddPlayer(lobbyCode, playerId, displayName, Context.ConnectionId, out var player, out bool isNewPlayer);
            if (!joined || player == null) return;

            var accessToken = Context.GetHttpContext()?.Session.GetString("SpotifyAccessToken");
            if (!string.IsNullOrEmpty(accessToken))
            {
                player.SpotifyAccessToken = accessToken;

                if (string.IsNullOrEmpty(player.SpotifyUserId))
                {
                    player.SpotifyUserId = await _spotifyService.GetSpotifyUserIdAsync(accessToken);
                }
            }

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
                if (lobby.SelectedPlaylistId == "__liked_songs__" || lobby.SelectedPlaylistId == "__recent_songs__")
                {
                    var eligiblePlayers = lobby.Players.Values
                        .Where(p => !string.IsNullOrEmpty(p.SpotifyAccessToken))
                        .ToList();

                    var perPlayerResults = await Task.WhenAll(
                        eligiblePlayers.Select(async p =>
                        {
                            try
                            {
                                var playerTracks = lobby.SelectedPlaylistId == "__liked_songs__"
                                    ? await _spotifyService.GetLikedSongsAsync(p.SpotifyAccessToken!)
                                    : await _spotifyService.GetRecentlyPlayedSongsAsync(p.SpotifyAccessToken!);

                                foreach (var t in playerTracks)
                                {
                                    t.ContributedByPlayerIds.Add(p.PlayerId);
                                }

                                return playerTracks;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to load track source for player {PlayerId} in lobby {LobbyCode}", p.PlayerId, lobby.Code);
                                return new List<TrackModel>();
                            }
                        })
                    );

                    tracks = perPlayerResults
                        .SelectMany(t => t)
                        .GroupBy(t => t.Id)
                        .Select(g =>
                        {
                            var merged = g.First();
                            merged.ContributedByPlayerIds = g.SelectMany(t => t.ContributedByPlayerIds).Distinct().ToList();
                            return merged;
                        })
                        .ToList();
                }
                else
                {
                    tracks = await _spotifyService.GetPlaylistTracksAsync(lobby.HostSpotifyAccessToken, lobby.SelectedPlaylistId);

                    var spotifyIdToPlayerId = lobby.Players.Values
                        .Where(p => !string.IsNullOrEmpty(p.SpotifyUserId))
                        .ToDictionary(p => p.SpotifyUserId!, p => p.PlayerId);

                    foreach (var track in tracks)
                    {
                        if (!string.IsNullOrEmpty(track.AddedBySpotifyUserId) &&
                            spotifyIdToPlayerId.TryGetValue(track.AddedBySpotifyUserId, out var matchedPlayerId))
                        {
                            track.ContributedByPlayerIds.Add(matchedPlayerId);
                        }
                    }
                }
            }
            catch (Exception)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Couldn't load that playlist. Try another." });
                return;
            }

            string sourceLabel = lobby.SelectedPlaylistId switch
            {
                "__liked_songs__" => "liked songs",
                "__recent_songs__" => "recently played songs",
                _ => "playlist"
            };

            if (tracks == null || tracks.Count < 4)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = $"Not enough tracks found across players' {sourceLabel} to start a game." });
                return;
            }

            lobby.RoundDurationSeconds = roundDurationSeconds;
            lobby.NumberOfQuestions = questionCount;

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
            await Clients.Groups(lobbyCode).SendAsync("PlayerAnswered", new { playerId });
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
                        StartedAtUtc = lobby.CountdownStartedAtUtc,
                        Prompt = lobby.Questions[lobby.CurrentQuestionIndex].Prompt
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

        public async Task ContinueGame(string lobbyCode)
        {
            var lobby = _lobbyManager.GetLobby(lobbyCode);
            if (lobby == null) return;

            if (!IsHost(lobby))
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Only host can continue the game." });
                return;
            }

            if (lobby.State != LobbyState.Finished)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "The game has not ended yet." });
                return;
            }

            await _broadcaster.BroadcastPreparingGame(lobbyCode);

            List<TrackModel> tracks;
            try
            {
                if (lobby.SelectedPlaylistId == "__liked_songs__")
                {
                    tracks = await FetchLikedSongsForAllPlayers(lobby);
                }
                else if (lobby.SelectedPlaylistId == "__recent_songs__")
                {
                    tracks = await FetchRecentlyPlayedSongsForAllPlayers(lobby);
                }
                else
                {
                    tracks = await _spotifyService.GetPlaylistTracksAsync(lobby.HostSpotifyAccessToken, lobby.SelectedPlaylistId!);

                    var spotifyIdToPlayerId = lobby.Players.Values
                        .Where(p => !string.IsNullOrEmpty(p.SpotifyUserId))
                        .ToDictionary(p => p.SpotifyUserId!, p => p.PlayerId);

                    foreach (var track in tracks)
                    {
                        if (!string.IsNullOrEmpty(track.AddedBySpotifyUserId) &&
                            spotifyIdToPlayerId.TryGetValue(track.AddedBySpotifyUserId, out var matchedPlayerId))
                        {
                            track.ContributedByPlayerIds.Add(matchedPlayerId);
                        }
                    }
                }
            }
            catch (Exception)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "Couldn't reload playlist." });
                return;
            }

            try
            {
                await _lobbyManager.ContinueSessionAsync(lobbyCode, tracks);
            }
            catch (PlaylistExhaustedException)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = "No more unplayed tracks" });
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("ActionError", new { Message = ex.Message });
            }
        }

        private bool IsHost(LobbyModel lobby)
        {
            var mapping = _lobbyManager.GetConnectionMapping(Context.ConnectionId);
            return mapping != null && mapping.Value.playerId == lobby.PlayerHostId;
        }

        private async Task<List<TrackModel>> FetchLikedSongsForAllPlayers(LobbyModel lobby)
        {
            var eligiblePlayers = lobby.Players.Values
                .Where(p => !string.IsNullOrEmpty(p.SpotifyAccessToken))
                .ToList();

            var perPlayerResult = await Task.WhenAll(
                eligiblePlayers.Select(async p =>
                {
                    try
                    {
                        var playerTracks = await _spotifyService.GetLikedSongsAsync(p.SpotifyAccessToken!);

                        foreach (var t in playerTracks)
                        {
                            t.ContributedByPlayerIds.Add(p.PlayerId);
                        }

                        return playerTracks;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load track source for player {PlayerId} in Lobby {Lobby}", p.PlayerId, lobby.Code);
                        return new List<TrackModel>();
                    }
                })
            );

            List<TrackModel> tracks = perPlayerResult
                .SelectMany(t => t)
                .GroupBy(t => t.Id)
                .Select(g =>
                {
                    var merged = g.First();
                    merged.ContributedByPlayerIds = g.SelectMany(t => t.ContributedByPlayerIds).Distinct().ToList();
                    return merged;
                })
                .ToList();

            return tracks;
        }

        private async Task<List<TrackModel>> FetchRecentlyPlayedSongsForAllPlayers(LobbyModel lobby)
        {
            var eligiblePlayers = lobby.Players.Values
                .Where(p => !string.IsNullOrEmpty(p.SpotifyAccessToken))
                .ToList();

            var perPlayerResult = await Task.WhenAll(
                eligiblePlayers.Select(async p =>
                {
                    try
                    {
                        var playerTracks = await _spotifyService.GetRecentlyPlayedSongsAsync(p.SpotifyAccessToken!);

                        foreach (var t in playerTracks)
                        {
                            t.ContributedByPlayerIds.Add(p.PlayerId);
                        }

                        return playerTracks;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load track source for player {PlayerId} in Lobby {Lobby}", p.PlayerId, lobby.Code);
                        return new List<TrackModel>();
                    }
                })
            );

            List<TrackModel> tracks = perPlayerResult
                .SelectMany(t => t)
                .GroupBy(t => t.Id)
                .Select(g =>
                {
                    var merged = g.First();
                    merged.ContributedByPlayerIds = g.SelectMany(t => t.ContributedByPlayerIds).Distinct().ToList();
                    return merged;
                })
                .ToList();

            return tracks;
        }
    }
}
