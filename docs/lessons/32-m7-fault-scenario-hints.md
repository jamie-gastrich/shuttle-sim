# Lesson 32 hints

## Tier 1 — nudge

- The full workflow is: Fault injected (blocked lane) → dashboard shows alarm → operator Pause → operator Acknowledge → operator Resume → shuttle recovered
- `HandleOperatorCommandAsync` Acknowledge case is at `src/FleetManager/Program.cs` line 337 (before the edit in lesson 31, now extended)
- `Shuttle.Recover()` is at `src/ShuttleCore/Shuttle.cs` line 144-152: it sets `_state = ShuttleState.Idle` and `_faultReason = null`
- `RecoveryCommandMessage` is in contract v4: `Envelope` + `ShuttleId`. The envelope uses `MessageTypes.RecoveryCommand`, sender `fleet-manager`, `DateTime.UtcNow`
- `TopicScheme.RecoveryCommand(shuttleId)` returns `"{root}/{shuttleId}/recoveryCommand"`
- The handler has access to `bus` and `topics` via closure (they are locals in `RunServeAsync`)
- The new `PublishAsync` call in the `Acknowledge` case has this shape:
  `await bus.PublishAsync(topics.RecoveryCommand(cmd.ShuttleId), JsonSerializer.Serialize(recoveryCmd), MessageFamily.Json, cancellationToken);`

## Tier 2 — direction

The full `Acknowledge` case in `HandleOperatorCommandAsync`:

```csharp
case OperatorCommandMessage.Command.Acknowledge:
    await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
    var recoveryEnvelope = MessageEnvelopes.Create(
        MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow);
    var recoveryCmd = new RecoveryCommandMessage(recoveryEnvelope, cmd.ShuttleId);
    await bus.PublishAsync(topics.RecoveryCommand(cmd.ShuttleId),
        JsonSerializer.Serialize(recoveryCmd), MessageFamily.Json, cancellationToken);
    break;
```

- `MessageEnvelopes.Create` is the same static method used by `RunMoveAsync` and `RunRecoverAsync` — it creates a v4 envelope with `ContractVersion`, `MessageType`, `MessageId`, `SenderId`, `SentAt`.
- `MessageTypes.RecoveryCommand` is the string constant (check `docs/contract/v4.md` for its exact value).
- The `recoveryCmd` payload is `{"Envelope":{...},"ShuttleId":"SH-01"}` — the same shape the shuttle simulator already subscribes to.
- The `topics` variable is `TopicScheme`; `topics.RecoveryCommand(shuttleId)` builds the per‑shuttle topic.
- The `bus` variable is `IMessageBus`; `PublishAsync` sends the JSON payload on that topic.
- The `cancellationToken` propagates cancellation through the MQTT send operation.

The handler Pause/Resume calls (already from lesson 30):

```csharp
case OperatorCommandMessage.Command.Pause:
    await taskBoard.PauseAllExcept(cmd.ShuttleId, cancellationToken);
    if (string.IsNullOrWhiteSpace(cmd.ShuttleId))
        pauseState.PauseAll(registry.ShuttleIds());
    else
        pauseState.Pause(cmd.ShuttleId);
    break;

case OperatorCommandMessage.Command.Resume:
    await taskBoard.ResumeAllExcept(cmd.ShuttleId, cancellationToken);
    if (string.IsNullOrWhiteSpace(cmd.ShuttleId))
        pauseState.ResumeAll(registry.ShuttleIds());
    else
        pauseState.Resume(cmd.ShuttleId);
    break;
```

- `taskBoard.PauseAllExcept` / `ResumeAllExcept` — unchanged from lesson 26, stops dispatch
- `pauseState.Pause` / `Resume` / `PauseAll` / `ResumeAll` — from lesson 30, stops/starts movement
- The empty-id branch pauses/resumes every known shuttle. `_shuttleIds` is the list of shuttles `serve` knows about (from status messages, or a configured list).

## Tier 3 — near-solution

The full `Acknowledge` case edit in `src/FleetManager/Program.cs`, replacing the existing:

```csharp
case OperatorCommandMessage.Command.Acknowledge:
    await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
    break;
```

with:

```csharp
case OperatorCommandMessage.Command.Acknowledge:
    await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
    var recoveryEnvelope = MessageEnvelopes.Create(
        MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow);
    var recoveryCmd = new RecoveryCommandMessage(recoveryEnvelope, cmd.ShuttleId);
    await bus.PublishAsync(topics.RecoveryCommand(cmd.ShuttleId),
        JsonSerializer.Serialize(recoveryCmd), MessageFamily.Json, cancellationToken);
    break;
```

The end-to-end scenario flow:

1. **Fault injection**: Simulation starts with a blocked lane (e.g., `Shuttle:BlockedLanes: ["A02-L02"]` in `appsettings.json`). When shuttle SH-01 tries `StartMove("A02-L02", now)`, the rack detects the route is blocked and calls `shuttle.RaiseFault(FaultReasons.BlockedPath, now)`. The shuttle state becomes `Fault`, `FaultReason` is `"blocked path"`. The `AlarmEvaluator` generates a critical alarm with code `blockedPath`. The dashboard shows the alarm.

