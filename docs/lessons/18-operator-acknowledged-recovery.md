# Lesson 18: Operator-acknowledged recovery (clear a faulted shuttle)

**Milestone:** M5 (Faults and recovery) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 17: Reassigning a failed shuttle's work](17-reassign-failed-shuttle-work.md) · **Decision:** [record 26](../decisions/26-operator-acknowledged-recovery.md); builds on [record 23](../decisions/23-blocked-path-fault-and-second-alarm.md), [record 22](../decisions/22-serve-loop-and-alarm-publish.md), [record 21](../decisions/21-alarms-with-severity.md), [record 25](../decisions/25-reassign-failed-shuttle-work.md), [record 17](../decisions/17-occupancy-and-fault-reservations.md), [record 07](../decisions/07-message-contract-v2-envelope-and-topics.md)

**Concepts this assignment requires:**

- [ ] **A faulted shuttle must be cleared explicitly.** The shuttle's state machine gains a recovery transition to exit `Fault` and return to `Idle`, clearing the fault reason. Taught in [1) Clearing a fault on the shuttle](#1-clearing-a-fault-on-the-shuttle).
- [ ] **Recovery travels as a command with acknowledgement (v4).** The wire contract gains `RecoveryCommand` and `RecoveryAck` message kinds, with correlation semantics matching move commands. Taught in [2) The recovery command and ack (v4)](#2-the-recovery-command-and-ack-v4).
- [ ] **Recovery is operator-acknowledged.** A recovery command is sent in response to operator acknowledgement; the shuttle responds with an ack and reports healthy status. Taught in [3) The recovery flow (operator-acknowledged)](#3-the-recovery-flow-operator-acknowledged).

Three new concepts. That is the cap.

**Not in this lesson:** `palletNotDetected` fault injection/raising (defined but not raised — later slice); lostCommunication recovery (different semantics); mid-move fault recovery; dashboard/operator UI that triggers recovery (the message shapes and mechanism only); reassignment of work already done in lesson 17; automatic recovery when obstacle clears (operator-acknowledged means explicit command).

**The end-product fact:** After lesson 16, a blocked-path fault leaves the shuttle in `Fault`. After lesson 17, unstarted work is reassigned. But there is no way to clear the fault. This lesson adds an explicit recovery command (v4) and a `Recover` transition on the shuttle so the system can return to `Idle` after the fault is addressed.

---

## Where this fits

M5 requires "Recovery that an operator acknowledges" and "every injected fault leads to the right alarm and the right recovery". For `blockedPath`, the right alarm exists (lesson 16). The right recovery means the operator acknowledges the alarm and the shuttle clears its fault. The alarm latch clears when the shuttle reports a healthy status (no fault). This slice adds the wire path and the shuttle-side transition to make that possible.

The existing command/ack pattern (move commands) gives us the template: manager→shuttle command, shuttle→manager ack, correlation via `CorrelationId` linking ack to command. Recovery follows the same pattern with new message kinds (v4).

---

## Verification table

| Claim | How I checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| Baseline pure suite green | `/tmp/opencode/l18baseclean.txt` (clean copy) | RC 0, 8 `Passed!`, 0 `Failed!`, **134** tests |
| No Recover method on Shuttle | `grep -rn "Recover" src/ShuttleCore/Shuttle.cs` | No matches |
| No recovery message kinds in MessageTypes | `grep -rn "RecoveryCommand\|recoveryCommand\|RecoveryAck\|recoveryAck" src/Contracts/MessageTypes.cs` | No matches |
| Contract version is 3 | `grep "Current" src/Contracts/MessageTypes.cs` | `public const int Current = 3;` |
| No recovery in TopicScheme/MessageFamily | `grep "Recovery" src/Messaging/TopicScheme.cs src/Messaging/MessageFamily.cs src/MqttMessaging/MqttMessageBus.cs` | No matches (repo as-is) |
| Faulted shuttle stays faulted | Scratch probe in baseline copy | From Fault, all operations throw; no way to exit Fault |
| Alarm latch clears on healthy status | Lesson 15 tests exist | `A_recovered_shuttle_clears_the_latch_and_can_raise_again` (FleetServeLoopTests) already asserts latch clears when healthy status arrives |

Not yet verified: the code changes and new tests for this lesson.

---

## Options survey (design choices)

### Survey 1: How does the shuttle exit Fault?
| | A: Add `Recover(DateTime now)` method (chosen) | B: Allow any command to clear fault | C: Auto-clear when blocking condition gone |
|---|---|---|---|
| Explicit, operator-controlled | **yes** | no | no (automatic) |
| Matches "operator acknowledges" | **yes** | no | no |
| Safe (prevents accidental clear) | **yes** — only from Fault | risky | loses audit trail |
| Symmetric with RaiseFault | **yes** | no | no |

**Decision:** `Shuttle.Recover(now)` transitions from `Fault` → `Idle`, sets `FaultReason = null`, updates `_lastEventAt`. Throws `ShuttleRejectedException` if not in Fault.

### Survey 2: Recovery message kinds (v4)
| | A: Add `RecoveryCommandMessage`, `RecoveryAckMessage` (chosen) | B: Overload MoveCommand | C: Embed in status |
|---|---|---|---|
| Follows command/ack pattern | **yes** | awkward | no |
| Correlation works | **yes** | possible but confusing | no |
| Clean separation | **yes** | mixes concerns | status is level, command is event |
| Contract clarity | **yes** — explicit kinds | ambiguous | not a command |

**Decision:** v4 adds `recoveryCommand` and `recoveryAck` kinds with full envelope + correlation semantics.

### Survey 3: Version bump timing
| | A: Bump to v4 now (contract change) (chosen) | B: Defer to later | C: Avoid bump by overloading |
|---|---|---|---|
| Contract is source of truth | **yes** — new kinds documented | leaves contract stale | breaks contract semantics |
| Backward compatible (old code ignores unknown kinds) | **yes** | n/a | creates ambiguity |
| Matches decision 26 | **yes** | contradicts | no |

**Decision:** `ContractVersion.Current` becomes 4. Add `docs/contract/v4.md` (keep v3 as-is).

---

## The lecture

Today we learn three things: **clearing a fault on the shuttle**, **the recovery command/ack (v4)**, and **the operator-acknowledged recovery flow**.

### 1) Clearing a fault on the shuttle

A shuttle enters `Fault` state when something goes wrong (e.g. blocked path). From `Fault`, the state machine rejects normal operations. The shuttle cannot just "go back to idle" on its own — that would lose the fact a fault occurred and violate "operator acknowledges".

The shuttle gains a `Recover` method:
```csharp
// src/ShuttleCore/Shuttle.cs (add)
public void Recover(DateTime now)
{
    EnsureInOrder(now);
    if (_state != ShuttleState.Fault)
        throw new ShuttleRejectedException($"cannot recover while {_state.ToWireName()}");
    _state = ShuttleState.Idle;
    _faultReason = null;
    _lastEventAt = now;
}
```

- Only from `Fault`. If already `Idle`/`Moving` etc, reject.
- Moves to `Idle`, clears `FaultReason`.
- Preserves `Position` and `CarriedPalletId` (the physical state remains; recovery addresses the fault condition).
- Uses `EnsureInOrder` for clock consistency.

**Where used later:** After operator acknowledges alarm and issues recovery command, the shuttle endpoint calls `Recover(now)`. Mid-move faults need more complex recovery (later slice).

### 2) The recovery command and ack (v4)

The wire contract needs new message kinds. Following the existing pattern (MoveCommand/CommandAck), add RecoveryCommand and RecoveryAck.

First, update `MessageTypes` and `ContractVersion`:
```csharp
// src/Contracts/MessageTypes.cs
namespace Contracts;

public static class MessageTypes
{
    public const string MoveCommand = "moveCommand";
    public const string CommandAck = "commandAck";
    public const string ShuttleStatus = "shuttleStatus";
    public const string Alarm = "alarm";
    public const string RecoveryCommand = "recoveryCommand";
    public const string RecoveryAck = "recoveryAck";
}

public static class ContractVersion
{
    public const int Current = 4;
}
```

Add the message records (matching envelope pattern):
```csharp
// src/Contracts/RecoveryCommandMessage.cs
using System.Text.Json.Serialization;

namespace Contracts;

public sealed record RecoveryCommandMessage(
    [property: JsonPropertyName("Envelope")] MessageEnvelope Envelope,
    [property: JsonPropertyName("ShuttleId")] string ShuttleId);
```

```csharp
// src/Contracts/RecoveryAckMessage.cs
using System.Text.Json.Serialization;

namespace Contracts;

public sealed record RecoveryAckMessage(
    [property: JsonPropertyName("Envelope")] MessageEnvelope Envelope,
    [property: JsonPropertyName("Accepted")] bool Accepted,
    [property: JsonPropertyName("Reason")] string? Reason);
```

Update `TopicScheme` to add recovery topics:
```csharp
// src/Messaging/TopicScheme.cs (add methods)
public string RecoveryCommand(string shuttleId) => $"{_root}/{shuttleId}/recoveryCommand";
public string RecoveryAck(string shuttleId) => $"{_root}/{shuttleId}/recoveryAck";
public string AllRecoveryAcks => $"{_root}/+/recoveryAck";
```

Update `MessageFamily` enum and QoS mapping (add Recovery):
```csharp
// src/Messaging/MessageFamily.cs
public enum MessageFamily 
{ 
    Command, 
    Ack, 
    Status,
    Alarm,
    Recovery,
}
```

```csharp
// src/MqttMessaging/MqttMessageBus.cs (ToMqttQos)
MessageFamily.Recovery => _options.Qos.Command, // or add Recovery QoS; use Command QoS for commands
```

Update `QosOptions` if needed? Looking at how others do it: commands use `Command` QoS, acks use `Ack` QoS. Recovery is a command-like message from manager→shuttle, so map to `Command` QoS (as above). The QosOptions record already has fields; adding a Recovery field is a config change — but easier to reuse Command QoS. The code above reuses Command QoS for Recovery family. That matches the design intent.

Add `docs/contract/v4.md` documenting the two new kinds with envelope, fields, and example JSON (keep v3 unchanged).

### 3) The recovery flow (operator-acknowledged)

The full flow (for understanding; implementation details vary):
1. Shuttle faults (e.g. blockedPath) → enters Fault state, reports status with FaultReason.
2. Fleet manager receives status → evaluates and publishes `blockedPath` alarm (lesson 16).
3. Operator acknowledges the alarm (outside this lesson's code changes except message contract support).
4. Fleet manager sends `RecoveryCommandMessage` to shuttle (`{root}/{shuttleId}/recoveryCommand`), with correlation ID.
5. Shuttle endpoint receives recovery command → validates, calls `shuttle.Recover(now)`, acknowledges with `RecoveryAckMessage` (Accepted true), publishes healthy status (Idle, FaultReason null).
6. Fleet manager receives ack and status → alarm latch clears (healthy status means no active fault condition).

This lesson focuses on the shuttle-side pieces (Recover method, message kinds, contract v4) and the supporting infrastructure. The fleet manager's recovery command sending logic is part of the full flow; the core building blocks are the contract + shuttle transition.

---

## Recap in plain words

- **Recover** clears a fault: only from Fault → Idle, fault reason gone. Operator must trigger it.
- **v4 adds recovery messages.** RecoveryCommand (manager→shuttle) and RecoveryAck (shuttle→manager) follow the same command/ack pattern as MoveCommand.
- **Operator-acknowledged.** Recovery happens only after operator acknowledgement triggers the command; the shuttle explicitly exits Fault state.

---

## Worked example (toy, not the assignment)

A tiny state machine showing fault → recover → back to normal.

```csharp
using System;

enum State { Idle, Fault }
class ShuttleToy
{
    public State State = State.Idle;
    public string? Fault;
    public void RaiseFault(string reason) { State = State.Fault; Fault = reason; }
    public void Recover() { if (State != State.Fault) throw new Exception("cannot recover"); State = State.Idle; Fault = null; }
}

class Program
{
    static void Main()
    {
        var s = new ShuttleToy();
        s.RaiseFault("blocked path");
        Console.WriteLine($"after fault: {s.State} fault={s.Fault}");
        s.Recover();
        Console.WriteLine($"after recover: {s.State} fault={s.Fault}");
    }
}
```

Output:
```text
after fault: Fault fault=blocked path
after recover: Idle fault=
```

---

## Say it out loud

1. Why can a faulted shuttle only exit via `Recover`, not by starting a move?
2. Why does recovery require a command/ack (v4) rather than just happening automatically?
3. How does the alarm latch know the fault is cleared? (Hint: shuttle status)
4. What does `ContractVersion.Current` become and why?
5. When recovering, do we clear occupancy or reservations? Why not? (connect to record 17)

---

## The assignment

Build the core pieces for operator-acknowledged recovery: shuttle-side recover transition, v4 contract messages/kinds, and supporting types so the system can send/receive recovery commands.

### Requirements

1. **`src/ShuttleCore/Shuttle.cs`** — add `public void Recover(DateTime now)`
   - Call `EnsureInOrder(now)`.
   - If `State != ShuttleState.Fault`, throw `ShuttleRejectedException` with message like `"cannot recover while {state}"`
   - Set `State = ShuttleState.Idle`, `_faultReason = null`, `_lastEventAt = now`.
   - Do not change `Position` or `CarriedPalletId`.

2. **`src/Contracts/MessageTypes.cs`** — add `RecoveryCommand = "recoveryCommand"`, `RecoveryAck = "recoveryAck"`. Set `ContractVersion.Current = 4`.

3. **`src/Contracts/RecoveryCommandMessage.cs`** — new file with record matching envelope pattern:
   - Fields: `MessageEnvelope Envelope`, `string ShuttleId` (with JsonPropertyName attributes like other message types).

4. **`src/Contracts/RecoveryAckMessage.cs`** — new file with record:
   - Fields: `MessageEnvelope Envelope`, `bool Accepted`, `string? Reason` (with JsonPropertyName attributes).

5. **`src/Messaging/TopicScheme.cs`** — add:
   - `public string RecoveryCommand(string shuttleId)`
   - `public string RecoveryAck(string shuttleId)`
   - `public string AllRecoveryAcks`

6. **`src/Messaging/MessageFamily.cs`** — add `Recovery` member.

7. **`src/MqttMessaging/MqttMessageBus.cs`** (ToMqttQos) — add mapping for `MessageFamily.Recovery` → use command QoS (e.g. `_options.Qos.Command`).

8. **Tests** (pure, no Integration trait):
   - `src/ShuttleCore.Tests/StateTransitionTests.cs` — add `Recover_from_fault_enters_idle_and_clears_fault_reason`. Also add at least one rejection: `Recover_from_idle_is_rejected`.
   - `src/Contracts.Tests/AlarmMessageTests.cs` or add new `RecoveryMessageTests`? Or extend existing. Add `Recovery_messages_survive_json_roundtrip` in `src/Contracts.Tests/` (new test method or new test class). Also verify wire names/constants.
   - `src/Messaging.Tests/TopicSchemeTests.cs` — add test for recovery topics (command/ack/wildcard).

9. **Contract docs** — add `docs/contract/v4.md` documenting RecoveryCommand and RecoveryAck (version 4). Keep `docs/contract/v3.md` unchanged.

### Acceptance criteria

```bash
# Files exist
test -f src/Contracts/RecoveryCommandMessage.cs
test -f src/Contracts/RecoveryAckMessage.cs
test -f docs/contract/v4.md

# Build
cd src && dotnet build

# Pure suite: expect RC 0, 8 Passed!, 0 Failed!, 134 + tests? (add ~3-4 new tests; total likely 137-138)
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" 2>&1 | tail -5

# Verify key pieces
grep -q '"recoveryCommand"' src/Contracts/MessageTypes.cs
grep -q '"recoveryAck"' src/Contracts/MessageTypes.cs
grep -q "ContractVersion.Current = 4" src/Contracts/MessageTypes.cs
grep -q "Recover" src/ShuttleCore/Shuttle.cs
grep -q "Recovery" src/Messaging/MessageFamily.cs
grep -rn "RecoveryCommand" docs/contract/v4.md | head -1
grep -rn "RecoveryAck" docs/contract/v4.md | head -1
```

**Note:** The test count may increase slightly (new tests). The key is RC 0 with 0 failures. Integration tests unchanged.

**Stretch goal:** Add a pure test that simulates receiving a RecoveryCommand in ShuttleEndpoint and verifies Recover is called (if ShuttleEndpoint is extended). Or add a manager-side test for recovery ack handling. Keep changes minimal and focused on the three concepts.

**Not in this assignment:** extending ShuttleEndpoint/Program/FleetServeLoop to actually send/receive recovery commands in production paths (those are wiring; the building blocks above are sufficient for this lesson's concepts).
