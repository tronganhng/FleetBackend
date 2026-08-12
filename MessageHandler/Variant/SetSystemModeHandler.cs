using FleetBackend.Models;
using System.Text.Json;

public class SetSystemModeHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.SetSystemMode;

    private readonly ICommunicationGateway _gateway;

    public SetSystemModeHandler(ICommunicationGateway gateway)
    {
        _gateway = gateway;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        SystemMode systemMode = socketMessage.Payload.Deserialize<SystemMode>(jsonOptions);
        _gateway.SystemMode = systemMode;
        Logger.Log("System mode change: " + systemMode);
        return Task.FromResult<SocketMessage?>(null);
    }
}