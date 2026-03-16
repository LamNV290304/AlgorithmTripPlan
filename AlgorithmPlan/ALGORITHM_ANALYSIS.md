# 📊 Phân Tích Chi Tiết Thuật Toán Lập Kế Hoạch Du Lịch Thông Minh

## Tổng Quan

Hệ thống sử dụng thuật toán **Dynamic Budget Partitioning** kết hợp với **Multi-Factor Location Scoring** để tạo lịch trình du lịch tối ưu cho nhóm khách dựa trên ngân sách, sở thích và thời gian.

---

## 🏗️ Kiến Trúc Thuật Toán

```
┌─────────────────────────────────────────────────────────────────┐
│                    INPUT (ItineraryRequest)                     │
│  - Destinations, GroupSize, TotalBudget, Start/End Date        │
│  - UserFavoriteTags, HotelPreference, TripSegment              │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  MODULE 3.0: Validate Destinations                              │
│  → Kiểm tra điểm đến có tồn tại trong database                  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  MODULE 3.1: Dynamic Budget Partitioning                        │
│  → Chia ngân sách thành: Contingency, Transport, Hotel, Activity│
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  MODULE 3.2: Location Filtering & Scoring                       │
│  → Lọc và chấm điểm địa điểm dựa trên sở thích người dùng       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  MODULE 3.3: Destination Ordering & Day Allocation              │
│  → Sắp xếp thứ tự điểm đến và phân bổ ngày                      │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│  MODULE 3.4: Daily Itinerary Generation                         │
│  → Tạo timeline chi tiết cho mỗi ngày với CheckIn/CheckOut     │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    OUTPUT (SmartItineraryOutput)                │
│  - Timeline chi tiết, Room Options, Transport Options           │
│  - Budget Summary, Contingency Fund                             │
└─────────────────────────────────────────────────────────────────┘
```

---

## 💰 Cơ Chế Chia Budget (Dynamic Budget Partitioning)

### 1. Tính Quỹ Dự Phòng (Contingency Fund)

**Mục đích:** Dự phòng cho chi phí phát sinh (ốm đau, thay đổi kế hoạch, mua sắm ngoài dự kiến)

```csharp
private double CalculateContingencyPercentage(double totalBudget)
{
    // Ngân sách càng cao → tỷ lệ dự phòng càng thấp
    if (totalBudget < 5.000.000)   return 20%; // 20% cho budget thấp
    if (totalBudget < 10.000.000)  return 15%; // 15% cho budget trung bình
    if (totalBudget < 20.000.000)  return 10%; // 10% cho budget khá
    if (totalBudget < 50.000.000)  return 8%;  // 8% cho budget cao
    return 5%;                                  // 5% cho budget rất cao
}
```

**Công thức:**
```
usableBudget = totalBudget - (totalBudget × contingencyPercentage)
```

### 2. Tính Chi Phí Vận Chuyển Liên Tỉnh

**Cơ chế lựa chọn phương tiện:**

| Khoảng Cách | Phương Tiện Ưu Tiên | Giá Tham Khảo |
|-------------|---------------------|---------------|
| < 150km     | Bus/Coach           | 200.000đ/người |
| 150-300km   | Bus/Coach hoặc Train | 300.000-500.000đ/người |
| 300-600km   | Train               | 500.000-800.000đ/người |
| 600-1000km  | Train hoặc Airplane | 800.000-1.200.000đ/người |
| > 1000km    | Airplane            | 1.200.000-2.500.000đ/người |

**Yếu tố ảnh hưởng:**
- **Group Size:** Nhóm > 4 người → ưu tiên máy bay để tiết kiệm thời gian
- **Budget:** Budget thấp → chọn phương tiện rẻ nhất

### 3. Tính Chi Phí Lưu Trú

**Phân loại theo Hotel Preference:**

| Segment | Giá/Đêm/Phòng | Loại Hình |
|---------|---------------|-----------|
| `budget` | ≤ 500.000đ | Nhà nghỉ, Homestay, Guesthouse |
| `midrange` | 500.000đ - 2.000.000đ | Khách sạn 3-4 sao |
| `luxury` | ≥ 2.000.000đ | Resort, Villa 5 sao |

**Công thức:**
```
totalAccommodationBudget = Σ(nightsInDest × avgHotelCostPerNight × groupSize)
```

### 4. Phân Bổ Budget Cho Hoạt Động

**Sau khi trừ các chi phí cố định:**
```
activityBudget = usableBudget - totalTransportBudget - totalAccommodationBudget
```

**Phân bổ theo điểm đến:**
```csharp
// Trọng số dựa trên số ngày và số lượng địa điểm
double weight = days × √(attractionCount);
destinationBudget = (weight / totalWeight) × activityBudget;
```

