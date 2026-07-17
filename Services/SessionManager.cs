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

        public SessionManager(IRobotManager robotManager, ITaskManager taskManager)
        {
            _robotManager = robotManager;
            _taskManager = taskManager;
        }

        public void Reset()
        {
            _robotManager.Clear();
            _taskManager.Clear();
        }
    }
}