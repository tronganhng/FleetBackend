namespace FleetBackend.Services
{
    public interface IMapManager
    {
        bool IsLocationValid(string locationId);
    }

    public class MapManager : IMapManager
    {
        public bool IsLocationValid(string locationId) { throw new System.NotImplementedException(); }
    }
}
