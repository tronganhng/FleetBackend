using FleetBackend.Models;
using FleetBackend.Services.Task;
using System.Text.Json;

namespace FleetBackend.Services
{
    public interface ITaskExecuteManager
    {
        void Clear();
        void ExecuteTask(DeliveryTask task);
        bool RemoveExecutor(string robotId);
        void OnRobotArrived(string robotId);
        void OnElevatorArrived(string elevatorId);
    }

    public class TaskExecuteManager : ITaskExecuteManager
    {
        private readonly Dictionary<string, TaskExecutor> _executors = new(StringComparer.OrdinalIgnoreCase);
        // All executor callbacks, timers and delayed continuations share this gate.
        private readonly object _gate = new();
        private readonly IRobotManager _robotManager;
        private readonly IMapManager _mapManager;
        private readonly ITaskManager _taskManager;
        private readonly IElevatorManager _elevators;
        private readonly ITrafficManager _traffic;
        private readonly ICommunicationGateway _gateway;
        private readonly JsonSerializerOptions _json;

        public TaskExecuteManager(IRobotManager robotManager, IMapManager mapManager, ITaskManager taskManager,
            IElevatorManager elevators, ITrafficManager traffic, ICommunicationGateway gateway, JsonSerializerOptions json)
        {
            _robotManager = robotManager;
            _mapManager = mapManager;
            _taskManager = taskManager;
            _elevators = elevators;
            _traffic = traffic;
            _gateway = gateway;
            _json = json;
        }

        public void Clear()
        {
            lock (_gate)
            {
                // Unsubscribe every waiter before disposing owners (which can grant access).
                var executors = _executors.Values.ToArray();
                _executors.Clear();
                _traffic.Clear();
                foreach (var executor in executors) executor.Dispose();
            }
        }

        public void ExecuteTask(DeliveryTask task)
        {
            if (task.AssignedRobotId == null) return;
            lock (_gate)
            {
                RemoveExecutor(task.AssignedRobotId);
                var executor = new TaskExecutor(task, _robotManager, _mapManager, _taskManager,
                    _elevators, _traffic, _gateway, _json, _gate);
                executor.OnCompleted = RemoveExecutor;
                _executors[task.AssignedRobotId] = executor;
                executor.Execute();
            }
        }

        public void OnRobotArrived(string robotId)
        {
            lock (_gate)
            {
                if (_executors.TryGetValue(robotId, out var executor)) executor.OnRobotArrived();
            }
        }

        public void OnElevatorArrived(string elevatorId)
        {
            lock (_gate)
            {
                foreach (var executor in _executors.Values.ToArray()) executor.OnElevatorArrived(elevatorId);
            }
        }

        private void RemoveExecutor(TaskExecutor executor)
        {
            lock (_gate)
            {
                if (_executors.TryGetValue(executor.RobotId, out var active) && ReferenceEquals(active, executor))
                    RemoveExecutor(executor.RobotId);
            }
        }

        public bool RemoveExecutor(string robotId)
        {
            lock (_gate)
            {
                if (!_executors.Remove(robotId, out var executor)) return false;
                var requiresRecovery = executor.RequiresElevatorRecovery;
                executor.Dispose();
                return requiresRecovery;
            }
        }
    }
}
