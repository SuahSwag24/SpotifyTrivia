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

        public SpotifyService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
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
        public async Task<List<TrackModel>> GetPlaylistTracksAsync(string accessToken, string playlistId)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync($"https://api.spotify.com/v1/playlists/{playlistId}/tracks");

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Spotify API return status code {response.StatusCode} when fetching tracks");
            }

            var json = await response.Content.ReadAsStringAsync();

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var result = JsonSerializer.Deserialize<SpotifyPlaylistTracksResponse>(json, jsonOptions);

            return result?.Items?
                .Where(i => i.Track != null && !string.IsNullOrEmpty(i.Track.PreviewUrl))
                .Select(i => new TrackModel
                {
                    Id = i.Track!.Id,
                    Title = i.Track.Name,
                    Artist = string.Join(", ", i.Track.Artists?.Select(a => a.Name) ?? Array.Empty<string>()),
                    AlbumCoverUrl = i.Track.Album?.Images?.FirstOrDefault()?.Url,
                    PreviewUrl = i.Track.PreviewUrl
                }).ToList() ?? new List<TrackModel>();
        }

        public async Task<UserProfileModel> GetUserProfileAsync(string accessToken)
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            var response = await client.GetAsync("https://api.spotify.com/v1/me");

            if (!response.IsSuccessStatusCode)
            {
                return new UserProfileModel { DisplayName = "Spotify User" };
            }

            var json = await response.Content.ReadAsStringAsync();
            var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var dto = JsonSerializer.Deserialize<SpotifyUserProfileDto>(json, jsonOptions);

            return new UserProfileModel
            {
                DisplayName = dto?.DisplayName ?? "Spotify User",
                ProfileImageUrl = dto?.Images?.FirstOrDefault()?.Url ?? "https://via.placeholder.com/150?text=User",
                Product = dto?.Product ?? "standard",
                SpotifyUrl = dto?.ExternalUrls?.Spotify ?? "#"
            };
        }
    }
}
