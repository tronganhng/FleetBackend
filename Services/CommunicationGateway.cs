using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Collections.Concurrent;
using System.Collections.Generic;
using FleetBackend.Services;
using FleetBackend.Models;

public interface ICommunicationGateway
{
    Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken);
    Task BroadcastAsync(SocketMessage message, CancellationToken cancellationToken = default);
}

public class CommunicationGateway : ICommunicationGateway
{
    private readonly IRobotManager _robotManager;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly MessageRouter _messageRouter;
    private readonly ConcurrentDictionary<Guid, WebSocket> _sockets = new();

    public CommunicationGateway(IRobotManager robotManager, MessageRouter messageRouter, JsonSerializerOptions jsonOptions)
    {
        _robotManager = robotManager;
        _jsonOptions = jsonOptions;
        _messageRouter = messageRouter;
    }

    public async Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var connectionId = Guid.NewGuid();
        _sockets.TryAdd(connectionId, socket);

        var buffer = new byte[4096];

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                WebSocketReceiveResult result;

                try
                {
                    result = await socket.ReceiveAsync(buffer, cancellationToken);
                }
                catch (WebSocketException ex)
                {
                    Console.WriteLine($"WebSocket closed: {ex.Message}");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", cancellationToken);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                Console.WriteLine($"Receive: {message}");

                try
                {
                    var socketMessage = JsonSerializer.Deserialize<SocketMessage>(message, _jsonOptions);

                    if (socketMessage is null) continue;

                    var response = await _messageRouter.RouteAsync(socketMessage);

                    if (response != null)
                    {
                        await SendAsync(socket, response, cancellationToken);
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Invalid message: {ex.Message}");
                }
            }
        }
        finally
        {
            _sockets.TryRemove(connectionId, out _);
        }
    }

    private async Task SendAsync(WebSocket socket, SocketMessage message, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
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