2. **Operator Pause**: The operator clicks *Pause* on the dashboard. The dashboard publishes `OperatorCommandMessage` with `Kind = Pause` on `fleet/operator/command`. The fleet manager receives it in `HandleOperatorCommandAsync`, calls `pauseState.Pause(shuttleId)` and `taskBoard.PauseAllExcept(cmd.ShuttleId)`. The movement gate `RunLoopGate.CanMove` returns `false` for SH-01, so new moves are rejected and in-progress moves freeze in place (reservations held, position preserved). Dispatch stops.

3. **Operator Acknowledge Recovery**: The operator clicks *Acknowledge* on the dashboard. The dashboard publishes `OperatorCommandMessage` with `Kind = Acknowledge` and `ShuttleId = "SH-01"`. The handler calls `alarmRepository.AcknowledgeAsync("SH-01", ct)` and publishes `RecoveryCommandMessage` on `topics.RecoveryCommand("SH-01")` = `"shuttle/SH-01/recoveryCommand"`. The shuttle's MQTT subscriber deserializes the message and calls `Shuttle.Recover(DateTime.Now)`. `Recover` sets `_state = ShuttleState.Idle` and `_faultReason = null`. The alarm tile on the dashboard clears.

4. **Operator Resume**: The operator clicks *Resume* on the dashboard. The dashboard publishes `OperatorCommandMessage` with `Kind = Resume` and `ShuttleId = "SH-01"`. The handler calls `pauseState.Resume("SH-01")` and `taskBoard.ResumeAllExcept(cmd.ShuttleId)`. The movement gate no longer blocks SH-01. The shuttle is now `Idle` with no `FaultReason`, the gate is not paused, and the next tick of the run loop grants it a route. The shuttle begins moving again, and the dashboard reflects the new position.

5. **Verification**: Check that after Acknowledge, the shuttle's status message has `State == "idle"` and `FaultReason == null`. Check that after Resume, the dashboard shows the shuttle moving toward a destination and the task queue is active.

The concurrency test for the new scenario: start serve, inject a blocked-path fault into a shuttle (e.g., via the simulator or a test that sets `FaultReason`), publish `OperatorCommandMessage.Acknowledge`, publish `OperatorCommandMessage.Resume`, then observe the shuttle's next status update has `State == "idle"` and `FaultReason == null`. Verify no exception is thrown and the dashboard reflects the change.

---

## Tier 3 — near-solution (full)

The full `Acknowledge` case edit in `src/FleetManager/Program.cs`, replacing the existing:

```csharp
case OperatorCommandMessage.Command.Acknowledge:
    await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
    break;
```

with:

```csharp
case OperatorCommandMessage.Command.Acknowledge:
    await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
    var recoveryEnvelope = MessageEnvelopes.Create(
        MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow);
    var recoveryCmd = new RecoveryCommandMessage(recoveryEnvelope, cmd.ShuttleId);
    await bus.PublishAsync(topics.RecoveryCommand(cmd.ShuttleId),
        JsonSerializer.Serialize(recoveryCmd), MessageFamily.Json, cancellationToken);
    break;
```

The end-to-end scenario flow:

1. **Fault injection**: Simulation starts with a blocked lane (e.g., `Shuttle:BlockedLanes: ["A02-L02"]` in `appsettings.json`). When shuttle SH-01 tries `StartMove("A02-L02", now)`, the rack detects the route is blocked and calls `shuttle.RaiseFault(FaultReasons.BlockedPath, now)`. The shuttle state becomes `Fault`, `FaultReason` is `"blocked path"`. The `AlarmEvaluator` generates a critical alarm with code `blockedPath`. The dashboard shows the alarm.

2. **Operator Pause**: The operator clicks *Pause* on the dashboard. The dashboard publishes `OperatorCommandMessage` with `Kind = Pause` on `fleet/operator/command`. The fleet manager receives it in `HandleOperatorCommandAsync`, calls `pauseState.Pause(shuttleId)` and `taskBoard.PauseAllExcept(cmd.ShuttleId)`. The movement gate `RunLoopGate.CanMove` returns `false` for SH-01, so new moves are rejected and in-progress moves freeze in place (reservations held, position preserved). Dispatch stops.

3. **Operator Acknowledge Recovery**: The operator clicks *Acknowledge* on the dashboard. The dashboard publishes `OperatorCommandMessage` with `Kind = Acknowledge` and `ShuttleId = "SH-01"`. The handler calls `alarmRepository.AcknowledgeAsync("SH-01", ct)` and publishes `RecoveryCommandMessage` on `topics.RecoveryCommand("SH-01")` = `"shuttle/SH-01/recoveryCommand"`. The shuttle's MQTT subscriber deserializes the message and calls `Shuttle.Recover(DateTime.Now)`. `Recover` sets `_state = ShuttleState.Idle` and `_faultReason = null`. The alarm tile on the dashboard clears.

4. **Operator Resume**: The operator clicks *Resume* on the dashboard. The dashboard publishes `OperatorCommandMessage` with `Kind = Resume` and `ShuttleId = "SH-01"`. The handler calls `pauseState.Resume("SH-01")` and `taskBoard.ResumeAllExcept(cmd.ShuttleId)`. The movement gate no longer blocks SH-01. The shuttle is now `Idle` with no `FaultReason`, the gate is not paused, and the next tick of the run loop grants it a route. The shuttle begins moving again, and the dashboard reflects the new position.

5. **Verification**: Check that after Acknowledge, the shuttle's status message has `State == "idle"` and `FaultReason == null`. Check that after Resume, the dashboard shows the shuttle moving toward a destination and the task queue is active.