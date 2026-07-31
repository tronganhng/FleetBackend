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
            _mapManager.Dock.ReleaseDock(payloadData.NodeName, payloadData.PointName);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}