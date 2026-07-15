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
            Console.WriteLine("Unity Connected");
            
            _robotManager.ClearAll();

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
                    var socketMessage = JsonSerializer.Deserialize<SocketMessage<RobotStateDto>>(message, _jsonOptions);

                    if (socketMessage?.Type == SocketMessageType.RegisterRobot && socketMessage.Payload is not null)
                    {
                        var robotId = _robotManager.RegisterRobot(socketMessage.Payload);

                        var response = new SocketMessage<RobotStateDto>
                        {
                            Type = SocketMessageType.None,
                            RequestId = socketMessage.RequestId,
                            Payload = new RobotStateDto { RobotId = robotId }
                        };

                        var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                        var responseBytes = Encoding.UTF8.GetBytes(responseJson);

                        await socket.SendAsync(responseBytes, WebSocketMessageType.Text, true, cancellationToken);
                    }
                }
                catch (JsonException ex)
                {
                    Console.WriteLine($"Invalid message: {ex.Message}");
                }
            }
        }
    }
}
