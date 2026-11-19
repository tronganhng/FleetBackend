using System.Diagnostics;
using System.Text.Json;
using FleetBackend.Models.ElevatorMqtt;
using FleetBackend.Options;
using Microsoft.Extensions.Options;

namespace FleetBackend.Services.Elevator;

public sealed class MqttElevatorAdapter : ElevatorAdapterBase, IDisposable
{
    private readonly IElevatorMqttTransport _transport;
    private readonly ILogger<MqttElevatorAdapter> _logger;
    private readonly object _gate = new();
    private readonly string _session = Guid.NewGuid().ToString("N");
    private readonly HashSet<string> _retiredBoots = [];
    private readonly Timer _watchdog;
    private JsonElement? _status;
    private long _received;
    private long _sequence;
    private ElevatorMqttCommand? _pending;
    private TaskCompletionSource? _reply;
    private int? _holdFloor;
    private string _trip = "";
    public override bool RequiresDoorControl => true;

    public MqttElevatorAdapter(IElevatorMqttTransport transport, IOptions<ElevatorMqttOptions> options,
        ILogger<MqttElevatorAdapter> logger) : base(options)
    {
        _transport = transport;
        _logger = logger;
        transport.ConnectionChanged += OnConnectionChanged;
        transport.MessageReceived += OnMessage;
        OnConnectionChanged(transport.IsReady);
        _watchdog = new Timer(_ => CheckHeartbeat(), null, 250, 250);
    }

