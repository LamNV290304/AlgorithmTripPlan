# Multi-Destination Test Script
# Usage: powershell -ExecutionPolicy Bypass -File "test-multi-destination.ps1" -TestCase 1

param(
    [string]$TestCase = "1"  # 1, 2, 3, or 4
)

$testCases = @{
    "1" = @{
        Destinations = @("Hanoi", "Da Nang", "Ho Chi Minh City")
        GroupSize = 4
        TotalBudget = 35000000
        StartDate = "2026-03-14"
        EndDate = "2026-03-23"
        UserFavoriteTags = @("Food", "Culture", "Sightseeing", "Beach")
        StartLatitude = 21.0285
        StartLongitude = 105.8522
    }
    "2" = @{
        Destinations = @("Hanoi", "Ninh Binh")
        GroupSize = 5
        TotalBudget = 18000000
        StartDate = "2026-03-14"
        EndDate = "2026-03-20"
        UserFavoriteTags = @("Culture", "History", "Nature", "Photography")
        StartLatitude = 21.0285
        StartLongitude = 105.8522
    }
    "3" = @{
        Destinations = @("Da Nang", "Hoi An")
        GroupSize = 2
        TotalBudget = 12000000
        StartDate = "2026-03-14"
        EndDate = "2026-03-19"
        UserFavoriteTags = @("Beach", "Food", "Culture", "Relaxation")
        StartLatitude = 16.0544
        StartLongitude = 108.2022
    }
    "4" = @{
        Destinations = @("Ho Chi Minh City", "Can Tho", "Chau Doc")
        GroupSize = 6
        TotalBudget = 25000000
        StartDate = "2026-03-14"
        EndDate = "2026-03-21"
        UserFavoriteTags = @("Nature", "Culture", "Food", "Adventure")
        StartLatitude = 10.8231
        StartLongitude = 106.6297
    }
}

$selectedCase = $testCases[$TestCase]

if (-not $selectedCase) {
    Write-Host "Invalid test case. Choose 1, 2, 3, or 4" -ForegroundColor Red
    exit 1
}

$body = $selectedCase | ConvertTo-Json -Depth 10

