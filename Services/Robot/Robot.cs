using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class Robot
{
    private readonly IMapManager _mapManager;
    private readonly ICommunicationGateway _gateway;
    private readonly JsonSerializerOptions _jsonOptions;

    public RobotStateDto State { get; private set; }

    public MapPointDto? CurrentDock { get; set; }
    public MapNodeDto? CurrentNode { get; set; }

    public Robot(RobotStateDto state, IMapManager mapManager, ICommunicationGateway gateway, JsonSerializerOptions jsonOptions)
    {
        State = state;
        _mapManager = mapManager;
        _gateway = gateway;
        _jsonOptions = jsonOptions;
    }

    public void ClearCurrentTask()
    {
        State.CurrentTaskId = null;
    }

    public void Offline()
    {
        State.Status = RobotStatus.Offline;
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
        var message = new SocketMessage
        {
            Type = SocketMessageType.ChangeRobotStatus,
            RequestId = null,
            RobotId = State.RobotId,
            Payload = JsonSerializer.SerializeToElement(status, _jsonOptions)
        };

        _ = _gateway.BroadcastAsync(message, CancellationToken.None);
    }
}