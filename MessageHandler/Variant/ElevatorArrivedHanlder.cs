using FleetBackend.Models;
using FleetBackend.Services;
using System.Text.Json;

public class ElevatorArrivedHandler : IMessageHandler
{
    public SocketMessageType MessageType => SocketMessageType.ElevatorArrived;

    private readonly ITaskExecuteManager _taskExecuteManager;
    private readonly IElevatorManager _elevatorManager;

    public ElevatorArrivedHandler(ITaskExecuteManager taskExecuteManager, IElevatorManager elevatorManager)
    {
        _taskExecuteManager = taskExecuteManager;
        _elevatorManager = elevatorManager;
    }

    public Task<SocketMessage?> HandleAsync(SocketMessage socketMessage, JsonSerializerOptions jsonOptions)
    {
        ElevatorDto? elevator = socketMessage.Payload.Deserialize<ElevatorDto>(jsonOptions);
        if (elevator != null && !string.IsNullOrWhiteSpace(elevator.ElevatorId))
        {
            if (_elevatorManager.UpdateElevator(elevator))
                _taskExecuteManager.OnElevatorArrived(elevator.ElevatorId);
        }

        return Task.FromResult<SocketMessage?>(null);
    }
}
