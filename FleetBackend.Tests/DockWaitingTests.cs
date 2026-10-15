using System.Net.WebSockets;
using System.Text.Json;
using FleetBackend.Models;
using FleetBackend.Services;
using FleetBackend.Services.Map;
using FleetBackend.Services.Task;

namespace FleetBackend.Tests;

public class DockWaitingTests
{
    [Fact]
    public void ReleaseDock_MakesDockAvailableBeforeNotifyingWaiters()
    {
        var docks = new DockManager();
        var occupied = Fill(docks, "room1");
        MapPointDto? acquired = null;
        docks.OnDockReleased += node => acquired = docks.AcquireDock(node);

        Assert.True(docks.ReleaseDock("room1", occupied[0].PointName));

        Assert.Same(occupied[0], acquired);
    }

    [Fact]
    public void ReleaseDock_DoesNotNotifyForUnoccupiedOrUnknownDock()
    {
        var docks = new DockManager();
        int notifications = 0;
        docks.OnDockReleased += _ => notifications++;

        Assert.False(docks.ReleaseDock("room1", "point1"));
        Assert.False(docks.ReleaseDock("room1", "unknown"));
        Assert.False(docks.ReleaseDock("unknown", "point1"));
        Assert.Equal(0, notifications);
    }

    [Theory]
    [InlineData("room1")]
    [InlineData("room2")]
    public void Executor_KeepsWaitingAfterAnUnrelatedNodeIsReleased(string nodeName)
    {
        var fleet = new TestFleet();
        var occupied = Fill(fleet.Map.Dock, nodeName);
        var unrelated = Assert.IsType<MapPointDto>(fleet.Map.Dock.AcquireDock("room3"));
        var robot = fleet.StartWaiting("R1", nodeName);
        int moves = fleet.Gateway.MoveCount("R1");

        fleet.Map.Dock.ReleaseDock("room3", unrelated.PointName);

        Assert.Null(robot.CurrentDock);
        Assert.Equal(moves, fleet.Gateway.MoveCount("R1"));

        fleet.Map.Dock.ReleaseDock(nodeName, occupied[0].PointName);

        Assert.Same(occupied[0], robot.CurrentDock);
        Assert.Equal(nodeName, robot.CurrentNode!.NodeName);
        Assert.Equal(moves + 1, fleet.Gateway.MoveCount("R1"));
    }

    [Theory]
    [InlineData("room1")]
    [InlineData("room2")]
    public void Executor_KeepsWaitingWhenAnotherRobotTakesTheReleasedDock(string nodeName)
    {
        var fleet = new TestFleet();
        var occupied = Fill(fleet.Map.Dock, nodeName);
        var first = fleet.StartWaiting("R1", nodeName);
        var second = fleet.StartWaiting("R2", nodeName);
        int firstMoves = fleet.Gateway.MoveCount("R1");
        int secondMoves = fleet.Gateway.MoveCount("R2");

        fleet.Map.Dock.ReleaseDock(nodeName, occupied[0].PointName);

        Assert.Same(occupied[0], first.CurrentDock);
        Assert.Null(second.CurrentDock);
        Assert.Equal(secondMoves, fleet.Gateway.MoveCount("R2"));

        fleet.Map.Dock.ReleaseDock(nodeName, occupied[1].PointName);

        Assert.Same(occupied[1], second.CurrentDock);
        Assert.Equal(firstMoves + 1, fleet.Gateway.MoveCount("R1"));
        Assert.Equal(secondMoves + 1, fleet.Gateway.MoveCount("R2"));
    }

    [Fact]
    public void Executor_DoesNotMissAReleaseDuringTheInitialAcquireAttempt()
    {
        var docks = new ReleaseDuringAcquireDockManager();
        var occupied = Fill(docks, "room1");
        var fleet = new TestFleet(docks);
        docks.OnFailedAcquire = () => docks.ReleaseDock("room1", occupied[0].PointName);

        var robot = fleet.StartWaiting("R1", "room1");

        Assert.Same(occupied[0], robot.CurrentDock);
        Assert.Equal(2, fleet.Gateway.MoveCount("R1"));
    }

