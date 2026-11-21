using System.Net.WebSockets;
using System.Text.Json;
using FleetBackend.Extensions;
using FleetBackend.Models;
using FleetBackend.Models.ElevatorMqtt;
using FleetBackend.Options;
using FleetBackend.Services;
using FleetBackend.Services.Elevator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FleetBackend.Tests;

public sealed class ElevatorAdapterTests
{
    private const string Elevator = "Elevator_01";

    private static MqttElevatorAdapter Adapter(FakeTransport transport) => new(transport,
        Microsoft.Extensions.Options.Options.Create(new ElevatorMqttOptions()), NullLogger<MqttElevatorAdapter>.Instance);

    [Fact]
    public async Task V2_RequiresCorrelatedCompletedAndOwnedHold()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        var esp = new ElevatorV2Fixture();
        transport.Receive(esp.Status());
        var call = adapter.CallAsync(Elevator, 2, TimeSpan.FromSeconds(3));
        var command = transport.Commands.Single();
        Assert.Equal(2, command.ProtocolVersion);
        Assert.NotNull(command.ExpiresAtMs);
        transport.Receive(esp.Status(command, 2, result: "ACCEPTED"));
        transport.Receive(esp.Status(command with { RequestId = "wrong" }, 2));
        transport.Receive(esp.Status(command, 2), retained: true);
        Assert.False(call.IsCompleted);
        transport.Receive(esp.Status(command, 2));
        await call;
        var open = adapter.OpenDoorAsync(Elevator, 2);
        var door = transport.Commands.Last();
        Assert.Equal(2, door.ExpectedFloor);
        transport.Receive(esp.Status(door, 2, "DOOR_OPEN"));
        await open;
        var lost = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.Disconnected += _ => lost.TrySetResult();
        transport.Receive(esp.Status(floor: 2));
        await lost.Task.WaitAsync(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task V2_RejectsLegacyAndRejectionsAndBootChanges()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        var esp = new ElevatorV2Fixture();
        transport.Receive("{\"current_floor\":1,\"state\":\"ARRIVED\"}");
        Assert.Null(adapter.GetSnapshot(Elevator).CurrentFloor);
        transport.Receive(esp.Status());
        var call = adapter.CallAsync(Elevator, 2, TimeSpan.FromSeconds(3));
        transport.Receive(esp.Status(transport.Commands.Last(), 1, result: "REJECTED"));
        await Assert.ThrowsAsync<IOException>(() => call);
        var next = adapter.CallAsync(Elevator, 2, TimeSpan.FromSeconds(3));
        transport.Receive(esp.Status(transport.Commands.Last(), 2, boot: "new-boot"));
        await Assert.ThrowsAsync<IOException>(() => next);
    }

