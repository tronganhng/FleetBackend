# Elevator integration contract

## Scope

The backend implements MQTT/Simulation adapters, cross-floor traversal, robot
floor-readiness handlers and recovery. Simulation behavior remains: arrival at a
floor is sufficient to enter or exit. Operation explicitly opens/closes doors and
waits for device and robot confirmations. Software tests use fake transports and
a loopback broker; validation against the physical elevator is still outstanding.

Only the backend will send elevator commands in Operation. Disable elevator MQTT
commands in the Python robot manager when enabling backend control.

## MQTT

Configuration section: `ElevatorMqtt` in `appsettings.json`, bound and validated at
startup through `IOptions<ElevatorMqttOptions>`. This registration opens no network
connection. The confirmed broker is `10.11.57.29:1883`, without credentials or TLS.
Each running MQTT client needs a unique `ClientId`.

Publish UTF-8 JSON to `elevator/request`, QoS 1, retain false. Subscribe to
`elevator/status` before publishing commands, including after reconnect. Status
publisher QoS, retained behavior and heartbeat availability remain unconfirmed.

The wire field `robot_id` is always `robot3`, the TurtleBot namespace. It is not
`SocketMessage.RobotId`. It is fixed in `ElevatorMqttCommand`, rather than mutable
configuration. There is no confirmed elevator ID or correlation ID on MQTT.
One configured broker/topic pair represents one physical elevator, mapped to
`ElevatorMqtt:ElevatorId` (default `Elevator_01`). Requests for another ID fail.

| Factory | Wire command | Completion evidence |
| --- | --- | --- |
| `GetStatus()` | `GET_STATUS` | Status update; no distinct acknowledgement |
| `CallElevator(1 or 2)` | `CALL_ELEVATOR` | `ARRIVED` with the requested floor |
| `OpenDoor()` | `OPEN_DOOR` | `DOOR_OPEN` at the established floor |
| `CloseDoor()` | `CLOSE_DOOR` | `DOOR_CLOSED` at the established floor |

Example: `{"robot_id":"robot3","command":"CALL_ELEVATOR","destination_floor":2}`.
Commands other than `CALL_ELEVATOR` omit `destination_floor`.
The user confirmed that the door stays open until `CLOSE_DOOR` is sent.

`ElevatorMqttStatusPayload` represents one received message. Its nullable fields
allow partial updates; unknown state strings and extra fields are preserved.
Missing and explicit-null fields both deserialize to null. If that distinction
is needed by the adapter, inspect the original JSON with `JsonDocument`.
Never infer an arrival by combining a new floor with an old cached `ARRIVED`.
MQTT publish completion does not mean the physical action completed. Fresh local
receipt times alone cannot prove request correlation without device sequence IDs.

## Adapter implementation (step 2)

`ElevatorAdapterResolver.ForMode(mode)` selects an `IElevatorAdapter`. Resolve it
once for a traversal. The interface exposes refresh, call, open and close methods,
returning an immutable `ElevatorSnapshot` after confirmation or throwing on
timeout, cancellation, unavailable transport or publish failure. Call timeouts are
supplied explicitly so pickup and travel can use different settings.

`MqttElevatorTransport` is a hosted MQTTnet service. `Enabled` defaults to false;
even when enabled it connects only in Operation. It reconnects with a configured
delay, subscribes at QoS 1, checks SUBACK, and sends `GET_STATUS` on every successful
connection. No physical command is automatically replayed. Mode changes and host
shutdown disconnect the client. The worker does not connect to the broker in
Simulation. Subscribe QoS 1 does not establish the controller publisher's QoS.

`MqttElevatorAdapter` preserves raw JSON, unknown states, receive timestamps and
retained flags. Malformed messages are logged and ignored without stopping the
receiver. Partial fields update cache independently. Completion requires a new,
non-retained message: calls require floor and `ARRIVED` together; door commands
conservatively require the expected floor and door state together. A state-only
door message updates the cache but cannot complete the command until the actual
device's partial-message semantics are established. Refresh accepts either valid
field. This avoids treating an old cached floor as current confirmation.

Commands are serialized per adapter instance with the waiter installed before
publish. `LastCommand` records pending/completed/timed-out/cancelled/failed
separately from the observed state. Disconnect fails a pending operation and
preserves last-known data. `IsConnected` means the MQTT connection and subscription
are ready; it does not prove controller liveness or freshness. Timeouts/cancellation
do not stop physical motion. Trip ownership and recovery are managed by the
traversal/traffic layer. Run a single backend process for this physical elevator;
neither the in-process lock nor the local journal provides distributed locking.

