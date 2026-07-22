using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface IMapManager
    {
        bool HasPoint(string pointName);
        MapPointDto GetPoint(string pointName);
        IEnumerable<MapLaneDto> GetConnectedLanes(string pointName);
        MapPointDto? GetRobotPoint(RobotStateDto robot);
    }

    public class MapManager : IMapManager
    {
        private List<MapPointDto> _points = new();
        private List<MapLaneDto> _lanes = new();

        public MapManager()
        {
            LoadMap();
        }

        private void LoadMap()
        {
            var mapDto = FileLoader.Load<MapDto>("Map");
            _points = mapDto.Points;
            _lanes = mapDto.Lanes;
        }

        public bool HasPoint(string pointName)
        {
            return _points.Any(p => p.PointName == pointName);
        }

        public MapPointDto GetPoint(string pointName)
        {
            return _points.First(p => p.PointName == pointName);
        }

        public IEnumerable<MapLaneDto> GetConnectedLanes(string pointName)
        {
            return _lanes.Where(l => l.StartPoint == pointName || l.EndPoint == pointName);
        }

        public MapPointDto? GetRobotPoint(RobotStateDto robot)
        {
            MapPointDto? nearest = null;
            double minDistance = double.MaxValue;

            foreach (var point in _points)
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
