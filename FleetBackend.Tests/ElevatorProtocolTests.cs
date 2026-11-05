using System.Text.Json;
using System.Text.Json.Serialization;
using FleetBackend.Models;
using FleetBackend.Models.ElevatorMqtt;
using FleetBackend.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FleetBackend.Tests;

public class ElevatorProtocolTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void CallCommand_UsesExactDeviceSchema(int floor)
    {
        var payload = JsonSerializer.SerializeToElement(ElevatorMqttCommand.CallElevator(floor), Json);

        Assert.Equal(3, payload.EnumerateObject().Count());
        Assert.Equal("robot3", payload.GetProperty("robot_id").GetString());
        Assert.Equal("CALL_ELEVATOR", payload.GetProperty("command").GetString());
        Assert.Equal(floor, payload.GetProperty("destination_floor").GetInt32());
    }

    [Fact]
    public void CommandsWithoutFloor_OmitDestinationRatherThanSendingNull()
    {
        var commands = new[]
        {
            (ElevatorMqttCommand.GetStatus(), "GET_STATUS"),
            (ElevatorMqttCommand.OpenDoor(), "OPEN_DOOR"),
            (ElevatorMqttCommand.CloseDoor(), "CLOSE_DOOR")
        };
        foreach (var (command, name) in commands)
        {
            var payload = JsonSerializer.SerializeToElement(command, Json);
            Assert.Equal(2, payload.EnumerateObject().Count());
            Assert.Equal("robot3", payload.GetProperty("robot_id").GetString());
            Assert.Equal(name, payload.GetProperty("command").GetString());
            Assert.False(payload.TryGetProperty("destination_floor", out _));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    public void CallCommand_RejectsUndocumentedFloors(int floor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ElevatorMqttCommand.CallElevator(floor));
    }

    [Fact]
    public void Status_PreservesPartialDataAndUnknownStates()
    {
        var floorOnly = JsonSerializer.Deserialize<ElevatorMqttStatusPayload>("{\"current_floor\":2}", Json)!;
        var stateOnly = JsonSerializer.Deserialize<ElevatorMqttStatusPayload>("{\"state\":\"VENDOR_STATE\",\"vendor_code\":7}", Json)!;

        Assert.Equal(2, floorOnly.CurrentFloor);
        Assert.Null(floorOnly.State);
        Assert.Null(stateOnly.CurrentFloor);
        Assert.Equal("VENDOR_STATE", stateOnly.State);
        Assert.Equal(7, stateOnly.AdditionalFields!["vendor_code"].GetInt32());
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ElevatorMqttStatusPayload>("{\"current_floor\":\"2\"}", Json));
    }

    [Fact]
    public void FloorChangeMessages_RetainBackendIdentityAndCorrelation()
    {
        var request = new SocketMessage
        {
            Type = SocketMessageType.ChangeRobotFloor,
            RequestId = "operation-123",
            RobotId = "fleet-robot-42",
            Payload = JsonSerializer.SerializeToElement(new ChangeRobotFloorRequest
            {
                ElevatorId = "Elevator_01", TargetFloor = 2
            }, Json)
        };
        var decoded = JsonSerializer.Deserialize<SocketMessage>(JsonSerializer.Serialize(request, Json), Json)!;
        Assert.Equal(SocketMessageType.ChangeRobotFloor, decoded.Type);
        Assert.Equal("operation-123", decoded.RequestId);
        Assert.Equal("fleet-robot-42", decoded.RobotId);
        Assert.Equal(2, decoded.Payload.Deserialize<ChangeRobotFloorRequest>(Json)!.TargetFloor);

        var ready = JsonSerializer.Deserialize<RobotFloorReadyPayload>(
            "{\"ElevatorId\":\"Elevator_01\",\"TargetFloor\":2,\"IsReady\":false,\"ErrorMessage\":\"Localization failed\"}", Json)!;
        Assert.False(ready.IsReady);
        Assert.Equal("Localization failed", ready.ErrorMessage);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RobotFloorReadyPayload>(
            "{\"ElevatorId\":\"Elevator_01\",\"TargetFloor\":2}", Json));
    }

    [Fact]
    public void Configuration_BindsAndRejectsInvalidConnectionValues()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ElevatorMqtt:Host"] = "10.11.57.29",
            ["ElevatorMqtt:Port"] = "70000"
        }).Build();
        var services = new ServiceCollection();
        services.AddOptions<ElevatorMqttOptions>()
            .Bind(configuration.GetSection(ElevatorMqttOptions.SectionName))
            .ValidateDataAnnotations();
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ElevatorMqttOptions>>().Value);
    }
}
