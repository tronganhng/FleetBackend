using System.Text.Json;
using FleetBackend.Models.ElevatorMqtt;
namespace FleetBackend.Tests;

internal sealed class ElevatorV2Fixture
{
    private long _seq;
    public string Status(ElevatorMqttCommand? command = null, int floor = 1, string state = "DOOR_CLOSED",
        string result = "COMPLETED", string boot = "esp-boot", string? session = null)
    {
        var fields = command == null ? new Dictionary<string, object?>() :
            JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(command))!;
        fields["protocol_version"] = 2;
        fields["robot_id"] = "robot3";
        fields["boot_id"] = boot;
        fields["status_seq"] = ++_seq;
        fields["uptime_ms"] = 1000 + _seq;
        fields["current_floor"] = floor;
        fields["target_floor"] = floor;
        fields["moving"] = false;
        fields["door_state"] = state == "DOOR_OPEN" ? "OPEN" : "CLOSED";
        fields["door_mode"] = state == "DOOR_OPEN" ? "ROBOT_HOLD" : "CLOSED";
        fields["hold_session"] = session ?? command?.SessionId ?? "";
        fields["result"] = command == null ? "STATUS" : result;
        fields["state"] = state;
        fields["error"] = "TEST_REJECTED";
        return JsonSerializer.Serialize(fields);
    }
}
