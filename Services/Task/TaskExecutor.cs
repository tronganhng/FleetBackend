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
            var node = _mapManager.Graph.GetNode(_task.PickupLocation);
            if (node == null) return;
            _step = TaskExecutorStep.MoveToPickup;
            SendMoveCommand(node.Position);
        }

        private void MoveToDestination()
        {
            var node = _mapManager.Graph.GetNode(_task.Destination);
            if (node == null) return;
            _step = TaskExecutorStep.MoveToDestination;
            SendMoveCommand(node.Position);
        }

        private void MoveToPickupDock()
        {
            var dock = _mapManager.Dock.AcquireDock(_task.PickupLocation);
            if (dock == null) return;
            _step = TaskExecutorStep.MoveToPickupDock;
            SendMoveCommand(dock.Position);
        }

        private void MoveToDestinationDock()
        {
            var dock = _mapManager.Dock.AcquireDock(_task.Destination);
            if (dock == null) return;
            _step = TaskExecutorStep.MoveToDestinationDock;
            SendMoveCommand(dock.Position);
        }

        private void Complete()
        {
            _step = TaskExecutorStep.Completed;
            _taskManager.UpdateTaskStatus(_task.TaskId, Models.TaskStatus.Completed);
        }

        private void SendMoveCommand(float[] position)
        {
            if (_robot == null)
                return;

            var message = new SocketMessage
            {
                Type = SocketMessageType.MoveRobot,
                RequestId = null,
                RobotId = _robot.RobotId,
                Payload = JsonSerializer.SerializeToElement(position, _jsonOptions)
            };

            _ = _gateway.BroadcastAsync(message, CancellationToken.None);
        }
    }
}