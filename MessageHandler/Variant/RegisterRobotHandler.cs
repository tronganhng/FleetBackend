using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services;

public class RegisterRobotHandler : IMessageHandler
{
    private readonly IRobotManager _robotManager;
    private readonly ICommunicationGateway _gateway;

    public SocketMessageType MessageType => SocketMessageType.RegisterRobot;

    public RegisterRobotHandler(IRobotManager robotManager, ICommunicationGateway gateway)
    {
        _robotManager = robotManager;
        _gateway = gateway;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        RobotStateDto? payloadData = socketMessage.Payload.Deserialize<RobotStateDto>(jsonOptions);

        if (payloadData != null)
        {
            var robotId = _robotManager.RegisterRobot(payloadData);
            _gateway.SetRobotIdToSocket(robotId, socketMessage.ConnectionId);
            var response = new SocketMessage
            {
                Type = SocketMessageType.ServerResponse,
                RequestId = socketMessage.RequestId,
                Payload = JsonSerializer.SerializeToElement(new RobotStateDto { RobotId = robotId }, jsonOptions),
            };

            return Task.FromResult<SocketMessage?>(response);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}