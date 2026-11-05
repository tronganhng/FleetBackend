using FleetBackend.Models;
using FleetBackend.Options;
using FleetBackend.Services.Elevator;
using System.Text.Json;

namespace FleetBackend.Services.Task;

// Own the resource throughout boarding, map change, exit and final door close.
public sealed class ElevatorTraversal : IDisposable
{
    private enum Step { None, Approaching, WaitingForAccess, Pickup, OpeningPickup, Entering,
        ClosingPickup, Riding, OpeningDestination, ChangingMap, Exiting, ClosingDestination, Finished }
    private readonly Robot _robot;
    private readonly IMapManager _map;
    private readonly ITrafficManager _traffic;
    private readonly ICommunicationGateway _gateway;
    private readonly IElevatorAdapter _adapter;
    private readonly ElevatorRecoveryStore _recovery;
    private readonly ElevatorMqttOptions _options;
    private readonly JsonSerializerOptions _json;
    private readonly object _gate;
    private readonly Action _completed;
    private readonly Action<string> _failed;
    private readonly MapNodeDto _source;
    private readonly MapNodeDto _destination;
    private readonly string _elevatorId;
    private readonly SystemMode _mode;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly TaskCompletionSource _access = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _arrival;
    private TaskCompletionSource? _floorReady;
    private string? _floorRequestId;
    private Step _step;
    private bool _ownsAccess;
    private bool _mayBeInCabin;
    private bool _physicalLease;
    private bool _successful;
    private string _failureReason = "Traversal interrupted; reconcile elevator and robot before reuse.";
    private string ResourceId => $"elevator:{_elevatorId}";
    public bool RequiresRecovery => _ownsAccess && (_mayBeInCabin || _physicalLease);

    public ElevatorTraversal(Robot robot, IMapManager map, ITrafficManager traffic,
        ICommunicationGateway gateway, IElevatorAdapter adapter, ElevatorRecoveryStore recovery,
        ElevatorMqttOptions options, JsonSerializerOptions json, object gate,
        MapNodeDto source, MapNodeDto destination, string elevatorId, Action completed, Action<string> failed)
    {
        _robot = robot; _map = map; _traffic = traffic; _gateway = gateway;
        _adapter = adapter; _recovery = recovery; _options = options; _json = json; _gate = gate;
        _source = source; _destination = destination; _elevatorId = elevatorId;
        _completed = completed; _failed = failed; _mode = gateway.SystemMode;
    }

    public void Start()
    {
        _traffic.AccessGranted += OnAccessGranted;
        _adapter.Disconnected += OnDisconnected;
        _ = RunAsync();
    }

    public void OnRobotArrived()
    {
        lock (_gate)
            if (_step is Step.Approaching or Step.Entering or Step.Exiting) _arrival?.TrySetResult();
    }

    public void OnRobotFloorReady(string requestId, RobotFloorReadyPayload payload)
    {
        lock (_gate)
        {
            if (_step != Step.ChangingMap || requestId != _floorRequestId ||
                !string.Equals(payload.ElevatorId, _elevatorId, StringComparison.OrdinalIgnoreCase) ||
                payload.TargetFloor != _destination.Floor) return;
            if (payload.IsReady) _floorReady?.TrySetResult();
            else _floorReady?.TrySetException(new InvalidOperationException(payload.ErrorMessage ?? "Robot could not change floor/map."));
        }
    }

