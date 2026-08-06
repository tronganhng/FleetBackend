using System.Collections.Concurrent;
using FleetBackend.Models;

namespace FleetBackend.Services.Map
{
    public interface IDockManager
    {
        void Clear();
        MapPointDto? AcquireDock(string nodeName);
        bool ReleaseDock(string nodeName, string dockName);
        bool IsNodeFull(string nodeName);
        MapPointDto? GetDock(string nodeName, string dockName);
        IEnumerable<MapPointDto> GetDocks(string nodeName);
        event Action<string>? OnDockReleased;
    }

    public class DockManager : IDockManager
    {
        private readonly Dictionary<string, List<MapPointDto>> _nodeDocks = new();
        private readonly Dictionary<(string Node, string Dock), MapPointDto> _dockLookup = new();
        private readonly ConcurrentDictionary<(string Node, string Dock), bool> _occupied = new();
        public event Action<string>? OnDockReleased;

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
                    var key = (node.NodeName, dock.PointName);

                    _dockLookup[key] = dock;
                    _occupied[key] = false;
                }
            }
        }

        public void Clear()
        {
            foreach (var key in _occupied.Keys)
            {
                _occupied[key] = false;
            }
        }

        public MapPointDto? AcquireDock(string nodeName)
        {
            if (!_nodeDocks.TryGetValue(nodeName, out var docks))
                return null;

            foreach (var dock in docks)
            {
                var key = (nodeName, dock.PointName);

                if (_occupied.TryUpdate(key, true, false))
                {
                    return dock;
                }
            }

            return null;
        }

        public bool ReleaseDock(string nodeName, string dockName)
        {
            var key = (nodeName, dockName);
            OnDockReleased?.Invoke(nodeName);
            return _occupied.TryUpdate(key, false, true);
        }

        public MapPointDto? GetDock(string nodeName, string dockName)
        {
            _dockLookup.TryGetValue((nodeName, dockName), out var dock);
            return dock;
        }

        public IEnumerable<MapPointDto> GetDocks(string nodeName)
        {
            return _nodeDocks.TryGetValue(nodeName, out var docks)
                ? docks
                : Enumerable.Empty<MapPointDto>();
        }

        public bool IsNodeFull(string nodeName)
        {
            if (!_nodeDocks.TryGetValue(nodeName, out var docks))
                return false;

            return docks.All(dock => _occupied.TryGetValue((nodeName, dock.PointName), out var isOccupied) && isOccupied);
        }
    }
}