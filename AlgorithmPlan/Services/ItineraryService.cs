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

        private static readonly TimeSpan MorningStart = new TimeSpan(8, 0, 0);
        private static readonly TimeSpan MorningEnd = new TimeSpan(12, 0, 0);
        private static readonly TimeSpan LunchStart = new TimeSpan(12, 0, 0);
        private static readonly TimeSpan LunchEnd = new TimeSpan(13, 0, 0);
        private static readonly TimeSpan AfternoonStart = new TimeSpan(13, 0, 0);
        private static readonly TimeSpan AfternoonEnd = new TimeSpan(18, 0, 0);
        private static readonly TimeSpan EveningStart = new TimeSpan(18, 0, 0);
        private static readonly TimeSpan EveningEnd = new TimeSpan(21, 0, 0);

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

            // Filter all locations by requested destinations
            var allLocations = GetAllLocations();
            var candidateLocations = FilterAndScoreLocations(allLocations, request.Destinations, request.UserFavoriteTags);

            // Determine best visiting order of destinations
            var orderedDestinations = DetermineBestVisitingOrder(request.Destinations, candidateLocations, request.StartLatitude ?? 21.0285, request.StartLongitude ?? 105.8522);

            // Determine how many days in each destination
            int totalDays = (request.EndDate - request.StartDate).Days + 1;
            var destinationDayAllocation = AllocateDaysToDestinations(orderedDestinations, candidateLocations, totalDays);

            // MODULE 3.2 - Daily Weight Allocation
            var dailyBudgets = AllocateDailyBudgets(request.StartDate, request.EndDate, usableBudget);
            
            var output = new SmartItineraryOutput();
            var visitedIds = new HashSet<int>();
            double totalSpent = 0;
            double rolloverBudget = 0;

            double currentLat = request.StartLatitude ?? 21.0285;
            double currentLon = request.StartLongitude ?? 105.8522;

            int dayCounter = 0;
            string currentDestination = null;

            foreach (var destAlloc in destinationDayAllocation)
            {
                string destinationName = destAlloc.Key;
                int daysInThisDest = destAlloc.Value;
                var destCandidates = candidateLocations.Where(c => c.Location.Destination.Equals(destinationName, StringComparison.OrdinalIgnoreCase)).ToList();

                for (int d = 0; d < daysInThisDest; d++)
                {
                    if (dayCounter >= totalDays) break;

                    var currentDate = request.StartDate.AddDays(dayCounter);
                    double dailyLimit = dailyBudgets[dayCounter] + rolloverBudget;
                    
                    var dailyPlan = new DailyItinerary
                    {
                        Day = $"Day {dayCounter + 1} – {destinationName} ({currentDate:yyyy-MM-dd})",
                        DailyBudgetStatus = new DailyBudgetStatus { Limit = Math.Round(dailyLimit, 0), Spent = 0 }
                    };

                    TimeSpan currentTime = MorningStart;

                    // Check if moving to a new destination
                    if (currentDestination != destinationName)
                    {
                        // Inter-city movement
                        var destCenter = GetDestinationCenter(destCandidates);
                        double distance = CalculateDistance(currentLat, currentLon, destCenter.Lat, destCenter.Lon);
                        
                        if (currentDestination != null) // Don't add transport for the very first city if we are already there
                        {
                            var transport = GetInterCityTransport(distance, request.GroupSize);
                            
                            // Add Transport to timeline
                            TimeSpan transportArrival = currentTime.Add(TimeSpan.FromMinutes(transport.TravelTimeMinutes));
                            
                            // Respect Lunch Break for travel
                            if (currentTime < LunchStart && transportArrival > LunchStart)
                            {
                                // If it overlaps lunch, we either start earlier or arrive later. 
                                // For simplicity, we just add the lunch duration if it crosses the lunch window.
                                transportArrival = transportArrival.Add(TimeSpan.FromHours(1));
                            }

                            string timeBlock = currentTime < LunchStart ? "Morning" : "Afternoon";

                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "Transport",
                                Time = $"{currentTime:hh\\:mm} - {transportArrival:hh\\:mm}",
                                TimeBlock = timeBlock,
                                Description = $"{transport.Description} from {currentDestination} to {destinationName} ({Math.Round(distance, 1)} km)",
                                Cost = Math.Round(transport.TotalCost, 0)
                            });
                            
                            currentTime = transportArrival;
                            dailyPlan.DailyBudgetStatus.Spent += transport.TotalCost;
                        }
                        
                        currentLat = destCenter.Lat;
                        currentLon = destCenter.Lon;
                        currentDestination = destinationName;
                    }

                    // --- MORNING BLOCK ---
                    while (currentTime < MorningEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedIds, currentTime, currentDate.DayOfWeek, 
                            MorningEnd, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent, false);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Morning", dailyPlan, request.GroupSize, dailyLimit, visitedIds, ref currentLat, ref currentLon);
                    }

                    // --- LUNCH BREAK ---
                    if (currentTime < LunchEnd)
                    {
                        if (currentTime < LunchStart) currentTime = LunchStart;
                        
                        dailyPlan.Timeline.Add(new TimelineItem
                        {
                            Type = "Rest",
                            Time = $"{LunchStart:hh\\:mm} - {LunchEnd:hh\\:mm}",
                            TimeBlock = "Lunch Break",
                            Description = "Lunch and rest period"
                        });
                        currentTime = LunchEnd;
                    }

                    // --- AFTERNOON BLOCK ---
                    while (currentTime < AfternoonEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedIds, currentTime, currentDate.DayOfWeek, 
                            AfternoonEnd, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent, false);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Afternoon", dailyPlan, request.GroupSize, dailyLimit, visitedIds, ref currentLat, ref currentLon);
                    }

                    // --- EVENING BLOCK ---
                    if (currentTime < EveningStart) currentTime = EveningStart;
                    while (currentTime < EveningEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedIds, currentTime, currentDate.DayOfWeek, 
                            EveningEnd, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent, true);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Evening", dailyPlan, request.GroupSize, dailyLimit, visitedIds, ref currentLat, ref currentLon);
                    }

                    dailyPlan.DailyBudgetStatus.Spent = Math.Round(dailyPlan.DailyBudgetStatus.Spent, 0);
                    output.Days.Add(dailyPlan);
                    totalSpent += dailyPlan.DailyBudgetStatus.Spent;
                    rolloverBudget = dailyLimit - dailyPlan.DailyBudgetStatus.Spent;
                    dayCounter++;
                }
            }

            output.TripSummary = new TripSummary
            {
                TotalEstimatedCost = Math.Round(totalSpent, 0),
                RemainingContingencyFund = Math.Round(contingencyFund, 0)
            };

            return output;
        }

        private void ProcessAttraction(BestAttraction bestAttraction, ref TimeSpan currentTime, string block, DailyItinerary dailyPlan, int groupSize, double dailyLimit, HashSet<int> visitedIds, ref double currentLat, ref double currentLon)
        {
            var transport = OptimizeTransport(bestAttraction.Distance, groupSize);
            double stepCost = transport.TotalCost + bestAttraction.Location.AverageBudget * groupSize;

            TimeSpan arrivalTime = currentTime.Add(TimeSpan.FromMinutes(transport.TravelTimeMinutes));
            dailyPlan.Timeline.Add(new TimelineItem
            {
                Type = "Transport",
                Time = $"{currentTime:hh\\:mm} - {arrivalTime:hh\\:mm}",
                TimeBlock = block,
                Description = $"{transport.Description} to {bestAttraction.Location.Name}",
                Cost = Math.Round(transport.TotalCost, 0)
            });

            double actualStayTimeMinutes = bestAttraction.Location.AverageStayDuration * (1 + 0.05 * (groupSize - 2));
            TimeSpan visitEndTime = arrivalTime.Add(TimeSpan.FromMinutes(actualStayTimeMinutes));

            dailyPlan.Timeline.Add(new TimelineItem
            {
                Type = "Visit",
                Time = $"{arrivalTime:hh\\:mm} - {visitEndTime:hh\\:mm}",
                TimeBlock = block,
                Description = $"Visit {bestAttraction.Location.Name}",
                TicketCost = Math.Round(bestAttraction.Location.AverageBudget * groupSize, 0),
                GroupDiscountApplied = groupSize >= 5
            });

            dailyPlan.DailyBudgetStatus.Spent += stepCost;
            visitedIds.Add(bestAttraction.Location.Id);
            currentLat = bestAttraction.Location.Latitude;
            currentLon = bestAttraction.Location.Longitude;
            currentTime = visitEndTime;
        }

        private bool IsEveningActivity(Location loc)
        {
            var eveningTags = new[] { "Food", "Coffee", "Local Life", "Entertainment", "Shopping", "Relax", "View", "Nightlife" };
            var heavySightseeingTags = new[] { "Sightseeing", "History", "Culture", "Museum", "Architecture", "Religion", "Art" };

            bool hasEveningTag = loc.Tags.Any(t => eveningTags.Contains(t, StringComparer.OrdinalIgnoreCase));
            bool hasHeavyTag = loc.Tags.Any(t => heavySightseeingTags.Contains(t, StringComparer.OrdinalIgnoreCase));

            return hasEveningTag && !hasHeavyTag;
        }

        // New Helper: Determine Visiting Order (Greedy Nearest Neighbor)
        private List<string> DetermineBestVisitingOrder(List<string> destinations, List<ScoredLocation> candidates, double startLat, double startLon)
        {
            var normalizedDestinations = destinations.Select(d => d.Equals("Ho Chi Minh City", StringComparison.OrdinalIgnoreCase) ? "HCMC" : d).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var remaining = normalizedDestinations;
            var ordered = new List<string>();
            double currentLat = startLat;
            double currentLon = startLon;

            while (remaining.Any())
            {
                var nextDest = remaining
                    .Select(d => {
                        var center = GetDestinationCenter(candidates.Where(c => c.Location.Destination.Equals(d, StringComparison.OrdinalIgnoreCase)).ToList());
                        return new { Destination = d, Distance = CalculateDistance(currentLat, currentLon, center.Lat, center.Lon), Center = center };
                    })
                    .OrderBy(x => x.Distance)
                    .FirstOrDefault();

                if (nextDest == null) break;

                ordered.Add(nextDest.Destination);
                remaining.Remove(nextDest.Destination);
                currentLat = nextDest.Center.Lat;
                currentLon = nextDest.Center.Lon;
            }

            return ordered;
        }

        // New Helper: Allocate Days proportionally to attraction counts
        private Dictionary<string, int> AllocateDaysToDestinations(List<string> orderedDestinations, List<ScoredLocation> candidates, int totalDays)
        {
            var counts = orderedDestinations.ToDictionary(d => d, d => candidates.Count(c => c.Location.Destination.Equals(d, StringComparison.OrdinalIgnoreCase)));
            int totalAttractions = counts.Values.Sum();
            
            if (totalAttractions == 0) return orderedDestinations.ToDictionary(d => d, d => (int)Math.Max(1, totalDays / (double)orderedDestinations.Count));

            var allocation = new Dictionary<string, int>();
            int assignedDays = 0;

            foreach (var dest in orderedDestinations)
            {
                int days = (int)Math.Max(1, Math.Round((double)counts[dest] / totalAttractions * totalDays));
                allocation[dest] = days;
                assignedDays += days;
            }

            // Adjust if rounding error causes deviation from totalDays
            if (assignedDays != totalDays && orderedDestinations.Any())
            {
                string lastDest = orderedDestinations.Last();
                allocation[lastDest] = Math.Max(1, allocation[lastDest] + (totalDays - assignedDays));
            }

            return allocation;
        }

        private (double Lat, double Lon) GetDestinationCenter(List<ScoredLocation> destinationCandidates)
        {
            if (!destinationCandidates.Any()) return (21.0285, 105.8522); // Default Hanoi
            var top = destinationCandidates.OrderByDescending(c => c.Score).Take(5).ToList();
            return (top.Average(c => c.Location.Latitude), top.Average(c => c.Location.Longitude));
        }

        // New Helper: Inter-city transportation recommendations
        private TransportOptimization GetInterCityTransport(double distance, int groupSize)
        {
            if (distance > 600)
            {
                return new TransportOptimization {
                    Description = "Airplane",
                    TotalCost = 2000000 * groupSize,
                    TravelTimeMinutes = 120 + 120 // 2h flight + 2h check-in/travel
                };
            }
            else if (distance >= 200)
            {
                return new TransportOptimization {
                    Description = "Train",
                    TotalCost = 500000 * groupSize,
                    TravelTimeMinutes = (distance / 60.0) * 60.0
                };
            }
            else
            {
                return new TransportOptimization {
                    Description = "Bus / Coach",
                    TotalCost = 200000 * groupSize,
                    TravelTimeMinutes = (distance / 45.0) * 60.0
                };
            }
        }

        // Updated FilterAndScoreLocations to handle list of destinations
        private List<ScoredLocation> FilterAndScoreLocations(List<Location> allLocations, List<string> destinations, List<string> favoriteTags)
        {
            var normalizedDestinations = destinations.Select(d => d.Equals("Ho Chi Minh City", StringComparison.OrdinalIgnoreCase) ? "HCMC" : d).ToList();
            return allLocations
                .Where(l => normalizedDestinations.Contains(l.Destination, StringComparer.OrdinalIgnoreCase))
                .Select(l => new ScoredLocation
                {
                    Location = l,
                    Score = favoriteTags == null ? 1 : l.Tags.Intersect(favoriteTags, StringComparer.OrdinalIgnoreCase).Count() + 1
                })
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

        private BestAttraction FindNextBestAttraction(
            double lat, 
            double lon, 
            List<ScoredLocation> candidates, 
            HashSet<int> visitedIds, 
            TimeSpan currentTime, 
            DayOfWeek dayOfWeek, 
            TimeSpan dayEndTime,
            int groupSize,
            double remainingDailyBudget,
            bool isEvening)
        {
            // 2.1 Adaptive Radius Search
            double r = 2.0;
            List<ScoredLocation> nearby = new List<ScoredLocation>();

            while (r <= 15.0)
            {
                nearby = candidates
                    .Where(c => !visitedIds.Contains(c.Location.Id))
                    .Where(c => !isEvening || IsEveningActivity(c.Location)) // If evening, only allow evening activities
                    .Where(c => isEvening || !IsEveningActivity(c.Location) || c.Location.Tags.Contains("Relax", StringComparer.OrdinalIgnoreCase)) // If daytime, allow anything EXCEPT evening-only (except relax)
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
