using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class CreateTaskHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.CreateTask;

    private readonly ITaskManager _taskManager;

    public CreateTaskHandler(ITaskManager taskManager)
    {
        _taskManager = taskManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        DeliveryTask? payloadData = socketMessage.Payload.Deserialize<DeliveryTask>(jsonOptions);
        if (payloadData != null)
        {
            _taskManager.CreateTask(payloadData);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}