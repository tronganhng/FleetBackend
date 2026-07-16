using FleetBackend.Models;

public class TaskCreatedEvent : IEvent
{
    public DeliveryTask Task { get; }

    public TaskCreatedEvent(DeliveryTask task)
    {
        Task = task;
    }
}