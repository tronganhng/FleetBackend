using System.Collections.Generic;
using FleetBackend.Models;
using TaskStatus = FleetBackend.Models.TaskStatus;

namespace FleetBackend.Services
{
    public interface ITaskManager
    {
        DeliveryTask CreateTask(DeliveryTask task);
        void UpdateTaskStatus(string taskId, TaskStatus status);
        IEnumerable<DeliveryTask> GetPendingTasks();
    }

    public class TaskManager : ITaskManager
    {
        public DeliveryTask CreateTask(DeliveryTask task) { throw new System.NotImplementedException(); }
        public void UpdateTaskStatus(string taskId, TaskStatus status) { throw new System.NotImplementedException(); }
        public IEnumerable<DeliveryTask> GetPendingTasks() { throw new System.NotImplementedException(); }
    }
}
