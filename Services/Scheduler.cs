using FleetBackend.Models;
using FleetBackend.Services.Task;
using System.Text.Json;

namespace FleetBackend.Services
{
    public interface IScheduler
    {
        void OnTaskCreated(TaskCreatedEvent e);

        void OnRobotIdle(RobotBackToIdleEvent e);

        void OnRobotOffline(Robot robot);

        void OnRobotStateChanged(RobotStateDto robot);

        void Tick();
    }

    public class Scheduler : IScheduler
    {
        private readonly IRobotManager _robotManager;
        private readonly IMapManager _mapManager;
        private readonly ITaskManager _taskManager;
        private readonly ITaskExecuteManager _taskExecuteManager;
        private readonly ILogger<Scheduler> _logger;
        private readonly ICostCaculator _costCaculator;

        public Scheduler(IRobotManager robotManager, IMapManager mapManager, ITaskExecuteManager taskExecuteManager, ITaskManager taskManager, ICostCaculator costCaculator, ILogger<Scheduler> logger, IEventBus eventBus)
        {
            _robotManager = robotManager;
            _mapManager = mapManager;
            _taskManager = taskManager;
            _taskExecuteManager = taskExecuteManager;
            _costCaculator = costCaculator;
            _logger = logger;
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

        public void OnRobotOffline(Robot robot)
        {
            _logger.LogWarning("{RobotId} timeout with task: {Task}", robot.State.RobotId, robot.State.CurrentTaskId ?? "No Task");

            // TODO:
            // Reassign unfinished task
            if (robot.State.CurrentTaskId == null) return;

            DeliveryTask? task = _taskManager.GetTask(robot.State.CurrentTaskId);

            if (task == null || task.Status != Models.TaskStatus.Running) return;

            robot.ClearCurrentTask();
            _taskManager.ResetTask(task);
            _taskExecuteManager.RemoveExecutor(robot.State.RobotId);

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
                if (robot.State.Status == RobotStatus.Offline)
                    continue;

                if (DateTime.Now - robot.State.LastHeartbeat > TimeSpan.FromSeconds(5))
                {
                    robot.ChangeStatus(RobotStatus.Offline);

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
            var robots = _robotManager.GetAllRobots().Where(r => r.State.Status == RobotStatus.Idle);

            if (robots.Count() == 0)
            {
                Logger.Log("No idle robots available.");
                return;
            }

            RobotStateDto? bestRobot = null;
            float bestCost = float.MaxValue;
            foreach (var robot in robots)
            {
                float cost = _costCaculator.GetCost(task, robot.State);

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestRobot = robot.State;
                }
            }

            if (bestRobot == null) return;

            // 4. Giao task
            _taskManager.AssignTaskToRobot(task, bestRobot);
            _taskExecuteManager.ExecuteTask(task);
        }
    }
}
