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
    public async Task Call_WaitsForNewNonRetainedArrivalWithFloorInSameMessage()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        transport.Receive("{\"current_floor\":2,\"state\":\"ARRIVED\"}");
        var call = adapter.CallAsync(Elevator, 2, TimeSpan.FromSeconds(5));
        Assert.False(call.IsCompleted); // Cached arrival is not an acknowledgement.
        transport.Receive("{\"current_floor\":2,\"state\":\"ARRIVED\"}", retained: true);
        transport.Receive("{\"current_floor\":2}");
        transport.Receive("{\"state\":\"ARRIVED\"}");
        transport.Receive("{\"current_floor\":1,\"state\":\"ARRIVED\"}");
        Assert.False(call.IsCompleted);

        transport.Receive("{\"current_floor\":2,\"state\":\"ARRIVED\"}");
        var result = await call.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, result.CurrentFloor);
        Assert.Equal(ElevatorCommandOutcome.Completed, result.LastCommand!.Outcome);
        Assert.Single(transport.Commands);
    }

    [Fact]
    public async Task Waiter_IsInstalledBeforeSynchronousPublishResponse()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        transport.OnPublish = _ => transport.Receive("{\"current_floor\":1,\"state\":\"ARRIVED\"}");
        var result = await adapter.CallAsync(Elevator, 1, TimeSpan.FromSeconds(1));
        Assert.Equal(ElevatorCommandOutcome.Completed, result.LastCommand!.Outcome);
    }

    [Theory]
    [InlineData(true, "OPEN_DOOR", "DOOR_OPEN")]
    [InlineData(false, "CLOSE_DOOR", "DOOR_CLOSED")]
    public async Task Door_WaitsForExplicitStateAndCorrectFloor(bool open, string command, string state)
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        var operation = open ? adapter.OpenDoorAsync(Elevator, 1) : adapter.CloseDoorAsync(Elevator, 1);
        Assert.Equal(command, transport.Commands.Single().Command);
        transport.Receive("{\"current_floor\":1,\"state\":\"ARRIVED\"}");
        transport.Receive($"{{\"current_floor\":2,\"state\":\"{state}\"}}");
        transport.Receive($"{{\"state\":\"{state}\"}}");
        Assert.False(operation.IsCompleted);
        transport.Receive($"{{\"current_floor\":1,\"state\":\"{state}\"}}");
        Assert.Equal(state, (await operation.WaitAsync(TimeSpan.FromSeconds(2))).State);
    }

    [Fact]
    public async Task PartialAndUnknownStatus_UpdateCacheButInvalidMessagesDoNotCorruptIt()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        Assert.Null(adapter.GetSnapshot(Elevator).CurrentFloor);
        transport.Receive("{\"current_floor\":1}");
        var floorAt = adapter.GetSnapshot(Elevator).FloorReceivedAtUtc;
        transport.Receive("{\"state\":\"VENDOR_STATE\",\"vendor\":42}");
        var snapshot = adapter.GetSnapshot(Elevator);
        Assert.Equal(1, snapshot.CurrentFloor);
        Assert.Equal(floorAt, snapshot.FloorReceivedAtUtc);
        Assert.Equal("VENDOR_STATE", snapshot.State);
        Assert.Contains("vendor", snapshot.RawPayload);

        foreach (var invalid in new[] { "{", "null", "[]", "{\"current_floor\":\"2\"}",
                     "{\"current_floor\":0}", "{\"current_floor\":null}", "{\"state\":3}", "{\"state\":\"\"}" })
            transport.Receive(invalid);
        transport.Receive("{\"current_floor\":2}", topic: "other/status");
        Assert.Equal(snapshot, adapter.GetSnapshot(Elevator));

        transport.OnPublish = _ => transport.Receive("{\"current_floor\":2}");
        Assert.Equal(2, (await adapter.RefreshAsync(Elevator)).CurrentFloor);
    }

    [Fact]
    public async Task Disconnect_FailsWaiterAndPreservesLastKnownDataWithoutReplayingCommand()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        transport.Receive("{\"current_floor\":1,\"state\":\"DOOR_CLOSED\"}");
        var call = adapter.CallAsync(Elevator, 2, TimeSpan.FromSeconds(5));
        transport.SetConnected(false);
        await Assert.ThrowsAsync<IOException>(() => call);
        Assert.False(adapter.GetSnapshot(Elevator).IsConnected);
        Assert.Equal(1, adapter.GetSnapshot(Elevator).CurrentFloor);
        Assert.Equal(ElevatorCommandOutcome.Failed, adapter.GetSnapshot(Elevator).LastCommand!.Outcome);
        transport.SetConnected(true);
        Assert.Single(transport.Commands);
    }

    [Fact]
    public async Task TimeoutAndCancellation_DoNotRetryAndReleaseCommandSlot()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        await Assert.ThrowsAsync<TimeoutException>(() => adapter.CallAsync(Elevator, 1, TimeSpan.FromMilliseconds(50)));
        Assert.Equal(ElevatorCommandOutcome.TimedOut, adapter.GetSnapshot(Elevator).LastCommand!.Outcome);
        using var cancellation = new CancellationTokenSource();
        var call = adapter.CallAsync(Elevator, 2, TimeSpan.FromSeconds(5), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Equal(ElevatorCommandOutcome.Cancelled, adapter.GetSnapshot(Elevator).LastCommand!.Outcome);
        Assert.Equal(2, transport.Commands.Count);

        transport.OnPublish = _ => transport.Receive("{\"current_floor\":1,\"state\":\"ARRIVED\"}");
        await adapter.CallAsync(Elevator, 1, TimeSpan.FromSeconds(1));
        Assert.Equal(3, transport.Commands.Count);
    }

    [Fact]
    public async Task Commands_AreSerializedUntilPreviousStatusArrives()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        var first = adapter.CallAsync(Elevator, 1, TimeSpan.FromSeconds(5));
        var second = adapter.CallAsync(Elevator, 2, TimeSpan.FromSeconds(5));
        Assert.Single(transport.Commands);
        var secondSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        transport.OnPublish = command =>
        {
            if (command.DestinationFloor == 2)
            {
                transport.Receive("{\"current_floor\":2,\"state\":\"ARRIVED\"}");
                secondSent.TrySetResult();
            }
        };
        transport.Receive("{\"current_floor\":1,\"state\":\"ARRIVED\"}");
        await first.WaitAsync(TimeSpan.FromSeconds(2));
        await secondSent.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(2, (await second).CurrentFloor);
    }

    [Fact]
    public async Task WrongPhysicalElevatorOrFloor_IsRejectedBeforePublish()
    {
        var transport = new FakeTransport();
        using var adapter = Adapter(transport);
        await Assert.ThrowsAsync<ArgumentException>(() => adapter.CallAsync("other", 1, TimeSpan.FromSeconds(1)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => adapter.OpenDoorAsync(Elevator, 3));
        Assert.Empty(transport.Commands);
        transport.SetConnected(false);
        await Assert.ThrowsAsync<IOException>(() => adapter.RefreshAsync(Elevator));
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
