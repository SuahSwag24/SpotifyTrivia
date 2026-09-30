using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace SpotifyTrivia.Services.Stems
{
    public sealed class StemStore
    {
        private readonly ConcurrentDictionary<string, (Dictionary<string, byte[]> Stems, StemManifest Manifest)> _byJob = new();

        public void Put(string jobId, Dictionary<string, byte[]> stems, StemManifest manifest)
            => _byJob[jobId] = (stems, manifest);

        public bool TryGetStem(string jobId, string stem, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (!_byJob.TryGetValue(jobId, out var entry)) return false;
            if (!entry.Stems.TryGetValue(stem, out var b)) return false;
            bytes = b;
            return true;
        }

        public bool TryGetManifest(string jobId, out StemManifest manifest)
        {
            manifest = null!;
            if (!_byJob.TryGetValue(jobId, out var entry)) return false;
            manifest = entry.Manifest;
            return true;
        }

        public void Remove(string jobId) => _byJob.TryRemove(jobId, out _);
    }
}
