using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Models.Multiplayer
{
    public class SearchAnswerModel
    {
        public long DeezerTrackId { get; set; }
        public long DeezerArtistId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ArtistName { get; set; } = string.Empty;
        public string? Isrc { get; set; }
    }
}
