# Lesson 19: Wiring the recovery round trip (operator says "recover")

**Milestone:** M5 (Faults and recovery) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 18: Operator-acknowledged recovery](18-operator-acknowledged-recovery.md) · **Decision:** [record 27](../decisions/27-recovery-wiring-and-qos-families.md); builds on [record 26](../decisions/26-operator-acknowledged-recovery.md), [record 10](../decisions/10-command-retry-and-ack-timeout.md), [record 09](../decisions/09-command-acknowledgement-and-idempotency.md), [record 22](../decisions/22-serve-loop-and-alarm-publish.md)

**Concepts this assignment requires:**

- [ ] **A recovery command is a retried command with a linked answer.** The manager publishes a `recoveryCommand`, waits for a `recoveryAck` that names the command it answers, re-sends on silence, and the operator's voice today is a `recover` CLI command. Taught in [1) The recovery round trip on the wire](#1-the-recovery-round-trip-on-the-wire).
- [ ] **The shuttle endpoint grows a second command path.** A second topic subscription and a second handler, each validating its own message kind, ending in `Recover` plus a healthy status. Taught in [2) The shuttle gets a second command path](#2-the-shuttle-gets-a-second-command-path).
- [ ] **A recovery that already happened still answers "yes".** Duplicates and double-clicks must not look like failures; the state machine itself makes the handler safe to run twice. Taught in [3) A recovery that already happened still answers yes](#3-a-recovery-that-already-happened-still-answers-yes).

Three new concepts. That is the cap.

**Not in this lesson:** `palletNotDetected` (there is still no load or unload command on the wire — a later slice with its own record and probably a contract bump); releasing occupancy after recovery (record 17 and record 26 §5 keep the physical position occupied); mid-move or lost-communication recovery; the dashboard (M7 — its REST route will call the same sender built here); alarm-acknowledgement storage; and **any contract change** — `v4.md` already documents both kinds, so `ContractVersion.Current` stays `4` and `docs/contract/` is untouched.

**The end-product fact:** After lesson 18, a faulted shuttle *can* clear itself, and the contract *can* describe the request. But no process in this repo sends that request, and no code answers one. A fleet manager that owns a recovery command it never sends is like a warehouse with a fire alarm button that is not wired to anything. This lesson wires the button.

---

## Where this fits

M5 says: *"every injected fault leads to the right alarm and the right recovery, and the saved state never loses or duplicates a pallet."* Lessons 13–17 built the alarms and the reassignment. Lesson 18 built `Shuttle.Recover` and the v4 recovery kinds. The right recovery for a `blockedPath` fault still has no trigger: the operator has no way to say "go ahead" that reaches a shuttle.

The wire pieces already exist. What is missing is the path: operator → fleet manager → `recoveryCommand` topic → shuttle → `Recover` → `recoveryAck` topic → fleet manager. Each half of that path reuses a pattern you already know: the move command's retry loop on the manager side, and the move handler's validate-then-act shape on the shuttle side.

**Forward references.** M7's dashboard will call the same sender from a REST route — that is why the sender is a class, not inline code. The M5 done-when proof will run exactly the demo in this lesson's stretch goal: fault, alarm, `recover`, healthy status. Later, when load and unload commands appear on the wire, this second command path becomes a third, and the shape repeats.

---

## Verification table

Everything below was checked today, in this repo or in a scratch copy under `/tmp/opencode`. The scratch copy was deleted afterwards.

| Claim | How I checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| Baseline suite is green | clean copy of `src` in `/tmp/opencode/l19base`; `dotnet build` then `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, 0 warnings, 0 errors; 8 `Passed!`, 0 `Failed!`, **139** tests (FleetManager 52, ShuttleCore 31, FleetCore 22, Messaging 10, Contracts 8, ShuttleSim 7, TrafficCore 6, MqttMessaging 3) |
| No recovery handler or sender exists | `grep -rn "HandleRecovery\|RecoverySender" src --include=*.cs` | no matches outside the constants, the topics, and the lesson-18 tests |
| The endpoint handles move commands only | read `src/ShuttleSim/ShuttleEndpoint.cs:38` | `envelope.MessageType != MessageTypes.MoveCommand` → return |
| The shuttle listens on one command topic | read `src/ShuttleSim/Program.cs:95` | one `SubscribeAsync(topics.Command(shuttleId), MessageFamily.Command, ...)` |
| The manager CLI has `serve` and `move` only | read `src/FleetManager/Program.cs:14-23` | two branches, usage line lists both |
| v4 already documents both recovery kinds | read `docs/contract/v4.md` sections `RecoveryCommandMessage` and `RecoveryAckMessage` | present; ack example uses `SenderId: "SH-01"`, `CorrelationId` names the command |
| Four delivery-guarantee knobs (`Command`, `Ack`, `Status`, `Alarm`); Recovery maps to Command | read `src/MqttMessaging/MqttMessageBusOptions.cs` and `src/MqttMessaging/MqttMessageBus.cs:103` | `QosOptions(Command, Ack, Status, Alarm)`; `MessageFamily.Recovery => _options.Qos.Command` |
| The bus mapping test exists and is load-bearing | read `src/MqttMessaging.Tests/MqttMessageBusTests.cs:113-159` | `Publish_with_Recovery_family_uses_correct_QoS` observes the delivery guarantee the bus actually sends, via the in-process broker |

Not yet verified: the code this lesson asks you to build.

---

## Options survey (design choices)

### Survey 1: How does the operator's acknowledgement reach the shuttle?

| | A: `recover` CLI command (chosen) | B: alarm-ack inside `serve` | C: REST endpoint now |
|---|---|---|---|
| Exists an input channel today | **yes** — same shape as `move` | no — nothing feeds the serve loop | no — REST is M6/M7 |
| Matches "operator acknowledges" | **yes** — the operator runs it | yes, in theory | yes |
| Milestone cost | tiny | builds M7 inside M5 | reorders M6/M7 into M5 |

The `move` command already proves the pattern: connect, publish, wait, exit. Recovery copies it.

### Survey 2: New sender class, or generalize the move sender?

| | A: new `RecoverySender` (chosen) | B: generalize `ReliableCommandSender` | C: shared base class |
|---|---|---|---|
| Risk to the green move path | **none** | refactor of reviewed code | refactor of reviewed code |
| Size a student can defend | one small class | one class + a new seam | two classes + a layer |
| Duplication cost | ~40 lines, twice | none | none |
| Real fleet systems | common — one sender per command kind | common at scale | common at scale |

Two copies of a small retry loop is the accepted cost now. The usual rule is to wait for a **third** correlated command kind before extracting the shared core.

### Survey 3: What does the shuttle answer when it has nothing to recover?

| | A: `Accepted: true`, change nothing (chosen) | B: `Accepted: false`, "cannot recover while idle" | C: stay silent |
|---|---|---|---|
| Truthful to the operator's goal | **yes** — shuttle is not faulted | no — the goal holds | says nothing |
| What the manager reports | success | failure for a success | retries, then "no acknowledgement" |
| Contract fit | `Reason` is null when accepted | invents a rejection the contract does not need | burns all retry attempts |

Full reasons are in [record 27 §5](../decisions/27-recovery-wiring-and-qos-families.md).

**Confirming against the installed code, not memory.** The delivery-guarantee facts above come from reading `MqttMessageBusOptions.cs` and `MqttMessageBus.cs`, and from the in-process-broker test that observes the guarantee on the wire. The retry behaviour comes from reading `ReliableCommandSender.cs`: same payload every attempt, wait `AckTimeout`, backoff, stop at `MaxAttempts`.

---

## Decision

[Record 27](../decisions/27-recovery-wiring-and-qos-families.md) settles all of it: the `recover` CLI as the operator's voice (A); a separate `RecoverySender` mirroring `ReliableCommandSender` (B); **the recovery command and the recovery ack use two different family labels — the command the Recovery one, the ack the Ack one** (C; the labels are defined in section 1 of the lecture below); **"already healthy" answers `Accepted: true` with no state change** and the recovery handler needs no message-id list (D); and a standing rule that contract versions never reorder their sections (E). Read record 27 before you write code — every requirement below is already decided there, so you never have to invent a product choice on your own.

---

## The lecture

Today we are going to learn about **the recovery round trip on the wire**, **the shuttle's second command path**, and **why a recovery that already happened still answers yes**.

### 1) The recovery round trip on the wire

A round trip is: send a message, get an answer, know which answer belongs to which message. Move commands already do this. Recovery does it too, with its own topic branch and its own message kinds.

Two words first, because the code uses them. **QoS** (quality of service) is the broker's delivery guarantee for one message: never, at least once, or exactly once. **`MessageFamily`** is a small label in our code — `Command`, `Ack`, `Status`, `Alarm`, `Recovery` — that the bus turns into a QoS level from configuration. It never appears on the wire; it is our local choice of how careful to be with each publish.

The manager side has three jobs.

**Job one: build the command.** The envelope is made the same way every other envelope is made — one helper, no hand-built envelopes:

```csharp
// src/FleetManager/Program.cs (inside the new recover branch, mirroring RunMoveAsync)
var commandEnvelope = MessageEnvelopes.Create(
    MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow);
var command = new RecoveryCommandMessage(commandEnvelope, shuttleId);
```

- `MessageEnvelopes.Create` stamps the current contract version, a fresh `MessageId`, and the sender.
- The message id matters later: the ack will name it.
- `RecoveryCommandMessage` is the plain record from lesson 18. No new contract work.

**Job two: publish and wait.** This is `ReliableCommandSender`'s loop with recovery's labels. The core of it, in one small chunk:

```csharp
// src/FleetManager/RecoverySender.cs (shape to build — the loop, not the whole file)
var payload = JsonSerializer.Serialize(command);   // serialize ONCE
for (var attempt = 1; attempt <= _policy.MaxAttempts; attempt++)
{
    await _bus.PublishAsync(
        _topics.RecoveryCommand(command.ShuttleId),
        payload,
        MessageFamily.Recovery,                    // command direction
        cancellationToken);
    var ack = await waiter.Task.WaitAsync(_policy.AckTimeout, cancellationToken);
    return ack.Accepted ? Acknowledged(...) : Rejected(...);
}
```

- The payload is serialized **once**, outside the loop. Every attempt is the same bytes and the same `MessageId`. That is what makes a re-send a duplicate instead of a new command.
- `_topics.RecoveryCommand(shuttleId)` is `shuttle/{id}/recoveryCommand` — the per-shuttle branch lesson 18 added. No topic string is typed by hand.
- `MessageFamily.Recovery` is the command direction. The bus maps it to the configured Command QoS. The existing test `Publish_with_Recovery_family_uses_correct_QoS` pins that mapping.
- `WaitAsync(_policy.AckTimeout)` throws `TimeoutException` on silence, and the loop re-sends. The retry policy comes from configuration, never from a number written in code.

**Job three: recognize the ack.** The waiter is keyed by the command's `MessageId`. The ack handler looks for that key in the incoming `CorrelationId`:

```csharp
// src/FleetManager/RecoverySender.cs (shape to build)
var envelope = ... deserialize the envelope ...;
if (envelope.MessageType != MessageTypes.RecoveryAck ||
    string.IsNullOrWhiteSpace(envelope.CorrelationId))
    return;                                        // not ours, not a problem
if (_waiters.TryGetValue(envelope.CorrelationId, out var waiter))
    waiter.TrySetResult(deserializedAck);
```

- `CorrelationId` on the ack equals `MessageId` on the command. That is the link between a question and its answer, exactly as in `v4.md`'s example.
- An ack for an unknown id is ignored, not an error. Late and duplicate acks are normal on a real network.

**And the operator's voice.** Today it is a CLI command, mirroring `move`:

```csharp
// src/FleetManager/Program.cs (new branch, next to the move one)
if (args.Length != 2 || args[0] != "recover")
{
    Console.Error.WriteLine("Usage: FleetManager recover <shuttleId>");
    return 2;
}
return await RunRecoverAsync(args[1]);
```

`RunRecoverAsync` looks like `RunMoveAsync`: read the same configuration keys, connect, subscribe `AllRecoveryAcks` with `MessageFamily.Ack`, subscribe `AllStatuses`, send, wait for the ack, then wait for a healthy status from that shuttle, print the outcome, exit 0 or 1. In M7, a dashboard button calls this same sender; the CLI stays as the operator's terminal tool.

**Why `MessageFamily.Ack` for the manager's subscription?** Because the thing arriving is an ack. Acks get Ack QoS. If you subscribed with `MessageFamily.Recovery` instead, the broker would deliver the ack at Command QoS — and if an operator ever lowered Command for throughput, recovery acks would get cheaper at the same time. Role, not flow. That is [record 27 §C](../decisions/27-recovery-wiring-and-qos-families.md).

**Where this is used later.** M7's REST route and the M5 done-when demo both call this sender. Nothing else in the system may send a `recoveryCommand` — one door.

### 2) The shuttle gets a second command path

Today the shuttle sim listens on one topic: `shuttle/{id}/command`, for move commands. Recovery rides a different topic, `shuttle/{id}/recoveryCommand`, so it needs its own subscription:

```csharp
// src/ShuttleSim/Program.cs (next to the existing SubscribeAsync for commands)
await bus.SubscribeAsync(topics.RecoveryCommand(shuttleId), MessageFamily.Recovery,
    (message, ct) =>
        connectedEndpoint.HandleRecoveryCommandAsync(message.Payload, DateTime.UtcNow, ct),
    CancellationToken.None);
```

- The topic and the family match the contract: a command-kind message on the command branch, at Command QoS.
- The handler is a **new method**, not an `if` bolted onto `HandleCommandAsync`. One handler per message kind. Each one validates its own kind, so a message can never be processed by a handler that does not understand it.

The new handler starts with the same validation dance as the move handler. The envelope is probed first, before the full body is trusted:

```csharp
// src/ShuttleSim/ShuttleEndpoint.cs (new method, first steps)
// 1. Parse the envelope (the EnvelopeWrapper trick the move handler uses).
// 2. Require: non-empty MessageId, ContractVersion.Current,
//    MessageType == MessageTypes.RecoveryCommand.
// 3. Parse the RecoveryCommandMessage body.
// 4. Require: body's Envelope.MessageId matches, and ShuttleId == my id.
```

- The topic already says `recoveryCommand`, but the **kind in the envelope** is checked anyway. A topic is an address; the envelope is the letter. A misbehaving publisher can put the wrong letter at the right address, and the handler must notice.
- `ShuttleId` must be this shuttle. A command for `SH-02` that arrives on `SH-01`'s branch is dropped with no reply — it is not ours to answer.

Then the act. The shuttle is faulted, so the goal is one transition and two messages:

```csharp
// src/ShuttleSim/ShuttleEndpoint.cs (the act — for the faulted case)
// try { _shuttle.Recover(now); }
// catch (ShuttleRejectedException ex) { publish recoveryAck: Accepted=false, Reason=ex.Message; return; }
// publish recoveryAck: Accepted=true, Reason=null, CorrelationId = command's MessageId,
//                      SenderId = my shuttle id, on _topics.RecoveryAck(_shuttleId),
//                      with MessageFamily.Ack
// publish the healthy status with the existing PublishStatusAsync(now, ct)
```

Read it as a story: clear the fault; if the state machine refuses (it should not — we checked `Fault` first), say so honestly; otherwise answer yes, then tell the world the shuttle is healthy.

- The ack is built with `MessageEnvelopes.Create(MessageTypes.RecoveryAck, _shuttleId, sentAt, command.Envelope.MessageId)` — the fourth argument is the correlation, same as the move ack.
- The ack is published with `MessageFamily.Ack`: it is an acknowledgement, so it rides Ack QoS. [Record 27 §C](../decisions/27-recovery-wiring-and-qos-families.md) and the assignment's tests pin this.
- The healthy status is what clears the alarm latch. Lesson 15's latch rule: an alarm clears when a status arrives without that condition. An `idle` status with `FaultReason: null` is that status for `blockedPath`.

**Where this is used later.** When load and unload commands join the wire, this becomes a third handler and a third subscription, and every sentence above repeats with different names.

### 3) A recovery that already happened still answers yes

The network lies. Not maliciously — messages get re-sent. The manager re-sends a `recoveryCommand` when the ack is late. The broker can deliver a copy. And operators double-click.

So the handler will run when there is nothing to do. Here is the rule that makes that safe:

```csharp
// src/ShuttleSim/ShuttleEndpoint.cs (the branch for every non-fault state)
// if (_shuttle.State != ShuttleState.Fault)
// {
//     publish recoveryAck: Accepted=true, Reason=null   (correlated, as always)
//     return;                                            // nothing changed: no status
// }
```

- **Success is the truth.** The operator's goal is "this shuttle is not faulted". That already holds.
- **No state changed, so no new status.** The shuttle's regular heartbeat publishes its idle status anyway.
- **`Reason` stays null.** The contract says `Reason` explains a rejection and is null when accepted. A success with a reason string would contradict the page the M7 dashboard is built from.

**Now the deeper idea: why this handler needs no message-id list.** Look at the two commands side by side.

`StartMove` is **not** safe to run twice. A second run would start a second move from a position that already changed. That is why the move handler keeps an `IdempotencyGuard`: a list of message ids it has already completed, and a duplicate is acked from the list without touching the shuttle.

`Recover` **is** safe to run twice, because of where it acts. It only acts from `Fault`. After the first run, the shuttle is `Idle`, and every later run falls into the "already healthy" branch above. The precondition is the guard. At-least-once delivery goes in; exactly one effect comes out. That idea has a name — the handler is **idempotent by precondition** — and you just derived it.

So the recovery handler does **not** touch `_guard`. Two protections for one job would make it unclear which one is load-bearing when a test breaks.

**What belongs on the wire, and what does not.** `Recover` throws `ShuttleRejectedException` when the shuttle is not in `Fault`. That is a **state-machine rejection** — the shuttle telling your code "that transition is not allowed here". It is not automatically a **wire rejection**. The endpoint decides what the operator sees: the already-healthy case answers success; a genuine unexpected rejection (the defensive `catch`) answers `Accepted: false` with the exception message. Keep those two ideas apart and the next command kind — pause, resume, load, unload — will not confuse you either.

**Where this is used later.** M5's done-when says the saved state "never loses or duplicates a pallet". A recovery that ran twice and moved something would be exactly that duplication. This branch is the line of code that prevents it.

---

## Recap in plain words

- The manager sends a `recoveryCommand` on `shuttle/{id}/recoveryCommand`, waits for a correlated `recoveryAck`, and re-sends the same bytes on silence. The operator's voice today is `FleetManager recover <shuttleId>`.
- The command rides `MessageFamily.Recovery`; the ack rides `MessageFamily.Ack`. Role, not flow.
- The shuttle subscribes to its recovery topic with its own handler. It validates kind, version, id, and shuttle, then clears the fault, answers yes, and publishes a healthy status.
- A recovery command that arrives when the shuttle is already fine still answers `Accepted: true` and changes nothing. The state machine is the guard, so there is no message-id list.
- A success carries no reason string; only a genuine rejection does.

---

## Worked example (toy — NOT the assignment)

This toy shows one idea only: a handler safe to run twice because it only acts in the fault state. It is not the endpoint, has no MQTT, no envelope, and no project types. The assignment is the real thing.

Create it and run it:

```bash
cd /tmp/opencode
dotnet new console -o recovery-toy
# replace Program.cs with the listing below
dotnet run --project recovery-toy
```

`Program.cs` (exactly as I ran it; `dotnet new console` already sets `ImplicitUsings`, so there are no `using` lines):

```csharp
// TOY - not the assignment. No MQTT, no envelope, no real project types.
// It shows one idea: a recovery handler that is safe to run twice
// because it only acts when the shuttle is actually faulted.

var shuttle = new ShuttleToy(ToyState.Fault);

// Delivery 1: the shuttle was faulted, so recovery runs.
var (accepted1, _) = shuttle.HandleRecovery();

// Delivery 2: the manager re-sent the same command because its ack was lost.
// The shuttle is idle now, so this delivery changes nothing.
var (accepted2, _) = shuttle.HandleRecovery();

// A second operator click: a brand-new command, not a re-delivery.
var (accepted3, _) = shuttle.HandleRecovery();

Console.WriteLine($"delivery 1: accepted={accepted1} state={shuttle.State}");
Console.WriteLine($"delivery 2: accepted={accepted2} state={shuttle.State}");
Console.WriteLine($"operator 2: accepted={accepted3} state={shuttle.State}");
Console.WriteLine($"recovery actually ran {shuttle.RecoveryRuns} time(s)");

enum ToyState { Idle, Moving, Fault }

sealed class ShuttleToy
{
    public ToyState State { get; private set; }
    public int RecoveryRuns { get; private set; }

    public ShuttleToy(ToyState state) => State = state;

    // The wire answer for one recovery command.
    public (bool Accepted, string? Reason) HandleRecovery()
    {
        if (State != ToyState.Fault)
            return (true, null);          // already healthy: success, nothing to do

        State = ToyState.Idle;            // the one real effect
        RecoveryRuns++;
        return (true, null);
    }
}
```

Output, from the run I did in `/tmp/opencode/l19toy`:

```text
delivery 1: accepted=True state=Idle
delivery 2: accepted=True state=Idle
operator 2: accepted=True state=Idle
recovery actually ran 1 time(s)
```

Three commands in, three successes out, one effect. That one line — `recovery actually ran 1 time(s)` — is the whole lesson in miniature.

---

## Say it out loud

Answer these out loud, the way you would in an interview. No notes.

1. The manager re-sends a `recoveryCommand` because the ack was late. The shuttle already recovered. Which branch answers, and why would answering `Accepted: false` make the operator's next ten minutes worse?
2. The recovery *command* is published with `MessageFamily.Recovery` and the recovery *ack* with `MessageFamily.Ack`. Why does the family follow the message's role instead of the flow it belongs to, and what silent config drift does the other choice allow?
3. The handler checks the envelope's `MessageType` even though the topic already says `recoveryCommand`. What real defect does that check catch, and what does an operator see if the check is dropped?
4. The move handler needs `IdempotencyGuard`; the recovery handler does not. What property of `Recover` makes the list unnecessary, and what would break if `Recover` acted from any state?
5. After a successful `Recover`, the endpoint publishes one healthy status. Trace how that clears the `blockedPath` alarm latch, and say what the operator still sees on the dashboard if the status publish is skipped and the shuttle's next heartbeat is a second away.

---

## The assignment

Wire the recovery round trip end to end, per [record 27](../decisions/27-recovery-wiring-and-qos-families.md). Language: C#. The contract does **not** change: `ContractVersion.Current` stays `4`, `docs/contract/` is untouched.

### Requirements

1. **`src/FleetManager/RecoverySender.cs`** — a new class mirroring `ReliableCommandSender`'s shape. `ReliableCommandSender` itself and its tests do not change.
   - Constructor: `(IMessageBus bus, TopicScheme topics, RetryPolicyExtensions.RetryPolicy policy)`.
   - `Task OnRecoveryAckReceivedAsync(MessageReceived message, CancellationToken ct)` — ignore anything whose envelope `MessageType` is not `MessageTypes.RecoveryAck` or whose `CorrelationId` is blank; otherwise complete the waiter keyed by that `CorrelationId`. Waiters are keyed by the **command's** `MessageId`, exactly as in the move sender.
   - `Task<RecoverySendResult> SendAsync(RecoveryCommandMessage command, CancellationToken ct)` — serialize once; each attempt publishes the same payload on `_topics.RecoveryCommand(command.ShuttleId)` with `MessageFamily.Recovery`; wait `_policy.AckTimeout`; on timeout, backoff and re-send; stop at `_policy.MaxAttempts`. `Accepted: false` stops the loop (rejected, not retried). Remove the waiter in `finally`.
   - `RecoverySendResult` is a small record: `(CommandSendStatus Status, int Attempts, RecoveryAckMessage? Acknowledgement)`. Reuse the existing `CommandSendStatus` enum; do not add a new one.
   - No hard-coded topics, timeouts, attempts, or backoff: topics from `TopicScheme`, timing from the retry policy.

2. **`src/FleetManager/Program.cs`** — a `recover <shuttleId>` branch next to `move`, and a usage line that mentions it.
   - Build the envelope with `MessageEnvelopes.Create(MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow)`.
   - Subscribe `topics.AllRecoveryAcks` with `MessageFamily.Ack` → `sender.OnRecoveryAckReceivedAsync`; subscribe `topics.AllStatuses` with `MessageFamily.Status` (mirror how `move` waits for a status).
   - Use the same configuration keys as `move` (`RoundTripTimeoutMs`, `AckTimeoutMs`, `MaxAttempts`, `BackoffMs`). No new keys unless you can justify them; no numbers in code.
   - Exit codes mirror `move`: 0 acknowledged with a healthy status from that shuttle; 1 rejected, no acknowledgement, or timeout.

3. **`src/ShuttleSim/ShuttleEndpoint.cs`** — `public async Task HandleRecoveryCommandAsync(string payload, DateTime now, CancellationToken ct)`.
   - Validate in this order, and do not trust the body before the envelope: non-empty `MessageId`; `ContractVersion == ContractVersion.Current`; `MessageType == MessageTypes.RecoveryCommand`; then the body's envelope id matches and `ShuttleId == _shuttleId`. Drop silently on any failure, like the move handler.
   - If the shuttle is **not** in `Fault`: publish `RecoveryAckMessage` with `Accepted: true`, `Reason: null`. Change nothing. Publish no status.
   - If it **is** in `Fault`: `Recover(now)`; catch `ShuttleRejectedException` and answer `Accepted: false` with the exception message; otherwise answer `Accepted: true`, `Reason: null`, then publish the healthy status with the existing `PublishStatusAsync(now, ct)`.
   - The ack goes to `_topics.RecoveryAck(_shuttleId)` with `MessageFamily.Ack`, built via `MessageEnvelopes.Create(MessageTypes.RecoveryAck, _shuttleId, sentAt, command.Envelope.MessageId)`.
   - Do **not** use `IdempotencyGuard` here (concept 3).

4. **`src/ShuttleSim/Program.cs`** — a second subscription: `topics.RecoveryCommand(shuttleId)`, `MessageFamily.Recovery`, calling `HandleRecoveryCommandAsync`, with a log line in the style of the existing one.

5. **Tests** (pure, no `Integration` trait):
   - `src/ShuttleSim.Tests/ShuttleEndpointTests.cs`, three new facts:
     - `Recovery_command_on_a_faulted_shuttle_recovers_and_reports_idle` — fault the shuttle the way the existing blocked-lane test does; deliver a recovery payload; assert the published `recoveryAck` has `Accepted: true`, `CorrelationId == the command's MessageId`, `SenderId == "SH-01"`, and family `MessageFamily.Ack`; assert a status with state `idle` and null `FaultReason`.
     - `Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing` — on an idle shuttle: one accepted ack, `Reason` null, state still `idle`, position unchanged, no status published.
     - `Recovery_command_for_another_shuttle_is_ignored` — payload addressed elsewhere publishes nothing.
   - `src/FleetManager.Tests/RecoverySenderTests.cs` — mirror the three `ReliableCommandSenderTests` facts: a lost ack is re-sent with the **same** `MessageId` on the recovery topic with `MessageFamily.Recovery`; retries stop after the configured attempts; an `Accepted: false` ack is not re-sent. The existing `FakeBus` in that file plans `CommandAckMessage` responses — extend it (or add a sibling) for `RecoveryAckMessage`.
   - **Pin wire names with literals**, as `AlarmMessageTests` and `RecoveryMessageTests` do: `Assert.Equal("recoveryAck", MessageTypes.RecoveryAck);` style, not constant-vs-itself. A roundtrip alone cannot catch a rename — review 18 proved that with six mutations.
   - **Pin the QoS families at the call sites**: the endpoint test asserts the published ack's family is `MessageFamily.Ack`; the sender test asserts the published command's family is `MessageFamily.Recovery`. The bus-side mapping is already pinned by `Publish_with_Recovery_family_uses_correct_QoS`; do not duplicate that test.

6. **One-line README honesty fix** (from review 18's leftover list): `src/Contracts/README.md` still lists the kind strings without the two recovery kinds — add `recoveryCommand` and `recoveryAck` where the other kinds are listed. Nothing else in the READMEs changes.

### Acceptance criteria

```bash
# Build and pure suite: RC 0, zero failures. Baseline is 139;
# with the six facts above you land at or above 145.
cd src && dotnet build
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"

# The pieces exist and use the decided names
test -f src/FleetManager/RecoverySender.cs
grep -q "RecoverySendResult" src/FleetManager/RecoverySender.cs
grep -q "MessageFamily.Recovery" src/FleetManager/RecoverySender.cs
grep -q "AllRecoveryAcks" src/FleetManager/Program.cs
grep -q "recover" src/FleetManager/Program.cs
grep -q "HandleRecoveryCommandAsync" src/ShuttleSim/ShuttleEndpoint.cs
grep -q "MessageTypes.RecoveryAck" src/ShuttleSim/ShuttleEndpoint.cs
grep -q "MessageFamily.Recovery" src/ShuttleSim/Program.cs

# The contract did not move
grep -q "Current = 4" src/Contracts/MessageTypes.cs
git diff --stat docs/contract   # must print nothing

# The move path did not move
git diff --stat src/FleetManager/ReliableCommandSender.cs src/Messaging/IdempotencyGuard.cs   # must print nothing

# The new tests exist and pin the families
grep -rq "Recovery_command_on_a_faulted_shuttle" src/ShuttleSim.Tests
grep -rq "Recovery" src/FleetManager.Tests/RecoverySenderTests.cs
grep -qi "retr" src/FleetManager.Tests/RecoverySenderTests.cs
grep -q "MessageFamily.Ack" src/ShuttleSim.Tests/ShuttleEndpointTests.cs
```

Mutation check before you call it done (run each in a scratch copy, never in the workspace): (a) make the endpoint answer `Accepted: false` on the healthy path → the healthy-shuttle test must go red; (b) publish the ack with `MessageFamily.Recovery` → the family assertion must go red; (c) re-serialize the command inside the sender loop and change the `MessageId` per attempt → the retry test must go red; (d) drop the `MessageType == MessageTypes.RecoveryAck` filter in `OnRecoveryAckReceivedAsync` and feed it a move ack → the sender must not complete a waiter. If a mutation survives, the test is decoration; fix the test.

### Stretch goal

Run the M5 demo for real, and keep the output in your review notes:

```bash
docker compose -f docker/compose.yml up -d
# Terminal 1: shuttle with a blocked lane (set Shuttle:BlockedLanes in ShuttleSim config, e.g. via env)
dotnet run --project src/ShuttleSim -- serve
# Terminal 2: the serving fleet manager, so the blockedPath alarm is published and latched
dotnet run --project src/FleetManager -- serve
# Terminal 3
dotnet run --project src/FleetManager -- move SH-01 <blockedLane>   # → exit 1, reason "blocked path"; alarm on shuttle/SH-01/alarm
dotnet run --project src/FleetManager -- recover SH-01              # → exit 0, healthy idle status on the wire
docker compose down
```

That sequence — fault, alarm, operator, recovery, healthy status — is exactly the shape of M5's done-when proof. The full proof (saved state never loses or duplicates a pallet) needs the pallet work that comes later; the recovery path is what this lesson adds to it.

**Not in this assignment:** `palletNotDetected`, occupancy release, mid-move recovery, dashboard or REST, alarm-ack storage, changes to `ReliableCommandSender` or `FleetServeLoop`, and any contract edit.
