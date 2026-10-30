using FleetBackend.Models;
using System.Text.Json;

namespace FleetBackend.Services.Task;

// One traversal owns the shared elevator until the robot confirms it has exited.
public sealed class ElevatorTraversal : IDisposable
{
    private enum Step { None, Approaching, WaitingForAccess, WaitingForPickup, Entering, Riding, Exiting, Finished }
    private readonly Robot _robot;
    private readonly IMapManager _map;
    private readonly IElevatorManager _elevators;
    private readonly ITrafficManager _traffic;
    private readonly ICommunicationGateway _gateway;
    private readonly JsonSerializerOptions _json;
    private readonly object _gate;
    private readonly Action _completed;
    private readonly Action<string> _failed;
    private readonly MapNodeDto _source;
    private readonly MapNodeDto _destination;
    private readonly string _elevatorId;
    private string ResourceId => $"elevator:{_elevatorId}";
    private Step _step;
    private bool _ownsAccess;
    private bool _mayBeInCabin;
    public bool RequiresRecovery => _mayBeInCabin;
    private CancellationTokenSource? _timeout;

    public ElevatorTraversal(Robot robot, IMapManager map, IElevatorManager elevators,
        ITrafficManager traffic, ICommunicationGateway gateway, JsonSerializerOptions json,
        object gate, MapNodeDto source, MapNodeDto destination, string elevatorId,
        Action completed, Action<string> failed)
    {
        _robot = robot;
        _map = map;
        _elevators = elevators;
        _traffic = traffic;
        _gateway = gateway;
        _json = json;
        _gate = gate;
        _source = source;
        _destination = destination;
        _elevatorId = elevatorId;
        _completed = completed;
        _failed = failed;
    }

    public void Start()
    {
        _traffic.AccessGranted += OnAccessGranted;
        SetStep(Step.Approaching);
        _robot.MoveTo(_source.Position);
    }

    public void OnRobotArrived()
    {
        switch (_step)
        {
            case Step.Approaching:
                _robot.CurrentNode = _source;
                SetStep(Step.WaitingForAccess);
                if (_traffic.RequestAccess(_robot.State.RobotId, ResourceId)) CallToPickup();
                break;
            case Step.Entering:
                SetStep(Step.Riding);
                MoveElevator(_destination.Floor);
                break;
            case Step.Exiting:
                _robot.CurrentNode = _destination;
                _mayBeInCabin = false;
                Dispose();
                _completed();
                break;
        }
    }

    public void OnElevatorArrived(string elevatorId)
    {
        if (!string.Equals(elevatorId, _elevatorId, StringComparison.OrdinalIgnoreCase)) return;
        var elevator = _elevators.GetElevator(elevatorId);
        if (elevator == null || elevator.Status != ElevatorStatus.Idle) return;

        if (_step == Step.WaitingForPickup && elevator.CurrentFloor == _source.Floor)
        {
            var dock = _map.Dock.AcquireDock(_source.NodeName);
            if (dock == null)
            {
                Fail("Elevator cabin point is unavailable.");
                return;
            }
            SetStep(Step.Entering);
            _mayBeInCabin = true;
            _robot.MoveTo(dock.Position);
            _robot.CurrentNode = _source;
            _robot.CurrentDock = dock;
        }
        else if (_step == Step.Riding && elevator.CurrentFloor == _destination.Floor)
        {
            SetStep(Step.Exiting);
            _robot.MoveTo(_destination.Position);
        }
    }

    private void OnAccessGranted(string robotId, string resourceId)
    {
        lock (_gate)
        {
            if (_step == Step.WaitingForAccess && robotId == _robot.State.RobotId && resourceId == ResourceId)
                CallToPickup();
        }
    }

    private void CallToPickup()
    {
        _ownsAccess = true;
        SetStep(Step.WaitingForPickup);
        // Always request pickup, even if the cached floor already matches.
        MoveElevator(_source.Floor);
    }

    private void MoveElevator(int floor)
    {
        var elevator = _elevators.GetElevator(_elevatorId);
        if (elevator == null)
        {
            Fail("The registered elevator is no longer available.");
            return;
        }
        elevator.Status = ElevatorStatus.Moving;
        _elevators.UpdateElevator(elevator);
        _ = SendElevatorAsync(new SocketMessage
        {
            Type = SocketMessageType.MoveElevator,
            Payload = JsonSerializer.SerializeToElement(new MoveElevatorRequest
            {
                ElevatorId = _elevatorId,
                TargetFloor = floor
            }, _json)
        });
    }

    private async System.Threading.Tasks.Task SendElevatorAsync(SocketMessage message)
    {
        try { await _gateway.SendCommandAsync(message); }
        catch (Exception ex)
        {
            lock (_gate)
            {
                if (_step != Step.Finished) Fail($"Failed to command elevator: {ex.Message}");
            }
        }
    }

    private void SetStep(Step step)
    {
        _timeout?.Cancel();
        _timeout?.Dispose();
        _step = step;
        _timeout = new CancellationTokenSource();
        _ = WatchTimeoutAsync(_timeout.Token);
    }

    private async System.Threading.Tasks.Task WatchTimeoutAsync(CancellationToken token)
    {
        try { await System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(2), token); }
        catch (OperationCanceledException) { return; }
        lock (_gate)
        {
            if (!token.IsCancellationRequested && _step != Step.Finished)
                Fail($"Elevator traversal timed out at {_step}.");
        }
    }

    private void Fail(string reason)
    {
        Dispose();
        _failed(reason);
    }

    public void Dispose()
    {
        if (_step == Step.Finished) return;
        _step = Step.Finished;
        _timeout?.Cancel();
        _timeout?.Dispose();
        _timeout = null;
        _traffic.AccessGranted -= OnAccessGranted;
        _traffic.CancelRequest(_robot.State.RobotId, ResourceId);
        // Cancellation/offline while boarding or inside must not give the cabin to another robot.
        if (_ownsAccess && !_mayBeInCabin)
        {
            _ownsAccess = false;
            _traffic.ReleaseAccess(_robot.State.RobotId, ResourceId);
        }
    }
}
