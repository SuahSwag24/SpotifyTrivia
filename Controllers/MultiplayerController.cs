using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.IIS;
using SpotifyTrivia.Models.Multiplayer;
using SpotifyTrivia.Services;

namespace SpotifyTrivia.Controllers
{
    public class MultiplayerController : Controller
    {
        private readonly ILobbyManager _lobbyManager;

        public MultiplayerController(ILobbyManager lobbyManager)
        {
            _lobbyManager = lobbyManager;
        }

        [HttpGet("multiplayer")]
        public IActionResult Index()
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");
            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Login", "Auth");
            }

            return View();
        }

        [HttpPost("multiplayer/create")]
        public IActionResult CreateLobby()
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");
            if (string.IsNullOrEmpty(token)) return RedirectToAction("Login", "Auth");

            string hostPlayerId = GetOrCreatePlayerId();
            string hostDisplayName = ResolveDisplayName();

            var lobby = _lobbyManager.CreateLobby(hostPlayerId, hostDisplayName, token);

            return RedirectToAction("Lobby", new { code = lobby.Code });
        }

        [HttpGet("multiplayer/lobby/{code}")]
        public IActionResult Lobby(string code)
        {
            var lobby = _lobbyManager.GetLobby(code);
            if (lobby == null)
            {
                TempData["ErrorMessage"] = "Lobby not found...";
                return RedirectToAction("Index");
            }

            ViewBag.PlayerId = GetOrCreatePlayerId();
            ViewBag.DisplayName = ResolveDisplayName();
            ViewBag.LobbyCode = code;
            return View(lobby);
        }

        [HttpPost("multiplayer/join")]
        public IActionResult JoinLobby(string code)
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");
            if (string.IsNullOrEmpty(token)) return RedirectToAction("Login", "Auth");

            code = code?.Trim().ToUpperInvariant() ?? string.Empty;

            var lobby = _lobbyManager.GetLobby(code);
            if (lobby == null)
            {
                TempData["ErrorMessage"] = "Lobby not found. Check the code and try again.";
                return RedirectToAction("Index");
            }

            return RedirectToAction("Lobby", new { code });
        }

        [HttpGet("multiplayer/game/{code}")]
        public IActionResult Game(string code)
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");
            if (string.IsNullOrEmpty(token)) return RedirectToAction("Login", "Auth");

            var lobby = _lobbyManager.GetLobby(code);
            if (lobby == null)
            {
                TempData["ErrorMessage"] = "This lobby no longer exists (ERROR)";
                return RedirectToAction("Index");
            }    

            if (lobby.State == LobbyState.Waiting)
            {
                //  Game waiting to start...
                return RedirectToAction("Lobby", new { code });
            }

            var playerId = HttpContext.Session.GetString("PlayerId");
            if (string.IsNullOrEmpty(playerId) || !lobby.Players.ContainsKey(playerId))
            {
                TempData["ErrorMessage"] = "Unable to join lobby";
                return RedirectToAction("Index");
            }

            ViewBag.PlayerId = GetOrCreatePlayerId();
            ViewBag.DisplayName = ResolveDisplayName();
            ViewBag.LobbyCode = code;
            return View(lobby);
        }

        private string GetOrCreatePlayerId()
        {
            var playerId = HttpContext.Session.GetString("PlayerId");
            if (string.IsNullOrEmpty(playerId))
            {
                playerId = Guid.NewGuid().ToString();
                HttpContext.Session.SetString("PlayerId", playerId);
            }

            return playerId;
        }

        private string ResolveDisplayName()
        {
            return HttpContext.Session.GetString("DisplayName") ?? "Player";
        }
    }
}
