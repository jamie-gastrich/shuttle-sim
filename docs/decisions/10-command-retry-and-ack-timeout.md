# Decision 10: Command retry, the acknowledgement timeout, and giving up

**Date:** 2026-10-10
**Milestone:** M2 (Message contract and transport) — the *sending* half of reliability: what the fleet manager does when a command is never acknowledged.
**Status:** Accepted. Written for lesson 06, which assigns the retry, the per-attempt timeout, and the give-up path.
**Supersedes:** none. This is the sequel [record 09](09-command-acknowledgement-and-idempotency.md) asked for and named:

> **Replaced by:** the reliability record (lesson 06) that adds **retries, the ack timeout, and the alarm after retries are exhausted**. That record links here; it adds the sender's half of the contract below, it does not change the receiver's.

Record 09 fixed the receiver (the shuttle): one ack per command, and a bounded cache of *accepted* ids so a duplicate does not run the work twice. This record fixes the sender (the fleet manager): how it notices a missing ack, how it resends, and when it stops. It also links back to [record 02](02-message-transport.md) (MQTT chosen), [record 07](07-message-contract-v2-envelope-and-topics.md) (the envelope and `CorrelationId`), and [record 08](08-mqtt-client-qos-and-configuration.md) (QoS 1 means duplicates are real; `FleetManager:RoundTripTimeoutMs` is "a driver bound, not the reliability policy — command retry and the ack timeout are lesson 06 and record 09").
**Replaced by:** any later record that changes the retry policy, the timeout config, or the give-up behaviour, or that turns the give-up into a wire alarm (contract v3). The replacement links here.
**Wire contract:** unchanged. Contract v2 already has everything a resend needs (a stable `MessageId`, a `CorrelationId` on the ack). Retry changes *how often* the same v2 message is sent, not *what* it contains. There is no version bump and no new message kind.

---

## 1. What we are deciding

The message contract's reliability line: *"A command is acknowledged. If no acknowledgement arrives within a timeout, retry a limited number of times, then raise an alarm."* Record 09 took the first sentence. This record takes the rest, as the sender's policy:

1. how the sender notices that an ack is **missing** (the per-attempt timeout);
2. what exactly is resent (the **same** message, same `MessageId`) and why that is both safe and required;
3. how many times it tries, and how it spaces the tries (the attempt count and backoff);
4. what a **rejected** ack does (it is not a lost ack — it is an answer);
5. what happens when the tries run out (giving up), and where the "alarm" lives at M2;
6. the configuration keys, with defaults, for all of the above.

Not decided here: the wire `AlarmMessage` and recovery messages (contract v3, the later M2 lesson — review 05 puts them there); heartbeats (M5); task-queue retry policy (M3); the WMS (M6). This record deliberately does **not** touch the contract, because it does not need to.

---

## 2. Why this record closes M2

The spec's M2 done-when is two test-visible facts:

> *"tests show a repeated command does not run twice and a lost acknowledgement is retried."*

Lesson 05 proved the first (the guard; review 05 mutation-proved it). The second is this record. Note what the done-when does **not** require: a wire alarm message. M2 closes when a lost ack is retried and the retry is safe; the operator-facing alarm is a message *shape* (contract v3) and belongs with alarms and recovery. At M2 the "alarm" is the local failure outcome in section 7. This is the honest reading of both the spec and review 05's own note that *"alarms, recovery, and heartbeats are later M2/M5 lessons and contract v3."*

---

## 3. Survey A: where the retry logic lives

AGENTS.md: *"Decision logic is separated from input and output … so it can be unit tested alone"*, and *"No hard-coded names, topics, timeouts, retry counts."* The retry must be testable without a broker, and the numbers must come from configuration. Three placements:

| | A: a pure `RetryPolicy` + a `ReliableCommandSender` in the fleet-manager app (chosen) | B: a generic `ReliableSender` in `Messaging` taking a payload and a reply predicate | C: inline in `FleetManager/Program.cs` |
|---|---|---|---|
| Test with no broker | yes — the sender is driven with a fake bus; the policy is pure | yes | no — the logic is inside `Main`'s top-level statements |
| Retry numbers from config | a pure record built from config | a pure record built from config | literals in `Main` unless extracted |
| `Messaging` stays domain-free | yes — it holds the pure policy only; the loop lives with the app | the loop lives in `Messaging`, so the transport library grows scheduling and a callback | yes, trivially |
| Same shape as lesson 05 | yes — `ShuttleSim/ShuttleEndpoint` is the receiver's protocol type living with its app | no | no |

