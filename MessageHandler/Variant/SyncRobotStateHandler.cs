using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services;

public class SyncRobotStateHandler : IMessageHandler
{
    private readonly IRobotManager _robotManager;
    private readonly ICommunicationGateway _gateway;

    public SocketMessageType MessageType => SocketMessageType.RobotState;

    public SyncRobotStateHandler(IRobotManager robotManager, ICommunicationGateway gateway)
    {
        _robotManager = robotManager;
        _gateway = gateway;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        RobotStateDto? payloadData = socketMessage.Payload.Deserialize<RobotStateDto>(jsonOptions);

        if (payloadData != null)
        {
            _robotManager.UpdateRobotState(payloadData);

            // if OP mode -> sync unity
            if (_gateway.SystemMode == SystemMode.Operation)
            {
                var message = new SocketMessage
                {
                    Type = SocketMessageType.RobotState,
                    RequestId = null,
                    RobotId = payloadData.RobotId,
                    Payload = JsonSerializer.SerializeToElement(payloadData, jsonOptions)
                };
                _ = _gateway.SendDashboardAsync(message, CancellationToken.None);
            }
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}