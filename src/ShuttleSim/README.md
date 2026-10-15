# src/ShuttleSim

The simulated shuttle program. Runs one pallet shuttle as a process: receives commands, moves, answers, publishes status, and sends a heartbeat on an interval. The counterpart of `FleetManager`.

## Depends on

- `ShuttleCore` (state machine, rack, track)
- `Contracts` (message shapes)
- `Messaging` (bus seam)
- `MqttMessaging` (concrete broker)
- NuGet: `Microsoft.Extensions.Configuration.*`

## Used by

- `ShuttleSim.Tests` (end-to-end over an in-process shuttle with a fake bus)

## Overview

**Incoming messages**

```text
  MoveCommandMessage     ←  shuttle/+/command       (FleetManager)
    What this does:  ShuttleEndpoint.HandleCommandAsync
      1. rejects anything that is not a current-version move command for this shuttle
      2. deduplicates via IdempotencyGuard (same MessageId → same ack, no re-run)
      3. calls Shuttle.StartMove(toLane, now) — which validates the lane
      4. acks, publishes a `moving` status, then CompleteMove at the arrival time
         and publishes an `idle` status
    Result:          the shuttle moves and reports its new position.
```

**Outgoing messages**

```text
  CommandAckMessage      →  shuttle/{id}/ack
    Why:  confirm the command was received so FleetManager stops retrying.
    How:  ShuttleEndpoint publishes on MessageFamily.Ack (accepted or rejected).

  ShuttleStatusMessage   →  shuttle/{id}/status
    Why:  tell FleetManager where the shuttle is and whether it is busy.
    How:  ShuttleEndpoint publishes after each move; HeartbeatPublisher
          publishes on an interval even while idle, so the manager keeps
          seeing the shuttle as alive.
```

The shuttle never decides where to go; it only validates the lane, moves, and
reports. Lane/queue decisions live in `FleetManager`, so a whole warehouse can be
simulated with one shuttle process per id.

## The files

| File | Where it fits |
|---|---|
| `Program.cs` | The composition root: reads config, connects the bus, and runs the endpoint + heartbeat. |
| `ShuttleEndpoint.cs` | Handles inbound move commands (validate, dedupe, move, ack) and publishes status. |
| `HeartbeatPublisher.cs` | Publishes a status on an interval so the manager keeps seeing the shuttle as alive. |
