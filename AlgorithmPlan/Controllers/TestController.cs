using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AlgorithmPlan.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        private readonly ILogger<TestController> _logger;

        public TestController(ILogger<TestController> logger)
        {
            _logger = logger;
        }
    }
}
