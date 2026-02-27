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

        [HttpPost("generate")]
        public IActionResult GenerateItinerary([FromBody] ItineraryRequest request)
        {
            var result = _itineraryService.GenerateMultiDayItinerary(
                request.StartLat,
                request.StartLon,
                request.Destination,
                request.Tags,
                request.Days,
                request.StartTime,
                request.EndTime,
                request.TotalBudget
            );
            return Ok(result);
        }

        public class ItineraryRequest
        {
            public double StartLat { get; set; }
            public double StartLon { get; set; }
            public string Destination { get; set; }
            public List<string> Tags { get; set; }
            public int Days { get; set; }
            public TimeSpan StartTime { get; set; }
            public TimeSpan EndTime { get; set; }
            public double TotalBudget { get; set; }
        }
    }
}
