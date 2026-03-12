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
            // MODULE 3.1 - Budget Partitioning (NEW DYNAMIC MODEL)
            // Step 2.1 - Reserve Fund
            double contingencyFund = request.TotalBudget * 0.1;
            double usableBudget = request.TotalBudget * 0.9;

            // Filter all locations by requested destinations
            var allLocations = GetAllLocations();
            var candidateLocations = FilterAndScoreLocations(allLocations, request.Destinations, request.UserFavoriteTags);

            // Determine best visiting order of destinations (optimize travel distance)
            var orderedDestinations = DetermineBestVisitingOrder(request.Destinations, candidateLocations, request.StartLatitude ?? 21.0285, request.StartLongitude ?? 105.8522);

            // Determine how many days in each destination based on attraction count and importance
            int totalDays = (request.EndDate - request.StartDate).Days + 1;
            var destinationDayAllocation = AllocateDaysToDestinations(orderedDestinations, candidateLocations, totalDays);

            // Step 2.2 - Assign Day Weights
            var dayWeights = new List<double>();
            for (int i = 0; i < totalDays; i++)
            {
                var date = request.StartDate.AddDays(i);
                double w = 1.0; // Base weight for Weekdays
                if (date.DayOfWeek == DayOfWeek.Friday || date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday)
                    w = 1.5; // Weekend effect
                
                if (i == 0) w += 0.2; // First day
                if (i == totalDays - 1) w += 0.3; // Last day
                
                dayWeights.Add(w);
            }

            // Step 2.3 - Calculate Daily Limits
            double totalWeights = dayWeights.Sum();
            double baseLimit = usableBudget / totalWeights;
            var dailyActivityBudgets = dayWeights.Select(w => baseLimit * w).ToList();

            // Estimate accommodation cost per destination to guide search
            var destinationHotelCosts = new Dictionary<string, double>();
            foreach (var dest in destinationDayAllocation.Keys)
            {
                var destCandidates = candidateLocations.Where(c => c.Location.Destination.Equals(dest, StringComparison.OrdinalIgnoreCase)).ToList();
                destinationHotelCosts[dest] = EstimateAccommodationCost(destCandidates, request.GroupSize);
            }

            var output = new SmartItineraryOutput();
            var visitedIds = new HashSet<int>();
            double totalSpent = 0;
            double rolloverBudget = 0;

            double currentLat = request.StartLatitude ?? 21.0285;
            double currentLon = request.StartLongitude ?? 105.8522;

            int dayCounter = 0;
            string currentDestination = null;
            Location currentHotel = null;

            foreach (var destAlloc in destinationDayAllocation)
            {
                string destinationName = destAlloc.Key;
                int daysInThisDest = destAlloc.Value;
                var destCandidates = candidateLocations.Where(c => c.Location.Destination.Equals(destinationName, StringComparison.OrdinalIgnoreCase)).ToList();

                for (int d = 0; d < daysInThisDest; d++)
                {
                    if (dayCounter >= totalDays) break;

                    var currentDate = request.StartDate.AddDays(dayCounter);

                    // Daily limit (Step 2.3 & 2.4)
                    double totalDailyLimit = dailyActivityBudgets[dayCounter] + rolloverBudget;
                    bool needHotelTonight = d < daysInThisDest - 1 || destAlloc.Key != destinationDayAllocation.Last().Key;
                    double accommodationBudgetTonight = needHotelTonight ? destinationHotelCosts[destinationName] : 0;

                    var dailyPlan = new DailyItinerary
                    {
                        Day = $"Day {dayCounter + 1} – {destinationName}",
                        DailyBudgetStatus = new DailyBudgetStatus { Limit = Math.Round(totalDailyLimit, 0), Spent = 0 }
                    };

                    TimeSpan currentTime = MorningStart;

                    // Daily limit passed down to search functions
                    double dailyLimit = totalDailyLimit;

                    // Check if moving to a new destination
                    if (currentDestination != destinationName)
                    {
                        currentHotel = null; // Reset hotel when changing cities
                        // Inter-city movement
                        var destCenter = GetDestinationCenter(destCandidates);
                        double distance = CalculateDistance(currentLat, currentLon, destCenter.Lat, destCenter.Lon);

                        if (currentDestination != null) // Don't add transport for the very first city if we are already there
                        {
                            // Get multiple inter-city transport options
                            var transportOptions = GetInterCityTransportOptions(distance, request.GroupSize);
                            var defaultTransport = transportOptions.FirstOrDefault(o => o.Recommended) ?? transportOptions.FirstOrDefault();

                            // Add Transport to timeline
                            TimeSpan transportArrival = currentTime.Add(TimeSpan.FromMinutes(defaultTransport.TravelTimeMinutes));

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
                                Description = $"{defaultTransport.Description} from {currentDestination} to {destinationName} ({Math.Round(distance, 1)} km)",
                                Cost = Math.Round(defaultTransport.TotalCost, 0),
                                TransportOptions = transportOptions,
                                SelectedTransportIndex = transportOptions.IndexOf(defaultTransport)
                            });

                            currentTime = transportArrival;
                            dailyPlan.DailyBudgetStatus.Spent += defaultTransport.TotalCost;
                        }

                        currentLat = destCenter.Lat;
                        currentLon = destCenter.Lon;
                        currentDestination = destinationName;
                    }

                    // --- MORNING BLOCK ---
                    // Stop morning activities with enough time before lunch (minimum 30 min buffer)
                    TimeSpan morningActualEnd = MorningEnd - TimeSpan.FromMinutes(30);
                    while (currentTime < morningActualEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedIds, currentTime, currentDate.DayOfWeek,
                            morningActualEnd, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent, false);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Morning", dailyPlan, request.GroupSize, dailyLimit, visitedIds, ref currentLat, ref currentLon);
                    }

                    // --- LUNCH BREAK ---
                    // Always add lunch break between 12:00 - 13:00
                    if (currentTime < LunchEnd)
                    {
                        if (currentTime < LunchStart) currentTime = LunchStart;

                        var lunchPlace = FindNextBestRestLocation(currentLat, currentLon, destCandidates, new[] { "Restaurant", "LunchRest" }, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent);
                        var cafePlace = FindNextBestRestLocation(currentLat, currentLon, destCandidates, new[] { "Cafe", "Coffee", "RestArea" }, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent);

                        string lunchDesc = lunchPlace != null ? $"Lunch at {lunchPlace.Location.Name}" : "Lunch at local restaurant";
                        string cafeDesc = cafePlace != null ? $"Rest at {cafePlace.Location.Name}" : "Rest at nearby café";

                        // If hotel is very close (within 1km), suggest returning to hotel
                        string hotelOption = "";
                        if (currentHotel != null && CalculateDistance(currentLat, currentLon, currentHotel.Latitude, currentHotel.Longitude) < 1.0)
                        {
                            hotelOption = " - or return to hotel";
                        }

                        dailyPlan.Timeline.Add(new TimelineItem
                        {
                            Type = "Rest",
                            Time = $"{LunchStart:hh\\:mm} - {LunchEnd:hh\\:mm}",
                            TimeBlock = "Lunch Break",
                            Description = $"Lunch: {lunchDesc}{hotelOption} | Optional: {cafeDesc}"
                        });

                        if (lunchPlace != null) dailyPlan.DailyBudgetStatus.Spent += lunchPlace.Location.AverageBudget * request.GroupSize;
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
                    // Allow evening activities to go up to 21:30 or 22:00 if budget allows
                    TimeSpan eveningActualEnd = new TimeSpan(22, 0, 0);
                    while (currentTime < eveningActualEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedIds, currentTime, currentDate.DayOfWeek, 
                            eveningActualEnd, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent, true);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Evening", dailyPlan, request.GroupSize, dailyLimit, visitedIds, ref currentLat, ref currentLon);
                    }

                    // --- NIGHT REST ---
                    TimeSpan nightStart = currentTime > EveningEnd ? (currentTime > eveningActualEnd ? eveningActualEnd : currentTime) : EveningEnd;
                    TimeSpan nightEnd = new TimeSpan(8, 0, 0); // Next day morning

                    // Determine search center for accommodation (proximity to current location and next day's potential attractions)
                    double searchLat = currentLat;
                    double searchLon = currentLon;

                    // Peek into next day's attractions if in the same city
                    if (d < daysInThisDest - 1)
                    {
                        var remainingCandidates = destCandidates.Where(c => !visitedIds.Contains(c.Location.Id)).ToList();
                        if (remainingCandidates.Any())
                        {
                            var nextDayCenter = GetDestinationCenter(remainingCandidates);
                            // Weight current location more heavily but consider next day
                            searchLat = (currentLat * 0.7) + (nextDayCenter.Lat * 0.3);
                            searchLon = (currentLon * 0.7) + (nextDayCenter.Lon * 0.3);
                        }
                    }

                    // Find accommodation using smart multi-criteria search
                    // Check if we need to find a new hotel (none set, or current is too far >8km, or significantly better option exists)
                    bool needNewHotel = currentHotel == null || 
                        CalculateDistance(searchLat, searchLon, currentHotel.Latitude, currentHotel.Longitude) > 8.0;
                    
                    if (needNewHotel)
                    {
                        // Use the accommodation budget allocated for tonight
                        var accommodation = FindNextBestAccommodation(
                            currentLat, 
                            currentLon, 
                            destCandidates, 
                            request.GroupSize, 
                            accommodationBudgetTonight,
                            currentHotel,
                            searchLat,
                            searchLon);
                        
                        if (accommodation != null)
                        {
                            currentHotel = accommodation.Location;
                        }
                    }

                    if (currentHotel != null)
                    {
                        double hotelCost = currentHotel.AverageBudget * request.GroupSize;
                        dailyPlan.Timeline.Add(new TimelineItem
                        {
                            Type = "Rest",
                            Time = $"{nightStart:hh\\:mm} - {nightEnd:hh\\:mm}",
                            TimeBlock = "Night Rest",
                            Description = $"Accommodation: {currentHotel.Name} | Cost: {Math.Round(hotelCost, 0):N0} VND/night"
                        });
                        dailyPlan.DailyBudgetStatus.Spent += hotelCost;

                        // Update current position to hotel for the start of next day
                        currentLat = currentHotel.Latitude;
                        currentLon = currentHotel.Longitude;
                    }

                    dailyPlan.DailyBudgetStatus.Spent = Math.Round(dailyPlan.DailyBudgetStatus.Spent, 0);
                    output.Days.Add(dailyPlan);
                    totalSpent += dailyPlan.DailyBudgetStatus.Spent;
                    
                    // Step 2.4 - Rollover Logic
                    rolloverBudget = totalDailyLimit - dailyPlan.DailyBudgetStatus.Spent;
                    
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
            // Get multiple transport options for user to choose
            var transportOptions = GetTransportOptions(bestAttraction.Distance, groupSize);
            var defaultTransport = transportOptions.FirstOrDefault(o => o.Recommended) ?? transportOptions.FirstOrDefault();
            
            double stepCost = defaultTransport.TotalCost + bestAttraction.Location.AverageBudget * groupSize;

            TimeSpan arrivalTime = currentTime.Add(TimeSpan.FromMinutes(defaultTransport.TravelTimeMinutes));
            
            dailyPlan.Timeline.Add(new TimelineItem
            {
                Type = "Transport",
                Time = $"{currentTime:hh\\:mm} - {arrivalTime:hh\\:mm}",
                TimeBlock = block,
                Description = $"{defaultTransport.Description} to {bestAttraction.Location.Name}",
                Cost = Math.Round(defaultTransport.TotalCost, 0),
                TransportOptions = transportOptions,
                SelectedTransportIndex = transportOptions.IndexOf(defaultTransport)
            });

            double actualStayTimeMinutes = bestAttraction.Location.AverageStayDuration * (1 + 0.05 * Math.Max(0, groupSize - 2));
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

        // New Helper: Inter-city transportation recommendations (returns multiple options)
        private List<TransportOption> GetInterCityTransportOptions(double distance, int groupSize)
        {
            var options = new List<TransportOption>();

            // Bus/Coach option (for distances < 500km)
            if (distance < 500)
            {
                double busCost = 200000 * groupSize;
                double busTime = (distance / 45.0) * 60.0 * (1 + 0.05 * Math.Max(0, groupSize - 2));
                options.Add(new TransportOption
                {
                    Method = "Bus/Coach",
                    Description = "Bus / Coach",
                    TotalCost = busCost,
                    TravelTimeMinutes = busTime,
                    VehiclesNeeded = 1,
                    Pros = "Most economical, direct route",
                    Cons = "Slower, less comfortable for long distances",
                    Recommended = distance < 200 && groupSize <= 16,
                    GroupSize = groupSize
                });
            }

            // Train option (for distances 150-800km)
            if (distance >= 150 && distance <= 800)
            {
                double trainCost = 500000 * groupSize;
                double trainTime = (distance / 60.0) * 60.0 * (1 + 0.05 * Math.Max(0, groupSize - 2));
                options.Add(new TransportOption
                {
                    Method = "Train",
                    Description = "Train",
                    TotalCost = trainCost,
                    TravelTimeMinutes = trainTime,
                    VehiclesNeeded = 1,
                    Pros = "Comfortable, scenic views, can move around",
                    Cons = "Fixed schedule, may be delayed",
                    Recommended = (distance >= 200 && distance <= 400) || groupSize > 7,
                    GroupSize = groupSize
                });
            }

            // Airplane option (for distances > 400km)
            if (distance > 400)
            {
                double flightCost = 2000000 * groupSize;
                double flightTime = (120 + 120) * (1 + 0.05 * Math.Max(0, groupSize - 2)); // 2h flight + 2h check-in/travel
                options.Add(new TransportOption
                {
                    Method = "Airplane",
                    Description = "Airplane",
                    TotalCost = flightCost,
                    TravelTimeMinutes = flightTime,
                    VehiclesNeeded = 1,
                    Pros = "Fastest for long distances, most comfortable",
                    Cons = "Most expensive, airport transfers needed",
                    Recommended = distance > 600,
                    GroupSize = groupSize
                });
            }

            // Private Van option (for groups <= 16 and distances < 300km)
            if (groupSize <= 16 && distance < 300)
            {
                int vansNeeded = (int)Math.Ceiling(groupSize / 16.0);
                double vanCost = vansNeeded * 35000 * distance;
                double vanTime = (distance / 50.0) * 60.0 * (1 + 0.05 * Math.Max(0, groupSize - 2));
                options.Add(new TransportOption
                {
                    Method = "Private Van",
                    Description = $"{vansNeeded} x 16-seat van",
                    TotalCost = vanCost,
                    TravelTimeMinutes = vanTime,
                    VehiclesNeeded = vansNeeded,
                    Pros = "Flexible schedule, door-to-door, group stays together",
                    Cons = "Driver fatigue on long trips",
                    Recommended = (groupSize > 7 && groupSize <= 16) && distance < 200,
                    GroupSize = groupSize
                });
            }

            // Sort by cost per person
            var sortedOptions = options.OrderBy(o => o.TotalCost / Math.Max(groupSize, 1)).ToList();

            // Mark the most suitable option as recommended if none already
            if (sortedOptions.Any(o => o.Recommended) == false && sortedOptions.Any())
            {
                // Recommend based on distance and group size
                if (distance > 600)
                    sortedOptions.FirstOrDefault(o => o.Method == "Airplane")!.Recommended = true;
                else if (distance >= 200 && distance <= 400)
                    sortedOptions.FirstOrDefault(o => o.Method == "Train")!.Recommended = true;
                else
                    sortedOptions[0].Recommended = true;
            }

            return sortedOptions;
        }

        // Backward compatible method
        private TransportOptimization GetInterCityTransport(double distance, int groupSize)
        {
            var options = GetInterCityTransportOptions(distance, groupSize);
            var best = options.FirstOrDefault(o => o.Recommended) ?? options.FirstOrDefault();

            if (best == null)
            {
                return new TransportOptimization
                {
                    Description = "Bus / Coach",
                    TotalCost = 200000 * groupSize,
                    TravelTimeMinutes = (distance / 45.0) * 60.0 * (1 + 0.05 * Math.Max(0, groupSize - 2))
                };
            }

            return new TransportOptimization
            {
                Description = best.Description,
                TotalCost = best.TotalCost,
                TravelTimeMinutes = best.TravelTimeMinutes
            };
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

        // Estimate average accommodation cost per night based on available hotels
        private double EstimateAccommodationCost(List<ScoredLocation> candidates, int groupSize)
        {
            var accommodations = candidates
                .Where(c => c.Location.Tags.Any(t =>
                    new[] { "Hotel", "Guesthouse", "Hostel", "Homestay", "Accommodation" }
                    .Contains(t, StringComparer.OrdinalIgnoreCase)))
                .Select(c => new { Location = c.Location, CostPerNight = c.Location.AverageBudget * groupSize })
                .OrderBy(x => x.CostPerNight)
                .ToList();

            if (!accommodations.Any())
            {
                // Fallback: estimate 300k per person per night
                return 300000 * groupSize;
            }

            // For large groups, prioritize budget-friendly options
            // Choose from the cheaper half to ensure affordability
            int budgetOptionCount = Math.Max(1, accommodations.Count / 2);
            var budgetOptions = accommodations.Take(budgetOptionCount);

            // Return average of budget-friendly options
            return budgetOptions.Average(x => x.CostPerNight);
        }

        // Calculate total inter-city transport budget
        private double CalculateInterCityTransportBudget(List<string> orderedDestinations, List<ScoredLocation> candidates, int groupSize, double startLat, double startLon)
        {
            double totalBudget = 0;
            double currentLat = startLat;
            double currentLon = startLon;

            foreach (var dest in orderedDestinations)
            {
                var destCenter = GetDestinationCenter(candidates.Where(c => c.Location.Destination.Equals(dest, StringComparison.OrdinalIgnoreCase)).ToList());
                double distance = CalculateDistance(currentLat, currentLon, destCenter.Lat, destCenter.Lon);
                var transport = GetInterCityTransport(distance, groupSize);
                totalBudget += transport.TotalCost;
                
                currentLat = destCenter.Lat;
                currentLon = destCenter.Lon;
            }

            return totalBudget;
        }

        // Allocate activity budget to each destination based on days and attraction count
        private Dictionary<string, double> AllocateBudgetToDestinations(Dictionary<string, int> destinationDayAllocation, List<ScoredLocation> allCandidates, double totalActivityBudget)
        {
            var destinationBudgets = new Dictionary<string, double>();
            
            // Calculate weight for each destination based on days and number of attractions
            var destinationWeights = new Dictionary<string, double>();
            foreach (var dest in destinationDayAllocation.Keys)
            {
                int days = destinationDayAllocation[dest];
                int attractionCount = allCandidates.Count(c => c.Location.Destination.Equals(dest, StringComparison.OrdinalIgnoreCase));
                
                // Weight = days * sqrt(attractionCount) to balance time and content
                double weight = days * Math.Max(1, Math.Sqrt(attractionCount));
                destinationWeights[dest] = weight;
            }

            double totalWeight = destinationWeights.Values.Sum();
            
            // Allocate budget proportionally
            foreach (var dest in destinationDayAllocation.Keys)
            {
                double destBudget = (destinationWeights[dest] / totalWeight) * totalActivityBudget;
                destinationBudgets[dest] = destBudget;
            }

            return destinationBudgets;
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
                    double actualStayTime = c.Location.AverageStayDuration * (1 + 0.05 * Math.Max(0, groupSize - 2));
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
        // Returns multiple transport options for user to choose
        private List<TransportOption> GetTransportOptions(double distance, int groupSize)
        {
            var options = new List<TransportOption>();

            // Walking for very short distances
            if (distance < 1.0)
            {
                options.Add(new TransportOption
                {
                    Method = "Walking",
                    Description = "Walking",
                    TotalCost = 0,
                    TravelTimeMinutes = (distance / 4.0) * 60.0 * (1 + 0.05 * Math.Max(0, groupSize - 2)),
                    VehiclesNeeded = 0,
                    Pros = "Free, eco-friendly, good for health",
                    Cons = "Slow, only for short distances",
                    Recommended = true
                });
            }
            else
            {
                // Add Walking as backup for distances up to 2km
                if (distance <= 2.0)
                {
                    options.Add(new TransportOption
                    {
                        Method = "Walking",
                        Description = "Walking",
                        TotalCost = 0,
                        TravelTimeMinutes = (distance / 4.0) * 60.0 * (1 + 0.05 * Math.Max(0, groupSize - 2)),
                        VehiclesNeeded = 0,
                        Pros = "Free, eco-friendly",
                        Cons = "Slow",
                        Recommended = distance <= 1.0
                    });
                }

                // Calculate options for each vehicle type
                foreach (var vehicle in _vehicleTypes.Where(v => !v.IsWalking))
                {
                    int vehiclesNeeded = (int)Math.Ceiling((double)groupSize / vehicle.Capacity);
                    double totalCost = vehiclesNeeded * vehicle.CostPerKm * distance;
                    double travelTimeMinutes = (distance / vehicle.SpeedKmh) * 60.0 * (1 + 0.05 * Math.Max(0, groupSize - 2));

                    string pros = "";
                    string cons = "";
                    bool recommended = false;

                    // Determine pros/cons based on vehicle type
                    if (vehicle.Name.Contains("Taxi"))
                    {
                        pros = "Fast, comfortable, door-to-door";
                        cons = "More expensive for large groups";
                        recommended = groupSize <= 4 && distance < 50;
                    }
                    else if (vehicle.Name.Contains("7-seat"))
                    {
                        pros = "Good balance of cost and comfort";
                        cons = "May need multiple vehicles for large groups";
                        recommended = (groupSize > 4 && groupSize <= 7) || (distance >= 10 && distance < 100);
                    }
                    else if (vehicle.Name.Contains("16-seat"))
                    {
                        pros = "Best for large groups, everyone travels together";
                        cons = "Higher total cost, slower speed";
                        recommended = groupSize > 7 || groupSize > 4;
                    }

                    options.Add(new TransportOption
                    {
                        Method = vehicle.Name,
                        Description = $"{vehiclesNeeded} x {vehicle.Name}",
                        TotalCost = totalCost,
                        TravelTimeMinutes = travelTimeMinutes,
                        VehiclesNeeded = vehiclesNeeded,
                        Pros = pros,
                        Cons = cons,
                        Recommended = recommended
                    });
                }
            }

            // Sort by cost per person (best value first)
            var sortedOptions = options.OrderBy(o => o.TotalCost / Math.Max(groupSize, 1)).ToList();

            // Mark the most cost-effective option as recommended if none already
            if (sortedOptions.Any(o => o.Recommended) == false && sortedOptions.Any())
            {
                sortedOptions[0].Recommended = true;
            }

            return sortedOptions;
        }

        // Returns the best transport option (backward compatible)
        private TransportOptimization OptimizeTransport(double distance, int groupSize)
        {
            var options = GetTransportOptions(distance, groupSize);
            var best = options.FirstOrDefault(o => o.Recommended) ?? options.FirstOrDefault();

            if (best == null)
            {
                return new TransportOptimization
                {
                    Description = "Walking",
                    TotalCost = 0,
                    TravelTimeMinutes = 0
                };
            }

            return new TransportOptimization
            {
                Description = best.Description,
                TotalCost = best.TotalCost,
                TravelTimeMinutes = best.TravelTimeMinutes
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

        // Smart Accommodation Search with Multi-Criteria Scoring
        private BestAttraction FindNextBestAccommodation(
            double lat,
            double lon,
            List<ScoredLocation> candidates,
            int groupSize,
            double accommodationBudget,
            Location currentHotel,
            double searchLat,
            double searchLon)
        {
            var accommodationTags = new[] { "Hotel", "Guesthouse", "Hostel", "Homestay", "Accommodation" };
            
            // Filter accommodations within budget
            var accommodations = candidates
                .Where(c => c.Location.Tags.Any(t => accommodationTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                .Where(c => (c.Location.AverageBudget * groupSize) <= accommodationBudget * 1.2) // Allow 20% flexibility
                .Select(c => new
                {
                    Location = c.Location,
                    Distance = CalculateDistance(searchLat, searchLon, c.Location.Latitude, c.Location.Longitude),
                    CostPerNight = c.Location.AverageBudget * groupSize,
                    OriginalScore = c.Score
                })
                .ToList();

            if (!accommodations.Any()) return null;

            // Calculate min/max for normalization
            var minCost = accommodations.Min(a => a.CostPerNight);
            var maxCost = accommodations.Max(a => a.CostPerNight);
            var minDistance = accommodations.Min(a => a.Distance);
            var maxDistance = accommodations.Max(a => a.Distance);
            var maxScore = accommodations.Max(a => a.OriginalScore);

            // Score each accommodation (0-100 scale)
            var scoredAccommodations = accommodations.Select(a =>
            {
                // Distance Score (40% weight): Prefer closer hotels
                double distanceScore = maxDistance == minDistance ? 50 : 
                    100 * (1 - (a.Distance - minDistance) / (maxDistance - minDistance));

                // Price Score (35% weight): Prefer budget-friendly but not necessarily cheapest
                // Optimal price is around 60-80% of budget (good value, not cheap luxury)
                double priceRatio = maxCost == minCost ? 0.5 : (a.CostPerNight - minCost) / (maxCost - minCost);
                double priceScore = 100 * (1 - Math.Abs(priceRatio - 0.6)); // Optimal at 60% of range

                // Quality Score (25% weight): Based on original score (tags matching, etc.)
                double qualityScore = maxScore == 0 ? 50 : 100 * a.OriginalScore / maxScore;

                // Apply weights
                double totalScore = distanceScore * 0.40 + priceScore * 0.35 + qualityScore * 0.25;

                return new
                {
                    a.Location,
                    a.Distance,
                    a.CostPerNight,
                    TotalScore = totalScore
                };
            }).OrderByDescending(a => a.TotalScore);

            // Select top 3 and pick the closest among them
            var topCandidates = scoredAccommodations.Take(3).ToList();
            var bestChoice = topCandidates.OrderBy(a => a.Distance).FirstOrDefault();

            if (bestChoice == null) return null;

            // Check if we should keep current hotel (avoid unnecessary moves)
            // Only move if new hotel is significantly better (>20% score improvement) or current is too far (>3km)
            if (currentHotel != null)
            {
                double currentHotelDistance = CalculateDistance(searchLat, searchLon, currentHotel.Latitude, currentHotel.Longitude);
                double currentHotelCost = currentHotel.AverageBudget * groupSize;
                
                // If current hotel is within 3km and cost is similar, keep it
                if (currentHotelDistance <= 3.0 && Math.Abs(currentHotelCost - bestChoice.CostPerNight) / Math.Max(bestChoice.CostPerNight, 1) <= 0.3)
                {
                    return null; // Signal to keep current hotel
                }
            }

            return new BestAttraction
            {
                Location = bestChoice.Location,
                Distance = bestChoice.Distance
            };
        }

        private BestAttraction FindNextBestRestLocation(
            double lat,
            double lon,
            List<ScoredLocation> candidates,
            string[] tags,
            int groupSize,
            double remainingDailyBudget,
            bool isAccommodation = false)
        {
            // Use smart accommodation search for hotels
            if (isAccommodation)
            {
                // This is a simplified fallback - actual accommodation search uses FindNextBestAccommodation
                var nearby = candidates
                    .Where(c => c.Location.Tags.Intersect(tags, StringComparer.OrdinalIgnoreCase).Any())
                    .Select(c => new { ScoredLocation = c, Distance = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude) })
                    .Where(x => (x.ScoredLocation.Location.AverageBudget * groupSize) <= remainingDailyBudget * 1.2)
                    .OrderBy(x => x.ScoredLocation.Location.AverageBudget * groupSize)
                    .ThenBy(x => x.Distance)
                    .FirstOrDefault();

                if (nearby == null) return null;

                return new BestAttraction
                {
                    Location = nearby.ScoredLocation.Location,
                    Distance = nearby.Distance
                };
            }

            // For restaurants/cafes: balance between distance and price
            var restLocations = candidates
                .Where(c => c.Location.Tags.Intersect(tags, StringComparer.OrdinalIgnoreCase).Any())
                .Select(c => new
                {
                    ScoredLocation = c,
                    Distance = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude),
                    Cost = c.Location.AverageBudget * groupSize
                })
                .Where(x => x.Distance <= 2.0) // Within 2km
                .Where(x => x.Cost <= remainingDailyBudget * 2.5)
                .ToList();

            if (!restLocations.Any()) return null;

            // Simple scoring: prefer closer and reasonably priced
            var bestRest = restLocations
                .OrderBy(x => x.Distance * 0.6 + (x.Cost / Math.Max(remainingDailyBudget, 1)) * 0.4)
                .FirstOrDefault();

            if (bestRest == null) return null;

            return new BestAttraction
            {
                Location = bestRest.ScoredLocation.Location,
                Distance = bestRest.Distance
            };
        }

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
