public class SocketMessage<T>
{
    public string Type { get; set; } = string.Empty;
    public string? RequestId { get; set; }
    public T Payload { get; set; } = default!;
}