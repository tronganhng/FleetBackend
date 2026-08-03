using FleetBackend.Models;

public class RobotBackToIdleEvent : IEvent
{
    public RobotStateDto Robot { get; }

    public RobotBackToIdleEvent(RobotStateDto robot)
    {
        Robot = robot;
    }
}