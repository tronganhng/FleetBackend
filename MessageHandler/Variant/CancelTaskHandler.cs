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
                _taskManager.CancelTask(task);
                if (task.AssignedRobotId != null) 
                {
                    _robotManager.GetRobot(task.AssignedRobotId)?.ClearCurrentTask();
                    _taskExecuteManager.RemoveExecutor(task.AssignedRobotId);
                }
                
                var response = new SocketMessage
                {
                    Type = SocketMessageType.ServerResponse,
                    RequestId = socketMessage.RequestId,
                    Payload = JsonSerializer.SerializeToElement(task, jsonOptions),
                };

                return Task.FromResult<SocketMessage?>(response);
            }

        }

        return Task.FromResult<SocketMessage?>(null);
    }
}