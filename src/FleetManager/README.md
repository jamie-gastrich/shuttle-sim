# src/FleetManager

The fleet manager console: command sending with retry, liveness tracking, and (M5) alarm publishing. It is one of the two apps that talk to the broker; it is also the only one that writes to Postgres.

## Depends on

- `FleetCore` (tasks)
- `Contracts` (message shapes)
- `Messaging` (bus seam)
- `MqttMessaging` (concrete broker)
- `TrafficCore` (segment reservations)
- NuGet: Npgsql, Configuration

## Used by

- `FleetManager.Tests` (unit + DB/integration)
- `FleetManager/Program.cs` is the entry point (`move`, `serve` commands)

## Overview

**Incoming messages**

```text
  ShuttleStatusMessage   ←  shuttle/{id}/status     (ShuttleSim)
    What this does:  HandleStatusAsync validates it and calls
                     ShuttleHealth.RecordSeen, remembering the shuttle id.
    Result:          each serve tick, EvaluateAndPublishAsync asks
                     ShuttleHealth who is unresponsive and publishes alarms.

  CommandAckMessage      ←  shuttle/{id}/ack        (ShuttleSim)
    What this does:  ReliableCommandSender matches the ack by CorrelationId
                     and stops retrying that command.
    Result:          the waiting `move` call returns (no message out).
```

**Outgoing messages**

```text
  MoveCommandMessage     →  shuttle/+/command
    Why:  assign work (move to a lane) to a shuttle.
    How:  ReliableCommandSender publishes on MessageFamily.Command and retries
          until CommandAckMessage arrives or attempts are exhausted.

  AlarmMessage           →  shuttle/{id}/alarm
    Why:  tell the WMS / operator something is wrong (e.g. lost communication).
    How:  FleetServeLoop evaluates ShuttleHealth + AlarmEvaluator each tick and
          publishes on MessageFamily.Alarm.
```

**Database**

```text
  FleetManager  ──►  Postgres (IFleetTaskRepository)
    Why:  tasks must survive a restart. The board is rebuilt from the database
          on startup; the Npgsql implementation lives here.
```

## The files

| File | Where it fits |
|---|---|
| `Program.cs` | The composition root and CLI: `move <shuttle> <lane>` and `serve`. Wires the bus, topics, and options. |
| `ReliableCommandSender.cs` | Sends a move command on the `Command` lane and retries until an ack; records the result. |
| `ShuttleHealth.cs` | Tracks last-seen time per shuttle and answers who is unresponsive. Pure. |
| `AlarmEvaluator.cs` | Turns the health verdict into alarms. Pure. |
| `FleetServeLoop.cs` | Subscribes to statuses, feeds `ShuttleHealth`, and each tick publishes alarms via `AlarmEvaluator`. The I/O seam for the `serve` path. |
| `TaskDispatcher.cs` | Assign / start / complete / fail a task through the board and the repository. |
| `OccupancyMap.cs` | Records which lane each shuttle last reported. |
| `RunLoop.cs` | The tick loop: picks queued tasks, gates moves, advances shuttles, persists progress. |
| `RunLoopGate.cs` | Combines the occupancy map and `TrafficController` into `CanMove`/`CommitMove`. |
| `PostgresFleetTaskRepository.cs` | The `IFleetTaskRepository` implementation over Npgsql. |
