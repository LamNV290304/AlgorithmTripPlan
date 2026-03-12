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
        
        // Detailed accommodation information
        public List<RoomType> RoomTypes { get; set; } = new List<RoomType>();
        public string CheckInTime { get; set; }
        public string CheckOutTime { get; set; }
        public bool HasLuggageStorage { get; set; }
        public double LuggageStorageCost { get; set; }
        public bool HasHourlyRate { get; set; }
        public double HourlyRate { get; set; }
    }

    public class RoomType
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public int MaxOccupancy { get; set; }
        public double PricePerNight { get; set; }
        public double PricePerHour { get; set; }
        public int AvailableRooms { get; set; }
        public List<string> Amenities { get; set; } = new List<string>();
    }
}
