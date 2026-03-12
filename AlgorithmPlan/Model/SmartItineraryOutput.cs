using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AlgorithmPlan.Model
{
    public class SmartItineraryOutput
    {
        public List<DailyItinerary> Days { get; set; } = new List<DailyItinerary>();
        public TripSummary TripSummary { get; set; } = new TripSummary();
    }

    public class DailyItinerary
    {
        public string Day { get; set; } = string.Empty;
        public DailyBudgetStatus DailyBudgetStatus { get; set; } = new DailyBudgetStatus();
        public List<TimelineItem> Timeline { get; set; } = new List<TimelineItem>();
    }

    public class DailyBudgetStatus
    {
        public double Spent { get; set; }
        public double Limit { get; set; }
        public double Ceiling { get; set; } // Maximum should spend
        public double Floor { get; set; } // Minimum should spend
        public double Weight { get; set; } // Weight for budget allocation (first/last day higher)
    }

    public class TimelineItem
    {
        public string Type { get; set; } = string.Empty; // "Transport", "Visit", "Rest", "Accommodation", "LuggageStorage", "CheckIn", "CheckOut", "Waiting", "Arrival"
        public string Time { get; set; } = string.Empty; // "HH:mm - HH:mm"
        public string TimeBlock { get; set; } = string.Empty; // "Morning", "Lunch Break", "Afternoon", "Evening"
        public string Description { get; set; } = string.Empty;
        public double? Cost { get; set; } // For Transport
        public double? TicketCost { get; set; } // For Visit
        public bool? GroupDiscountApplied { get; set; } // For Visit

        // Multiple transport options for user to choose (only for Transport type)
        [JsonPropertyName("transportOptions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public List<TransportOption> TransportOptions { get; set; } = new List<TransportOption>();

        [JsonPropertyName("selectedTransportIndex")]
        public int? SelectedTransportIndex { get; set; } // User's selected option index

        // Multiple accommodation options (only for Rest/Accommodation type)
        [JsonPropertyName("accommodationOptions")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AccommodationOption> AccommodationOptions { get; set; }

        [JsonPropertyName("selectedAccommodationIndex")]
        public int? SelectedAccommodationIndex { get; set; }
        
        [JsonPropertyName("alternativeAccommodations")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<AlternativeAccommodationDisplay> AlternativeAccommodations { get; set; }

        // For luggage storage, check-in/out actions
        public double? LuggageStorageCost { get; set; }
        public string Action { get; set; } // "CheckIn", "CheckOut", "LuggageStorage"
    }

    public class AccommodationOption
    {
        [JsonPropertyName("roomType")]
        public string RoomType { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("pricePerNight")]
        public double PricePerNight { get; set; }

        [JsonPropertyName("pricePerHour")]
        public double PricePerHour { get; set; }

        [JsonPropertyName("maxOccupancy")]
        public int MaxOccupancy { get; set; }

        [JsonPropertyName("roomsNeeded")]
        public int RoomsNeeded { get; set; }

        [JsonPropertyName("totalCost")]
        public double TotalCost { get; set; }

        [JsonPropertyName("amenities")]
        public List<string> Amenities { get; set; } = new List<string>();

        [JsonPropertyName("recommended")]
        public bool Recommended { get; set; }

        [JsonPropertyName("pros")]
        public string Pros { get; set; } = string.Empty;

        [JsonPropertyName("cons")]
        public string Cons { get; set; } = string.Empty;
    }

    // Alternative accommodation option for user selection
    public class AlternativeAccommodationDisplay
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("distance")]
        public double Distance { get; set; }

        [JsonPropertyName("recommendedRoomType")]
        public string RecommendedRoomType { get; set; } = string.Empty;

        [JsonPropertyName("totalCost")]
        public double TotalCost { get; set; }

        [JsonPropertyName("options")]
        public List<AccommodationOption> Options { get; set; } = new List<AccommodationOption>();
    }

    public class TripSummary
    {
        public double TotalEstimatedCost { get; set; }
        public double RemainingContingencyFund { get; set; }
        public double ContingencyFundPercentage { get; set; }
        public bool IsBudgetInsufficient { get; set; }
        public string BudgetWarning { get; set; }
        public double MinimumRecommendedBudget { get; set; }
    }

    public class ItineraryRequest
    {
        public List<string> Destinations { get; set; } = new List<string>();
        public int GroupSize { get; set; }
        public double TotalBudget { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<string> UserFavoriteTags { get; set; } = new List<string>();
        public double? StartLatitude { get; set; }
        public double? StartLongitude { get; set; }
    }

    // Transport option with pros/cons for user selection
    public class TransportOption
    {
        [JsonPropertyName("method")]
        public string Method { get; set; } = string.Empty;
        
        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;
        
        [JsonPropertyName("totalCost")]
        public double TotalCost { get; set; }
        
        [JsonPropertyName("travelTimeMinutes")]
        public double TravelTimeMinutes { get; set; }
        
        [JsonPropertyName("vehiclesNeeded")]
        public int VehiclesNeeded { get; set; }
        
        [JsonPropertyName("pros")]
        public string Pros { get; set; } = string.Empty;
        
        [JsonPropertyName("cons")]
        public string Cons { get; set; } = string.Empty;
        
        [JsonPropertyName("recommended")]
        public bool Recommended { get; set; }
        
        [JsonPropertyName("costPerPerson")]
        public double CostPerPerson => GroupSize > 0 ? TotalCost / GroupSize : 0;
        
        [JsonPropertyName("groupSize")]
        public int GroupSize { get; set; }
    }
}
