using FleetBackend.Models;

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
        private readonly ILogger<Scheduler> _logger;

        public Scheduler(IRobotManager robotManager, ILogger<Scheduler> logger, IEventBus eventBus)
        {
            _robotManager = robotManager;
            _logger = logger;
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
            // 2. Lấy robot Idle
            // 3. Chọn robot phù hợp
            // 4. Giao task
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
