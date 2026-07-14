using System.Collections.Generic;
using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface IRobotManager
    {
        void RegisterRobot(string robotId);
        void UpdateRobotState(RobotState state);
        RobotState? GetRobot(string robotId);
        IEnumerable<RobotState> GetAllRobots();
    }

    public class RobotManager : IRobotManager
    {
        public void RegisterRobot(string robotId) { throw new System.NotImplementedException(); }
        public void UpdateRobotState(RobotState state) { throw new System.NotImplementedException(); }
        public RobotState? GetRobot(string robotId) { throw new System.NotImplementedException(); }
        public IEnumerable<RobotState> GetAllRobots() { throw new System.NotImplementedException(); }
    }
}
