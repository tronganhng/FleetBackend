# Elevator integration contract

## Scope

The backend implements MQTT/Simulation adapters, cross-floor traversal, robot
floor-readiness handlers and recovery. Simulation behavior remains: arrival at a
floor is sufficient to enter or exit. Operation explicitly opens/closes doors and
waits for device and robot confirmations. Software tests use fake transports and
a loopback broker; validation against the physical elevator is still outstanding.

Only the backend will send elevator commands in Operation. Disable elevator MQTT
commands in the Python robot manager when enabling backend control.

## MQTT protocol v2

The Operation adapter now requires the protocol in
`elevator_manager/docs/mqtt_protocol_v2.md`. Update ESP firmware together with the
client; legacy status messages cannot complete commands. ESP currently simulates
floor/door state; acknowledgements are not physical sensor verification.

Requests on `elevator/request` use QoS 1, retain false, `protocol_version: 2`,
`robot_id: robot3`, a process `session_id`, `trip_id`, unique `request_id`, increasing
`command_seq`, and the observed ESP `boot_id`. Mutations expire after ten seconds
of estimated ESP uptime. `CALL_ELEVATOR` includes `destination_floor` (1/2);
`OPEN_DOOR`/`CLOSE_DOOR` include `expected_floor` (1/2). `GET_STATUS` discovers boot
and state without moving or changing doors. Backend generates a trip UUID after acquiring elevator access and clears it at
traversal end; manual reads use an empty trip ID. The trip UUID is distinct from
the delivery task ID.

Commands retry up to four times at two-second intervals using the identical
envelope. Only a matching `COMPLETED` response plus actual floor/door state
completes a command. `ACCEPTED` is intermediate; `REJECTED` fails immediately.
Open requires `ROBOT_HOLD` owned by this backend session, not an automatic opening.
Retained, legacy, malformed, duplicate/out-of-order, wrong-session and
wrong-request replies cannot acknowledge commands. Sequence numbers reset only
for a new ESP boot; retired boots cannot return.

A watchdog checks the five-second heartbeat deadline, including while waiting for
robot boarding, map loading and exit. MQTT disconnect, ESP reboot or lost door hold
signals traversal failure through the existing recovery path. This cancels robot
work and preserves the physical resource lease; reconnect never resumes a trip.
The firmware cannot stop already accepted elevator motion. Recovery remains an
explicit operator action with cabin-empty confirmation and a fresh closed-door
status. A hold owned by an earlier process cannot be silently released.

Keep only one elevator command publisher enabled: disable Python elevator control
when the backend owns the resource. The configured host in `appsettings.json` is
preserved; align it with the actual ESP broker rather than assuming the example
address in the upstream documentation. Each MQTT client needs a unique client ID.
The transport subscribes before sending a v2 discovery read on each connection.
Simulation still uses the Unity adapter with no MQTT/door commands.

## Backend to robot: movement yaw

In Operation, moves to a configured dock use the `MoveRobot` payload
`[x, y, yaw]`, where yaw is in radians and comes from `MapPointDto.Yaw`.
This includes cabin boarding, pickup/destination docks and waiting docks.
Node approach/exit commands keep `[x, y]`; the robot defaults their yaw to zero.
Simulation keeps the existing `[x, y]` format for Unity.

The robot must pass all three values to Nav2 and send `RobotArrived` only after
that goal succeeds. The source cabin goal must establish the configured heading
before the ride: destination-map initialization assumes that physical cabin pose.
Keep elevator cabin coordinates and yaw in `Data/Map.json` aligned with the
robot's `config/elevator.yaml` parking poses on each floor.

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
3. `GET_STATUS` must establish a correlated v2 completion with a fresh full snapshot.
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
