using System.Collections.Generic;

namespace AlgorithmPlan.Model
{
    public class Location
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int LocationTypeId { get; set; }
        public LocationType LocationType { get; set; }
        public List<OpeningHours> OpeningHours { get; set; }
        public double AverageBudget { get; set; }
        public int AverageStayDuration { get; set; } // In minutes
        public string Destination { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
    }
}
