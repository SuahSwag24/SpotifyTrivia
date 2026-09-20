using System;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;

namespace SpotifyTrivia.Services
{
    public interface ISpotifyService
    {
        Task<List<PlaylistModel>> GetUserPlaylistsAsync(string accessToken);
        Task<SpotifyApiResult<List<TrackModel>>> GetPlaylistTracksAsync(string accessToken, string? refreshToken, string playlistId);
        Task<SpotifyApiResult<List<TrackModel>>> GetLikedSongsAsync(string accessToken, string? refreshToken);
        Task<SpotifyApiResult<List<TrackModel>>> GetRecentlyPlayedSongsAsync(string accessToken, string? refreshToken);
        Task<SpotifyApiResult<UserProfileModel>> GetUserProfileAsync(string accessToken, string? refreshToken);
        Task<SpotifyApiResult<string?>> GetSpotifyUserIdAsync(string accessToken, string? refreshToken);
        Task<string?> RefreshAccessTokenAsync(string refreshToken);
    }
}
