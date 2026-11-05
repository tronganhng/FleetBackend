namespace FleetBackend.Models;

public sealed record RecoverElevatorRequest
{
    public required string ElevatorId { get; init; }
    public required bool CabinConfirmedEmpty { get; init; }
}
