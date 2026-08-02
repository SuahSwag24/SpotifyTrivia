using Microsoft.AspNetCore.Mvc;
using SpotifyTrivia.Services;
using SpotifyTrivia.Models;

namespace SpotifyTrivia.Controllers
{
    public class DashboardController : Controller
    {
        private readonly ISpotifyService _spotifyService;

        public DashboardController(ISpotifyService spotifyService)
        {
            _spotifyService = spotifyService;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> Index()
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");
            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Login", "Auth");
            }

            var profile = await _spotifyService.GetUserProfileAsync(token);

            var playlists = await _spotifyService.GetUserPlaylistsAsync(token);

            var viewModel = new DashboardViewModel
            {
                UserProfile = profile,
                GamesPlayed = HttpContext.Session.GetInt32("GamesPlayed") ?? 0,
            };

            return View(viewModel);
        }
    }
}