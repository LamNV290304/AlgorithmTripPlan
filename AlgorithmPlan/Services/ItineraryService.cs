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
        
        // Vehicle capacities and costs per km (examples)
        private readonly List<VehicleType> _vehicleTypes = new List<VehicleType>
        {
            new VehicleType { Name = "Walking", Capacity = 100, CostPerKm = 0, SpeedKmh = 4, IsWalking = true },
            new VehicleType { Name = "Taxi 4-seat", Capacity = 4, CostPerKm = 15000, SpeedKmh = 30 },
            new VehicleType { Name = "7-seat vehicle", Capacity = 7, CostPerKm = 20000, SpeedKmh = 30 },
            new VehicleType { Name = "16-seat van", Capacity = 16, CostPerKm = 35000, SpeedKmh = 25 }
        };

        public List<Location> GetAllLocations()
        {
            if (!File.Exists(_dataPath)) return new List<Location>();
            var json = File.ReadAllText(_dataPath);
            return JsonSerializer.Deserialize<List<Location>>(json);
        }

        public SmartItineraryOutput GenerateSmartItinerary(ItineraryRequest request)
        {
            // MODULE 3.1 - Budget Partitioning
            double contingencyFund = request.TotalBudget * 0.1;
            double usableBudget = request.TotalBudget * 0.9;

            // MODULE 1 - Preference Filtering (Tag-Based Scoring)
            var allLocations = GetAllLocations();
            var candidateLocations = FilterAndScoreLocations(allLocations, request.TargetCity, request.UserFavoriteTags);

            // MODULE 3.2 - Daily Weight Allocation
            var dailyBudgets = AllocateDailyBudgets(request.StartDate, request.EndDate, usableBudget);
            
            var output = new SmartItineraryOutput();
            var visitedIds = new HashSet<int>();
            double totalSpent = 0;
            double rolloverBudget = 0;

            double currentLat = request.StartLatitude ?? 21.0285; // Default Hanoi lat
            double currentLon = request.StartLongitude ?? 105.8522; // Default Hanoi lon

            int totalDays = (request.EndDate - request.StartDate).Days + 1;
            for (int i = 0; i < totalDays; i++)
            {
                var currentDate = request.StartDate.AddDays(i);
                double dailyLimit = dailyBudgets[i] + rolloverBudget;
                var dailyItinerary = GenerateDailyPlan(
                    currentDate, 
                    dailyLimit, 
                    request.GroupSize, 
                    candidateLocations, 
                    visitedIds, 
                    ref currentLat, 
                    ref currentLon);

                output.Days.Add(dailyItinerary);
                totalSpent += dailyItinerary.DailyBudgetStatus.Spent;
                
                // MODULE 3.4 - Rollover Constraint
                rolloverBudget = dailyLimit - dailyItinerary.DailyBudgetStatus.Spent;
            }

            output.TripSummary = new TripSummary
            {
                TotalEstimatedCost = Math.Round(totalSpent, 0),
                RemainingContingencyFund = Math.Round(contingencyFund, 0)
            };

            return output;
        }

        // MODULE 1 - PREFERENCE FILTERING
        private List<ScoredLocation> FilterAndScoreLocations(List<Location> allLocations, string targetCity, List<string> favoriteTags)
        {
            return allLocations
                .Where(l => l.Destination.Equals(targetCity, StringComparison.OrdinalIgnoreCase))
                .Select(l => new ScoredLocation
                {
                    Location = l,
                    Score = favoriteTags == null ? 0 : l.Tags.Intersect(favoriteTags, StringComparer.OrdinalIgnoreCase).Count()
                })
                .Where(sl => sl.Score > 0)
                .OrderByDescending(sl => sl.Score)
                .ToList();
        }

        // MODULE 3.2 - Daily Weight Allocation
        private List<double> AllocateDailyBudgets(DateTime start, DateTime end, double usableBudget)
        {
            int totalDays = (end - start).Days + 1;
            var weights = new List<double>();
            for (int i = 0; i < totalDays; i++)
            {
                var date = start.AddDays(i);
                double weight = 1.0;

                // Weekend (Fri/Sat/Sun) = +0.5
                if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                    weight += 0.5;

                // First day = +0.2
                if (i == 0) weight += 0.2;

                // Last day = +0.3
                if (i == totalDays - 1) weight += 0.3;

                weights.Add(weight);
            }

            double totalWeight = weights.Sum();
            return weights.Select(w => (w / totalWeight) * usableBudget).ToList();
        }

        private DailyItinerary GenerateDailyPlan(
            DateTime date, 
            double dailyLimit, 
            int groupSize, 
            List<ScoredLocation> candidates, 
            HashSet<int> visitedIds,
            ref double currentLat,
            ref double currentLon)
        {
            var dailyPlan = new DailyItinerary
            {
                Day = $"{date.DayOfWeek} - {date:yyyy-MM-dd}",
                DailyBudgetStatus = new DailyBudgetStatus { Limit = Math.Round(dailyLimit, 0), Spent = 0 }
            };

            TimeSpan currentTime = new TimeSpan(8, 0, 0); // Start at 08:00
            TimeSpan endTime = new TimeSpan(21, 0, 0); // End at 21:00

            while (currentTime < endTime)
            {
                // MODULE 2 - SPATIAL–TEMPORAL ROUTING
                var bestAttraction = FindNextBestAttraction(
                    currentLat, 
                    currentLon, 
                    candidates, 
                    visitedIds, 
                    currentTime, 
                    date.DayOfWeek, 
                    endTime, 
                    groupSize,
                    dailyLimit - dailyPlan.DailyBudgetStatus.Spent);

                if (bestAttraction == null) break;

                // MODULE 3.3 - Vehicle Packing Optimization
                var transport = OptimizeTransport(bestAttraction.Distance, groupSize);

                // Check budget with rollover flexibility (3.4)
                double stepCost = transport.TotalCost + bestAttraction.Location.AverageBudget * groupSize;
                if (dailyPlan.DailyBudgetStatus.Spent + stepCost > dailyLimit * 1.2) break;

                // Add Transport to timeline
                TimeSpan arrivalTime = currentTime.Add(TimeSpan.FromMinutes(transport.TravelTimeMinutes));
                dailyPlan.Timeline.Add(new TimelineItem
                {
                    Type = "Transport",
                    Time = $"{currentTime:hh\\:mm} - {arrivalTime:hh\\:mm}",
                    Description = $"{transport.Description} to {bestAttraction.Location.Name}",
                    Cost = Math.Round(transport.TotalCost, 0)
                });

                // 2.5 Group Delay Effect
                double actualStayTimeMinutes = bestAttraction.Location.AverageStayDuration * (1 + 0.05 * (groupSize - 2));
                TimeSpan visitEndTime = arrivalTime.Add(TimeSpan.FromMinutes(actualStayTimeMinutes));

                // Add Visit to timeline
                dailyPlan.Timeline.Add(new TimelineItem
                {
                    Type = "Visit",
                    Time = $"{arrivalTime:hh\\:mm} - {visitEndTime:hh\\:mm}",
                    Description = $"Visit {bestAttraction.Location.Name}",
                    TicketCost = Math.Round(bestAttraction.Location.AverageBudget * groupSize, 0),
                    GroupDiscountApplied = groupSize >= 5 // Example discount logic
                });

                dailyPlan.DailyBudgetStatus.Spent += stepCost;
                visitedIds.Add(bestAttraction.Location.Id);
                currentLat = bestAttraction.Location.Latitude;
                currentLon = bestAttraction.Location.Longitude;
                currentTime = visitEndTime;
            }

            dailyPlan.DailyBudgetStatus.Spent = Math.Round(dailyPlan.DailyBudgetStatus.Spent, 0);
            return dailyPlan;
        }

        // MODULE 2 - SPATIAL–TEMPORAL ROUTING
        private BestAttraction FindNextBestAttraction(
            double lat, 
            double lon, 
            List<ScoredLocation> candidates, 
            HashSet<int> visitedIds, 
            TimeSpan currentTime, 
            DayOfWeek dayOfWeek, 
            TimeSpan dayEndTime,
            int groupSize,
            double remainingDailyBudget)
        {
            // 2.1 Adaptive Radius Search
            double r = 2.0;
            List<ScoredLocation> nearby = new List<ScoredLocation>();

            while (r <= 15.0)
            {
                nearby = candidates
                    .Where(c => !visitedIds.Contains(c.Location.Id))
                    .Select(c => new { ScoredLocation = c, Distance = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude) })
                    .Where(x => x.Distance <= r)
                    .Select(x => x.ScoredLocation)
                    .ToList();

                if (nearby.Count >= 3 || r >= 15.0) break;
                r += 2.0;
            }

            if (!nearby.Any()) return null;

            // 2.3 Time Constraint Filtering
            var validAttractions = nearby
                .Select(c => {
                    double dist = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude);
                    var transport = OptimizeTransport(dist, groupSize);
                    TimeSpan arrivalTime = currentTime.Add(TimeSpan.FromMinutes(transport.TravelTimeMinutes));
                    
                    // 2.5 Group Delay Effect
                    double actualStayTime = c.Location.AverageStayDuration * (1 + 0.05 * (groupSize - 2));
                    TimeSpan visitEndTime = arrivalTime.Add(TimeSpan.FromMinutes(actualStayTime));

                    return new {
                        ScoredLocation = c,
                        Distance = dist,
                        Transport = transport,
                        ArrivalTime = arrivalTime,
                        VisitEndTime = visitEndTime,
                        IsOpen = IsLocationOpen(c.Location, dayOfWeek, arrivalTime, visitEndTime),
                        Cost = transport.TotalCost + c.Location.AverageBudget * groupSize
                    };
                })
                .Where(x => x.IsOpen && x.VisitEndTime <= dayEndTime && x.Cost <= remainingDailyBudget * 1.2)
                .OrderByDescending(x => x.ScoredLocation.Score)
                .ThenBy(x => x.Distance)
                .FirstOrDefault();

            if (validAttractions == null) return null;

            return new BestAttraction
            {
                Location = validAttractions.ScoredLocation.Location,
                Distance = validAttractions.Distance
            };
        }

        // MODULE 3.3 - Vehicle Packing Optimization
        private TransportOptimization OptimizeTransport(double distance, int groupSize)
        {
            if (distance < 1.0)
            {
                return new TransportOptimization
                {
                    Description = "Walking",
                    TotalCost = 0,
                    TravelTimeMinutes = (distance / 4.0) * 60.0
                };
            }

            var options = _vehicleTypes.Where(v => !v.IsWalking).Select(v => {
                int vehiclesNeeded = (int)Math.Ceiling((double)groupSize / v.Capacity);
                double totalCost = vehiclesNeeded * v.CostPerKm * distance;
                return new {
                    Vehicle = v,
                    Count = vehiclesNeeded,
                    TotalCost = totalCost,
                    TravelTimeMinutes = (distance / v.SpeedKmh) * 60.0
                };
            }).OrderBy(x => x.TotalCost / groupSize).First();

            return new TransportOptimization
            {
                Description = $"{options.Count} x {options.Vehicle.Name}",
                TotalCost = options.TotalCost,
                TravelTimeMinutes = options.TravelTimeMinutes
            };
        }

        private bool IsLocationOpen(Location loc, DayOfWeek day, TimeSpan arrival, TimeSpan departure)
        {
            if (loc.OpeningHours == null || !loc.OpeningHours.Any()) return true;
            var hours = loc.OpeningHours.FirstOrDefault(h => h.DayOfWeek == day);
            if (hours == null) return false;

            return arrival >= hours.OpenTime && departure <= hours.CloseTime;
        }

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

        private class ScoredLocation
        {
            public Location Location { get; set; }
            public int Score { get; set; }
        }

        private class BestAttraction
        {
            public Location Location { get; set; }
            public double Distance { get; set; }
        }

        private class VehicleType
        {
            public string Name { get; set; }
            public int Capacity { get; set; }
            public double CostPerKm { get; set; }
            public double SpeedKmh { get; set; }
            public bool IsWalking { get; set; }
        }

        private class TransportOptimization
        {
            public string Description { get; set; }
            public double TotalCost { get; set; }
            public double TravelTimeMinutes { get; set; }
        }
    }
}
