using FleetBackend.Models;
using System.Text.Json;

namespace FleetBackend.Services.Task
{
    public class TaskExecutor
    {
        private readonly DeliveryTask _task;
        private readonly IRobotManager _robotManager;
        private readonly IMapManager _mapManager;
        private readonly ICommunicationGateway _gateway;
        private readonly ILogger _logger;
        private readonly JsonSerializerOptions _jsonOptions;

        public TaskExecutor(DeliveryTask task, IRobotManager robotManager, IMapManager mapManager, ICommunicationGateway gateway, ILogger logger, JsonSerializerOptions jsonOptions)
        {
            _task = task;
            _robotManager = robotManager;
            _mapManager = mapManager;
            _gateway = gateway;
            _logger = logger;
            _jsonOptions = jsonOptions;
        }

        public DeliveryTask Task => _task;

        public void Execute()
        {
            if (_task.AssignedRobotId == null)
            {
                _logger.LogWarning("Task {TaskId} has no assigned robot.", _task.TaskId);
                return;
            }

            var robot = _robotManager.GetRobot(_task.AssignedRobotId);
            if (robot == null)
            {
                _logger.LogWarning("Robot {RobotId} not found for task {TaskId}", _task.AssignedRobotId, _task.TaskId);
                return;
            }

            _task.Status = FleetBackend.Models.TaskStatus.Running;
            var startNode = _mapManager.Graph.GetNode(_task.PickupLocation);

            var message = new SocketMessage
            {
                Type = SocketMessageType.MoveRobot,
                RequestId = null,
                RobotId = robot.RobotId,
                Payload = JsonSerializer.SerializeToElement(startNode.Position, _jsonOptions)
            };

            _ = _gateway.BroadcastAsync(message, CancellationToken.None);
        }
    }
}