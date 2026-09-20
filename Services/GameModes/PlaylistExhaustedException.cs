using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Services.GameModes
{
    public class PlaylistExhaustedException : Exception
    {
        public PlaylistExhaustedException(string message) : base(message) { }
    }
}
