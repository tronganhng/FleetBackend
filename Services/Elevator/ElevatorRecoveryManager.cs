namespace FleetBackend.Services.Elevator;

public sealed class ElevatorRecoveryManager(ElevatorRecoveryStore store, ElevatorAdapterResolver adapters,
    ITrafficManager traffic, ICommunicationGateway gateway)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<ElevatorSnapshot> RecoverAsync(string elevatorId, bool cabinConfirmedEmpty, CancellationToken token)
    {
        if (!cabinConfirmedEmpty) throw new InvalidOperationException("Operator must confirm that the cabin is empty.");
        await _gate.WaitAsync(token);
        try
        {
            if (gateway.SystemMode != FleetBackend.Models.SystemMode.Operation)
                throw new InvalidOperationException("Recovery requires Operation mode.");
            var entry = store.GetAll().SingleOrDefault(item => string.Equals(item.ElevatorId, elevatorId, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("Elevator has no unresolved trip.");
            if (entry.IsActive) throw new InvalidOperationException("Cancel the active task before recovery.");
            // Read only: recovery never closes the door or moves the elevator automatically.
            var status = await adapters.ForMode(FleetBackend.Models.SystemMode.Operation).RefreshAsync(elevatorId, token);
            if (gateway.SystemMode != FleetBackend.Models.SystemMode.Operation)
                throw new InvalidOperationException("Mode changed during recovery; elevator remains blocked.");
            if (status.IsRetained || !status.IsConnected || status.State != "DOOR_CLOSED" || !status.CurrentFloor.HasValue ||
                status.StateReceivedAtUtc != status.ReceivedAtUtc || status.FloorReceivedAtUtc != status.ReceivedAtUtc)
                throw new InvalidOperationException("Recovery requires a fresh full status confirming floor and DOOR_CLOSED.");
            store.Complete(entry.ElevatorId, entry.RobotId);
            traffic.UnprotectAccess(entry.RobotId, $"elevator:{entry.ElevatorId}");
            traffic.ReleaseAccess(entry.RobotId, $"elevator:{entry.ElevatorId}");
            return status;
        }
        finally { _gate.Release(); }
    }
}
