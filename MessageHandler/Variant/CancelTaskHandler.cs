using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class CancelTaskHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.CancelTask;

    private readonly ITaskManager _taskManager;
    private readonly ITaskExecuteManager _taskExecuteManager;
    private readonly IRobotManager _robotManager;

    public CancelTaskHandler(ITaskManager taskManager, ITaskExecuteManager taskExecuteManager, IRobotManager robotManager)
    {
        _taskManager = taskManager;
        _taskExecuteManager = taskExecuteManager;
        _robotManager = robotManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        DeliveryTask? payloadData = socketMessage.Payload.Deserialize<DeliveryTask>(jsonOptions);
        if (payloadData != null)
        {
            var task = _taskManager.GetTask(payloadData.TaskId);
            if (task != null)
            {
                if (task.AssignedRobotId != null) 
                {
                    var requiresRecovery = _taskExecuteManager.RemoveExecutor(task.AssignedRobotId);
                    var robot  = _robotManager.GetRobot(task.AssignedRobotId);
                    if (robot != null) 
                    {
                        robot.ClearCurrentTask();
                        robot.ChangeStatus(requiresRecovery ? RobotStatus.Error : RobotStatus.Idle);
                    }
                }
                _taskManager.CancelTask(task);
            }

        }

        return Task.FromResult<SocketMessage?>(null);
    }
}
