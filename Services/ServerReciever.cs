using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FleetBackend.Models;

public interface IServerReciever
{
    Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken);
}

public class ServerReciever : IServerReciever
{
    private readonly MessageRouter _messageRouter;
    private readonly ICommunicationGateway _gateway;
    private readonly JsonSerializerOptions _jsonOptions;

    public ServerReciever(MessageRouter messageRouter, ICommunicationGateway gateway, JsonSerializerOptions jsonOptions)
    {
        _messageRouter = messageRouter;
        _gateway = gateway;
        _jsonOptions = jsonOptions;
    }

    public async Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        var connectionId = Guid.NewGuid();

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
                    Logger.Log($"WebSocket closed: {ex.Message}");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", cancellationToken);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                // Logger.Log($"Receive: {message}");

                try
                {
                    var socketMessage = JsonSerializer.Deserialize<SocketMessage>(message, _jsonOptions);

                    if (socketMessage is null) continue;

                    if (socketMessage.Type == SocketMessageType.RegisterClient)
                    {
                        ClientRegisterHandle(socketMessage, socket, connectionId);
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
                    Logger.Log($"Invalid message: {ex.Message}");
                }
            }
        }
        finally
        {
            _gateway.RemoveSocket(connectionId);
        }
    }

    private async Task SendAsync(WebSocket socket, SocketMessage message, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }

    private void ClientRegisterHandle(SocketMessage socketMessage, WebSocket socket, Guid connectionId)
    {
        var clientType = socketMessage.Payload.Deserialize<ClientType>(_jsonOptions);

        var client = new ConnectedSocket
        {
            ConnectionId = connectionId,
            WebSocket = socket,
        };

        switch (clientType)
        {
            case ClientType.Unity:
                _gateway.UnitySockets.TryAdd(connectionId, client);
                break;
            case ClientType.Robot:
                if (socketMessage.RobotId != null)
                    _gateway.RobotSockets.TryAdd(socketMessage.RobotId, client);
                break;
        }
    }
}