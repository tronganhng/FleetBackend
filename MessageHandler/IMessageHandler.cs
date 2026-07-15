using FleetBackend.Models;

public interface IMessageHandler
{
    SocketMessageType MessageType { get; }
    Task<SocketMessage?> HandleAsync(SocketMessage message);
}