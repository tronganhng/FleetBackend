using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class RobotArrivedHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.RobotArrived;

    private readonly ITaskExecuteManager _taskExecuteManager;
    private readonly ICommunicationGateway _gateway;

    public RobotArrivedHandler(ITaskExecuteManager taskExecuteManager, ICommunicationGateway gateway)
    {
        _taskExecuteManager = taskExecuteManager;
        _gateway = gateway;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        string? robotId = socketMessage.RobotId;
        if (robotId != null && (_gateway.SystemMode == SystemMode.Simulation ||
            _gateway.IsRobotConnection(robotId, socketMessage.ConnectionId)))
        {
            _taskExecuteManager.OnRobotArrived(robotId);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}
