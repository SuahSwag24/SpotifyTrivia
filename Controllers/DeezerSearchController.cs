using Microsoft.AspNetCore.Mvc;
using SpotifyTrivia.Services;

namespace SpotifyTrivia.Controllers
{
    [ApiController]
    [Route("api/deezer")]
    public class DeezerSearchController : ControllerBase
    {
        private readonly IDeezerService _deezer;
        public DeezerSearchController(IDeezerService deezer) => _deezer = deezer;

        [HttpGet("artists")]
        public async Task<IActionResult> Artists([FromQuery] string q, [FromQuery] int index = 0)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Ok(Array.Empty<object>());
            var results = await _deezer.SearchArtistsAsync(q, index);
            return results == null ? StatusCode(503) : Ok(results);
        }

        [HttpGet("tracks")]
        public async Task<IActionResult> Tracks([FromQuery] string q, [FromQuery] int index = 0)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2) return Ok(Array.Empty<object>());
            var results = await _deezer.SearchTracksAsync(q, index);
            return results == null ? StatusCode(503) : Ok(results);
        }

        [HttpGet("artists/{artistId:long}/top")]
        public async Task<IActionResult> ArtistTop(long artistId)
        {
            var results = await _deezer.GetArtistTopTracksAsync(artistId);
            return results == null ? StatusCode(503) : Ok(results);
        }
    }
}