### 5. Daily Budget với Limit/Floor

**Cơ chế linh hoạt theo ngày:**

```csharp
// Ngày đầu và ngày cuối có xu hướng chi tiêu nhiều hơn
if (d == 0) weight = 1.3;  // +30% (háo hức, mua sắm)
if (d == 1) weight = 1.1;  // +10% (vẫn còn năng lượng)
if (d == last) weight = 1.2; // +20% (quà lưu niệm)

// Limit: giới hạn trên (+30%)
limit = weightedBudget × 1.3

// Floor: giới hạn dưới (-30%)
floor = weightedBudget × 0.7
```

**Rollover Budget (cuộn sang ngày sau):**
```
rollover = limit - activitySpentToday
maxRollover = nextDayLimit × 0.5  // Tối đa 50% ngân sách ngày hôm sau
```

---

## 🎯 Cơ Chế Gợi Ý Địa Điểm (Multi-Factor Scoring)

### 1. Lọc Địa Điểm

**Loại trừ khách sạn khỏi danh sách tham quan:**
```csharp
var accommodationTags = {
    "Hotel", "Guesthouse", "Hostel", 
    "Homestay", "Accommodation", "Resort", "Villa"
};

candidates = allLocations
    .Where(l => l.Destination in requestedDestinations)
    .Where(l => l.Tags NOT IN accommodationTags);  // ✅ Chỉ lấy địa điểm tham quan
```

### 2. Chấm Điểm Composite

**3 yếu tố chính:**

```
┌────────────────────────────────────────────────────────────┐
│  Composite Score = 40% Quality + 35% Time + 25% Cost      │
└────────────────────────────────────────────────────────────┘
```

#### a) Quality Score (40%)
```csharp
// Dựa trên khớp với sở thích người dùng
int tagScore = favoriteTags == null ? 50 
    : matchingTags.Count × 10 + 50;

// Ví dụ: User thích ["Food", "Culture"]
// Location có tags ["Food", "History"] → 1 match → tagScore = 60
```

#### b) Time Efficiency Score (35%)
```csharp
// Ưu tiên địa điểm có thời gian tham quan ngắn → thăm được nhiều nơi
double stayDuration = location.AverageStayDuration > 0 
    ? location.AverageStayDuration : 60;

double timeEfficiencyScore = Max(0, 100 - (stayDuration - 30) / 3);

// 30 phút → 100 điểm (tối ưu)
// 90 phút → 80 điểm
// 150 phút → 60 điểm
// 270 phút → 20 điểm
```

#### c) Cost Efficiency Score (25%)
```csharp
// Ưu tiên địa điểm có chi phí thấp → tiết kiệm budget
double costEfficiencyScore = Max(0, 100 - averageBudget / 5000);

// Miễn phí → 100 điểm
// 50.000đ → 90 điểm
// 250.000đ → 50 điểm
// 500.000đ → 0 điểm
```

### 3. Thuật Toán Tìm Địa Điểm Tiếp Theo

**Tìm trong bán kính tăng dần:**

```csharp
double radius = 2.0; // Bắt đầu từ 2km
while (radius <= 15.0)
{
    nearby = candidates
        .Where(c => !visited)  // Chưa thăm
        .Where(c => distance <= radius)
        .ToList();
    
    if (nearby.Count >= 3 || radius >= 15.0) break;
    radius += 2.0;  // Mở rộng bán kính
}
```

**Lọc theo thời gian còn trong ngày:**
```csharp
var validAttractions = nearby
    .Select(c => {
        totalTime = travelTime + delayBuffer + stayDuration;
        
        // Chỉ chọn nếu vừa với thời gian còn lại
        timeEfficiency = (remainingMinutes - totalTime) / remainingMinutes;
        
        // Chỉ chọn nếu vừa với budget còn lại
        fitsBudget = (transportCost + ticketCost + extraSpending) <= remainingBudget;
        
        return new {
            c,
            compositeScore = c.Score × 0.4 + distanceScore × 0.3 + timeEfficiency × 0.3,
            fitsBudget,
            isOpen
        };
    })
    .Where(x => x.fitsBudget && x.isOpen && x.visitEndTime <= dayEndTime)
    .OrderByDescending(x => x.compositeScore)
    .FirstOrDefault();
```

---

## 📅 Cơ Chế Sắp Xếp Lịch Trình

### 1. Xác Định Thứ Tự Điểm Đến

**Thuật toán Nearest Neighbor với trọng số:**

