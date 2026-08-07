using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Models.Multiplayer
{
    public class LobbySettingsModel
    {
        public int NumberOfQuestions { get; set; } = 10;
        public int CountdownSeconds { get; set; } = 3;
        public int RoundDurationSeconds { get; set; } = 20;
        public int RevealSeconds { get; set; } = 3;
    }
}
