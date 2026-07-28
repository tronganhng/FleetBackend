using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class ResourceAccessHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.ResourceAccess;

    private readonly ITrafficManager _trafficManager;

    public ResourceAccessHandler(ITrafficManager trafficManager)
    {
        _trafficManager = trafficManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        ResourceAccessRequest? payloadData = socketMessage.Payload.Deserialize<ResourceAccessRequest>(jsonOptions);
        if (payloadData != null)
        {
            bool canAccess = _trafficManager.RequestAccess(payloadData.RobotId, payloadData.PointName);
            var response = new SocketMessage
            {
                Type = SocketMessageType.ServerResponse,
                RequestId = socketMessage.RequestId,
                Payload = JsonSerializer.SerializeToElement(canAccess, jsonOptions),
            };
            return Task.FromResult<SocketMessage?>(response);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}