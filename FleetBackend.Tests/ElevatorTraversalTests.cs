using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using FleetBackend.Models;
using FleetBackend.Models.ElevatorMqtt;
using FleetBackend.Options;
using FleetBackend.Services;
using FleetBackend.Services.Elevator;
using FleetBackend.Services.Map;
using FleetBackend.Services.Task;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using FleetBackend.Extensions;

namespace FleetBackend.Tests;

public sealed class ElevatorTraversalTests
{
    [Fact]
    public async Task Operation_FullTripWaitsForDoorsMapAndFinalClose()
    {
        using var fleet = new Fleet();
        var trip = fleet.Trip("R1");
        var mapRequest = await fleet.ReachMapChange(trip);
        Assert.True(fleet.Store.IsBlocked(Fleet.Elevator));
        Assert.Equal(1, trip.Robot.State.Floor); // Map not ready yet.
        Assert.False(fleet.Traffic.RequestAccess("R2", Fleet.Resource));
        fleet.Traffic.ReleaseAccess("R1", Fleet.Resource); // Generic release cannot bypass cabin protection.
        Assert.False(fleet.Traffic.RequestAccess("R2", Fleet.Resource));

        trip.Traversal.OnRobotFloorReady("stale", Ready());
        trip.Traversal.OnRobotFloorReady(mapRequest.RequestId!, Ready() with { TargetFloor = 1 });
        trip.Traversal.OnRobotFloorReady(mapRequest.RequestId!, Ready() with { ElevatorId = "other" });
        trip.Traversal.OnRobotArrived(); // Not a map readiness signal.
        Assert.False(fleet.Gateway.Messages.Reader.TryRead(out _));

        trip.Traversal.OnRobotFloorReady(mapRequest.RequestId!, Ready());
        await fleet.Move("R1", fleet.Destination.Position);
        Assert.Equal(2, trip.Robot.State.Floor);
        Assert.True(fleet.Store.IsBlocked(Fleet.Elevator));
        trip.Traversal.OnRobotArrived();
        await fleet.Command("CLOSE_DOOR");
        Assert.False(trip.Completed.Task.IsCompleted);
        Assert.False(fleet.Traffic.RequestAccess("R2", Fleet.Resource));
        fleet.Transport.Receive(2, "DOOR_CLOSED");
        await trip.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(fleet.Store.GetAll());
        Assert.True(fleet.Traffic.RequestAccess("R2", Fleet.Resource));
        Assert.False(trip.Failed.Task.IsCompleted);
    }

    [Fact]
    public async Task PickupFromOtherFloor_CallsSourceBeforeOpeningDoor()
    {
        using var fleet = new Fleet();
        var trip = fleet.Trip("R1");
        trip.Traversal.Start();
        await fleet.Move("R1", fleet.Source.Position);
        trip.Traversal.OnRobotArrived();
        await fleet.Command("GET_STATUS");
        fleet.Transport.Receive(2, "DOOR_CLOSED");
        var pickup = await fleet.Command("CALL_ELEVATOR");
        Assert.Equal(1, pickup.DestinationFloor);
        Assert.False(fleet.Gateway.Messages.Reader.TryRead(out _));
        fleet.Transport.Receive(1, "ARRIVED");
        await fleet.Command("OPEN_DOOR");
        Assert.False(fleet.Gateway.Messages.Reader.TryRead(out _));
        fleet.Transport.Receive(1, "DOOR_OPEN");
        await fleet.Move("R1", fleet.Source.MapPoints[0].Position);
    }

