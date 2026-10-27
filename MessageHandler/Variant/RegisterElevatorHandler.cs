using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services;

public class RegisterElevatorHandler : IMessageHandler
{
    private readonly IElevatorManager _elevatorManager;

    public SocketMessageType MessageType => SocketMessageType.RegisterElevator;

    public RegisterElevatorHandler(IElevatorManager elevatorManager)
    {
        _elevatorManager = elevatorManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        var elevator = socketMessage.Payload.Deserialize<ElevatorDto>(jsonOptions);
        if (elevator == null)
            return Task.FromResult<SocketMessage?>(null);

        var elevatorId = _elevatorManager.RegisterElevator(elevator);
        var response = new SocketMessage
        {
            Type = SocketMessageType.ServerResponse,
            RequestId = socketMessage.RequestId,
            Payload = JsonSerializer.SerializeToElement(new ElevatorDto { ElevatorId = elevatorId }, jsonOptions)
        };

        return Task.FromResult<SocketMessage?>(response);
    }
}