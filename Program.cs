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
        Console.WriteLine($"Recieve: {message}");
    }
});

app.Run();