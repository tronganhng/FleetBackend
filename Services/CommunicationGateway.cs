using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using FleetBackend.Models;

public class ConnectedSocket : IDisposable
{
    public Guid ConnectionId { get; init; }
    public WebSocket WebSocket { get; init; } = default!;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public async Task SendTextAsync(byte[] bytes, CancellationToken cancellationToken = default)
    {
        if (WebSocket.State != WebSocketState.Open) return;

        await _sendLock.WaitAsync(cancellationToken);
        try
        {
            if (WebSocket.State == WebSocketState.Open)
            {
                await WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
            }
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        _sendLock.Dispose();
    }
}

public interface ICommunicationGateway
{
    SystemMode SystemMode { get; set; }
    IReadOnlyDictionary<Guid, ConnectedSocket> UnitySockets { get; }
    IReadOnlyDictionary<string, ConnectedSocket> RobotSockets { get; }

    void RegisterUnitySocket(Guid connectionId, WebSocket socket);
    void RegisterRobotSocket(string robotId, Guid connectionId, WebSocket socket);
    void RemoveSocket(Guid connectionId);

    Task SendCommandAsync(SocketMessage message, CancellationToken cancellationToken = default);
    Task SendDashboardAsync(SocketMessage message, CancellationToken cancellationToken = default);
}

public class CommunicationGateway : ICommunicationGateway
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<Guid, ConnectedSocket> _unitySockets = new();
    private readonly ConcurrentDictionary<string, ConnectedSocket> _robotSockets = new();
    private readonly ConcurrentDictionary<Guid, string> _connectionToRobotId = new();

    public SystemMode SystemMode { get; set; }
    public IReadOnlyDictionary<Guid, ConnectedSocket> UnitySockets => _unitySockets;
    public IReadOnlyDictionary<string, ConnectedSocket> RobotSockets => _robotSockets;

    public CommunicationGateway(JsonSerializerOptions jsonOptions)
    {
        SystemMode = SystemMode.Simulation;
        _jsonOptions = jsonOptions;
    }

    public void RegisterUnitySocket(Guid connectionId, WebSocket socket)
    {
        var client = new ConnectedSocket
        {
            ConnectionId = connectionId,
            WebSocket = socket
        };
        _unitySockets.TryAdd(connectionId, client);
    }

    public void RegisterRobotSocket(string robotId, Guid connectionId, WebSocket socket)
    {
        var client = new ConnectedSocket
        {
            ConnectionId = connectionId,
            WebSocket = socket
        };

        _connectionToRobotId[connectionId] = robotId;

        if (_robotSockets.TryGetValue(robotId, out var oldClient))
        {
            oldClient.Dispose();
        }

        _robotSockets[robotId] = client;
    }

    public void RemoveSocket(Guid connectionId)
    {
        if (_unitySockets.TryRemove(connectionId, out var unitySocket))
        {
            unitySocket.Dispose();
            return;
        }

        if (_connectionToRobotId.TryRemove(connectionId, out var robotId))
        {
            if (_robotSockets.TryRemove(robotId, out var robotSocket))
            {
                robotSocket.Dispose();
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
        if (_unitySockets.IsEmpty) return;

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tasks = _unitySockets.Select(async kv =>
        {
            var id = kv.Key;
            var client = kv.Value;

            if (client.WebSocket.State != WebSocketState.Open)
            {
                RemoveSocket(id);
                return;
            }

            try
            {
                await client.SendTextAsync(bytes, cancellationToken);
            }
            catch (Exception ex)
            {
                Logger.Log($"Error sending message to Unity socket {id}: {ex.Message}");
                RemoveSocket(id);
            }
        });

        await Task.WhenAll(tasks);
    }

    private async Task SendRobot(SocketMessage message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message.RobotId))
            return;

        if (!_robotSockets.TryGetValue(message.RobotId, out var client))
        {
            return;
        }

        if (client.WebSocket.State != WebSocketState.Open)
        {
            RemoveSocket(client.ConnectionId);
            return;
        }

        try
        {
            var json = JsonSerializer.Serialize(message, _jsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);

            await client.SendTextAsync(bytes, cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.Log($"Error sending message to Robot socket {message.RobotId}: {ex.Message}");
            RemoveSocket(client.ConnectionId);
        }
    }
}
