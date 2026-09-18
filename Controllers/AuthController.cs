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
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";

        if (force)
        {
            spotifyAuthUrl += "&show_dialog=true";
        }

        return Redirect(spotifyAuthUrl);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback(string? code, string? error)
    {
        if (!string.IsNullOrEmpty(error))
        {
            TempData["LoginError"] = error == "access_denied"
                ? "Login was cancelled."
                : "Something went wrong when signing in with Spotify.";
            
            return RedirectToAction("Index", "Dashboard");
        }

        if (string.IsNullOrEmpty(code))
        {
            TempData["LoginError"] = "Login was cancelled.";
            return RedirectToAction("index", "Dashboard");
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
            error = await response.Content.ReadAsStringAsync();
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

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet("logged-out")]
    public IActionResult LoggedOut()
    {
        return View();
    }
}