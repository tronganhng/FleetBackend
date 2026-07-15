using FleetBackend.Models;

public class MessageRouter
{
    private readonly Dictionary<SocketMessageType, IMessageHandler> _handlers;

    public MessageRouter(IEnumerable<IMessageHandler> handlers)
    {
        _handlers = handlers.ToDictionary(h => h.MessageType);
    }

    public async Task<SocketMessage?> RouteAsync(SocketMessage message)
    {
        if (!_handlers.TryGetValue(message.Type, out var handler))
            return null;

        return await handler.HandleAsync(message);
    }
}