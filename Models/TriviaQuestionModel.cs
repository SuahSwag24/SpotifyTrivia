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
    }
}