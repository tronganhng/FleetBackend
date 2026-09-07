using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface IRobotBatteryWatcher
    {
        void Tick();
    }

    public class RobotBatteryWatcher : IRobotBatteryWatcher
    {
        private readonly IRobotManager _robotManager;
        private readonly IMapManager _mapManager;

        public RobotBatteryWatcher(IRobotManager robotManager, IMapManager mapManager)
        {
            _robotManager = robotManager;
            _mapManager = mapManager;
        }

        public void Tick()
        {
            var idleRobots = _robotManager.GetAllRobots().Where(r => r.State.Status == RobotStatus.Idle);
            foreach (var robot in idleRobots)
            {
                if (robot.State.Battery <= 50)
                {
                    robot.TryGoToChargingDock();
                    robot.ChangeStatus(RobotStatus.Charging);
                }
            }

            var chargingRobots = _robotManager.GetAllRobots().Where(r => r.State.Status == RobotStatus.Charging);
            foreach (var robot in chargingRobots)
            {
                if (robot.State.Battery >= 95)
                {
                    robot.ChangeStatus(RobotStatus.Idle);
                }
            }
        }
    }
}