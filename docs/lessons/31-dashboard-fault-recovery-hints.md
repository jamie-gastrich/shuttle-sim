# Lesson 31 hints

## Tier 1 — nudge

- `PauseState` is in `FleetCore` and shared between the gate and the handler (lesson 30).
- The handler `Acknowledge` case is at `src/FleetManager/Program.cs` line 337-339 (before the edit).
- `Shuttle.Recover()` is at `src/ShuttleCore/Shuttle.cs` line 144-152: it sets `_state = ShuttleState.Idle` and `_faultReason = null`.
- `RecoveryCommandMessage` is in contract v4: `docs/contract/v4.md` under `RecoveryCommandMessage`. It has `Envelope` + `ShuttleId`.
- `TopicScheme.RecoveryCommand(shuttleId)` returns the topic `"{root}/{shuttleId}/recoveryCommand"`.
- The handler has access to `bus` and `topics` via closure (they are locals in `RunServeAsync`).
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

The gate edits (only the pause-related lines change; occupancy/traffic stay):

```csharp
// These are already done in lesson 30; no changes needed for lesson 31.
```

In `CanMove`, the pause check `_pauseState.IsPaused(shuttleId)` is still first, before occupancy and traffic. The fault check is separate (the shuttle's own `State` property). The lesson teaches that pause and fault are orthogonal concerns that the operator coordinates through the three-command sequence.

The concurrency test for the new scenario: start serve with a shuttle in `Fault` state (blocked path), publish `OperatorCommandMessage.Acknowledge`, then publish `OperatorCommandMessage.Resume`, then observe the shuttle's next status update has `State == "idle"` and `FaultReason == null`. Verify no exception is thrown and the dashboard reflects the change.