```csharp
// Bắt đầu từ vị trí hiện tại
currentLocation = startPoint;

while (destinations.Any())
{
    nextDest = destinations
        .Select(d => {
            center = GetDestinationCenter(d);
            distance = CalculateDistance(currentLocation, center);
            
            // Ưu tiên: gần + nhiều địa điểm hấp dẫn
            attractionCount = candidates.Count(c => c.Destination == d);
            score = attractionCount / distance;
            
            return new { d, score };
        })
        .OrderByDescending(x => x.score)
        .First();
    
    ordered.Add(nextDest.d);
    currentLocation = nextDest.center;
    destinations.Remove(nextDest.d);
}
```

### 2. Phân Bổ Ngày Cho Điểm Đến

```csharp
// Dựa trên số lượng địa điểm và khoảng cách
foreach (dest in orderedDestinations)
{
    int attractionCount = candidates.Count(c => c.Destination == dest);
    double weight = days × √(attractionCount);
    destinationWeights[dest] = weight;
}

// Chuẩn hóa thành số ngày
totalWeight = Sum(destinationWeights.Values);
foreach (dest in destinationWeights.Keys)
{
    daysInDest = (destinationWeights[dest] / totalWeight) × totalDays;
}
```

### 3. Timeline Chi Tiết Mỗi Ngày

**Cấu trúc timeline chuẩn:**

```
┌─────────────────────────────────────────────────────────────┐
│  Morning (08:00 - 12:00)                                   │
│  ├─ Check-in (nếu ngày đầu)                                │
│  ├─ Activity 1 (08:30 - 10:00)                             │
│  ├─ Activity 2 (10:30 - 12:00)                             │
├─────────────────────────────────────────────────────────────┤
│  Lunch Break (12:00 - 13:00)                               │
├─────────────────────────────────────────────────────────────┤
│  Afternoon (13:00 - 18:00)                                 │
│  ├─ Activity 3 (13:30 - 15:00)                             │
│  ├─ Activity 4 (15:30 - 17:00)                             │
│  └─ Free Time (17:00 - 18:00)                              │
├─────────────────────────────────────────────────────────────┤
│  Evening (18:00 - 22:00)                                   │
│  ├─ Dinner (18:00 - 19:00)                                 │
│  ├─ Evening Activity (19:30 - 21:00)                       │
│  └─ Free Time (21:00 - 22:00)                              │
├─────────────────────────────────────────────────────────────┤
│  Night Rest (22:00 - 08:00)                                │
│  └─ Accommodation với Room Options                         │
└─────────────────────────────────────────────────────────────┘
```

### 4. Cơ Chế Check-In/Check-Out

**Ngày đầu tiên:**
```
08:00 - 08:30  Check-in khách sạn (gửi hành lý)
08:30 - 12:00  Tham quan địa điểm 1, 2
```

**Ngày chuyển thành phố:**
```
08:00 - 08:30  Check-out khách sạn cũ
08:30 - 09:00  Di chuyển ra ga/sân bay
09:00 - 11:00  Chờ và di chuyển liên tỉnh
11:00 - 11:15  Đến ga/sân bay mới
11:15 - 12:00  Check-in khách sạn mới
12:00 - 13:00  Ăn trưa
13:00 - 18:00  Tham quan
```

**Ngày ở cùng khách sạn:**
```
08:00 - 12:00  Tham quan
12:00 - 13:00  Ăn trưa
13:00 - 18:00  Tham quan
18:00 - 22:00  Ăn tối + hoạt động tối
22:00 - 08:00  Night Rest (giữ nguyên khách sạn)
```

---

## 🏨 Cơ Chế Gợi Ý Khách Sạn & Room Types

### 1. Lọc Khách Sạn Theo Segment

```csharp
(minPrice, maxPrice) = hotelSegment switch
{
    "budget"  => (0, 500.000),
    "luxury"  => (2.000.000, double.MaxValue),
    _         => (500.000, 2.000.000)  // midrange
};

hotels = allLocations
    .Where(h => h.Tags.Contains("Hotel") || h.Tags.Contains("Accommodation"))
    .Where(h => h.AverageBudget >= minPrice && h.AverageBudget <= maxPrice);
```

### 2. Chấm Điểm Khách Sạn

**4 yếu tố:**

```
┌────────────────────────────────────────────────────────────┐
│  Hotel Score = 25% Distance + 35% Budget +               │
│                25% GroupSize + 15% Amenities              │
└────────────────────────────────────────────────────────────┘
```

