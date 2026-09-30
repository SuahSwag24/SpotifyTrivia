using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using SpotifyTrivia.Models;

namespace SpotifyTrivia.Services.Stems
{
    public sealed class PendingStemQuestions
    {
        public ConcurrentDictionary<string, TriviaQuestionModel> Map { get; } = new();
    }
}
