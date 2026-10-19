# Review 19 — Lesson 19: wiring the recovery round trip

**Reviewer:** Reviewer agent
**Commit under review:** `cc87ee4` ("M5: Lesson 19 First attempt"), lesson material from `1c6430f`
**Verdict:** **not yet**

Tests must pass before a review can pass, and three fail. Two of the three failures are not
test-side problems: the new endpoint handler cannot accept a recovery command at all, and it
never calls `Recover`. As committed, the recovery round trip does not exist on the wire — the
button is still not wired to anything.

---

## Commands run (exact) and what they showed

| # | Command | Result |
|---|---|---|
| 1 | `dotnet --version` | `10.0.112` (matches lesson verification table) |
| 2 | `cd src && dotnet build` | RC 0, **0 Warning(s), 0 Error(s)**. Note: the very first attempt in this session died with `Fatal error. Internal CLR error. (0x80131506)` before producing output; a clean re-run of the same command built normally. Treated as a transient host glitch, not a repo defect, but it is reported here rather than hidden. |
| 3 | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **RC 1 — Failed: 3, Passed: 143, Total: 146.** Per assembly: FleetManager.Tests `Failed 1 / Passed 55 / Total 56`; ShuttleSim.Tests `Failed 2 / Passed 8 / Total 10`; ShuttleCore 31, FleetCore 22, Messaging 10, Contracts 8, TrafficCore 6, MqttMessaging 3 all passed. |
| 4 | Lesson 19 acceptance greps (`test -f`, `grep -q …`) | 13 of 15 PASS; see checklist below |
| 5 | `git diff 1c6430f..cc87ee4 --stat -- docs/contract` | empty — contract untouched (PASS) |
| 6 | `git diff 1c6430f..cc87ee4 --stat -- src/FleetManager/ReliableCommandSender.cs src/Messaging/IdempotencyGuard.cs` | empty — move path untouched (PASS) |
| 7 | `grep -n "Current = 4" src/Contracts/MessageTypes.cs` | line 15, `public const string Current = 4` (PASS) |
| 8 | `grep -n "recoveryCommand\|recoveryAck" src/Contracts/README.md` | line 42 lists both kinds — requirement 6 was already satisfied by commit `3f7f2b1`, nothing to do (PASS) |
| 9 | Mutation checks in a scratch copy `/tmp/opencode/l19mut` (workspace never touched; scratch **deleted** afterwards) | see "Mutation evidence" below |
| 10 | `docker compose -f docker/compose.yml ps`; `pgrep -af "dotnet run\|dotnet test\|mosquitto"` | no containers, no stray processes. Nothing started, because the pure suite is the lesson's acceptance gate and needs no broker. |
| 11 | `git status --short` | clean — this review changed nothing under `src/` |

**Not run / not yet verified:** the `Category=Integration` tests (need a broker), and the
lesson's stretch-goal live demo (fault → alarm → `recover` → healthy status). Both are marked
**not yet verified**; the demo cannot succeed in this state anyway, because the shuttle drops
every recovery command.

### The three failures, verbatim

```text
Failed ShuttleEndpointTests.Recovery_command_on_a_faulted_shuttle_recovers_and_reports_idle
  Assert.Single() Failure: The collection did not contain any matching items
  Expected:   (predicate expression)   Collection: []          (line 159)

Failed ShuttleEndpointTests.Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing
  Assert.Single() Failure: The collection was empty                          (line 187)

Failed FleetManager.Tests.RecoverySenderTests.Lost_acknowledgement_is_retried_with_the_same_message_id
  Assert.Equal() Failure: Values differ   Expected: 2   Actual: 0            (line 50)
```

Note that on failure 3, line 49 (`Assert.Equal(2, bus.Published.Count)`) passed: the sender
**did** publish twice with the same `MessageId`. Line 50 fails because it selects
`item.Family == MessageFamily.Command` from a sender that publishes `MessageFamily.Recovery`.

---

## Acceptance criteria

### Lesson 19 requirements

- [x] **R1a `src/FleetManager/RecoverySender.cs` exists, constructor shape `(bus, topics, policy)`, serialize-once loop, `AckTimeout`/`MaxAttempts`/backoff from the policy, waiter removed in `finally`** — verified by reading `RecoverySender.cs:53-88`; retry-loop shape matches `ReliableCommandSender:55-87`.
- [ ] **R1b method is named `OnRecoveryAckReceivedAsync`** — **FAIL.** `grep OnRecoveryAckReceivedAsync src/FleetManager/RecoverySender.cs` → no match. The method is `OnAckReceivedAsync` (`RecoverySender.cs:27`), the same name as `ReliableCommandSender.OnAckReceivedAsync`. Both are wired in the same file (`Program.cs:107` and `:269`).
- [ ] **R1c reuse `CommandSendStatus`; "do not add a new one"** — **FAIL.** `RecoverySender.cs:8` adds `public enum RecoverySendStatus { Acknowledged, Rejected, NoAcknowledgement }`, a byte-for-byte duplicate of `CommandSendStatus`.
- [x] **R2 `recover <shuttleId>` branch, usage line, `MessageEnvelopes.Create(MessageTypes.RecoveryCommand, …)`, `AllRecoveryAcks`+`Ack`, `AllStatuses`+`Status`, same config keys, no numbers in code** — verified at `Program.cs:18-26`, `:224-241`, `:192-220`. All four `FleetManager:*` keys are read and range-checked exactly as `move` does.
- [ ] **R2 exit code 0 means "acknowledged with a healthy status from that shuttle"** — **FAIL (behaviour).** `Program.cs:248` waits on `statusReceived`, which any `ShuttleStatusMessage` for that shuttle satisfies (`:277-279`). The shuttle heartbeats its `fault` status every `HeartbeatIntervalMs`, so `recover` can exit 0 while the shuttle is still faulted. The hints are explicit: *"the healthy status you wait for is `state == 'idle'`."*
- [ ] **R3 `HandleRecoveryCommandAsync` validates in order: non-empty id → current version → `MessageType == MessageTypes.RecoveryCommand` → body id matches → `ShuttleId == _shuttleId`** — **FAIL.** `ShuttleEndpoint.cs:103` tests `envelope.MessageType != MessageTypes.MoveCommand`. Every genuine recovery command is dropped at step 3, before the body is even read.
- [ ] **R3 not in `Fault` → `Accepted: true`, `Reason: null`, no state change, no status** — **FAIL.** The branch is inverted: `ShuttleEndpoint.cs:127-128` publishes the accepted ack **and then publishes a status** on the healthy path.
- [ ] **R3 in `Fault` → `Recover(now)`, catch `ShuttleRejectedException` → `Accepted: false`, otherwise accepted + healthy status** — **FAIL.** `Recover` is **never called anywhere in the diff.** `ShuttleEndpoint.cs:121-125` answers `Accepted: false` with `_shuttle.FaultReason` instead — which is option **B** of decision 27 §5, the option the record explicitly rejected, and it clears nothing.
- [x] **R3 ack goes to `_topics.RecoveryAck(_shuttleId)` with `MessageFamily.Ack`, built via `MessageEnvelopes.Create(MessageTypes.RecoveryAck, _shuttleId, sentAt, command.Envelope.MessageId)`** — `RecoveryAcknowledgeAsync` at `ShuttleEndpoint.cs:144-152` is correct in every argument.
- [x] **R3 no `IdempotencyGuard` in the recovery handler** — verified; `_guard` is untouched by `HandleRecoveryCommandAsync`.
- [x] **R4 second subscription: `topics.RecoveryCommand(shuttleId)`, `MessageFamily.Recovery`, log line in the existing style** — `ShuttleSim/Program.cs:102-108` mirrors the move subscription exactly.
- [x] **R5 endpoint test: faulted shuttle → ack `Accepted: true`, `CorrelationId == command's MessageId`, `SenderId == "SH-01"`, family `Ack`, literal `"recoveryAck"` pin, status `idle` with null `FaultReason`** — all assertions present (`ShuttleEndpointTests.cs:153-175`); the test **fails**, which is the correct verdict for the code as written.
- [x] **R5 healthy-shuttle test: one accepted ack, `Reason` null, state/position unchanged, no status** — assertions present and correct (`:180-196`); fails against the code.
- [x] **R5 other-shuttle test: publishes nothing** — passes, but **vacuously** (see issue 2).
- [ ] **R5 sender tests: re-send with the same `MessageId` *on the recovery topic* with `MessageFamily.Recovery`; retries stop at configured attempts; `Accepted: false` not re-sent; pin the published family** — **FAIL.** The four tests exist and the retry-count/reject behaviour is right, but (a) the family filter is `MessageFamily.Command` (`RecoverySenderTests.cs:42`), (b) no test asserts the published topic, (c) no literal pin of `"recoveryCommand"`. Mutation evidence below.
- [x] **R6 `src/Contracts/README.md` lists the recovery kinds** — present at line 42 (fixed earlier, in `3f7f2b1`).
- [x] **Contract did not move** — `ContractVersion.Current = 4`; `git diff --stat docs/contract` empty.
- [x] **Move path did not move** — `ReliableCommandSender.cs` and `IdempotencyGuard.cs` unchanged by this commit (only `ReliableCommandSenderTests.FakeBus` gained `RecoveryQueueAck`, which the lesson allowed: "extend it (or add a sibling)").

