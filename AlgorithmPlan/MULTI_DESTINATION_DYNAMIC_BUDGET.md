# 🗺️ Multi-Destination Support & Dynamic Budget Allocation

## 📋 Tổng quan

Cải tiến thuật toán để hỗ trợ:
1. **Multi-Destination**: Lập lịch cho nhiều thành phố trong 1 chuyến đi
2. **Dynamic Budget Allocation**: Phân bổ ngân sách thông minh theo từng destination
3. **Inter-City Transport**: Tự động tính toán chi phí di chuyển liên thành phố

---

## 🎯 Các cải tiến chính

### 1. **Multi-Destination Routing**

```csharp
// Xác định thứ tự thăm quan tối ưu (Nearest Neighbor algorithm)
var orderedDestinations = DetermineBestVisitingOrder(
    request.Destinations, 
    candidateLocations, 
    startLat, 
    startLon
);
```

**Kết quả**: 
- Input: `["Hanoi", "HCMC", "Da Nang"]`
- Output: `["Hanoi", "Da Nang", "HCMC"]` (optimize khoảng cách di chuyển)

### 2. **Dynamic Budget Allocation per Destination**

```csharp
// Weight = days × sqrt(attractionCount)
// → Cân bằng giữa thời gian và số lượng địa điểm
double weight = days * Math.Max(1, Math.Sqrt(attractionCount));

// Allocate budget proportionally
double destBudget = (weight / totalWeight) × totalActivityBudget;
```

**Ví dụ**:
- Hanoi: 5 days, 20 attractions → Weight = 5 × √20 = 22.4
- Da Nang: 2 days, 8 attractions → Weight = 2 × √8 = 5.7
- HCMC: 3 days, 15 attractions → Weight = 3 × √15 = 11.6

→ Hanoi nhận ~57% budget, Da Nang ~15%, HCMC ~28%

### 3. **Inter-City Transport Budget Calculation**

```csharp
// Tính toán tổng chi phí di chuyển liên thành trước
double totalTransportBudget = CalculateInterCityTransportBudget(
    orderedDestinations, 
    candidates, 
    groupSize, 
    startLat, 
    startLon
);

// Trừ khỏi budget tổng trước khi phân bổ
activityBudget = usableBudget - totalTransportBudget - totalAccommodationBudget;
```

**Transport modes** (based on distance):
- **< 200km**: Bus/Coach (~200k VND/person)
- **200-600km**: Train (~500k VND/person)
- **> 600km**: Airplane (~2M VND/person)

### 4. **Per-Destination Hotel Pricing**

```csharp
// Mỗi thành phố có giá khách sạn khác nhau
foreach (var dest in destinationDayAllocation.Keys)
{
    double avgHotelCostPerNight = EstimateAccommodationCost(destCandidates, groupSize);
    destinationHotelCosts[dest] = avgHotelCostPerNight;
}
```

---

## 📊 Test Results

### Test Case 1: Multi-Destination (Hanoi → Da Nang → HCMC)
```json
{
  "Destinations": ["Hanoi", "HCMC", "Da Nang"],
  "GroupSize": 4,
  "TotalBudget": 20000000,
  "Days": 10
}
```

**Kết quả**:
- **Lộ trình**: Hanoi (5 days) → Da Nang (1 day) → HCMC (4 days)
- **Total Cost**: 27,476,813 VND
- **Breakdown**:
  - Inter-city transport: ~14M (Hanoi→Da Nang: train, Da Nang→HCMC: flight)
  - Hotels: ~8M (7 nights)
  - Activities: ~5.5M
- **Budget Status**: 7/10 days OK

### Test Case 2: Single Destination (Hanoi only)
```json
{
  "Destinations": ["Hanoi"],
  "GroupSize": 4,
  "TotalBudget": 5000000,
  "Days": 5
}
```

**Kết quả**:
- **Total Cost**: 5,557,651 VND (gần sát budget)
- **Budget Status**: 2-3/5 days OK

### Test Case 3: Two Destinations (Hanoi + Da Nang)
```json
{
  "Destinations": ["Hanoi", "Da Nang"],
  "GroupSize": 5,
  "TotalBudget": 12000000,
  "Days": 7
}
```

**Kết quả**:
- **Lộ trình**: Hanoi (5 days) → Da Nang (2 days)
- **Total Cost**: 24,474,805 VND (vượt budget do inter-city transport)
- **Budget Status**: 5/7 days OK (Hanoi days OK, travel days over)

---

## ⚠️ Hạn chế & Khuyến nghị

### Vấn đề hiện tại:
1. **Inter-city transport quá đắt** với budget thấp
   - Hanoi → Da Nang → HCMC: ~14M cho 4 người
   - Chiếm 70% budget 20M

2. **Ngày di chuyển bị OVER BUDGET**
   - Transport cost được add vào timeline nhưng không được tính vào daily limit properly

3. **Số ngày ở mỗi destination** có thể không tối ưu
   - Da Nang chỉ có 1-2 ngày → quá ít để tham quan

### Khuyến nghị:
1. **Budget đề xuất cho multi-destination**:
   - 2 destinations (7 days): **15-20M** cho 4-5 người
   - 3 destinations (10 days): **30-40M** cho 4-5 người

2. **Optimize inter-city transport**:
   - Chọn transport mode phù hợp hơn (bus thay vì train/flight nếu budget thấp)
   - Gom các destination gần nhau (Hanoi + Ninh Binh + Ha Long)

3. **Cải thiện algorithm**:
   - Tự động đề xuất số ngày tối ưu per destination
   - Cảnh báo nếu budget không đủ cho inter-city transport
   - Alternative routing suggestions

---

## 🚀 Cách sử dụng

### Test multi-destination:
```powershell
# Start server
dotnet run --urls="http://localhost:5002"

# Run test script
powershell -ExecutionPolicy Bypass -File "test-api.ps1"
```

### Input format:
```json
{
  "Destinations": ["Hanoi", "Da Nang", "HCMC"],
  "GroupSize": 4,
  "TotalBudget": 30000000,
  "StartDate": "2026-03-14",
  "EndDate": "2026-03-23",
  "UserFavoriteTags": ["Food", "Culture", "Sightseeing"],
  "StartLatitude": 21.0285,
  "StartLongitude": 105.8522
}
```

---

## 📈 Hướng phát triển tiếp theo

1. **Transport mode selection UI**: Cho phép user chọn preference (fastest/cheapest)
2. **Dynamic day allocation**: Tự động điều chỉnh số ngày dựa trên budget
3. **Budget warning system**: Cảnh báo sớm nếu budget không đủ
4. **Alternative suggestions**: Đề xuất destination thay thế nếu budget quá thấp
5. **Real-time transport pricing**: API integration với booking platforms