    private static List<MapPointDto> Fill(IDockManager docks, string nodeName)
    {
        Assert.NotEmpty(docks.GetDocks(nodeName));
        var occupied = new List<MapPointDto>();
        while (docks.AcquireDock(nodeName) is { } dock)
            occupied.Add(dock);
        return occupied;
    }

    private sealed class TestFleet
    {
        public MapManager Map { get; }
        public RecordingGateway Gateway { get; } = new();
        private readonly RobotManager _robots;
        private readonly TaskManager _tasks;
        private readonly JsonSerializerOptions _options = new();

        public TestFleet(IDockManager? docks = null)
        {
            Map = new MapManager(new GraphManager(), docks ?? new DockManager());
            var events = new EventBus();
            _robots = new RobotManager(events, Map, Gateway, _options);
            _tasks = new TaskManager(Map, events, Gateway, _options);
        }

        public Robot StartWaiting(string robotId, string nodeName)
        {
            _robots.RegisterRobot(new RobotStateDto { RobotId = robotId, Battery = 100 });
            var robot = _robots.GetRobot(robotId)!;
            var task = _tasks.CreateTask(new DeliveryTask
            {
                PickupLocation = "room1",
                Destination = "room2"
            });
            _tasks.AssignTaskToRobot(task, robot.State);
            var executor = new TaskExecutor(task, _robots, Map, _tasks);
            executor.Execute();
            executor.OnRobotArrived();
            if (nodeName == "room2")
            {
                int moveCount = Gateway.MoveCount(robotId);
                executor.OnRobotArrived();
                Assert.True(SpinWait.SpinUntil(
                    () => Gateway.MoveCount(robotId) > moveCount,
                    TimeSpan.FromSeconds(10)));
                executor.OnRobotArrived();
            }
            return robot;
        }
    }

    private sealed class RecordingGateway : ICommunicationGateway
    {
        private readonly List<SocketMessage> _commands = new();
        public SystemMode SystemMode { get; set; } = SystemMode.Simulation;
        public int MoveCount(string robotId) => _commands.Count(message =>
            message.Type == SocketMessageType.MoveRobot && message.RobotId == robotId);
        public void RegisterUnitySocket(Guid connectionId, WebSocket socket) { }
        public void RegisterRobotSocket(Guid connectionId, WebSocket socket) { }
        public void RemoveSocket(Guid connectionId) { }
        public void SetRobotIdToSocket(string robotId, Guid connectionId) { }
        public Task ResetRobotIdSockets() => Task.CompletedTask;
        public Task SendDashboardAsync(SocketMessage message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task SendCommandAsync(SocketMessage message, CancellationToken cancellationToken = default)
        {
            _commands.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ReleaseDuringAcquireDockManager : IDockManager
    {
        private readonly DockManager _inner = new();
        public Action? OnFailedAcquire { get; set; }
        public event Action<string>? OnDockReleased
        {
            add => _inner.OnDockReleased += value;
            remove => _inner.OnDockReleased -= value;
        }
        public MapPointDto? AcquireDock(string nodeName)
        {
            var dock = _inner.AcquireDock(nodeName);
            if (dock == null && OnFailedAcquire is { } release)
            {
                OnFailedAcquire = null;
                release();
            }
            return dock;
        }
        public void Clear() => _inner.Clear();
        public bool ReleaseDock(string nodeName, string dockName) => _inner.ReleaseDock(nodeName, dockName);
        public bool IsNodeFull(string nodeName) => _inner.IsNodeFull(nodeName);
        public MapPointDto? GetDock(string nodeName, string dockName) => _inner.GetDock(nodeName, dockName);
        public IEnumerable<MapPointDto> GetDocks(string nodeName) => _inner.GetDocks(nodeName);
    }
}