    protected override void ValidateId(string id)
    {
        base.ValidateId(id);
        if (!string.Equals(id, Options.ElevatorId, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Elevator ID does not match the configured physical elevator.", nameof(id));
    }

    public void BeginTrip()
    {
        lock (_gate) _trip = Guid.NewGuid().ToString("N");
    }

    public void EndTrip()
    {
        lock (_gate) { _trip = ""; _holdFloor = null; }
    }

    private bool Fresh => _status.HasValue && Stopwatch.GetElapsedTime(_received).TotalSeconds <= 5;

    private void Fail(string reason)
    {
        _reply?.TrySetException(new IOException(reason));
        SetConnected(Options.ElevatorId, false, notifyAsync: true);
        _holdFloor = null;
        _trip = "";
    }

    private void OnConnectionChanged(bool connected)
    {
        lock (_gate)
        {
            if (!connected) { Fail("Elevator MQTT disconnected; recovery required."); _status = null; }
            else SetConnected(Options.ElevatorId, true);
        }
    }

    private void CheckHeartbeat()
    {
        lock (_gate)
            if (_status.HasValue && !Fresh && GetSnapshot(Options.ElevatorId).IsConnected)
                Fail("Elevator heartbeat missing for five seconds; recovery required.");
    }

    protected override async global::System.Threading.Tasks.Task PublishAsync(string elevatorId, string command, int? floor, CancellationToken token)
    {
        ElevatorMqttCommand request;
        global::System.Threading.Tasks.Task reply;
        lock (_gate)
        {
            if (!_transport.IsReady || (command != "GET_STATUS" && !Fresh))
                throw new IOException("A fresh ESP status is required before elevator mutations.");
            request = (command switch
            {
                "GET_STATUS" => ElevatorMqttCommand.GetStatus(),
                "CALL_ELEVATOR" => ElevatorMqttCommand.CallElevator(floor!.Value),
                "OPEN_DOOR" => ElevatorMqttCommand.OpenDoor(),
                "CLOSE_DOOR" => ElevatorMqttCommand.CloseDoor(),
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            }) with
            {
                SessionId = _session, TripId = _trip, CommandSeq = ++_sequence,
                BootId = _status?.GetProperty("boot_id").GetString() ?? "",
                ExpectedFloor = command is "OPEN_DOOR" or "CLOSE_DOOR" ? floor : null,
                ExpiresAtMs = command == "GET_STATUS" ? null :
                    _status!.Value.GetProperty("uptime_ms").GetInt64() + (long)Stopwatch.GetElapsedTime(_received).TotalMilliseconds + (long)Math.Clamp((Monitor(elevatorId).Snapshot.LastCommand!.DeadlineUtc - DateTimeOffset.UtcNow).TotalMilliseconds, 1, 10000)
            };
            if (command == "CLOSE_DOOR") _holdFloor = null;
            _pending = request;
            _reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
            reply = _reply.Task;
        }
        try
        {
            for (var attempt = 0; attempt < 4 && !reply.IsCompleted; attempt++)
            {
                await _transport.PublishAsync(request, token);
                await global::System.Threading.Tasks.Task.WhenAny(reply, global::System.Threading.Tasks.Task.Delay(2000, token));
                token.ThrowIfCancellationRequested();
            }
            await reply.WaitAsync(token);
        }
        finally
        {
            lock (_gate) { _pending = null; _reply = null; }
        }
    }

    private bool Held(JsonElement data, int floor) => !data.GetProperty("moving").GetBoolean() &&
        data.GetProperty("current_floor").GetInt32() == floor && data.GetProperty("door_state").GetString() == "OPEN" &&
        data.GetProperty("door_mode").GetString() == "ROBOT_HOLD" && data.GetProperty("hold_session").GetString() == _session;

    private void OnMessage(ElevatorMqttMessage message)
    {
        if (message.Topic != Options.StatusTopic || message.Retained) return;
        try
        {
            using var document = JsonDocument.Parse(message.Payload);
            var data = document.RootElement;
            if (data.GetProperty("protocol_version").GetInt32() != 2 || data.GetProperty("robot_id").GetString() != "robot3") return;
            var boot = data.GetProperty("boot_id").GetString();
            var seq = data.GetProperty("status_seq").GetInt64();
            var floor = data.GetProperty("current_floor").GetInt32();
            var target = data.GetProperty("target_floor").GetInt32();
            var moving = data.GetProperty("moving").GetBoolean();
            var door = data.GetProperty("door_state").GetString();
            var mode = data.GetProperty("door_mode").GetString();
            var result = data.GetProperty("result").GetString();
            if (string.IsNullOrEmpty(boot) || seq <= 0 || data.GetProperty("uptime_ms").GetInt64() < 0 ||
                floor is not (1 or 2) || target is not (1 or 2) || door is not ("OPEN" or "CLOSED") ||
                mode is not ("AUTO" or "ROBOT_HOLD" or "CLOSED") ||
                data.GetProperty("hold_session").ValueKind != JsonValueKind.String ||
                result is not ("STATUS" or "ACCEPTED" or "COMPLETED" or "REJECTED") ||
                (door == "CLOSED") != (mode == "CLOSED") || (moving && door != "CLOSED")) return;
            lock (_gate)
            {
                if (_retiredBoots.Contains(boot)) return;
                if (_status is { } old)
                {
                    if (old.GetProperty("boot_id").GetString() != boot)
                    {
                        _retiredBoots.Add(old.GetProperty("boot_id").GetString()!);
                        Fail("ESP rebooted; recovery required.");
                    }
                    else if (seq <= old.GetProperty("status_seq").GetInt64()) return;
                }
                _status = data.Clone();
                _received = Stopwatch.GetTimestamp();
                if (_holdFloor is { } held && !Held(data, held)) Fail("Elevator door hold lost; recovery required.");
                SetConnected(Options.ElevatorId, _transport.IsReady);
                var state = moving ? "MOVING" : door == "OPEN" ? "DOOR_OPEN" : "DOOR_CLOSED";
                var matches = false;
                if (_pending is { } request && _reply != null && !_reply.Task.IsCompleted &&
                    new[] { "robot_id", "session_id", "trip_id", "request_id", "command", "command_seq" }.All(key =>
                        data.TryGetProperty(key, out var value) && value.ToString() == JsonSerializer.SerializeToElement(request).GetProperty(key).ToString()))
                {
                    if (result == "REJECTED") _reply.TrySetException(new IOException($"ESP rejected {request.Command}: {data.GetProperty("error")}"));
                    else if (result == "COMPLETED")
                    {
                        matches = request.Command == "GET_STATUS" || boot == request.BootId && request.Command switch
                        {
                            "CALL_ELEVATOR" => !moving && floor == request.DestinationFloor,
                            "OPEN_DOOR" => Held(data, request.ExpectedFloor!.Value),
                            "CLOSE_DOOR" => !moving && floor == request.ExpectedFloor && door == "CLOSED",
                            _ => false
                        };
                        if (!matches) _reply.TrySetException(new IOException("ESP completion has incompatible actual state."));
                        else
                        {
                            if (request.Command == "OPEN_DOOR") _holdFloor = floor;
                            if (request.Command == "CALL_ELEVATOR") state = "ARRIVED";
                        }
                    }
                }
                Monitor(Options.ElevatorId).Receive(floor, state, message.Payload, message.Topic, false, matches);
                if (matches) _reply!.TrySetResult();
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            _logger.LogWarning(ex, "Ignored invalid elevator v2 status");
        }
    }

    public void Dispose()
    {
        _watchdog.Dispose();
        _transport.ConnectionChanged -= OnConnectionChanged;
        _transport.MessageReceived -= OnMessage;
        OnConnectionChanged(false);
    }
}
