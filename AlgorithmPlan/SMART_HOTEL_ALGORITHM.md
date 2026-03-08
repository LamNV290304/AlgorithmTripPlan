# 🏨 Smart Hotel Search Algorithm - Cải tiến tìm kiếm khách sạn

## 📋 Tổng quan

Thuật toán tìm kiếm khách sạn mới sử dụng **Multi-Criteria Scoring System** để cân bằng 3 yếu tố:
- **Khoảng cách** (40% trọng số)
- **Chi phí** (35% trọng số)  
- **Chất lượng** (25% trọng số)

---

## 🎯 Các cải tiến chính

### 1. **Hàm `FindNextBestAccommodation()` mới**

Thay vì chỉ chọn khách sạn rẻ nhất hoặc gần nhất, thuật toán mới tính **composite score**:

```csharp
// Distance Score (40%): Prefer closer hotels
double distanceScore = 100 * (1 - (distance - minDistance) / (maxDistance - minDistance));

// Price Score (35%): Optimal at 60% of price range (good value, not cheapest)
double priceRatio = (cost - minCost) / (maxCost - minCost);
double priceScore = 100 * (1 - Math.Abs(priceRatio - 0.6));

// Quality Score (25%): Based on tag matching score
double qualityScore = 100 * originalScore / maxScore;

// Total weighted score
double totalScore = distanceScore * 0.40 + priceScore * 0.35 + qualityScore * 0.25;
```

### 2. **Thông minh chọn lọc**

- **Lấy top 3** khách sạn có score cao nhất
- **Chọn khách sạn gần nhất** trong top 3 (ưu tiên khoảng cách)
- **Tránh di chuyển không cần thiết**: Giữ khách sạn cũ nếu:
  - Trong vòng 3km
  - Chênh lệch giá ≤ 30%

### 3. **Budget-aware Selection**

- Filter accommodations trong **120% budget** (linh hoạt 20%)
- Price score optimal ở **60% price range** → Không chọn rẻ nhất cũng không đắt nhất
- Cân nhắc **value for money** thay vì chỉ price

### 4. **Smart Restaurant/Cafe Search**

```csharp
// Balance between distance (60%) and price (40%)
OrderBy(x => x.Distance * 0.6 + (x.Cost / budget) * 0.4)
```

---

## 📊 So sánh với thuật toán cũ

| Tiêu chí | Algorithm Cũ | Algorithm Mới |
|----------|--------------|---------------|
| **Selection Criteria** | Price only (cheapest first) | Multi-criteria (Distance 40% + Price 35% + Quality 25%) |
| **Hotel Stability** | Change every night if far | Keep current hotel if within 3km & price similar |
| **Price Range** | Cheapest available | Optimal at 60% of range (mid-range value) |
| **Restaurant Search** | Nearest within budget | Weighted score: 60% distance + 40% price |

---

## 🔧 Cách sử dụng

### Test Case 1: Budget thấp (8M, 7 người, 8 ngày)
```json
{
  "Destinations": ["Hanoi"],
  "GroupSize": 7,
  "TotalBudget": 8000000,
  "StartDate": "2026-03-14",
  "EndDate": "2026-03-21",
  "UserFavoriteTags": ["Food", "Culture", "Sightseeing", "Shopping"],
  "StartLatitude": 21.0285,
  "StartLongitude": 105.8522
}
```

**Kết quả mong đợi:**
- Hotel: ~250,000 - 350,000 VND/người/đêm (Hostel/Guesthouse)
- Tổng chi phí: ~17-18M (vượt budget do budget quá thấp)
- 4-5/8 ngày trong budget

### Test Case 2: Budget khá (15M, 7 người, 8 ngày)
```json
{
  "Destinations": ["Hanoi"],
  "GroupSize": 7,
  "TotalBudget": 15000000,
  ...
}
```

**Kết quả mong đợi:**
- Hotel: ~300,000 - 450,000 VND/người/đêm (3-star hotel)
- Tổng chi phí: ~15-16M (trong budget)
- 5-6/8 ngày trong budget

### Test Case 3: Nhóm nhỏ (5M, 4 người, 5 ngày)
```json
{
  "Destinations": ["Hanoi"],
  "GroupSize": 4,
  "TotalBudget": 5000000,
  "StartDate": "2026-03-14",
  "EndDate": "2026-03-18",
  ...
}
```

**Kết quả mong đợi:**
- Hotel: ~200,000 - 300,000 VND/người/đêm
- Tổng chi phí: ~5-6M (gần sát budget)
- 3-4/5 ngày trong budget

---

## 🚀 Chạy test

```powershell
# Start server
cd "E:\FU Learning\2026\AlgorithmTripPlan\AlgorithmPlan"
dotnet run --urls="http://localhost:5002"

# Run test script (PowerShell)
powershell -ExecutionPolicy Bypass -File "test-api.ps1"
```

---

## 📈 Hướng phát triển tiếp theo

1. **Thêm data khách sạn đa dạng hơn** (nhiều price tier)
2. **User ratings/reviews integration** (nếu có)
3. **Dynamic weight adjustment** dựa trên user preference
4. **Machine Learning** để optimize weights dựa trên feedback
5. **Real-time availability checking** (API integration)

---

## ⚠️ Lưu ý

- **Budget quá thấp** (< 200k/người/ngày) sẽ hạn chế lựa chọn
- **Số lượng hotel trong data** cần đủ đa dạng để algorithm hoạt động hiệu quả
- **Quality score** hiện dựa trên tag matching - có thể cải thiện bằng ratings
