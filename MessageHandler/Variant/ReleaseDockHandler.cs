using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services;

public class ReleaseDockHandler : IMessageHandler
{
    private readonly IMapManager _mapManager;

    public SocketMessageType MessageType => SocketMessageType.ReleaseDockPoint;

    public ReleaseDockHandler(IMapManager mapManager)
    {
        _mapManager = mapManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        ReleasePointRequest? payloadData = socketMessage.Payload.Deserialize<ReleasePointRequest>(jsonOptions);

        if (payloadData != null)
        {
            bool isReleased = _mapManager.Dock.ReleaseDock(payloadData.NodeName, payloadData.PointName);

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