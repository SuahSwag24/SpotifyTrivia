using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Models
{
    public class UserProfileModel
    {
        public string DisplayName { get; set; } = string.Empty;
        public string ProfileImageUrl { get; set; } = string.Empty;
        public string Product { get; set; } = string.Empty;
        public string SpotifyUrl { get; set; } = string.Empty;
    }
}
