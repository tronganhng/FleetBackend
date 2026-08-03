using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class CheckNodeFullHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.CheckNodeFull;

    private readonly IMapManager _mapManager;

    public CheckNodeFullHandler(IMapManager mapManager)
    {
        _mapManager = mapManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        string? nodeName = socketMessage.Payload.Deserialize<string>(jsonOptions);
        if (nodeName != null)
        {
            bool isNodeFull = _mapManager.Dock.IsNodeFull(nodeName);

            var response = new SocketMessage
            {
                Type = SocketMessageType.ServerResponse,
                RequestId = socketMessage.RequestId,
                Payload = JsonSerializer.SerializeToElement(isNodeFull, jsonOptions),
            };

            return Task.FromResult<SocketMessage?>(response);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}