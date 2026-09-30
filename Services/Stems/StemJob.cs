using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Services.Stems
{
    public sealed record StemJob(string jobId, string PreviewUrl, int StartSec, int DurationSec);
}
