using SpotifyTrivia.Models.Multiplayer;

namespace SpotifyTrivia.Models
{
    public class TriviaQuestionModel
    {
        public string TargetTrackId { get; set; } = string.Empty;
        public string PreviewUrl { get; set; } = string.Empty;
        public string AlbumCoverUrl { get; set; } = string.Empty;
        public string SongTitle { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string CorrectAnswer { get; set; } = string.Empty;
        public string Prompt { get; set; } = string.Empty;
        public List<string> AnswerChoices { get; set; } = new();
        public string SpotifyUrl { get; set; } = string.Empty;
        public List<string> ContributedByPlayerIds { get; set; } = new();
        public List<string> StemUrls { get; set; } = new();
        public List<string> StemRevealOrder { get; set; } = new();
        public int StemDurationMs { get; set; }
        public string Isrc { get; set; } = string.Empty;
        public long DeezerTrackId { get; set; }
        public long DeezerArtistId { get; set; }
        public string DeezerTitle { get; set; } = string.Empty;
        public List<string> ChoicesFor(AnswerStyle style) => style == AnswerStyle.Search ? new List<string>() : AnswerChoices;
    }
}