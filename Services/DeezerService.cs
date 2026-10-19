using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using SpotifyTrivia.Services.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace SpotifyTrivia.Services
{
    public class DeezerService : IDeezerService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IMemoryCache _cache;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions() { PropertyNameCaseInsensitive = true };

        public DeezerService(IHttpClientFactory httpClientFactory, IMemoryCache cache)
        {
            _httpClientFactory = httpClientFactory;
            _cache = cache;
        }

        public async Task<string?> GetPreviewUrlAsync(string isrc)
        {
            var track = await GetTrackByIsrcAsync(isrc);
            return track?.Preview;
        }

        public async Task<DeezerTrackResponse?> GetTrackByIsrcAsync(string isrc)
        {
            var result = await GetJsonAsync<DeezerTrackResponse>($"https://api.deezer.com/track/isrc:{Uri.EscapeDataString(isrc)}");
            return result is { Id: not 0 } ? result : null;
        }

        public async Task<DeezerTrackResponse?> GetTrackByIdAsync(long trackId)
        {
            var result = await GetJsonAsync<DeezerTrackResponse>($"https://api.deezer.com/track/{trackId}");
            return result is { Id: not 0 } ? result : null;
        }

        public Task<List<SearchArtistResult>?> SearchArtistsAsync(string query) =>
            GetCachedAsync($"artists:{query.Trim().ToLowerInvariant()}", async () =>
            {
                var r = await GetJsonAsync<DeezerArtistListResponse>(
                    $"https://api.deezer.com/search/artist?q={Uri.EscapeDataString(query.Trim())}&limit=8");
                return r?.Data?.Select(a => new SearchArtistResult(a.Id, a.Name ?? "", a.PictureMedium)).ToList();
            });

        public Task<List<SearchTrackResult>?> SearchTracksAsync(string query) =>
            GetCachedAsync($"tracks:{query.Trim().ToLowerInvariant()}", async () =>
            {
                var r = await GetJsonAsync<DeezerSearchResponse>(
                    $"https://api.deezer.com/search/track?q={Uri.EscapeDataString(query.Trim())}&limit=10");
                return r?.Data?.Select(ToTrackResult).ToList();
            });

        public Task<List<SearchTrackResult>?> GetArtistTopTracksAsync(long artistId) =>
            GetCachedAsync($"top:{artistId}", async () =>
            {
                var r = await GetJsonAsync<DeezerSearchResponse>(
                    $"https://api.deezer.com/artist/{artistId}/top?limit=50");
                return r?.Data?.Select(ToTrackResult).ToList();
            });

        private async Task<T?> GetJsonAsync<T>(string url) where T : class
        {
            var client = _httpClientFactory.CreateClient();
            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync();
            try
            {
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private async Task<List<T>?> GetCachedAsync<T>(string key, Func<Task<List<T>?>> fetch)
        {
            if (_cache.TryGetValue(key, out List<T>? cached)) return cached;
            var result = await fetch();
            if (result != null) _cache.Set(key, result, TimeSpan.FromMinutes(10));
            return result;
        }

        private static SearchTrackResult ToTrackResult(DeezerTrackDto t) =>
            new(t.Id, t.Title ?? "", t.Artist?.Id ?? 0, t.Artist?.Name ?? "", t.Album?.CoverMedium);
    }
}
