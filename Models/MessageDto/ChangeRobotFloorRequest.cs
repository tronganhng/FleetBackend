namespace FleetBackend.Models;

// SocketMessage.RobotId identifies the backend robot; RequestId identifies this operation.
public sealed record ChangeRobotFloorRequest
{
    public required string ElevatorId { get; init; }
    public required int TargetFloor { get; init; }
}
