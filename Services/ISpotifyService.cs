using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;

namespace SpotifyTrivia.Services
{
    public interface ISpotifyService
    {
        Task<List<PlaylistModel>> GetUserPlaylistsAsync(string accessToken);
        Task<List<TrackModel>> GetPlaylistTracksAsync(string accessToken, string playlistId);
        Task<List<TrackModel>> GetLikedSongsAsync(string accessToken);
        Task<List<TrackModel>> GetRecentlyPlayedSongsAsync(string accessToken);
        Task<UserProfileModel> GetUserProfileAsync(string accessToken);
        Task<string?> GetSpotifyUserIdAsync(string accessToken);
    }
}
