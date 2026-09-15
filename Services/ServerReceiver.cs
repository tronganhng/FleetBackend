using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FleetBackend.Models;

public interface IServerReceiver
{
    Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken);
}

public class ServerReceiver : IServerReceiver
{
    private readonly MessageRouter _messageRouter;
    private readonly ICommunicationGateway _gateway;
    private readonly JsonSerializerOptions _jsonOptions;

    public ServerReceiver(MessageRouter messageRouter, ICommunicationGateway gateway, JsonSerializerOptions jsonOptions)
    {
        _messageRouter = messageRouter;
        _gateway = gateway;
        _jsonOptions = jsonOptions;
    }

    public async Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];
        var connectionId = Guid.NewGuid();
        ClientType? registeredClientType = null;

        try
        {
            while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;

                do
                {
                    try
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    }
                    catch (WebSocketException ex)
                    {
                        Logger.Log($"WebSocket closed on connection {connectionId}: {ex.Message}");
                        return;
                    }

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                        {
                            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", cancellationToken);
                        }
                        return;
                    }

                    ms.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(ms, Encoding.UTF8);
                var message = await reader.ReadToEndAsync(cancellationToken);

                try
                {
                    var socketMessage = JsonSerializer.Deserialize<SocketMessage>(message, _jsonOptions);

                    if (socketMessage is null) continue;

                    if (socketMessage.Type == SocketMessageType.RegisterClient)
                    {
                        registeredClientType = ClientRegisterHandle(socketMessage, socket, connectionId);
                        continue;
                    }

                    if (registeredClientType == ClientType.Robot && _gateway.SystemMode == SystemMode.Simulation)
                    {
                        Logger.Log($"Blocked '{socketMessage.Type}' from Robot because system is in Simulation.");
                        continue;
                    }

                    var response = await _messageRouter.RouteAsync(socketMessage);

                    if (response != null)
                    {
                        await SendAsync(socket, response, cancellationToken);
                    }
                }
                catch (JsonException ex)
                {
                    Logger.Log($"Invalid JSON message on connection {connectionId}: {ex.Message}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Host is shutting down cleanly
        }
        finally
        {
            _gateway.RemoveSocket(connectionId);
        }
    }

    private async Task SendAsync(WebSocket socket, SocketMessage message, CancellationToken token)
    {
        if (socket.State != WebSocketState.Open) return;

        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
    }

    private ClientType? ClientRegisterHandle(SocketMessage socketMessage, WebSocket socket, Guid connectionId)
    {
        try
        {
            var clientType = socketMessage.Payload.Deserialize<ClientType>(_jsonOptions);

            switch (clientType)
            {
                case ClientType.Unity:
                    _gateway.RegisterUnitySocket(connectionId, socket);
                    break;
                case ClientType.Robot:
                    if (!string.IsNullOrWhiteSpace(socketMessage.RobotId))
                    {
                        _gateway.RegisterRobotSocket(socketMessage.RobotId, connectionId, socket);
                    }
                    break;
            }

            return clientType;
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to register client on connection {connectionId}: {ex.Message}");
            return null;
        }
    }
}
