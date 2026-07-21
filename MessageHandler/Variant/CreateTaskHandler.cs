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
            var task = _taskManager.CreateTask(payloadData);
            var response = new SocketMessage
            {
                Type = SocketMessageType.ServerResponse,
                RequestId = socketMessage.RequestId,
                Payload = JsonSerializer.SerializeToElement(task, jsonOptions),
            };

            return Task.FromResult<SocketMessage?>(response);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}