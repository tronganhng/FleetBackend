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

    public enum ElevatorStatus
    {
        Idle,
        Moving,
    }

    public enum NodeType
    {
        Room,
        SharedResource,
        WaitingArea,
        Elevator,
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
        StopRobot,
        RobotArrived,
        ChangeRobotStatus,
        ChargeRobot,
        RegisterElevator,
        MoveElevator,
        ElevatorArrived,
    }
}
