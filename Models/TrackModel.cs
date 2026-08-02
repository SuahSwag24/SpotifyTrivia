namespace SpotifyTrivia.Models
{
    public class TrackModel
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string? AlbumCoverUrl { get; set; }
        public string? PreviewUrl { get; set; }
    }
}