**Decision 1: a pure `RetryPolicy` record in `Messaging`, and a `ReliableCommandSender` class in `FleetManager` that loops around `IMessageBus`.**

```csharp
// Messaging — pure, no I/O, no broker
public sealed record RetryPolicy(int MaxAttempts, TimeSpan AckTimeout, TimeSpan Backoff);

// FleetManager — the loop; tested in FleetManager.Tests with a fake bus
public sealed class ReliableCommandSender(IMessageBus bus, TopicScheme topics, RetryPolicy policy);
```

`RetryPolicy` is pure and lives with `TopicScheme`/`IdempotencyGuard` (record 08's domain-free project). The loop needs a bus, so it lives with the fleet-manager app, exactly as the shuttle's `ShuttleEndpoint` lives with `ShuttleSim`. The loop is still testable without a broker: `FleetManager.Tests` gets a direct `Messaging` project reference and drives the sender with a fake `IMessageBus`.

**Naming:** the count is `MaxAttempts` — the total number of sends, so `3` means one original plus two retries. `MaxRetries` invites an off-by-one ("retries *plus* the first"?), and off-by-one in a retry count is the classic production bug.

---

## 4. Decision 2: resend the SAME message (same `MessageId`, same `SentAt`)

On a timeout the sender publishes **the exact message it built before the loop** — same `MessageId`, same `SentAt`, same body. It does not build a new command per attempt.

- **Why it is safe.** The shuttle's `IdempotencyGuard` (record 09) records an id only when the command was *accepted*. A second copy of the same id is re-acknowledged `Accepted = true` and returns **before** `StartMove`. The move runs once. This is exactly "at-least-once delivery plus an idempotent receiver" (record 08).
- **Why it is required.** A fresh `MessageId` per attempt makes the second copy a *new command* to the guard. The shuttle would try to start a second move. For `StartMove` that is merely rejected ("cannot start a move while Moving"); for a later `StartLoad` it is two pallets, or a crash. The id is what makes the resend a resend.
- **`SentAt` stays put.** The envelope's `SentAt` is *when the sender created the message*, not when this copy hit the wire (record 07 §3). Reusing the id but changing the timestamp would make the message not-quite-identical and tempt a receiver to treat it as new. The whole message is frozen; only the network delivery repeats.

**No wire change.** Contract v2 is sufficient and untouched; `docs/contract/v2.md` stays `Version: 2`, and there is no `v3.md` from this record.

---

## 5. Decision 3: two clocks — one per try, one for the whole job

The sender needs two different bounds, and they answer two different questions:

- **`AckTimeout` (per attempt)** — "has *this* copy been acknowledged?" When it elapses, the attempt is lost and the sender resends. This is the new clock.
- **`RoundTripTimeoutMs` (the whole `move`)** — "has the whole operation taken too long?" This is record 08's existing driver bound, already validated positive. It now caps the retry loop *and* the status wait, so a bug in the loop cannot hang the process. It is the R2 lesson (a bounded wait, never an unbounded one) held over from review 03.

The per-attempt timeout is what makes a lost ack visible. Without it the sender cannot tell "the shuttle is slow" from "the ack is gone," so it can never decide to resend. This is the seed of M5's heartbeat reasoning, one level down.

**Config keys** (`FleetManager` section; no literals in code; `appsettings.json` with environment-variable override, per record 08):

| Key | Default | Meaning |
|---|---|---|
| `FleetManager:AckTimeoutMs` | `2000` | how long one attempt waits for the correlated ack |
| `FleetManager:MaxAttempts` | `3` | total sends (1 original + 2 retries) |
| `FleetManager:BackoffMs` | `500` | wait between attempts (constant; see Survey B) |
| `FleetManager:RoundTripTimeoutMs` | `10000` | unchanged; the overall cap on the whole `move` |

Defaults fit the overall cap: worst case `3 × 2000 + 2 × 500 = 7000 ms < 10000 ms`. If a deployment sets `AckTimeoutMs × MaxAttempts` above `RoundTripTimeoutMs`, the overall token wins and cuts the loop short — that is the safety net doing its job, and it is why both clocks exist.

---

## 6. Survey B: the backoff shape

Between attempts the sender waits. How long? Three shapes:

| | A: constant, from config (chosen) | B: exponential with jitter | C: no wait (retry immediately) |
|---|---|---|---|
| Behaviour when the shuttle is briefly gone | a few evenly spaced tries; deterministic and easy to read in a test | backs off harder under load; the standard choice when many clients share a broker | hammers the broker and the shuttle; a tight loop |
| Testability | exact; a zero backoff runs instantly | needs a jitter source or a fake clock | exact |
| Fits M2's one-command simulator | yes | more machinery than one manager and one shuttle need | no — it is a busy-wait in message form |

**Decision 4: constant backoff, `BackoffMs` from config (default 500).** Exponential backoff with jitter is the *right* change when many fleet managers or many shuttles share a broker and retries could synchronise into a thundering herd; the sim has one manager and one command at a time, so the simpler shape is enough and far easier to prove. This is recorded so the choice is not mistaken for an oversight: the change trigger is in section 11.

---

## 7. Decision 5: retry a *missing* ack, never a *rejected* ack

An acknowledgement with `Accepted = false` is not a lost ack. It is a definitive answer: the shuttle received the command and refused it (for example, an unknown lane). Resending it re-sends the same refusal. So:

- **no ack within `AckTimeout`** → the attempt is lost; retry (same id).
- **ack with `Accepted = true`** → done; stop.
- **ack with `Accepted = false`** → stop, report the rejection, do **not** retry.

This is consistent with record 09 §2: a rejected command is *not* recorded by the guard, so a retry would be re-checked — but that only matters for a *duplicate the shuttle already received*, not for a sender deciding to try again. Deciding to re-issue a refused command (in case the obstruction cleared) is a policy for a later milestone (M3's task lifecycle, where a task can be retried or failed), not this one.

---

## 8. Decision 6: bounded attempts, and what "raise an alarm" means at M2

After `MaxAttempts` sends with no ack, the sender **stops** and returns a failure outcome:

```csharp
public enum CommandSendStatus { Acknowledged, Rejected, NoAcknowledgement }
public sealed record CommandSendResult(CommandSendStatus Status, int Attempts, CommandAckMessage? Acknowledgement);
```

An unbounded retry loop is an unbounded wait with a friendlier name — the same defect review 03 named R2. A fleet needs a deterministic "this command did not land" so a higher layer can reassign the work.

At M2, giving up is surfaced **locally**: the fleet manager prints a clear failure line (shuttle id, command id, attempts) and exits non-zero. The **wire** alarm — an `AlarmMessage` with a severity that an operator must acknowledge — is contract v3 and belongs with the alarms-and-recovery lesson. Record 09's phrase *"then raise an alarm"* is satisfied at M2 by this local failure outcome; the wire shape arrives with the rest of the alarm set. This keeps M2's contract frozen at v2 and M2's done-when fully met.

Rejected: inventing a minimal `AlarmMessage` now. It would be a wire change (record + version bump) for a message the operator dashboard cannot yet receive (M7), and it would collide with the real alarm/recovery shapes being designed later. Deferred, linked here.

---

## 9. Rejected options, and why

- **A generic retry inside `MqttMessaging` (the adapter).** Would let the adapter retry any publish, but the adapter does not know what a *correlated ack* is; it would have to learn the contract, and `MQTTnet`'s own QoS-1 already retries at the link layer (record 08). Application retry is about the *ack of a specific command*, which only the sender side knows. Rejected.
- **Retry on any QoS failure inside the client library.** QoS 1 retries the publish to the *broker*, not the command to the *shuttle*. A broker that accepts the publish but never delivers it (shuttle offline) produces no ack and no library error. The retry must be driven by the missing ack, above the library. Rejected.
- **A new `MessageId` per attempt (so each attempt is "fresh").** Breaks idempotency — the guard sees two commands. Rejected in section 4; the toy in lesson 06 measures exactly this failure.
- **Retry forever until an ack.** Unbounded wait. Rejected (section 8).
- **A wire alarm now.** Rejected/deferred (section 8): a contract change for a consumer that does not exist yet.
- **Exponential backoff with jitter now.** Rejected for M2 only (Survey B): real, but later.

---

## 10. Verification table

Checked this session (2026-10-10). The retry mechanism ran as a throwaway console toy in `/tmp/opencode/retrytoy`; the async timeout facts ran in `/tmp/opencode/timeoutprobe`; both are deleted afterwards. Read-only commands and file reads were run against this repo. The owner's `RetryPolicy` and `ReliableCommandSender` do not exist yet — this record is the specification for them.

| Claim | How checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| A same-id resend is retried, and the receiver runs the work once | toy: link drops the first reply; sender resends the same id | `delivered after 2 attempt(s)`; ids `note-1, note-1`; receiver ran **1** time |
| A bounded retry stops and reports failure | toy: link never replies; `maxAttempts: 3` | `gave up after 3 attempt(s)`; receiver still ran **1** time |
| A *new* id per attempt makes the work run twice (why the id must not change) | toy mutation: `id + "-" + attempt` | ids `note-1-1, note-1-2`; receiver ran **2** times |
| A per-attempt wait can be bounded and detected | `await task.WaitAsync(TimeSpan.FromMilliseconds(200))` on a never-completing task | `TimeoutException after 216 ms` |
| An overall token cancels the wait the same way | `await task.WaitAsync(cts.Token)` with `CancellationTokenSource(200ms)` | `OperationCanceledException after 200 ms` |
| A pre-cancelled token wins over a later timeout | `WaitAsync(TimeSpan.FromSeconds(5), cancelled.Token)` | `OperationCanceledException` |
| A zero backoff runs instantly in tests | `await Task.Delay(TimeSpan.Zero)` | returned in `1 ms` |
| The bus interface has no retry parameter to inherit | read `src/Messaging/IMessageBus.cs` | `PublishAsync(topic, payload, family, ct)` only |
| The receiver already dedupes by `MessageId` | read `src/Messaging/IdempotencyGuard.cs` + `src/ShuttleSim/ShuttleEndpoint.cs:56-73`; review 05c mutation-proved it | guard checked **before** `StartMove`; duplicate re-acked, not re-run |
| The contract stays v2; no alarm message exists to send | read `docs/contract/v2.md` | `Version: 2`; kinds `moveCommand`/`commandAck`/`shuttleStatus` only |
| The existing overall bound is the one kept | read `src/FleetManager/Program.cs:33-35`; `src/FleetManager/appsettings.json` | `FleetManager:RoundTripTimeoutMs = 10000`, validated positive |

Not yet verified:

- The owner's `RetryPolicy`, `ReliableCommandSender`, and their tests — this is the assignment.
- A **lost-ack retry over a real broker** — the deterministic proof is the fake-bus test; the real-broker phase proves the integrated round trip and the give-up path. Not measured here because the code does not exist yet.
- Exponential backoff with jitter — deferred by Survey B.
- The wire alarm, recovery, and heartbeats — later M2/M5, contract v3.

---

## 11. What would change this choice

- **Many managers or many shuttles sharing a broker** → exponential backoff with jitter (Survey B's rejected option); the change trigger is M8's load test, or a second fleet-manager process.
- **A rejected command should sometimes be retried** (for example, a lane that may free up) → a task-lifecycle retry policy in M3, linking here; this record keeps rejection definitive.
- **Alarms become a wire message** → contract v3 and a new record; the give-up outcome here becomes the trigger for an `AlarmMessage`, and this record's local failure is replaced.
- **The status wait needs its own timeout separate from the ack timeout** → record 08's `RoundTripTimeoutMs` split into two keys; a new record links here.
- **Retry needs to survive a process restart** → the retry state (attempt count, pending command) would have to be persisted; that is M3's persistence, and this record's in-memory retry becomes its input.

---

## 12. What this record unblocks

- Lesson 06 can fix the exact shapes and names its acceptance greps for: `RetryPolicy`, `ReliableCommandSender`, `CommandSendStatus`, `CommandSendResult`, and the four config keys.
- M2's done-when — *"a lost acknowledgement is retried"* — becomes testable: a fake-bus test drops the first ack and asserts the same `MessageId` is sent twice and the work runs once.
- The fleet manager can now send a command that is not silently lost, which every later milestone's commands (M3 task assignment, M5 recovery) stand on.
- The receiver half (record 09) and the sender half (this record) are now both written down, so the reliability loop is complete and explainable.
