using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Models
{
    public class DashboardViewModel
    {
        public UserProfileModel UserProfile { get; set; } = new();
        public int GamesPlayed { get; set; }
        public List<PlaylistModel> TopPlaylists { get; set; } = new();
    }
}