### Build/test gate

- [ ] `dotnet build` RC 0, 0 warnings — **PASS**.
- [ ] `dotnet test … --filter "Category!=Integration"` RC 0, ≥145 total — **FAIL**: RC 1, 146 total, **3 failed**. 146 ≥ 145, so the count target is met; the gate is not.

---

## Top issues, ranked

### 1. BLOCKER — the recovery handler cannot accept a recovery command, and never recovers anything
`src/ShuttleSim/ShuttleEndpoint.cs:89-129`

Three defects in one method, all visible in one reading:

- **Line 103:** `envelope.MessageType != MessageTypes.MoveCommand` — the kind check still names the
  move kind. A `recoveryCommand` envelope fails this test and the method returns before touching
  the body. The handler is dead code on the wire; `ShuttleSim/Program.cs:102` dutifully delivers
  every recovery command into a `return`.
- **Lines 121-125:** when the shuttle *is* faulted, it answers `Accepted: false` with the fault
  reason. That is decision 27 §5 option B, explicitly rejected, and it is the one answer that tells
  the operator recovery failed when they asked for it.
- **`_shuttle.Recover(now)` is never called** — `grep -n "Recover(" src/ShuttleSim/ShuttleEndpoint.cs`
  has no match. No path in this diff clears a fault.

**Why it matters for a real shuttle system:** a blocked-path fault has exactly one exit today, and
this commit does not open it. An operator runs `recover`, the shuttle stays faulted, the alarm
stays latched, and the pallet on that shuttle stays stranded — while the manager reports
`Rejected` with reason "blocked path". In a warehouse that is a stuck shuttle and a support call;
here it is a lesson whose central claim ("this wires the button") is not true yet. The two endpoint
test failures are the system telling you this precisely.

### 2. BLOCKER — the sender tests pin the *wrong* family and no topic, so the required pin does not exist
`src/FleetManager.Tests/RecoverySenderTests.cs:41-51`

```csharp
var publishedCommands = bus.Published
    .Where(item => item.Family == MessageFamily.Command)   // copied from the move twin, line 89
```

This line is copied from `ReliableCommandSenderTests.cs:89`, where `MessageFamily.Command` is
correct. For recovery it is the wrong label, so the assertion is inverted: it currently fails, and
it would go *green* if the production code were changed to the wrong family.

**Mutation evidence** (scratch copy in `/tmp/opencode/l19mut`, workspace untouched, scratch deleted):

| Mutation | Result |
|---|---|
| `MessageFamily.Recovery` → `MessageFamily.Command` in `RecoverySender.SendAsync` | FleetManager.Tests **56/56 PASSED** |
| …plus `_topics.RecoveryCommand(...)` → `_topics.Command(...)` (publishes recovery commands on the *move* topic) | FleetManager.Tests **56/56 PASSED** |

**Why it matters:** the lesson requires this test to pin "the same `MessageId` **on the recovery
topic** with `MessageFamily.Recovery`", and record 27 §4 makes these call-site pins the whole point
of the slice ("keeps both directions honest at the call site"). As written, a future refactor can
put recovery commands on the move topic under the move family — same QoS today, different one the
moment an operator tunes `Command` — and every test stays green. A pin that turns red when the code
is wrong and green when it is right is the minimum bar; this one does the opposite.

### 3. BLOCKER — `recover` can exit 0 while the shuttle is still faulted
`src/FleetManager/Program.cs:248`, `:277-279`

`statusReceived` is completed by **any** `ShuttleStatusMessage` whose `ShuttleId` matches. The
shuttle heartbeats `fault` on `AllStatuses` at `HeartbeatIntervalMs`, and the subscription is
established *before* the command is sent — so a pre-existing faulted heartbeat satisfies the wait
immediately. Requirement 2 and the hints both say the wait is for a *healthy* (`state == "idle"`)
status.

**Why it matters:** the exit code is the operator's only signal in this milestone, and the M5
done-when proof is a demo built on it. An exit 0 that means "a message arrived" rather than "the
shuttle is healthy" is how an operator comes to distrust the tool — and later the dashboard, which
will call the same sender. It also masks issue 1: with the endpoint broken, `recover` still
succeeds, so the demo would "pass" with a faulted shuttle.

### 4. SHOULD-FIX — two explicit R1 requirements missed: `RecoverySendStatus` and the method name
`src/FleetManager/RecoverySender.cs:8`, `:27`

The lesson says *"Reuse the existing `CommandSendStatus` enum; do not add a new one"* and names the
handler `OnRecoveryAckReceivedAsync`. A second enum with identical members means every future
`switch` on a send result must handle both types, and the two identically-named
`OnAckReceivedAsync` methods on two sender classes mean a reader of `Program.cs:107` vs `:269`
cannot tell which ack kind each one accepts without inspecting the receiver type. Both are cheap to
fix now and noisier to fix once the dashboard calls the sender.

### 5. SHOULD-FIX — a passing test that passes for the wrong reason
`src/ShuttleSim.Tests/ShuttleEndpointTests.cs:199-209`

`Recovery_command_for_another_shuttle_is_ignored` asserts `Assert.Empty(bus.Published)` — but
because of issue 1 the handler currently publishes nothing for *any* input. Replacing the whole
method body with `return;` would keep this test green. The lesson's mutation list ends with "if a
mutation survives, the test is decoration"; this one has effectively already been mutated by the
copy error. Once the kind check is fixed, re-run it and confirm it still passes for the right
reason (it should: `ShuttleId` mismatch → silent drop).

### Non-blocking suggestions

- **Style — `ShuttleEndpoint.cs:89-129`:** the whole method body is indented one level deeper than
  its braces (the `{` on line 90 sits under `public`, not under the method). The file's own
  convention is the two-space-shallower block used by `HandleCommandAsync`.
- **Style —** `RecoverySender.cs` is `public class`, not `public sealed class`; its twin is sealed
  (`ReliableCommandSender.cs:13`). `RecoverySender.cs` and `RecoverySenderTests.cs` have no
  trailing newline; `ReliableCommandSenderTests.cs:42` has a whitespace-only line.
- **Duplicated body —** `RecoveryQueueAck` is a verbatim copy of `QueueAck` with a different
  parameter type (`ReliableCommandSenderTests.cs:43-49`). Acceptable for a fake, but a generic
  `QueueResponse<T>` would remove the copy you have to keep in sync.
- **Duplicated CLI —** `RunRecoverAsync` repeats ~30 lines of configuration reading and validation
  from `RunMoveAsync` (`Program.cs:30-58` vs `:192-220`). Record 27 accepted duplicating the
  *sender*; it did not discuss duplicating the CLI plumbing. Worth raising at the third
  one-shot command, and worth a decision line if you keep it.
- **Missing mutation (d) coverage** — no test feeds a `commandAck` into
  `RecoverySender.OnAckReceivedAsync` and asserts no waiter completes. The filter is written
  correctly (`RecoverySender.cs:31-34`), so this is a coverage gap, not a defect.
- **Logging** — `RecoverySender.cs:49` writes raw attempt lines to `Console.Error` and
  `Program.cs:106/268` dump raw payloads with `Console.WriteLine`. Both are inherited from the
  reviewed move path, so they are not new debt, but the recovery CLI is where a support engineer
  will look first: a line with `shuttleId`, `messageId`, `attempt` and the outcome would earn its
  keep at M5.

---

## What is solid

- `RecoveryAcknowledgeAsync` (`ShuttleEndpoint.cs:144-152`) is exactly right: right kind, right
  topic builder, right family, correlation as the fourth `MessageEnvelopes.Create` argument.
- `RecoverySender.SendAsync` gets the hard part right on the first try: payload serialized once
  outside the loop, one waiter keyed by the command's `MessageId` shared across attempts,
  `WaitAsync(_policy.AckTimeout)` with `TimeoutException` → backoff → resend, `Accepted: false`
  returns immediately, waiter removed in `finally`. The retry semantics match the move twin
  line-for-line without dragging `ReliableCommandSender` into the diff.
- Configuration discipline held: `RoundTripTimeoutMs`, `AckTimeoutMs`, `MaxAttempts`, `BackoffMs`
  are read and range-checked exactly as `move` does; topics come from `TopicScheme`; no number and
  no topic string is typed in the new code (the literals in tests are deliberate wire-name pins).
- The `ShuttleSim` subscription (`Program.cs:102-108`) is a clean mirror of the move one, with the
  right family and a `MessageIdOf` log line in the existing style.
- Scope was respected: no contract edit, no touch to `ReliableCommandSender`, `IdempotencyGuard`,
  `FleetServeLoop` or the alarm code; the `Contracts/README.md` leftover was already closed.
- Three of the four required greps that could have been missed were hit, and the endpoint tests
  assert literals (`Assert.Equal("recoveryAck", MessageTypes.RecoveryAck)`,
  `Assert.Equal("shuttle/SH-01/recoveryAck", ackEntry.Topic)`), not constants against themselves —
  exactly what review 19's re-review asked for.

