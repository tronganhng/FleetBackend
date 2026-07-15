using FleetBackend.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IRobotManager, RobotManager>();
builder.Services.AddSingleton<ICommunicationGateway, CommunicationGateway>();

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

    await gateway.HandleClientAsync(socket, context.RequestAborted);
});

app.Run();