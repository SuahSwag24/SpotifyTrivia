using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Models.Singleplayer
{
    public enum SingleplayerRoundState { Idle, Active, Ended }

    public class SingleplayerSessionModel
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string PlayerId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public GameModeType GameMode { get; init; }
        public AnswerStyle AnswerStyle { get; init; }
        public AlbumCoverVisibility BlurAlbum { get; init; }
        public int RoundDurationSeconds { get; init; }
        public int QuestionCount { get; init; }
        public List<TrackModel> CachedTracks { get; init; } = new();
        public List<TriviaQuestionModel> Questions { get; set; } = new();
        public HashSet<string> PlayedTrackIds { get; } = new();
        public List<AnswerResultModel> History { get; } = new();
        public int Score { get; set; }
        public int StartingScore { get; set; }
        public int CurrentIndex { get; set; } = -1;
        public SingleplayerRoundState RoundState { get; set; } = SingleplayerRoundState.Idle;
        public bool AnsweredCurrent { get; set; }
        public bool IsContinuing { get; set; }
        public DateTime RoundStartedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; } = DateTime.UtcNow;
        public object Sync { get; } = new();
    }
}
