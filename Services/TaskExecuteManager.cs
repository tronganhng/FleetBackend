using FleetBackend.Models;
using System.Text.Json;
using FleetBackend.Services.Task;

namespace FleetBackend.Services
{
    public interface ITaskExecuteManager
    {
        void Clear();
        void ExecuteTask(DeliveryTask task, ICommunicationGateway gateway);
        void RemoveExecutor(string robotId);
        void OnRobotArrived(string robotId);
    }

    public class TaskExecuteManager : ITaskExecuteManager
    {
        private readonly Dictionary<string, TaskExecutor> _executors = new Dictionary<string, TaskExecutor>();

        private readonly IRobotManager _robotManager;
        private readonly IMapManager _mapManager;
        private readonly ITaskManager _taskManager;
        private readonly JsonSerializerOptions _jsonOptions;

        public TaskExecuteManager(IRobotManager robotManager, IMapManager mapManager, ITaskManager taskManager, JsonSerializerOptions jsonOptions)
        {
            _robotManager = robotManager;
            _mapManager = mapManager;
            _taskManager = taskManager;
            _jsonOptions = jsonOptions;
        }

        public void Clear()
        {
            _executors.Clear();
        }

        public void ExecuteTask(DeliveryTask task, ICommunicationGateway gateway)
        {
            if (task.AssignedRobotId == null) return;

            var executor = new TaskExecutor(task, _robotManager, _mapManager, _taskManager, gateway, _jsonOptions);
            executor.OnCompleted = RemoveExecutor;
            _executors[task.AssignedRobotId] = executor;
            executor.Execute();
        }

        public void OnRobotArrived(string robotId)
        {
            if (_executors.TryGetValue(robotId, out var executor))
            {
                executor.OnRobotArrived();
            }
        }

        private void RemoveExecutor(TaskExecutor executor)
        {
            if (!_executors.Remove(executor.RobotId)) return;
        }

        public void RemoveExecutor(string robotId)
        {
            _executors.Remove(robotId);
        }
    }
}
