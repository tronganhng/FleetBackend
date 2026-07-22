using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class UpdateTaskHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.UpdateTask;

    private readonly ITaskManager _taskManager;

    public UpdateTaskHandler(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        DeliveryTask? payloadData = socketMessage.Payload.Deserialize<DeliveryTask>(jsonOptions);
        if (payloadData != null)
        {
            _taskManager.UpdateTaskStatus(payloadData.TaskId, payloadData.Status);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}