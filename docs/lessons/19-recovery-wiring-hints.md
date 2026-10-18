# Hints — Lesson 19: Wiring the recovery round trip

Open a tier only when you are stuck. Tier 1 first.

---

## Tier 1 — Nudge (read this before giving up)

- **Every piece of this slice has a move twin.** Open the twin file next to the file you are writing and copy its *shape*, not its names:

  | Move twin | Recovery twin |
  |---|---|
  | `ReliableCommandSender` | `RecoverySender` |
  | `MoveCommandMessage` | `RecoveryCommandMessage` |
  | `MessageTypes.CommandAck` | `MessageTypes.RecoveryAck` |
  | `_topics.Command(id)` | `_topics.RecoveryCommand(id)` |
  | `topics.AllAcks` + `MessageFamily.Ack` | `topics.AllRecoveryAcks` + `MessageFamily.Ack` |
  | `MessageFamily.Command` (publish) | `MessageFamily.Recovery` (publish) |
  | `HandleCommandAsync` | `HandleRecoveryCommandAsync` |
  | `AcknowledgeAsync` (move ack) | a recovery-ack helper of the same shape |
  | `CommandSendResult` | `RecoverySendResult` |

- **The contract is already done.** `docs/contract/v4.md` has the `RecoveryCommandMessage` and `RecoveryAckMessage` sections: fields, direction, topics, example JSON. Build against that page, not against memory. (Its ack example shows `SenderId: "SH-01"` and a `CorrelationId` naming the command — match that.)
- **Read the review that just passed.** `docs/reviews/19-lesson-18-followup.md` and the re-review at the bottom of `docs/reviews/18-operator-acknowledged-recovery.md` list exactly what is left, and the re-review's "Note for the professor" says two things about tests you should not repeat: assert **literals**, not constants against themselves; and a contract record is a **plain positional record** (no `JsonPropertyName` attributes — you are not writing records this lesson anyway, they exist).
- **No new config keys.** If you are typing a number or a topic string, stop: the number belongs in `appsettings.json` (reuse the `FleetManager` block) and the topic belongs in `TopicScheme`.
- **The contract must not move.** If `git diff docs/contract` shows anything, undo it. `ContractVersion.Current` stays `4`.

---

## Tier 2 — Direction (the shape, step by step)

1. **`RecoverySender`**: fields `_bus`, `_topics`, `_policy`, and a `ConcurrentDictionary<string, TaskCompletionSource<RecoveryAckMessage>>` of waiters keyed by the **command's** `MessageId`. `OnRecoveryAckReceivedAsync` filters on `MessageTypes.RecoveryAck` + non-blank `CorrelationId`, deserializes the body, and completes the matching waiter with `TrySetResult` (never `SetResult` — late duplicates are normal). `SendAsync` serializes once, then loops: publish → `WaitAsync(_policy.AckTimeout)` → on `TimeoutException` backoff and repeat → stop at `_policy.MaxAttempts`; `Accepted: false` returns `Rejected` immediately; remove the waiter in `finally`.
2. **`RecoverySendResult`**: a record like `CommandSendResult` but holding a `RecoveryAckMessage?`. Reuse the existing `CommandSendStatus` enum — do not invent a second one.
3. **`RunRecoverAsync` in `Program.cs`**: copy the bones of `RunMoveAsync` — same configuration keys, same overall timeout via `RoundTripTimeoutMs`, same exit-code table. Differences: build a `RecoveryCommandMessage`; subscribe `AllRecoveryAcks` with `MessageFamily.Ack` instead of `AllAcks`; the healthy status you wait for is `state == "idle"` (after recovery, that is what healthy means). Update the usage line at the top of the file.
4. **`HandleRecoveryCommandAsync`**: copy the validation prologue of `HandleCommandAsync` (probe the envelope via the private `EnvelopeWrapper` record, then deserialize the body, then compare ids) and change only the kind. Then: **state check first** — not `Fault` → ack `Accepted: true`, `Reason: null`, return. `Fault` → `Recover(now)`; catch `ShuttleRejectedException` → ack `false` with the message; otherwise ack `true` and `PublishStatusAsync(now, ct)`.
5. **Recovery-ack helper**: same shape as `AcknowledgeAsync`, but `MessageTypes.RecoveryAck`, `_topics.RecoveryAck(_shuttleId)`, `MessageFamily.Ack`, and the envelope built with the fourth argument `command.Envelope.MessageId` (that is the correlation).
6. **`Program.cs` in ShuttleSim**: one more `SubscribeAsync`, on `topics.RecoveryCommand(shuttleId)`, family `MessageFamily.Recovery`, calling the new handler. Mirror the existing log line.
7. **Tests**: the three endpoint facts use the existing `FakeBus` (it already records `(Topic, Payload, Family)` — that is your family pin for free). For the faulted case, reuse the blocked-lane setup from `A_blocked_move_is_acknowledged_as_not_accepted_and_reports_a_fault_status`, then deliver a recovery payload. For `RecoverySenderTests`, the existing `FakeBus` in `FleetManager.Tests` plans responses on publish; add a `QueueRecoveryAck(RecoveryAckMessage)` beside `QueueAck`.

