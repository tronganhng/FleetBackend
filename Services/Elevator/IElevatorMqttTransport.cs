using FleetBackend.Models.ElevatorMqtt;

namespace FleetBackend.Services.Elevator;

public sealed record ElevatorMqttMessage(string Topic, string Payload, bool Retained);

public interface IElevatorMqttTransport
{
    bool IsReady { get; }
    event Action<bool>? ConnectionChanged;
    event Action<ElevatorMqttMessage>? MessageReceived;
    global::System.Threading.Tasks.Task PublishAsync(ElevatorMqttCommand command, CancellationToken cancellationToken);
}
