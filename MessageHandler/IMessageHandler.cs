using FleetBackend.Models;
using System.Text.Json;

public interface IMessageHandler
{
    SocketMessageType MessageType { get; }
    Task<SocketMessage?> HandleAsync(SocketMessage message, JsonSerializerOptions jsonOptions);
}