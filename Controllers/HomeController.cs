using Microsoft.AspNetCore.Mvc;

namespace SpotifyTrivia.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet("")]
        public IActionResult Index()
        {
            var token = HttpContext.Session.GetString("SpotifyAccessToken");

            // If user isn't logged in, send them to Spotify Auth
            if (string.IsNullOrEmpty(token))
            {
                return RedirectToAction("Login", "Auth");
            }

            // If user is already logged in, send them to the Dashboard
            return RedirectToAction("Index", "Dashboard");
        }
    }
}