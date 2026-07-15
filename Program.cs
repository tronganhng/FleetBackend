using FleetBackend.Services;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;

services.AddSingleton(new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    Converters = { new JsonStringEnumConverter() }
});

services.AddSingleton<IRobotManager, RobotManager>();
services.AddSingleton<ICommunicationGateway, CommunicationGateway>();

services.AddSingleton<IMessageHandler, RegisterRobotHandler>();
services.AddSingleton<MessageRouter>();

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
    var gateway = context.RequestServices.GetRequiredService<ICommunicationGateway>();

    Console.WriteLine("Unity Connected");

    await gateway.HandleClientAsync(socket, context.RequestAborted);
});

app.Run();