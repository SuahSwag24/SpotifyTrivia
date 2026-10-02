using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading.Channels;
using SpotifyTrivia.Models;

namespace SpotifyTrivia.Services.Stems
{
    public sealed class StemPipeline : BackgroundService
    {
        private readonly Channel<StemJob> _jobs = Channel.CreateUnbounded<StemJob>();
        private readonly IStemSeparator _separator;
        private readonly StemStore _store;
        private readonly ConcurrentDictionary<string, TriviaQuestionModel> _pending;

        private readonly ILogger<StemPipeline> _logger;

        public StemPipeline(IStemSeparator separator, StemStore store, PendingStemQuestions pending, ILogger<StemPipeline> logger)
        {
            _separator = separator;
            _store = store;
            _pending = pending.Map;
            _logger = logger;
        }

        public void Enqueue(StemJob job) => _jobs.Writer.TryWrite(job);
        
        protected override async Task ExecuteAsync(CancellationToken ct)
        {
            var opts = new ParallelOptions { MaxDegreeOfParallelism = 3, CancellationToken = ct };
            await Parallel.ForEachAsync(_jobs.Reader.ReadAllAsync(ct), opts, async (job, token) =>
            {
                if (string.IsNullOrWhiteSpace(job.PreviewUrl) || !Uri.TryCreate(job.PreviewUrl, UriKind.Absolute, out _))
                {
                    _logger.LogWarning("Skipping stem job {JobId}: invalid previewUrl", job.JobId);
                    return;
                }

                var (stems, manifest) = await _separator.SeparateAsync(job.PreviewUrl, job.StartSec, job.DurationSec, token);
                _store.Put(job.JobId, stems, manifest);
                if (_pending.TryRemove(job.JobId, out var question))
                {
                    question.StemRevealOrder = manifest.Stems.Except(manifest.Silent).ToList();
                    question.StemDurationMs = manifest.DurationMs;
                }
            });
        }
    }
}
