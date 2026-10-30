using FleetBackend.Models;
using System.Text.Json;

namespace FleetBackend.Services.Task
{
    public class TaskExecutor : IDisposable
    {
        private enum TaskExecutorStep
        {
            None,
            MoveToPickup,
            MoveToPickupDock,
            WaitingToMoveToDestination,
            MoveToDestination,
            MoveToDestinationDock,
            TraversingElevator,
            Completed
        }
        private readonly DeliveryTask _task;
        private readonly IRobotManager _robotManager;
        private readonly IMapManager _mapManager;
        private readonly ITaskManager _taskManager;

        private readonly IElevatorManager? _elevators;
        private readonly ITrafficManager? _traffic;
        private readonly ICommunicationGateway? _gateway;
        private readonly JsonSerializerOptions _json;
        private readonly object _gate;
        private ElevatorTraversal? _traversal;
        private bool _disposed;
        private Robot? _robot;
        private TaskExecutorStep _step;
        private readonly object _dockWaitLock = new();
        private string? _waitingDockNode;

        public string RobotId => _task.AssignedRobotId ?? string.Empty;
        public bool RequiresElevatorRecovery => _traversal?.RequiresRecovery == true;

        public Action<TaskExecutor>? OnCompleted { get; set; }

        public TaskExecutor(DeliveryTask task, IRobotManager robotManager, IMapManager mapManager, ITaskManager taskManager,
            IElevatorManager? elevators = null, ITrafficManager? traffic = null,
            ICommunicationGateway? gateway = null, JsonSerializerOptions? json = null, object? gate = null)
        {
            _task = task;
            _robotManager = robotManager;
            _mapManager = mapManager;
            _taskManager = taskManager;
            _elevators = elevators;
            _traffic = traffic;
            _gateway = gateway;
            _json = json ?? new JsonSerializerOptions();
            _gate = gate ?? new object();
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
            lock (_gate)
            {
                if (_robot == null || _disposed) return;
                if (_traversal != null)
                {
                    _traversal.OnRobotArrived();
                    return;
                }

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
        }

        public void OnElevatorArrived(string elevatorId)
        {
            lock (_gate)
            {
                if (!_disposed) _traversal?.OnElevatorArrived(elevatorId);
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
            _robot?.ChangeStatus(RobotStatus.DoingTask);
            MoveToNode(_task.PickupLocation, TaskExecutorStep.MoveToPickup);
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
            await System.Threading.Tasks.Task.Delay(TimeSpan.FromSeconds(2));

            lock (_gate)
            {
                if (_disposed || _step != TaskExecutorStep.WaitingToMoveToDestination || _robot == null)
                    return;
                MoveToNode(_task.Destination, TaskExecutorStep.MoveToDestination);
            }
        }

        private void MoveToNode(string nodeName, TaskExecutorStep arrivalStep)
        {
            if (_robot == null) return;
            var target = _mapManager.Graph.GetNode(nodeName);
            var current = _robot.CurrentNode ?? _mapManager.Graph.GetRobotNode(_robot.State);
            if (current == null)
            {
                Fail("Cannot determine the robot's current node.");
                return;
            }
            if (current.Floor == target.Floor)
            {
                _step = arrivalStep;
                _robot.MoveTo(target.Position);
                return;
            }

            var elevators = _elevators?.GetAllElevators().ToArray();
            var sources = _mapManager.Graph.GetNodesBy(NodeType.Elevator).Where(n => n.Floor == current.Floor).ToArray();
            var destinations = _mapManager.Graph.GetNodesBy(NodeType.Elevator).Where(n => n.Floor == target.Floor).ToArray();
            if (elevators?.Length != 1 || _traffic == null || _gateway == null ||
                sources.Length != 1 || destinations.Length != 1 ||
                sources[0].MapPoints.Count == 0 || destinations[0].MapPoints.Count == 0)
            {
                Fail("Cross-floor travel requires one registered elevator and one elevator node with cabin points on each floor.");
                return;
            }

            _step = TaskExecutorStep.TraversingElevator;
            _traversal = new ElevatorTraversal(_robot, _mapManager, _elevators!, _traffic, _gateway,
                _json, _gate, sources[0], destinations[0], elevators[0].ElevatorId,
                () =>
                {
                    _traversal = null;
                    _step = arrivalStep;
                    _robot.MoveTo(target.Position);
                }, Fail);
            _traversal.Start();
        }

        private void Fail(string reason)
        {
            Logger.Log($"Task {_task.TaskId} failed: {reason}");
            _taskManager.UpdateTaskStatus(_task.TaskId, Models.TaskStatus.Failed);
            Dispose();
            _robot?.ClearCurrentTask();
            _robot?.ChangeStatus(RobotStatus.Error);
            OnCompleted?.Invoke(this);
            OnCompleted = null;
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                _traversal?.Dispose();
                _traversal = null;
                lock (_dockWaitLock)
                {
                    _waitingDockNode = null;
                    _mapManager.Dock.OnDockReleased -= OnDockReleased;
                }
            }
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
            lock (_gate)
            {
                if (!_disposed) TryMoveToDock(nodeName);
            }
        }
    }
}
