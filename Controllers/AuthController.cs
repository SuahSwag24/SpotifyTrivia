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
    public IActionResult Login()
    {
        var clientId = _config["Spotify:ClientId"];
        var redirectUri = "https://localhost:5177/callback";

        var scope = "user-read-private playlist-read-private playlist-read-collaborative";

        var spotifyAuthUrl = $"https://accounts.spotify.com/authorize?" +
            $"response_type=code" +
            $"&client_id={Uri.EscapeDataString(clientId!)}" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}";

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
            { "redirect_uri", "https://localhost:5177/callback" },
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

        return Content($"Received authorization code from Spotify: {code}");
    }
}