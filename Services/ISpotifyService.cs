using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Services
{
    public interface ISpotifyService
    {
        Task<List<PlaylistModel>> GetUserPlaylistsAsync(string accessToken);
        Task<List<TrackModel>> GetPlaylistTracksAsync(string accessToken, string playlistId);
    }
}
