using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface IScheduler
    {
        RobotStateDto? FindBestRobotForTask(DeliveryTask task);
        void AssignTask(DeliveryTask task, RobotStateDto robot);
    }

    public class Scheduler : IScheduler
    {
        public RobotStateDto? FindBestRobotForTask(DeliveryTask task) { throw new System.NotImplementedException(); }
        public void AssignTask(DeliveryTask task, RobotStateDto robot) { throw new System.NotImplementedException(); }
    }
}
