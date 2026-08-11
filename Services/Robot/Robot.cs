using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class Robot
{
    private readonly IMapManager _mapManager;
    private readonly IEventBus _eventBus;
    private readonly ICommunicationGateway _gateway;
    private readonly JsonSerializerOptions _jsonOptions;

    public RobotStateDto State { get; private set; }

    public MapPointDto? CurrentDock { get; set; }
    public MapNodeDto? CurrentNode { get; set; }

    public Robot(RobotStateDto state, IMapManager mapManager, IEventBus eventBus, ICommunicationGateway gateway, JsonSerializerOptions jsonOptions)
    {
        State = state;
        _mapManager = mapManager;
        _gateway = gateway;
        _eventBus = eventBus;
        _jsonOptions = jsonOptions;
    }

    public void ClearCurrentTask()
    {
        State.CurrentTaskId = null;
    }

    public void MoveTo(float[] position)
    {
        if (CurrentDock != null && CurrentNode != null)
        {
            _mapManager.Dock.ReleaseDock(CurrentNode.NodeName, CurrentDock.PointName);
            CurrentNode = null;
            CurrentDock = null;
        }

        var message = new SocketMessage
        {
            Type = SocketMessageType.MoveRobot,
            RequestId = null,
            RobotId = State.RobotId,
            Payload = JsonSerializer.SerializeToElement(position, _jsonOptions)
        };

        _ = _gateway.BroadcastAsync(message, CancellationToken.None);
    }

    public void ChangeStatus(RobotStatus status)
    {
        State.Status = status;

        var message = new SocketMessage
        {
            Type = SocketMessageType.ChangeRobotStatus,
            RequestId = null,
            RobotId = State.RobotId,
            Payload = JsonSerializer.SerializeToElement(status, _jsonOptions)
        };

        _ = _gateway.BroadcastAsync(message, CancellationToken.None);

        if (status == RobotStatus.Idle)
        {
            _eventBus.Publish(new RobotBackToIdleEvent(State));
            TryGoToWaitingDock();
        }
    }

    private void TryGoToWaitingDock()
    {
        if (CurrentNode != null && _mapManager.Dock.IsNodeFull(CurrentNode.NodeName))
        {
            var waitingNodes = _mapManager.Graph.GetNodesBy(NodeType.WaitingArea);
            foreach (var node in waitingNodes)
            {
                var dock = _mapManager.Dock.AcquireDock(node.NodeName);
                if (dock == null) continue;
                MoveTo(dock.Position);
                CurrentNode = node;
                CurrentDock = dock;
                break;
            }
        }
    }

    public void TryGoToChargingDock()
    {
        var chargingNodes = _mapManager.Graph.GetNodesBy(NodeType.ChargingArea);
        foreach (var node in chargingNodes)
        {
            var dock = _mapManager.Dock.AcquireDock(node.NodeName);
            if (dock == null) continue;
            MoveTo(dock.Position);
            CurrentNode = node;
            CurrentDock = dock;
            ChangeStatus(RobotStatus.Charging);
            break;
        }
    }
}