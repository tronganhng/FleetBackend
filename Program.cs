using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IRobotManager, RobotManager>();

var app = builder.Build();

app.UseWebSockets();

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var robotManager = context.RequestServices.GetRequiredService<IRobotManager>();

    Console.WriteLine("Unity Connected");

    var buffer = new byte[4096];

    while (true)
    {
        var result = await socket.ReceiveAsync(buffer, CancellationToken.None);

        if (result.MessageType == WebSocketMessageType.Close)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed", CancellationToken.None);
            break;
        }

        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
        Console.WriteLine($"Receive: {message}");

        try
        {
            var socketMessage = JsonSerializer.Deserialize<SocketMessage<RobotStateDto>>(message, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (socketMessage?.Type == SocketMessageType.RegisterRobot.ToString() && socketMessage.Payload is not null)
            {
                var robotId = robotManager.RegisterRobot(socketMessage.Payload);

                var response = new SocketMessage<RegisterRobotResponse>
                {
                    Type = "_",
                    RequestId = socketMessage.RequestId,
                    Payload = new RegisterRobotResponse { RobotId = robotId }
                };

                var responseJson = JsonSerializer.Serialize(response);
                var responseBytes = Encoding.UTF8.GetBytes(responseJson);

                await socket.SendAsync(responseBytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Invalid message: {ex.Message}");
        }
    }
});

app.Run();