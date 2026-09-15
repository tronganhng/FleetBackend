using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using FleetBackend.Models;
using System.Threading.Tasks;

public class ConnectedSocket : IDisposable
{
    public Guid ConnectionId { get; init; }
    public string RobotId { get; set; } = string.Empty;
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

    void RegisterUnitySocket(Guid connectionId, WebSocket socket);
    void RegisterRobotSocket(Guid connectionId, WebSocket socket);
    void RemoveSocket(Guid connectionId);

    void SetRobotIdToSocket(string robotId, Guid connectionId);
    Task ResetRobotIdSockets();

    Task SendCommandAsync(SocketMessage message, CancellationToken cancellationToken = default);
    Task SendDashboardAsync(SocketMessage message, CancellationToken cancellationToken = default);
}

public class CommunicationGateway : ICommunicationGateway
{
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ConcurrentDictionary<Guid, ConnectedSocket> _unitySockets = new();
    private readonly List<ConnectedSocket> _robotSockets = new();

    public SystemMode SystemMode { get; set; }

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

    public void RegisterRobotSocket(Guid connectionId, WebSocket socket)
    {
        var client = new ConnectedSocket
        {
            ConnectionId = connectionId,
            WebSocket = socket
        };
        _robotSockets.Add(client);
    }

    public void RemoveSocket(Guid connectionId)
    {
        if (_unitySockets.TryRemove(connectionId, out var unitySocket))
        {
            unitySocket.Dispose();
            return;
        }

        var client = _robotSockets.FirstOrDefault(c => c.ConnectionId == connectionId);
        if (client != null)
        {
            client.Dispose();
            _robotSockets.Remove(client);
        }
    }

    public void SetRobotIdToSocket(string robotId, Guid connectionId)
    {
        var client = _robotSockets.FirstOrDefault(c => c.ConnectionId == connectionId);
        if (client != null)
        {
            client.RobotId = robotId;
        }
    }

    public async Task ResetRobotIdSockets()
    {
        foreach (var item in _robotSockets)
        {
            item.RobotId = string.Empty;
        }

        if (SystemMode == SystemMode.Operation)
        {
            var message = new SocketMessage
            {
                Type = SocketMessageType.SetSystemMode,
                RequestId = null,
                Payload = JsonSerializer.SerializeToElement(true, _jsonOptions)
            };
            await BroadCastAllRobot(message, CancellationToken.None);
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


        var client = _robotSockets.FirstOrDefault(c => c.RobotId == message.RobotId);
        if (client == null)
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

    private async Task BroadCastAllRobot(SocketMessage message, CancellationToken cancellationToken)
    {
        if (_robotSockets.Count == 0) return;

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tasks = _robotSockets
            .Select(client => Task.Run(async () =>
            {
                if (client.WebSocket.State != WebSocketState.Open)
                {
                    RemoveSocket(client.ConnectionId);
                    return;
                }

                try
                {
                    await client.SendTextAsync(bytes, cancellationToken);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Error broadcasting message to robot socket {client.ConnectionId}: {ex.Message}");
                    RemoveSocket(client.ConnectionId);
                }
            }, cancellationToken));

        await Task.WhenAll(tasks);
    }
}
