using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Options;
using Microsoft.Extensions.Options;

namespace FleetBackend.Services.Elevator;

public sealed class SimulationElevatorAdapter(ICommunicationGateway gateway, IElevatorManager elevators,
    IOptions<ElevatorMqttOptions> options, JsonSerializerOptions json) : ElevatorAdapterBase(options)
{
    public override bool RequiresDoorControl => false;

    protected override void ValidateId(string id)
    {
        base.ValidateId(id);
        if (elevators.GetElevator(id) == null) throw new ArgumentException("Simulation elevator is not registered.", nameof(id));
        if (gateway.SystemMode != SystemMode.Simulation) throw new InvalidOperationException("Simulation mode is not active.");
        Monitor(id).SetConnected(true);
    }

    protected override global::System.Threading.Tasks.Task PublishAsync(string elevatorId, string command, int? floor, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (gateway.SystemMode != SystemMode.Simulation) throw new InvalidOperationException("Simulation mode is not active.");
        if (command == "GET_STATUS")
        {
            var elevator = elevators.GetElevator(elevatorId) ?? throw new IOException("Simulation elevator disappeared.");
            NotifyArrived(elevator);
            return global::System.Threading.Tasks.Task.CompletedTask;
        }
        var current = elevators.GetElevator(elevatorId) ?? throw new IOException("Simulation elevator disappeared.");
        current.Status = ElevatorStatus.Moving;
        elevators.UpdateElevator(current);
        return gateway.SendCommandAsync(new SocketMessage
        {
            Type = SocketMessageType.MoveElevator,
            Payload = JsonSerializer.SerializeToElement(new MoveElevatorRequest
            {
                ElevatorId = elevatorId, TargetFloor = floor!.Value
            }, json)
        }, token);
    }

    public void NotifyArrived(ElevatorDto elevator)
    {
        if (gateway.SystemMode != SystemMode.Simulation) return;
        var monitor = Monitor(elevator.ElevatorId);
        monitor.SetConnected(true);
        monitor.Receive(elevator.CurrentFloor, elevator.Status == ElevatorStatus.Idle ? "ARRIVED" : "MOVING",
            JsonSerializer.Serialize(elevator, json), "Unity/ElevatorArrived", false);
    }
}
