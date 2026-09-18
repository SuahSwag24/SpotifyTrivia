using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using SpotifyTrivia.Models;
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
            var accessToken = HttpContext.Session.GetString("SpotifyAccessToken");
            var refreshToken = HttpContext.Session.GetString("SpotifyRefreshToken");

            if (string.IsNullOrEmpty(accessToken))
            {
                return RedirectToAction("Login", "Auth");
            }

            try
            {
                var result = await _spotifyService.GetPlaylistTracksAsync(accessToken, null, playlistId);
                var tracks = result.Data ?? new List<TrackModel>();

                if (tracks == null || tracks.Count < 4)
                {
                    int count = tracks?.Count ?? 0;
                    throw new InvalidOperationException($"Playlist does not have enough tracks to play ({count} found.)");
                }

                //  TODO: Implement mode selection for single player
                var gameMode = _gameModeFactory.GetGameMode(GameModeType.ClassicGuessSong);
                var questions = await gameMode.GenerateQuestionsAsync(tracks, numberOfQuestions: 10, new HashSet<string>());

                ViewBag.SpotifyAccessToken = accessToken;

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