`SimulationElevatorAdapter` sends the existing Unity `MoveElevator` message and
waits for `ElevatorArrived`. Its door methods are no-ops; refresh reads the
registered simulation state. Its connection flag represents registered simulation
availability, not a Unity socket health check. `ElevatorArrivedHandler` forwards
simulation updates to the adapter and ignores them in Operation.

`TaskExecutor` selects the configured physical `ElevatorId` in Operation, so no
Unity elevator registration is needed there. Simulation still requires exactly
one registered elevator. Both modes require one logical elevator node with a
cabin point on each involved floor. Physical observations remain separate from
legacy `Idle/Moving` simulation DTOs. The gateway rejects legacy Operation
`MoveElevator`; traversal uses the MQTT adapter directly instead.

MQTTnet API references: [client subscription example](https://github.com/dotnet/MQTTnet/blob/master/Samples/Client/Client_Subscribe_Samples.cs)
and [client publishing example](https://github.com/dotnet/MQTTnet/blob/master/Samples/Client/Client_Publish_Samples.cs).
The loopback integration test uses MQTTnet.Server as a test-only dependency; it
checks QoS/retain, status receipt, disconnect/reconnect without replay and mode
isolation without reaching the configured physical broker.

Timeouts are configurable in seconds. Pickup (60), travel (120) and door (15)
defaults follow the source client. Status (15) and robot floor readiness (120)
are backend defaults, not device guarantees.

## Backend to robot: change floor/map

The following WebSocket messages are new backend contracts, not MQTT commands.
They use the existing serializer's PascalCase properties and string enum names.

Backend request:

```json
{
  "Type": "ChangeRobotFloor",
  "RequestId": "unique-floor-change-operation",
  "RobotId": "backend-robot-id",
  "Payload": { "ElevatorId": "Elevator_01", "TargetFloor": 2 }
}
```

The robot maps `TargetFloor` to its own navigation map and cabin localization
configuration. Map loading, initial pose and localization checks belong to the
robot. The backend sends this request while the robot is in the cabin at the
destination floor, after the door is confirmed open.

Robot reply after map loading AND localization are complete:

```json
{
  "Type": "RobotFloorReady",
  "RequestId": "unique-floor-change-operation",
  "RobotId": "backend-robot-id",
  "Payload": { "ElevatorId": "Elevator_01", "TargetFloor": 2, "IsReady": true }
}
```

On failure, send `IsReady: false` with `ErrorMessage`. The handler validates the
bound robot socket, robot ID, operation ID, elevator and target floor against the
active request. Stale/duplicate replies are ignored and exit requires `IsReady: true`.
The robot should handle repeated requests for the same operation idempotently.
No exit command is issued after failure or timeout pending recovery.

`RobotArrived` remains the confirmation for boarding and exiting, interpreted
according to the active traversal step. It is not a floor/map readiness signal.
New enum members are appended to preserve existing numeric message values.
The existing `RobotArrived` contract does not correlate individual movement
commands: duplicates in one waiting step are harmless, but a delayed arrival from
an older movement cannot be reliably identified. Robot clients must emit arrival
only for the current completed movement; adding movement correlation would be a
separate protocol extension.

## Operation sequence and timeouts

1. Approach the source elevator node and wait for `RobotArrived`.
2. Acquire exclusive traffic access. A waiting robot sends no elevator command.
3. `GET_STATUS` must establish a fresh, non-retained full floor/state message.
4. Journal the physical lease. If at another floor, call the pickup floor and
   wait for `ARRIVED`. If already at pickup, skip the same-floor call.
5. Open the door and wait for `DOOR_OPEN`, then move into the cabin.
6. After `RobotArrived`, close the door and wait for `DOOR_CLOSED`.
7. Call the destination, wait for `ARRIVED`, open and wait for `DOOR_OPEN`.
8. Send `ChangeRobotFloor` and wait for matching `RobotFloorReady`. Only then
   update the backend robot floor and command exit.
9. After exit `RobotArrived`, close the destination door and wait for confirmation.
10. Remove the journal entry, release traffic access and continue the delivery.

Access and robot movements default to 120 seconds each. Floor/map readiness also
defaults to 120 seconds. These are independent from status, door, pickup and travel
timeouts. Cancellation or failure sends a best-effort robot stop; the elevator
protocol has no confirmed stop/cancel command. No door close is automatically
issued on boarding, map-change or exit failure.

## Durable ownership and manual recovery

`ElevatorRecoveryStore` writes `RecoveryFilePath` (default
`Data/elevator-recovery.json`) before the first physical command. The journal is
flushed and replaced atomically; keep this file on persistent local storage and
do not delete it to clear a lock. A malformed or unreadable journal prevents
startup. A write failure prevents new hardware commands and keeps the in-memory
block. A normal completed trip removes the entry only after the final closed-door
confirmation.

Interrupted physical trips remain blocked even if the robot had not yet entered.
Session reset cancels executors before changing mode, preserves physical traffic
ownership/journal entries and discards old waiters. On restart, journal entries
become inactive recovery records; they never resume a trip automatically. A robot
associated with an unresolved entry registers/reconnects in `Error` rather than
being scheduled again. Simulation ownership can be cleared by a session reset.

Inspect recovery entries over the existing WebSocket:

```json
{ "Type": "GetElevatorRecovery", "RequestId": "inspect-1", "Payload": {} }
```

The server replies with `ServerResponse`, the matching request ID and an array
containing `ElevatorId`, `RobotId`, `StartedAtUtc`, `IsActive` and `Reason`.

After cancelling the active task and physically reconciling the elevator and
robot, an operator can explicitly confirm the cabin is empty:

```json
{
  "Type": "RecoverElevator",
  "RequestId": "recover-1",
  "Payload": { "ElevatorId": "Elevator_01", "CabinConfirmedEmpty": true }
}
```

Recovery requires Operation mode and an inactive recovery entry. It sends only
`GET_STATUS`, requires a fresh full `DOOR_CLOSED` status with a valid floor, then
removes the journal entry and releases the protected resource. Retained, partial,
open-door or disconnected status cannot unlock the elevator. The result is
`{ "Success": true, "Status": ... }` or `{ "Success": false, "Error": ... }`.
Recovery does not automatically change an errored robot to idle or resume a task.
Re-register the reconciled robot with its actual floor/status before assigning
new work. The existing WebSocket service has no operator authentication/roles;
the recovery interface inherits that deployment limitation.

## Running tests and physical acceptance (step 5)

Run `dotnet test FleetBackend.Tests/FleetBackend.Tests.csproj`. The suite exercises
the complete Simulation/Operation sequence, delivery through message handlers,
multiple robots, stale map replies, map failure/timeouts, disconnect during
boarding, manual recovery and journal survival across session/process resets.
The real MQTTnet transport is tested against a broker on loopback, including
subscription, QoS/retain, reconnect and mode isolation.

Before the physical acceptance run:

1. Disable elevator MQTT publishing in Python. Keep robot navigation, heartbeat,
   map loading and localization active. Implement the floor-change messages above.
2. Confirm the logical elevator/cabin/outside points match each robot floor map.
   The supplied logical map coordinates are not a calibrated physical deployment.
3. Set `ElevatorMqtt:Enabled=true` via deployment configuration (or environment
   variable `ElevatorMqtt__Enabled=true`), retain the configured broker
   `10.11.57.29:1883`, and use a unique client ID. Default config remains disabled.
4. Register the dashboard as `Unity` on `/ws`, then send
   `{"Type":"SetSystemMode","Payload":"Operation"}`. Register the robot client
   as `Robot` and send `RegisterRobot` after this session change; prior socket
   robot-ID bindings are reset. Maintain heartbeat telemetry during the trip.
5. Confirm fresh controller statuses contain floor and state together for arrival
   and door events. Observe a 1-to-2 trip, then a 2-to-1 trip, checking every door
   and robot acknowledgement before the next command. Also check pickup when
   the cabin starts at the other floor and when already at pickup.
6. Verify queueing with a second robot, then controlled interruption/reconnect and
   operator recovery. Confirm no interrupted command is replayed, no exit occurs
   before localization readiness, and ownership is held through final door close.

These physical checks have not been executed by the software test suite.

Verify the robot implements the floor-change contract, only one command owner is
active, and actual controller status matches the source document. Determine
controller behavior for same-floor calls and repeated commands. MQTT reconnect
or an application session reset must not automatically replay physical commands
or discard unresolved cabin ownership. The implementation preserves those blocks;
device-specific behavior and the Python/robot implementation still need acceptance.
