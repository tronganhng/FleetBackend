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
        private readonly List<DeliveryTask> _tasks = new List<DeliveryTask>();
        private readonly object _lockObject = new object();
        private readonly IEventBus _eventBus;
        
        public TaskManager(IEventBus eventBus)
        {
            _eventBus = eventBus;
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

                task.CreatedAt = DateTime.UtcNow;

                _tasks.Add(task);
                _eventBus.Publish(new TaskCreatedEvent(task));
                return task;
            }
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
    }
}
