using System.Collections.Generic;
using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface IRobotManager
    {
        void RegisterRobot(string robotId);
        void UpdateRobotState(RobotStateDto state);
        RobotStateDto? GetRobot(string robotId);
        IEnumerable<RobotStateDto> GetAllRobots();
    }

    public class RobotManager : IRobotManager
    {
        public void RegisterRobot(string robotId) { throw new System.NotImplementedException(); }
        public void UpdateRobotState(RobotStateDto state) { throw new System.NotImplementedException(); }
        public RobotStateDto? GetRobot(string robotId) { throw new System.NotImplementedException(); }
        public IEnumerable<RobotStateDto> GetAllRobots() { throw new System.NotImplementedException(); }
    }
}
