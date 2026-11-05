using System.Text.Json;
using System.Text.Json.Serialization;

namespace FleetBackend.Models.ElevatorMqtt;

// A single received message, not a merged cache or acknowledgement of a command.
public sealed record ElevatorMqttStatusPayload
{
    [JsonPropertyName("current_floor")]
    public int? CurrentFloor { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; init; }
}

public static class ElevatorMqttStates
{
    public const string Arrived = "ARRIVED";
    public const string DoorOpen = "DOOR_OPEN";
    public const string DoorClosed = "DOOR_CLOSED";
}
