using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Text;

namespace SpotifyTrivia.Models.Multiplayer
{
    public class AnswerResultModel
    {
        public bool Success { get; set; }
        public bool WasCorrect { get; set; }
        public int SubmittedIndex { get; set; }
        public int CorrectIndex { get; set; }
        public string? CorrectAnswerText { get; set; }
        public int AwardedScore { get; set; }
        public bool WasSelfContributionPenalty { get; set; }
    }
}