    private async System.Threading.Tasks.Task RunAsync()
    {
        var token = _lifetime.Token;
        try
        {
            await MoveRobotAsync(Step.Approaching, _source.Position, null, token);
            lock (_gate)
            {
                EnsureActive();
                _robot.CurrentNode = _source;
                _step = Step.WaitingForAccess;
                if (_traffic.RequestAccess(_robot.State.RobotId, ResourceId)) _access.TrySetResult();
            }
            await _access.Task.WaitAsync(TimeSpan.FromSeconds(_options.AccessTimeoutSeconds), token);
            lock (_gate)
            {
                EnsureActive();
                _ownsAccess = true;
                _traffic.ProtectAccess(_robot.State.RobotId, ResourceId, _adapter.RequiresDoorControl);
                if (_adapter.RequiresDoorControl && _recovery.IsBlocked(_elevatorId))
                    throw new InvalidOperationException("Elevator requires recovery before another trip.");
                _step = Step.Pickup;
            }

            if (_adapter.RequiresDoorControl)
            {
                var status = await _adapter.RefreshAsync(_elevatorId, token);
                lock (_gate)
                {
                    EnsureActive();
                    if (!status.IsConnected || status.IsRetained || !status.CurrentFloor.HasValue ||
                        status.FloorReceivedAtUtc != status.ReceivedAtUtc || status.StateReceivedAtUtc != status.ReceivedAtUtc ||
                        status.State is not ("ARRIVED" or "DOOR_OPEN" or "DOOR_CLOSED"))
                        throw new InvalidOperationException("Cannot establish a fresh full elevator state before pickup.");
                    _physicalLease = true;
                    _recovery.Begin(_elevatorId, _robot.State.RobotId);
                }
                if (status.CurrentFloor != _source.Floor)
                    await _adapter.CallAsync(_elevatorId, _source.Floor, TimeSpan.FromSeconds(_options.PickupTimeoutSeconds), token);
                SetStep(Step.OpeningPickup);
                await _adapter.OpenDoorAsync(_elevatorId, _source.Floor, token);
            }
            else
                await _adapter.CallAsync(_elevatorId, _source.Floor, TimeSpan.FromSeconds(_options.PickupTimeoutSeconds), token);

            MapPointDto dock;
            lock (_gate)
            {
                EnsureActive();
                dock = _map.Dock.AcquireDock(_source.NodeName)
                    ?? throw new InvalidOperationException("Elevator cabin point is unavailable.");
                _mayBeInCabin = true;
            }
            await MoveRobotAsync(Step.Entering, dock.Position, dock, token);
            SetStep(Step.ClosingPickup);
            await _adapter.CloseDoorAsync(_elevatorId, _source.Floor, token);
            SetStep(Step.Riding);
            await _adapter.CallAsync(_elevatorId, _destination.Floor, TimeSpan.FromSeconds(_options.TravelTimeoutSeconds), token);
            SetStep(Step.OpeningDestination);
            await _adapter.OpenDoorAsync(_elevatorId, _destination.Floor, token);

            if (_adapter.RequiresDoorControl) await ChangeRobotFloorAsync(token);
            lock (_gate) { EnsureActive(); _robot.State.Floor = _destination.Floor; }
            await MoveRobotAsync(Step.Exiting, _destination.Position, null, token);
            lock (_gate)
            {
                EnsureActive();
                _robot.CurrentNode = _destination;
                _mayBeInCabin = false;
            }
            SetStep(Step.ClosingDestination);
            await _adapter.CloseDoorAsync(_elevatorId, _destination.Floor, token);
            lock (_gate)
            {
                EnsureActive();
                if (_physicalLease) _recovery.Complete(_elevatorId, _robot.State.RobotId);
                _physicalLease = false;
                _successful = true;
                Dispose();
                _completed();
            }
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (_step == Step.Finished) return;
                _failureReason = $"Elevator traversal failed at {_step}: {ex.Message}";
                Dispose();
                _failed(_failureReason);
            }
        }
        finally { _lifetime.Dispose(); }
    }

    private async System.Threading.Tasks.Task MoveRobotAsync(Step step, float[] position, MapPointDto? dock, CancellationToken token)
    {
        TaskCompletionSource arrival;
        System.Threading.Tasks.Task send;
        lock (_gate)
        {
            EnsureActive();
            _step = step;
            arrival = _arrival = new(TaskCreationOptions.RunContinuationsAsynchronously);
            send = _robot.MoveToAsync(position, token);
            if (dock != null) { _robot.CurrentNode = _source; _robot.CurrentDock = dock; }
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.RobotMovementTimeoutSeconds));
        await send.WaitAsync(deadline.Token);
        await arrival.Task.WaitAsync(deadline.Token);
        lock (_gate) { if (ReferenceEquals(_arrival, arrival)) _arrival = null; }
    }

    private async System.Threading.Tasks.Task ChangeRobotFloorAsync(CancellationToken token)
    {
        SocketMessage message;
        TaskCompletionSource ready;
        lock (_gate)
        {
            EnsureActive();
            _step = Step.ChangingMap;
            _floorRequestId = Guid.NewGuid().ToString("N");
            ready = _floorReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            message = new SocketMessage
            {
                Type = SocketMessageType.ChangeRobotFloor, RobotId = _robot.State.RobotId, RequestId = _floorRequestId,
                Payload = JsonSerializer.SerializeToElement(new ChangeRobotFloorRequest
                {
                    ElevatorId = _elevatorId, TargetFloor = _destination.Floor
                }, _json)
            };
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.RobotFloorReadyTimeoutSeconds));
        await _gateway.SendCommandAsync(message, deadline.Token);
        await ready.Task.WaitAsync(deadline.Token);
    }

    private void OnAccessGranted(string robotId, string resourceId)
    {
        lock (_gate)
            if (_step == Step.WaitingForAccess && robotId == _robot.State.RobotId && resourceId == ResourceId)
                _access.TrySetResult();
    }

    private void OnDisconnected(string elevatorId)
    {
        lock (_gate)
        {
            if (_step == Step.Finished || !_ownsAccess || !_adapter.RequiresDoorControl ||
                !string.Equals(elevatorId, _elevatorId, StringComparison.OrdinalIgnoreCase)) return;
            _failureReason = "Elevator transport disconnected during traversal; physical outcome is unknown.";
            Dispose();
            _failed(_failureReason);
        }
    }

    private void SetStep(Step step) { lock (_gate) { EnsureActive(); _step = step; } }
    private void EnsureActive()
    {
        _lifetime.Token.ThrowIfCancellationRequested();
        if (_step == Step.Finished || _gateway.SystemMode != _mode)
            throw new OperationCanceledException("Traversal mode/session changed.");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_step == Step.Finished) return;
            _step = Step.Finished;
            _lifetime.Cancel();
            _traffic.AccessGranted -= OnAccessGranted;
            _adapter.Disconnected -= OnDisconnected;
            if (_physicalLease)
            {
                try { _recovery.RequireRecovery(_elevatorId, _robot.State.RobotId, _failureReason); }
                catch (Exception ex) { Logger.Log($"Elevator recovery journal could not be updated: {ex.Message}"); }
            }
            if (_ownsAccess && (_successful || !RequiresRecovery))
                _traffic.UnprotectAccess(_robot.State.RobotId, ResourceId);
            _traffic.CancelRequest(_robot.State.RobotId, ResourceId);
        }
    }
}
