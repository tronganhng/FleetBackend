using System.Collections.Generic;
using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface IRobotManager
    {
        void Clear();
        string RegisterRobot(RobotStateDto state);
        void UpdateRobotState(RobotStateDto state);
        RobotStateDto? GetRobot(string robotId);
        IEnumerable<RobotStateDto> GetAllRobots();
    }

    public class RobotManager : IRobotManager
    {
        private readonly Dictionary<string, RobotStateDto> _robots = new(StringComparer.OrdinalIgnoreCase);

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
            _robots[robotId] = state;

            return robotId;
        }

        public void UpdateRobotState(RobotStateDto state)
        {
            if (string.IsNullOrWhiteSpace(state.RobotId))
            {
                return;
            }

            _robots[state.RobotId] = state;
        }

        public RobotStateDto? GetRobot(string robotId)
        {
            _robots.TryGetValue(robotId, out var robot);
            return robot;
        }

        public IEnumerable<RobotStateDto> GetAllRobots() => _robots.Values;
    }
}
