using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class RobotArrivedHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.RobotArrived;

    private readonly ITaskExecuteManager _taskExecuteManager;

    public RobotArrivedHandler(ITaskExecuteManager taskExecuteManager)
    {
        _taskExecuteManager = taskExecuteManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        string? robotId = socketMessage.RobotId;
        if (robotId != null)
        {
            _taskExecuteManager.OnRobotArrived(robotId);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}