using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FleetBackend.Models;

namespace FleetBackend.Services
{
    public interface ICommunicationGateway
    {
        Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken);
    }

    public class CommunicationGateway : ICommunicationGateway
    {
        private readonly IRobotManager _robotManager;
        private readonly JsonSerializerOptions _jsonOptions;

        public CommunicationGateway(IRobotManager robotManager)
        {
            _robotManager = robotManager;
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };
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

                    switch (socketMessage.Type)
                    {
                        case SocketMessageType.RegisterRobot:
                            RobotStateDto? payloadData = socketMessage.Payload.Deserialize<RobotStateDto>(_jsonOptions);

                            if (payloadData == null) continue;

                            var robotId = _robotManager.RegisterRobot(payloadData);
                            var response = new SocketMessage
                            {
                                Type = SocketMessageType.None,
                                RequestId = socketMessage.RequestId,
                                Payload = JsonSerializer.SerializeToElement(new RobotStateDto { RobotId = robotId }),
                            };

                            await SendAsync(socket, response, cancellationToken);
                            break;
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
}
