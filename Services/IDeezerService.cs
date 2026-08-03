using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Services
{
    public interface IDeezerService
    {
        Task<string?> GetPreviewUrlAsync(string artist, string title);
    }
}
