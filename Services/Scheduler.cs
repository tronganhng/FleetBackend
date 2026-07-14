using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface IScheduler
    {
        RobotState? FindBestRobotForTask(DeliveryTask task);
        void AssignTask(DeliveryTask task, RobotState robot);
    }

    public class Scheduler : IScheduler
    {
        public RobotState? FindBestRobotForTask(DeliveryTask task) { throw new System.NotImplementedException(); }
        public void AssignTask(DeliveryTask task, RobotState robot) { throw new System.NotImplementedException(); }
    }
}
