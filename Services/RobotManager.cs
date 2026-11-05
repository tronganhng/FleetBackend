using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services.Elevator;

namespace FleetBackend.Services
{
    public interface IRobotManager
    {
        void Clear();
        string RegisterRobot(RobotStateDto state);
        void UpdateRobotState(RobotStateDto state);
        Robot? GetRobot(string robotId);
        IEnumerable<Robot> GetAllRobots();
    }

    public class RobotManager : IRobotManager
    {
        private readonly Dictionary<string, Robot> _robots = new(StringComparer.OrdinalIgnoreCase);

        private readonly IMapManager _mapManager;
        private readonly ICommunicationGateway _gateway;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IEventBus _eventBus;
        private readonly ElevatorRecoveryStore? _recovery;

        public RobotManager(IEventBus eventBus, IMapManager mapManager, ICommunicationGateway gateway, JsonSerializerOptions jsonOptions,
            ElevatorRecoveryStore? recovery = null)
        {
            _eventBus = eventBus;
            _mapManager = mapManager;
            _gateway = gateway;
            _jsonOptions = jsonOptions;
            _recovery = recovery;
        }

        public void Clear()
        {
            _robots.Clear();
        }

        public string RegisterRobot(RobotStateDto state)
        {
            var robotId = string.IsNullOrWhiteSpace(state.RobotId)
                ? GenerateUniqueRobotId()
                : state.RobotId;

            state.RobotId = robotId;
            if (_gateway.SystemMode == SystemMode.Simulation) state.Floor = 1;
            if (_recovery?.GetAll().Any(entry => entry.RobotId == robotId) == true) state.Status = RobotStatus.Error;
            state.LastHeartbeat = state.LastHeartbeat == default ? DateTime.Now : state.LastHeartbeat;
            _robots[robotId] = new Robot(state, _mapManager, _eventBus, _gateway, _jsonOptions);

            return robotId;
        }

        private string GenerateUniqueRobotId()
        {
            int index = 1;
            while (_robots.ContainsKey($"Robot_{index:00}"))
            {
                index++;
            }
            return $"Robot_{index:00}";
        }

        public void UpdateRobotState(RobotStateDto state)
        {
            if (string.IsNullOrWhiteSpace(state.RobotId))
            {
                return;
            }

            if (!_robots.TryGetValue(state.RobotId, out var robot))
            {
                Logger.Log($"Received RobotState for unregistered robot '{state.RobotId}'. Ignoring.");
                return;
            }

            robot.State.CopyFrom(state);
            if (robot.State.Status == RobotStatus.Offline)
            {
                robot.ChangeStatus(_recovery?.GetAll().Any(entry => entry.RobotId == state.RobotId) == true
                    ? RobotStatus.Error : RobotStatus.Idle);
            }
        }

        public Robot? GetRobot(string robotId)
        {
            _robots.TryGetValue(robotId, out var robot);
            return robot;
        }

        public IEnumerable<Robot> GetAllRobots() => _robots.Values;
    }
}
