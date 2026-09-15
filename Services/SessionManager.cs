using System.Text.Json;
using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface ISessionManager
    {
        string CurrentSessionId { get; }
        SystemMode CurrentMode { get; }
        string StartNewSession(SystemMode mode);
    }

    public class SessionManager : ISessionManager
    {
        private readonly IRobotManager _robotManager;
        private readonly ITaskManager _taskManager;
        private readonly ITrafficManager _trafficManager;
        private readonly IMapManager _mapManager;
        private readonly ITaskExecuteManager _taskExecuteManager;
        private readonly ICommunicationGateway _gateway;
        private readonly JsonSerializerOptions _jsonOptions;

        public string CurrentSessionId { get; private set; } = Guid.NewGuid().ToString("N");
        public SystemMode CurrentMode => _gateway.SystemMode;

        public SessionManager(
            IRobotManager robotManager,
            ITaskManager taskManager,
            ITrafficManager trafficManager,
            IMapManager mapManager,
            ITaskExecuteManager taskExecuteManager,
            ICommunicationGateway gateway,
            JsonSerializerOptions jsonOptions)
        {
            _robotManager = robotManager;
            _taskManager = taskManager;
            _trafficManager = trafficManager;
            _mapManager = mapManager;
            _taskExecuteManager = taskExecuteManager;
            _gateway = gateway;
            _jsonOptions = jsonOptions;
        }

        public string StartNewSession(SystemMode mode)
        {
            CurrentSessionId = Guid.NewGuid().ToString("N");
            _gateway.SystemMode = mode;

            Logger.Log($"[SessionManager] Starting new session '{CurrentSessionId}' with mode '{mode}'");

            _robotManager.Clear();
            _taskManager.Clear();
            _trafficManager.Clear();
            _mapManager.Clear();
            _taskExecuteManager.Clear();
            _gateway.ResetRobotIdSockets();

            return CurrentSessionId;
        }
    }
}