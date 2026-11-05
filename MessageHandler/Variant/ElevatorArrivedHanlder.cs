using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;
using FleetBackend.Services.Elevator;

public class ElevatorArrivedHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.ElevatorArrived;

    private readonly IElevatorManager _elevatorManager;
    private readonly SimulationElevatorAdapter _simulation;
    private readonly ICommunicationGateway _gateway;

    public ElevatorArrivedHandler(IElevatorManager elevatorManager,
        SimulationElevatorAdapter simulation, ICommunicationGateway gateway)
    {
        _elevatorManager = elevatorManager;
        _simulation = simulation;
        _gateway = gateway;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        // Physical status comes exclusively through MQTT, never Unity's simulation events.
        if (_gateway.SystemMode != SystemMode.Simulation)
            return Task.FromResult<SocketMessage?>(null);
        ElevatorDto? elevator = socketMessage.Payload.Deserialize<ElevatorDto>(jsonOptions);
        if (elevator != null && !string.IsNullOrWhiteSpace(elevator.ElevatorId))
        {
            if (_elevatorManager.UpdateElevator(elevator))
            {
                _simulation.NotifyArrived(elevator);
            }
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}
