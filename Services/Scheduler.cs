using FleetBackend.Models;
using System.Text.Json;

namespace FleetBackend.Services
{
    public interface IScheduler
    {
        void OnTaskCreated(TaskCreatedEvent e);

        void OnRobotIdle(RobotBackToIdleEvent e);

        void OnRobotOffline(RobotStateDto robot);

        void OnRobotStateChanged(RobotStateDto robot);

        void Tick();
    }

    public class Scheduler : IScheduler
    {
        private readonly IRobotManager _robotManager;
        private readonly ITaskManager _taskManager;
        private readonly ILogger<Scheduler> _logger;
        private readonly ICommunicationGateway _gateway;
        private readonly ICostCaculator _costCaculator;
        private readonly JsonSerializerOptions _jsonOptions;

        public Scheduler(IRobotManager robotManager, ITaskManager taskManager, ICostCaculator costCaculator, ILogger<Scheduler> logger, IEventBus eventBus, ICommunicationGateway gateway, JsonSerializerOptions jsonOptions)
        {
            _robotManager = robotManager;
            _taskManager = taskManager;
            _costCaculator = costCaculator;
            _logger = logger;
            _gateway = gateway;
            _jsonOptions = jsonOptions;
            eventBus.Subscribe<TaskCreatedEvent>(OnTaskCreated);
            eventBus.Subscribe<RobotBackToIdleEvent>(OnRobotIdle);
        }

        public void OnTaskCreated(TaskCreatedEvent e)
        {
            _logger.LogInformation("Scheduler Trigger : Task Created ({TaskId})", e.Task.TaskId);

            Schedule();
        }

        public void OnRobotIdle(RobotBackToIdleEvent e)
        {
            _logger.LogInformation("Scheduler Trigger : Robot Idle");

            // check and move robot to waiting area

            Schedule();
        }

        public void OnRobotOffline(RobotStateDto robot)
        {
            _logger.LogWarning("{RobotId} timeout with task: {Task}", robot.RobotId, robot.CurrentTaskId ?? "No Task");

            // TODO:
            // Reassign unfinished task
            if (robot.CurrentTaskId == null) return;

            DeliveryTask? task = _taskManager.GetTask(robot.CurrentTaskId);

            if (task == null || task.Status != Models.TaskStatus.Running) return;

            robot.ClearCurrentTask();
            task.ResetTask();

            Schedule();
        }

        public void OnRobotStateChanged(RobotStateDto robot)
        {
            // Ví dụ:
            // Battery
            // Charging
            // Pause
            // Resume
        }

        public void Tick()
        {
            CheckHeartbeat();
        }

        private void CheckHeartbeat()
        {
            foreach (var robot in _robotManager.GetAllRobots())
            {
                if (robot.Status == RobotStatus.Offline)
                    continue;

                if (DateTime.UtcNow - robot.LastHeartbeat > TimeSpan.FromSeconds(5))
                {
                    robot.Status = RobotStatus.Offline;

                    OnRobotOffline(robot);
                }
            }
        }

        private void Schedule()
        {
            // TODO:
            // 1. Lấy task Pending
            var task = _taskManager
                        .GetPendingTasks()
                        .OrderByDescending(t => t.Priority)
                        .ThenBy(t => t.CreatedAt)
                        .FirstOrDefault();
            if (task == null) return;

            // 2. Chọn robot phù hợp
            var robots = _robotManager.GetAllRobots().Where(r => r.Status == RobotStatus.Idle);

            if (robots.Count() == 0)
            {
                Logger.Log("No idle robots available.");
                return;
            }

            RobotStateDto? bestRobot = null;
            float bestCost = float.MaxValue;
            foreach (var robot in robots)
            {
                float cost = _costCaculator.GetCost(task, robot);

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestRobot = robot;
                }
            }

            if (bestRobot == null) return;

            // 4. Giao task
            task.AssignedTo(bestRobot);

            var message = new SocketMessage
            {
                Type = SocketMessageType.TaskAssigned,
                RequestId = null,
                Payload = JsonSerializer.SerializeToElement(task, _jsonOptions)
            };

            _ = _gateway.BroadcastAsync(message, CancellationToken.None);
        }
    }
}
