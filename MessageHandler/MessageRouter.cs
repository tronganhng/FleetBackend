using FleetBackend.Models;
using System.Text.Json;

public class MessageRouter
{
    private readonly Dictionary<SocketMessageType, IMessageHandler> _handlers;
    private readonly JsonSerializerOptions _jsonOptions;

    public MessageRouter(IEnumerable<IMessageHandler> handlers, JsonSerializerOptions jsonOptions)
    {
        _handlers = handlers.ToDictionary(h => h.MessageType);
        _jsonOptions = jsonOptions;
    }

    public async Task<SocketMessage?> RouteAsync(SocketMessage message)
    {
        if (!_handlers.TryGetValue(message.Type, out var handler))
            return null;

        return await handler.HandleAsync(message, _jsonOptions);
    }
}