---

## Tier 3 — Near-solution (skeletons with the load-bearing lines)

Use this tier only after Tier 2 failed you. The blanks are the parts you must understand.

**Sender loop (fill the gaps):**

```csharp
var payload = JsonSerializer.Serialize(command);          // once, outside the loop
for (var attempt = 1; attempt <= _policy.MaxAttempts; attempt++)
{
    await _bus.PublishAsync(_topics.RecoveryCommand(command.ShuttleId),
        payload, MessageFamily.???, cancellationToken);
    try
    {
        var ack = await waiter.Task.WaitAsync(_policy.???, cancellationToken);
        return ack.Accepted
            ? new RecoverySendResult(CommandSendStatus.???, attempt, ack)
            : new RecoverySendResult(CommandSendStatus.???, attempt, ack);
    }
    catch (TimeoutException)
    {
        if (attempt < _policy.MaxAttempts)
            await Task.Delay(_policy.???, cancellationToken);
    }
}
return new RecoverySendResult(CommandSendStatus.???, _policy.MaxAttempts, null);
```

**Endpoint handler (fill the gaps):**

```csharp
public async Task HandleRecoveryCommandAsync(string payload, DateTime now, CancellationToken ct)
{
    // envelope probe: same three guards as HandleCommandAsync, but the kind is ??? 
    // body: Deserialize<RecoveryCommandMessage>, ids must match, ShuttleId must be ???

    if (_shuttle.State != ShuttleState.???)
    {
        await AcknowledgeRecoveryAsync(command, accepted: ???, reason: null, now, ct);
        return;
    }

    try { _shuttle.???(now); }
    catch (ShuttleRejectedException exception)
    {
        await AcknowledgeRecoveryAsync(command, accepted: false, reason: exception.Message, now, ct);
        return;
    }

    await AcknowledgeRecoveryAsync(command, accepted: true, reason: null, now, ct);
    await ???(now, ct);                       // the healthy status that clears the latch
}
```

**Test assertions worth writing (each pins one mutation):**

- `Assert.Equal(MessageFamily.Ack, entry.Family)` on the published recovery ack — kills "ack published with the wrong family".
- `Assert.Equal(commandEnvelope.MessageId, ack.Envelope.CorrelationId)` — kills "correlation points at the ack's own id".
- `Assert.Contains("\"Accepted\"", payloadText)` — kills a casing/attribute "refactor" of the wire format.
- On the healthy path: `Assert.Equal(ShuttleState.Idle, shuttle.State)` **and** `Assert.Single(bus.Published, m => m.Family == MessageFamily.Ack)` **and** no `Status` entries — kills "recovery runs when there is nothing to recover" and "spurious status on a no-op".
- Sender test: `Assert.Equal(command.Envelope.MessageId, publishedAttempts.All(...same id...))` and `Assert.Equal(2, result.Attempts)` — kills "each retry is a new command", which is the defect that would make every retry a fresh recovery instead of a duplicate.

If a mutation from the assignment's mutation list survives one of your tests, the missing assertion is usually in this list.