Write-Host "=== Running Multi-Destination Test Case $TestCase ===" -ForegroundColor Cyan
Write-Host "Destinations: $($selectedCase.Destinations -join ' → ')" -ForegroundColor Green
Write-Host "Group Size: $($selectedCase.GroupSize) people" -ForegroundColor Green
Write-Host "Budget: $($selectedCase.TotalBudget.ToString('N0')) VND" -ForegroundColor Green
$duration = (New-TimeSpan -Start $selectedCase.StartDate -End $selectedCase.EndDate).Days + 1
Write-Host "Duration: $duration days" -ForegroundColor Green
Write-Host ""

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5002/api/Test/generate-smart" `
        -Method POST `
        -Body $body `
        -ContentType "application/json"

    # Display summary
    Write-Host "=== Trip Summary ===" -ForegroundColor Cyan
    Write-Host "Total Estimated Cost: $($response.tripSummary.totalEstimatedCost.ToString('N0')) VND"
    Write-Host "Contingency Fund: $($response.tripSummary.remainingContingencyFund.ToString('N0')) VND ($($response.tripSummary.contingencyFundPercentage)%)"
    Write-Host "Budget Insufficient: $($response.tripSummary.isBudgetInsufficient)"
    if ($response.tripSummary.budgetWarning) {
        Write-Host "Warning: $($response.tripSummary.budgetWarning)" -ForegroundColor Yellow
    }

    # Display destination breakdown
    if ($response.destinationBreakdown) {
        Write-Host "`n=== Destination Breakdown ===" -ForegroundColor Cyan
        foreach ($dest in $response.destinationBreakdown) {
            Write-Host "`n$($dest.destination):" -ForegroundColor Yellow
            Write-Host "  Days: $($dest.days) | Nights: $($dest.nights)"
            Write-Host "  Allocated Budget: $($dest.allocatedBudget.ToString('N0')) VND"
            Write-Host "  Estimated Cost: $($dest.estimatedCost.ToString('N0')) VND"
            Write-Host "  Hotel Cost/Night: $($dest.hotelCostPerNight.ToString('N0')) VND"
            Write-Host "  Activity Budget: $($dest.activityBudget.ToString('N0')) VND"
            Write-Host "  Weight: $($dest.weight)"
        }
    }

    # Display inter-city transport
    if ($response.interCityTransport) {
        Write-Host "`n=== Inter-City Transport ===" -ForegroundColor Cyan
        foreach ($transport in $response.interCityTransport) {
            Write-Host "$($transport.from) → $($transport.to)" -ForegroundColor Green
            Write-Host "  Mode: $($transport.mode) | Distance: $($transport.distance) km"
            Write-Host "  Cost: $($transport.totalCost.ToString('N0')) VND ($($transport.costPerPerson.ToString('N0')) VND/person)"
            Write-Host "  Duration: $($transport.duration) | Day: $($transport.scheduledDay)"
        }
        Write-Host "`nTotal Transport Cost: $($response.interCityTransport.totalInterCityTransportCost.ToString('N0')) VND" -ForegroundColor Cyan
    }

    # Display daily breakdown
    Write-Host "`n=== Daily Breakdown ===" -ForegroundColor Cyan
    for ($i = 0; $i -lt $response.days.Count; $i++) {
        $day = $response.days[$i]
        Write-Host "`nDay $($i + 1): $($day.day)" -ForegroundColor Green
        Write-Host "  Spent: $($day.dailyBudgetStatus.spent.ToString('N0')) / Limit: $($day.dailyBudgetStatus.limit.ToString('N0'))"
        Write-Host "  Ceiling: $($day.dailyBudgetStatus.ceiling.ToString('N0')) | Floor: $($day.dailyBudgetStatus.floor.ToString('N0'))"
        Write-Host "  Weight: $($day.dailyBudgetStatus.weight)"
        Write-Host "  Timeline Items: $($day.timeline.Count)"
        
        # Check budget status
        $spent = [double]$day.dailyBudgetStatus.spent
        $limit = [double]$day.dailyBudgetStatus.limit
        $ceiling = [double]$day.dailyBudgetStatus.ceiling
        
        if ($spent -gt $ceiling) {
            Write-Host "  ⚠️  WARNING: Over ceiling!" -ForegroundColor Red
        } elseif ($spent -gt $limit) {
            Write-Host "  ⚠️  WARNING: Over limit!" -ForegroundColor Yellow
        } else {
            Write-Host "  ✅ Budget OK" -ForegroundColor Green
        }
    }

    # Export to file
    $outputFile = "test-multi-destination-case$TestCase-output.json"
    $response | ConvertTo-Json -Depth 10 | Out-File $outputFile -Encoding UTF8
    Write-Host "`n✅ Full response saved to $outputFile" -ForegroundColor Green

    # Validation
    Write-Host "`n=== Validation ===" -ForegroundColor Cyan
    $totalCost = [double]$response.tripSummary.totalEstimatedCost
    $contingency = [double]$response.tripSummary.remainingContingencyFund
    $totalBudget = $selectedCase.TotalBudget
    $isBudgetOK = ($totalCost + $contingency) -le $totalBudget
    $daysCovered = $response.days.Count -eq $duration
    
    Write-Host "Budget Status: $(if($isBudgetOK) {'✅ PASS'} else {'❌ FAIL'})"
    Write-Host "  Total Cost: $([math]::Round($totalCost / 1000000, 2))M / Budget: $([math]::Round($totalBudget / 1000000, 2))M"
    Write-Host "All Days Covered: $(if($daysCovered) {'✅ PASS'} else {'❌ FAIL'}) ($($response.days.Count)/$duration days)"
    
    # Check for budget overruns
    $overBudgetDays = 0
    foreach ($day in $response.days) {
        $spent = [double]$day.dailyBudgetStatus.spent
        $ceiling = [double]$day.dailyBudgetStatus.ceiling
        if ($spent -gt $ceiling) {
            $overBudgetDays++
        }
    }
    
    if ($overBudgetDays -gt 0) {
        Write-Host "Days Over Ceiling: ❌ $overBudgetDays day(s)" -ForegroundColor Yellow
    } else {
        Write-Host "Days Over Ceiling: ✅ None" -ForegroundColor Green
    }

} catch {
    Write-Host "❌ Error calling API: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "Make sure the server is running at http://localhost:5002" -ForegroundColor Yellow
}
