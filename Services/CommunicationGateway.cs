using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;

public interface ICommunicationGateway
{
    ConcurrentDictionary<Guid, WebSocket> Sockets { get; }
    Task BroadcastAsync(SocketMessage message, CancellationToken cancellationToken = default);
}

public class CommunicationGateway : ICommunicationGateway
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();

    public ConcurrentDictionary<Guid, WebSocket> Sockets => _sockets;

    public CommunicationGateway(JsonSerializerOptions jsonOptions)
    {
        _jsonOptions = jsonOptions;
    }

    public async Task BroadcastAsync(SocketMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        var toRemove = new List<Guid>();

        foreach (var kv in _sockets)
        {
            var id = kv.Key;
            var ws = kv.Value;

            if (ws.State != WebSocketState.Open)
            {
                toRemove.Add(id);
                continue;
            }

            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            }
            catch
            {
                toRemove.Add(id);
            }
        }

        foreach (var id in toRemove)
        {
            _sockets.TryRemove(id, out _);
        }
    }
}
