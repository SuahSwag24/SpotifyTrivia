using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Server.IIS;
using SpotifyTrivia.Services;

namespace SpotifyTrivia.Controllers
{
    public class MultiplayerController : Controller
    {
        private readonly LobbyManager _lobbyManager;

        [HttpGet("multiplayer")]
        public IActionResult Index()
        {
            throw new NotImplementedException();
        }

        [HttpPost("multiplayer/create")]
        public IActionResult CreateLobby()
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");
            if (string.IsNullOrEmpty(token)) return RedirectToAction("Login", "Auth");

            string hostPlayerId = "0000";
            var lobby = _lobbyManager.CreateLobby(hostPlayerId, hostPlayerName: "Host", token);

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
            ViewBag.LobbyCode = code;
            return View(lobby);
        }

        [HttpGet("mutliplayer/game/{code}")]
        public IActionResult Game(string code)
        {
            throw new NotImplementedException();
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
    }
}
