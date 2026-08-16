using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Models.Multiplayer
{
    public class AnswerResult
    {
        public bool Success { get; set; }
        public bool WasCorrect { get; set; }
        public int SubmittedIndex { get; set; }
        public int CorrectIndex { get; set; }
    }
}