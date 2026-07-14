Project Context
Project Overview

This project implements the Fleet Manager backend for a Digital Twin system of autonomous mobile robots (AMRs) operating in environments such as hospitals, hotels, or warehouses.

The Fleet Manager is not responsible for robot navigation or motor control. Those responsibilities belong to the physical robot.

Instead, the Fleet Manager coordinates the entire fleet by assigning tasks, monitoring robot states, managing shared resources, and communicating with both physical robots and the Unity-based Digital Twin.

The backend is written in pure C# (.NET) and follows a modular architecture.

System Architecture
                     Dashboard
                         │
                    REST / WebSocket
                         │
                         ▼
                  Fleet Manager (.NET)
 ┌──────────────────────────────────────────────────────┐
 │                                                      │
 │ RobotManager                                         │
 │ TaskManager                                          │
 │ Scheduler                                            │
 │ TrafficManager                                       │
 │ MapManager                                           │
 │ CommunicationGateway                                 │
 │ SimulationManager (future)                           │
 └──────────────────────────────────────────────────────┘
         │                                   │
         │                                   │
   MQTT / ROS2 / gRPC                 WebSocket
         │                                   │
         ▼                                   ▼
 Physical Robots                 Unity Digital Twin
Responsibilities

Fleet Manager is responsible for:

Maintaining the state of every robot.
Receiving delivery requests.
Creating delivery tasks.
Assigning tasks to the most suitable robot.
Monitoring task execution.
Managing shared resources (doors, elevators, corridors).
Receiving robot telemetry.
Broadcasting robot states to Unity Digital Twin.
Supporting future simulation mode.

Fleet Manager does NOT

Perform path planning.
Avoid obstacles.
Run SLAM.
Control robot motors.
Execute navigation algorithms.

Those are handled by the robot itself.

Robot Workflow

A typical workflow is

Dashboard

↓

Create Delivery Task

↓

TaskManager

↓

Scheduler

↓

Robot Selected

↓

TrafficManager

↓

CommunicationGateway

↓

Physical Robot

↓

Robot executes navigation

↓

Robot sends telemetry

↓

RobotManager updates state

↓

Unity synchronizes visualization
Robot Responsibilities

Each robot already has

Navigation
Localization
Obstacle avoidance
Path planning
Battery monitoring
Motor controller

Fleet Manager only sends

Task

Destination

Priority

The robot decides

How to reach destination.
Which path to follow.
How to avoid obstacles.
Main Modules
RobotManager

Responsible for

Robot registration
Robot lifecycle
Robot state
Battery
Connectivity
Current task
TaskManager

Responsible for

Creating tasks
Task lifecycle
Pending queue
Completed tasks

Task states

Pending

Assigned

Running

Completed

Failed

Cancelled
Scheduler

Responsible for

Selecting the best robot.

Selection criteria may include

Distance
Battery
Robot status
Robot capability
Task priority

Initially a simple cost function is sufficient.

Future algorithms may include

Hungarian Algorithm
Auction Based Assignment
MRTA
TrafficManager

Responsible for shared resources.

Examples

Elevator
Door
Narrow corridor
Intersection

TrafficManager does NOT control robot movement.

Instead it grants or denies permission to enter a shared resource.

Example

Robot

↓

Request Elevator

↓

TrafficManager

↓

Granted

↓

Robot enters elevator

↓

Release Elevator
MapManager

Maintains logical map.

The logical map contains

Rooms
Pharmacy
Elevator
Charging station
Storage

Fleet Manager does not require wall geometry.

The robot owns the navigation map.

CommunicationGateway

Responsible for all communication.

Examples

Robot

↓

MQTT

↓

Fleet

Unity

↓

WebSocket

↓

Fleet

Dashboard

↓

REST API

↓

Fleet

CommunicationGateway contains no business logic.

Robot State

Each robot maintains

RobotId

Position

Rotation

Battery

Status

CurrentTask

LastHeartbeat
Task

A task contains

TaskId

PickupLocation

Destination

Priority

AssignedRobot

Status
Digital Twin

Unity has two operating modes.

Operation Mode

Unity only visualizes real robots.

Data flow

Robot

↓

Fleet

↓

Unity

Unity never controls robots.

Simulation Mode

Unity simulates virtual robots.

Fleet Manager treats virtual robots exactly like physical robots.

Data flow

Fleet

↓

Unity Simulation

↓

Fleet

This allows testing without physical robots.

Coding Principles

Prefer

SOLID
Dependency Injection
Event-driven architecture
Clean separation of concerns
Small modules
Interfaces for external services
Async communication
Immutable DTOs where appropriate

Avoid

Business logic inside communication layer
Tight coupling between modules
Static global state (except composition root if necessary)
Future Extensions

The architecture should support

Multiple hospitals
Multiple floors
Hundreds of robots
Historical replay
Analytics dashboard
Simulation mode
AI scheduling algorithms
Predictive maintenance