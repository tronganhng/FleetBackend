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
    }

    public class TaskManager : ITaskManager
    {
        private readonly List<DeliveryTask> _tasks = new();
        private readonly object _lockObject = new object();
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

                _tasks.Add(task);
                _eventBus.Publish(new TaskCreatedEvent(task));

                SendMessage(task);
                return task;
            }
        }

        public DeliveryTask? GetTask(string id)
        {
            return _tasks.Find(t => t.TaskId == id);
        }

        public void UpdateTaskStatus(string taskId, TaskStatus status)
        {
            if (string.IsNullOrEmpty(taskId))
            {
                throw new ArgumentException("Task ID cannot be null or empty.", nameof(taskId));
            }

            lock (_lockObject)
            {
                var task = _tasks.FirstOrDefault(t => t.TaskId == taskId);
                if (task == null)
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
                    .Where(t => t.Status == TaskStatus.Pending)
                    .ToList();
            }
        }

        private void SendMessage(DeliveryTask task)
        {
            var message = new SocketMessage
            {
                Type = SocketMessageType.UpdateTask,
                RequestId = null,
                Payload = JsonSerializer.SerializeToElement(task, _jsonOptions)
            };
            _ = _gateway.BroadcastAsync(message, CancellationToken.None);
        }
    }
}
