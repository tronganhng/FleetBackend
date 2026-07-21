namespace FleetBackend.Models
{
    public class MapDto
    {
        public List<MapPointDto> Points { get; set; } = new();
        public List<MapLaneDto> Lanes { get; set; } = new();
    }

    public class MapPointDto
    {
        public string? PointName { get; }
        public float[]? Position { get; }
    }

    public class MapLaneDto
    {
        public string? StartPoint { get; }
        public string? EndPoint { get; }
        public float Distance { get; }
    }
}