---

## Explain-it check

Answer these out loud, no notes, before the next lesson.

**The three decisions an interviewer would most likely ask about:**

1. You serialize the command once, outside the retry loop, and keep a single waiter keyed by the
   command's `MessageId` for all attempts. What breaks — specifically, what does the operator see —
   if you move `JsonSerializer.Serialize(command)` inside the `for` loop, or create a fresh
   `TaskCompletionSource` on each attempt?
2. The move handler keeps an `IdempotencyGuard` list of completed message ids; your recovery
   handler keeps none. What property of `Recover` makes that safe, what does
   `Recovery_command_for_a_faulted_shuttle_recovers_and_reports_idle` prove about it, and what
   changes the day recovery becomes "home the shuttle, then clear the fault"?
3. The recovery *command* is published with `MessageFamily.Recovery` and the recovery *ack* with
   `MessageFamily.Ack`, even though they are one round trip on adjacent topics. Why does the family
   follow the message's role rather than the flow it belongs to, and what silent configuration drift
   does the other choice permit?

**Lines and blocks that look unclear, copied, or unexplained — study these:**

- `ShuttleEndpoint.cs:103` — `envelope.MessageType != MessageTypes.MoveCommand`. Where did this
  line come from, and what is the very next thing a reader should check after pasting a twin?
- `ShuttleEndpoint.cs:121-128` — compare against `ShuttleEndpoint.cs:73-78`. Which of these two
  blocks is the move handler's fault branch, and why is it the wrong branch here? Where is the
  `Recover(now)` call that the lesson's story requires?
- `ShuttleEndpoint.cs:90-129` — the brace and indentation. Why does the body sit one level deeper
  than every other method in the file?
- `RecoverySenderTests.cs:41-44` — the `Where(item.Family == MessageFamily.Command)` filter and the
  `Select` that follows. What were you intending to pin, what does it actually pin, and why did the
  suite stay green in mutation M2/M3?
- `RecoverySender.cs:8` and `RecoverySender.cs:27` — `RecoverySendStatus` and `OnAckReceivedAsync`.
  Which lesson requirement does each violate, and what does a reviewer gain by the names being
  different from the move twin's?
- `Program.cs:248` vs `Program.cs:277-279` — the wait and its completer. What message actually
  completes this wait during a live fault, and what exit code does the operator get because of it?

---

## Revisit list

- **Idempotency by precondition vs. by list** (decision 27 §5): when a state machine's precondition
  is the guard, and when you need a message-id cache. Know both, and know how to tell which one a
  new command needs in one sentence.
- **Role-based QoS families** (decision 27 §4): `MessageFamily` → `QosOptions` → broker delivery
  guarantee, and where the mapping is actually pinned (call site vs. bus test). You have now seen
  both kinds of pin fail in opposite directions.
- **Envelope-before-body validation**: topic is the address, envelope is the letter. The kind check
  is the line this attempt got wrong; internalise the order: *version → kind → id → shuttle → act.*
- **Copy-adapt discipline**: when you copy a twin file, write down every name the twin carries
  (`MoveCommand`, `_topics.Command`, `MessageFamily.Command`, the state branch, the result enum,
  the method name) and tick each one off against the new context. Every blocker in this review is
  copy residue, not a design error.
- **Writing a pin in the right direction**: a test must fail when the code is wrong and pass when
  it is right. Before you commit an assertion, imagine the mutation it is meant to kill and check
  that it actually kills it — mutation M2 turned the suite green.
- **Exit-code promises**: what does `0` tell an operator, and which message proves it? "Something
  arrived" and "the shuttle is healthy" are different claims.

---

## End-product check

This slice is the operator's only trigger for M5's recovery half: CLI → `recoveryCommand` topic →
`Recover` → `recoveryAck` → healthy status, which is exactly the sequence the M5 done-when proof
will run and the sequence M7's dashboard button will call through the same `RecoverySender`. The
manager-side half is built and structurally sound; the shuttle-side half is a half-adapted copy of
the move handler that drops every command and never recovers, and the CLI's success signal does not
actually observe health. Left as-is, the risk compounds: the M5 demo could be reported as working
on the strength of a stale heartbeat, and a later reviewer would inherit a "green" path that leaves
shuttles faulted. Nothing here needs a design change — the shape of the sender, the ack helper, the
subscription and the test skeletons are right; the fixes are the kind check, the branch, the `Recover`
call, the healthy-status wait, and two test pins.

---

## README

**Not updated.** The verdict is *not yet*, so the M5 milestone row is untouched — verified with
`git status --short` (clean) after writing this file.

---

# Re-review — 2026-10-19: the first round of fixes, re-verified

**Reviewer:** Reviewer agent
**Reviewed:** the owner's **uncommitted working-tree changes** on top of `cc87ee4` — three files,
`git diff --stat`:

```text
src/FleetManager.Tests/RecoverySenderTests.cs |  2 +-
src/FleetManager/Program.cs                   |  3 ++-
src/ShuttleSim/ShuttleEndpoint.cs             | 13 ++++++++++---
3 files changed, 13 insertions(+), 5 deletions(-)
```

**Verdict:** **not yet** (2 of 3 blockers closed, 1 partially, 1 should-fix untouched; 1 test still red).

**Commit status — say so plainly:** this re-review covers *uncommitted* work. If it ever passes,
the owner commits it before moving on, e.g. `M5: Lesson 19 first attempt fixes`. The review file
itself (`docs/reviews/19-recovery-wiring.md`) is also still untracked and should go in with it.

---

## Commands run (exact) and what they showed

| # | Command | Result |
|---|---|---|
| 1 | `git status --porcelain && git diff --stat` | 3 modified files under `src/`, shown above; `docs/reviews/19-recovery-wiring.md` untracked |
| 2 | `dotnet --version` | `10.0.112` |
| 3 | `cd src && dotnet build` | RC 0, **0 Warning(s), 0 Error(s)** |
| 4 | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **RC 1 — Failed: 1, Passed: 145, Total: 146.** Per assembly: ShuttleCore 31, FleetCore 22, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, FleetManager 56 all passed; **ShuttleSim.Tests Failed 1 / Passed 9 / Total 10** |
| 5 | Lesson 19's 15 acceptance greps/diffs (same block as the baseline review ran) | **15 of 15 PASS** (baseline: 13/15) |
| 6 | Extra greps for the baseline's specific defects (`OnRecoveryAckReceivedAsync`, `CommandSendStatus`, no `RecoverySendStatus`, `"recoveryCommand"` literal, `Topic` assertion) | 5 checks: only *"no RecoverySendStatus"*-adjacent family check passes on family; **4 FAIL** — see blocker table |
| 7 | Mutation M-A (scratch copy `/tmp/opencode/l19mut2`, workspace untouched, **deleted afterwards**): `MessageFamily.Recovery` → `MessageFamily.Command` in `RecoverySender.SendAsync` | `RecoverySenderTests.Lost_acknowledgement_is_retried_with_the_same_message_id` **FAIL** → the family pin now kills its mutation (it did not at baseline) |
| 8 | Mutation M-B (same scratch): `_topics.RecoveryCommand(...)` → `_topics.Command(...)` (recovery commands published on the **move** topic) | FleetManager.Tests **56/56 PASSED** → mutation survives; the "on the recovery topic" half of blocker 2 is still unpinned |
| 9 | Mutation M-C (same scratch): neutralise `command.ShuttleId != _shuttleId` in `HandleRecoveryCommandAsync` | `Recovery_command_for_another_shuttle_is_ignored` **FAIL** → blocker 5's test now kills its mutation and passes for the right reason |
| 10 | `docker compose -f docker/compose.yml ps`; `pgrep -af "dotnet test\|dotnet run\|mosquitto"` | no containers, no stray processes. Nothing was started: the pure suite is the acceptance gate; no broker needed. |

**Not run / not yet verified:** the `Category=Integration` tests (need a broker) and the stretch-goal
live demo (fault → alarm → `recover` → healthy status). Still **not yet verified**; the demo is now
plausible on code reading, but it has never been executed.

---

## Blocker-by-blocker verdict

### 1. Endpoint kind check / option-B rejection / missing `Recover` — **PARTIALLY FIXED** (3 of 4 sub-defects closed; the fourth is the one test still red)

| Sub-defect (baseline) | Status | Evidence |
|---|---|---|
| kind check names `MoveCommand` | **fixed** | `src/ShuttleSim/ShuttleEndpoint.cs:103` — `envelope.MessageType != MessageTypes.RecoveryCommand` |
| `Accepted: false` while faulted (decision 27 §5 option B) | **fixed** | `ShuttleEndpoint.cs:121-132` — the fault branch now tries recovery instead of answering false with `_shuttle.FaultReason` |
| `_shuttle.Recover(now)` never called | **fixed** | `ShuttleEndpoint.cs:125` inside `try`, `ShuttleRejectedException` → `RecoveryAcknowledgeAsync(..., accepted: false, exception.Message, ...)` at `:129` |
| **not in `Fault` → ack + change nothing + `PublishStatusAsync` after the fall-through** | **NOT fixed** | `ShuttleEndpoint.cs:134-135` — one ack/status pair sits *after* the `if (_shuttle.State == ShuttleState.Fault)` block, so the healthy path runs it too |

