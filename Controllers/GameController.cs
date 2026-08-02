using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic;
using SpotifyTrivia.Services;

namespace SpotifyTrivia.Controllers
{
    public class GameController : Controller
    {
        private readonly ISpotifyService _spotifyService;
        private readonly ITriviaEngine _triviaEngine;

        public GameController(ISpotifyService spotifyService, ITriviaEngine triviaEngine)
        {
            _spotifyService = spotifyService;
            _triviaEngine = triviaEngine;
        }

        [HttpGet("game/play/{playlistId}")]
        public async Task<IActionResult> Play(string playlistId)
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");
            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Login", "Auth");
            }

            try
            {
                var tracks = await _spotifyService.GetPlaylistTracksAsync(token, playlistId);

                if (tracks == null || tracks.Count < 4)
                {
                    int count = tracks?.Count ?? 0;
                    throw new InvalidOperationException($"Playlist does not have enough tracks with audio previews ({count} found.)");
                }

                var questions = await _triviaEngine.CreateTriviaQuestions(tracks);

                return View(questions);
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Index", "Playlist");
            }
        }
    }
}
