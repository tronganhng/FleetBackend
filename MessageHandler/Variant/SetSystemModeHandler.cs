using FleetBackend.Models;
using System.Text.Json;
using FleetBackend.Services;

public class SetSystemModeHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.SetSystemMode;

    private readonly ICommunicationGateway _gateway;
    private readonly ISessionManager _sessionManager;

    public SetSystemModeHandler(ICommunicationGateway gateway, ISessionManager sessionManager)
    {
        _gateway = gateway;
        _sessionManager = sessionManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        SystemMode systemMode = socketMessage.Payload.Deserialize<SystemMode>(jsonOptions);

        _sessionManager.StartNewSession(systemMode);

        return Task.FromResult<SocketMessage?>(null);
    }
}