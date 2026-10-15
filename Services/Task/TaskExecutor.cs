using FleetBackend.Models;
using System.Text.Json;

namespace FleetBackend.Services.Task
{
    public class TaskExecutor
    {
        private enum TaskExecutorStep
        {
            None,
            MoveToPickup,
            MoveToPickupDock,
            WaitingToMoveToDestination,
            MoveToDestination,
            MoveToDestinationDock,
            Completed
        }
        private readonly DeliveryTask _task;
        private readonly IRobotManager _robotManager;
        private readonly IMapManager _mapManager;
        private readonly ITaskManager _taskManager;

        private Robot? _robot;
        private TaskExecutorStep _step;
        private readonly object _dockWaitLock = new();
        private string? _waitingDockNode;

        public string RobotId => _task.AssignedRobotId ?? string.Empty;

        public Action<TaskExecutor>? OnCompleted { get; set; }

        public TaskExecutor(DeliveryTask task, IRobotManager robotManager, IMapManager mapManager, ITaskManager taskManager)
        {
            _task = task;
            _robotManager = robotManager;
            _mapManager = mapManager;
            _taskManager = taskManager;
        }

        public void Execute()
        {
            if (!TryInitializeRobot())
                return;

            _taskManager.UpdateTaskStatus(_task.TaskId, Models.TaskStatus.Running);

            if (_robot != null && _robot.CurrentNode == _mapManager.Graph.GetNode(_task.PickupLocation))
            {
                _robot.ChangeStatus(RobotStatus.DoingTask);
                MoveToDestination();
            }
            else
                MoveToPickup();
        }

        public void OnRobotArrived()
        {
            if (_robot == null)
                return;

            switch (_step)
            {
                case TaskExecutorStep.MoveToPickup:
                    MoveToPickupDock();
                    break;

                case TaskExecutorStep.MoveToPickupDock:
                    MoveToDestination();
                    break;

                case TaskExecutorStep.MoveToDestination:
                    MoveToDestinationDock();
                    break;

                case TaskExecutorStep.MoveToDestinationDock:
                    Complete();
                    break;
            }
        }

        private bool TryInitializeRobot()
        {
            if (string.IsNullOrWhiteSpace(_task.AssignedRobotId)) return false;

            _robot = _robotManager.GetRobot(_task.AssignedRobotId);

            if (_robot == null) return false;

            return true;
        }

        private void MoveToPickup()
        {
            _step = TaskExecutorStep.MoveToPickup;
            var node = _mapManager.Graph.GetNode(_task.PickupLocation);
            if (node == null) return;
            _robot?.MoveTo(node.Position);
            _robot?.ChangeStatus(RobotStatus.DoingTask);
        }

        private void MoveToPickupDock()
        {
            _step = TaskExecutorStep.MoveToPickupDock;
            WaitForDock(_task.PickupLocation);
        }

        private void MoveToDestination()
        {
            _step = TaskExecutorStep.WaitingToMoveToDestination;
            _ = MoveToDestinationAfterDelayAsync();
        }

        private async System.Threading.Tasks.Task MoveToDestinationAfterDelayAsync()
        {
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(5));

            if (_step != TaskExecutorStep.WaitingToMoveToDestination || _robot == null)
                return;

            _step = TaskExecutorStep.MoveToDestination;
            var node = _mapManager.Graph.GetNode(_task.Destination);
            if (node == null) return;
            _robot.MoveTo(node.Position);
        }

        private void MoveToDestinationDock()
        {
            _step = TaskExecutorStep.MoveToDestinationDock;
            WaitForDock(_task.Destination);
        }

        private void WaitForDock(string nodeName)
        {
            lock (_dockWaitLock)
            {
                // Subscribe before acquiring so a concurrent release cannot be missed.
                if (_waitingDockNode == null)
                    _mapManager.Dock.OnDockReleased += OnDockReleased;

                _waitingDockNode = nodeName;
            }

            TryMoveToDock(nodeName);
        }

        private void TryMoveToDock(string nodeName)
        {
            MapPointDto? dock;
            lock (_dockWaitLock)
            {
                if (_robot == null || _waitingDockNode != nodeName)
                    return;

                dock = _mapManager.Dock.AcquireDock(nodeName);
                if (dock == null)
                    return;

                _waitingDockNode = null;
                _mapManager.Dock.OnDockReleased -= OnDockReleased;
            }

            // Moving can release another dock and invoke other executors' callbacks.
            _robot.MoveTo(dock.Position);
            _robot.CurrentNode = _mapManager.Graph.GetNode(nodeName);
            _robot.CurrentDock = dock;
        }

        private void Complete()
        {
            _step = TaskExecutorStep.Completed;
            _taskManager.UpdateTaskStatus(_task.TaskId, Models.TaskStatus.Completed);
            OnCompleted?.Invoke(this);
            OnCompleted = null;

            _robot?.ChangeStatus(RobotStatus.Idle);
        }

        private void OnDockReleased(string nodeName)
        {
            TryMoveToDock(nodeName);
        }
    }
}
