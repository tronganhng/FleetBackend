using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FleetBackend.Services;

public interface ICommunicationGateway
{
    Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken);
}

public class CommunicationGateway : ICommunicationGateway
{
    private readonly IRobotManager _robotManager;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly MessageRouter _messageRouter;

    public CommunicationGateway(IRobotManager robotManager, MessageRouter messageRouter, JsonSerializerOptions jsonOptions)
    {
        _robotManager = robotManager;
        _jsonOptions = jsonOptions;
        _messageRouter = messageRouter;
    }

    public async Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

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

    private async Task SendAsync(WebSocket socket, SocketMessage message, CancellationToken token)
    {
        var json = JsonSerializer.Serialize(message, _jsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    }
}