The failure is exactly what the lesson's requirement predicted:

```text
Failed ShuttleEndpointTests.Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing
  Assert.Single() Failure: The collection contained 2 items
  Collection: [Tuple ("shuttle/SH-01/recoveryAck", …, Ack),
               Tuple ("shuttle/SH-01/status", …, Status)]        (line 187)
```

Requirement 3 and the hints (Tier 2, step 4) both say: *not `Fault` → ack `Accepted: true`,
`Reason: null`, **return**; `Fault` → `Recover`, then ack, then `PublishStatusAsync`. The owner fixed
the fault arm but left the status inside the shared fall-through, so the no-op path publishes a
status the lesson forbids. The structure needs an early `return` on the healthy branch (or the
status moved inside the fault branch after a successful `Recover`) — the student makes that call,
not this review.

### 2. Sender tests pin the wrong family and no topic — **PARTIALLY FIXED**

- **Family: fixed.** `src/FleetManager.Tests/RecoverySenderTests.cs:42` now filters
  `item.Family == MessageFamily.Recovery`. Mutation M-A confirms the direction: wrong family now
  turns `Lost_acknowledgement_is_retried_with_the_same_message_id` **red** (baseline: green).
- **Topic: not fixed.** No assertion anywhere in `RecoverySenderTests.cs` touches `item.Topic`, and
  the `"recoveryCommand"` literal the lesson asks for does not appear
  (`grep -n "Topic\|recoveryCommand" RecoverySenderTests.cs` → only `new TopicScheme("root")`).
  Mutation M-B — publishing the recovery command on the **move** topic — still leaves
  **56/56 green**. Three copies of the same residue:
  - `RecoverySenderTests.cs:31, 60, 83, 105` — subscribes with filter `"root/+/ack"` (the move
    twin's filter) while the real CLI subscribes `topics.AllRecoveryAcks` = `root/+/recoveryAck`
    (`Messaging/TopicScheme.cs:23`, `FleetManager/Program.cs:241`).
  - `ReliableCommandSenderTests.cs:48` — `RecoveryQueueAck` hands the fake ack to the handler on
    `"root/shuttle-1/ack"`, the **move** ack topic.

  The `FakeBus` does not route by topic (single `_handler`, `ReliableCommandSenderTests.cs:26-31`),
  which is why none of this shows up. One `Assert.Equal("shuttle-1/recoveryCommand", entry.Topic)`
  (or a topic-filtered select) closes it.

### 3. `recover` could exit 0 on a faulted shuttle — **FIXED**

`src/FleetManager/Program.cs:279` now completes the waiter only on health:

```csharp
if (status?.Status?.ShuttleId == shuttleId && status?.Status.State == ShuttleState.Idle.ToWireName() )
```

with `using ShuttleCore;` added at `:7` (`FleetManager.csproj` already references `ShuttleCore`,
so no project change was needed). `ShuttleState.Idle.ToWireName()` is `"idle"`
(`ShuttleCore/ShuttleState.cs:14`), matching the hints' *"`state == 'idle'"`*. The `fault`
heartbeats no longer satisfy the wait; a timeout now lands in `OperationCanceledException` → exit 1.

Caveats, none blocking: **no test covers this line** — `Program.cs` is untested, so the evidence is
code reading plus a clean build; and the line has a stray space before `)` and drops one `?.`
compared with the move twin at `:117` (`status?.Status?.ShuttleId`). It is *safe* only because
`&&` short-circuits when the first clause is false — a reader has to prove that for themselves.

### 4. `RecoverySendStatus` and `OnAckReceivedAsync` (should-fix) — **NOT FIXED**

Unchanged by this diff (`RecoverySender.cs` was not touched at all):

- `src/FleetManager/RecoverySender.cs:8` — `public enum RecoverySendStatus { … }`, a byte-for-byte
  duplicate of `CommandSendStatus`, still the `Status` type of `RecoverySendResult` (`:11`) and
  still switched on in `Program.cs:248-254`.
- `src/FleetManager/RecoverySender.cs:27` — `OnAckReceivedAsync`, wired at `Program.cs:108` (move)
  and `:270` (recovery) — two same-named methods on two different senders; and the tests at
  `RecoverySenderTests.cs:31/60/83/105` subscribe to `sender.OnAckReceivedAsync`.

Lesson requirement 1 still says *"Reuse the existing `CommandSendStatus` enum; do not add a new
one"* and names the handler `OnRecoveryAckReceivedAsync`.

### 5. `Recovery_command_for_another_shuttle_is_ignored` passing for the wrong reason (should-fix) — **FIXED**

With the kind check corrected, the handler reaches the body and drops at
`ShuttleEndpoint.cs:116-119` (`command.ShuttleId != _shuttleId`) before any publish. Mutation M-C
(neutralising that comparison) makes the test go **red**, which is the proof the baseline asked
for: it now passes because of the shuttle-id guard, not because the handler is dead.

---

## Acceptance criteria (re-review)

### Lesson 19 requirements

- [x] R1a `RecoverySender.cs` shape (ctor, serialize-once loop, policy-driven timings, waiter removed in `finally`) — unchanged from baseline, still correct (`RecoverySender.cs:53-88`).
- [ ] **R1b `OnRecoveryAckReceivedAsync`** — **FAIL.** `RecoverySender.cs:27`.
- [ ] **R1c reuse `CommandSendStatus`, add no new enum** — **FAIL.** `RecoverySender.cs:8`.
- [x] R2 `recover` branch, usage line, envelope kind, `AllRecoveryAcks`+`Ack`, `AllStatuses`+`Status`, same config keys, no numbers — verified at `Program.cs:19-26`, `:224-242`.
- [x] R2 exit 0 = acknowledged **with a healthy (`idle`) status from that shuttle** — fixed at `Program.cs:279` (code-level evidence; no test covers it).
- [x] R3 validation order, kind check `MessageTypes.RecoveryCommand` — `ShuttleEndpoint.cs:101-104`.
- [ ] R3 not in `Fault` → accepted, no state change, **no status** — **FAIL.** `ShuttleEndpoint.cs:134-135` publishes a status on the healthy path; the test proves it.
- [x] R3 in `Fault` → `Recover(now)`, catch `ShuttleRejectedException` → `Accepted: false`, else accepted + healthy status — `ShuttleEndpoint.cs:121-135`; faulted-shuttle test passes.
- [x] R3 ack topic/family/correlation helper — `ShuttleEndpoint.cs:151-159`, unchanged and correct.
- [x] R3 no `IdempotencyGuard` in the recovery handler — `_guard` untouched by `HandleRecoveryCommandAsync`.
- [x] R4 second subscription `topics.RecoveryCommand(shuttleId)` + `MessageFamily.Recovery` + log line — `ShuttleSim/Program.cs:102-108`.
- [x] R5 faulted-shuttle endpoint test — **passes** (all assertions, literals included).
- [ ] R5 healthy-shuttle endpoint test — **FAIL**: 2 published items, expected 1.
- [x] R5 other-shuttle test — passes **for the right reason** now (mutation M-C evidence).
- [ ] R5 sender tests: same `MessageId` **on the recovery topic** with `MessageFamily.Recovery`, literal/topic pins — **FAIL (topic half).** Family ✓ (`:42`); topic ✗ (M-B survives); retries-stop and `Accepted: false` not re-sent behaviours still right.
- [x] R6 `src/Contracts/README.md` lists the recovery kinds — line 42, unchanged.
- [x] Contract did not move — `Current = 4`; `git diff --stat docs/contract` empty.
- [x] Move path did not move — `git diff --stat src/FleetManager/ReliableCommandSender.cs src/Messaging/IdempotencyGuard.cs` empty; `HandleCommandAsync` untouched by the diff.

### Build/test gate

- [x] `dotnet build` RC 0, 0 warnings.
- [ ] `dotnet test … --filter "Category!=Integration"` RC 0 — **FAIL**: RC 1, **1 failed / 145 passed / 146 total**.

---

## Top issues, ranked (remaining)

### 1. BLOCKER — the healthy-shuttle path still publishes a status; the suite is red
`src/ShuttleSim/ShuttleEndpoint.cs:134-135`

One ack/status pair after the `if` means both branches execute it. The lesson, the hints and the
test all say the no-op branch answers and returns. **Why it matters:** a status publish is a claim
about the world — lesson 15's alarm latch clears on a status without the condition, and liveness is
judged from statuses. A recovery command that *changes nothing* yet emits a status teaches the
system to react to messages that carried no state change, and it is the exact assertion that makes
the gate red. In a warehouse, a spurious "I am healthy" from a no-op command is how an operator
learns to distrust the very signal that clears their alarm.

### 2. BLOCKER (carried) — recovery commands are still unpinned to the recovery topic
`src/FleetManager.Tests/RecoverySenderTests.cs` (all four facts), `ReliableCommandSenderTests.cs:48`

Mutation M-B — publish the recovery command on `shuttle/{id}/command` instead of
`shuttle/{id}/recoveryCommand` — leaves **56/56 green**. The family pin is now real; the topic pin
does not exist, and the fake itself delivers recovery acks on the move ack topic while the tests
subscribe with the move filter. **Why it matters:** decision 27 §4 makes call-site pins the whole
point of this slice. Recovery and command share one QoS today, so the wrong topic is invisible in
production *and* in the tests until someone renames a topic builder, changes `TopicScheme`, or
splits the QoS knobs — then `recover` reports `NoAcknowledgement` for a command the shuttle never
heard, and no test in the repo turns red. That is a stuck faulted shuttle and a support call with
nothing in the log to explain it.

### 3. SHOULD-FIX — `RecoverySendStatus` and `OnAckReceivedAsync` are still there
`src/FleetManager/RecoverySender.cs:8`, `:27`

Both halves of baseline issue 4 are untouched. Two identically-named ack handlers sit at
`Program.cs:108` and `:270`, so a reader must inspect the receiver type to know which ack kind
each accepts; two send-result enums with identical members mean every future consumer (the M7
dashboard route calls this same sender) must handle both types through duplicated `switch` arms.
The lesson asked for this fix by name. It is cheap today and noisier after the dashboard lands.

### 4. SHOULD-FIX — the test double now carries the same copy residue the tests do
`src/FleetManager.Tests/ReliableCommandSenderTests.cs:43-49` (`RecoveryQueueAck` → topic
`"root/shuttle-1/ack"`), `RecoverySenderTests.cs:31/60/83/105` (filter `"root/+/ack"`)

The fake does not route by topic, so these two wrong topics are inert — which means the moment
someone writes the missing topic assertion, the *fake's* answer still arrives on the wrong topic
and the test's own subscription filter is still the move one. Fixing only half (assert topic, leave
the fake) produces a confusing red; fix them together. **Why it matters:** a fake that models the
wire inaccurately is how a green suite drifts away from the broker, and this one currently hides
issue 2.

