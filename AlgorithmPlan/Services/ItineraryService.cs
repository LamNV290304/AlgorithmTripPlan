using AlgorithmPlan.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace AlgorithmPlan.Services
{
    public class ItineraryService
    {
        private readonly string _dataPath = Path.Combine(AppContext.BaseDirectory, "data.json");
        private readonly Transport _defaultTransport = new Transport { TransportType = "Taxi", CostPerKm = 15000, AverageSpeed = 30 };

        public List<Location> GetAllLocations()
        {
            if (!File.Exists(_dataPath)) return new List<Location>();
            var json = File.ReadAllText(_dataPath);
            return JsonSerializer.Deserialize<List<Location>>(json);
        }

        public List<List<ItineraryItem>> GenerateMultiDayItinerary(
            double startLat, 
            double startLon, 
            string destination, 
            List<string> tags, 
            int days, 
            TimeSpan startTime, 
            TimeSpan endTime,
            double totalBudget)
        {
            var allLocations = GetAllLocations()
                .Where(l => l.Destination.Equals(destination, StringComparison.OrdinalIgnoreCase))
                .Where(l => tags == null || !tags.Any() || l.Tags.Intersect(tags, StringComparer.OrdinalIgnoreCase).Any())
                .ToList();

            var multiDayItinerary = new List<List<ItineraryItem>>();
            var visitedIds = new HashSet<int>();
            
            double effectiveBudgetLimit = totalBudget * 0.8;
            double remainingBudgetForOptimization = effectiveBudgetLimit;
            double totalSpentSoFar = 0;

            for (int day = 0; day < days; day++)
            {
                var dailyPlan = GenerateDailyItinerary(startLat, startLon, allLocations, visitedIds, startTime, endTime, day, ref remainingBudgetForOptimization, ref totalSpentSoFar, totalBudget);
                multiDayItinerary.Add(dailyPlan);
                foreach (var item in dailyPlan) visitedIds.Add(item.Location.Id);
            }

            return multiDayItinerary;
        }

        private List<ItineraryItem> GenerateDailyItinerary(
            double startLat, 
            double startLon, 
            List<Location> candidates, 
            HashSet<int> visitedIds, 
            TimeSpan startTime, 
            TimeSpan endTime,
            int dayOffset,
            ref double remainingBudgetForOptimization,
            ref double totalSpentSoFar,
            double totalBudget)
        {
            var currentLat = startLat;
            var currentLon = startLon;
            var currentTime = startTime;
            var dailyItinerary = new List<ItineraryItem>();
            var dayOfWeek = (DayOfWeek)(((int)DateTime.Now.DayOfWeek + dayOffset) % 7);

            while (currentTime < endTime && remainingBudgetForOptimization > 0)
            {
                var nextStopResult = FindNextStop(currentLat, currentLon, candidates, visitedIds, currentTime, dayOfWeek, remainingBudgetForOptimization, endTime);
                if (nextStopResult == null) break;

                var nextStop = nextStopResult.Location;
                var travelStartTime = currentTime;
                var travelTime = TimeSpan.FromMinutes(nextStopResult.TravelTimeMinutes);
                var arrivalTime = travelStartTime.Add(travelTime);
                
                var duration = TimeSpan.FromMinutes(nextStop.AverageStayDuration > 0 ? nextStop.AverageStayDuration : 120);
                var visitEndTime = arrivalTime.Add(duration);
                
                double totalCostForStep = nextStop.AverageBudget + nextStopResult.TransportCost;
                remainingBudgetForOptimization -= totalCostForStep;
                totalSpentSoFar += totalCostForStep;

                dailyItinerary.Add(new ItineraryItem
                {
                    Location = nextStop,
                    TransportMethod = nextStopResult.TransportMethod,
                    TravelTimeMinutes = Math.Round(nextStopResult.TravelTimeMinutes, 1),
                    TransportCost = Math.Round(nextStopResult.TransportCost, 0),
                    TravelStartTime = FormatTime(travelStartTime),
                    ArrivalTime = FormatTime(arrivalTime),
                    VisitEndTime = FormatTime(visitEndTime),
                    EstimatedCost = nextStop.AverageBudget,
                    TotalSpent = Math.Round(totalSpentSoFar, 0),
                    RemainingBudget = Math.Round(totalBudget - totalSpentSoFar, 0)
                });

                visitedIds.Add(nextStop.Id);
                currentLat = nextStop.Latitude;
                currentLon = nextStop.Longitude;
                
                currentTime = visitEndTime;
            }

            return dailyItinerary;
        }

        private dynamic FindNextStop(
            double currentLat, 
            double currentLon, 
            List<Location> candidates, 
            HashSet<int> visitedIds, 
            TimeSpan currentTime, 
            DayOfWeek dayOfWeek,
            double remainingBudget,
            TimeSpan dayEndTime)
        {
            var unvisited = candidates.Where(c => !visitedIds.Contains(c.Id)).ToList();
            
            var scoredCandidates = unvisited
                .Select(c => {
                    var distance = CalculateDistance(currentLat, currentLon, c.Latitude, c.Longitude);
                    
                    string transportMethod = "Walking";
                    double transportCost = 0;
                    double travelTimeMinutes = (distance / 4.0) * 60.0; 

                    if (distance >= 1.0)
                    {
                        transportMethod = _defaultTransport.TransportType;
                        transportCost = distance * _defaultTransport.CostPerKm;
                        travelTimeMinutes = (distance / _defaultTransport.AverageSpeed) * 60.0;
                    }

                    var arrivalTime = currentTime.Add(TimeSpan.FromMinutes(travelTimeMinutes));
                    var durationMinutes = c.AverageStayDuration > 0 ? c.AverageStayDuration : 120;
                    var visitEndTime = arrivalTime.Add(TimeSpan.FromMinutes(durationMinutes));
                    
                    return new { 
                        Location = c, 
                        Distance = distance,
                        TransportMethod = transportMethod,
                        TransportCost = transportCost,
                        TravelTimeMinutes = travelTimeMinutes,
                        IsOpen = IsLocationOpen(c, dayOfWeek, arrivalTime),
                        CanAfford = remainingBudget >= (c.AverageBudget + transportCost),
                        FitsInTime = visitEndTime <= dayEndTime,
                        BufferAfterVisit = (dayEndTime - visitEndTime).TotalMinutes
                    };
                })
                .Where(x => x.Distance <= 6.0 && x.IsOpen && x.CanAfford && x.FitsInTime)
                .OrderBy(x => x.Distance)
                .Take(10) 
                .Select(x => new {
                    x.Location,
                    x.TransportMethod,
                    x.TransportCost,
                    x.TravelTimeMinutes,
                    Score = CalculateScore(x.Distance, x.Location.AverageBudget + x.TransportCost, remainingBudget, x.BufferAfterVisit)
                })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            return scoredCandidates;
        }

        private double CalculateScore(double distance, double stepCost, double remainingBudget, double bufferAfterVisit)
        {
            double distScore = 1.0 / (distance + 0.1); 
            double budgetEfficiencyScore = (remainingBudget - stepCost) / (remainingBudget + 1.0);
            double timeFeasibilityScore = Math.Min(bufferAfterVisit / 600.0, 1.0); 

            return (distScore * 0.4) + (budgetEfficiencyScore * 0.4) + (timeFeasibilityScore * 0.2);
        }

        private bool IsLocationOpen(Location loc, DayOfWeek day, TimeSpan time)
        {
            if (loc.OpeningHours == null || !loc.OpeningHours.Any()) return true;
            var hours = loc.OpeningHours.FirstOrDefault(h => h.DayOfWeek == day);
            if (hours == null) return false;

            return time >= hours.OpenTime && time <= hours.CloseTime;
        }

        private string FormatTime(TimeSpan ts) => ts.ToString(@"hh\:mm");

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371; // km
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double deg) => deg * (Math.PI / 180);
    }
}
