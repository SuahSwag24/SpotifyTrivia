using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using SpotifyTrivia.Models.Multiplayer;
using SpotifyTrivia.Services;
using SpotifyTrivia.Services.GameModes;

namespace SpotifyTrivia.Controllers
{
    public class GameController : Controller
    {
        private readonly ISpotifyService _spotifyService;
        private readonly IGameModeFactory _gameModeFactory;

        public GameController(ISpotifyService spotifyService, IGameModeFactory gameModeFactory)
        {
            _spotifyService = spotifyService;
            _gameModeFactory = gameModeFactory;
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

                //  TODO: Implement mode selection for single player
                var gameMode = _gameModeFactory.GetGameMode(GameModeType.ClassicGuessSong);
                var questions = await gameMode.GenerateQuestionsAsync(tracks, numberOfQuestions: 10, new HashSet<string>());

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