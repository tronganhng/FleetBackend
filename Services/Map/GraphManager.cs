using FleetBackend.Models;

namespace FleetBackend.Services.Map
{
    public interface IGraphManager
    {
        bool HasNode(string nodeName);
        MapNodeDto GetNode(string nodeName);
        IEnumerable<MapLaneDto> GetConnectedLanes(string nodeName);
        MapNodeDto? GetRobotNode(RobotStateDto robot);
    }

    public class GraphManager : IGraphManager
    {
        private List<MapNodeDto> _nodes = new();
        private List<MapLaneDto> _lanes = new();

        public GraphManager()
        {
            LoadMap();
        }

        private void LoadMap()
        {
            var mapDto = FileLoader.Load<MapDto>("Map");
            _nodes = mapDto.Nodes;
            _lanes = mapDto.Lanes;
        }

        public bool HasNode(string nodeName)
        {
            return _nodes.Any(p => p.NodeName == nodeName);
        }

        public MapNodeDto GetNode(string nodeName)
        {
            return _nodes.First(p => p.NodeName == nodeName);
        }

        public IEnumerable<MapLaneDto> GetConnectedLanes(string nodeName)
        {
            return _lanes.Where(l => l.StartNode == nodeName || l.EndNode == nodeName);
        }

        public MapNodeDto? GetRobotNode(RobotStateDto robot)
        {
            MapNodeDto? nearest = null;
            double minDistance = double.MaxValue;

            foreach (var point in _nodes)
            {
                if (point.Position.Length < 2)
                    continue;

                double dx = robot.X - point.Position[0];
                double dy = robot.Y - point.Position[1];

                double distance = dx * dx + dy * dy;

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = point;
                }
            }

            return nearest;
        }
    }
}