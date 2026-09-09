using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using SpotifyTrivia.Models;
using SpotifyTrivia.Services.Dtos;

namespace SpotifyTrivia.Services
{
    public class SpotifyService : ISpotifyService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SpotifyService> _logger;
        private readonly IConfiguration _config;

        public SpotifyService(IHttpClientFactory httpClientFactory, ILogger<SpotifyService> logger, IConfiguration config)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
            _logger = logger;
            _config = config;
        }

        public async Task<List<PlaylistModel>> GetUserPlaylistsAsync(string accessToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync("https://api.spotify.com/v1/me/playlists");

            if (!response.IsSuccessStatusCode)
            {
                return new List<PlaylistModel>();
            }

            var json = await response.Content.ReadAsStringAsync();

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<SpotifyPlaylistsResponse>(json, jsonOptions);

            if (result?.Items == null)
            {
                return new List<PlaylistModel>();
            }

            return result.Items.Select(dto => new PlaylistModel
            {
                Id = dto.Id,
                Name = dto.Name,
                ImageUrl = dto.Images?.FirstOrDefault()?.Url
            }).ToList();
        }
        public async Task<SpotifyApiResult<List<TrackModel>>> GetPlaylistTracksAsync(string accessToken, string? refreshToken, string playlistId)
        {
            var client = _httpClientFactory.CreateClient();
            var (response, refreshedToken) = await SendWithRefreshAsync(
                client, $"https://api.spotify.com/v1/playlists/{playlistId}/items", accessToken, refreshToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Spotify API returned status code {response.StatusCode} when fetching tracks: {errorBody}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<SpotifyPlaylistTracksResponse>(json, jsonOptions);

            var tracks = result?.Items?
                .Where(i => i.Track != null)
                .Select(i => new TrackModel
                {
                    Id = i.Track!.Id,
                    Title = i.Track.Name,
                    Artist = string.Join(", ", i.Track.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                    AlbumCoverUrl = i.Track.Album?.Images?.FirstOrDefault()?.Url,
                    PreviewUrl = $"spotify:track:{i.Track.Id}",
                    SpotifyUrl = i.Track.ExternalUrls?.Spotify,
                    AddedBySpotifyUserId = i.AddedBy?.Id
                }).ToList() ?? new List<TrackModel>();

            return new SpotifyApiResult<List<TrackModel>> { Data = tracks, RefreshedAccessToken = refreshedToken };
        }

        public async Task<SpotifyApiResult<List<TrackModel>>> GetLikedSongsAsync(string accessToken, string? refreshToken)
        {
            var client = _httpClientFactory.CreateClient();
            var (response, refreshedToken) = await SendWithRefreshAsync(
                client, "https://api.spotify.com/v1/me/tracks?limit=50", accessToken, refreshToken);

            if (!response.IsSuccessStatusCode)
                return new SpotifyApiResult<List<TrackModel>> { Data = new List<TrackModel>(), RefreshedAccessToken = refreshedToken };

            var json = await response.Content.ReadAsStringAsync();
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<SpotifySavedTracksResponse>(json, jsonOptions);

            var tracks = result?.Items?
                .Where(i => i.Track != null)
                .Select(i => new TrackModel
                {
                    Id = i.Track!.Id,
                    Title = i.Track.Name,
                    Artist = string.Join(", ", i.Track.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                    AlbumCoverUrl = i.Track.Album?.Images?.FirstOrDefault()?.Url,
                    PreviewUrl = $"spotify:track:{i.Track.Id}",
                    SpotifyUrl = i.Track.ExternalUrls?.Spotify
                }).ToList() ?? new List<TrackModel>();

            return new SpotifyApiResult<List<TrackModel>> { Data = tracks, RefreshedAccessToken = refreshedToken };
        }

        public async Task<SpotifyApiResult<List<TrackModel>>> GetRecentlyPlayedSongsAsync(string accessToken, string? refreshToken)
        {
            var client = _httpClientFactory.CreateClient();
            var (response, refreshedToken) = await SendWithRefreshAsync(
                client, "https://api.spotify.com/v1/me/player/recently-played?limit=50", accessToken, refreshToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Spotify API returned status code {response.StatusCode} when fetching tracks: {errorBody}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<SpotifyRecentlyPlayedResponse>(json, jsonOptions);

            var tracks = result?.Items?
                .Where(i => i.Track != null)
                .Select(i => new TrackModel
                {
                    Id = i.Track!.Id,
                    Title = i.Track.Name,
                    Artist = string.Join(", ", i.Track.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                    AlbumCoverUrl = i.Track.Album?.Images?.FirstOrDefault()?.Url,
                    PreviewUrl = $"spotify:track:{i.Track.Id}",
                    SpotifyUrl = i.Track.ExternalUrls?.Spotify
                }).ToList() ?? new List<TrackModel>();

            return new SpotifyApiResult<List<TrackModel>> { Data = tracks, RefreshedAccessToken = refreshedToken };
        }

        public async Task<SpotifyApiResult<UserProfileModel>> GetUserProfileAsync(string accessToken, string? refreshToken)
        {
            var client = _httpClientFactory.CreateClient();
            var (response, refreshedToken) = await SendWithRefreshAsync(
                client, "https://api.spotify.com/v1/me", accessToken, refreshToken);

            if (!response.IsSuccessStatusCode)
            {
                return new SpotifyApiResult<UserProfileModel>
                {
                    Data = new UserProfileModel { DisplayName = "Spotify User" },
                    RefreshedAccessToken = refreshedToken
                };
            }

            var json = await response.Content.ReadAsStringAsync();
            var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var dto = JsonSerializer.Deserialize<SpotifyUserProfileDto>(json, jsonOptions);

            return new SpotifyApiResult<UserProfileModel>
            {
                Data = new UserProfileModel
                {
                    DisplayName = dto?.DisplayName ?? "Spotify User",
                    ProfileImageUrl = dto?.Images?.FirstOrDefault()?.Url ?? "https://via.placeholder.com/150?text=User",
                    Product = dto?.Product ?? "standard",
                    SpotifyUrl = dto?.ExternalUrls?.Spotify ?? "#"
                },
                RefreshedAccessToken = refreshedToken
            };
        }

        public async Task<SpotifyApiResult<string?>> GetSpotifyUserIdAsync(string accessToken, string? refreshToken)
        {
            var client = _httpClientFactory.CreateClient();
            var (response, refreshedToken) = await SendWithRefreshAsync(
                client, "https://api.spotify.com/v1/me", accessToken, refreshToken);

            if (!response.IsSuccessStatusCode)
                return new SpotifyApiResult<string?> { Data = null, RefreshedAccessToken = refreshedToken };

            var json = await response.Content.ReadAsStringAsync();
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var profile = JsonSerializer.Deserialize<SpotifyUserProfileDto>(json, jsonOptions);

            return new SpotifyApiResult<string?> { Data = profile?.Id, RefreshedAccessToken = refreshedToken };
        }

        public async Task<string?> RefreshAccessTokenAsync(string refreshToken)
        {
            var client = _httpClientFactory.CreateClient();
            var body = new Dictionary<string, string>
            {
                { "grant_type", "refresh_token" },
                { "refresh_token", refreshToken },
                { "client_id", _config["Spotify:ClientId"]! },
                { "client_secret", _config["Spotify:ClientSecret"]! }
            };

            var response = await client.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(body));
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to refresh Spotify access token: {Status} - {Error}", response.StatusCode, error);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("access_token").GetString();
        }

        private async Task<(HttpResponseMessage Response, string? RefreshedAccessToken)> SendWithRefreshAsync(HttpClient client, string url, string accessToken, string? refreshToken)
        {
            async Task<HttpResponseMessage> DoRequest(string token)
            {
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                return await client.GetAsync(url);
            }

            var response = await DoRequest(accessToken);
            string? refreshedToken = null;

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogInformation("Acess token expired for {Url}, attempting refresh...", url);
                var newToken = await RefreshAccessTokenAsync(refreshToken);
                if (!string.IsNullOrEmpty(newToken))
                {
                    refreshedToken = newToken;
                    response = await DoRequest(newToken);
                }
            }

            return (response, refreshedToken);
        }
    }
}
