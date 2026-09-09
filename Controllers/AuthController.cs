using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SpotifyTrivia.Controllers;

public class AuthController : Controller
{
    private readonly IConfiguration _config;
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthController(IConfiguration config, IHttpClientFactory httpClientFactory)
    {
        _config = config;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet("login")]
    public IActionResult Login(bool force = false)
    {
        var clientId = _config["Spotify:ClientId"];
        var redirectUri = _config["Spotify:RedirectUri"];

        var scope = "user-read-private playlist-read-private playlist-read-collaborative streaming user-read-email user-library-read user-read-recently-played";

        var spotifyAuthUrl = $"https://accounts.spotify.com/authorize?" +
            $"response_type=code" +
            $"&client_id={Uri.EscapeDataString(clientId!)}" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            (force ? "&show_dialog=true" : string.Empty);

        return Redirect(spotifyAuthUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return BadRequest("Authorization code was missing from Spotify.");
        }

        var client = _httpClientFactory.CreateClient();

        var tokenRequestBody = new Dictionary<string, string>
        {
            {"grant_type", "authorization_code"},
            {"code", code},
            { "redirect_uri", _config["Spotify:RedirectUri"] },
            { "client_id", _config["Spotify:ClientId"]! },
            { "client_secret", _config["Spotify:ClientSecret"]! }
        };

        var requestContent = new FormUrlEncodedContent(tokenRequestBody);

        var response = await client.PostAsync("https://accounts.spotify.com/api/token", requestContent);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            return Content($"Error retrieving token: {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var jsonDoc = JsonDocument.Parse(responseString);

        var accessToken = jsonDoc.RootElement.GetProperty("access_token").GetString();
        var refreshToken = jsonDoc.RootElement.TryGetProperty("refresh_token", out var refreshTokenElement)
            ? refreshTokenElement.GetString()
            : null;

        if (!string.IsNullOrEmpty(accessToken))
        {
            HttpContext.Session.SetString("SpotifyAccessToken", accessToken);
        }

        if (!string.IsNullOrEmpty(refreshToken))
        {
            HttpContext.Session.SetString("SpotifyRefreshToken", refreshToken);
        }

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public IActionResult Logout()
    {
        HttpContext.Session.Clear();
        Response.Cookies.Delete(".AspNetCore.Session");

        return RedirectToAction(nameof(Login), new { force = true });
    }

    [HttpGet("logged-out")]
    public IActionResult LoggedOut()
    {
        return View();
    }
}