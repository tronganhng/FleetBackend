using System.Text.Json;
using FleetBackend.Models.ElevatorMqtt;
using FleetBackend.Options;
using Microsoft.Extensions.Options;

namespace FleetBackend.Services.Elevator;

public sealed class MqttElevatorAdapter : ElevatorAdapterBase, IDisposable
{
    private readonly IElevatorMqttTransport _transport;
    private readonly ILogger<MqttElevatorAdapter> _logger;
    public override bool RequiresDoorControl => true;

    public MqttElevatorAdapter(IElevatorMqttTransport transport, IOptions<ElevatorMqttOptions> options,
        ILogger<MqttElevatorAdapter> logger) : base(options)
    {
        _transport = transport;
        _logger = logger;
        _transport.ConnectionChanged += OnConnectionChanged;
        _transport.MessageReceived += OnMessage;
        OnConnectionChanged(_transport.IsReady);
    }

    protected override void ValidateId(string id)
    {
        base.ValidateId(id);
        if (!string.Equals(id, Options.ElevatorId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Elevator ID does not match the configured physical elevator.", nameof(id));
    }

    private void OnConnectionChanged(bool connected) => SetConnected(Options.ElevatorId, connected);

    protected override global::System.Threading.Tasks.Task PublishAsync(string elevatorId, string command, int? floor, CancellationToken token) =>
        _transport.PublishAsync(command switch
        {
            "GET_STATUS" => ElevatorMqttCommand.GetStatus(),
            "CALL_ELEVATOR" => ElevatorMqttCommand.CallElevator(floor!.Value),
            "OPEN_DOOR" => ElevatorMqttCommand.OpenDoor(),
            "CLOSE_DOOR" => ElevatorMqttCommand.CloseDoor(),
            _ => throw new ArgumentOutOfRangeException(nameof(command))
        }, token);

    private void OnMessage(ElevatorMqttMessage message)
    {
        if (message.Topic != Options.StatusTopic) return;
        try
        {
            using var document = JsonDocument.Parse(message.Payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException("Status must be an object.");
            int? floor = null;
            string? state = null;
            if (root.TryGetProperty("current_floor", out var floorValue))
            {
                if (!floorValue.TryGetInt32(out var number) || number is not (1 or 2))
                    throw new JsonException("Invalid current_floor.");
                floor = number;
            }
            if (root.TryGetProperty("state", out var stateValue))
            {
                if (stateValue.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(stateValue.GetString()))
                    throw new JsonException("Invalid state.");
                state = stateValue.GetString();
            }
            Monitor(Options.ElevatorId).Receive(floor, state, message.Payload, message.Topic, message.Retained);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            _logger.LogWarning(ex, "Ignored invalid elevator status on {Topic}: {Payload}", message.Topic, message.Payload);
        }
    }

    public void Dispose()
    {
        _transport.ConnectionChanged -= OnConnectionChanged;
        _transport.MessageReceived -= OnMessage;
        OnConnectionChanged(false);
    }
}
