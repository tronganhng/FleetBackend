using FleetBackend.Models;
using System.Text.Json;

namespace FleetBackend.Services
{
    public interface IScheduler
    {
        void OnTaskCreated(TaskCreatedEvent e);

        void OnRobotIdle(RobotStateDto robot);

        void OnRobotOffline(RobotStateDto robot);

        void OnRobotStateChanged(RobotStateDto robot);

        Task TickAsync(CancellationToken cancellationToken);
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
        }

        public void OnTaskCreated(TaskCreatedEvent e)
        {
            _logger.LogInformation("Scheduler Trigger : Task Created ({TaskId})", e.Task.TaskId);

            Schedule();
        }

        public void OnRobotIdle(RobotStateDto robot)
        {
            _logger.LogInformation("Scheduler Trigger : Robot Idle ({RobotId})", robot.RobotId);

            Schedule();
        }

        public void OnRobotOffline(RobotStateDto robot)
        {
            _logger.LogWarning("Scheduler Trigger : Robot Offline ({RobotId})", robot.RobotId);

            // TODO:
            // Reassign unfinished task

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

        public async Task TickAsync(CancellationToken cancellationToken)
        {
            CheckHeartbeat();

            await Task.CompletedTask;
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

                    _logger.LogWarning("Robot {RobotId} timeout.", robot.RobotId);

                    OnRobotOffline(robot);
                }
            }
        }
    }
}
