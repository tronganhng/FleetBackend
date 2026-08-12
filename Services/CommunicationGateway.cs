using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using FleetBackend.Models;

public class ConnectedSocket
{
    public Guid ConnectionId { get; init; }
    public WebSocket WebSocket { get; init; } = default!;
    public ClientType ClientType { get; set; }
}

public interface ICommunicationGateway
{
    SystemMode SystemMode { get; set; }
    ConcurrentDictionary<Guid, ConnectedSocket> Sockets { get; }
    Task BroadcastAsync(SocketMessage message, CancellationToken cancellationToken = default);
    IEnumerable<ConnectedSocket> GetSocketsByType(ClientType clientType);
}

public class CommunicationGateway : ICommunicationGateway
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<Guid, ConnectedSocket> _sockets = new();

    public SystemMode SystemMode { get; set; }
    public ConcurrentDictionary<Guid, ConnectedSocket> Sockets => _sockets;

    public CommunicationGateway(JsonSerializerOptions jsonOptions)
    {
        SystemMode = SystemMode.Simulation;
        Logger.Log("System mode: " + SystemMode);
        _jsonOptions = jsonOptions;
    }

    public IEnumerable<ConnectedSocket> GetSocketsByType(ClientType clientType)
    {
        return _sockets.Values.Where(socket => socket.ClientType == clientType);
    }

    public async Task BroadcastAsync(SocketMessage message, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        var toRemove = new List<Guid>();

        foreach (var kv in _sockets)
        {
            var id = kv.Key;
            var client = kv.Value;
            var ws = client.WebSocket;

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
