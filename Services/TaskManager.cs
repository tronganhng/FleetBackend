using FleetBackend.Models;
using TaskStatus = FleetBackend.Models.TaskStatus;
using System.Text.Json;

namespace FleetBackend.Services
{
    public interface ITaskManager
    {
        void Clear();
        DeliveryTask CreateTask(DeliveryTask task);
        DeliveryTask? GetTask(string id);
        void UpdateTaskStatus(string taskId, TaskStatus status);
        IEnumerable<DeliveryTask> GetPendingTasks();
        void AssignTaskToRobot(DeliveryTask task, RobotStateDto robotState);
        void CancelTask(DeliveryTask task);
        void ResetTask(DeliveryTask task);
    }

    public class TaskManager : ITaskManager
    {
        private readonly Dictionary<string, DeliveryTask> _tasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lockObject = new();
        private readonly IMapManager _mapManager;
        private readonly ICommunicationGateway _gateway;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IEventBus _eventBus;

        public TaskManager(IMapManager mapManager, IEventBus eventBus, ICommunicationGateway gateway, JsonSerializerOptions jsonOptions)
        {
            _mapManager = mapManager;
            _eventBus = eventBus;
            _gateway = gateway;
            _jsonOptions = jsonOptions;
        }

        public void Clear()
        {
            _tasks.Clear();
        }

        public DeliveryTask CreateTask(DeliveryTask task)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            lock (_lockObject)
            {
                // Ensure unique TaskId
                if (string.IsNullOrEmpty(task.TaskId))
                {
                    task.TaskId = Guid.NewGuid().ToString();
                }

                if (!_mapManager.Graph.HasNode(task.PickupLocation) || !_mapManager.Graph.HasNode(task.Destination))
                {
                    Logger.Log("Invalid Point name on create task");
                    return new();
                }

                task.TaskId = Guid.NewGuid().ToString();
                task.CreatedAt = DateTime.UtcNow;

                _tasks[task.TaskId] = task;
                _eventBus.Publish(new TaskCreatedEvent(task));

                SendMessage(task);
                return task;
            }
        }

        public DeliveryTask? GetTask(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                return null;
            }

            _tasks.TryGetValue(id, out var task);
            return task;
        }

        public void UpdateTaskStatus(string taskId, TaskStatus status)
        {
            if (string.IsNullOrEmpty(taskId))
            {
                throw new ArgumentException("Task ID cannot be null or empty.", nameof(taskId));
            }

            lock (_lockObject)
            {
                if (!_tasks.TryGetValue(taskId, out var task))
                {
                    throw new KeyNotFoundException($"Task with ID '{taskId}' not found.");
                }

                task.Status = status;

                SendMessage(task);
            }
        }

        public IEnumerable<DeliveryTask> GetPendingTasks()
        {
            lock (_lockObject)
            {
                return _tasks
                    .Values
                    .Where(t => t.Status == TaskStatus.Pending)
                    .ToList();
            }
        }

        public void AssignTaskToRobot(DeliveryTask task, RobotStateDto robotState)
        {
            task.AssignedTo(robotState);
            SendMessage(task);
        }

        public void CancelTask(DeliveryTask task)
        {
            task.Cancel();
            SendMessage(task);
        }

        public void ResetTask(DeliveryTask task)
        {
            task.ResetTask();
            SendMessage(task);
        }

        private void SendMessage(DeliveryTask task)
        {
            var message = new SocketMessage
            {
                Type = SocketMessageType.UpdateTask,
                RequestId = null,
                Payload = JsonSerializer.SerializeToElement(task, _jsonOptions)
            };
            _ = _gateway.SendDashboardAsync(message, CancellationToken.None);
        }
    }
}
