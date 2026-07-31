namespace FleetBackend.Services
{
    public interface ISessionManager
    {
        void Reset();
    }

    public class SessionManager : ISessionManager
    {
        private readonly IRobotManager _robotManager;
        private readonly ITaskManager _taskManager;
        private readonly ITrafficManager _trafficManager;
        private readonly IMapManager _mapManager;

        public SessionManager(IRobotManager robotManager, ITaskManager taskManager, ITrafficManager trafficManager, IMapManager mapManager)
        {
            _robotManager = robotManager;
            _taskManager = taskManager;
            _trafficManager = trafficManager;
            _mapManager = mapManager;
        }

        public void Reset()
        {
            _robotManager.Clear();
            _taskManager.Clear();
            _trafficManager.Clear();
            _mapManager.Clear();
        }
    }
}