using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface IMapManager
    {
        bool IsLocationValid(string locationId);
    }

    public class MapManager : IMapManager
    {
        private List<MapPointDto> _points = new();
        private List<MapLaneDto> _lanes = new();

        public MapManager()
        {
            LoadMap();
        }

        public bool IsLocationValid(string locationId) { throw new System.NotImplementedException(); }

        private void LoadMap()
        {
            var mapDto = FileLoader.Load<MapDto>("Map");
            _points = mapDto.Points;
            _lanes = mapDto.Lanes;
        }
    }
}
