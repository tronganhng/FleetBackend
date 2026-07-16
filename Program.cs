using FleetBackend.Extensions;
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

services.AddFleetCore()
        .AddCommunication()
        .AddMessageHandlers();

var app = builder.Build();

// Ensure Scheduler is instantiated so it can subscribe to TaskCreatedEvent.
app.Services.GetRequiredService<IScheduler>();

app.UseWebSockets();

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var socket = await context.WebSockets.AcceptWebSocketAsync();
    var gateway = context.RequestServices.GetRequiredService<ICommunicationGateway>();

    Console.WriteLine("Unity Connected");

    await gateway.HandleClientAsync(socket, context.RequestAborted);
});

app.Run();