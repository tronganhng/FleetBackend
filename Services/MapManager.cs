using FleetBackend.Services.Map;

namespace FleetBackend.Services
{
    public interface IMapManager
    {
        IGraphManager Graph { get; }
        IDockManager Dock { get; }
        void Clear();
    }

    public class MapManager : IMapManager
    {
        public IGraphManager Graph { get; }
        public IDockManager Dock { get; }

        public MapManager(IGraphManager graph, IDockManager dock)
        {
            Graph = graph;
            Dock = dock;
        }

        public void Clear()
        {
            Dock.Clear();
        }
    }
}