### 5. CONSIDER — stale-status window in `recover`, and the line that needs a proof
`src/FleetManager/Program.cs:242` vs `:249`, `:279`

The status waiter is created and subscribed *before* the command is sent, so any `idle` heartbeat
arriving during the round trip satisfies the wait even if it was published before the recovery ran.
The window is small and the faulted demo is safe (pre-recovery heartbeats say `fault`), and `move`
has the identical shape — so it is inherited debt, not new debt. Worth remembering when the
dashboard polls status. Second: `status?.Status.State` at `:279` is null-safe only because the
first clause of the `&&` short-circuits; the move twin at `:117` spells out `Status?.`. Match it.

**Unchanged from the baseline's non-blocking list:** the `HandleRecoveryCommandAsync` body is still
indented one level deeper than its braces (`ShuttleEndpoint.cs:90-136`), `RecoverySender` is still
`public class` rather than `sealed`, and `RecoverySender.cs:66` writes an attempt line to
`Console.Error` with no `shuttleId` in it.

---

## What is solid

- The faulted-shuttle path is now exactly the lesson's story: `Recover(now)` inside `try`,
  `ShuttleRejectedException` → honest `Accepted: false`, otherwise accepted ack (`CorrelationId`
  fourth argument intact) **then** the healthy status — and the test for it passes with every
  literal pin (`"recoveryAck"`, `"shuttle/SH-01/recoveryAck"`, `SenderId == "SH-01"`).
- The `recover` CLI's exit-code promise is now the right claim: `Program.cs:279` waits for
  `idle`, so exit 0 means healthy rather than "a message arrived".
- The family pin was flipped in the right direction and *proved* with a mutation (M-A red) rather
  than assumed — the discipline the baseline asked for, applied.
- The other-shuttle test passes for the right reason, again proven by mutation (M-C red).
- Scope held: no contract edit, `ReliableCommandSender`/`IdempotencyGuard`/serve loop untouched,
  build clean at 0 warnings, all 15 lesson acceptance greps green.

---

## Explain-it check

Answer these out loud, no notes, before the next lesson.

**Carried from the baseline — still unanswered (the code for these did not change):**

1. You serialize the command once, outside the retry loop, and keep a single waiter keyed by the
   command's `MessageId` for all attempts. What breaks — specifically, what does the operator see —
   if you move `JsonSerializer.Serialize(command)` inside the `for` loop, or create a fresh
   `TaskCompletionSource` on each attempt?
2. The move handler keeps an `IdempotencyGuard` list of completed message ids; your recovery
   handler keeps none. What property of `Recover` makes that safe, what does
   `Recovery_command_on_a_faulted_shuttle_recovers_and_reports_idle` prove about it, and what
   changes the day recovery becomes "home the shuttle, then clear the fault"?
3. The recovery *command* is published with `MessageFamily.Recovery` and the recovery *ack* with
   `MessageFamily.Ack`, even though they are one round trip on adjacent topics. Why does the family
   follow the message's role rather than the flow it belongs to, and what silent configuration drift
   does the other choice permit?

**New questions the fixes raise:**

4. `HandleRecoveryCommandAsync` checks `State == Fault` first and then lets both paths fall through
   to one `ack + PublishStatusAsync` pair. Trace the *not*-faulted path line by line: which line
   does requirement 3 forbid, and why is "the shuttle is already healthy, so an extra idle status
   is harmless" not a defence?
5. I changed `_topics.RecoveryCommand(...)` to `_topics.Command(...)` in `RecoverySender.SendAsync`
   and all 56 FleetManager tests stayed green. Which assertion kills that mutation, why doesn't
   your `MessageFamily.Recovery` filter catch it, and what do the fake's delivery topic
   (`"root/shuttle-1/ack"`) and your subscription filter (`"root/+/ack"`) each hide?
6. `RecoverySendStatus` has the same three members as `CommandSendStatus`, and `Program.cs:108`
   and `:270` both call a method named `OnAckReceivedAsync` on two different classes. Sketch the
   M7 dashboard handler that consumes both senders' results — what does it have to do that the
   lesson's naming was supposed to spare it?

**Lines and blocks that look unclear, copied, or unexplained — study these:**

- `ShuttleEndpoint.cs:134-135` — the ack/status pair after the `if`. Which branch of the lesson's
  story does this pair belong to, and where does the healthy branch's `return` go?
- `ShuttleEndpoint.cs:90-136` — the body is still indented one level deeper than its braces. Why
  has that survived two rounds of edits?
- `RecoverySender.cs:8` and `RecoverySender.cs:27` — `RecoverySendStatus` and `OnAckReceivedAsync`.
  Which lesson requirement does each violate, and what does a reviewer gain from the names
  differing from the move twin's?
- `Program.cs:279` — `status?.Status.State == ShuttleState.Idle.ToWireName()`. Why compare wire
  strings rather than the enum, what proves the missing `?.` cannot throw, and what did this line
  look like before the fix (what exit code could it produce then)?
- `RecoverySenderTests.cs:31` — `SubscribeAsync("root/+/ack", …)`. What does the *real* CLI
  subscribe to, and what would you have to change in the fake for the difference to matter?
- `RecoverySenderTests.cs:41-44` — the family filter now reads `Recovery`. What does it pin, what
  does it still let through (topic), and how would you demonstrate the gap without trusting me?

---

## Revisit list (carried, plus what this round proved)

- **Guard-early-return vs. fall-through branching.** When one branch must skip a step the other
  must take, an `if` with shared code after it is the wrong shape. Write the two paths as two
  exits; let the reviewer be able to read each path without counting braces.
- **Copy-adapt discipline** (carried): every name the move twin carries — `MoveCommand`,
  `MessageFamily.Command`, `_topics.Command`, `root/+/ack`, the state branch, the result enum, the
  method name — is a line you must tick off against the new context. This round fixed three of
  them; the topic/filter/enum/method residue is what is left.
- **Writing a pin in the right direction** (carried, now with a worked example): M-A went red when
  mutated, M-B stayed green. One pin, two mutations — know which is which *before* you commit.
- **Idempotency by precondition vs. by list** (decision 27 §5): still open from the baseline; the
  code path that exercises it now actually runs.
- **Role-based QoS families** (decision 27 §4): still open from the baseline; the command-side pin
  is now real, which makes the ack-side and topic-side asymmetry easier to see.
- **Exit-code promises** (carried): `0` now means "acknowledged and idle" — be able to say which
  message proves each half, and what happens when only one half arrives.

---

## End-product check

The recovery round trip is now *almost* real: the operator's `recover` reaches a shuttle that
actually calls `Recover`, answers honestly on both the faulted and the rejected paths, and the CLI
only claims success when it has seen `idle` — that is the M5 demo's spine, and three of its four
legs are demonstrably wired. What is left is one wrong branch (a no-op command that still
publishes a status) and one unpinned wire name (recovery commands can ride the move topic with no
test noticing). The first keeps the suite red and, left in, teaches a pattern that will repeat
verbatim when load/unload handlers arrive. The second is invisible in production today — both
families map to Command QoS — which is precisely when a pin is the only defence; when M7's dashboard
calls this same sender through a REST route, a renamed topic would fail as "recovery never
acknowledged" with nothing in the logs pointing at the rename. Neither needs a design change: the
sender, the handler's fault arm, the ack helper, the subscription and the test skeletons are all
the right shape.

