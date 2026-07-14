namespace FleetBackend.Services
{
    public interface ITrafficManager
    {
        bool RequestAccess(string robotId, string resourceId);
        void ReleaseAccess(string robotId, string resourceId);
    }

    public class TrafficManager : ITrafficManager
    {
        public bool RequestAccess(string robotId, string resourceId) { throw new System.NotImplementedException(); }
        public void ReleaseAccess(string robotId, string resourceId) { throw new System.NotImplementedException(); }
    }
}
