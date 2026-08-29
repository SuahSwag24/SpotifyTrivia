using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Models.Multiplayer
{
    public enum LobbyState { Waiting, Countdown, Question, Reveal, Finished}

    public class LobbyModel
    {
        public string Code { get; set; } = string.Empty;
        public string PlayerHostId { get; set; } = string.Empty;
        public string HostDisplayName { get; set; } = string.Empty;
        public LobbyState State { get; set; } = LobbyState.Waiting;
        public List<TriviaQuestionModel> Questions { get; set; } = new();
        public int CurrentQuestionIndex { get; set; } = -1;
        public ConcurrentDictionary<string, PlayerModel> Players { get; set; } = new();
        public SemaphoreSlim StateLock { get; set; } = new(1, 1);
        public CancellationTokenSource? SessionLoopCts { get; set; }
        public DateTime RoundStartedAtUtc { get; set; }
        public DateTime CountdownStartedAtUtc { get; set; }
        public string HostSpotifyAccessToken { get; set; } = string.Empty;
        public string? SelectedPlaylistId { get; set; }
        public string? SelectedPlaylistName { get; set; }
    }
}
