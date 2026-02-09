using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace AlgorithmPlan.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class TestController : ControllerBase
    {
        private readonly string _dataPath = "dataPattern.json";
        // Model cho Địa điểm
        public class TagModel { public string TagName { get; set; } = ""; public double Weight { get; set; } }
        public class Destination { public int Id { get; set; } public string Name { get; set; } = ""; public List<TagModel> Tags { get; set; } = new(); public double FinalScore { get; set; } }

        // Model cho yêu cầu từ người dùng
        public class UserPreference { public string TagName { get; set; } = ""; public double InterestLevel { get; set; } }


        [HttpPost("score-module-1")]
        public IActionResult TestModule1([FromBody] List<UserPreference> userPrefs)
        {
            if (!System.IO.File.Exists(_dataPath)) return NotFound("Không tìm thấy file data.json");

            // 1. Đọc dữ liệu giả từ file
            var jsonData = System.IO.File.ReadAllText(_dataPath);
            var destinations = JsonSerializer.Deserialize<List<Destination>>(jsonData);

            if (destinations == null) return BadRequest("Dữ liệu không hợp lệ");

            // 2. Logic Module 1: Chấm điểm (Scoring Engine)
            foreach (var dest in destinations)
            {
                double score = 0;
                foreach (var pref in userPrefs)
                {
                    var matchingTag = dest.Tags.FirstOrDefault(t => t.TagName == pref.TagName);
                    if (matchingTag != null)
                    {
                        // Công thức: Score = Sum(Mức độ thích * Trọng số tag địa điểm)
                        score += pref.InterestLevel * matchingTag.Weight;
                    }
                }
                dest.FinalScore = Math.Round(score, 2);
            }

            // 3. Sắp xếp theo điểm số từ cao xuống thấp
            var result = destinations.OrderByDescending(d => d.FinalScore).ToList();

            return Ok(result);
        }
    }
}
