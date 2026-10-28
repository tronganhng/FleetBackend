namespace FleetBackend.Models;

public class MoveElevatorRequest
{
    public string ElevatorId { get; set; } = string.Empty;
    public int TargetFloor { get; set; } = 1;
}