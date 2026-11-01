using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ReStore.Api.Controllers
{
    [AllowAnonymous]
    public class FallbackController(IWebHostEnvironment environment) : Controller
    {
        public IActionResult Index()
        {
            return PhysicalFile(Path.Combine(environment.WebRootPath, "index.html"), "text/html");
        }
    }
}
