namespace FleetBackend.Models
{
    public enum RobotStatus
    {
        Idle,
        Moving,
        Charging,
        Error,
        Offline
    }

    public enum TaskStatus
    {
        Pending,
        Assigned,
        Running,
        Completed,
        Failed,
        Cancelled
    }

    public enum SocketMessageType
    {
        None,
        RegisterRobot,
        RobotState,
        CreateTask,
    }
}
