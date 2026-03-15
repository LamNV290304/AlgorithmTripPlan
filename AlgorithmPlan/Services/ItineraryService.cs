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
        private readonly Dictionary<string, ProvinceData> _provinceDataCache = new Dictionary<string, ProvinceData>();

        // Vehicle capacities and costs per km (examples)
        private readonly List<VehicleType> _vehicleTypes = new List<VehicleType>
        {
            new VehicleType { Name = "Walking", Capacity = 100, CostPerKm = 0, SpeedKmh = 4, IsWalking = true },
            new VehicleType { Name = "Taxi 4-seat", Capacity = 4, CostPerKm = 15000, SpeedKmh = 30 },
            new VehicleType { Name = "7-seat vehicle", Capacity = 7, CostPerKm = 20000, SpeedKmh = 30 },
            new VehicleType { Name = "16-seat van", Capacity = 16, CostPerKm = 35000, SpeedKmh = 25 }
        };

        // Extra spending multipliers based on trip segment (for service locations)
        // Budget: 50k-150k, Midrange: 150k-400k, Luxury: 400k-1M+
        private readonly (double min, double max) _budgetExtraSpending = (50000, 150000);
        private readonly (double min, double max) _midrangeExtraSpending = (150000, 400000);
        private readonly (double min, double max) _luxuryExtraSpending = (400000, 1000000);

        // Time delay buffers based on transport mode (research-based)
        // Source: IATA recommends arriving 2h before domestic, 3h before international flights
        // Train stations: 30-45 min buffer, Bus stations: 15-20 min buffer
        private readonly double _flightDelayBuffer = 120; // minutes
        private readonly double _trainDelayBuffer = 45; // minutes
        private readonly double _busDelayBuffer = 20; // minutes
        private readonly double _generalDelayBuffer = 15; // minutes for local transport

        private static readonly TimeSpan DayStart = new TimeSpan(0, 0, 0);
        private static readonly TimeSpan MorningStart = new TimeSpan(8, 0, 0);
        private static readonly TimeSpan MorningEnd = new TimeSpan(12, 0, 0);
        private static readonly TimeSpan LunchStart = new TimeSpan(12, 0, 0);
        private static readonly TimeSpan LunchEnd = new TimeSpan(13, 0, 0);
        private static readonly TimeSpan AfternoonStart = new TimeSpan(13, 0, 0);
        private static readonly TimeSpan AfternoonEnd = new TimeSpan(18, 0, 0);
        private static readonly TimeSpan EveningStart = new TimeSpan(18, 0, 0);
        private static readonly TimeSpan EveningEnd = new TimeSpan(23, 59, 59);

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
                        // Parse province data and cache it
                        var provinceData = ParseProvinceDataWithDetails(dataElement);
                        if (provinceData != null)
                        {
                            _provinceDataCache[provinceData.Name.ToLowerInvariant()] = provinceData;
                            allLocations.Add(provinceData.Location);
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

        private ProvinceData ParseProvinceDataWithDetails(JsonElement dataElement)
        {
            try
            {
                var name = dataElement.GetProperty("name").GetString()?.ToLowerInvariant();
                var latitude = dataElement.GetProperty("latitude").GetDouble();
                var longitude = dataElement.GetProperty("longitude").GetDouble();
                var englishName = dataElement.GetProperty("english_name").GetString();

                // Extract airports
                var airports = new List<AirportInfo>();
                if (dataElement.TryGetProperty("airports", out var airportArray))
                {
                    foreach (var airport in airportArray.EnumerateArray())
                    {
                        if (airport.TryGetProperty("properties", out var props))
                        {
                            var airportNameVi = props.TryGetProperty("AirportName_Vi", out var nameVi) ? nameVi.GetString() : null;
                            var airportNameEn = props.TryGetProperty("AirportName_En", out var nameEn) ? nameEn.GetString() : null;
                            var iataCode = props.TryGetProperty("IATA_FAA", out var iata) ? iata.GetString() : null;
                            var cityName = props.TryGetProperty("CityName_Vi", out var city) ? city.GetString() : null;
                            
                            airports.Add(new AirportInfo
                            {
                                Name = airportNameVi ?? "City Airport",
                                EnglishName = airportNameEn ?? "City Airport",
                                IataCode = iataCode ?? "",
                                CityName = cityName ?? "",
                                Distance = airport.TryGetProperty("distance", out var dist) ? dist.GetDouble() : 0
                            });
                        }
                    }
                }

                // Extract train stations
                var trainStations = new List<TrainStationInfo>();
                if (dataElement.TryGetProperty("train_stations", out var stationArray))
                {
                    foreach (var station in stationArray.EnumerateArray())
                    {
                        if (station.TryGetProperty("properties", out var props))
                        {
                            var stationNameVi = props.TryGetProperty("StationName_Vi", out var nameVi) ? nameVi.GetString() : null;
                            var stationNameEn = props.TryGetProperty("StationName_En", out var nameEn) ? nameEn.GetString() : null;
                            var stationCity = props.TryGetProperty("CityName_Vi", out var city) ? city.GetString() : null;
                            
                            trainStations.Add(new TrainStationInfo
                            {
                                Name = stationNameVi ?? "Train Station",
                                EnglishName = stationNameEn ?? "Train Station",
                                CityName = stationCity ?? englishName
                            });
                        }
                    }
                }

                var tags = new List<string> { "Province", "Destination" };
                if (airports.Any())
                {
                    tags.Add("Airport");
                    tags.Add("Transport");
                }
                if (trainStations.Any())
                {
                    tags.Add("TrainStation");
                    tags.Add("Transport");
                }

                return new ProvinceData
                {
                    Name = name,
                    EnglishName = englishName,
                    Location = new Location
                    {
                        Id = int.Parse(dataElement.GetProperty("id").GetString()),
                        Name = englishName,
                        Description = $"{englishName}",
                        Latitude = latitude,
                        Longitude = longitude,
                        Destination = englishName,
                        Tags = tags,
                        AverageBudget = 0,
                        AverageStayDuration = 0,
                        OpeningHours = new List<OpeningHours>()
                    },
                    Airports = airports,
                    TrainStations = trainStations
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
            // Task 5: Replace HashSet with Dictionary to track visit count (max 1 per itinerary)
            var visitedCountMap = new Dictionary<int, int>();
            double totalSpent = 0;
            double rolloverBudget = 0;

            bool wantHotel = !string.Equals(request.HotelPreference, "none", StringComparison.OrdinalIgnoreCase);
            string tripSegment = request.TripSegment ?? "midrange";
            string hotelSegment = request.HotelPreference ?? "midrange";

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

                    // Task 1: Use ceiling as hard cap; rollover can lift the floor but not the ceiling
                    double dailyCeiling = dailyBudgetInfo.Ceiling + rolloverBudget;
                    double dailyFloor   = dailyBudgetInfo.Floor;
                    bool needHotelTonight = wantHotel &&
                        (d < daysInThisDest - 1 || destAlloc.Key != destinationDayAllocation.Last().Key);

                    double accommodationBudgetTonight = needHotelTonight ? destinationHotelCosts[destinationName] : 0;
                    double totalDailyCeiling = dailyCeiling + accommodationBudgetTonight;

                    var dailyPlan = new DailyItinerary
                    {
                        Day = $"Day {dayCounter + 1} – {destinationName}",
                        DailyBudgetStatus = new DailyBudgetStatus
                        {
                            Limit   = Math.Round(dailyBudgetInfo.Limit + accommodationBudgetTonight, 0), // internal only, hidden from JSON
                            Spent   = 0,
                            Ceiling = Math.Round(totalDailyCeiling, 0),
                            Floor   = Math.Round(dailyFloor, 0),
                            Weight  = dailyBudgetInfo.Weight
                        }
                    };

                    TimeSpan currentTime = MorningStart;

                    // Issue 3: On Day 1, add hotel check-in at the VERY beginning before any activities
                    // Customers need to drop luggage before they can start sightseeing
                    if (dayCounter == 0 && wantHotel && currentHotel == null)
                    {
                        var hotelResult = FindNextBestAccommodationWithDetails(
                            currentLat, currentLon, destCandidates, request.GroupSize, accommodationBudgetTonight, null);
                        if (hotelResult != null)
                        {
                            currentHotel = hotelResult.Location;
                            double actualCheckinDuration = 30 * (1 + 0.05 * (request.GroupSize - 2));
                            if (actualCheckinDuration < 15) actualCheckinDuration = 15;

                            TimeSpan checkinEnd = currentTime.Add(TimeSpan.FromMinutes(actualCheckinDuration));
                            dailyPlan.Timeline.Insert(0, new TimelineItem
                            {
                                Type = "CheckIn",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(checkinEnd)}",
                                TimeBlock = "Morning",
                                Description = $"Hotel Check-in: {currentHotel.Name} (Drop luggage before sightseeing)" +
                                              $" | Check-in: {currentHotel.CheckInTime ?? "14:00"}" +
                                              $" | Check-out: {currentHotel.CheckOutTime ?? "12:00"}",
                                Action = "CheckIn",
                                // Fix 2: Add options directly on day 1 CheckIn
                                AccommodationOptions = hotelResult?.Options,
                                SelectedAccommodationIndex = hotelResult != null ? hotelResult.SelectedIndex : null,
                                AlternativeAccommodations = hotelResult?.AlternativeAccommodations
                            });
                            currentTime = checkinEnd;
                        }
                    }

                    // Handle inter-city movement and Hotel Check-in/out
                    if (currentDestination != destinationName)
                    {
                        if (currentDestination != null)
                        {
                            // 1. Hotel Check-out (from previous city hotel)
                            if (currentHotel != null)
                            {
                                double actualCheckoutDuration = 30 * (1 + 0.05 * (request.GroupSize - 2));
                                if (actualCheckoutDuration < 15) actualCheckoutDuration = 15;

                                TimeSpan checkoutEnd = currentTime.Add(TimeSpan.FromMinutes(actualCheckoutDuration));
                                dailyPlan.Timeline.Add(new TimelineItem
                                {
                                    Type = "CheckOut",
                                    Time = $"{FormatTime(currentTime)} - {FormatTime(checkoutEnd)}",
                                    TimeBlock = currentTime < LunchStart ? "Morning" : "Afternoon",
                                    Description = $"Hotel Check-out: {currentHotel.Name}",
                                    Action = "CheckOut"
                                });
                                currentTime = checkoutEnd;
                            }

                            // 2. Local Transfer: Hotel to Terminal
                            double distanceToTerminal = 15.0; // Estimated 15km to hub (Airport/Station)
                            var localToTerminalOptions = GetTransportOptions(distanceToTerminal, request.GroupSize);
                            var toTerminalTransport = localToTerminalOptions.FirstOrDefault(o => o.Recommended) ?? localToTerminalOptions.FirstOrDefault();

                            TimeSpan toTerminalEnd = currentTime.Add(TimeSpan.FromMinutes(toTerminalTransport.TravelTimeMinutes + 10)); // +10 min local buffer

                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "Transport",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(toTerminalEnd)}",
                                TimeBlock = currentTime < LunchStart ? "Morning" : "Afternoon",
                                Description = $"Local Transfer: {toTerminalTransport.Description} from Hotel to Departure Terminal",
                                TransportOptions = localToTerminalOptions,
                                SelectedTransportIndex = localToTerminalOptions.IndexOf(toTerminalTransport),
                                Cost = toTerminalTransport.TotalCost
                            });
                            currentTime = toTerminalEnd;
                            dailyPlan.DailyBudgetStatus.Spent += toTerminalTransport.TotalCost;

                            // 3. Terminal Waiting (1.5 - 2 hours)
                            var destCenter = GetDestinationCenter(destCandidates);
                            double distance = CalculateDistance(currentLat, currentLon, destCenter.Lat, destCenter.Lon);
                            var mainJourneyOptions = GetInterCityTransportOptions(distance, request.GroupSize, currentDestination, destinationName, candidateLocations);
                            var mainJourneyTransport = mainJourneyOptions.FirstOrDefault(o => o.Recommended) ?? mainJourneyOptions.FirstOrDefault();

                            double waitingTime = GetDelayBufferForTransport(mainJourneyTransport?.Method ?? "");
                            waitingTime = Math.Max(waitingTime, 90); // At least 1.5 hours per requirement

                            TimeSpan waitingEnd = currentTime.Add(TimeSpan.FromMinutes(waitingTime));
                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "Waiting",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(waitingEnd)}",
                                TimeBlock = currentTime < LunchStart ? "Morning" : "Afternoon",
                                Description = $"Terminal Waiting: {mainJourneyTransport?.Method} Boarding & Security buffer at {mainJourneyTransport?.DepartureHub ?? "Terminal"}"
                            });
                            currentTime = waitingEnd;

                            // 4. Main Inter-City Journey
                            TimeSpan journeyEnd = currentTime.Add(TimeSpan.FromMinutes(mainJourneyTransport.TravelTimeMinutes));

                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "Transport",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(journeyEnd)}",
                                TimeBlock = currentTime < LunchStart ? "Morning" : "Afternoon",
                                Description = $"{mainJourneyTransport.Description} from {mainJourneyTransport.DepartureHub ?? currentDestination} to {mainJourneyTransport.ArrivalHub ?? destinationName} ({Math.Round(distance, 2)} km)",
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
                                Description = $"Arrive at {mainJourneyTransport?.ArrivalHub ?? destinationName + " Terminal"}"
                            });
                            currentTime = arrivalTerminalEnd;
                        }

                        // Set up for new city
                        currentHotel = null;
                        var newDestCenter = GetDestinationCenter(destCandidates);
                        currentLat = newDestCenter.Lat;
                        currentLon = newDestCenter.Lon;
                        currentDestination = destinationName;

                        // 6. Hotel Check-in (for new city) - NOT on day 1 as it's handled above
                        AccommodationResult hotelResult = null;
                        if (dayCounter > 0 && currentHotel == null)
                        {
                            hotelResult = FindNextBestAccommodationWithDetails(
                                currentLat, currentLon, destCandidates, request.GroupSize, accommodationBudgetTonight, null);
                            if (hotelResult != null) currentHotel = hotelResult.Location;
                        }

                        if (currentHotel != null && dayCounter > 0)
                        {
                            double actualCheckinDuration = 30 * (1 + 0.05 * (request.GroupSize - 2));
                            if (actualCheckinDuration < 15) actualCheckinDuration = 15;

                            TimeSpan checkinEnd = currentTime.Add(TimeSpan.FromMinutes(actualCheckinDuration));
                            string checkinDesc = $"Hotel Check-in: {currentHotel.Name}" +
                                                 $" | Check-in: {currentHotel.CheckInTime ?? "14:00"}" +
                                                 $" | Check-out: {currentHotel.CheckOutTime ?? "12:00"}";

                            if (currentTime.Hours < 14) checkinDesc += " (Drop luggage if room not ready)";

                            dailyPlan.Timeline.Add(new TimelineItem
                            {
                                Type = "CheckIn",
                                Time = $"{FormatTime(currentTime)} - {FormatTime(checkinEnd)}",
                                TimeBlock = currentTime < LunchStart ? "Morning" : "Afternoon",
                                Description = checkinDesc,
                                Action = "CheckIn",
                                // Fix 2: Add options directly on CheckIn for new cities
                                AccommodationOptions = hotelResult?.Options,
                                SelectedAccommodationIndex = hotelResult != null ? hotelResult.SelectedIndex : null,
                                AlternativeAccommodations = hotelResult?.AlternativeAccommodations
                            });
                            currentTime = checkinEnd;
                        }
                    }

                    // --- MORNING BLOCK (8:00 - 12:00) ---
                    TimeSpan morningActualEnd = MorningEnd - TimeSpan.FromMinutes(30);
                    while (currentTime < morningActualEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedCountMap, currentTime, currentDate.DayOfWeek,
                            morningActualEnd, request.GroupSize, dailyCeiling - dailyPlan.DailyBudgetStatus.Spent, false);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Morning", dailyPlan, request.GroupSize, dailyCeiling, visitedCountMap, ref currentLat, ref currentLon, tripSegment);
                    }

                    // Fill gap before lunch if any
                    if (currentTime < LunchStart - TimeSpan.FromMinutes(30))
                    {
                        FillTimeGap(currentTime, LunchStart - TimeSpan.FromMinutes(30), dailyPlan, "Morning",
                            currentLat, currentLon, destCandidates, request.GroupSize, dailyCeiling - dailyPlan.DailyBudgetStatus.Spent);
                        currentTime = LunchStart - TimeSpan.FromMinutes(30);
                    }

                    // --- LUNCH BREAK (12:00 - 13:00) ---
                    if (currentTime < LunchEnd)
                    {
                        if (currentTime < LunchStart) currentTime = LunchStart;

                        var lunchPlace = FindNextBestRestLocation(currentLat, currentLon, destCandidates, 
                            new[] { "Restaurant", "LunchRest", "Food" }, request.GroupSize, dailyCeiling - dailyPlan.DailyBudgetStatus.Spent);
                        var cafePlace = FindNextBestRestLocation(currentLat, currentLon, destCandidates, 
                            new[] { "Cafe", "Coffee", "RestArea" }, request.GroupSize, dailyCeiling - dailyPlan.DailyBudgetStatus.Spent);

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
                            var lunchExtraSpend = lunchPlace.Location.AverageBudget * request.GroupSize;
                            dailyPlan.DailyBudgetStatus.Spent += lunchExtraSpend;
                        }
                        currentTime = LunchEnd;
                    }

                    // --- AFTERNOON BLOCK (13:00 - 18:00) ---
                    while (currentTime < AfternoonEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedCountMap, currentTime, currentDate.DayOfWeek,
                            AfternoonEnd, request.GroupSize, dailyCeiling - dailyPlan.DailyBudgetStatus.Spent, false);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Afternoon", dailyPlan, request.GroupSize, dailyCeiling, visitedCountMap, ref currentLat, ref currentLon, tripSegment);
                    }

                    // Fill gap before evening if any
                    if (currentTime < EveningStart - TimeSpan.FromMinutes(30))
                    {
                        FillTimeGap(currentTime, EveningStart - TimeSpan.FromMinutes(30), dailyPlan, "Late Afternoon",
                            currentLat, currentLon, destCandidates, request.GroupSize, dailyCeiling - dailyPlan.DailyBudgetStatus.Spent);
                        currentTime = EveningStart - TimeSpan.FromMinutes(30);
                    }

                    // --- EVENING BLOCK (18:00 - 23:59) ---
                    TimeSpan eveningActualEnd = EveningEnd;
                    while (currentTime < eveningActualEnd)
                    {
                        var bestAttraction = FindNextBestAttraction(
                            currentLat, currentLon, destCandidates, visitedCountMap, currentTime, currentDate.DayOfWeek,
                            eveningActualEnd, request.GroupSize, dailyCeiling - dailyPlan.DailyBudgetStatus.Spent, true);

                        if (bestAttraction == null) break;
                        ProcessAttraction(bestAttraction, ref currentTime, "Evening", dailyPlan, request.GroupSize, dailyCeiling, visitedCountMap, ref currentLat, ref currentLon, tripSegment);
                    }

                    // --- NIGHT REST & ACCOMMODATION ---
                    TimeSpan nightStart = currentTime > EveningEnd ? EveningEnd : currentTime;
                    if (nightStart < EveningStart) nightStart = EveningStart;
                    TimeSpan nightEnd = new TimeSpan(8, 0, 0); // Next day morning

                    double searchLat = currentLat;
                    double searchLon = currentLon;

                    if (d < daysInThisDest - 1)
                    {
                        // Task 5: use visitedCountMap to find unvisited candidates for next-day planning
                        var remainingCandidates = destCandidates.Where(c => !visitedCountMap.ContainsKey(c.Location.Id)).ToList();
                        if (remainingCandidates.Any())
                        {
                            var nextDayCenter = GetDestinationCenter(remainingCandidates);
                            searchLat = (currentLat * 0.7) + (nextDayCenter.Lat * 0.3);
                            searchLon = (currentLon * 0.7) + (nextDayCenter.Lon * 0.3);
                        }
                    }

                    // Task 4: Only search for hotel if user wants one
                    bool needNewHotel = wantHotel && (currentHotel == null ||
                        CalculateDistance(searchLat, searchLon, currentHotel.Latitude, currentHotel.Longitude) > 8.0);

                    List<AccommodationOption> accommodationOptions = null;
                    int selectedAccommodationIndex = 0;
                    List<AlternativeAccommodationDisplay> alternativeAccommodations = null;

                    if (needNewHotel)
                    {
                        var accommodationResult = FindNextBestAccommodationWithDetails(
                            searchLat, searchLon, destCandidates, request.GroupSize, accommodationBudgetTonight, currentHotel, hotelSegment);

                        if (accommodationResult != null)
                        {
                            currentHotel = accommodationResult.Location;
                            accommodationOptions = accommodationResult.Options;
                            selectedAccommodationIndex = accommodationResult.SelectedIndex;

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

                        var accommodationItem = new TimelineItem
                        {
                            Type = "Rest",
                            Time = $"{FormatTime(nightStart)} - {FormatTime(nightEnd)}",
                            TimeBlock = "Night Rest",
                            Description = $"Accommodation: {currentHotel.Name}" +
                                          $" | Check-in: {currentHotel.CheckInTime ?? "14:00"}" +
                                          $" | Check-out: {currentHotel.CheckOutTime ?? "12:00"}" +
                                          (currentHotel.HasLuggageStorage ? $" | Luggage storage available: {currentHotel.LuggageStorageCost:N0} VND/bag" : ""),
                            // Fix 2: expose accommodation cost explicitly in timeline
                            Cost = Math.Round(hotelCost, 0),
                            AccommodationOptions = accommodationOptions,
                            SelectedAccommodationIndex = accommodationOptions != null ? selectedAccommodationIndex : null,
                            AlternativeAccommodations = alternativeAccommodations
                        };

                        // Show continuing stay info
                        if (d > 0 && currentDestination == destinationName)
                        {
                            accommodationItem.Description += " (Continuing stay)";
                        }

                        if (currentHotel.HasLuggageStorage)
                        {
                            accommodationItem.LuggageStorageCost = currentHotel.LuggageStorageCost;
                        }

                        dailyPlan.Timeline.Add(accommodationItem);

                        dailyPlan.DailyBudgetStatus.Spent += hotelCost;

                        currentLat = currentHotel.Latitude;
                        currentLon = currentHotel.Longitude;
                    }

                    // Fill any remaining time gaps to ensure 24h coverage
                    FillRemainingTimeGaps(dailyPlan, request.GroupSize, currentLat, currentLon, destCandidates);

                    dailyPlan.DailyBudgetStatus.Spent = Math.Round(dailyPlan.DailyBudgetStatus.Spent, 2);
                    output.Days.Add(dailyPlan);
                    totalSpent += dailyPlan.DailyBudgetStatus.Spent;

                    // Task 1: Rollover = ceiling minus activity spent (exclude accommodation from rollover calc)
                    double activitySpentToday = dailyPlan.DailyBudgetStatus.Spent - accommodationBudgetTonight;
                    rolloverBudget = dailyBudgetInfo.Ceiling - activitySpentToday;
                    double maxRollover = dailyBudgets[Math.Min(dayCounter + 1, dailyBudgets.Count - 1)].Ceiling * 0.5;
                    if (rolloverBudget > maxRollover) rolloverBudget = maxRollover;
                    if (rolloverBudget < 0) rolloverBudget = 0; // never carry-over debt

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

            // Only fill gaps >= 30 minutes to avoid trivial Rest items
            var gapDuration = endTime - startTime;
            if (gapDuration.TotalMinutes < 30) return;

            // Find a nearby, non-accommodation activity to label the free time
            var nearbyActivity = candidates
                .Select(c => new { Location = c, Distance = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude) })
                .Where(x => x.Distance <= 1.0)
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            string description = nearbyActivity != null
                ? $"Free time / Rest near {nearbyActivity.Location.Location.Name}"
                : "Free time / Rest";

            dailyPlan.Timeline.Add(new TimelineItem
            {
                Type = "Rest",
                Time = $"{FormatTime(startTime)} - {FormatTime(endTime)}",
                TimeBlock = timeBlock,
                Description = description
            });
        }

        private void FillRemainingTimeGaps(DailyItinerary dailyPlan, int groupSize, double lat, double lon, List<ScoredLocation> candidates)
        {
            // Sort timeline by start time, skip already-tagged gap blocks
            var sortedTimeline = dailyPlan.Timeline
                .Where(t => !t.TimeBlock.Equals("Gap") && !t.TimeBlock.Equals("Early Morning") && !t.TimeBlock.Equals("Late Night"))
                .OrderBy(t => ParseTime(t.Time.Split(" - ")[0]))
                .ToList();

            // Remove stale gap items
            var itemsToRemove = dailyPlan.Timeline
                .Where(t => t.TimeBlock.Equals("Gap") || t.TimeBlock.Equals("Early Morning") || t.TimeBlock.Equals("Late Night"))
                .ToList();
            foreach (var item in itemsToRemove) dailyPlan.Timeline.Remove(item);

            TimeSpan? previousEndTime = null;

            foreach (var item in sortedTimeline)
            {
                var times = item.Time.Split(" - ");
                var startTime = ParseTime(times[0]);
                var endTime = ParseTime(times[1]);

                if (previousEndTime.HasValue && startTime > previousEndTime.Value)
                {
                    var gapDuration = startTime - previousEndTime.Value;
                    // Issue 4: Only add FREE TIME gap if >= 90 minutes (suppress minor gaps, maximize activities)
                    if (gapDuration.TotalMinutes >= 90)
                    {
                        FillTimeGap(previousEndTime.Value, startTime, dailyPlan, "Free Time", lat, lon, candidates, groupSize, 1_000_000);
                    }
                }

                previousEndTime = endTime;
            }

            // Issue 4: Fill gap from last activity to 22:00 only if gap >= 2 hours (reduce rest time)
            // Skip this if the last block is Night Rest (accommodation) to avoid giant "08:00-23:00" rest
            var lastNonRestItem = sortedTimeline.LastOrDefault(t => t.Type != "Rest" || t.TimeBlock == "Night Rest");
            if (previousEndTime.HasValue
                && previousEndTime.Value < new TimeSpan(22, 0, 0)
                && lastNonRestItem?.TimeBlock != "Night Rest")
            {
                var gapDuration = new TimeSpan(22, 0, 0) - previousEndTime.Value;
                if (gapDuration.TotalMinutes >= 120)
                {
                    FillTimeGap(previousEndTime.Value, new TimeSpan(22, 0, 0), dailyPlan, "Evening Free Time", lat, lon, candidates, groupSize, 1_000_000);
                }
            }

            // Issue 4: Fill gap from start of day (08:00) to first activity only if gap >= 60 minutes
            if (sortedTimeline.Any())
            {
                var firstStartTime = ParseTime(sortedTimeline.First().Time.Split(" - ")[0]);
                if (firstStartTime > MorningStart)
                {
                    var gapDuration = firstStartTime - MorningStart;
                    if (gapDuration.TotalMinutes >= 60)
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
            double accommodationBudget, Location currentHotel, string hotelSegment = "midrange")
        {
            var accommodationTags = new[] { "Hotel", "Guesthouse", "Hostel", "Homestay", "Accommodation", "Resort", "Villa" };

            // Task 4: apply segment-based price filter per room per night
            (double minPrice, double maxPrice) = hotelSegment?.ToLowerInvariant() switch
            {
                "budget"  => (0,          500_000),
                "luxury"  => (2_000_000,  double.MaxValue),
                _         => (500_000,    2_000_000)  // midrange default
            };

            var accommodations = candidates
                .Where(c => c.Location.Tags.Any(t => accommodationTags.Contains(t, StringComparer.OrdinalIgnoreCase)))
                .Select(c => new
                {
                    Location = c.Location,
                    Distance = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude),
                    OriginalScore = c.Score
                })
                .ToList();

            // Task 4: filter by segment price range; fallback to all if no match
            var segmentFiltered = accommodations
                .Where(a => a.Location.AverageBudget >= minPrice && a.Location.AverageBudget <= maxPrice)
                .ToList();
            if (!segmentFiltered.Any())
                segmentFiltered = accommodations; // fallback: use all

            if (!segmentFiltered.Any()) return null;

            // Generate room options for each segment-filtered accommodation
            var accommodationWithOptions = segmentFiltered.Select(a => new
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
                AlternativeAccommodations = topCandidates.Skip(1).Take(4).Select(a => new AlternativeAccommodationDisplay
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
            public List<AlternativeAccommodationDisplay> AlternativeAccommodations { get; set; }
        }

        // ... rest of the existing methods (ProcessAttraction, IsEveningActivity, etc.)
        // Keeping them unchanged for brevity but they would be included in the actual file

        private void ProcessAttraction(BestAttraction bestAttraction, ref TimeSpan currentTime, string block,
            DailyItinerary dailyPlan, int groupSize, double dailyCeiling,
            Dictionary<int, int> visitedCountMap, ref double currentLat, ref double currentLon,
            string tripSegment)
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

            // Task 3: Separate ticket cost from discretionary extra spending
            double ticketCost = bestAttraction.Location.AverageBudget * groupSize;
            double extraSpendingCost = CalculateLocationSpendingBudget(bestAttraction.Location, groupSize, tripSegment);

            dailyPlan.Timeline.Add(new TimelineItem
            {
                Type = "Visit",
                Time = $"{FormatTime(arrivalTime)} - {FormatTime(visitEndTime)}",
                TimeBlock = block,
                Description = $"Visit {bestAttraction.Location.Name}",
                TicketCost = Math.Round(ticketCost, 2),
                ExtraSpendingCost = Math.Round(extraSpendingCost, 2),
                GroupDiscountApplied = groupSize >= 5
            });

            dailyPlan.DailyBudgetStatus.Spent += defaultTransport.TotalCost + ticketCost + extraSpendingCost;

            // Task 5: Track visit count (max 1 per location across entire itinerary)
            visitedCountMap.TryGetValue(bestAttraction.Location.Id, out int currentCount);
            visitedCountMap[bestAttraction.Location.Id] = currentCount + 1;

            currentLat = bestAttraction.Location.Latitude;
            currentLon = bestAttraction.Location.Longitude;
            currentTime = visitEndTime;
        }

        /// <summary>
        /// Task 3: Calculate estimated discretionary spending (food, souvenirs, incidentals)
        /// at a location based on trip segment and location type tags.
        /// Does NOT include ticket/entry cost (that is AverageBudget).
        /// </summary>
        private double CalculateLocationSpendingBudget(Location loc, int groupSize, string tripSegment)
        {
            // Base spending per person per visit by segment
            double basePerson = tripSegment?.ToLowerInvariant() switch
            {
                "budget"   => 50_000,
                "luxury"   => 400_000,
                _          => 150_000   // midrange default
            };

            // Multiplier by location type
            bool isShopping = loc.Tags.Any(t => new[] { "Shopping", "Food", "Market", "Restaurant" }
                                                    .Contains(t, StringComparer.OrdinalIgnoreCase));
            bool isMuseumPark = loc.Tags.Any(t => new[] { "Museum", "History", "Park", "Nature", "Religion" }
                                                      .Contains(t, StringComparer.OrdinalIgnoreCase));

            double multiplier = isShopping ? 1.5 : isMuseumPark ? 0.5 : 1.0;

            return Math.Round(basePerson * multiplier * groupSize, 0);
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
        /// <returns>List of transport options</returns>
        private List<TransportOption> GetInterCityTransportOptions(double distance, int groupSize, string fromDest, string toDest, List<ScoredLocation> candidates = null)
        {
            var options = new List<TransportOption>();

            // Get airport/station info from province data cache
            string fromAirportName = "City Airport";
            string toAirportName = "City Airport";
            string fromStationName = "Central Station";
            string toStationName = "Central Station";

            // Use province data cache for airport/station names
            var fromAirports = GetAirportsForDestination(fromDest);
            var toAirports = GetAirportsForDestination(toDest);
            var fromStations = GetTrainStationsForDestination(fromDest);
            var toStations = GetTrainStationsForDestination(toDest);

            if (fromAirports.Any()) fromAirportName = fromAirports.First().Name;
            if (toAirports.Any()) toAirportName = toAirports.First().Name;
            if (fromStations.Any()) fromStationName = fromStations.First().Name;
            if (toStations.Any()) toStationName = toStations.First().Name;

            // Bus/Coach: Best for short distances (< 300km)
            if (distance < 300)
            {
                double busCost = 200000 * groupSize;
                double busTime = (distance / 45.0) * 60.0;
                options.Add(new TransportOption
                {
                    Method = "Bus/Coach",
                    Description = $"Bus / Coach from {fromDest} to {toDest}",
                    TotalCost = Math.Round(busCost, 2),
                    TravelTimeMinutes = Math.Round(busTime, 2),
                    VehiclesNeeded = 1,
                    Pros = "Most economical, direct route, frequent departures",
                    Cons = "Slower, less comfortable for long distances",
                    Recommended = distance < 150 || groupSize > 10,
                    GroupSize = groupSize,
                    DepartureHub = $"{fromDest} Bus Station",
                    ArrivalHub = $"{toDest} Bus Station"
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
                    Description = $"Train from {fromStationName} ({fromDest}) to {toStationName} ({toDest})",
                    TotalCost = Math.Round(trainCost, 2),
                    TravelTimeMinutes = Math.Round(trainTime, 2),
                    VehiclesNeeded = 1,
                    Pros = $"Comfortable, scenic views, departs from {fromStationName}, arrives at {toStationName}",
                    Cons = "Fixed schedule, may be delayed, limited routes",
                    Recommended = (distance >= 200 && distance <= 500) || (distance > 600 && groupSize <= 4),
                    GroupSize = groupSize,
                    DepartureHub = fromStationName,
                    ArrivalHub = toStationName
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
                    Description = $"Flight from {fromAirportName} ({fromDest}) to {toAirportName} ({toDest})",
                    TotalCost = Math.Round(flightCost, 2),
                    TravelTimeMinutes = Math.Round(flightTime, 2),
                    VehiclesNeeded = 1,
                    Pros = $"Fastest option, departs from {fromAirportName}, arrives at {toAirportName}",
                    Cons = "Most expensive, airport transfers needed, security checks, weather dependent",
                    Recommended = distance > 700 || (distance > 500 && groupSize <= 4),
                    GroupSize = groupSize,
                    DepartureHub = fromAirportName,
                    ArrivalHub = toAirportName
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
                    Description = $"{vansNeeded} x 16-seat van from {fromDest} to {toDest}",
                    TotalCost = Math.Round(vanCost, 2),
                    TravelTimeMinutes = Math.Round(vanTime, 2),
                    VehiclesNeeded = vansNeeded,
                    Pros = "Flexible schedule, door-to-door, group stays together, luggage space",
                    Cons = "Driver fatigue on long trips, road conditions dependent",
                    Recommended = (groupSize > 4 && groupSize <= 16) && distance < 250,
                    GroupSize = groupSize,
                    DepartureHub = fromDest,
                    ArrivalHub = toDest
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

            // Exclude accommodation-type locations from the activity candidate pool
            // (hotels/hostels should only appear via FindNextBestAccommodationWithDetails)
            var accommodationTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "Hotel", "Guesthouse", "Hostel", "Homestay", "Accommodation", "Resort", "Villa", "Province", "Destination" };

            // Requirement 1: Score locations to maximize number of visits
            // - Prefer locations with shorter stay durations (more locations can be visited)
            // - Still consider quality (tag matches with user favorites)
            // - Prefer locations with lower cost (fit more within budget)
            return allLocations
                .Where(l => normalizedDestinations.Contains(l.Destination, StringComparer.OrdinalIgnoreCase))
                .Where(l => !l.Tags.Any(t => accommodationTags.Contains(t))) // exclude hotels from activity pool
                .Select(l =>
                {
                    // Base score from tag matching
                    int tagScore = favoriteTags == null ? 50 : l.Tags.Intersect(favoriteTags, StringComparer.OrdinalIgnoreCase).Count() * 10 + 50;

                    // Time efficiency score: prefer shorter stays (allows more locations to be visited)
                    // Normalize: assume 30min-4hr range, shorter = higher score
                    double stayDurationMinutes = l.AverageStayDuration > 0 ? l.AverageStayDuration : 60;
                    double timeEfficiencyScore = Math.Max(0, 100 - (stayDurationMinutes - 30) / 3);

                    // Cost efficiency: prefer lower cost locations (more locations within budget)
                    double costEfficiencyScore = Math.Max(0, 100 - l.AverageBudget / 5000);

                    // Composite score: 40% quality, 35% time efficiency, 25% cost efficiency
                    double compositeScore = tagScore * 0.4 + timeEfficiencyScore * 0.35 + costEfficiencyScore * 0.25;

                    return new ScoredLocation
                    {
                        Location = l,
                        Score = (int)Math.Round(compositeScore)
                    };
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
            string previousDest = null;

            foreach (var dest in orderedDestinations)
            {
                var destCenter = GetDestinationCenter(candidates.Where(c =>
                    c.Location.Destination.Equals(dest, StringComparison.OrdinalIgnoreCase)).ToList());
                double distance = CalculateDistance(currentLat, currentLon, destCenter.Lat, destCenter.Lon);

                // For budget calculation, candidate exact hubs don't matter, passing null is fine
                var options = GetInterCityTransportOptions(distance, groupSize, previousDest, dest, null);
                var recommended = options.FirstOrDefault(o => o.Recommended) ?? options.FirstOrDefault();

                if (recommended != null)
                {
                    totalBudget += recommended.TotalCost;
                }

                currentLat = destCenter.Lat;
                currentLon = destCenter.Lon;
                previousDest = dest;
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
            double lat, double lon, List<ScoredLocation> candidates, Dictionary<int, int> visitedCountMap,
            TimeSpan currentTime, DayOfWeek dayOfWeek, TimeSpan dayEndTime, int groupSize,
            double remainingCeilingBudget, bool isEvening)
        {
            double r = 2.0;
            List<ScoredLocation> nearby = new List<ScoredLocation>();

            while (r <= 15.0)
            {
                nearby = candidates
                    // Task 5: only allow locations not yet visited (count == 0)
                    .Where(c => !visitedCountMap.ContainsKey(c.Location.Id))
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

            double remainingMinutes = (dayEndTime - currentTime).TotalMinutes;

            var validAttractions = nearby
                .Select(c => {
                    double dist = CalculateDistance(lat, lon, c.Location.Latitude, c.Location.Longitude);
                    var transport = OptimizeTransport(dist, groupSize);
                    var transportDescription = transport.Description.Split(' ').FirstOrDefault();
                    double delayBuffer = GetDelayBufferForTransport(transportDescription ?? "");
                    TimeSpan arrivalTime = currentTime.Add(TimeSpan.FromMinutes(transport.TravelTimeMinutes + delayBuffer));

                    double actualStayTime = c.Location.AverageStayDuration * (1 + 0.05 * (groupSize - 2));
                    TimeSpan visitEndTime = arrivalTime.Add(TimeSpan.FromMinutes(actualStayTime));

                    double totalTime = transport.TravelTimeMinutes + delayBuffer + actualStayTime;

                    // Requirement 1: time-efficiency score — prefer shorter activities to maximize number of locations
                    double timeEfficiency = remainingMinutes > 0
                        ? Math.Min(1.0, (remainingMinutes - totalTime) / Math.Max(remainingMinutes, 1))
                        : 0;

                    // Normalize distance score: 0-100, closer = higher
                    double distanceScore = Math.Max(0, 100 - dist * 10);

                    // Requirement 2: Calculate extra spending cost for service locations
                    double ticketCost = c.Location.AverageBudget * groupSize;
                    double extraSpendingCost = CalculateLocationSpendingBudget(c.Location, groupSize, "midrange");
                    double totalActivityCost = ticketCost + extraSpendingCost;

                    // Composite score: 40% quality, 30% distance, 30% time-efficiency
                    double compositeScore = c.Score * 0.4 + distanceScore * 0.3 + timeEfficiency * 100 * 0.3;

                    return new {
                        ScoredLocation = c,
                        Distance = dist,
                        Transport = transport,
                        ArrivalTime = arrivalTime,
                        VisitEndTime = visitEndTime,
                        IsOpen = IsLocationOpen(c.Location, dayOfWeek, arrivalTime, visitEndTime),
                        Cost = transport.TotalCost + totalActivityCost,
                        CompositeScore = compositeScore
                    };
                })
                // Task 1: enforce ceiling — cost must fit within remaining ceiling budget
                .Where(x => x.IsOpen && x.VisitEndTime <= dayEndTime && x.Cost <= remainingCeilingBudget)
                // Task 2: order by composite score (quality + proximity + time-fit)
                .OrderByDescending(x => x.CompositeScore)
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
                    GroupSize = groupSize,
                    DepartureHub = "",
                    ArrivalHub = ""
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
                        GroupSize = groupSize,
                        DepartureHub = "",
                        ArrivalHub = ""
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
                        GroupSize = groupSize,
                        DepartureHub = "",
                        ArrivalHub = ""
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

        /// <summary>
        /// Get extra spending cost based on trip segment and location type.
        /// This is for discretionary spending at service locations (food, souvenirs, activities).
        /// </summary>
        private double GetExtraSpendingCost(string tripSegment, Location location)
        {
            // Base amount depends on segment
            (double min, double max) = tripSegment.ToLowerInvariant() switch
            {
                "budget" => _budgetExtraSpending,
                "luxury" => _luxuryExtraSpending,
                _ => _midrangeExtraSpending // midrange default
            };

            // Adjust based on location type
            double multiplier = location.Tags.Any(t => 
                new[] { "Restaurant", "Cafe", "Food", "Entertainment", "Shopping", "Market" }
                .Contains(t, StringComparer.OrdinalIgnoreCase)) 
                ? 1.2 : 1.0;

            // Return random value within range * multiplier
            var random = new Random(Guid.NewGuid().GetHashCode());
            return (min + random.NextDouble() * (max - min)) * multiplier;
        }

        /// <summary>
        /// Get airports for a destination with flexible key lookup
        /// </summary>
        private List<AirportInfo> GetAirportsForDestination(string destination)
        {
            if (string.IsNullOrEmpty(destination)) return new List<AirportInfo>();
            
            // Try multiple key formats
            var keys = new[] { 
                destination.ToLowerInvariant(),
                destination.ToLowerInvariant().Replace(" ", ""),
                destination.ToLowerInvariant().Replace(" ", "_"),
                NormalizeDestinationName(destination)
            };

            foreach (var key in keys)
            {
                if (_provinceDataCache.TryGetValue(key, out var data) && data.Airports.Any())
                {
                    return data.Airports;
                }
            }

            return new List<AirportInfo>();
        }

        /// <summary>
        /// Get train stations for a destination with flexible key lookup
        /// </summary>
        private List<TrainStationInfo> GetTrainStationsForDestination(string destination)
        {
            if (string.IsNullOrEmpty(destination)) return new List<TrainStationInfo>();
            
            // Try multiple key formats
            var keys = new[] { 
                destination.ToLowerInvariant(),
                destination.ToLowerInvariant().Replace(" ", ""),
                destination.ToLowerInvariant().Replace(" ", "_"),
                NormalizeDestinationName(destination)
            };

            foreach (var key in keys)
            {
                if (_provinceDataCache.TryGetValue(key, out var data) && data.TrainStations.Any())
                {
                    return data.TrainStations;
                }
            }

            return new List<TrainStationInfo>();
        }

        /// <summary>
        /// Normalize destination name for cache lookup
        /// </summary>
        private string NormalizeDestinationName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            
            // Handle common destination name variations
            var normalized = name.ToLowerInvariant();
            if (normalized.Contains("ho chi minh") || normalized.Contains("hcmc") || normalized.Contains("saigon"))
                return "hcmc";
            if (normalized.Contains("ha noi") || normalized.Contains("hanoi"))
                return "hanoi";
            if (normalized.Contains("da nang"))
                return "da nang";
            if (normalized.Contains("hue"))
                return "hue";
            if (normalized.Contains("hoi an"))
                return "hoi an";
            
            return normalized;
        }

public class ScoredLocation
        {
            public Location Location { get; set; }
            public int Score { get; set; }
        }

public class BestAttraction
        {
            public Location Location { get; set; }
            public double Distance { get; set; }
        }

        public class VehicleType
        {
            public string Name { get; set; }
            public int Capacity { get; set; }
            public double CostPerKm { get; set; }
            public double SpeedKmh { get; set; }
            public bool IsWalking { get; set; }
        }

public class TransportOptimization
        {
            public string Description { get; set; }
            public double TotalCost { get; set; }
            public double TravelTimeMinutes { get; set; }
        }

        public class ProvinceData
        {
            public string Name { get; set; }
            public string EnglishName { get; set; }
            public Location Location { get; set; }
            public List<AirportInfo> Airports { get; set; } = new List<AirportInfo>();
            public List<TrainStationInfo> TrainStations { get; set; } = new List<TrainStationInfo>();
        }

        public class AirportInfo
        {
            public string Name { get; set; }
            public string EnglishName { get; set; }
            public string IataCode { get; set; }
            public string CityName { get; set; }
            public double Distance { get; set; }
        }

        public class TrainStationInfo
        {
            public string Name { get; set; }
            public string EnglishName { get; set; }
            public string CityName { get; set; }
        }
    }
}
