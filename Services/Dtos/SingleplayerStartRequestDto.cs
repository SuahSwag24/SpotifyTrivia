using System;
using System.Collections.Generic;
using System.Text;

namespace SpotifyTrivia.Services.Dtos
{
    public class SingleplayerStartRequestDto
    {
        public string? PlaylistId { get; set; }
        public string? GameMode { get; set; }
        public int QuestionCount { get; set; } = 10;
        public int RoundDuration { get; set; } = 10;
        public string? SampleSize { get; set; } = "200";
        public string? BlurAlbum { get; set; } = "hide";
        public string? AnswerStyle { get; set; } = "MultipleChoice";
    }
}
