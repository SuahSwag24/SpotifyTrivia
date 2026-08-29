using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic;
using SpotifyTrivia.Services;
using SpotifyTrivia.Services.GameModes;

namespace SpotifyTrivia.Controllers
{           
    public class GameController : Controller
    {
        private readonly ISpotifyService _spotifyService;
        private readonly IGuessArtistGameMode _triviaEngine;

        public GameController(ISpotifyService spotifyService, IGuessArtistGameMode triviaEngine)
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
                    throw new InvalidOperationException($"Playlist does not have enough tracks to play ({count} found.)");
                }

                var questions = await _triviaEngine.CreateTriviaQuestions(tracks);

                ViewBag.SpotifyAccessToken = token;

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
