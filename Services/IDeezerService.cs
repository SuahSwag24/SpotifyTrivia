using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Services.Dtos;

namespace SpotifyTrivia.Services
{
    public interface IDeezerService
    {
        Task<string?> GetPreviewUrlAsync(string isrc);
        Task<DeezerTrackResponse?> GetTrackByIsrcAsync(string isrc);
        Task<DeezerTrackResponse?> GetTrackByIdAsync(long trackId);
        Task<List<SearchArtistResult>?> SearchArtistsAsync(string query);
        Task<List<SearchTrackResult>?> SearchTracksAsync(string query);
        Task<List<SearchTrackResult>?> GetArtistTopTracksAsync(long artistId);
    }
}
