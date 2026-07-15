using FleetBackend.Models;

public class SocketMessage<T>
{
    public SocketMessageType Type { get; set; }
    public string? RequestId { get; set; }
    public T Payload { get; set; } = default!;
}