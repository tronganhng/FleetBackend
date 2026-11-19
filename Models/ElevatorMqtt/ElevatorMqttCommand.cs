using System.Text.Json.Serialization;

namespace FleetBackend.Models.ElevatorMqtt;

// This namespace belongs to the TurtleBot integration, not the fleet robot registry.
public sealed record ElevatorMqttCommand
{
    [JsonPropertyName("protocol_version")] public int ProtocolVersion => 2;
    [JsonPropertyName("session_id")] public string SessionId { get; init; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("trip_id")] public string TripId { get; init; } = "";
    [JsonPropertyName("request_id")] public string RequestId { get; init; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("command_seq")] public long CommandSeq { get; init; } = 1;
    [JsonPropertyName("boot_id")] public string BootId { get; init; } = "";
    [JsonPropertyName("expires_at_ms"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExpiresAtMs { get; init; }
    [JsonPropertyName("expected_floor"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ExpectedFloor { get; init; }

    [JsonPropertyName("robot_id")]
    public string RobotNamespace => "robot3";

    [JsonPropertyName("command")]
    public string Command { get; }

    [JsonPropertyName("destination_floor")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DestinationFloor { get; }

    private ElevatorMqttCommand(string command, int? destinationFloor = null)
    {
        Command = command;
        DestinationFloor = destinationFloor;
    }

    public static ElevatorMqttCommand GetStatus() => new("GET_STATUS");
    public static ElevatorMqttCommand OpenDoor() => new("OPEN_DOOR");
    public static ElevatorMqttCommand CloseDoor() => new("CLOSE_DOOR");

    public static ElevatorMqttCommand CallElevator(int destinationFloor)
    {
        // Only these floors are supported by the documented integration.
        if (destinationFloor is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(destinationFloor), "Supported elevator floors are 1 and 2.");

        return new("CALL_ELEVATOR", destinationFloor);
    }
}
