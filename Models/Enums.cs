namespace FleetBackend.Models
{
    public enum RobotStatus
    {
        Idle,
        DoingTask,
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

    public enum NodeType
    {
        Room,
        SharedResource,
        WaitingArea,
    }

    public enum SocketMessageType
    {
        ServerResponse,
        RegisterRobot,
        RobotState,
        CreateTask,
        UpdateTask,
        CancelTask,
        ResourceAccess,
        ResourceRelease,
        CheckNodeFull,
        MoveRobot,
        RobotArrived,
        ChangeRobotStatus,
    }
}
