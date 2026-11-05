namespace FleetBackend.Services.Elevator;

// Cache and one pending command are separate. Callers serialize command execution.
public sealed class ElevatorStatusMonitor(string elevatorId)
{
    private readonly object _gate = new();
    private ElevatorSnapshot _snapshot = new() { ElevatorId = elevatorId };
    private TaskCompletionSource<ElevatorSnapshot>? _pending;
    private Func<int?, string?, bool>? _matches;

    public ElevatorSnapshot Snapshot { get { lock (_gate) return _snapshot; } }

    public void SetConnected(bool connected)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with { IsConnected = connected };
            if (!connected)
                _pending?.TrySetException(new IOException("Elevator transport disconnected; command outcome is unknown."));
        }
    }

    public Task<ElevatorSnapshot> Begin(string command, int? floor, TimeSpan timeout,
        Func<int?, string?, bool> matches)
    {
        lock (_gate)
        {
            if (!_snapshot.IsConnected) throw new IOException("Elevator transport is not ready.");
            if (_pending != null) throw new InvalidOperationException("An elevator command is already pending.");
            var now = DateTimeOffset.UtcNow;
            _snapshot = _snapshot with
            {
                LastCommand = new(command, floor, now, now + timeout, ElevatorCommandOutcome.Pending)
            };
            _matches = matches;
            _pending = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _pending.Task;
        }
    }

    public void Receive(int? floor, string? state, string raw, string topic, bool retained)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            _snapshot = _snapshot with
            {
                CurrentFloor = floor ?? _snapshot.CurrentFloor,
                State = state ?? _snapshot.State,
                FloorReceivedAtUtc = floor.HasValue ? now : _snapshot.FloorReceivedAtUtc,
                StateReceivedAtUtc = state != null ? now : _snapshot.StateReceivedAtUtc,
                ReceivedAtUtc = now, RawPayload = raw, Topic = topic, IsRetained = retained
            };
            // Match this message, never merged cache. Retained data is display-only.
            if (_snapshot.IsConnected && !retained && _pending != null && _matches!(floor, state))
                _pending.TrySetResult(_snapshot);
        }
    }

    public void End(ElevatorCommandOutcome outcome)
    {
        lock (_gate)
        {
            if (_snapshot.LastCommand != null)
                _snapshot = _snapshot with { LastCommand = _snapshot.LastCommand with { Outcome = outcome } };
            // Publish can fail before its waiter is awaited. Observe a concurrent disconnect failure.
            if (_pending?.Task.IsFaulted == true) _ = _pending.Task.Exception;
            _pending = null;
            _matches = null;
        }
    }
}
