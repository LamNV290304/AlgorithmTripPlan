using System;

namespace AlgorithmPlan.Model
{
    public class ItineraryItem
    {
        public Location Location { get; set; }
        public string TransportMethod { get; set; }
        public double TravelTimeMinutes { get; set; }
        public double TransportCost { get; set; }
        public string TravelStartTime { get; set; }
        public string ArrivalTime { get; set; }
        public string VisitEndTime { get; set; }
        public double EstimatedCost { get; set; }
        public double TotalSpent { get; set; }
        public double RemainingBudget { get; set; }
    }
}
