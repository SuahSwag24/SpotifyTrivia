using Microsoft.AspNetCore.Mvc;

namespace SpotifyTrivia.Controllers
{
    public class HomeController : Controller
    {
        [HttpGet("")]
        public IActionResult Index()
        {
            //  Auth check is guarded in dashboard already. Always redirect user to dashboard.
            return RedirectToAction("Index", "Dashboard");
        }
    }
}