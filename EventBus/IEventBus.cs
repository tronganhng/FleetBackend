public interface IEventBus
{
    void Subscribe<T>(Action<T> handler) where T : IEvent;

    void Publish<T>(T @event) where T : IEvent;
}