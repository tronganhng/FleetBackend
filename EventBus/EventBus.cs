using System.Collections.Concurrent;

public class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();

    public void Subscribe<T>(Action<T> handler) where T : IEvent
    {
        var handlers = _handlers.GetOrAdd(typeof(T), _ => new List<Delegate>());

        lock (handlers)
        {
            handlers.Add(handler);
        }
    }

    public void Publish<T>(T @event) where T : IEvent
    {
        if (!_handlers.TryGetValue(typeof(T), out var handlers))
            return;

        Delegate[] subscribers;

        lock (handlers)
        {
            subscribers = handlers.ToArray();
        }

        foreach (var handler in subscribers)
        {
            ((Action<T>)handler).Invoke(@event);
        }
    }
}