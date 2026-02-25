namespace AlgorithmPlan.Model
{
    public class TripItem
    {
        public int Id { get; set; }
        public int TripId { get; set; }
        public int LocationId { get; set; }
        public DateTime VisitDate { get; set; }

        public Trip ParentTrip { get; set; }
        public Location Location { get; set; }
    }
}
