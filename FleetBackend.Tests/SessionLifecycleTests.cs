using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using FleetBackend.Models;
using FleetBackend.Services;
using FleetBackend.Services.Map;
using Xunit;

namespace FleetBackend.Tests
{
    public class SessionLifecycleTests
    {
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly IEventBus _eventBus;
        private readonly IGraphManager _graphManager;
        private readonly IDockManager _dockManager;
        private readonly IMapManager _mapManager;
        private readonly ICommunicationGateway _gateway;
        private readonly IRobotManager _robotManager;
        private readonly ITaskManager _taskManager;
        private readonly ITrafficManager _trafficManager;
        private readonly ITaskExecuteManager _taskExecuteManager;
        private readonly ISessionManager _sessionManager;
        private readonly SetSystemModeHandler _setSystemModeHandler;
        private readonly RegisterRobotHandler _registerRobotHandler;
        private readonly SyncRobotStateHandler _syncRobotStateHandler;

        public SessionLifecycleTests()
        {
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            };

            _eventBus = new EventBus();
            _graphManager = new GraphManager();
            _dockManager = new DockManager();
            _mapManager = new MapManager(_graphManager, _dockManager);
            _gateway = new CommunicationGateway(_jsonOptions);
            _robotManager = new RobotManager(_eventBus, _mapManager, _gateway, _jsonOptions);
            _taskManager = new TaskManager(_mapManager, _eventBus, _gateway, _jsonOptions);
            _trafficManager = new TrafficManager();
            _taskExecuteManager = new TaskExecuteManager(_robotManager, _mapManager, _taskManager);

            _sessionManager = new SessionManager(
                _robotManager,
                _taskManager,
                _trafficManager,
                _mapManager,
                _taskExecuteManager,
                _gateway);

            _setSystemModeHandler = new SetSystemModeHandler(_gateway, _sessionManager);
            _registerRobotHandler = new RegisterRobotHandler(_robotManager);
            _syncRobotStateHandler = new SyncRobotStateHandler(_robotManager, _gateway);
        }

        [Fact]
        public async Task Scenario1_NormalStartupAndRegistration()
        {
            // 1. Send SetSystemMode(Operation)
            var modeMessage = new SocketMessage
            {
                Type = SocketMessageType.SetSystemMode,
                RequestId = "req-1",
                Payload = JsonSerializer.SerializeToElement(SystemMode.Operation, _jsonOptions)
            };

            var modeResponse = await _setSystemModeHandler.HandleAsync(modeMessage, _jsonOptions);
            Assert.NotNull(modeResponse);
            Assert.Equal(SocketMessageType.ServerResponse, modeResponse.Type);

            string sessionId = _sessionManager.CurrentSessionId;
            Assert.False(string.IsNullOrWhiteSpace(sessionId));
            Assert.Equal(SystemMode.Operation, _sessionManager.CurrentMode);

            // 2. Register Robot
            var regMessage = new SocketMessage
            {
                Type = SocketMessageType.RegisterRobot,
                RequestId = "req-2",
                Payload = JsonSerializer.SerializeToElement(new RobotStateDto
                {
                    RobotId = "Robot_01",
                    Battery = 100,
                    Status = RobotStatus.Idle
                }, _jsonOptions)
            };

            var regResponse = await _registerRobotHandler.HandleAsync(regMessage, _jsonOptions);
            Assert.NotNull(regResponse);

            var registeredRobot = _robotManager.GetRobot("Robot_01");
            Assert.NotNull(registeredRobot);
            Assert.Equal("Robot_01", registeredRobot.State.RobotId);
            Assert.Equal(RobotStatus.Idle, registeredRobot.State.Status);
        }

