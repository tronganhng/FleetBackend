using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services;

public class SyncRobotStateHandler : IMessageHandler
{
    private readonly IRobotManager _robotManager;

    public SocketMessageType MessageType => SocketMessageType.RobotState;

    public SyncRobotStateHandler(IRobotManager robotManager)
    {
        _robotManager = robotManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        RobotStateDto? payloadData = socketMessage.Payload.Deserialize<RobotStateDto>(jsonOptions);

        if (payloadData != null)
        {
            _robotManager.UpdateRobotState(payloadData);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}