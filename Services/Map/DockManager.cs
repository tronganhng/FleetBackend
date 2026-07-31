using System.Collections.Concurrent;
using FleetBackend.Models;

namespace FleetBackend.Services.Map
{
    public interface IDockManager
    {
        MapPointDto? AcquireDock(string nodeName);
        void ReleaseDock(string dockName);
        MapPointDto? GetDock(string dockName);
        IEnumerable<MapPointDto> GetDocks(string nodeName);
    }

    public class DockManager : IDockManager
    {
        private readonly Dictionary<string, List<MapPointDto>> _nodeDocks = new();
        private readonly Dictionary<string, MapPointDto> _dockLookup = new();
        private readonly ConcurrentDictionary<string, bool> _occupied = new();

        public DockManager()
        {
            LoadMap();
        }

        private void LoadMap()
        {
            var map = FileLoader.Load<MapDto>("Map");

            _nodeDocks.Clear();
            _dockLookup.Clear();
            _occupied.Clear();

            foreach (var node in map.Nodes)
            {
                var docks = node.MapPoints;

                _nodeDocks[node.NodeName] = docks;

                foreach (var dock in docks)
                {
                    _dockLookup[dock.PointName] = dock;
                    _occupied[dock.PointName] = false;
                }
            }
        }

        public MapPointDto? AcquireDock(string nodeName)
        {
            if (!_nodeDocks.TryGetValue(nodeName, out var docks))
                return null;

            foreach (var dock in docks)
            {
                if (_occupied.TryUpdate(dock.PointName, true, false))
                {
                    return dock;
                }
            }

            return null;
        }

        public void ReleaseDock(string dockName)
        {
            if (_occupied.ContainsKey(dockName))
            {
                _occupied[dockName] = false;
            }
        }

        public MapPointDto? GetDock(string dockName)
        {
            _dockLookup.TryGetValue(dockName, out var dock);
            return dock;
        }

        public IEnumerable<MapPointDto> GetDocks(string nodeName)
        {
            if (_nodeDocks.TryGetValue(nodeName, out var docks))
                return docks;

            return Enumerable.Empty<MapPointDto>();
        }
    }
}