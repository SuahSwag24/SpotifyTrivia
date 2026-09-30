using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Services
{
    public sealed record StemManifest(string[] Stems, string[] Silent, int DurationMs);

    public interface IStemSeparator
    {
        Task<(Dictionary<string, byte[]> Stems, StemManifest Manifest)> SeparateAsync(string previewUrl, int startSec, int durationSec, CancellationToken ct);
    }
}
