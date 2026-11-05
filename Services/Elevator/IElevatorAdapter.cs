using FleetBackend.Models;

namespace FleetBackend.Services.Elevator;

public enum ElevatorCommandOutcome { Pending, Completed, TimedOut, Cancelled, Failed }

public sealed record ElevatorCommandProgress(string Command, int? TargetFloor,
    DateTimeOffset StartedAtUtc, DateTimeOffset DeadlineUtc, ElevatorCommandOutcome Outcome);

public sealed record ElevatorSnapshot
{
    public required string ElevatorId { get; init; }
    // Connected means transport/subscription ready, not proof that the controller is alive.
    public bool IsConnected { get; init; }
    public int? CurrentFloor { get; init; }
    public string? State { get; init; }
    public DateTimeOffset? FloorReceivedAtUtc { get; init; }
    public DateTimeOffset? StateReceivedAtUtc { get; init; }
    public DateTimeOffset? ReceivedAtUtc { get; init; }
    public string? RawPayload { get; init; }
    public string? Topic { get; init; }
    public bool IsRetained { get; init; }
    public ElevatorCommandProgress? LastCommand { get; init; }
}

public interface IElevatorAdapter
{
    bool RequiresDoorControl { get; }
    event Action<string>? Disconnected;
    ElevatorSnapshot GetSnapshot(string elevatorId);
    Task<ElevatorSnapshot> RefreshAsync(string elevatorId, CancellationToken cancellationToken = default);
    Task<ElevatorSnapshot> CallAsync(string elevatorId, int floor, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<ElevatorSnapshot> OpenDoorAsync(string elevatorId, int floor, CancellationToken cancellationToken = default);
    Task<ElevatorSnapshot> CloseDoorAsync(string elevatorId, int floor, CancellationToken cancellationToken = default);
}

// Resolve once at traversal start; a trip must not silently switch transport mid-flight.
public sealed class ElevatorAdapterResolver(MqttElevatorAdapter mqtt, SimulationElevatorAdapter simulation)
{
    public IElevatorAdapter ForMode(SystemMode mode) => mode switch
    {
        SystemMode.Operation => mqtt,
        SystemMode.Simulation => simulation,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