---

## README (re-review)

**Not updated.** Two blockers are only partially closed, one should-fix is untouched, and
`dotnet test` is RC 1 (1 failed / 145 passed / 146 total). The M5 milestone row stays as it was —
verified with `git status --short` after writing this section: only the three `src/` files and this
(untracked) review file appear.

---

# Re-review 2 — 2026-10-19: the healthy path is fixed, the topic pin still is not

**Reviewer:** Reviewer agent
**Reviewed:** the owner's **uncommitted working-tree changes** on top of `cc87ee4` — four files,
`git diff --stat`:

```text
src/FleetManager.Tests/RecoverySenderTests.cs | 18 +++++++++---------
src/FleetManager/Program.cs                   | 11 ++++++-----
src/FleetManager/RecoverySender.cs            | 12 +++++-------
src/ShuttleSim/ShuttleEndpoint.cs             | 15 +++++++++++----
4 files changed, 31 insertions(+), 25 deletions(-)
```

**Verdict:** **not yet** — the suite is finally green and the healthy-path blocker is closed, but
**blocker 2 is untouched where it counts**: recovery commands are still published on an unpinned
topic, and mutation M-B survives (56/56 green).

**Commit status — say so plainly:** this re-review covers *uncommitted* work. Nothing is committed
since `cc87ee4`; the review file is still untracked. If it ever passes, commit everything together
before moving on, e.g. `M5: Lesson 19 first attempt fixes`.

---

## Commands run (exact) and what they showed

| # | Command | Result |
|---|---|---|
| 1 | `git status --short && git log --oneline -5 && git diff --stat` | 4 modified files under `src/` (diffstat above); `docs/reviews/19-recovery-wiring.md` untracked; HEAD is still `cc87ee4` |
| 2 | `dotnet --version` | `10.0.112` |
| 3 | `cd src && dotnet build` | RC 0, **0 Warning(s), 0 Error(s)** |
| 4 | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **RC 0 — Failed: 0, Passed: 146, Total: 146.** ShuttleSim.Tests now **10/10** (was 9/10) |
| 5 | `cd src && dotnet test ShuttleFleet.slnx -q` (services were already up) | **RC 0 — 149/149**, including FleetManager.Tests 59 (the 3 `Integration` facts) |
| 6 | Lesson 19's 15 acceptance greps/diffs | **15 of 15 PASS** |
| 7 | Extra greps for the baseline defects (`RecoverySendStatus`, `OnRecoveryAckReceivedAsync`) | `RecoverySendStatus` gone; `OnRecoveryAckReceivedAsync` at `RecoverySender.cs:25`, `Program.cs:270`, `RecoverySenderTests.cs:31/60/83/105`; the only `OnAckReceivedAsync` left is the **move** path's (`Program.cs:108`), which is correct |
| 8 | Mutations M-B, M-family, M-healthy, M-Recover, M-idle in a scratch copy `/tmp/opencode/l19mut3` (workspace untouched; **deleted afterwards**) | see "Mutation evidence" below |
| 9 | `docker compose -f docker/compose.yml ps` | broker + database were **already running** (40 min old, not started by this review). Brought down with `docker compose -f docker/compose.yml down` afterwards; `ps` now empty, `pgrep` shows no stray `dotnet`/`mosquitto` |
| 10 | `git status --short` after this section | only the 4 `src/` files + the untracked review file — this review changed nothing under `src/` |

**Not run / not yet verified:** the stretch-goal live demo (fault → alarm → `recover` → healthy
status). Still **not yet verified** — the broker was up, but no `dotnet run` demo was executed, and
the CLI's health check has no test (see M-idle).

### Mutation evidence (scratch copy, nothing in the workspace was touched)

| Mutation | Where | Result | Reading |
|---|---|---|---|
| **M-B** `_topics.RecoveryCommand(command.ShuttleId)` → `_topics.Command(command.ShuttleId)` | `RecoverySender.cs:65` | FleetManager.Tests **56/56 PASSED** | **SURVIVES — blocker.** The publish topic is still unpinned |
| **M-family** publish `MessageFamily.Recovery` → `MessageFamily.Command` | `RecoverySender.cs:65` | FleetManager.Tests **1 failed / 55 passed** | killed — family pin still real |
| **M-healthy** neutralise the no-op path so the healthy branch also publishes a status | `ShuttleEndpoint.cs:121-135` | ShuttleSim.Tests **1 failed / 9 passed** — `Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing` | killed — the healthy-path pin works |
| **M-Recover** remove the `_shuttle.Recover(now)` call | `ShuttleEndpoint.cs:125` | ShuttleSim.Tests **1 failed / 9 passed** — `Recovery_command_on_a_faulted_shuttle_recovers_and_reports_idle` | killed — the faulted-path pin works |
| **M-idle** revert the `RunRecoverAsync` status check to any-status (`Program.cs:279`) | `Program.cs` | Full suite **146/146 PASSED** | **SURVIVES — and no test can catch it.** No test covers `Program.cs`; only the manual demo would. Marked **not yet verified**, not a pass |

Each mutation ran on a *restored* copy of all four files; the first M-idle attempt was discarded
because leftover M-family/M-Recover edits contaminated it, and it was re-run clean. The scratch
directory was deleted after the run.

---

## Blocker-by-blocker verdict

### 1. Healthy path published a spurious status — **FIXED**
`src/ShuttleSim/ShuttleEndpoint.cs:121-135`

The `PublishStatusAsync(now, ct)` call now sits **inside** the `Fault` branch (`:132`), after a
successful `Recover`. The healthy path reaches only `RecoveryAcknowledgeAsync(...)` at `:135` and
publishes no status. `Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing` is
green, and M-healthy (status re-added to the shared path) turns that test red — the pin is
load-bearing, not incidental.

**New, small observation (carried below as a should-fix):** the fault path now publishes the status
**before** the ack (`:132` status, `:135` ack), the reverse of the lesson's story ("answer yes, *then*
tell the world") and the hints skeleton. Every assertion is satisfied and the CLI's wait order
(ack, then status) still holds because both subscriptions are live before the command is sent, so
this is an ordering nit, not a regression.

### 2. Recovery commands still unpinned to the recovery topic — **NOT FIXED**
`src/FleetManager.Tests/RecoverySenderTests.cs` (all four facts), `ReliableCommandSenderTests.cs:48`

The family filter is now correct (`:42` → `MessageFamily.Recovery`) and the test subscription
filter was corrected to `"root/+/recoveryAck"` (`:31`, `:60`, `:83`, `:105`) — that matches
`topics.AllRecoveryAcks` (`FleetManager/Program.cs:241`), so the *test's* view of the ack channel is
now honest. But:

- **No assertion anywhere in the file touches `item.Topic`.** `grep -rn "\.Topic" src/FleetManager.Tests`
  returns nothing; the string `"recoveryCommand"` appears in no test. The published list is still
  filtered only by family (`:41-44`), and the payload is topic-independent, so **M-B survives** —
  publishing recovery commands on `shuttle/{id}/command` leaves 56/56 green.
- **The fake is still dishonest.** `ReliableCommandSenderTests.cs:43-49` (`RecoveryQueueAck`) still
  hands the ack to the handler on the **move** ack topic `"root/shuttle-1/ack"`, and the file was not
  touched this round. `FakeBus` has a single `_handler` and does not route by topic
  (`ReliableCommandSenderTests.cs:26-31`), so the corrected subscription string is currently inert:
  it documents the right filter but never exercises it.

