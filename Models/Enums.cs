namespace FleetBackend.Models
{
    public enum SystemMode
    {
        Operation,
        Simulation
    }

    public enum ClientType
    {
        Unity,
        Robot
    }

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
        ChargingArea,
    }

    public enum SocketMessageType
    {
        RegisterClient,
        SetSystemMode,
        ServerResponse,
        RegisterRobot,
        RobotState,
        CreateTask,
        UpdateTask,
        CancelTask,
        ResourceAccess,
        ResourceRelease,
        MoveRobot,
        RobotArrived,
        ChangeRobotStatus,
    }
}
