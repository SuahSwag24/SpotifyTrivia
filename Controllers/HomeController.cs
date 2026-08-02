using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace SpotifyTrivia.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        var token = HttpContext.Session.GetString("SpotifyAccessToken");

        if (string.IsNullOrEmpty(token))
        {
            ViewBag.IsLoggedIn = false;
        }
        else
        {
            ViewBag.IsLoggedIn = true;
            ViewBag.TokenPreview = token [..10] + "...";
        }
        
        return View();
    }
}