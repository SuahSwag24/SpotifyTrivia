using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using SpotifyTrivia.Services.Dtos;

namespace SpotifyTrivia.Services
{
    public class DeezerService : IDeezerService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public DeezerService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<string?> GetPreviewUrlAsync(string artist, string title)
        {
            var client = _httpClientFactory.CreateClient();

            var query = $"artist:\"{artist}\" track:\"{title}\"";
            var url = $"https://api.deezer.com/search?q={Uri.EscapeDataString(query)}";

            var response = await client.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return null; // No preview available
            }

            var json = await response.Content.ReadAsStringAsync();
            var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var result = JsonSerializer.Deserialize<DeezerSearchResponse>(json, jsonOptions);

            var firstMatch = result?.Data?.FirstOrDefault();
            return firstMatch?.Preview;
        }
    }
}
