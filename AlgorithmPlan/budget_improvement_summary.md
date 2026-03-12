## ✅ Cập nhật: Smart Budget Allocation cho Accommodation

### Vấn đề cũ:
- Daily budget allocation không tách riêng accommodation
- Hotel cost ~1,750,000 - 8,400,000 VND/đêm nhưng daily budget chỉ ~700,000 - 1,100,000 VND
- → Ngày có hotel bị vượt budget (spent > limit)

### Giải pháp đã thực hiện:

#### 1. **Tách budget thành 2 phần** (ItineraryService.cs - GenerateSmartItinerary):
```csharp
// Step 1: Estimate accommodation cost for all nights
double estimatedHotelCostPerNight = EstimateAccommodationCost(candidateLocations, request.GroupSize);
int hotelNights = totalDays - 1;
double totalAccommodationBudget = estimatedHotelCostPerNight * hotelNights;

// Step 2: Remaining budget for daily activities (food, transport, tickets)
double activityBudget = usableBudget - totalAccommodationBudget;

// Step 3: Allocate daily activity budgets
var dailyActivityBudgets = AllocateDailyBudgets(request.StartDate, request.EndDate, activityBudget);
```

#### 2. **Daily limit = Activity Budget + Accommodation Budget (nếu cần)**:
```csharp
double dailyLimit = dailyActivityBudgets[dayCounter] + rolloverBudget;
bool needHotelTonight = ...;
double accommodationBudgetTonight = needHotelTonight ? estimatedHotelCostPerNight : 0;
double totalDailyLimit = dailyLimit + accommodationBudgetTonight;
```

#### 3. **Rollover chỉ tính activity budget**:
```csharp
// Rollover only the activity budget portion (not accommodation)
rolloverBudget = dailyLimit - (dailyPlan.DailyBudgetStatus.Spent - accommodationBudgetTonight);

// Cap rollover to prevent excessive accumulation (max 50% of next day's activity budget)
double maxRollover = dailyActivityBudgets[...] * 0.5;
if (rolloverBudget > maxRollover) rolloverBudget = maxRollover;
if (rolloverBudget < -maxRollover) rolloverBudget = -maxRollover; // Allow some deficit
```

#### 4. **EstimateAccommodationCost - Chọn budget-friendly options**:
```csharp
// For large groups, prioritize budget-friendly options
// Choose from the cheaper half to ensure affordability
int budgetOptionCount = Math.Max(1, accommodations.Count / 2);
var budgetOptions = accommodations.Take(budgetOptionCount);

// Return average of budget-friendly options
return budgetOptions.Average(x => x.CostPerNight);
```

### Kết quả mong đợi:
- **Ngày thường**: Limit = Activity Budget (~400,000 - 600,000 VND/người)
- **Ngày có hotel**: Limit = Activity Budget + Hotel Budget (~400,000 - 600,000 + 250,000 - 500,000 VND/người)
- **Không còn tình trạng spent > limit** do hotel cost đã được tính trước
- **Rollover được giới hạn** để tránh tích lũy quá nhiều

### Lưu ý:
- Với budget 8,000,000 VND cho 7 người trong 8 ngày:
  - Total budget/người = 1,142,857 VND
  - Budget/ngày/người = ~143,000 VND (rất thấp)
  - → Cần tăng budget hoặc giảm số ngày/người để có trải nghiệm tốt hơn
