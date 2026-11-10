using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using SpotifyTrivia.Services;

namespace SpotifyTrivia.Controllers
{
    public class SingleplayerController : Controller
    {
        private readonly ISpotifyService _spotifyService;

        public SingleplayerController(ISpotifyService spotifyService)
        {
            _spotifyService = spotifyService;
        }

        [HttpGet("singleplayer")]
        public async Task<IActionResult> Index()
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");
            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Login", "Auth");
            }

            var playlists = await _spotifyService.GetUserPlaylistsAsync(token);
            return View(playlists);
        }
    }
}