        [Fact]
        public void Scenario2_StartSecondSession_ClearsOldRuntimeAndAllowsReRegistration()
        {
            // Session 1: Register Robot and create task
            _sessionManager.StartNewSession(SystemMode.Operation);
            string session1Id = _sessionManager.CurrentSessionId;

            _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_01", Battery = 90, Status = RobotStatus.Idle });
            Assert.NotNull(_robotManager.GetRobot("Robot_01"));

            var firstNode = _graphManager.GetNodesBy(NodeType.Room).First().NodeName;
            var secondNode = _graphManager.GetNodesBy(NodeType.Room).Last().NodeName;
            var task = _taskManager.CreateTask(new DeliveryTask
            {
                PickupLocation = firstNode,
                Destination = secondNode,
                Priority = 1
            });
            Assert.Contains(_taskManager.GetPendingTasks(), t => t.TaskId == task.TaskId);

            // Start Session 2
            _sessionManager.StartNewSession(SystemMode.Operation);
            string session2Id = _sessionManager.CurrentSessionId;

            Assert.NotEqual(session1Id, session2Id);

            // Invariant verification: old runtime state is gone
            Assert.Null(_robotManager.GetRobot("Robot_01"));
            Assert.Empty(_robotManager.GetAllRobots());
            Assert.Empty(_taskManager.GetPendingTasks());
            Assert.Empty(_gateway.RobotSockets);

            // Robot_01 re-registers into Session 2
            _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_01", Battery = 85, Status = RobotStatus.Idle });
            var reRegisteredRobot = _robotManager.GetRobot("Robot_01");
            Assert.NotNull(reRegisteredRobot);
            Assert.Equal("Robot_01", reRegisteredRobot.State.RobotId);
            Assert.Equal(85, reRegisteredRobot.State.Battery);
        }

        [Fact]
        public async Task Scenario3_RobotSendsRobotStateBeforeReRegistration_HandledSafely()
        {
            // Session 1: Register robot
            _sessionManager.StartNewSession(SystemMode.Operation);
            _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_01", Battery = 100 });
            Assert.NotNull(_robotManager.GetRobot("Robot_01"));

            // Start Session 2 without re-registering Robot_01
            _sessionManager.StartNewSession(SystemMode.Operation);
            Assert.Null(_robotManager.GetRobot("Robot_01"));

            // Robot sends RobotState before re-registering
            var stateMessage = new SocketMessage
            {
                Type = SocketMessageType.RobotState,
                Payload = JsonSerializer.SerializeToElement(new RobotStateDto
                {
                    RobotId = "Robot_01",
                    X = 10,
                    Y = 20,
                    Battery = 95
                }, _jsonOptions)
            };

            // Must NOT throw KeyNotFoundException and must safely ignore
            var exception = await Record.ExceptionAsync(() => _syncRobotStateHandler.HandleAsync(stateMessage, _jsonOptions));
            Assert.Null(exception);

            // Robot remains unregistered in current session
            Assert.Null(_robotManager.GetRobot("Robot_01"));
        }

        [Fact]
        public void Scenario4_MultipleRobotsSynchronization_NoIdCollisions()
        {
            _sessionManager.StartNewSession(SystemMode.Operation);

            // Register multiple robots
            var id1 = _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_01" });
            var id2 = _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_02" });
            var id3 = _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_03" });

            Assert.Equal("Robot_01", id1);
            Assert.Equal("Robot_02", id2);
            Assert.Equal("Robot_03", id3);
            Assert.Equal(3, _robotManager.GetAllRobots().Count());

            // Start Session 2
            _sessionManager.StartNewSession(SystemMode.Operation);
            Assert.Empty(_robotManager.GetAllRobots());

            // Synchronize all 3 robots into Session 2
            _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_01" });
            _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_02" });
            _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_03" });

            // Also register an anonymous robot to verify ID generation doesn't collide
            var id4 = _robotManager.RegisterRobot(new RobotStateDto());
            Assert.Equal("Robot_04", id4);

            Assert.Equal(4, _robotManager.GetAllRobots().Count());
            Assert.NotNull(_robotManager.GetRobot("Robot_01"));
            Assert.NotNull(_robotManager.GetRobot("Robot_02"));
            Assert.NotNull(_robotManager.GetRobot("Robot_03"));
            Assert.NotNull(_robotManager.GetRobot("Robot_04"));
        }

        [Fact]
        public void Scenario5_DuplicateSetSystemModeCalls_CreatesCleanSessionsWithoutDuplication()
        {
            var session1 = _sessionManager.StartNewSession(SystemMode.Operation);
            _robotManager.RegisterRobot(new RobotStateDto { RobotId = "Robot_01" });

            var session2 = _sessionManager.StartNewSession(SystemMode.Operation);
            Assert.NotEqual(session1, session2);
            Assert.Empty(_robotManager.GetAllRobots());

            var session3 = _sessionManager.StartNewSession(SystemMode.Simulation);
            Assert.NotEqual(session2, session3);
            Assert.Equal(SystemMode.Simulation, _sessionManager.CurrentMode);
            Assert.Empty(_robotManager.GetAllRobots());
            Assert.Empty(_gateway.RobotSockets);
        }
    }
}
