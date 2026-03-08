# 🚚 Multi-Transport Options Feature

## 📋 Overview

Thuật toán bây giờ cung cấp **nhiều lựa chọn phương tiện di chuyển** để user có thể chọn tùy theo nhu cầu (giá cả, thời gian, comfort).

---

## 🎯 Features

### 1. **Local Transport Options** (trong thành phố)

**Khoảng cách < 1km:**
- Walking (miễn phí)

**Khoảng cách 1-15km:**
- Walking (nếu ≤ 2km)
- Taxi 4-seat
- 7-seat vehicle  
- 16-seat van

Mỗi option có:
- **Total Cost**: Tổng chi phí
- **Travel Time**: Thời gian di chuyển
- **Vehicles Needed**: Số xe cần
- **Pros**: Ưu điểm
- **Cons**: Nhược điểm
- **Recommended**: Được đề xuất hay không

### 2. **Inter-City Transport Options** (liên thành phố)

**Bus/Coach** (< 500km):
- Cost: 200,000 VND/person
- Pros: Economical, direct route
- Cons: Slower, less comfortable

**Train** (150-800km):
- Cost: 500,000 VND/person
- Pros: Comfortable, scenic views
- Cons: Fixed schedule, may be delayed

**Airplane** (> 400km):
- Cost: 2,000,000 VND/person
- Pros: Fastest for long distances
- Cons: Most expensive, airport transfers

**Private Van** (groups ≤ 16, < 300km):
- Cost: 35,000 VND/km x 16-seat van
- Pros: Flexible schedule, door-to-door
- Cons: Driver fatigue on long trips

---

## 📊 Example Output

### Local Transport (2km):
```json
{
  "type": "Transport",
  "description": "1 x Taxi 4-seat to Hoan Kiem Lake",
  "cost": 30000,
  "transportOptions": [
    {
      "method": "Walking",
      "description": "Walking",
      "totalCost": 0,
      "travelTimeMinutes": 30,
      "pros": "Free, eco-friendly",
      "cons": "Slow",
      "recommended": false
    },
    {
      "method": "Taxi 4-seat",
      "description": "1 x Taxi 4-seat",
      "totalCost": 30000,
      "travelTimeMinutes": 5,
      "pros": "Fast, comfortable, door-to-door",
      "cons": "More expensive for large groups",
      "recommended": true
    },
    {
      "method": "7-seat vehicle",
      "description": "1 x 7-seat vehicle",
      "totalCost": 40000,
      "travelTimeMinutes": 5,
      "pros": "Good balance of cost and comfort",
      "cons": "May need multiple vehicles for large groups",
      "recommended": false
    }
  ],
  "selectedTransportIndex": 1
}
```

### Inter-City Transport (Hanoi → Da Nang, 600km):
```json
{
  "type": "Transport",
  "description": "Airplane from Hanoi to Da Nang (608.7 km)",
  "cost": 8000000,
  "transportOptions": [
    {
      "method": "Bus/Coach",
      "totalCost": 800000,
      "travelTimeMinutes": 800,
      "pros": "Most economical, direct route",
      "cons": "Slower, less comfortable for long distances",
      "recommended": false
    },
    {
      "method": "Train",
      "totalCost": 2000000,
      "travelTimeMinutes": 600,
      "pros": "Comfortable, scenic views, can move around",
      "cons": "Fixed schedule, may be delayed",
      "recommended": true
    },
    {
      "method": "Airplane",
      "totalCost": 8000000,
      "travelTimeMinutes": 240,
      "pros": "Fastest for long distances, most comfortable",
      "cons": "Most expensive, airport transfers needed",
      "recommended": false
    }
  ],
  "selectedTransportIndex": 1
}
```

---

## 🔧 How to Use

### API Response:
```json
{
  "days": [
    {
      "timeline": [
        {
          "type": "Transport",
          "transportOptions": [...],
          "selectedTransportIndex": 1
        }
      ]
    }
  ]
}
```

### Frontend Integration:
```javascript
// Display transport options
timelineItem.transportOptions.forEach((option, index) => {
  const isRecommended = option.recommended;
  const isSelected = index === timelineItem.selectedTransportIndex;
  
  // Render option card
  console.log(`${option.method}: ${option.totalCost} VND, ${option.travelTimeMinutes} min`);
  console.log(`Pros: ${option.pros}`);
  console.log(`Cons: ${option.cons}`);
});

// User can select different option
function selectTransport(timelineItemIndex, optionIndex) {
  // Update selectedTransportIndex
  // Recalculate total cost
  // Update timeline
}
```

---

## 📈 Benefits

1. **Flexibility**: User có thể chọn theo preference (budget vs time vs comfort)
2. **Transparency**: Thấy rõ pros/cons của từng option
3. **Budget Control**: Có thể chọn option rẻ hơn nếu budget hạn hẹp
4. **Group Optimization**: Tự động đề xuất số xe phù hợp với group size

---

## ⚠️ Limitations

1. **Short distance (< 1km)**: Chỉ có Walking option
2. **Data**: Transport options dựa trên estimated costs, actual costs có thể khác
3. **Availability**: Không check real-time availability (train seats, flight tickets)

---

## 🚀 Future Enhancements

1. **Real-time pricing**: API integration với transport providers
2. **Booking links**: Direct booking từ platform
3. **User preferences**: Remember user's preferred transport mode
4. **Carbon footprint**: Show CO2 emissions per option
5. **Reviews**: User ratings for transport providers
