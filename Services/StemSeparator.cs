using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpotifyTrivia.Services
{
    public class HttpStemSeparator : IStemSeparator
    {
        private readonly HttpClient _http;
        private readonly string _apiKey;

        public HttpStemSeparator(HttpClient http, IConfiguration config)
        {
            _http = http;
            _apiKey = config["StemWorker:ApiKey"] ?? throw new InvalidOperationException("StemWorker: ApiKey is missing");
        }

        public async Task<(Dictionary<string, byte[]> Stems, StemManifest Manifest)> SeparateAsync(string previewUrl, int startSec, int durationSec, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/separate");
            request.Headers.Add("X-Api-Key", _apiKey);
            request.Content = JsonContent.Create(new { previewUrl, startSec, durationSec });

            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            await using var zipStream = await response.Content.ReadAsStreamAsync(ct);
            using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            var stems = new Dictionary<string, byte[]>();
            StemManifest? manifest = null;

            foreach (var entry in archive.Entries)
            {
                await using var entryStream = entry.Open();
                using var ms = new MemoryStream();
                await entryStream.CopyToAsync(ms, ct);

                if (entry.Name ==  "manifest.json")
                {
                    manifest = JsonSerializer.Deserialize<StemManifest>(ms.ToArray(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
                else
                {
                    var stemName = Path.GetFileNameWithoutExtension(entry.Name);
                    stems[stemName] = ms.ToArray();
                }
            }

            if (manifest is null)
                throw new InvalidOperationException("Worker response missing manifest.json");

            return (stems, manifest);
        }
    }
}
