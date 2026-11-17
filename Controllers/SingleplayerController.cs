using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using SpotifyTrivia.Models.Multiplayer;
using SpotifyTrivia.Services;
using SpotifyTrivia.Services.Dtos;
using SpotifyTrivia.Services.Singleplayer;

namespace SpotifyTrivia.Controllers
{
    public class SingleplayerAnswerRequest
    {
        public int ChoiceIndex { get; set; } = -1;
        public long? TrackId { get; set; }
        public long? ArtistId { get; set; }
    }

    public class SingleplayerController : Controller
    {
        private readonly ISpotifyService _spotifyService;
        private readonly ILobbyManager _lobbyManager;
        private readonly ISingleplayerService _singleplayerService;

        public SingleplayerController(ISpotifyService spotifyService, ILobbyManager lobbyManager, ISingleplayerService singleplayerService)
        {
            _spotifyService = spotifyService;
            _lobbyManager = lobbyManager;
            _singleplayerService = singleplayerService;
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

        [HttpPost("singleplayer/start")]
        public async Task<IActionResult> Start([FromForm] SingleplayerStartRequestDto request)
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");
            if (string.IsNullOrWhiteSpace(token))
                return Unauthorized(new { error = "Please log in with Spotify." });

            if (string.IsNullOrWhiteSpace(request.PlaylistId))
                return BadRequest(new { error = "Select a playlist first." });

            if (!Enum.TryParse<GameModeType>(request.GameMode, true, out var mode)
                || (mode != GameModeType.ClassicGuessSong && mode != GameModeType.GuessArtist))
                return BadRequest(new { error = "Select a valid game mode." });

            if (!Enum.TryParse<AnswerStyle>(request.AnswerStyle, true, out var answerStyle))
                answerStyle = AnswerStyle.MultipleChoice;
            if (!Enum.TryParse<AlbumCoverVisibility>(request.BlurAlbum, true, out var blur))
                blur = AlbumCoverVisibility.Hide;

            int? sampleSize;
            if (string.Equals(request.SampleSize, "max", StringComparison.OrdinalIgnoreCase)) sampleSize = null;
            else if (int.TryParse(request.SampleSize, out var s) && new[] { 50, 100, 200, 500 }.Contains(s)) sampleSize = s;
            else sampleSize = 200;

            var result = await _singleplayerService.StartAsync(new SingleplayerStartParams(
                GetOrCreatePlayerId(),
                HttpContext.Session.GetString("DisplayName") ?? "Player",
                token,
                HttpContext.Session.GetString("SpotifyRefreshToken"),
                request.PlaylistId,
                mode,
                Math.Clamp(request.QuestionCount, 5, 20),
                Math.Clamp(request.RoundDuration, 5, 20),
                sampleSize, blur, answerStyle));

            if (!result.Ok) return StatusCode(result.Status, new { error = result.Error });

            if (result.Value!.RefreshedAccessToken != null)
                HttpContext.Session.SetString("SpotifyAccessToken", result.Value.RefreshedAccessToken);

            return Ok(new { sessionId = result.Value.SessionId, totalQuestions = result.Value.TotalQuestions });
        }

        [HttpGet("singleplayer/game/{id}")]
        public IActionResult Game(string id)
        {
            if (string.IsNullOrWhiteSpace(HttpContext.Session.GetString("SpotifyAccessToken")))
                return RedirectToAction("Login", "Auth");

            var info = _singleplayerService.GetGameInfo(id, GetOrCreatePlayerId());
            if (!info.Ok) return RedirectToAction(nameof(Index));

            return View(info.Value);
        }

        [HttpGet("singleplayer/{id}/round/{round:int}/prepare")]
        public IActionResult Prepare(string id, int round) => ToAction(_singleplayerService.Prepare(id, GetOrCreatePlayerId(), round));

        [HttpPost("singleplayer/{id}/round/{round:int}/begin")]
        public IActionResult Begin(string id, int round) => ToAction(_singleplayerService.Begin(id, GetOrCreatePlayerId(), round));

        [HttpPost("singleplayer/{id}/round/{round:int}/answer")]
        public async Task<IActionResult> Answer(string id, int round, [FromBody] SingleplayerAnswerRequest body)
            => ToAction(await _singleplayerService.AnswerAsync(id, GetOrCreatePlayerId(), round, body.ChoiceIndex, body.TrackId, body.ArtistId));

        [HttpPost("singleplayer/{id}/round/{round:int}/end")]
        public IActionResult End(string id, int round) => ToAction(_singleplayerService.End(id, GetOrCreatePlayerId(), round));

        [HttpGet("singleplayer/{id}/summary")]
        public IActionResult Summary(string id) => ToAction(_singleplayerService.Summary(id, GetOrCreatePlayerId()));

        [HttpPost("singleplayer/{id}/continue")]
        public async Task<IActionResult> Continue(string id)
            => ToAction(await _singleplayerService.ContinueAsync(id, GetOrCreatePlayerId()));

        private IActionResult ToAction<T>(SpResult<T> r) =>
            r.Ok ? Ok(r.Value) : StatusCode(r.Status, new { error = r.Error });

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
