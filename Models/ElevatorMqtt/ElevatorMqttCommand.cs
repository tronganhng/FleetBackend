using System.Text.Json.Serialization;

namespace FleetBackend.Models.ElevatorMqtt;

// This namespace belongs to the TurtleBot integration, not the fleet robot registry.
public sealed record ElevatorMqttCommand
{
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
