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
            MoveToDestination,
            MoveToDestinationDock,
            Completed
        }
        private readonly DeliveryTask _task;
        private readonly IRobotManager _robotManager;
        private readonly IMapManager _mapManager;
        private readonly ITaskManager _taskManager;
        private readonly ICommunicationGateway _gateway;
        private readonly JsonSerializerOptions _jsonOptions;

        private RobotStateDto? _robot;
        private TaskExecutorStep _step;

        public string RobotId => _task.AssignedRobotId ?? string.Empty;

        public Action<TaskExecutor>? OnCompleted { get; set; }

        public TaskExecutor(DeliveryTask task, IRobotManager robotManager, IMapManager mapManager, ITaskManager taskManager, ICommunicationGateway gateway, JsonSerializerOptions jsonOptions)
        {
            _task = task;
            _robotManager = robotManager;
            _mapManager = mapManager;
            _taskManager = taskManager;
            _gateway = gateway;
            _jsonOptions = jsonOptions;
        }

        public void Execute()
        {
            if (!TryInitializeRobot())
                return;

            _taskManager.UpdateTaskStatus(_task.TaskId, Models.TaskStatus.Running);

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
            SendMoveCommand(node.Position);
        }

        private void MoveToDestination()
        {
            _step = TaskExecutorStep.MoveToDestination;
            var node = _mapManager.Graph.GetNode(_task.Destination);
            if (node == null) return;
            SendMoveCommand(node.Position);
        }

        private void MoveToPickupDock()
        {
            _step = TaskExecutorStep.MoveToPickupDock;
            var dock = _mapManager.Dock.AcquireDock(_task.PickupLocation);
            if (dock == null)
            {
                _mapManager.Dock.OnDockReleased += OnDockReleased;
                return;
            }
            SendMoveCommand(dock.Position);
            if (_robot != null) 
            {
                _robot._currentDock = dock;
                _robot._currentNode = _mapManager.Graph.GetNode(_task.PickupLocation);
            }
        }

        private void MoveToDestinationDock()
        {
            _step = TaskExecutorStep.MoveToDestinationDock;
            var dock = _mapManager.Dock.AcquireDock(_task.Destination);
            if (dock == null)
            {
                _mapManager.Dock.OnDockReleased += OnDockReleased;
                return;
            }
            SendMoveCommand(dock.Position);
            if (_robot != null) 
            {
                _robot._currentNode = _mapManager.Graph.GetNode(_task.Destination);
                _robot._currentDock = dock;
            }
        }

        private void Complete()
        {
            _step = TaskExecutorStep.Completed;
            _taskManager.UpdateTaskStatus(_task.TaskId, Models.TaskStatus.Completed);
            OnCompleted?.Invoke(this);
            OnCompleted = null;

            SendChangeStateCommand(RobotStatus.Idle);

            if (_mapManager.Dock.IsNodeFull(_task.Destination))
            {
                // move to waiting area
            }
        }

        private void OnDockReleased(string nodeName)
        {
            if (_step == TaskExecutorStep.MoveToPickupDock && nodeName == _task.PickupLocation)
            {
                MoveToPickupDock();
            }
            else if (_step == TaskExecutorStep.MoveToDestinationDock && nodeName == _task.Destination)
            {
                MoveToDestinationDock();
            }
            _mapManager.Dock.OnDockReleased -= OnDockReleased;
        }

        private void SendMoveCommand(float[] position)
        {
            if (_robot == null)
                return;

            if (_robot._currentDock != null && _robot._currentNode != null)
            {
                _mapManager.Dock.ReleaseDock(_robot._currentNode.NodeName, _robot._currentDock.PointName);
                _robot._currentNode = null;
                _robot._currentDock = null;
            }

            var message = new SocketMessage
            {
                Type = SocketMessageType.MoveRobot,
                RequestId = null,
                RobotId = _robot.RobotId,
                Payload = JsonSerializer.SerializeToElement(position, _jsonOptions)
            };

            _ = _gateway.BroadcastAsync(message, CancellationToken.None);
        }

        private void SendChangeStateCommand(RobotStatus status)
        {
            if (_robot == null)
                return;

            var message = new SocketMessage
            {
                Type = SocketMessageType.ChangeRobotStatus,
                RequestId = null,
                RobotId = _robot.RobotId,
                Payload = JsonSerializer.SerializeToElement(status, _jsonOptions)
            };

            _ = _gateway.BroadcastAsync(message, CancellationToken.None);
        }
    }
}