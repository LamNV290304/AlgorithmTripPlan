using Microsoft.AspNetCore.Mvc;

namespace AlgorithmPlan.Controllers
{
    public class TestController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
