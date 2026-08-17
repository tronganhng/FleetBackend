using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using FleetBackend.Models;

public class ConnectedSocket
{
    public Guid ConnectionId { get; init; }
    public WebSocket WebSocket { get; init; } = default!;
}

public interface ICommunicationGateway
{
    SystemMode SystemMode { get; set; }
    ConcurrentDictionary<Guid, ConnectedSocket> UnitySockets { get; }
    ConcurrentDictionary<string, ConnectedSocket> RobotSockets { get; }

    Task SendCommandAsync(SocketMessage message, CancellationToken cancellationToken = default);
    Task SendDashboardAsync(SocketMessage message, CancellationToken cancellationToken = default);
    void RemoveSocket(Guid connectionId);
}

public class CommunicationGateway : ICommunicationGateway
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<Guid, ConnectedSocket> unitySockets = new();
    private readonly ConcurrentDictionary<string, ConnectedSocket> robotSockets = new();

    public SystemMode SystemMode { get; set; }
    public ConcurrentDictionary<Guid, ConnectedSocket> UnitySockets => unitySockets;
    public ConcurrentDictionary<string, ConnectedSocket> RobotSockets => robotSockets;

    public CommunicationGateway(JsonSerializerOptions jsonOptions)
    {
        SystemMode = SystemMode.Simulation;
        _jsonOptions = jsonOptions;
    }

    public void RemoveSocket(Guid connectionId)
    {
        if (unitySockets.TryRemove(connectionId, out _))
            return;

        foreach (var pair in robotSockets)
        {
            if (pair.Value.ConnectionId == connectionId)
            {
                robotSockets.TryRemove(pair.Key, out _);
                return;
            }
        }
    }

    public async Task SendCommandAsync(SocketMessage message, CancellationToken cancellationToken = default)
    {
        if (SystemMode == SystemMode.Operation)
        {
            await SendRobot(message, cancellationToken);
        }
        else if (SystemMode == SystemMode.Simulation)
        {
            await SendUnity(message, cancellationToken);
        }
    }

    public async Task SendDashboardAsync(SocketMessage message, CancellationToken cancellationToken = default)
    {
        await SendUnity(message, cancellationToken);
    }

    private async Task SendUnity(SocketMessage message, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        var toRemove = new List<Guid>();
        foreach (var kv in unitySockets)
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
            unitySockets.TryRemove(id, out _);
        }
    }

    private async Task SendRobot(SocketMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.RobotId))
            return;

        if (!robotSockets.TryGetValue(message.RobotId, out var client))
        {
            return;
        }

        var ws = client.WebSocket;

        if (ws.State != WebSocketState.Open)
        {
            robotSockets.TryRemove(message.RobotId, out _);
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(message, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
        }
        catch
        {
            robotSockets.TryRemove(message.RobotId, out _);
        }
    }
}