    [Fact]
    public async Task V2_MatchesReplyOvertakenByHeartbeatAndRejectionWithoutError()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        var esp = new ElevatorV2Fixture();
        transport.Receive(esp.Status());
        var call = adapter.CallAsync(Elevator, 2, TimeSpan.FromSeconds(3));
        var reply = esp.Status(transport.Commands.Last(), 2);
        transport.Receive(esp.Status(floor: 2)); // newer heartbeat arrives first
        transport.Receive(reply);
        await call;
        var next = adapter.CallAsync(Elevator, 1, TimeSpan.FromSeconds(3));
        transport.Receive(esp.Status(transport.Commands.Last(), 2, result: "REJECTED")
            .Replace(",\"error\":\"TEST_REJECTED\"", ""));
        var error = await Assert.ThrowsAsync<IOException>(() => next);
        Assert.Contains("UNKNOWN", error.Message);
    }

    [Fact]
    public async Task V2_RetryReusesEnvelopeAndHeartbeatLossFailsIdleHold()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        var esp = new ElevatorV2Fixture();
        transport.Receive(esp.Status());
        transport.OnPublish = command =>
        {
            if (transport.Commands.Count == 2)
                transport.Receive(esp.Status(command, 1, "DOOR_OPEN"));
        };
        await adapter.OpenDoorAsync(Elevator, 1);
        Assert.Equal(2, transport.Commands.Count);
        Assert.Equal(transport.Commands[0], transport.Commands[1]);
        var failure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        adapter.Disconnected += _ => failure.TrySetResult();
        await failure.Task.WaitAsync(TimeSpan.FromSeconds(7));
        Assert.False(adapter.GetSnapshot(Elevator).IsConnected);
    }

    [Fact]
    public async Task Simulation_UsesExistingUnityCommandAndDoesNotSendDoorCommands()
    {
        var gateway = new RecordingGateway();
        var manager = new ElevatorManager();
        manager.RegisterElevator(new ElevatorDto { ElevatorId = Elevator });
        var adapter = new SimulationElevatorAdapter(gateway, manager,
            Microsoft.Extensions.Options.Options.Create(new ElevatorMqttOptions()), new JsonSerializerOptions());
        Assert.False(adapter.RequiresDoorControl);
        var call = adapter.CallAsync(Elevator, 2, TimeSpan.FromSeconds(2));
        var message = Assert.Single(gateway.Commands);
        Assert.Equal(SocketMessageType.MoveElevator, message.Type);
        Assert.Equal(2, message.Payload.Deserialize<MoveElevatorRequest>()!.TargetFloor);
        Assert.False(call.IsCompleted);
        adapter.NotifyArrived(new ElevatorDto { ElevatorId = Elevator, CurrentFloor = 2, Status = ElevatorStatus.Idle });
        await call;
        await adapter.OpenDoorAsync(Elevator, 2);
        await adapter.CloseDoorAsync(Elevator, 2);
        Assert.Single(gateway.Commands);
    }

    [Fact]
    public async Task DependencyRegistration_StartsAndStopsWithoutMqttWhenDisabled()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new JsonSerializerOptions());
        services.AddSingleton<IElevatorManager, ElevatorManager>();
        services.AddOptions<ElevatorMqttOptions>();
        services.AddCommunication();
        // Receiver/router are not resolved in this transport lifecycle test.
        using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ElevatorAdapterResolver>();
        Assert.True(resolver.ForMode(SystemMode.Operation).RequiresDoorControl);
        Assert.False(resolver.ForMode(SystemMode.Simulation).RequiresDoorControl);
        var worker = Assert.Single(provider.GetServices<IHostedService>());
        await worker.StartAsync(CancellationToken.None);
        Assert.False(provider.GetRequiredService<IElevatorMqttTransport>().IsReady);
        await worker.StopAsync(CancellationToken.None);
    }

    private sealed class FakeTransport : IElevatorMqttTransport
    {
        public bool IsReady { get; private set; } = true;
        public event Action<bool>? ConnectionChanged;
        public event Action<ElevatorMqttMessage>? MessageReceived;
        public List<ElevatorMqttCommand> Commands { get; } = [];
        public Action<ElevatorMqttCommand>? OnPublish { get; set; }
        public Task PublishAsync(ElevatorMqttCommand command, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Commands.Add(command);
            OnPublish?.Invoke(command);
            return Task.CompletedTask;
        }
        public void Receive(string json, bool retained = false, string topic = "elevator/status") =>
            MessageReceived?.Invoke(new(topic, json, retained));
        public void SetConnected(bool connected)
        {
            IsReady = connected;
            ConnectionChanged?.Invoke(connected);
        }
    }

    private sealed class RecordingGateway : ICommunicationGateway
    {
        public List<SocketMessage> Commands { get; } = [];
        public SystemMode SystemMode { get; set; } = SystemMode.Simulation;
        public void RegisterUnitySocket(Guid id, WebSocket socket) { }
        public void RegisterRobotSocket(Guid id, WebSocket socket) { }
        public void RemoveSocket(Guid id) { }
        public void SetRobotIdToSocket(string robotId, Guid id) { }
        public Task ResetRobotIdSockets() => Task.CompletedTask;
        public Task SendDashboardAsync(SocketMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SendCommandAsync(SocketMessage message, CancellationToken cancellationToken = default)
        {
            Commands.Add(message);
            return Task.CompletedTask;
        }
    }
}
