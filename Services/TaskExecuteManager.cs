using FleetBackend.Models;
using FleetBackend.Services.Task;
using System.Text.Json;
using FleetBackend.Services.Elevator;
using FleetBackend.Options;
using Microsoft.Extensions.Options;

namespace FleetBackend.Services
{
    public interface ITaskExecuteManager
    {
        void Clear();
        void ResetSession(Action reset);
        void ExecuteTask(DeliveryTask task);
        bool RemoveExecutor(string robotId);
        void OnRobotArrived(string robotId);
        void OnRobotFloorReady(string robotId, string requestId, RobotFloorReadyPayload payload);
    }

    public class TaskExecuteManager : ITaskExecuteManager, IDisposable
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
        private readonly ElevatorAdapterResolver _adapters;
        private readonly ElevatorRecoveryStore _recovery;
        private readonly ElevatorMqttOptions _options;

        public TaskExecuteManager(IRobotManager robotManager, IMapManager mapManager, ITaskManager taskManager,
            IElevatorManager elevators, ITrafficManager traffic, ICommunicationGateway gateway, JsonSerializerOptions json,
            ElevatorAdapterResolver adapters, ElevatorRecoveryStore recovery, IOptions<ElevatorMqttOptions> options)
        {
            _robotManager = robotManager;
            _mapManager = mapManager;
            _taskManager = taskManager;
            _elevators = elevators;
            _traffic = traffic;
            _gateway = gateway;
            _json = json;
            _adapters = adapters;
            _recovery = recovery;
            _options = options.Value;
        }

        public void Clear()
        {
            lock (_gate)
            {
                // Unsubscribe every waiter before disposing owners (which can grant access).
                var executors = _executors.Values.ToArray();
                _executors.Clear();
                // First cancel every traversal while the original mode/robot sockets still exist.
                foreach (var executor in executors)
                {
                    executor.Dispose();
                    _robotManager.GetRobot(executor.RobotId)?.ClearCurrentTask();
                }
                // Protected cabin ownership survives; queued requests do not survive reset.
                _traffic.Clear();
            }
        }

        public void ExecuteTask(DeliveryTask task)
        {
            if (task.AssignedRobotId == null) return;
            lock (_gate)
            {
                // A scheduler callback from the old session must not restart a cleared task.
                if (!ReferenceEquals(_taskManager.GetTask(task.TaskId), task)) return;
                if (RemoveExecutor(task.AssignedRobotId))
                {
                    _taskManager.UpdateTaskStatus(task.TaskId, Models.TaskStatus.Failed);
                    _robotManager.GetRobot(task.AssignedRobotId)?.ChangeStatus(RobotStatus.Error);
                    return;
                }
                var executor = new TaskExecutor(task, _robotManager, _mapManager, _taskManager,
                    _elevators, _traffic, _gateway, _json, _gate, _adapters, _recovery, _options);
                executor.OnCompleted = RemoveExecutor;
                _executors[task.AssignedRobotId] = executor;
                executor.Execute();
            }
        }

        public void ResetSession(Action reset)
        {
            lock (_gate)
            {
                Clear();
                reset();
            }
        }

        public void Dispose() => Clear();

        public void OnRobotArrived(string robotId)
        {
            lock (_gate)
            {
                if (_executors.TryGetValue(robotId, out var executor)) executor.OnRobotArrived();
            }
        }

        public void OnRobotFloorReady(string robotId, string requestId, RobotFloorReadyPayload payload)
        {
            lock (_gate)
            {
                if (_executors.TryGetValue(robotId, out var executor)) executor.OnRobotFloorReady(requestId, payload);
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
