# Test Case Documentation - Reasonable Tests

## ✅ Test Case 1: Nearby Destinations (Recommended)

**Input:**
```json
{
  "Destinations": ["Hanoi", "Ninh Binh", "Ha Long"],
  "GroupSize": 5,
  "TotalBudget": 15000000,
  "StartDate": "2026-03-14",
  "EndDate": "2026-03-19",
  "UserFavoriteTags": ["Food", "Culture", "Sightseeing", "Nature"],
  "StartLatitude": 21.0285,
  "StartLongitude": 105.8522
}
```

**Expected Results:**
- ✅ 3 destinations visited
- ✅ Inter-city transport: Hanoi ↔ Ninh Binh ↔ Ha Long
- ✅ Total cost: 12-16M (within or slightly over budget)
- ✅ 4-5/6 days within budget
- ✅ 6 lunches, 5 nights accommodation

**Actual Results:**
- Total Cost: 12.64M VND ✅
- Contingency: 1.5M VND ✅
- Destinations: Ha Long, Hanoi ⚠️ (Ninh Binh missing - no data)
- Rest: 6 lunches, 5 nights ✅
- Budget: 3/6 days OK ⚠️

---

## ✅ Test Case 2: Single Destination - Budget Friendly

**Input:**
```json
{
  "Destinations": ["Hanoi"],
  "GroupSize": 4,
  "TotalBudget": 5000000,
  "StartDate": "2026-03-14",
  "EndDate": "2026-03-18",
  "UserFavoriteTags": ["Food", "Shopping"],
  "StartLatitude": 21.0285,
  "StartLongitude": 105.8522
}
```

**Expected:**
- Total cost: 5-6M
- 3-4/5 days within budget
- Good for testing budget constraints

---

## ✅ Test Case 3: Two Destinations - Comfortable

**Input:**
```json
{
  "Destinations": ["Hanoi", "Da Nang"],
  "GroupSize": 4,
  "TotalBudget": 12000000,
  "StartDate": "2026-03-14",
  "EndDate": "2026-03-18",
  "UserFavoriteTags": ["Food", "Culture", "Relax"],
  "StartLatitude": 21.0285,
  "StartLongitude": 105.8522
}
```

**Expected:**
- Hanoi (3 days) + Da Nang (2 days)
- Inter-city transport: ~3-5M (train/bus)
- Total cost: 12-15M
- 3-4/5 days within budget

---

## 🔧 How to Run Tests

### 1. Start Server
```bash
cd "E:\FU Learning\2026\AlgorithmTripPlan\AlgorithmPlan"
dotnet run --urls="http://localhost:5002"
```

### 2. Run Test Script
```powershell
powershell -ExecutionPolicy Bypass -File "test-simple.ps1"
```

### 3. Or Test Manually with PowerShell
```powershell
$body = @{
    Destinations = @("Hanoi", "Da Nang")
    GroupSize = 4
    TotalBudget = 12000000
    StartDate = "2026-03-14"
    EndDate = "2026-03-18"
    UserFavoriteTags = @("Food", "Culture", "Relax")
    StartLatitude = 21.0285
    StartLongitude = 105.8522
} | ConvertTo-Json

Invoke-RestMethod -Uri "http://localhost:5002/api/test/generate-smart" -Method Post -Body $body -ContentType "application/json" | ConvertTo-Json -Depth 10
```

---

## 📊 Success Criteria

| Criteria | Pass | Warning | Fail |
|----------|------|---------|------|
| **Budget** | ≤100% | 100-120% | >120% |
| **Days OK** | ≥70% | 50-70% | <50% |
| **Rest Items** | 1 lunch/day, 1 night/day | Missing some | Missing most |
| **Multi-Dest** | All visited | Some missing | Single dest only |

---

## ⚠️ Known Limitations

1. **Data Coverage**: Ninh Binh has limited locations in data.json
2. **Transport Costs**: Inter-city transport can be 30-50% of budget
3. **Hotel Prices**: Budget hotels booked early may limit options

---

## 📝 Recommendations

For best results:
- **Budget**: 2-3M VND/person/day for multi-destination
- **Days**: Minimum 3 days per destination
- **Group Size**: 4-6 people (optimal for vehicle sharing)
- **Destinations**: 2-3 nearby cities (avoid long-distance travel)