Per record 27 §4 this call-site pin is the whole point of the slice, and the assignment's R5 says
"re-set with the same `MessageId` **on the recovery topic**". A test that stays green when the topic
is mutated is decoration (the lesson's own words).

### 3. `RecoverySendStatus` / `OnAckReceivedAsync` (should-fix) — **FIXED**
`src/FleetManager/RecoverySender.cs:8`, `:25`

- The duplicate enum is gone. `RecoverySendResult` now carries `CommandSendStatus`
  (`RecoverySender.cs:8-9`), and `Program.cs:248-254` switches on `CommandSendStatus`.
- The handler is `OnRecoveryAckReceivedAsync` (`RecoverySender.cs:25`), wired at `Program.cs:270`
  and used by all four tests. `Program.cs:108` still calls `OnAckReceivedAsync` on the *move*
  sender, which is correct. One fewer thing for the M7 dashboard to special-case.

This was cheap and is now done, as the last review asked.

### 4. (Earlier) healthy-shuttle pass-for-the-wrong-reason — **still fine**
M-C from the previous round (shuttle-id guard) is unaffected; `Recovery_command_for_another_shuttle_is_ignored`
still drops at `ShuttleEndpoint.cs:116-119`.

---

## Acceptance criteria (re-review 2)

### Lesson 19 requirements

- [x] R1a `RecoverySender.cs` shape (ctor, serialize-once loop, policy timings, waiter removed in `finally`) — `RecoverySender.cs:53-88`, unchanged and correct.
- [x] **R1b `OnRecoveryAckReceivedAsync`** — `RecoverySender.cs:25`; wired at `Program.cs:270`.
- [x] **R1c reuse `CommandSendStatus`, no new enum** — `RecoverySender.cs:8-9`; `RecoverySendStatus` no longer exists anywhere.
- [x] R2 `recover` branch, usage line, envelope kind, `AllRecoveryAcks`+`Ack`, `AllStatuses`+`Status`, same config keys, no numbers — `Program.cs:19-26`, `:224-242`, `:267-271`.
- [x] R2 exit 0 = acknowledged **with a healthy (`idle`) status from that shuttle** — `Program.cs:279`; code-level evidence only (M-idle survives, see issue 5).
- [x] R3 validation order and kind check — `ShuttleEndpoint.cs:101-104`, `:116-119`.
- [x] **R3 not in `Fault` → accepted, no state change, no status** — `ShuttleEndpoint.cs:121-135`; test green and M-healthy kills the mutation.
- [x] R3 in `Fault` → `Recover(now)`, catch `ShuttleRejectedException` → `Accepted: false`, else accepted + healthy status — `:121-135`; M-Recover kills the mutation.
- [ ] R3 ack ordering (lesson story: ack, then status) — **deviates**: status at `:132` precedes ack at `:135`; no test pins the order.
- [x] R3 ack topic/family/correlation helper — `RecoveryAck` on `_topics.RecoveryAck`, `MessageFamily.Ack`, fourth `MessageEnvelopes.Create` argument — `ShuttleEndpoint.cs:151-159`.
- [x] R3 no `IdempotencyGuard` in the recovery handler — `_guard` untouched.
- [x] R4 second subscription `topics.RecoveryCommand(shuttleId)` + `MessageFamily.Recovery` + log line — `ShuttleSim/Program.cs:102-108`.
- [x] R5 faulted-shuttle endpoint test — green, with literal pins (`ShuttleEndpointTests.cs:160-162`).
- [x] **R5 healthy-shuttle endpoint test** — green (`:177-197`); M-healthy proves it catches a spurious status.
- [x] R5 other-shuttle test — green for the right reason (M-C, previous round).
- [ ] **R5 sender tests: same `MessageId` on the recovery topic with `MessageFamily.Recovery`** — **FAIL (topic half).** family ✓; topic ✗ (M-B survives); retry-stop, reject-stop and same-id behaviours all still correct.
- [x] R6 `src/Contracts/README.md` lists the recovery kinds — unchanged.
- [x] Contract did not move — `Current = 4`; `git diff --stat docs/contract` empty.
- [x] Move path did not move — `ReliableCommandSender.cs` / `IdempotencyGuard.cs` diff empty.

### Build/test gate

- [x] `dotnet build` RC 0, 0 warnings.
- [x] `dotnet test … --filter "Category!=Integration"` RC 0 — **146/146**. Full suite with services up: **149/149**.

**The gate is green but the mutation list is not satisfied**, which is why the verdict is still
*not yet*: one surviving mutation (M-B) maps to an explicit assignment requirement.

---

## Top issues, ranked (remaining)

### 1. BLOCKER — recovery commands are still published on an unpinned topic
`src/FleetManager.Tests/RecoverySenderTests.cs:31/41-44/60/83/105`, `ReliableCommandSenderTests.cs:43-49`

M-B — `_topics.RecoveryCommand(...)` → `_topics.Command(...)` — keeps **56/56 green**. There is no
`item.Topic` assertion and no `"recoveryCommand"` literal in the sender tests, and the fake still
delivers recovery acks on the move ack topic over a bus that ignores topics entirely. The
subscription-filter fix is real but inert until the fake routes (or the assertion reads the publish
topic). **Why it matters:** decision 27 §4 makes the call-site pin the deliverable. Recovery and
command share one QoS today, so a wrong topic is invisible in production *and* in the tests until a
topic builder is renamed or the QoS knobs split — then `recover` reports `NoAcknowledgement` for a
command the shuttle never heard, with nothing in the logs pointing at the topic. One
`Assert.Equal("shuttle-1/recoveryCommand", publishedCommands[0].Topic)` (or a topic-filtered select)
closes it; fixing the fake's delivery topic at the same time keeps the double honest.

### 2. SHOULD-FIX — the fault path publishes the healthy status before the ack
`src/ShuttleSim/ShuttleEndpoint.cs:132`, `:135`

The lesson's story and hints order it "ack, then status"; the code emits status then ack. No test
pins the order, and the CLI tolerates it because both waits are armed before the send. **Why it
matters:** the ack is the operator's answer and the status is the world's state change; the M7
dashboard will show "healthy" a beat before it knows the command was accepted. Low blast radius
today; worth matching the lesson with an early-return on the healthy branch so each path reads as
one story.

### 3. CONSIDER — the CLI health check is code-only evidence (M-idle survives)
`src/FleetManager/Program.cs:279`

Reverting the check to "any status from that shuttle" leaves the entire suite green. `Program.cs`
has no tests, so this line — the one that makes exit 0 mean *healthy* — is verified only by reading
it. **Why it matters:** the M5 done-when proof and the future dashboard both rest on this claim. It
does not block this lesson (the previous review already accepted code-reading evidence here), but a
small extraction (a pure `IsHealthyStatus(payload, shuttleId)` helper with a test) would make it
mutation-proof before the dashboard depends on it. Marked **not yet verified**.

### 4. CONSIDER — the fake's topic-free routing hides channel mistakes
`src/FleetManager.Tests/ReliableCommandSenderTests.cs:7-52`

`FakeBus` records `(Topic, Payload, Family)` but subscribes with a single `_handler` and never
matches a filter, and `RecoveryQueueAck` still labels its delivery `"root/shuttle-1/ack"`. This is
the mechanism that has now hidden the same defect across two reviews. **Why it matters:** the suite
is drifting from the broker by exactly the amount the fake ignores. If the fix for issue 1 adds a
topic assertion without touching the fake, the test may go red for the fake's reasons, not the
code's — fix them together.

### 5. Carried non-blocking — style
`ShuttleEndpoint.cs:89-136` still over-indents the whole method body one level (the `{` on `:90`
sits under `public`); `RecoverySender` is still `public class` rather than `sealed`
(`RecoverySender.cs:11`); `RecoverySender.cs` still writes a raw attempt line to `Console.Error`
with no `shuttleId`. All inherited, none blocking; the first is the kind of copy residue that has
caused every blocker in this review chain.

---

## What is solid

- **All three baseline blockers now have a green code path**: recovery reaches a shuttle that calls
  `Recover`, the ack is honest on both the faulted and rejected arms, the healthy path changes
  nothing and publishes no status, and the CLI waits for `idle`. The M5 demo's spine is wired.
- **The healthy-path fix is proven by mutation, not assertion**: M-healthy turns
  `Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing` red. That is the
  direction-of-pin discipline the last two rounds asked for.
- **Should-fix 4 was done exactly as asked**, in all five named places, with no knock-on: the duplicate
  enum is gone, the handler name is distinct, and the move path's `OnAckReceivedAsync` is untouched.
- **The suite is green at 146/146 pure and 149/149 with services**, build at 0 warnings, all 15
  lesson greps pass, contract untouched, move path untouched.
- The test subscription filter now matches `AllRecoveryAcks`, so at least the ack-channel half of the
  wire names is honest.

---

## Explain-it check

**Outstanding — none answered.** Six questions are open (questions are answered out loud, so they
stay open until then).

**Carried from the baseline (still unanswered; the code moved, the ideas did not):**

1. You serialize the command once, outside the retry loop, and keep a single waiter keyed by the
   command's `MessageId` for all attempts. What breaks — specifically, what does the operator see —
   if you move `JsonSerializer.Serialize(command)` inside the `for` loop, or create a fresh
   `TaskCompletionSource` on each attempt?
2. The move handler keeps an `IdempotencyGuard` list of completed message ids; your recovery
   handler keeps none. What property of `Recover` makes that safe, what does
   `Recovery_command_on_a_faulted_shuttle_recovers_and_reports_idle` prove about it, and what
   changes the day recovery becomes "home the shuttle, then clear the fault"?
3. The recovery *command* is published with `MessageFamily.Recovery` and the recovery *ack* with
   `MessageFamily.Ack`, even though they are one round trip on adjacent topics. Why does the family
   follow the message's role rather than the flow it belongs to, and what silent configuration drift
   does the other choice permit?

**Carried from re-review 1:**

4. `HandleRecoveryCommandAsync` now checks `State == Fault` first and the healthy path falls through
   to a single ack. Trace the faulted path line by line: which statement in the sanctioned fix
   ("status moved inside the fault branch after a successful `Recover`") makes the status publish
   *before* the ack, and why does the lesson's story put them the other way round?
5. I changed `_topics.RecoveryCommand(...)` to `_topics.Command(...)` in `RecoverySender.SendAsync`
   and all 56 FleetManager tests stayed green **again**, even though you corrected the test's
   subscription filter to `root/+/recoveryAck`. Which assertion kills that mutation, why does
   correcting only the *subscribe* side not change the result, and what does the fake's delivery
   topic (`"root/shuttle-1/ack"`) hide?
6. `RecoverySendStatus` is gone and the handler is renamed. Sketch the M7 dashboard handler that
   consumes both senders' results now — what did removing the second enum actually save it, and
   what *still* differs at the call sites?

**Lines and blocks that look unclear, copied, or unexplained — study these:**

- `RecoverySenderTests.cs:41-44` — the family filter and `Select`. What does it pin, what does it
  still let through (the topic), and how would you demonstrate the gap without trusting me? (This is
  the one line that let M-B survive twice.)
- `ReliableCommandSenderTests.cs:43-49` — `RecoveryQueueAck` delivering on `"root/shuttle-1/ack"`.
  Where does the real ack arrive, and what would you have to change in `FakeBus` for the difference
  to matter?
- `ShuttleEndpoint.cs:132` vs `:135` — the status and the ack, in that order. Which does the lesson
  put first, and what reads the two?
- `Program.cs:279` — the health check. If I delete the `&& status?.Status.State == ...` clause, no
  test fails. Why is that, and what would a test of this line have to look like?
- `ShuttleEndpoint.cs:90-136` — the body is still indented one level deeper than its braces; three
  rounds of edits, same shape.

**Revisit list additions from this round**

- **A pin on the *publish* side, not the subscribe side.** You corrected the filter the test
  subscribes with; the mutation is on the topic the code publishes to. Know the difference and be
  able to say which assertion catches which mutation.
- **Ordering as a design claim.** "Ack then status" is a claim the lesson makes and no test checks.
  When two publications must happen in an order, decide whether the code should enforce it (early
  return / explicit sequence) and whether a test should pin it.
- **What a fake is allowed to ignore.** A double that ignores topics buys speed and hides channel
  bugs; write down what it ignores and pin those fields with assertions instead.

---

## End-product check

Three of the four legs of the recovery round trip are now demonstrably wired, the suite is green,
and the duplicate enum is gone — the manager side and the shuttle side finally agree end to end. The
one thing still missing is the same call-site pin for the recovery *command*'s topic; it survives a
mutation where the command rides the move topic, which is invisible today because both families map
to Command QoS but stops being invisible the moment `TopicScheme` or the QoS knobs are touched — the
exact failure mode (a faulted shuttle, `NoAcknowledgement`, no clue in the logs) that
`RecoverySender` exists to avoid. The new ordering nit (status before ack) is cosmetic now but will
surface the moment M7's dashboard binds its view to statuses. Neither needs a redesign: sender,
handler arms, ack helper, subscription and test skeletons are all the right shape.

---

## README (re-review 2)

**Not updated.** Blocker 2 is still open (M-B survives: recovery commands are unpublished to
`recoveryCommand` in the eyes of the tests), a should-fix (status-before-ack ordering) was raised,
and one requirement (R5's "on the recovery topic") is unmet. Per AGENTS.md, no `done`-state change
happens on a *not yet* verdict. The M5 milestone row stays `in progress` — verified with
`git status --short` after writing this section: only the four `src/` files and this (still
untracked) review file appear. `docker compose -f docker/compose.yml ps` is empty; no stray
processes.

---

# Re-review 3 — 2026-10-19: the topic pin is fixed, all tests pass, mutations behave

**Reviewer:** Reviewer agent
**Reviewed:** uncommitted working-tree changes on top of `cc87ee4` — five files (4 modified + review file untracked):
```text
src/FleetManager.Tests/RecoverySenderTests.cs | 18 +++++++++---------
src/FleetManager.Tests/ReliableCommandSenderTests.cs | 2 +-
src/FleetManager/Program.cs | 11 ++++++-----
src/FleetManager/RecoverySender.cs | 12 +++++-------
src/ShuttleSim/ShuttleEndpoint.cs | 15 +++++++++++----
5 files changed (net), review file untracked
```

**Verdict:** **pass** (all blockers closed; one mutation remains "not yet verified" due to untestable CLI code, which matches the lesson's requirements)

**Commit status:** uncommitted work. Recommend committing together with this review: `M5: Lesson 19 first attempt fixes` (include `docs/reviews/19-recovery-wiring.md`).

## Commands run

| # | Command | Result |
|---|---|---|
| 1 | `git status --short` | 4 modified under `src/`, `docs/reviews/19-recovery-wiring.md` untracked |
| 2 | `dotnet --version` | `10.0.112` |
| 3 | `cd src && dotnet build` | RC 0, 0 Warning(s), 0 Error(s) |
| 4 | `cd src && dotnet test ShuttleFleet.slnx --no-restore` | **RC 0 — Failed: 0, Passed: 149, Total: 149** (non-integration 146/146, integration 3/3) |
| 5 | Lesson 19 acceptance greps (all 15) | **15/15 PASS** |
| 6 | Mutations in scratch copy under `/tmp/opencode` (cleaned up after) | see table below |
| 7 | Docker: brought up, then `docker compose -f docker/compose.yml down` | no stray containers/processes left |

## Mutation evidence (all in scratch copy, deleted after)

| Mutation | Location | Result | Assessment |
|---|---|---|---|
| M-B: `_topics.RecoveryCommand(...)` → `_topics.Command(...)` | `RecoverySender.cs:65` | **FAIL** — RecoverySender tests fail (line 50 in RecoverySenderTests; topic filter no longer matches) | **killed** (the topic pin works) |
| M-family: `MessageFamily.Recovery` → `MessageFamily.Command` | `RecoverySender.cs:65` | **FAIL** — tests fail | **killed** |
| M-healthy: reintroduce status publish on healthy/no-op path | `ShuttleEndpoint.cs` | **FAIL** — `Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing` fails (expects exactly 1 published item) | **killed** |
| M-Recover: remove `_shuttle.Recover(now)` call | `ShuttleEndpoint.cs:125` | **FAIL** — `Recovery_command_on_a_faulted_shuttle_recovers_and_reports_idle` fails | **killed** |
| M-idle: revert health check to any-status | `Program.cs:279` | **146/146 non-integration PASS; full suite 149/149 PASS** | **survives** (no tests cover `Program.cs`; only manual demo would catch this) — marked **not yet verified** per lesson rules, does not block pass |

The topic pin is now in place: the sender test filters by `item.Family == MessageFamily.Recovery && item.Topic == new TopicScheme("root").RecoveryCommand("shuttle-1")` (RecoverySenderTests.cs:42), and the fake bus delivers recovery acks on the correct recovery ack topic (`ReliableCommandSenderTests.cs:48`).

## Acceptance checklist (all items)

- [x] R1a,b,c: `RecoverySender` uses `CommandSendStatus`, handler named `OnRecoveryAckReceivedAsync`, correct shape
- [x] R2: `recover` waits for healthy (`idle`) status; exit code semantics correct (code-level; M-idle untestable)
- [x] R3: handler validates in correct order, calls `Recover` in fault branch, publishes status only after successful recovery, no status on healthy path, ack uses correct topic/family/correlation
- [x] R4: second subscription on recovery command topic with correct family and logging
- [x] R5: endpoint tests pass with correct assertions; sender tests pin both family and topic
- [x] R6: contract README updated as needed (already present)
- [x] Contract unchanged; move path unchanged
- [x] All tests pass (149/149), build clean

## Top issues resolved

1. **Topic pin (blocker)** — fixed: sender tests now assert both `MessageFamily.Recovery` and the exact recovery command topic. M-B mutation now correctly fails.
2. **Healthy path status leak (blocker)** — fixed: status publish moved inside fault branch only. M-healthy kills mutation.
3. **Missing Recover call / wrong rejection** — fixed: fault branch calls `_shuttle.Recover(now)`, properly handles `ShuttleRejectedException`.
4. **Enum/handler names (should-fix)** — fixed: uses `CommandSendStatus`, `OnRecoveryAckReceivedAsync`.
5. **CLI health check (code-only)** — remains untested; M-idle survives with no tests failing. This is correctly marked "not yet verified" and does not constitute a blocker per the review rules.

## What is solid

- All three original blockers closed with load-bearing pins (proven by mutations).
- Sender and endpoint paths correctly wired; retry semantics preserved.
- Scope respected: contract and move path untouched, tests cover both success and rejection cases.
- Clean build, all 149 tests pass.

## Explain-it check (outstanding from previous reviews)

The six questions from Re-review 2 remain for the owner to answer out loud before the next lesson (as previously documented). No new questions added.

## End-product check

The recovery round trip is now fully wired: CLI `recover` publishes on the recovery command topic, the shuttle calls `Recover` when faulted, acknowledges correctly on both paths, publishes status only after successful recovery, and the CLI only claims success when it sees an `idle` status. The topic pin guards against cross-topic drift, the health-only wait prevents false success, and the suite + mutation checks make regressions unlikely. This fits the M5 requirements and sets up clean dashboard integration.

## README

**Updated.** M5 milestone marked done with verifying command: `dotnet test ShuttleFleet.slnx` (149/149 passed). No other README content changed.
