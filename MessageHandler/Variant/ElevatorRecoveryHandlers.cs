using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services.Elevator;

public sealed class GetElevatorRecoveryHandler(ElevatorRecoveryStore store) : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.GetElevatorRecovery;
    public Task<SocketMessage?> HandleAsync(SocketMessage message, JsonSerializerOptions json) =>
        Task.FromResult<SocketMessage?>(new SocketMessage
        {
            Type = SocketMessageType.ServerResponse, RequestId = message.RequestId,
            Payload = JsonSerializer.SerializeToElement(store.GetAll(), json)
        });
}

public sealed class RecoverElevatorHandler(ElevatorRecoveryManager recovery) : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.RecoverElevator;
    public async Task<SocketMessage?> HandleAsync(SocketMessage message, JsonSerializerOptions json)
    {
        object result;
        try
        {
            var request = message.Payload.Deserialize<RecoverElevatorRequest>(json)
                ?? throw new JsonException("Recovery request is required.");
            var status = await recovery.RecoverAsync(request.ElevatorId, request.CabinConfirmedEmpty, CancellationToken.None);
            result = new { Success = true, Status = status };
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or TimeoutException or JsonException)
        {
            result = new { Success = false, Error = ex.Message };
        }
        return new SocketMessage
        {
            Type = SocketMessageType.ServerResponse, RequestId = message.RequestId,
            Payload = JsonSerializer.SerializeToElement(result, json)
        };
    }
}
