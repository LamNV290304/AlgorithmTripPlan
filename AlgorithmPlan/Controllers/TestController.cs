using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using AlgorithmPlan.Services;
using AlgorithmPlan.Model;

namespace AlgorithmPlan.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        private readonly ItineraryService _itineraryService;

        public TestController(ItineraryService itineraryService)
        {
            _itineraryService = itineraryService;
        }

        [HttpPost("generate-smart")]
        public IActionResult GenerateSmartItinerary([FromBody] ItineraryRequest request)
        {
            try
            {
                var result = _itineraryService.GenerateSmartItinerary(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
