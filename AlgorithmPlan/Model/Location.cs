namespace AlgorithmPlan.Model
{
    public class Location
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int LocationTypeId { get; set; }
        public LocationType LocationType { get; set; }
    }
}
