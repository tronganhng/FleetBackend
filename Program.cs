using System.Net.WebSockets;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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

    Console.WriteLine("Unity Connected");

    var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

    while (await timer.WaitForNextTickAsync())
    {
        var robot = new
        {
            id = "Robot_01",
            x = Random.Shared.Next(0, 20),
            y = 0,
            z = Random.Shared.Next(0, 20),
            name = "LATGOTO"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(robot);

        var bytes = Encoding.UTF8.GetBytes(json);

        await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
    }
});

app.Run();