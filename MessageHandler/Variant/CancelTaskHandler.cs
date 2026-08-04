using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class CancelTaskHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.CancelTask;

    private readonly ITaskManager _taskManager;
    private readonly IRobotManager _robotManager;

    public CancelTaskHandler(ITaskManager taskManager, IRobotManager robotManager)
    {
        _taskManager = taskManager;
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
                task.Cancel();
                if (task.AssignedRobotId != null) _robotManager.GetRobot(task.AssignedRobotId)?.ClearCurrentTask();
            }

        }

        return Task.FromResult<SocketMessage?>(null);
    }
}