namespace FleetBackend.Models;

// The robot echoes the original SocketMessage.RequestId and RobotId.
public sealed record RobotFloorReadyPayload
{
    public required string ElevatorId { get; init; }
    public required int TargetFloor { get; init; }
    public required bool IsReady { get; init; }
    public string? ErrorMessage { get; init; }
}
