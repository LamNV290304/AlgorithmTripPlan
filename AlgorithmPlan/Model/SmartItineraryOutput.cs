using System;
using System.Collections.Generic;

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
    }

    public class TimelineItem
    {
        public string Type { get; set; } = string.Empty; // "Transport" or "Visit"
        public string Time { get; set; } = string.Empty; // "HH:mm - HH:mm"
        public string Description { get; set; } = string.Empty;
        public double? Cost { get; set; } // For Transport
        public double? TicketCost { get; set; } // For Visit
        public bool? GroupDiscountApplied { get; set; } // For Visit
    }

    public class TripSummary
    {
        public double TotalEstimatedCost { get; set; }
        public double RemainingContingencyFund { get; set; }
    }

    public class ItineraryRequest
    {
        public string TargetCity { get; set; } = string.Empty;
        public int GroupSize { get; set; }
        public double TotalBudget { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<string> UserFavoriteTags { get; set; } = new List<string>();
        public double? StartLatitude { get; set; }
        public double? StartLongitude { get; set; }
    }
}
