using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class AcquireDockHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.AcquireDockPoint;

    private readonly IMapManager _mapManager;

    public AcquireDockHandler(IMapManager taskManager)
    {
        _mapManager = taskManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        string? nodeName = socketMessage.Payload.Deserialize<string>(jsonOptions);
        if (nodeName != null)
        {
            MapPointDto? point = _mapManager.Dock.AcquireDock(nodeName);
            if (point != null)
            {
                var response = new SocketMessage
                {
                    Type = SocketMessageType.ServerResponse,
                    RequestId = socketMessage.RequestId,
                    Payload = JsonSerializer.SerializeToElement(point, jsonOptions),
                };

                return Task.FromResult<SocketMessage?>(response);
            }
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}