```csharp
double distanceScore = Max(0, 100 - distance × 15);
double budgetScore = Max(0, 100 - (totalCost / budget × 100));
double groupSizeScore = CalculateGroupSizeSuitabilityScore(hotel, groupSize);
double amenitiesScore = CalculateAmenitiesScore(hotel);

double totalScore = distanceScore × 0.25 + budgetScore × 0.35 
                  + groupSizeScore × 0.25 + amenitiesScore × 0.15;
```

### 3. Generate Room Options

**Từ dữ liệu RoomTypes trong database:**

```json
{
  "RoomTypes": [
    {
      "Name": "Standard Double",
      "Description": "Cozy room for couples",
      "MaxOccupancy": 2,
      "PricePerNight": 850000,
      "PricePerHour": 150000,
      "AvailableRooms": 5,
      "Amenities": ["WiFi", "AC", "Mini Bar", "Breakfast"]
    },
    {
      "Name": "Family Suite",
      "Description": "Spacious room for families",
      "MaxOccupancy": 4,
      "PricePerNight": 1500000,
      "AvailableRooms": 3,
      "Amenities": ["WiFi", "AC", "Mini Bar", "Breakfast", "Bathtub"]
    }
  ]
}
```

**Tính số phòng cần:**
```csharp
foreach (room in hotel.RoomTypes)
{
    int roomsNeeded = Ceiling(groupSize / room.MaxOccupancy);
    double totalCost = roomsNeeded × room.PricePerNight;
    
    // Xác định room được recommend
    bool isRecommended = room.MaxOccupancy >= groupSize 
                      && room.MaxOccupancy <= groupSize + 2
                      && totalCost <= budget;
    
    options.Add(new AccommodationOption {
        RoomType = room.Name,
        PricePerNight = room.PricePerNight,
        MaxOccupancy = room.MaxOccupancy,
        RoomsNeeded = roomsNeeded,
        TotalCost = totalCost,
        Amenities = room.Amenities,
        Recommended = isRecommended,
        Pros = GeneratePros(room, groupSize, totalCost),
        Cons = GenerateCons(room, groupSize, totalCost)
    });
}
```

### 4. Alternative Accommodations

**Hiển thị 3-5 khách sạn khác để so sánh:**

```csharp
var topCandidates = scoredHotels.Take(5).ToList();

var bestChoice = topCandidates.First();
var alternatives = topCandidates.Skip(1).Take(4).Select(a => 
    new AlternativeAccommodationDisplay {
        Name = a.Location.Name,
        Distance = a.Distance,
        RecommendedRoomType = a.RecommendedOption.RoomType,
        TotalCost = a.RecommendedOption.TotalCost,
        Options = a.Options  // Tất cả room types của khách sạn này
    }
).ToList();
```

---

## 🚐 Cơ Chế Gợi Ý Phương Tiện

### 1. Transport Options với Pros/Cons

**Local Transport (< 50km):**

```csharp
var options = new List<TransportOption>();

// Walking (miễn phí)
if (distance < 1.0) {
    options.Add(new TransportOption {
        Method = "Walking",
        TotalCost = 0,
        TravelTimeMinutes = distance / 4 × 60,
        Pros = "Free, eco-friendly, good for health",
        Cons = "Slow, only for short distances",
        Recommended = true
    });
}

// Taxi 4-seat
options.Add(new TransportOption {
    Method = "Taxi 4-seat",
    TotalCost = 15.000 × distance × vehiclesNeeded,
    TravelTimeMinutes = distance / 30 × 60,
    VehiclesNeeded = Ceiling(groupSize / 4),
    Pros = "Fast, comfortable, door-to-door",
    Cons = "More expensive for large groups",
    Recommended = groupSize <= 4 && distance < 50
});

// 7-seat vehicle
options.Add(new TransportOption {
    Method = "7-seat vehicle",
    TotalCost = 20.000 × distance × vehiclesNeeded,
    VehiclesNeeded = Ceiling(groupSize / 7),
    Pros = "Good balance of cost and comfort",
    Cons = "May need multiple vehicles for large groups"
});

// 16-seat van
options.Add(new TransportOption {
    Method = "16-seat van",
    TotalCost = 35.000 × distance × vehiclesNeeded,
    VehiclesNeeded = Ceiling(groupSize / 16),
    Pros = "Best for large groups, everyone travels together",
    Cons = "Higher total cost, slower speed"
});
```

### 2. Inter-City Transport với Airport/Station Names

**Lấy thông tin từ data_province:**

