using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services;

public class RegisterRobotHandler : IMessageHandler
{
    private readonly IRobotManager _robotManager;

    public SocketMessageType MessageType => SocketMessageType.RegisterRobot;

    public RegisterRobotHandler(IRobotManager robotManager)
    {
        _robotManager = robotManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        RobotStateDto? payloadData = socketMessage.Payload.Deserialize<RobotStateDto>(jsonOptions);

        if (payloadData != null)
        {
            var robotId = _robotManager.RegisterRobot(payloadData);
            var response = new SocketMessage
            {
                Type = SocketMessageType.None,
                RequestId = socketMessage.RequestId,
                Payload = JsonSerializer.SerializeToElement(new RobotStateDto { RobotId = robotId }),
            };

            return Task.FromResult<SocketMessage?>(response);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}