using System.Collections.Generic;
using FleetBackend.Models;

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
        private readonly IEventBus _eventBus;

        public RobotManager(IEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void Clear()
        {
            _robots.Clear();
        }

        public string RegisterRobot(RobotStateDto state)
        {
            var robotId = string.IsNullOrWhiteSpace(state.RobotId)
                ? $"Robot_{_robots.Count + 1:00}"
                : state.RobotId;

            state.RobotId = robotId;
            state.LastHeartbeat = state.LastHeartbeat == default ? DateTime.UtcNow : state.LastHeartbeat;
            _robots[robotId] = new Robot(state);

            return robotId;
        }

        public void UpdateRobotState(RobotStateDto state)
        {
            if (string.IsNullOrWhiteSpace(state.RobotId))
            {
                return;
            }

            var previousStatus = _robots[state.RobotId].State.Status;
            _robots[state.RobotId].State.CopyFrom(state);
            if (state.Status == RobotStatus.Idle && previousStatus != RobotStatus.Idle) _eventBus.Publish(new RobotBackToIdleEvent(_robots[state.RobotId].State));
        }

        public Robot? GetRobot(string robotId)
        {
            _robots.TryGetValue(robotId, out var robot);
            return robot;
        }

        public IEnumerable<Robot> GetAllRobots() => _robots.Values;
    }
}