```csharp
// Lấy tên sân bay thực tế từ cache
var fromAirports = GetAirportsForDestination(fromDest);
var toAirports = GetAirportsForDestination(toDest);

string fromAirportName = fromAirports.FirstOrDefault()?.Name ?? "City Airport";
// Ví dụ: "Sân bay Tân Sơn Nhất"

options.Add(new TransportOption {
    Method = "Airplane",
    Description = $"Flight from {fromAirportName} ({fromDest}) to {toAirportName} ({toDest})",
    DepartureHub = fromAirportName,  // ✅ Hiển thị trong UI
    ArrivalHub = toAirportName,      // ✅ Hiển thị trong UI
    TotalCost = flightCostPerPerson × groupSize,
    TravelTimeMinutes = 60 + 90 + 90,  // Check-in + Flight + Check-out
    Pros = $"Fastest option, departs from {fromAirportName}",
    Cons = "Most expensive, airport transfers needed"
});
```

---

## 🎫 Cơ Chế Tính Extra Spending Cost

### 1. Phân Biệt Ticket Cost vs Extra Spending

```
┌────────────────────────────────────────────────────────────┐
│  Total Location Cost = Ticket Cost + Extra Spending Cost  │
└────────────────────────────────────────────────────────────┘

Ticket Cost: Vé tham quan (cố định, biết trước)
Extra Spending: Ăn uống, mua sắm, tip (biến động)
```

### 2. Tính Extra Spending Theo Segment

```csharp
(double min, double max) = tripSegment switch
{
    "budget"   => (50.000, 150.000),    // 50k-150k/người
    "midrange" => (150.000, 400.000),   // 150k-400k/người
    "luxury"   => (400.000, 1.000.000)  // 400k-1M/người
};

// Nhân hệ số theo loại địa điểm
double multiplier = location.Tags.Any(t => 
    new[] { "Shopping", "Food", "Market", "Restaurant" }.Contains(t)
) ? 1.2 : 1.0;  // +20% cho địa điểm mua sắm/ăn uống

extraSpendingCost = (min + random × (max - min)) × multiplier × groupSize;
```

---

## 🔄 Cơ Chế Anti-Duplicate Locations

### 1. Theo Dõi Visit Count

```csharp
// Dictionary xuyên suốt toàn bộ hành trình
var visitedCountMap = new Dictionary<int, int>();

// Khi chọn địa điểm
nearby = candidates
    .Where(c => !visitedCountMap.ContainsKey(c.Location.Id))  // ✅ Chưa thăm
    .ToList();

// Sau khi thăm
visitedCountMap.TryGetValue(location.Id, out int count);
visitedCountMap[location.Id] = count + 1;  // Đánh dấu đã thăm
```

### 2. Không Gợi Ý Lại

```csharp
// Ngày tiếp theo vẫn dùng cùng visitedCountMap
// → Địa điểm đã thăm ngày 1 sẽ không xuất hiện ngày 2, 3, ...
```

---

## 📊 Summary: Các Công Thức Quan Trọng

| Yếu Tố | Công Thức | Ghi Chú |
|--------|-----------|---------|
| **Contingency %** | `5-20%` tùy budget | Budget cao → % thấp |
| **Usable Budget** | `totalBudget - contingencyFund` | Budget thực tế để chi |
| **Activity Budget** | `usableBudget - transport - hotel` | Còn lại cho hoạt động |
| **Location Score** | `40% Quality + 35% Time + 25% Cost` | Composite score |
| **Time Efficiency** | `100 - (stayDuration - 30) / 3` | Ưu tiên địa điểm ngắn |
| **Cost Efficiency** | `100 - averageBudget / 5000` | Ưu tiên địa điểm rẻ |
| **Hotel Score** | `25% Dist + 35% Budget + 25% Group + 15% Amenities` | |
| **Daily Weight** | `1.3 (day 1), 1.1 (day 2), 1.2 (last day)` | |
| **Limit/Floor** | `weightedBudget × 1.3 / × 0.7` | Giới hạn chi tiêu |
| **Rollover** | `Min(limit - spent, nextDayLimit × 0.5)` | Cuộn sang ngày sau |

---

## 🎯 Kết Luận

Thuật toán sử dụng **đa yếu tố** để tối ưu hóa:
1. **Ngân sách:** Chia thông minh với quỹ dự phòng linh hoạt
2. **Địa điểm:** Chấm điểm composite để cân bằng chất lượng, thời gian, chi phí
3. **Thời gian:** Sắp xếp hợp lý với buffer cho di chuyển và nghỉ ngơi
4. **Lưu trú:** Gợi ý khách sạn với room types chi tiết
5. **Di chuyển:** Hiển thị airport/station names thực tế
6. **Anti-duplicate:** Theo dõi xuyên suốt để không thăm lại

Hệ thống được thiết kế để **tối đa hóa trải nghiệm** trong khi **tối thiểu hóa chi phí** và **đảm bảo tính khả thi** của lịch trình.
