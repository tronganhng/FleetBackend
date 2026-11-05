using System.Collections.Concurrent;
using FleetBackend.Options;
using Microsoft.Extensions.Options;

namespace FleetBackend.Services.Elevator;

public abstract class ElevatorAdapterBase(IOptions<ElevatorMqttOptions> options) : IElevatorAdapter
{
    private readonly ConcurrentDictionary<string, ElevatorStatusMonitor> _monitors = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _commands = new(1, 1);
    protected ElevatorMqttOptions Options { get; } = options.Value;
    public abstract bool RequiresDoorControl { get; }
    public event Action<string>? Disconnected;
    protected void SetConnected(string id, bool connected)
    {
        var wasConnected = Monitor(id).Snapshot.IsConnected;
        Monitor(id).SetConnected(connected);
        if (wasConnected && !connected) Disconnected?.Invoke(id);
    }
    protected ElevatorStatusMonitor Monitor(string id) => _monitors.GetOrAdd(id, key => new(key));
    protected virtual void ValidateId(string id) => ArgumentException.ThrowIfNullOrWhiteSpace(id);
    protected abstract global::System.Threading.Tasks.Task PublishAsync(string elevatorId, string command, int? floor, CancellationToken token);

    public ElevatorSnapshot GetSnapshot(string elevatorId)
    {
        ValidateId(elevatorId);
        return Monitor(elevatorId).Snapshot;
    }

    public Task<ElevatorSnapshot> RefreshAsync(string elevatorId, CancellationToken cancellationToken = default) =>
        ExecuteAsync(elevatorId, "GET_STATUS", null, TimeSpan.FromSeconds(Options.StatusTimeoutSeconds),
            (floor, state) => floor.HasValue || state != null, cancellationToken);

    public Task<ElevatorSnapshot> CallAsync(string elevatorId, int floor, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        ExecuteAsync(elevatorId, "CALL_ELEVATOR", floor, timeout,
            (receivedFloor, state) => receivedFloor == floor && state == "ARRIVED", cancellationToken);

    public Task<ElevatorSnapshot> OpenDoorAsync(string elevatorId, int floor, CancellationToken cancellationToken = default) =>
        DoorAsync(elevatorId, floor, true, cancellationToken);

    public Task<ElevatorSnapshot> CloseDoorAsync(string elevatorId, int floor, CancellationToken cancellationToken = default) =>
        DoorAsync(elevatorId, floor, false, cancellationToken);

    private Task<ElevatorSnapshot> DoorAsync(string id, int floor, bool open, CancellationToken token)
    {
        if (!RequiresDoorControl)
        {
            token.ThrowIfCancellationRequested();
            return global::System.Threading.Tasks.Task.FromResult(GetSnapshot(id));
        }
        // Require floor in the same status as door confirmation; never infer it from stale cache.
        return ExecuteAsync(id, open ? "OPEN_DOOR" : "CLOSE_DOOR", floor,
            TimeSpan.FromSeconds(Options.DoorTimeoutSeconds),
            (receivedFloor, state) => receivedFloor == floor && state == (open ? "DOOR_OPEN" : "DOOR_CLOSED"), token);
    }

    private async Task<ElevatorSnapshot> ExecuteAsync(string id, string command, int? floor, TimeSpan timeout,
        Func<int?, string?, bool> matches, CancellationToken token)
    {
        ValidateId(id);
        if (RequiresDoorControl && floor.HasValue && floor is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(floor), "Supported physical elevator floors are 1 and 2.");
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        await _commands.WaitAsync(token);
        var monitor = Monitor(id);
        var begun = false;
        try
        {
            token.ThrowIfCancellationRequested();
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(timeout);
            var result = monitor.Begin(command, floor, timeout, matches);
            begun = true;
            // Start the waiter before publish, including for synchronous/very fast responses.
            await PublishAsync(id, command, floor, deadline.Token);
            await result.WaitAsync(deadline.Token);
            monitor.End(ElevatorCommandOutcome.Completed);
            return monitor.Snapshot;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            if (begun) monitor.End(ElevatorCommandOutcome.TimedOut);
            throw new TimeoutException($"Elevator {id}: {command} timed out; physical outcome is unknown.");
        }
        catch (OperationCanceledException)
        {
            if (begun) monitor.End(ElevatorCommandOutcome.Cancelled);
            throw;
        }
        catch
        {
            if (begun) monitor.End(ElevatorCommandOutcome.Failed);
            throw;
        }
        finally { _commands.Release(); }
    }
}
