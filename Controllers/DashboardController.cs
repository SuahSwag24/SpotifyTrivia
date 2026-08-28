using Microsoft.AspNetCore.Mvc;
using SpotifyTrivia.Services;
using SpotifyTrivia.Models;
using System.Data;

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

            if (string.IsNullOrEmpty(HttpContext.Session.GetString("DisplayName")))
            {
                HttpContext.Session.SetString("DisplayName", profile.DisplayName);
            }

            var viewModel = new DashboardViewModel
            {
                UserProfile = profile,
                GamesPlayed = HttpContext.Session.GetInt32("GamesPlayed") ?? 0,
                EffectiveDisplayName = HttpContext.Session.GetString("DisplayName")
            };

            return View(viewModel);
        }

        [HttpPost("dashboard/set-display-name")]
        public IActionResult SetDisplayName([FromBody] SetDisplayNameRequest request)
        {
            var name = request.DisplayName?.Trim();
            if (string.IsNullOrWhiteSpace(name) || name.Length > 20)
            {
                return BadRequest();
            }

            HttpContext.Session.SetString("DisplayName", name);
            return Ok();
        }

        public class SetDisplayNameRequest
        {
            public string? DisplayName { get; set; }
        }
    }
}