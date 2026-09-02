using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Models.Multiplayer
{
    public enum PlayerJoinStatus
    {
        Active,
        PendingJoin
    }

    public class PlayerModel
    {
        public string? SpotifyUserId { get; set; }
        public string PlayerId { get; set; } = String.Empty;
        public string DisplayName { get; set; } = String.Empty;
        public string? ConnectionId { get; set; }
        public int Score { get; set; } = 0;
        public bool HasAnsweredCurrentQuestion { get; set; } = false;
        public bool? LastAnswerCorrect { get; set; }
        public bool IsConnected { get; set; } = false;
        public DateTime? LastAnswerSubmittedUtc { get; set; }
        public PlayerJoinStatus JoinStatus { get; set; } = PlayerJoinStatus.Active;
        public List<AnswerResultModel> AnswerHistory { get; set; } = new();
        public string? SpotifyAccessToken { get; set; }
        public bool LastAnswerPenalized { get; set; }
    }
}
