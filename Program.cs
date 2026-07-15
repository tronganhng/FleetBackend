using FleetBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IRobotManager, RobotManager>();
builder.Services.AddSingleton<ICommunicationGateway, CommunicationGateway>();

var app = builder.Build();

// Initialize Services
var robotManager = app.Services.GetRequiredService<IRobotManager>();
robotManager.Init();

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