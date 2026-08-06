namespace FleetBackend.Models
{
    public class MapDto
    {
        public List<MapNodeDto> Nodes { get; set; } = new();
        public List<MapLaneDto> Lanes { get; set; } = new();
    }

    public class MapNodeDto
    {
        public string NodeName { get; set; } = string.Empty;
        public NodeType NodeType { get; set; } = NodeType.Room;
        public float[] Position { get; set; } = Array.Empty<float>();
        public List<MapPointDto> MapPoints { get; set; } = new();
    }

    public class MapPointDto
    {
        public string PointName { get; set; } = string.Empty;
        public float[] Position { get; set; } = Array.Empty<float>();
    }

    public class MapLaneDto
    {
        public string StartNode { get; set; } = string.Empty;
        public string EndNode { get; set; } = string.Empty;
        public float Distance { get; set; }
    }
}