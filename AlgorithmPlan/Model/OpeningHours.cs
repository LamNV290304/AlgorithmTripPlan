using System;
using System.Collections.Generic;

namespace AlgorithmPlan.Model
{
    public class OpeningHours
    {
        public DayOfWeek DayOfWeek { get; set; }
        public TimeSpan OpenTime { get; set; }
        public TimeSpan CloseTime { get; set; }
    }
}
