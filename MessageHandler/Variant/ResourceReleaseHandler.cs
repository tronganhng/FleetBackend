using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class ResourceReleaseHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.ResourceRelease;

    private readonly ITrafficManager _trafficManager;

    public ResourceReleaseHandler(ITrafficManager trafficManager)
    {
        _trafficManager = trafficManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        ResourceAccessRequest? payloadData = socketMessage.Payload.Deserialize<ResourceAccessRequest>(jsonOptions);
        if (payloadData != null)
        {
            bool isReleased = _trafficManager.ReleaseAccess(payloadData.RobotId, payloadData.PointName);
            var response = new SocketMessage
            {
                Type = SocketMessageType.ServerResponse,
                RequestId = socketMessage.RequestId,
                Payload = JsonSerializer.SerializeToElement(isReleased, jsonOptions),
            };
            return Task.FromResult<SocketMessage?>(response);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}