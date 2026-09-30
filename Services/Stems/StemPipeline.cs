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
            await foreach (var job in _jobs.Reader.ReadAllAsync(ct))
            {
                try
                {
                    var (stems, manifest) = await _separator.SeparateAsync(job.PreviewUrl, job.StartSec, job.DurationSec, ct);
                    _store.Put(job.jobId, stems, manifest);

                    if (_pending.TryRemove(job.jobId, out var question))
                    {
                        question.StemRevealOrder = manifest.Stems.Except(manifest.Silent).ToList();
                        question.StemDurationMs = manifest.DurationMs;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Stem separation failed for job {jobId}", job.jobId);
                    _pending.TryRemove(job.jobId, out _);
                }
            }
        }
    }
}