    [Fact]
    public async Task Simulation_DoesNotNeedDoorOrMapMessages()
    {
        using var fleet = new Fleet(simulation: true);
        var trip = fleet.Trip("R1");
        trip.Traversal.Start();
        await fleet.Move("R1", fleet.Source.Position);
        trip.Traversal.OnRobotArrived();
        var pickup = await fleet.Next(SocketMessageType.MoveElevator);
        Assert.Equal(1, pickup.Payload.Deserialize<MoveElevatorRequest>()!.TargetFloor);
        fleet.Simulation.NotifyArrived(new() { ElevatorId = Fleet.Elevator, CurrentFloor = 1, Status = ElevatorStatus.Idle });
        await fleet.Move("R1", fleet.Source.MapPoints[0].Position);
        trip.Traversal.OnRobotArrived();
        var ride = await fleet.Next(SocketMessageType.MoveElevator);
        Assert.Equal(2, ride.Payload.Deserialize<MoveElevatorRequest>()!.TargetFloor);
        fleet.Simulation.NotifyArrived(new() { ElevatorId = Fleet.Elevator, CurrentFloor = 2, Status = ElevatorStatus.Idle });
        await fleet.Move("R1", fleet.Destination.Position);
        trip.Traversal.OnRobotArrived();
        await trip.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Empty(fleet.Store.GetAll());
        Assert.False(fleet.Transport.Commands.Reader.TryRead(out _));
        Assert.False(fleet.Gateway.Messages.Reader.TryRead(out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MapFailureOrTimeout_HoldsCabinAndDoesNotSendExit(bool timeout)
    {
        using var fleet = new Fleet();
        fleet.Options.RobotFloorReadyTimeoutSeconds = 1;
        var trip = fleet.Trip("R1");
        var request = await fleet.ReachMapChange(trip);
        if (!timeout) trip.Traversal.OnRobotFloorReady(request.RequestId!, Ready() with { IsReady = false, ErrorMessage = "AMCL failed" });
        await trip.Failed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var entry = Assert.Single(fleet.Store.GetAll());
        Assert.False(entry.IsActive);
        Assert.False(fleet.Traffic.RequestAccess("R2", Fleet.Resource));
        Assert.False(fleet.Gateway.Messages.Reader.TryRead(out _));
        Assert.False(fleet.Transport.Commands.Reader.TryRead(out _));
    }

    [Fact]
    public async Task DisconnectDuringBoarding_FailsEvenWithoutPendingElevatorCommand()
    {
        using var fleet = new Fleet();
        var trip = fleet.Trip("R1");
        await fleet.ReachBoarding(trip);
        fleet.Transport.Disconnect();
        Assert.Contains("disconnected", await trip.Failed.Task.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.True(fleet.Store.IsBlocked(Fleet.Elevator));
        Assert.False(fleet.Traffic.RequestAccess("R2", Fleet.Resource));
    }

    [Fact]
    public async Task CancellationBeforeCabinStillHoldsPhysicalLease_AndSurvivesRestart()
    {
        using var fleet = new Fleet();
        var trip = fleet.Trip("R1");
        trip.Traversal.Start();
        await fleet.Move("R1", fleet.Source.Position);
        trip.Traversal.OnRobotArrived();
        await fleet.Command("GET_STATUS");
        fleet.Transport.Receive(2, "DOOR_CLOSED");
        await fleet.Command("CALL_ELEVATOR");
        trip.Traversal.Dispose();
        fleet.Traffic.Clear();
        Assert.False(fleet.Traffic.RequestAccess("R2", Fleet.Resource));
        var restarted = new ElevatorRecoveryStore(Microsoft.Extensions.Options.Options.Create(fleet.Options));
        var entry = Assert.Single(restarted.GetAll());
        Assert.False(entry.IsActive);
        Assert.Equal("R1", entry.RobotId);
        Assert.False(fleet.Transport.Commands.Reader.TryRead(out _));
    }

    [Fact]
    public async Task TwoRobots_SecondWaitsUntilFirstFinalDoorClose()
    {
        using var fleet = new Fleet();
        var first = fleet.Trip("R1");
        var request = await fleet.ReachMapChange(first);
        var second = fleet.Trip("R2");
        second.Traversal.Start();
        await fleet.Move("R2", fleet.Source.Position);
        second.Traversal.OnRobotArrived();
        Assert.False(fleet.Transport.Commands.Reader.TryRead(out _));
        first.Traversal.OnRobotFloorReady(request.RequestId!, Ready());
        await fleet.Move("R1", fleet.Destination.Position);
        first.Traversal.OnRobotArrived();
        await fleet.Command("CLOSE_DOOR");
        Assert.False(fleet.Transport.Commands.Reader.TryRead(out _));
        fleet.Transport.Receive(2, "DOOR_CLOSED");
        await first.Completed.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await fleet.Command("GET_STATUS"); // Only now can the second trip start using the elevator.
        Assert.False(second.Failed.Task.IsCompleted);
    }

    [Fact]
    public async Task ManualRecoveryRequiresEmptyCabinAndFreshClosedDoor()
    {
        using var fleet = new Fleet();
        fleet.Store.Begin(Fleet.Elevator, "R1");
        fleet.Store.RequireRecovery(Fleet.Elevator, "R1", "Interrupted");
        Assert.True(fleet.Traffic.RequestAccess("R1", Fleet.Resource));
        fleet.Traffic.ProtectAccess("R1", Fleet.Resource, true);
        var manager = new ElevatorRecoveryManager(fleet.Store, fleet.Resolver, fleet.Traffic, fleet.Gateway);
        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.RecoverAsync(Fleet.Elevator, false, CancellationToken.None));

        var rejected = manager.RecoverAsync(Fleet.Elevator, true, CancellationToken.None);
        await fleet.Command("GET_STATUS");
        fleet.Transport.Receive(2, "DOOR_OPEN");
        await Assert.ThrowsAsync<InvalidOperationException>(() => rejected);
        Assert.True(fleet.Store.IsBlocked(Fleet.Elevator));

        var accepted = manager.RecoverAsync(Fleet.Elevator, true, CancellationToken.None);
        await fleet.Command("GET_STATUS");
        fleet.Transport.Receive(2, "DOOR_CLOSED");
        await accepted;
        Assert.Empty(fleet.Store.GetAll());
        Assert.True(fleet.Traffic.RequestAccess("R2", Fleet.Resource));
        Assert.False(fleet.Transport.Commands.Reader.TryRead(out _)); // Recovery sends no motion/door command.
    }

    [Fact]
    public async Task TaskManagerSessionReset_PreservesRecoveryAndDoesNotResumeOldTrip()
    {
        using var fleet = new Fleet();
        var events = new EventBus();
        var robots = new RobotManager(events, fleet.Map, fleet.Gateway, fleet.Json, fleet.Store);
        robots.RegisterRobot(new() { RobotId = "R1", Battery = 100 });
        robots.GetRobot("R1")!.CurrentNode = fleet.Map.Graph.GetNode("room1");
        var tasks = new TaskManager(fleet.Map, events, fleet.Gateway, fleet.Json);
        var executors = new TaskExecuteManager(robots, fleet.Map, tasks, fleet.Elevators,
            fleet.Traffic, fleet.Gateway, fleet.Json, fleet.Resolver, fleet.Store,
            Microsoft.Extensions.Options.Options.Create(fleet.Options));
        var task = tasks.CreateTask(new() { PickupLocation = "room3", Destination = "room3" });
        tasks.AssignTaskToRobot(task, robots.GetRobot("R1")!.State);
        executors.ExecuteTask(task);
        await fleet.Next(SocketMessageType.ChangeRobotStatus);
        await fleet.Move("R1", fleet.Source.Position);
        executors.OnRobotArrived("R1");
        await fleet.Command("GET_STATUS");
        fleet.Transport.Receive(1, "DOOR_CLOSED");
        await fleet.Command("OPEN_DOOR");
        fleet.Transport.Receive(1, "DOOR_OPEN");
        await fleet.Move("R1", fleet.Source.MapPoints[0].Position);

        var sessions = new SessionManager(robots, fleet.Elevators, tasks, fleet.Traffic, fleet.Map, executors, fleet.Gateway, fleet.Json);
        sessions.StartNewSession(SystemMode.Simulation);
        Assert.False(Assert.Single(fleet.Store.GetAll()).IsActive);
        Assert.False(fleet.Traffic.RequestAccess("R2", Fleet.Resource));
        executors.OnRobotArrived("R1");
        sessions.StartNewSession(SystemMode.Operation);
        Assert.True(fleet.Store.IsBlocked(Fleet.Elevator));
        robots.RegisterRobot(new() { RobotId = "R1", Floor = 2 });
        Assert.Equal(RobotStatus.Error, robots.GetRobot("R1")!.State.Status);
        Assert.Equal(2, robots.GetRobot("R1")!.State.Floor);
        Assert.False(fleet.Transport.Commands.Reader.TryRead(out _));
    }

    private static RobotFloorReadyPayload Ready() => new() { ElevatorId = Fleet.Elevator, TargetFloor = 2, IsReady = true };

    [Fact]
    public async Task TaskExecutor_QueuesSecondDeliveryWhilePhysicalTripIsActive()
    {
        using var fleet = new Fleet();
        var first = fleet.Trip("R1");
        await fleet.ReachBoarding(first);
        var events = new EventBus();
        var robots = new RobotManager(events, fleet.Map, fleet.Gateway, fleet.Json, fleet.Store);
        robots.RegisterRobot(new() { RobotId = "R2", Battery = 100 });
        var robot = robots.GetRobot("R2")!;
        robot.CurrentNode = fleet.Map.Graph.GetNode("room1");
        var tasks = new TaskManager(fleet.Map, events, fleet.Gateway, fleet.Json);
        using var executors = new TaskExecuteManager(robots, fleet.Map, tasks, fleet.Elevators,
            fleet.Traffic, fleet.Gateway, fleet.Json, fleet.Resolver, fleet.Store,
            Microsoft.Extensions.Options.Options.Create(fleet.Options));
        var task = tasks.CreateTask(new() { PickupLocation = "room3", Destination = "room3" });
        tasks.AssignTaskToRobot(task, robot.State);
        executors.ExecuteTask(task);
        await fleet.Next(SocketMessageType.ChangeRobotStatus);
        await fleet.Move("R2", fleet.Source.Position);
        executors.OnRobotArrived("R2");
        Assert.Equal(FleetBackend.Models.TaskStatus.Running, task.Status);
        Assert.False(fleet.Transport.Commands.Reader.TryRead(out _));
        Assert.True(Assert.Single(fleet.Store.GetAll()).IsActive);
    }

    [Fact]
    public void Startup_ResolvesFullDependencyGraphAndRestoresProtectedOwnership()
    {
        using var fleet = new Fleet();
        fleet.Store.Begin(Fleet.Elevator, "R1");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fleet.Json);
        services.AddOptions<ElevatorMqttOptions>().Configure(options => options.RecoveryFilePath = fleet.Options.RecoveryFilePath);
        services.AddFleetCore().AddCommunication().AddMessageHandlers().AddAlgorithms();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        Assert.NotNull(provider.GetRequiredService<MessageRouter>());
        Assert.NotEmpty(provider.GetServices<IHostedService>());
        var traffic = provider.GetRequiredService<ITrafficManager>();
        Assert.False(traffic.RequestAccess("R1", Fleet.Resource));
        Assert.False(traffic.RequestAccess("R2", Fleet.Resource));
        traffic.Clear();
        traffic.ReleaseAccess("R1", Fleet.Resource);
        Assert.False(traffic.RequestAccess("R2", Fleet.Resource));
    }

    [Fact]
    public void CorruptJournal_PreventsSilentRecoveryResetOnStartup()
    {
        using var fleet = new Fleet();
        Directory.CreateDirectory(Path.GetDirectoryName(fleet.Options.RecoveryFilePath)!);
        File.WriteAllText(fleet.Options.RecoveryFilePath, "invalid journal");
        Assert.Throws<JsonException>(() => new ElevatorRecoveryStore(Microsoft.Extensions.Options.Options.Create(fleet.Options)));
    }

    [Fact]
    public async Task Delivery_UsesHandlersAndCompletesAfterCrossFloorTraversal()
    {
        using var fleet = new Fleet();
        var events = new EventBus();
        var robots = new RobotManager(events, fleet.Map, fleet.Gateway, fleet.Json, fleet.Store);
        robots.RegisterRobot(new() { RobotId = "R1", Battery = 100 });
        var robot = robots.GetRobot("R1")!;
        robot.CurrentNode = fleet.Map.Graph.GetNode("room1");
        var tasks = new TaskManager(fleet.Map, events, fleet.Gateway, fleet.Json);
        using var executors = new TaskExecuteManager(robots, fleet.Map, tasks, fleet.Elevators,
            fleet.Traffic, fleet.Gateway, fleet.Json, fleet.Resolver, fleet.Store,
            Microsoft.Extensions.Options.Options.Create(fleet.Options));
        var arrived = new RobotArrivedHandler(executors, fleet.Gateway);
        var ready = new RobotFloorReadyHandler(executors, fleet.Gateway);
        var connection = Guid.NewGuid();
        async Task Arrive() => await arrived.HandleAsync(new()
        {
            Type = SocketMessageType.RobotArrived, RobotId = "R1", ConnectionId = connection
        }, fleet.Json);

        var task = tasks.CreateTask(new() { PickupLocation = "room3", Destination = "room3" });
        tasks.AssignTaskToRobot(task, robot.State);
        executors.ExecuteTask(task);
        await fleet.Next(SocketMessageType.ChangeRobotStatus);
        await fleet.Move("R1", fleet.Source.Position);
        await Arrive();
        await fleet.Command("GET_STATUS");
        fleet.Transport.Receive(1, "DOOR_CLOSED");
        await fleet.Command("OPEN_DOOR");
        fleet.Transport.Receive(1, "DOOR_OPEN");
        await fleet.Move("R1", fleet.Source.MapPoints[0].Position);
        await Arrive();
        await fleet.Command("CLOSE_DOOR");
        fleet.Transport.Receive(1, "DOOR_CLOSED");
        await fleet.Command("CALL_ELEVATOR");
        fleet.Transport.Receive(2, "ARRIVED");
        await fleet.Command("OPEN_DOOR");
        fleet.Transport.Receive(2, "DOOR_OPEN");
        var change = await fleet.Next(SocketMessageType.ChangeRobotFloor);
        var reply = new SocketMessage
        {
            Type = SocketMessageType.RobotFloorReady, RobotId = "R1", RequestId = change.RequestId,
            Payload = JsonSerializer.SerializeToElement(Ready(), fleet.Json)
        };
        await ready.HandleAsync(reply, fleet.Json); // Unbound connection is ignored.
        Assert.Equal(1, robot.State.Floor);
        reply.ConnectionId = connection;
        await ready.HandleAsync(reply, fleet.Json);
        await fleet.Move("R1", fleet.Destination.Position);
        await Arrive();
        await fleet.Command("CLOSE_DOOR");
        fleet.Transport.Receive(2, "DOOR_CLOSED");
        await fleet.Move("R1", fleet.Map.Graph.GetNode("room3").Position);
        Assert.Empty(fleet.Store.GetAll());
        await Arrive();
        await fleet.Move("R1", fleet.Map.Graph.GetNode("room3").MapPoints[0].Position);
        await Arrive();
        await fleet.Move("R1", fleet.Map.Graph.GetNode("room3").Position);
        await Arrive();
        await fleet.Move("R1", fleet.Map.Graph.GetNode("room3").MapPoints[0].Position);
        await Arrive();
        Assert.Equal(FleetBackend.Models.TaskStatus.Completed, task.Status);
        Assert.Equal(RobotStatus.Idle, robot.State.Status);
    }

    private sealed record TripState(Robot Robot, ElevatorTraversal Traversal,
        TaskCompletionSource Completed, TaskCompletionSource<string> Failed);

    private sealed class Fleet : IDisposable
    {
        public const string Elevator = "Elevator_01";
        public const string Resource = "elevator:Elevator_01";
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "fleet-elevator-tests-" + Guid.NewGuid().ToString("N"));
        private readonly List<ElevatorTraversal> _trips = [];
        public JsonSerializerOptions Json { get; } = new();
        public ElevatorMqttOptions Options { get; }
        public ElevatorRecoveryStore Store { get; }
        public TrafficManager Traffic { get; } = new();
        public RecordingGateway Gateway { get; } = new();
        public FakeTransport Transport { get; } = new();
        public MqttElevatorAdapter Mqtt { get; }
        public SimulationElevatorAdapter Simulation { get; }
        public ElevatorAdapterResolver Resolver { get; }
        public ElevatorManager Elevators { get; } = new();
        public MapManager Map { get; } = new(new GraphManager(), new DockManager());
        public MapNodeDto Source => Map.Graph.GetNode("elevator");
        public MapNodeDto Destination => Map.Graph.GetNode("elevator2");

        public Fleet(bool simulation = false)
        {
            Gateway.SystemMode = simulation ? SystemMode.Simulation : SystemMode.Operation;
            Options = new() { Enabled = true, RecoveryFilePath = Path.Combine(_directory, "recovery.json") };
            var options = Microsoft.Extensions.Options.Options.Create(Options);
            Store = new(options);
            Mqtt = new(Transport, options, NullLogger<MqttElevatorAdapter>.Instance);
            Elevators.RegisterElevator(new() { ElevatorId = Elevator });
            Simulation = new(Gateway, Elevators, options, Json);
            Resolver = new(Mqtt, Simulation);
        }

        public TripState Trip(string robotId)
        {
            var robot = new Robot(new() { RobotId = robotId, Floor = 1 }, Map, new EventBus(), Gateway, Json);
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var failed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            var traversal = new ElevatorTraversal(robot, Map, Traffic, Gateway, Resolver.ForMode(Gateway.SystemMode), Store,
                Options, Json, new object(), Source, Destination, Elevator, () => completed.TrySetResult(), reason => failed.TrySetResult(reason));
            _trips.Add(traversal);
            return new(robot, traversal, completed, failed);
        }

        public async Task ReachBoarding(TripState trip)
        {
            trip.Traversal.Start();
            await Move(trip.Robot.State.RobotId, Source.Position);
            trip.Traversal.OnRobotArrived();
            await Command("GET_STATUS");
            Transport.Receive(1, "DOOR_CLOSED");
            await Command("OPEN_DOOR");
            Transport.Receive(1, "DOOR_OPEN");
            await Move(trip.Robot.State.RobotId, Source.MapPoints[0].Position);
        }

        public async Task<SocketMessage> ReachMapChange(TripState trip)
        {
            await ReachBoarding(trip);
            trip.Traversal.OnRobotArrived();
            await Command("CLOSE_DOOR");
            Transport.Receive(1, "DOOR_CLOSED");
            var call = await Command("CALL_ELEVATOR");
            Assert.Equal(2, call.DestinationFloor);
            Transport.Receive(2, "ARRIVED");
            await Command("OPEN_DOOR");
            Transport.Receive(2, "DOOR_OPEN");
            return await Next(SocketMessageType.ChangeRobotFloor);
        }

        public async Task<SocketMessage> Next(SocketMessageType type)
        {
            var message = await Gateway.Messages.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(type, message.Type);
            return message;
        }

        public async Task Move(string robotId, float[] position)
        {
            var message = await Next(SocketMessageType.MoveRobot);
            Assert.Equal(robotId, message.RobotId);
            Assert.Equal(position, message.Payload.Deserialize<float[]>());
        }

        public async Task<ElevatorMqttCommand> Command(string command)
        {
            var message = await Transport.Commands.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(command, message.Command);
            return message;
        }

        public void Dispose()
        {
            foreach (var trip in _trips) trip.Dispose();
            Mqtt.Dispose();
            if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
        }
    }

    private sealed class FakeTransport : IElevatorMqttTransport
    {
        public bool IsReady { get; private set; } = true;
        public event Action<bool>? ConnectionChanged;
        public event Action<ElevatorMqttMessage>? MessageReceived;
        public Channel<ElevatorMqttCommand> Commands { get; } = Channel.CreateUnbounded<ElevatorMqttCommand>();
        public Task PublishAsync(ElevatorMqttCommand command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Commands.Writer.TryWrite(command);
            return Task.CompletedTask;
        }
        public void Receive(int floor, string state) => MessageReceived?.Invoke(new("elevator/status",
            JsonSerializer.Serialize(new { current_floor = floor, state }), false));
        public void Disconnect() { IsReady = false; ConnectionChanged?.Invoke(false); }
    }

    private sealed class RecordingGateway : ICommunicationGateway
    {
        public SystemMode SystemMode { get; set; }
        public Channel<SocketMessage> Messages { get; } = Channel.CreateUnbounded<SocketMessage>();
        public void RegisterUnitySocket(Guid id, WebSocket socket) { }
        public void RegisterRobotSocket(Guid id, WebSocket socket) { }
        public void RemoveSocket(Guid id) { }
        public void SetRobotIdToSocket(string id, Guid connectionId) { }
        public bool IsRobotConnection(string robotId, Guid connectionId) => connectionId != Guid.Empty;
        public Task ResetRobotIdSockets() => Task.CompletedTask;
        public Task SendCommandAsync(SocketMessage message, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Messages.Writer.TryWrite(message);
            return Task.CompletedTask;
        }
        public Task SendDashboardAsync(SocketMessage message, CancellationToken token = default) => Task.CompletedTask;
    }
}
