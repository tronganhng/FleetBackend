using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services;

public sealed class RobotFloorReadyHandler(ITaskExecuteManager tasks, ICommunicationGateway gateway) : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.RobotFloorReady;

    public Task<SocketMessage?> HandleAsync(SocketMessage message, JsonSerializerOptions json)
    {
        if (gateway.SystemMode == SystemMode.Operation && !string.IsNullOrWhiteSpace(message.RobotId) &&
            !string.IsNullOrWhiteSpace(message.RequestId) && gateway.IsRobotConnection(message.RobotId, message.ConnectionId))
        {
            var payload = message.Payload.Deserialize<RobotFloorReadyPayload>(json);
            if (payload != null) tasks.OnRobotFloorReady(message.RobotId, message.RequestId, payload);
        }
        return Task.FromResult<SocketMessage?>(null);
    }
}
