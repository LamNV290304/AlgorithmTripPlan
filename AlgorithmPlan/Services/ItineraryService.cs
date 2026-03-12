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
        private readonly string _dataProvincePath = Path.Combine(AppContext.BaseDirectory, "data_province");

        // Vehicle capacities and costs per km (examples)
        private readonly List<VehicleType> _vehicleTypes = new List<VehicleType>
        {
            new VehicleType { Name = "Walking", Capacity = 100, CostPerKm = 0, SpeedKmh = 4, IsWalking = true },
            new VehicleType { Name = "Taxi 4-seat", Capacity = 4, CostPerKm = 15000, SpeedKmh = 30 },
            new VehicleType { Name = "7-seat vehicle", Capacity = 7, CostPerKm = 20000, SpeedKmh = 30 },
            new VehicleType { Name = "16-seat van", Capacity = 16, CostPerKm = 35000, SpeedKmh = 25 }
        };

        // Time delay buffers based on transport mode (research-based)
        // Source: IATA recommends arriving 2h before domestic, 3h before international flights
        // Train stations: 30-45 min buffer, Bus stations: 15-20 min buffer
        private readonly double _flightDelayBuffer = 120; // minutes
        private readonly double _trainDelayBuffer = 45; // minutes
        private readonly double _busDelayBuffer = 20; // minutes
        private readonly double _generalDelayBuffer = 15; // minutes for local transport

        private static readonly TimeSpan DayStart = new TimeSpan(0, 0, 0);
        private static readonly TimeSpan MorningStart = new TimeSpan(7, 0, 0);
        private static readonly TimeSpan MorningEnd = new TimeSpan(12, 0, 0);
        private static readonly TimeSpan LunchStart = new TimeSpan(12, 0, 0);
        private static readonly TimeSpan LunchEnd = new TimeSpan(13, 0, 0);
        private static readonly TimeSpan AfternoonStart = new TimeSpan(13, 0, 0);
        private static readonly TimeSpan AfternoonEnd = new TimeSpan(18, 0, 0);
        private static readonly TimeSpan EveningStart = new TimeSpan(18, 0, 0);
        private static readonly TimeSpan EveningEnd = new TimeSpan(22, 0, 0);

        public List<Location> GetAllLocations()
        {
            if (!File.Exists(_dataPath)) return new List<Location>();
            var json = File.ReadAllText(_dataPath);
            return JsonSerializer.Deserialize<List<Location>>(json);
        }

        public List<Location> LoadLocationsFromProvinceData()
        {
            var allLocations = new List<Location>();
            
            if (!Directory.Exists(_dataProvincePath))
            {
                return GetAllLocations(); // Fallback to data.json
            }

            var jsonFiles = Directory.GetFiles(_dataProvincePath, "*.json");
            
            foreach (var file in jsonFiles)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    using var doc = JsonDocument.Parse(json);
                    
                    if (doc.RootElement.TryGetProperty("data", out var dataElement))
                    {
                        // Parse province data and convert to Location objects
                        var province = ParseProvinceData(dataElement);
                        if (province != null)
                        {
                            allLocations.Add(province);
                        }
                    }
                }
                catch (Exception)
                {
                    // Skip invalid files
                }
            }

            // Also load from data.json for additional locations
            var dataJsonLocations = GetAllLocations();
            allLocations.AddRange(dataJsonLocations);

            return allLocations;
        }

        private Location ParseProvinceData(JsonElement dataElement)
        {
            try
            {
                var name = dataElement.GetProperty("name").GetString();
                var latitude = dataElement.GetProperty("latitude").GetDouble();
                var longitude = dataElement.GetProperty("longitude").GetDouble();
                var englishName = dataElement.GetProperty("english_name").GetString();

                // Extract airports as transportation points
                var tags = new List<string> { "Province", "Destination" };
                if (dataElement.TryGetProperty("airports", out var airports))
                {
                    if (airports.GetArrayLength() > 0)
                    {
                        tags.Add("Airport");
                        tags.Add("Transport");
                    }
                }

                return new Location
                {
                    Id = int.Parse(dataElement.GetProperty("id").GetString()),
                    Name = name,
                    Description = $"{name} ({englishName})",
                    Latitude = latitude,
                    Longitude = longitude,
                    Destination = name,
                    Tags = tags,
                    AverageBudget = 0,
                    AverageStayDuration = 0,
                    OpeningHours = new List<OpeningHours>()
                };
            }
            catch
            {
                return null;
            }
        }

        public SmartItineraryOutput GenerateSmartItinerary(ItineraryRequest request)
        {
            // Load locations from data_province
            var allLocations = LoadLocationsFromProvinceData();
            
            // MODULE 3.0 - Validate Destinations
            var validationError = ValidateDestinations(request.Destinations, allLocations);
            if (validationError != null)
            {
                return new SmartItineraryOutput
                {
                    TripSummary = new TripSummary
                    {
                        IsBudgetInsufficient = false,
                        BudgetWarning = validationError,
                        TotalEstimatedCost = 0,
                        RemainingContingencyFund = 0,
                        ContingencyFundPercentage = 0,
                        MinimumRecommendedBudget = 0
                    }
                };
            }

            // MODULE 3.1 - Dynamic Budget Partitioning
            // Contingency fund: 5-20% based on budget level
            double contingencyPercentage = CalculateContingencyPercentage(request.TotalBudget);
            double contingencyFund = request.TotalBudget * (contingencyPercentage / 100.0);
            double usableBudget = request.TotalBudget - contingencyFund;

            var candidateLocations = FilterAndScoreLocations(allLocations, request.Destinations, request.UserFavoriteTags);

            // Determine best visiting order of destinations
            var orderedDestinations = DetermineBestVisitingOrder(
                request.Destinations,
                candidateLocations,
                request.StartLatitude ?? 21.0285,
                request.StartLongitude ?? 105.8522
            );

            // Determine days allocation
            int totalDays = (request.EndDate - request.StartDate).Days + 1;
            var destinationDayAllocation = AllocateDaysToDestinations(orderedDestinations, candidateLocations, totalDays);

            // MODULE 3.2 - Calculate transport and accommodation costs
            double totalTransportBudget = CalculateInterCityTransportBudget(
                orderedDestinations, 
                candidateLocations, 
                request.GroupSize, 
                request.StartLatitude ?? 21.0285, 
                request.StartLongitude ?? 105.8522
            );

            // Estimate accommodation costs
            var destinationHotelCosts = new Dictionary<string, double>();
            double totalAccommodationBudget = 0;

            foreach (var dest in destinationDayAllocation.Keys)
            {
                int nightsInDest = Math.Max(0, destinationDayAllocation[dest] - 1);
                var destCandidates = candidateLocations.Where(c => 
                    c.Location.Destination.Equals(dest, StringComparison.OrdinalIgnoreCase)).ToList();
                double avgHotelCostPerNight = EstimateAccommodationCost(destCandidates, request.GroupSize);
                destinationHotelCosts[dest] = avgHotelCostPerNight;
                totalAccommodationBudget += avgHotelCostPerNight * nightsInDest;
            }

            // Activity budget
            double activityBudget = usableBudget - totalTransportBudget - totalAccommodationBudget;
            
            // Check if budget is insufficient
            double minimumRequiredBudget = totalTransportBudget + totalAccommodationBudget + (totalDays * 500000 * request.GroupSize);
            bool isBudgetInsufficient = request.TotalBudget < minimumRequiredBudget * 0.7;

            if (activityBudget < 0) 
            {
                activityBudget = usableBudget * 0.4;
            }

            // Allocate activity budgets per destination
            var destinationActivityBudgets = AllocateBudgetToDestinations(destinationDayAllocation, candidateLocations, activityBudget);

            // Create daily budgets with ceiling/floor values and front-loaded spending
            var dailyBudgets = CreateDailyBudgetsWithWeights(
                destinationDayAllocation, 
                destinationActivityBudgets, 
                destinationHotelCosts,
                totalDays,
                request.TotalBudget
            );

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
                var destCandidates = candidateLocations.Where(c => 
                    c.Location.Destination.Equals(destinationName, StringComparison.OrdinalIgnoreCase)).ToList();

                for (int d = 0; d < daysInThisDest; d++)
                {
                    if (dayCounter >= totalDays) break;

                    var currentDate = request.StartDate.AddDays(dayCounter);
                    var dailyBudgetInfo = dailyBudgets[dayCounter];
                    
                    double dailyLimit = dailyBudgetInfo.Limit + rolloverBudget;
                    bool needHotelTonight = d < daysInThisDest - 1 || destAlloc.Key != destinationDayAllocation.Last().Key;

                    double accommodationBudgetTonight = needHotelTonight ? destinationHotelCosts[destinationName] : 0;
                    double totalDailyLimit = dailyLimit + accommodationBudgetTonight;

                    var dailyPlan = new DailyItinerary
                    {
                        Day = $"Day {dayCounter + 1} – {destinationName}",
                        DailyBudgetStatus = new DailyBudgetStatus 
                        { 
                            Limit = Math.Round(totalDailyLimit, 0), 
                            Spent = 0,
                            Ceiling = Math.Round(dailyBudgetInfo.Ceiling + accommodationBudgetTonight, 0),
                            Floor = Math.Round(dailyBudgetInfo.Floor, 0),
                            Weight = dailyBudgetInfo.Weight
                        }
                    };

                    TimeSpan currentTime = MorningStart;

                    // Handle inter-city movement and Hotel Check-in/out
                    if (currentDestination != destinationName)
                    {
                        if (currentDestination != null)
                        {
                            // 1. Standalone Hotel Check-out (from previous city hotel)
                            if (currentHotel != null)
                            {
                                double actualCheckoutDuration = 30 * (1 + 0.05 * (request.GroupSize - 2));
                                if (actualCheckoutDuration < 15) actualCheckoutDuration = 15;
                                
                                TimeSpan checkoutEnd = currentTime.Add(TimeSpan.FromMinutes(actualCheckoutDuration));
                                dailyPlan.Timeline.Add(new TimelineItem
                                {
                                    Type = "HotelCheckOut",
                                    Time = $"{FormatTime(currentTime)} - {FormatTime(checkoutEnd)}",
                                    TimeBlock = "Morning",
                                    Description = $"Hotel Check-out: {currentHotel.Name}",
                                    Action = "CheckOut"
                                });
                                currentTime = checkoutEnd;
                            }

                            // 2. Local Transfer: Hotel to Departure Terminal
                            double distanceToTerminal = 15.0; // Estimated 15km to hub
                            var localToTerminalOptions = GetTransportOptions(distanceToTerminal, request.GroupSize);
                            var toTerminalTransport = localToTerminalOptions.FirstOrDefault(o => o.Recommended) ?? localToTerminalOptions.FirstOrDefault();
                            
                            TimeSpan toTerminalEnd = currentTime.Add(TimeSpan.FromMinutes(toTerminalTransport.TravelTimeMinutes + 10));
                            
                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "Transport",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(toTerminalEnd)}",
                                TimeBlock = "Morning",
                                Description = $"Local Transfer: {toTerminalTransport.Description} from Hotel to Departure Terminal",
                                TransportOptions = localToTerminalOptions,
                                SelectedTransportIndex = localToTerminalOptions.IndexOf(toTerminalTransport),
                                Cost = toTerminalTransport.TotalCost
                            });
                            currentTime = toTerminalEnd;
                            dailyPlan.DailyBudgetStatus.Spent += toTerminalTransport.TotalCost;

                            // 3. Terminal Waiting
                            var destCenter = GetDestinationCenter(destCandidates);
                            double distance = CalculateDistance(currentLat, currentLon, destCenter.Lat, destCenter.Lon);
                            var mainJourneyOptions = GetInterCityTransportOptions(distance, request.GroupSize);
                            var mainJourneyTransport = mainJourneyOptions.FirstOrDefault(o => o.Recommended) ?? mainJourneyOptions.FirstOrDefault();
                            
                            double waitingTime = GetDelayBufferForTransport(mainJourneyTransport?.Method ?? "");
                            waitingTime = Math.Max(waitingTime, 90); 
                            
                            TimeSpan waitingEnd = currentTime.Add(TimeSpan.FromMinutes(waitingTime));
                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "Waiting",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(waitingEnd)}",
                                TimeBlock = "Morning",
                                Description = $"Terminal Waiting: {mainJourneyTransport?.Method} Boarding & Security buffer"
                            });
                            currentTime = waitingEnd;

                            // 4. Main Inter-City Journey
                            TimeSpan journeyEnd = currentTime.Add(TimeSpan.FromMinutes(mainJourneyTransport.TravelTimeMinutes));

                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "Transport",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(journeyEnd)}",
                                TimeBlock = currentTime < LunchStart ? "Morning" : "Afternoon",
                                Description = $"{mainJourneyTransport.Description} from {currentDestination} to {destinationName} ({Math.Round(distance, 2)} km)",
                                TransportOptions = mainJourneyOptions,
                                SelectedTransportIndex = mainJourneyOptions.IndexOf(mainJourneyTransport),
                                Cost = mainJourneyTransport.TotalCost
                            });

                            currentTime = journeyEnd;
                            dailyPlan.DailyBudgetStatus.Spent += mainJourneyTransport.TotalCost;

                            // 5. Arrival Terminal
                            TimeSpan arrivalTerminalEnd = currentTime.Add(TimeSpan.FromMinutes(15));
                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "Arrival",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(arrivalTerminalEnd)}",
                                TimeBlock = currentTime < LunchStart ? "Morning" : "Afternoon",
                                Description = $"Arrive at {destinationName} Terminal"
                            });
                            currentTime = arrivalTerminalEnd;

                            // 6. Local Transfer: Terminal to New Hotel
                            double distanceToHotel = 10.0; // Estimated 10km to new hotel
                            var terminalToHotelOptions = GetTransportOptions(distanceToHotel, request.GroupSize);
                            var toHotelTransport = terminalToHotelOptions.FirstOrDefault(o => o.Recommended) ?? terminalToHotelOptions.FirstOrDefault();
                            
                            TimeSpan toHotelEnd = currentTime.Add(TimeSpan.FromMinutes(toHotelTransport.TravelTimeMinutes + 10));
                            
                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "Transport",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(toHotelEnd)}",
                                TimeBlock = currentTime < LunchStart ? "Morning" : "Afternoon",
                                Description = $"Local Transfer: {toHotelTransport.Description} from Terminal to Hotel",
                                TransportOptions = terminalToHotelOptions,
                                SelectedTransportIndex = terminalToHotelOptions.IndexOf(toHotelTransport),
                                Cost = toHotelTransport.TotalCost
                            });
                            currentTime = toHotelEnd;
                            dailyPlan.DailyBudgetStatus.Spent += toHotelTransport.TotalCost;
                        }

                        // Set up for new city
                        currentHotel = null;
                        var newDestCenter = GetDestinationCenter(destCandidates);
                        currentLat = newDestCenter.Lat;
                        currentLon = newDestCenter.Lon;
                        currentDestination = destinationName;

                        // 7. Standalone Hotel Check-in
                        if (currentHotel == null)
                        {
                            var hotelResult = FindNextBestAccommodationWithDetails(
                                currentLat, currentLon, destCandidates, request.GroupSize, accommodationBudgetTonight, null);
                            if (hotelResult != null) currentHotel = hotelResult.Location;
                        }

                        if (currentHotel != null)
                        {
                            double actualCheckinDuration = 30 * (1 + 0.05 * (request.GroupSize - 2));
                            if (actualCheckinDuration < 15) actualCheckinDuration = 15;
                            
                            TimeSpan checkinEnd = currentTime.Add(TimeSpan.FromMinutes(actualCheckinDuration));
                            string checkinDesc = $"Hotel Check-in: {currentHotel.Name}";
                            if (currentTime < new TimeSpan(14, 0, 0)) checkinDesc += " (Drop luggage if room not ready)";

                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "HotelCheckIn",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(checkinEnd)}",
                                TimeBlock = currentTime < LunchStart ? "Morning" : "Afternoon",
                                Description = checkinDesc,
                                Action = "CheckIn"
                            });
                            currentTime = checkinEnd;
                        }
                    }
                    else if (dayCounter == 0) // Day 1, same city
                    {
                        if (currentHotel == null)
                        {
                            var hotelResult = FindNextBestAccommodationWithDetails(
                                currentLat, currentLon, destCandidates, request.GroupSize, accommodationBudgetTonight, null);
                            if (hotelResult != null) currentHotel = hotelResult.Location;
                        }

                        if (currentHotel != null)
                        {
                            double actualCheckinDuration = 30 * (1 + 0.05 * (request.GroupSize - 2));
                            TimeSpan checkinEnd = currentTime.Add(TimeSpan.FromMinutes(actualCheckinDuration));
                            
                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "HotelCheckIn",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(checkinEnd)}",
                                TimeBlock = "Morning",
                                Description = $"Hotel Check-in: {currentHotel.Name}",
                                Action = "CheckIn"
                            });
                            currentTime = checkinEnd;
                        }
                    }


                    // --- MORNING BLOCK (8:00 - 12:00) ---
                    TimeSpan morningActualEnd = MorningEnd - TimeSpan.FromMinutes(30);
                    while (currentTime < morningActualEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedIds, currentTime, currentDate.DayOfWeek,
                            morningActualEnd, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent, false);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Morning", dailyPlan, request.GroupSize, dailyLimit, visitedIds, ref currentLat, ref currentLon);
                    }

                    // Fill gap before lunch if any
                    if (currentTime < LunchStart - TimeSpan.FromMinutes(30))
                    {
                        FillTimeGap(currentTime, LunchStart - TimeSpan.FromMinutes(30), dailyPlan, "Morning", 
                            currentLat, currentLon, destCandidates, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent);
                        currentTime = LunchStart - TimeSpan.FromMinutes(30);
                    }

                    // --- LUNCH BREAK (12:00 - 13:00) ---
                    if (currentTime < LunchEnd)
                    {
                        if (currentTime < LunchStart) currentTime = LunchStart;

                        var lunchPlace = FindNextBestRestLocation(currentLat, currentLon, destCandidates, 
                            new[] { "Restaurant", "LunchRest", "Food" }, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent);
                        var cafePlace = FindNextBestRestLocation(currentLat, currentLon, destCandidates, 
                            new[] { "Cafe", "Coffee", "RestArea" }, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent);

                        string lunchDesc = lunchPlace != null ? $"Lunch at {lunchPlace.Location.Name}" : "Lunch at local restaurant";
                        string cafeDesc = cafePlace != null ? $"Rest at {cafePlace.Location.Name}" : "Rest at nearby café";

                        string hotelOption = "";
                        if (currentHotel != null && CalculateDistance(currentLat, currentLon, currentHotel.Latitude, currentHotel.Longitude) < 1.0)
                        {
                            hotelOption = " - or return to hotel";
                        }

                        dailyPlan.Timeline.Add(new TimelineItem
                        {
                            Type = "Rest",
                            Time = $"{FormatTime(LunchStart)} - {FormatTime(LunchEnd)}",
                            TimeBlock = "Lunch Break",
                            Description = $"Lunch: {lunchDesc}{hotelOption} | Optional: {cafeDesc}"
                        });

                        if (lunchPlace != null) 
                        {
                            dailyPlan.DailyBudgetStatus.Spent += lunchPlace.Location.AverageBudget * request.GroupSize;
                        }
                        currentTime = LunchEnd;
                    }

                    // --- AFTERNOON BLOCK (13:00 - 18:00) ---
                    while (currentTime < AfternoonEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedIds, currentTime, currentDate.DayOfWeek,
                            AfternoonEnd, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent, false);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Afternoon", dailyPlan, request.GroupSize, dailyLimit, visitedIds, ref currentLat, ref currentLon);
                    }

                    // Fill gap before evening if any
                    if (currentTime < EveningStart - TimeSpan.FromMinutes(30))
                    {
                        FillTimeGap(currentTime, EveningStart - TimeSpan.FromMinutes(30), dailyPlan, "Late Afternoon",
                            currentLat, currentLon, destCandidates, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent);
                        currentTime = EveningStart - TimeSpan.FromMinutes(30);
                    }

                    // --- EVENING BLOCK (18:00 - 23:59) ---
                    TimeSpan eveningActualEnd = EveningEnd;
                    while (currentTime < eveningActualEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedIds, currentTime, currentDate.DayOfWeek,
                            eveningActualEnd, request.GroupSize, dailyLimit - dailyPlan.DailyBudgetStatus.Spent, true);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Evening", dailyPlan, request.GroupSize, dailyLimit, visitedIds, ref currentLat, ref currentLon);
                    }

                    // --- STANDALONE SLEEP BLOCK (22:00 - 07:00) ---
                    TimeSpan nightStart = currentTime > EveningEnd ? EveningEnd : currentTime;
                    if (nightStart < EveningStart) nightStart = EveningStart;
                    TimeSpan nightEnd = MorningStart; // Next day morning

                    double searchLat = currentLat;
                    double searchLon = currentLon;

                    if (d < daysInThisDest - 1)
                    {
                        var remainingCandidates = destCandidates.Where(c => !visitedIds.Contains(c.Location.Id)).ToList();
                        if (remainingCandidates.Any())
                        {
                            var nextDayCenter = GetDestinationCenter(remainingCandidates);
                            searchLat = (currentLat * 0.7) + (nextDayCenter.Lat * 0.3);
                            searchLon = (currentLon * 0.7) + (nextDayCenter.Lon * 0.3);
                        }
                    }

                    bool needNewHotel = currentHotel == null ||
                        CalculateDistance(searchLat, searchLon, currentHotel.Latitude, currentHotel.Longitude) > 8.0;

                    List<AccommodationOption> accommodationOptions = null;
                    int selectedAccommodationIndex = 0;
                    List<AlternativeAccommodationDisplay> alternativeAccommodations = null;

                    if (needNewHotel)
                    {
                        var accommodationResult = FindNextBestAccommodationWithDetails(
                            searchLat, searchLon, destCandidates, request.GroupSize, accommodationBudgetTonight, currentHotel);

                        if (accommodationResult != null)
                        {
                            currentHotel = accommodationResult.Location;
                            accommodationOptions = accommodationResult.Options;
                            selectedAccommodationIndex = accommodationResult.SelectedIndex;
                            
                            // Convert alternative accommodations to display format
                            if (accommodationResult.AlternativeAccommodations != null && accommodationResult.AlternativeAccommodations.Any())
                            {
                                alternativeAccommodations = accommodationResult.AlternativeAccommodations.Select(a => new AlternativeAccommodationDisplay
                                {
                                    Name = a.Name,
                                    Distance = a.Distance,
                                    RecommendedRoomType = a.RecommendedRoomType,
                                    TotalCost = a.TotalCost,
                                    Options = a.Options
                                }).ToList();
                            }
                        }
                    }

                    if (currentHotel != null)
                    {
                        // Use the recommended option's cost instead of average budget
                        double hotelCost = currentHotel.AverageBudget * request.GroupSize;
                        if (accommodationOptions != null && accommodationOptions.Any())
                        {
                            var recommendedOption = accommodationOptions.FirstOrDefault(o => o.Recommended) ?? accommodationOptions.FirstOrDefault();
                            hotelCost = recommendedOption?.TotalCost ?? hotelCost;
                        }

                        var sleepItem = new TimelineItem
                        {
                            Type = "Sleep",
                            Time = $"{FormatTime(nightStart)} - {FormatTime(nightEnd)}",
                            TimeBlock = "Night",
                            Description = $"Sleep/Rest at {currentHotel.Name}",
                            AccommodationOptions = accommodationOptions,
                            SelectedAccommodationIndex = accommodationOptions != null ? selectedAccommodationIndex : null,
                            AlternativeAccommodations = alternativeAccommodations
                        };

                        // Show continuing stay info
                        if (d > 0 && currentDestination == destinationName)
                        {
                            sleepItem.Description += " (Continuing stay)";
                        }

                        // Add luggage storage info if available
                        if (currentHotel.HasLuggageStorage)
                        {
                            sleepItem.LuggageStorageCost = currentHotel.LuggageStorageCost;
                        }

                        dailyPlan.Timeline.Add(sleepItem);
                        dailyPlan.DailyBudgetStatus.Spent += hotelCost;

                        currentLat = currentHotel.Latitude;
                        currentLon = currentHotel.Longitude;
                    }

                    // Fill any remaining time gaps to ensure 24h coverage
                    FillRemainingTimeGaps(dailyPlan, request.GroupSize, currentLat, currentLon, destCandidates);

                    dailyPlan.DailyBudgetStatus.Spent = Math.Round(dailyPlan.DailyBudgetStatus.Spent, 2);
                    output.Days.Add(dailyPlan);
                    totalSpent += dailyPlan.DailyBudgetStatus.Spent;

                    // Rollover calculation
                    rolloverBudget = dailyLimit - (dailyPlan.DailyBudgetStatus.Spent - accommodationBudgetTonight);
                    double maxRollover = dailyBudgets[Math.Min(dayCounter + 1, dailyBudgets.Count - 1)].Limit * 0.5;
                    if (rolloverBudget > maxRollover) rolloverBudget = maxRollover;
                    if (rolloverBudget < -maxRollover) rolloverBudget = -maxRollover;

                    dayCounter++;
                }
            }

            // Set trip summary with budget warnings
            output.TripSummary = new TripSummary
            {
                TotalEstimatedCost = Math.Round(totalSpent, 2),
                RemainingContingencyFund = Math.Round(contingencyFund, 2),
                ContingencyFundPercentage = contingencyPercentage,
                IsBudgetInsufficient = isBudgetInsufficient,
                BudgetWarning = isBudgetInsufficient 
                    ? $"Warning: Your budget of {request.TotalBudget:N0} VND is significantly lower than the recommended minimum of {minimumRequiredBudget:N0} VND. Consider increasing your budget or reducing trip duration." 
                    : null,
                MinimumRecommendedBudget = Math.Round(minimumRequiredBudget, 2)
            };

            return output;
        }

        private double CalculateContingencyPercentage(double totalBudget)
        {
            // Dynamic contingency: higher budget = lower percentage
            // Budget < 5M: 20% contingency
            // Budget 5M-10M: 15% contingency
            // Budget 10M-20M: 10% contingency
            // Budget 20M-50M: 8% contingency
            // Budget > 50M: 5% contingency
            if (totalBudget < 5000000) return 20;
            if (totalBudget < 10000000) return 15;
            if (totalBudget < 20000000) return 10;
            if (totalBudget < 50000000) return 8;
            return 5;
        }

        private double GetDelayBufferForTransport(string transportMethod)
        {
            if (transportMethod.Contains("Airplane") || transportMethod.Contains("Flight"))
                return _flightDelayBuffer;
            if (transportMethod.Contains("Train"))
                return _trainDelayBuffer;
            if (transportMethod.Contains("Bus") || transportMethod.Contains("Coach"))
                return _busDelayBuffer;
            return _generalDelayBuffer;
        }

        private List<DailyBudgetInfo> CreateDailyBudgetsWithWeights(
            Dictionary<string, int> destinationDayAllocation,
            Dictionary<string, double> destinationActivityBudgets,
            Dictionary<string, double> destinationHotelCosts,
            int totalDays,
            double totalBudget)
        {
            var dailyBudgets = new List<DailyBudgetInfo>();
            int dayIndex = 0;

            foreach (var destAlloc in destinationDayAllocation)
            {
                string dest = destAlloc.Key;
                int daysInDest = destAlloc.Value;
                double destActivityBudget = destinationActivityBudgets[dest];
                double hotelCostPerNight = destinationHotelCosts[dest];
                double dailyAvg = destActivityBudget / daysInDest;

                for (int d = 0; d < daysInDest; d++)
                {
                    // Weight: first and last days have higher spending tendency
                    double weight = 1.0;
                    if (d == 0) weight = 1.3; // First day: arrival excitement, more spending
                    else if (d == daysInDest - 1) weight = 1.2; // Last day: souvenir shopping, final meals
                    else if (d == 1) weight = 1.1; // Second day: still high energy

                    double weightedBudget = dailyAvg * weight;
                    
                    // Calculate ceiling and floor (±30% from weighted average)
                    double ceiling = weightedBudget * 1.3;
                    double floor = weightedBudget * 0.7;

                    dailyBudgets.Add(new DailyBudgetInfo
                    {
                        Limit = weightedBudget,
                        Ceiling = ceiling,
                        Floor = floor,
                        Weight = weight
                    });

                    dayIndex++;
                }
            }

            return dailyBudgets;
        }

        private class DailyBudgetInfo
        {
            public double Limit { get; set; }
            public double Ceiling { get; set; }
            public double Floor { get; set; }
            public double Weight { get; set; }
        }

        private void FillTimeGap(TimeSpan startTime, TimeSpan endTime, DailyItinerary dailyPlan, string timeBlock,
            double lat, double lon, List<ScoredLocation> candidates, int groupSize, double remainingBudget)
        {
            if (startTime >= endTime) return;
            
            // Only fill gaps that are at least 15 minutes
            var gapDuration = endTime - startTime;
            if (gapDuration.TotalMinutes < 15) return;

            // Find a nearby activity to fill the gap
            var nearbyActivity = candidates
                .Where(c => !c.Location.Tags.Contains("Hotel", StringComparer.OrdinalIgnoreCase) && 
                           !c.Location.Tags.Contains("Guesthouse", StringComparer.OrdinalIgnoreCase))
                .Select(c => new { 
                    Location = c, 
                    Distance = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude) 
                })
                .Where(x => x.Distance <= 1.0)
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            if (nearbyActivity != null && gapDuration.TotalMinutes >= 30)
            {
                string description = $"Free time / Rest near {nearbyActivity.Location.Location.Name}";
                
                dailyPlan.Timeline.Add(new TimelineItem
                {
                    Type = "Rest",
                    Time = $"{FormatTime(startTime)} - {FormatTime(endTime)}",
                    TimeBlock = timeBlock,
                    Description = description
                });
            }
            else
            {
                dailyPlan.Timeline.Add(new TimelineItem
                {
                    Type = "Rest",
                    Time = $"{FormatTime(startTime)} - {FormatTime(endTime)}",
                    TimeBlock = timeBlock,
                    Description = "Free time / Rest"
                });
            }
        }

        private void FillRemainingTimeGaps(DailyItinerary dailyPlan, int groupSize, double lat, double lon, List<ScoredLocation> candidates)
        {
            // Sort timeline by start time
            var sortedTimeline = dailyPlan.Timeline
                .Where(t => !t.TimeBlock.Equals("Gap") && !t.TimeBlock.Equals("Early Morning") && !t.TimeBlock.Equals("Late Night"))
                .OrderBy(t => ParseTime(t.Time.Split(" - ")[0]))
                .ToList();
            
            // Remove any existing Gap, Early Morning, Late Night items
            var itemsToRemove = dailyPlan.Timeline
                .Where(t => t.TimeBlock.Equals("Gap") || t.TimeBlock.Equals("Early Morning") || t.TimeBlock.Equals("Late Night"))
                .ToList();
            
            foreach (var item in itemsToRemove)
            {
                dailyPlan.Timeline.Remove(item);
            }
            
            TimeSpan? previousEndTime = null;
            
            foreach (var item in sortedTimeline)
            {
                var times = item.Time.Split(" - ");
                var startTime = ParseTime(times[0]);
                var endTime = ParseTime(times[1]);

                if (previousEndTime.HasValue && startTime > previousEndTime.Value)
                {
                    // Found a gap - only fill if >= 15 minutes
                    var gapDuration = startTime - previousEndTime.Value;
                    if (gapDuration.TotalMinutes >= 15)
                    {
                        FillTimeGap(previousEndTime.Value, startTime, dailyPlan, "Free Time", lat, lon, candidates, groupSize, 1000000);
                    }
                }

                previousEndTime = endTime;
            }

            // Fill gap from last activity to end of day (23:00) - not 23:59
            if (previousEndTime.HasValue && previousEndTime.Value < new TimeSpan(23, 0, 0))
            {
                var gapDuration = new TimeSpan(23, 0, 0) - previousEndTime.Value;
                if (gapDuration.TotalMinutes >= 15)
                {
                    FillTimeGap(previousEndTime.Value, new TimeSpan(23, 0, 0), dailyPlan, "Evening", lat, lon, candidates, groupSize, 500000);
                }
            }

            // Fill gap from start of day (08:00) to first activity if first activity is after 08:00
            if (sortedTimeline.Any())
            {
                var firstStartTime = ParseTime(sortedTimeline.First().Time.Split(" - ")[0]);
                if (firstStartTime > MorningStart)
                {
                    var gapDuration = firstStartTime - MorningStart;
                    if (gapDuration.TotalMinutes >= 15)
                    {
                        FillTimeGap(MorningStart, firstStartTime, dailyPlan, "Morning", lat, lon, candidates, groupSize, 0);
                    }
                }
            }
        }

        private TimeSpan ParseTime(string timeStr)
        {
            if (TimeSpan.TryParse(timeStr, out var result))
                return result;
            return TimeSpan.Zero;
        }

        private string FormatTime(TimeSpan time)
        {
            return $"{time.Hours:D2}:{time.Minutes:D2}";
        }

        private AccommodationResult FindNextBestAccommodationWithDetails(
            double lat, double lon, List<ScoredLocation> candidates, int groupSize,
            double accommodationBudget, Location currentHotel)
        {
            var accommodationTags = new[] { "Hotel", "Guesthouse", "Hostel", "Homestay", "Accommodation", "Resort", "Villa" };

            var accommodations = candidates
                .Where(c => c.Location.Tags.Any(t => accommodationTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                .Select(c => new
                {
                    Location = c.Location,
                    Distance = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude),
                    OriginalScore = c.Score
                })
                .ToList();

            if (!accommodations.Any()) return null;

            // Generate room options for each accommodation
            var accommodationWithOptions = accommodations.Select(a => new
            {
                a.Location,
                a.Distance,
                a.OriginalScore,
                Options = GenerateAccommodationOptions(a.Location, groupSize, accommodationBudget)
            }).Where(a => a.Options.Any()).ToList();

            if (!accommodationWithOptions.Any()) return null;

            // Score and rank accommodations based on multiple factors
            var scored = accommodationWithOptions.Select(a =>
            {
                var recommendedOption = a.Options.FirstOrDefault(o => o.Recommended) ?? a.Options.FirstOrDefault();
                
                // Score factors:
                // 1. Distance (closer is better) - 25%
                // 2. Budget fit (within budget is better) - 35%
                // 3. Room suitability for group size - 25%
                // 4. Amenities and services - 15%
                double distanceScore = Math.Max(0, 100 - a.Distance * 15);
                double budgetScore = Math.Max(0, 100 - (recommendedOption.TotalCost / Math.Max(accommodationBudget, 1) * 100));
                double groupSizeScore = CalculateGroupSizeSuitabilityScore(a.Location, groupSize);
                double amenitiesScore = CalculateAmenitiesScore(a.Location);
                
                double totalScore = distanceScore * 0.25 + budgetScore * 0.35 + groupSizeScore * 0.25 + amenitiesScore * 0.15;
                
                return new
                {
                    a.Location,
                    a.Distance,
                    TotalScore = totalScore,
                    Options = a.Options,
                    RecommendedOption = recommendedOption
                };
            }).OrderByDescending(a => a.TotalScore).ToList();

            // Get top 3-5 accommodations to give users options (like transport options)
            var topCandidates = scored.Take(5).ToList();
            
            if (!topCandidates.Any()) return null;

            // Check if keeping current hotel makes sense
            if (currentHotel != null)
            {
                double currentHotelDistance = CalculateDistance(lat, lon, currentHotel.Latitude, currentHotel.Longitude);
                if (currentHotelDistance <= 3.0)
                {
                    return null;
                }
            }

            // Return the best choice with all options
            var bestChoice = topCandidates.First();
            return new AccommodationResult
            {
                Location = bestChoice.Location,
                Options = bestChoice.Options.OrderBy(o => o.Recommended ? 0 : 1).ThenBy(o => o.TotalCost).ToList(),
                SelectedIndex = bestChoice.Options.FindIndex(o => o.Recommended),
                AlternativeAccommodations = topCandidates.Skip(1).Take(4).Select(a => new AlternativeAccommodation
                {
                    Name = a.Location.Name,
                    Distance = Math.Round(a.Distance, 2),
                    RecommendedRoomType = a.RecommendedOption.RoomType,
                    TotalCost = Math.Round(a.RecommendedOption.TotalCost, 2),
                    Options = a.Options.OrderBy(o => o.Recommended ? 0 : 1).ThenBy(o => o.TotalCost).ToList()
                }).ToList()
            };
        }

        private double CalculateGroupSizeSuitabilityScore(Location hotel, int groupSize)
        {
            if (hotel.RoomTypes == null || !hotel.RoomTypes.Any())
                return 50; // Default score for hotels without detailed room types

            // Check if there's a room type that perfectly fits the group
            var bestRoom = hotel.RoomTypes
                .Where(r => r.MaxOccupancy >= groupSize)
                .OrderBy(r => r.MaxOccupancy - groupSize)
                .FirstOrDefault();

            if (bestRoom == null)
                return 20; // No suitable room

            // Perfect fit (group size == max occupancy or close)
            if (bestRoom.MaxOccupancy == groupSize || bestRoom.MaxOccupancy == groupSize + 1)
                return 100;
            
            // Good fit (slightly larger room)
            if (bestRoom.MaxOccupancy <= groupSize + 2)
                return 80;
            
            // Acceptable (much larger room, more expensive)
            return 50;
        }

        private double CalculateAmenitiesScore(Location hotel)
        {
            int score = 50; // Base score
            
            if (hotel.HasLuggageStorage) score += 15;
            if (hotel.HasHourlyRate) score += 10;
            if (!string.IsNullOrEmpty(hotel.CheckInTime) && !string.IsNullOrEmpty(hotel.CheckOutTime)) score += 5;
            
            if (hotel.RoomTypes != null && hotel.RoomTypes.Any())
            {
                var allAmenities = hotel.RoomTypes.SelectMany(r => r.Amenities ?? new List<string>()).Distinct().ToList();
                if (allAmenities.Contains("WiFi")) score += 5;
                if (allAmenities.Contains("Breakfast")) score += 10;
                if (allAmenities.Contains("Kitchen") || allAmenities.Contains("Kitchenette")) score += 5;
                if (allAmenities.Contains("Pool")) score += 5;
            }
            
            return Math.Min(100, score);
        }

        private List<AccommodationOption> GenerateAccommodationOptions(Location hotel, int groupSize, double budget)
        {
            var options = new List<AccommodationOption>();

            // If hotel has detailed room types
            if (hotel.RoomTypes != null && hotel.RoomTypes.Any())
            {
                foreach (var room in hotel.RoomTypes)
                {
                    int roomsNeeded = (int)Math.Ceiling((double)groupSize / room.MaxOccupancy);
                    double totalCost = roomsNeeded * room.PricePerNight;
                    
                    // Recommendation logic based on group size and budget
                    bool isRecommended = false;
                    string pros = "";
                    string cons = "";
                    
                    // Perfect fit: room capacity matches group size exactly or with minimal extra space
                    if (room.MaxOccupancy >= groupSize && room.MaxOccupancy <= groupSize + 2)
                    {
                        isRecommended = totalCost <= budget;
                        pros = roomsNeeded == 1 ? "Perfect fit - group stays together in one room" : "Ideal configuration for your group";
                    }
                    // Good fit: room can accommodate but might be tight or spacious
                    else if (room.MaxOccupancy >= groupSize)
                    {
                        isRecommended = totalCost <= budget * 1.2; // Allow 20% over budget for good fit
                        pros = room.MaxOccupancy > groupSize + 5 ? "Very spacious for your group" : "Comfortable fit for your group";
                        cons = room.MaxOccupancy > groupSize + 5 ? "May be larger (and more expensive) than needed" : "";
                    }
                    // Need multiple rooms
                    else
                    {
                        isRecommended = roomsNeeded <= 2 && totalCost <= budget;
                        pros = roomsNeeded == 2 ? "Two rooms provide privacy and comfort" : "Multiple rooms for flexibility";
                        cons = roomsNeeded > 2 ? "Requires multiple rooms - group split up" : "Group will be in separate rooms";
                    }
                    
                    // Additional pros/cons based on amenities
                    if (room.Amenities != null && room.Amenities.Any())
                    {
                        if (room.Amenities.Contains("Breakfast")) pros += (pros.Length > 0 ? " | " : "") + "Includes breakfast";
                        if (room.Amenities.Contains("Kitchen") || room.Amenities.Contains("Kitchenette")) pros += (pros.Length > 0 ? " | " : "") + "Kitchen access";
                        if (room.Amenities.Contains("Pool")) pros += (pros.Length > 0 ? " | " : "") + "Pool access";
                    }
                    
                    if (roomsNeeded > 2) cons += (cons.Length > 0 ? " | " : "") + "Less economical";
                    if (totalCost > budget) cons += (cons.Length > 0 ? " | " : "") + $"Exceeds budget by {Math.Round((totalCost - budget) / budget * 100, 0)}%";

                    options.Add(new AccommodationOption
                    {
                        RoomType = room.Name,
                        Description = room.Description,
                        PricePerNight = Math.Round(room.PricePerNight, 2),
                        PricePerHour = Math.Round(room.PricePerHour, 2),
                        MaxOccupancy = room.MaxOccupancy,
                        RoomsNeeded = roomsNeeded,
                        TotalCost = Math.Round(totalCost, 2),
                        Amenities = room.Amenities ?? new List<string>(),
                        Recommended = isRecommended,
                        Pros = pros,
                        Cons = cons
                    });
                }
            }
            else
            {
                // Generate generic room options based on group size
                // Standard room (2 people) - Best for couples or solo travelers
                if (groupSize <= 2)
                {
                    options.Add(new AccommodationOption
                    {
                        RoomType = "Standard Room",
                        Description = "Standard room with basic amenities - Perfect for couples",
                        PricePerNight = Math.Round(hotel.AverageBudget, 2),
                        PricePerHour = Math.Round(hotel.AverageBudget / 8, 2),
                        MaxOccupancy = 2,
                        RoomsNeeded = 1,
                        TotalCost = Math.Round(hotel.AverageBudget, 2),
                        Amenities = new List<string> { "WiFi", "AC", "Private Bathroom" },
                        Recommended = true,
                        Pros = "Most economical for 2 people | Compact and cozy",
                        Cons = "Limited space"
                    });
                }
                else
                {
                    int standardRoomsNeeded = (int)Math.Ceiling((double)groupSize / 2);
                    options.Add(new AccommodationOption
                    {
                        RoomType = "Standard Room",
                        Description = "Standard room with basic amenities",
                        PricePerNight = Math.Round(hotel.AverageBudget, 2),
                        PricePerHour = Math.Round(hotel.AverageBudget / 8, 2),
                        MaxOccupancy = 2,
                        RoomsNeeded = standardRoomsNeeded,
                        TotalCost = Math.Round(hotel.AverageBudget * standardRoomsNeeded, 2),
                        Amenities = new List<string> { "WiFi", "AC", "Private Bathroom" },
                        Recommended = false,
                        Pros = $"Most economical option | {standardRoomsNeeded} rooms needed",
                        Cons = standardRoomsNeeded > 2 ? "Group will be split across many rooms" : "Less convenient than single room"
                    });
                }

                // Family room (4 people) - Best for small families or groups of 3-4
                if (groupSize >= 3 && groupSize <= 5)
                {
                    double familyRoomPrice = hotel.AverageBudget * 1.8;
                    int familyRoomsNeeded = (int)Math.Ceiling((double)groupSize / 4);
                    double familyTotalCost = familyRoomPrice * familyRoomsNeeded;
                    
                    options.Add(new AccommodationOption
                    {
                        RoomType = "Family Room",
                        Description = "Spacious room for families or small groups",
                        PricePerNight = Math.Round(familyRoomPrice, 2),
                        PricePerHour = Math.Round(familyRoomPrice / 8, 2),
                        MaxOccupancy = 4,
                        RoomsNeeded = familyRoomsNeeded,
                        TotalCost = Math.Round(familyTotalCost, 2),
                        Amenities = new List<string> { "WiFi", "AC", "Private Bathroom", "Mini Fridge", "TV" },
                        Recommended = familyRoomsNeeded == 1 && familyTotalCost <= budget,
                        Pros = familyRoomsNeeded == 1 ? "Group stays together in one room | More space" : "Better than multiple standard rooms",
                        Cons = familyTotalCost > budget ? "Higher cost" : "Slightly over standard budget"
                    });
                }

                // Large Family Room / Junior Suite (5-6 people)
                if (groupSize >= 5 && groupSize <= 7)
                {
                    double largeRoomPrice = hotel.AverageBudget * 2.5;
                    int largeRoomsNeeded = (int)Math.Ceiling((double)groupSize / 6);
                    double largeTotalCost = largeRoomPrice * largeRoomsNeeded;
                    
                    options.Add(new AccommodationOption
                    {
                        RoomType = "Large Family Room",
                        Description = "Extra spacious room for larger families or groups",
                        PricePerNight = Math.Round(largeRoomPrice, 2),
                        PricePerHour = Math.Round(largeRoomPrice / 8, 2),
                        MaxOccupancy = 6,
                        RoomsNeeded = largeRoomsNeeded,
                        TotalCost = Math.Round(largeTotalCost, 2),
                        Amenities = new List<string> { "WiFi", "AC", "Private Bathroom", "Mini Fridge", "TV", "Living Area" },
                        Recommended = largeRoomsNeeded == 1 && largeTotalCost <= budget,
                        Pros = largeRoomsNeeded == 1 ? $"Perfect for {groupSize} people - everyone stays together" : "Comfortable for large groups",
                        Cons = largeTotalCost > budget ? "Premium pricing" : "Higher cost but more convenient"
                    });
                }

                // Suite (6+ people) - Best for large groups
                if (groupSize >= 6)
                {
                    double suitePrice = hotel.AverageBudget * 3.5;
                    int suiteRoomsNeeded = (int)Math.Ceiling((double)groupSize / 7);
                    double suiteTotalCost = suitePrice * suiteRoomsNeeded;
                    
                    options.Add(new AccommodationOption
                    {
                        RoomType = "Suite",
                        Description = "Luxury suite with premium amenities for large groups",
                        PricePerNight = Math.Round(suitePrice, 2),
                        PricePerHour = Math.Round(suitePrice / 8, 2),
                        MaxOccupancy = 7,
                        RoomsNeeded = suiteRoomsNeeded,
                        TotalCost = Math.Round(suiteTotalCost, 2),
                        Amenities = new List<string> { "WiFi", "AC", "Private Bathroom", "Mini Fridge", "TV", "Living Area", "Breakfast" },
                        Recommended = suiteRoomsNeeded == 1 && suiteTotalCost <= budget * 1.3,
                        Pros = suiteRoomsNeeded == 1 ? "Maximum comfort - entire group in one suite | Premium amenities" : "Best option for large groups",
                        Cons = suiteTotalCost > budget ? "Most expensive option" : "Premium pricing"
                    });
                }
                
                // Multiple room configurations for very large groups (7+ people)
                if (groupSize >= 7)
                {
                    // Configuration 1: Mix of family + standard rooms
                    int familyRoomsForLarge = groupSize / 4;
                    int remainingAfterFamily = groupSize % 4;
                    int standardForRemainder = remainingAfterFamily > 0 ? 1 : 0;
                    int totalRoomsMix = familyRoomsForLarge + standardForRemainder;
                    double mixCost = (familyRoomsForLarge * hotel.AverageBudget * 1.8) + (standardForRemainder * hotel.AverageBudget);
                    
                    options.Add(new AccommodationOption
                    {
                        RoomType = "Mixed Configuration",
                        Description = $"{familyRoomsForLarge} Family Room(s) + {standardForRemainder} Standard Room(s)",
                        PricePerNight = Math.Round(hotel.AverageBudget, 2),
                        PricePerHour = Math.Round(hotel.AverageBudget / 8, 2),
                        MaxOccupancy = groupSize,
                        RoomsNeeded = totalRoomsMix,
                        TotalCost = Math.Round(mixCost, 2),
                        Amenities = new List<string> { "WiFi", "AC", "Private Bathroom", "TV" },
                        Recommended = mixCost <= budget && totalRoomsMix <= 3,
                        Pros = "Flexible configuration | Cost-effective for large groups",
                        Cons = "Group split across different room types"
                    });
                }
            }

            // Add hourly rental option if available
            if (hotel.HasHourlyRate && hotel.HourlyRate > 0)
            {
                options.Add(new AccommodationOption
                {
                    RoomType = "Hourly Rental",
                    Description = "Rent a room by the hour for short rest (minimum 2 hours)",
                    PricePerNight = 0,
                    PricePerHour = Math.Round(hotel.HourlyRate, 2),
                    MaxOccupancy = Math.Max(2, groupSize),
                    RoomsNeeded = (int)Math.Ceiling((double)groupSize / 4),
                    TotalCost = Math.Round(hotel.HourlyRate * 2, 2), // Minimum 2 hours
                    Amenities = new List<string> { "WiFi", "AC", "Shower" },
                    Recommended = false,
                    Pros = "Pay only for what you need | Great for layovers",
                    Cons = "Not suitable for overnight stay | Minimum 2 hours"
                });
            }

            // Sort by recommendation and cost
            return options.OrderBy(o => o.Recommended ? 0 : 1).ThenBy(o => o.TotalCost).ToList();
        }

        private class AccommodationResult
        {
            public Location Location { get; set; }
            public List<AccommodationOption> Options { get; set; }
            public int SelectedIndex { get; set; }
            public List<AlternativeAccommodation> AlternativeAccommodations { get; set; }
        }

        private class AlternativeAccommodation
        {
            public string Name { get; set; }
            public double Distance { get; set; }
            public string RecommendedRoomType { get; set; }
            public double TotalCost { get; set; }
            public List<AccommodationOption> Options { get; set; }
        }

        // ... rest of the existing methods (ProcessAttraction, IsEveningActivity, etc.)
        // Keeping them unchanged for brevity but they would be included in the actual file

        private void ProcessAttraction(BestAttraction bestAttraction, ref TimeSpan currentTime, string block, DailyItinerary dailyPlan, int groupSize, double dailyLimit, HashSet<int> visitedIds, ref double currentLat, ref double currentLon)
        {
            var transportOptions = GetTransportOptions(bestAttraction.Distance, groupSize);
            var defaultTransport = transportOptions.FirstOrDefault(o => o.Recommended) ?? transportOptions.FirstOrDefault();

            // Add delay buffer
            double delayBuffer = GetDelayBufferForTransport(defaultTransport.Method);
            double totalTravelTime = defaultTransport.TravelTimeMinutes + delayBuffer;

            TimeSpan arrivalTime = currentTime.Add(TimeSpan.FromMinutes(totalTravelTime));

            // Only add transport item if distance > 0.5km (not walking distance)
            if (bestAttraction.Distance > 0.5)
            {
                dailyPlan.Timeline.Add(new TimelineItem
                {
                    Type = "Transport",
                    Time = $"{FormatTime(currentTime)} - {FormatTime(arrivalTime)}",
                    TimeBlock = block,
                    Description = $"{defaultTransport.Description} to {bestAttraction.Location.Name}",
                    TransportOptions = transportOptions,
                    SelectedTransportIndex = transportOptions.IndexOf(defaultTransport)
                });
            }

            double actualStayTimeMinutes = bestAttraction.Location.AverageStayDuration * (1 + 0.05 * (groupSize - 2));
            TimeSpan visitEndTime = arrivalTime.Add(TimeSpan.FromMinutes(actualStayTimeMinutes));

            dailyPlan.Timeline.Add(new TimelineItem
            {
                Type = "Visit",
                Time = $"{FormatTime(arrivalTime)} - {FormatTime(visitEndTime)}",
                TimeBlock = block,
                Description = $"Visit {bestAttraction.Location.Name}",
                TicketCost = Math.Round(bestAttraction.Location.AverageBudget * groupSize, 2),
                GroupDiscountApplied = groupSize >= 5
            });

            dailyPlan.DailyBudgetStatus.Spent += defaultTransport.TotalCost + bestAttraction.Location.AverageBudget * groupSize;
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

        private List<string> DetermineBestVisitingOrder(List<string> destinations, List<ScoredLocation> candidates, double startLat, double startLon)
        {
            var normalizedDestinations = destinations.Select(d => 
                d.Equals("Ho Chi Minh City", StringComparison.OrdinalIgnoreCase) ? "HCMC" : d)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var remaining = normalizedDestinations;
            var ordered = new List<string>();
            double currentLat = startLat;
            double currentLon = startLon;

            while (remaining.Any())
            {
                var nextDest = remaining
                    .Select(d => {
                        var center = GetDestinationCenter(candidates.Where(c => 
                            c.Location.Destination.Equals(d, StringComparison.OrdinalIgnoreCase)).ToList());
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

        private Dictionary<string, int> AllocateDaysToDestinations(List<string> orderedDestinations, List<ScoredLocation> candidates, int totalDays)
        {
            var counts = orderedDestinations.ToDictionary(d => d, 
                d => candidates.Count(c => c.Location.Destination.Equals(d, StringComparison.OrdinalIgnoreCase)));
            int totalAttractions = counts.Values.Sum();

            if (totalAttractions == 0) 
                return orderedDestinations.ToDictionary(d => d, d => (int)Math.Max(1, totalDays / (double)orderedDestinations.Count));

            var allocation = new Dictionary<string, int>();
            int assignedDays = 0;

            foreach (var dest in orderedDestinations)
            {
                int days = (int)Math.Max(1, Math.Round((double)counts[dest] / totalAttractions * totalDays));
                allocation[dest] = days;
                assignedDays += days;
            }

            if (assignedDays != totalDays && orderedDestinations.Any())
            {
                string lastDest = orderedDestinations.Last();
                allocation[lastDest] = Math.Max(1, allocation[lastDest] + (totalDays - assignedDays));
            }

            return allocation;
        }

        private (double Lat, double Lon) GetDestinationCenter(List<ScoredLocation> destinationCandidates)
        {
            if (!destinationCandidates.Any()) return (21.0285, 105.8522);
            var top = destinationCandidates.OrderByDescending(c => c.Score).Take(5).ToList();
            return (top.Average(c => c.Location.Latitude), top.Average(c => c.Location.Longitude));
        }

        /// <summary>
        /// Validates that all requested destinations exist in the database
        /// </summary>
        /// <param name="requestedDestinations">List of destination names requested by user</param>
        /// <param name="availableLocations">List of all available locations from database</param>
        /// <returns>Error message if validation fails, null if successful</returns>
        private string ValidateDestinations(List<string> requestedDestinations, List<Location> availableLocations)
        {
            if (requestedDestinations == null || !requestedDestinations.Any())
            {
                return "No destinations provided. Please specify at least one destination for your trip.";
            }

            // Get all unique destination names from database (case-insensitive)
            var availableDestinations = availableLocations
                .Where(l => !string.IsNullOrEmpty(l.Destination))
                .Select(l => l.Destination.ToLowerInvariant())
                .Distinct()
                .ToHashSet();

            // Find invalid destinations
            var invalidDestinations = requestedDestinations
                .Where(d => !availableDestinations.Contains(d.ToLowerInvariant()))
                .ToList();

            if (invalidDestinations.Any())
            {
                // Get list of available destinations for suggestion
                var availableList = availableDestinations
                    .OrderBy(d => d)
                    .Take(10)
                    .Select(d => char.ToUpperInvariant(d[0]) + d.Substring(1));

                return $"Invalid destination(s): {string.Join(", ", invalidDestinations)}. " +
                       $"These destination(s) are not available in our database. " +
                       $"Popular destinations: {string.Join(", ", availableList)}. " +
                       $"Please check the spelling or choose from available destinations.";
            }

            return null;
        }

        /// <summary>
        /// Get available transport options based on distance and group size
        /// Transport mode selection logic:
        /// - &lt; 100km: Bus/Coach (most economical)
        /// - 100-300km: Bus/Coach or Train (balance of cost and comfort)
        /// - 300-600km: Train (comfortable, reasonable time)
        /// - 600-1000km: Train or Airplane (depends on budget preference)
        /// - &gt; 1000km: Airplane (fastest, time-saving)
        /// </summary>
        private List<TransportOption> GetInterCityTransportOptions(double distance, int groupSize)
        {
            var options = new List<TransportOption>();

            // Bus/Coach: Best for short distances (< 300km)
            if (distance < 300)
            {
                double busCost = 200000 * groupSize;
                double busTime = (distance / 45.0) * 60.0;
                options.Add(new TransportOption
                {
                    Method = "Bus/Coach",
                    Description = "Bus / Coach",
                    TotalCost = Math.Round(busCost, 2),
                    TravelTimeMinutes = Math.Round(busTime, 2),
                    VehiclesNeeded = 1,
                    Pros = "Most economical, direct route, frequent departures",
                    Cons = "Slower, less comfortable for long distances",
                    Recommended = distance < 150 || groupSize > 10,
                    GroupSize = groupSize
                });
            }

            // Train: Best for medium distances (150-1000km)
            if (distance >= 100 && distance <= 1000) // Fixed: was "distance = 1000"
            {
                // Tiered pricing based on distance (Fixed: added relational patterns)
                double trainCostPerPerson = distance switch
                {
                    < 200 => 300000,      // Short haul
                    < 400 => 500000,      // Medium haul
                    < 600 => 800000,      // Long haul
                    < 800 => 1200000,     // Very long haul
                    _ => 1500000          // Maximum range
                };

                double trainCost = trainCostPerPerson * groupSize;
                double trainTime = (distance / 55.0) * 60.0;

                options.Add(new TransportOption
                {
                    Method = "Train",
                    Description = "Train (Reunification Express or Regional)",
                    TotalCost = Math.Round(trainCost, 2),
                    TravelTimeMinutes = Math.Round(trainTime, 2),
                    VehiclesNeeded = 1,
                    Pros = "Comfortable, scenic views, can move around, reliable schedule",
                    Cons = "Fixed schedule, may be delayed, limited routes",
                    Recommended = (distance >= 200 && distance <= 500) || (distance > 600 && groupSize <= 4),
                    GroupSize = groupSize
                });
            }

            // Airplane: Best for long distances (> 400km)
            if (distance > 400)
            {
                double flightCostPerPerson = distance switch
                {
                    < 600 => 1200000,     // Short domestic
                    < 900 => 1800000,     // Medium domestic
                    _ => 2500000          // Long domestic
                };

                double flightCost = flightCostPerPerson * groupSize;
                double flightTime = 60 + 90 + 90;

                options.Add(new TransportOption
                {
                    Method = "Airplane",
                    Description = "Domestic Flight (Vietnam Airlines/VietJet/Bamboo)",
                    TotalCost = Math.Round(flightCost, 2),
                    TravelTimeMinutes = Math.Round(flightTime, 2),
                    VehiclesNeeded = 1,
                    Pros = "Fastest for long distances, most comfortable, time-saving",
                    Cons = "Most expensive, airport transfers needed, security checks, weather dependent",
                    Recommended = distance > 700 || (distance > 500 && groupSize <= 4),
                    GroupSize = groupSize
                });
            }

            // Private Van: Best for groups (4-16 people)
            if (groupSize <= 16 && distance < 400)
            {
                int vansNeeded = (int)Math.Ceiling(groupSize / 16.0);
                double vanCost = vansNeeded * 35000 * distance;
                double vanTime = (distance / 50.0) * 60.0;

                options.Add(new TransportOption
                {
                    Method = "Private Van",
                    Description = $"{vansNeeded} x 16-seat van",
                    TotalCost = Math.Round(vanCost, 2),
                    TravelTimeMinutes = Math.Round(vanTime, 2),
                    VehiclesNeeded = vansNeeded,
                    Pros = "Flexible schedule, door-to-door, group stays together, luggage space",
                    Cons = "Driver fatigue on long trips, road conditions dependent",
                    Recommended = (groupSize > 4 && groupSize <= 16) && distance < 250,
                    GroupSize = groupSize
                });
            }

            // Intelligent fallback
            if (!options.Any(o => o.Recommended) && options.Any())
            {
                if (distance > 800)
                    options.FirstOrDefault(o => o.Method == "Airplane")!.Recommended = true;
                else
                {
                    var cheapest = options.OrderBy(o => o.TotalCost).First();
                    cheapest.Recommended = true;
                }
            }

            return options.OrderBy(o => o.Recommended ? 0 : 1).ThenBy(o => o.TotalCost).ToList();
        }

        private List<ScoredLocation> FilterAndScoreLocations(List<Location> allLocations, List<string> destinations, List<string> favoriteTags)
        {
            var normalizedDestinations = destinations.Select(d => 
                d.Equals("Ho Chi Minh City", StringComparison.OrdinalIgnoreCase) ? "HCMC" : d).ToList();
            return allLocations
                .Where(l => normalizedDestinations.Contains(l.Destination, StringComparer.OrdinalIgnoreCase))
                .Select(l => new ScoredLocation
                {
                    Location = l,
                    Score = favoriteTags == null ? 1 : l.Tags.Intersect(favoriteTags, StringComparer.OrdinalIgnoreCase).Count() + 1
                })
                .ToList();
        }

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
                return 300000 * groupSize;
            }

            int budgetOptionCount = Math.Max(1, accommodations.Count / 2);
            var budgetOptions = accommodations.Take(budgetOptionCount);

            return budgetOptions.Average(x => x.CostPerNight);
        }

        private double CalculateInterCityTransportBudget(List<string> orderedDestinations, List<ScoredLocation> candidates, int groupSize, double startLat, double startLon)
        {
            double totalBudget = 0;
            double currentLat = startLat;
            double currentLon = startLon;

            foreach (var dest in orderedDestinations)
            {
                var destCenter = GetDestinationCenter(candidates.Where(c => 
                    c.Location.Destination.Equals(dest, StringComparison.OrdinalIgnoreCase)).ToList());
                double distance = CalculateDistance(currentLat, currentLon, destCenter.Lat, destCenter.Lon);
                
                var options = GetInterCityTransportOptions(distance, groupSize);
                var recommended = options.FirstOrDefault(o => o.Recommended) ?? options.FirstOrDefault();
                
                if (recommended != null)
                {
                    totalBudget += recommended.TotalCost;
                }

                currentLat = destCenter.Lat;
                currentLon = destCenter.Lon;
            }

            return totalBudget;
        }

        private Dictionary<string, double> AllocateBudgetToDestinations(Dictionary<string, int> destinationDayAllocation, List<ScoredLocation> allCandidates, double totalActivityBudget)
        {
            var destinationBudgets = new Dictionary<string, double>();
            var destinationWeights = new Dictionary<string, double>();

            foreach (var dest in destinationDayAllocation.Keys)
            {
                int days = destinationDayAllocation[dest];
                int attractionCount = allCandidates.Count(c => 
                    c.Location.Destination.Equals(dest, StringComparison.OrdinalIgnoreCase));
                double weight = days * Math.Max(1, Math.Sqrt(attractionCount));
                destinationWeights[dest] = weight;
            }

            double totalWeight = destinationWeights.Values.Sum();

            foreach (var dest in destinationDayAllocation.Keys)
            {
                double destBudget = (destinationWeights[dest] / totalWeight) * totalActivityBudget;
                destinationBudgets[dest] = destBudget;
            }

            return destinationBudgets;
        }

        private BestAttraction FindNextBestAttraction(
            double lat, double lon, List<ScoredLocation> candidates, HashSet<int> visitedIds,
            TimeSpan currentTime, DayOfWeek dayOfWeek, TimeSpan dayEndTime, int groupSize,
            double remainingDailyBudget, bool isEvening)
        {
            double r = 2.0;
            List<ScoredLocation> nearby = new List<ScoredLocation>();

            while (r <= 15.0)
            {
                nearby = candidates
                    .Where(c => !visitedIds.Contains(c.Location.Id))
                    .Where(c => !isEvening || IsEveningActivity(c.Location))
                    .Where(c => isEvening || !IsEveningActivity(c.Location) || c.Location.Tags.Contains("Relax", StringComparer.OrdinalIgnoreCase))
                    .Select(c => new { ScoredLocation = c, Distance = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude) })
                    .Where(x => x.Distance <= r)
                    .Select(x => x.ScoredLocation)
                    .ToList();

                if (nearby.Count >= 3 || r >= 15.0) break;
                r += 2.0;
            }

            if (!nearby.Any()) return null;

            var validAttractions = nearby
                .Select(c => {
                    double dist = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude);
                    var transport = OptimizeTransport(dist, groupSize);
                    var transportDescription = transport.Description.Split(' ').FirstOrDefault();
                    double delayBuffer = GetDelayBufferForTransport(transportDescription ?? "");
                    TimeSpan arrivalTime = currentTime.Add(TimeSpan.FromMinutes(transport.TravelTimeMinutes + delayBuffer));

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

        private List<TransportOption> GetTransportOptions(double distance, int groupSize)
        {
            var options = new List<TransportOption>();

            if (distance < 1.0)
            {
                options.Add(new TransportOption
                {
                    Method = "Walking",
                    Description = "Walking",
                    TotalCost = 0,
                    TravelTimeMinutes = Math.Round((distance / 4.0) * 60.0, 2),
                    VehiclesNeeded = 0,
                    Pros = "Free, eco-friendly, good for health",
                    Cons = "Slow, only for short distances",
                    Recommended = true,
                    GroupSize = groupSize
                });
            }
            else
            {
                if (distance <= 2.0)
                {
                    options.Add(new TransportOption
                    {
                        Method = "Walking",
                        Description = "Walking",
                        TotalCost = 0,
                        TravelTimeMinutes = Math.Round((distance / 4.0) * 60.0, 2),
                        VehiclesNeeded = 0,
                        Pros = "Free, eco-friendly",
                        Cons = "Slow",
                        Recommended = distance <= 1.0,
                        GroupSize = groupSize
                    });
                }

                foreach (var vehicle in _vehicleTypes.Where(v => !v.IsWalking))
                {
                    int vehiclesNeeded = (int)Math.Ceiling((double)groupSize / vehicle.Capacity);
                    double totalCost = vehiclesNeeded * vehicle.CostPerKm * distance;
                    double travelTimeMinutes = (distance / vehicle.SpeedKmh) * 60.0;

                    string pros = "";
                    string cons = "";
                    bool recommended = false;

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
                        TotalCost = Math.Round(totalCost, 2),
                        TravelTimeMinutes = Math.Round(travelTimeMinutes, 2),
                        VehiclesNeeded = vehiclesNeeded,
                        Pros = pros,
                        Cons = cons,
                        Recommended = recommended,
                        GroupSize = groupSize
                    });
                }
            }

            var sortedOptions = options.OrderBy(o => o.TotalCost / Math.Max(groupSize, 1)).ToList();

            if (sortedOptions.Any(o => o.Recommended) == false && sortedOptions.Any())
            {
                sortedOptions[0].Recommended = true;
            }

            // Sort recommended first
            return sortedOptions.OrderBy(o => o.Recommended ? 0 : 1).ThenBy(o => o.TotalCost).ToList();
        }

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
            if (hours == null) return false; // Return false if no opening hours for that day

            return arrival >= hours.OpenTime && departure <= hours.CloseTime;
        }

        private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
        {
            var R = 6371;
            var dLat = ToRadians(lat2 - lat1);
            var dLon = ToRadians(lon2 - lon1);
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                    Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                    Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private double ToRadians(double deg) => deg * (Math.PI / 180);

        private BestAttraction FindNextBestRestLocation(
            double lat, double lon, List<ScoredLocation> candidates, string[] tags,
            int groupSize, double remainingDailyBudget, bool isAccommodation = false)
        {
            if (isAccommodation)
            {
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

            var restLocations = candidates
                .Where(c => c.Location.Tags.Intersect(tags, StringComparer.OrdinalIgnoreCase).Any())
                .Select(c => new
                {
                    ScoredLocation = c,
                    Distance = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude),
                    Cost = c.Location.AverageBudget * groupSize
                })
                .Where(x => x.Distance <= 2.0)
                .Where(x => x.Cost <= remainingDailyBudget * 2.5)
                .ToList();

            if (!restLocations.Any()) return null;

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
