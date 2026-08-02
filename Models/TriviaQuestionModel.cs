namespace SpotifyTrivia.Models
{
    public class TriviaQuestionModel
    {
        public string TargetTrackId { get; set; } = string.Empty;
        public string PreviewUrl { get; set; } = string.Empty;
        public string AlbumCoverUrl { get; set; } = string.Empty;
        public string CorrectAnswer { get; set; } = string.Empty;
        public List<string> AnswerChoices { get; set; } = new();
    }
}