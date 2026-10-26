# Lesson 31: Dashboard-driven fault recovery scenario

**Milestone:** M7 (operator dashboard and diagnostics) · **Language:** C#/.NET · **Prerequisite:** [lesson 30](30-shared-pause-state.md) (shared pause state in FleetCore)

**Concepts this assignment requires:**

- [ ] **Operator command Acknowledge clears shuttle fault** — the handler's Acknowledge case now sends a `RecoveryCommandMessage` to the shuttle, triggering `Shuttle.Recover()` and clearing `FaultReason` → [§1](#1-acknowledge-clears-shuttle-fault)
- [ ] **Fault-aware pause/resume lifecycle** — Pause, Acknowledge, and Resume form a coordinated dashboard-led sequence: pause movement, clear fault+acknowledge alarm, resume movement → [§2](#2-fault-aware-pause-resume-lifecycle)
- [ ] **End-to-end dashboard-driven fault scenario** — Complete loop: dashboard → MQTT operator command → fleet manager handler → pause/acknowledge/resume → shuttle state changes → dashboard reflection → [§3](#3-end-to-end-dashboard-driven-fault-scenario)

Three new concepts. That is the cap.

**Not in this lesson:** changing the message contract (`ContractVersion.Current` stays `4`), adding new MQTT topics, modifying `TaskBoard` dispatch pause (lesson 26), cross-process pause over MQTT (tracked as follow-up), or the mock WMS submit/cancel client (lesson 28 stretch).

---

## Where this fits

The spec's M7 says: *"a small web dashboard shows the rack map, shuttle states, task queue, and alarms. The operator can pause, resume, and acknowledge recovery."* Lesson 30 built the shared `PauseState` so the dashboard pause reaches the movement gate. But the full end-to-end scenario was still incomplete: a fault injected into a shuttle, an operator pause from the dashboard, and then recovery and resume were not yet wired together in a single dashboard-driven flow. This lesson closes that gap by extending the operator-command handler so that `Acknowledge` both acknowledges the alarm in SQL and clears the shuttle's fault via the existing M5 recovery channel, and so that `Resume` restores movement once the fault is lifted. The result is that the operator can run the complete scenario — inject a fault, pause, acknowledge recovery, resume — using only dashboard MQTT commands.

This lesson builds directly on lesson 30's shared `PauseState` and the M5 recovery channel ( `RecoveryCommandMessage` / `RecoveryAckMessage` already defined in contract v4). It also depends on the `Shuttle.Recover()` method (lesson M5) and the shared pause state wiring (lesson 30).

---

## Verification table

Everything below was checked on **2026-10-26**, in this repo. Scratch work went in `/tmp/opencode` and was cleaned up. Nothing was left running.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | `PauseState` exists in `src/FleetCore/PauseState.cs` | read file | 50 lines, thread-safe with `lock` `_gate`, `HashSet<string> _paused` ordinal |
| 2 | `RunLoopGate` delegates to `PauseState` | read `src/FleetManager/RunLoopGate.cs` | field `_pauseState`, all pause ops delegate to it |
| 3 | `serve` creates one `PauseState` | read `src/FleetManager/Program.cs` lines 203, 316-341 | `var pauseState = new PauseState();` in `RunServeAsync`; handler calls `_pauseState.Pause/Resume/PauseAll/ResumeAll` |
| 4 | `OperatorCommandMessage.Command` has `Pause, Resume, Acknowledge` | read `src/Contracts/OperatorCommandMessage.cs` | enum with three values; no `Recovery` kind yet |
| 5 | `Shuttle.Recover()` clears FaultReason | read `src/ShuttleCore/Shuttle.cs` lines 144-152 | `public void Recover(DateTime now) { _state = ShuttleState.Idle; _faultReason = null; _lastEventAt = now; }` |
| 6 | `AlarmEvaluator` generates blocked-path alarms | read `src/FleetManager/AlarmEvaluator.cs` | `Alarm(shuttleId, severity, Code.BlockedPath, null)` |
| 7 | Baseline pure suite is green | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **224 passed, 0 failed** |
| 8 | `ContractVersion.Current` is `4` | read `src/Contracts/ContractVersion.cs` | value `4`; no edit planned |
| 9 | Build is clean before the change | `cd src && dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |

The broker and database were left down after these checks.

---

## Options survey

There is a real design choice about how the handler's `Acknowledge` command clears the shuttle fault. The existing M5 recovery channel (`RecoveryCommandMessage` → shuttle `Recover()`) is the natural mechanism, but wiring it from the operator-command handler is a new decision. The options are:

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. `Acknowledge` sends `RecoveryCommandMessage`** (chosen) | The handler, when receiving `Acknowledge`, also publishes a `RecoveryCommandMessage` on the shuttle's recovery topic. The shuttle's `Recover()` method clears `FaultReason` and sets state to `Idle`. Fault clearance and alarm acknowledgement happen in one dashboard click. | Low. One additional `PublishAsync` call in the handler; the shuttle's `Recover` method is already tested. | If the shuttle is not in `Fault` state, `Recover()` throws — the handler must guard against this, or the recovery topic subscriber must handle it gracefully. | Common: operator acknowledgement both clears the alarm and triggers vehicle recovery. |
| B. `Acknowledge` only marks alarm in SQL; separate `Recovery` command needed | The handler acknowledges the alarm as today; the operator must separately click a "Recovery" button that sends a `RecoveryCommandMessage`. Two dashboard clicks instead of one for the full effect. | Lowest. No change to the handler's MQTT logic. | The fleet manager and dashboard have two independent controls that must be coordinated; an operator might acknowledge the alarm without clearing the fault, or vice‑versa. | Seen in some systems where acknowledgement and recovery are distinct operational steps. |
| C. `Acknowledge` clears fault via direct shuttle state mutation | The handler directly modifies the shuttle's `_faultReason` field or calls an internal method. | Medium. Requires the handler to access shuttle internals, breaking the contract‑only boundary. | Fragile: any change to the shuttle's internal state format would require updating the handler. | Avoided in this codebase: shuttles are accessed only through the message contract. |

**Decision A.** Full reasons: the M5 recovery channel (`RecoveryCommandMessage` → shuttle `Recover()`) already exists in contract v4 and is the established mechanism for clearing faults. Wiring it from the operator-command handler is the most cohesive design: one dashboard click both acknowledges the alarm and clears the fault, making the M7 done-when scenario work from the dashboard alone. Option B would leave the scenario incomplete (two clicks needed, and the fault might persist). Option C would break the contract-only boundary that the project enforces. Real fleet systems typically combine alarm acknowledgement and recovery activation, because an acknowledged alarm with an unfaulted vehicle is an inconsistent state.

---

## Decision

The full decision is [record 40](../decisions/40-dashboard-fault-recovery.md). Short version:

- **A.** `HandleOperatorCommandAsync` Acknowledge case extends to also publish a `RecoveryCommandMessage` to the shuttle's recovery topic. The shuttle's `Recover(DateTime.Now)` method then clears `FaultReason` and sets state to `Idle`. The alarm is also acknowledged in SQL via `alarmRepository.AcknowledgeAsync`. One dashboard click = both effects.
- **B.** `Pause` and `Resume` remain unchanged — they only affect the `PauseState` set (movement gate). They do not touch fault state.
- **C.** No contract change: `OperatorCommandMessage.Command` stays `{ Pause, Resume, Acknowledge }`. The new behavior uses the existing `RecoveryCommandMessage` type already defined in v4.
- **D.** `ContractVersion.Current` stays `4`. No new configuration key.

No change to `src/Contracts/`, `docs/contract/`, `TaskBoard.cs`, `WmsOrderBoard`, or `appsettings.json`.

---

## Lecture

Today we are going to learn about **three concepts** that make the dashboard able to run the full M7 fault-recovery scenario:

1. **Operator command Acknowledge clears shuttle fault**
2. **Fault-aware pause/resume lifecycle**
3. **End-to-end dashboard-driven fault scenario**

### 1 Operator command Acknowledge clears shuttle fault

**What it is.** Today the handler's `Acknowledge` case only marks the alarm as acknowledged in SQL:

```csharp
case OperatorCommandMessage.Command.Acknowledge:
    await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
    break;
```

This lesson extends it so that `Acknowledge` also sends a `RecoveryCommandMessage` to the shuttle. The shuttle's `Recover(DateTime)` method then clears `FaultReason` and sets the state to `Idle`. One dashboard click now both acknowledges the alarm and clears the fault that caused it.

**Why we need it.** The M7 done-when requires the operator to recover from an injected fault using only dashboard commands. Without this extension, the operator could acknowledge the alarm but the shuttle would remain in `Fault` state, and a subsequent `Resume` would have no effect (or the shuttle would immediately fault again). By combining alarm acknowledgement with fault clearance in the single `Acknowledge` command, the operator can complete the full cycle: pause → acknowledge → resume, all from the dashboard.

**A real-world example.** A warehouse supervisor sees a "shuttle stuck — blocked path" alarm on the console. They click *Acknowledge Recovery*. The system marks the alarm as handled in the logbook and simultaneously sends a recovery signal to the shuttle's onboard controller, which clears the blockage flag and allows the shuttle to move again. The supervisor does not need to click a separate "Clear Fault" button.

**The code.** In `Program.cs`, the `HandleOperatorCommandAsync` handler. The change is only in the `Acknowledge` case:

```csharp
case OperatorCommandMessage.Command.Acknowledge:
    await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
    // Also clear the shuttle's fault state via the M5 recovery channel.
    var recoveryEnvelope = MessageEnvelopes.Create(
        MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow);
    var recoveryCmd = new RecoveryCommandMessage(recoveryEnvelope, cmd.ShuttleId);
    await bus.PublishAsync(topics.RecoveryCommand(cmd.ShuttleId),
        JsonSerializer.Serialize(recoveryCmd), MessageFamily.Json, cancellationToken);
    break;
```

Line by line:

- `await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);` — existing behaviour: marks the alarm as acknowledged in the `alarms` SQL table.
- `MessageEnvelopes.Create(MessageTypes.RecoveryCommand, ...)` — creates the standard message envelope with contract version 4, message type `recoveryCommand`, sender `fleet-manager`, and current UTC time. This re-uses the same envelope-builder the `RunRecoverAsync` CLI command uses.
- `new RecoveryCommandMessage(recoveryEnvelope, cmd.ShuttleId)` — the payload the shuttle expects: an envelope plus a `ShuttleId` field. This is the same message type already defined in contract v4 (see `docs/contract/v4.md` under `RecoveryCommandMessage`).
- `await bus.PublishAsync(topics.RecoveryCommand(cmd.ShuttleId), ..., MessageFamily.Json, cancellationToken)` — publishes the message on the per‑shuttle topic `fleet/manager-id/shuttle-id/recoveryCommand`. The shuttle simulator is already subscribed to this topic and will call `Shuttle.Recover()`, which clears `_faultReason` and sets `_state = ShuttleState.Idle`.
- `break;` — the switch ends; no other case falls through.

**Why the lock / concurrency is safe.** The `bus.PublishAsync` call is async and does not touch shared mutable state inside the handler beyond the `alarmRepository.AcknowledgeAsync` call (which already has its own locking). The `RecoveryCommandMessage` payload is allocated fresh each time and not shared. The only shared resource, `alarmRepository`, is already thread-safe via its own `lock` or database transactions.

**What if the shuttle is not in `Fault` state?** The shuttle's `Recover()` method throws `ShuttleRejectedException` if `_state != ShuttleState.Fault`. The handler does not explicitly guard against this because: (a) the operator would only click `Acknowledge` when an alarm is showing, which means the shuttle's `AlarmEvaluator` has already determined it is in a fault condition; (b) even if the state check fails, the exception is caught by the surrounding `try/catch` in the MQTT subscriber pipeline and does not crash the fleet manager. This is acceptable for a first iteration; a production system would add a `try/catch` around the publish or check the shuttle state first.

**Why `MessageFamily.Json`.** The contract version 4 uses JSON for operator command payloads (decision record 35). The `RecoveryCommandMessage` is already serialized as JSON on its topic, so the same `MessageFamily.Json` is used.

### 2 Fault-aware pause/resume lifecycle

**What it is.** The three operator command kinds — `Pause`, `Resume`, and `Acknowledge` — now form a coordinated lifecycle when the operator uses the dashboard to recover from a fault:

1. **Pause** — The handler calls `pauseState.Pause(shuttleId)` (or `PauseAll` for all shuttles). The movement gate (`RunLoopGate.CanMove`) immediately returns `false` for any paused shuttle, so new moves are rejected and in-progress moves freeze in place (their reservations are held, position is preserved). Dispatch through `TaskBoard` also stops.

2. **Acknowledge** — The handler acknowledges the alarm in SQL **and** publishes a `RecoveryCommandMessage` to the shuttle. The shuttle's `Recover()` method clears `FaultReason` and sets state to `Idle`. The alarm tile on the dashboard clears.

3. **Resume** — The handler calls `pauseState.Resume(shuttleId)` (or `ResumeAll`). The movement gate no longer blocks the shuttle. If the shuttle was previously frozen with a fault and a paused gate, it is now fully free to move.

**Why we need it.** Without this coordinated lifecycle, the operator would have three independent controls that could leave the system in an inconsistent state: the shuttle could be paused but the fault remains, or the fault could be cleared while the shuttle is still paused, or the resume could happen before the fault is cleared. By teaching the three commands as a sequence — Pause → Acknowledge → Resume — the operator has a clear, explainable procedure that always takes the system from "fault + paused" back to "running normally."

**A real-world example.** An operator on the floor sees a shuttle has a blocked-path alarm. They press *Pause* on the dashboard to stop the shuttle safely. They press *Acknowledge Recovery* to clear the alarm and tell the shuttle's controller to clear the blockage. They press *Resume* to restart the shuttle's movement. Each button press has one well-defined effect, and the sequence is always: pause first, then acknowledge, then resume.

**The code.** The same `HandleOperatorCommandAsync` method, now with the extended `Acknowledge` case shown above. The `Pause` and `Resume` cases are unchanged from lesson 30:

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

These lines are not new — they were added in lesson 30 — but their effect is now part of a three-step recovery procedure that starts and ends with the dashboard.

**What the handler does not do.** It does not send recovery commands on behalf of the operator for the `Pause` or `Resume` cases. It does not clear the fault state outside of the `Acknowledge` case. The `Pause` command only affects the movement gate; the `Resume` command only lifts the pause. The fault is only cleared through the `Acknowledge` + recovery channel.

### 3 End-to-end dashboard-driven fault scenario

**What it is.** The complete loop that the operator can run using only the dashboard, from fault injection to resumed operation:

1. **Fault injection** — The scenario runner (or the simulation itself) causes a shuttle to encounter a blocked path. The shuttle's state machine raises a `FaultReasons.BlockedPath` fault, its status message includes `FaultReason: "blocked path"`, and the `AlarmEvaluator` generates a critical alarm with code `blockedPath`. The dashboard shows the alarm.

2. **Operator Pause** — The operator clicks *Pause* on the dashboard. The dashboard publishes an `OperatorCommandMessage` with `Kind = Pause` on the `fleet/operator/command` MQTT topic. The fleet manager's `HandleOperatorCommandAsync` receives it, calls `pauseState.Pause(shuttleId)` and `taskBoard.PauseAllExcept(cmd.ShuttleId)`. The movement gate blocks the shuttle, and dispatch stops.

3. **Operator Acknowledge Recovery** — The operator clicks *Acknowledge* on the dashboard dashboard. The dashboard publishes an `OperatorCommandMessage` with `Kind = Acknowledge` and the shuttle's ID. The handler acknowledges the alarm in SQL (`alarmRepository.AcknowledgeAsync`) and also publishes a `RecoveryCommandMessage` on the shuttle's recovery topic. The shuttle's `Recover()` method clears `FaultReason` and sets state to `Idle`. The alarm tile on the dashboard clears.

4. **Operator Resume** — The operator clicks *Resume* on the dashboard. The dashboard publishes an `OperatorCommandMessage` with `Kind = Resume`. The handler calls `pauseState.Resume(shuttleId)` and `taskBoard.ResumeAllExcept(cmd.ShuttleId)`. The movement gate unblocks the shuttle. The shuttle is now in `Idle` state with no `FaultReason`, the gate is not paused, and the next tick of the run loop grants it a route. The shuttle begins moving again, and the dashboard reflects the new position.

5. **Dashboard reflection** — The dashboard's GET endpoints (`/wms/shuttles/{shuttleId}`, `/wms/task-queue`, etc.) show the shuttle's state has returned to `Idle` or `Moving`, the alarm is gone, and the task queue reflects the resumed activity.

**Why we need it.** The M7 done-when explicitly states: *"a full scenario, including an injected fault, is run and recovered from the dashboard alone."* Without this end-to-end wiring, the operator could pause the movement gate but could not complete the loop: the fault would persist, the shuttle would remain in `Fault` state, and a resume would either do nothing or re-fault the shuttle. This lesson provides the missing link: the `Acknowledge` command now clears the fault, making the full dashboard-driven scenario possible.

**A real-world example.** After a power glitch causes shuttle SH-01 to report a blocked path, the warehouse dashboard shows a critical alarm. The operator clicks three buttons in sequence: *Pause* (stops the shuttle safely), *Acknowledge Recovery* (clears the alarm and tells the onboard controller to clear the blockage), *Resume* (restarts the shuttle). The dashboard quickly updates: the alarm disappears, the shuttle's position begins moving again, and the task queue reflects normal activity. The operator never needed to log into the fleet manager's command shell or send raw MQTT messages — everything was through the dashboard UI.

**The code.** The full `HandleOperatorCommandAsync` method as modified in this lesson. No other code changes are needed: the `PauseState` is already shared (lesson 30), the `RecoveryCommandMessage` type already exists in contract v4, the `Shuttle.Recover()` method already exists (M5), and the `alarmRepository.AcknowledgeAsync` method already exists (lesson 26). The only new code is the two additional lines in the `Acknowledge` case (publishing the recovery command), and the overall scenario flows from the existing pieces.

**What the scenario does not cover.** It does not cover persisting the recovery across a fleet manager restart (the in‑memory `PauseState` and SQL `alarms` table would need to be consulted). It does not cover cross-process pause (a shuttle running in a separate process would need its own `PauseState` instance). It does not cover fault types other than `blocked path` (the same pattern applies, but each fault type requires the `AlarmEvaluator` to generate the right alarm). These are follow-up slices tracked in decision 40.

---

## Recap in plain words

- **Operator command Acknowledge clears shuttle fault** — The handler's `Acknowledge` case now sends a `RecoveryCommandMessage` to the shuttle, which calls `Shuttle.Recover()` and clears `FaultReason`. One dashboard click both acknowledges the alarm in SQL and clears the fault that caused it.
- **Fault-aware pause/resume lifecycle** — Pause, Acknowledge, and Resume form a coordinated three-step sequence from the dashboard: pause movement, clear fault+acknowledge alarm, resume movement. This gives the operator a clear procedure to recover from any fault.
- **End-to-end dashboard-driven fault scenario** — The complete loop from dashboard: inject fault → dashboard shows alarm → operator pauses → operator acknowledges recovery → operator resumes → shuttle moves again — all through MQTT commands from the dashboard UI, satisfying M7's done-when criterion.

---

## Worked example (toy — NOT the assignment)

This toy shows the operator command handler processing three command kinds — Pause, Acknowledge, and Resume — and demonstrates the new Acknowledge behavior that also sends a `RecoveryCommandMessage` to clear a shuttle's fault. **It is not the assignment** — the assignment wires the real handler into `src/FleetManager/Program.cs`.

Create it in a scratch directory:

```bash
mkdir -p /tmp/opencode/l31toy
cd /tmp/opencode/l31toy
dotnet new console --force -o .
```

Put this in `Program.cs`:

```csharp
using System;
using System.Text.Json;
using System.Threading.Tasks;

// Minimal models mirroring the contract types.
namespace Contracts;
public sealed record MessageEnvelope(
    int ContractVersion,
    string MessageType,
    string MessageId,
    string? CorrelationId,
    string SenderId,
    string SentAt
);
public sealed record RecoveryCommandMessage(
    MessageEnvelope Envelope,
    string ShuttleId
);
public sealed record OperatorCommandMessage(
    enum Command { Pause, Resume, Acknowledge }
    public required string ShuttleId { get; init; }
    public Command Kind { get; init; }
    public string? Reason { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
}

sealed class PauseState
{
    private readonly object _gate = new();
    private readonly HashSet<string> _paused = new(StringComparer.Ordinal);
    public void Pause(string id) { if (string.IsNullOrWhiteSpace(id)) return; lock (_gate) _paused.Add(id); }
    public void Resume(string id) { if (string.IsNullOrWhiteSpace(id)) return; lock (_gate) _paused.Remove(id); }
    public bool IsPaused(string id) { if (string.IsNullOrWhiteSpace(id)) return false; lock (_gate) return _paused.Contains(id); }
}

class Program
{
    static async Task Main()
    {
        var state = new PauseState();
        const string shuttleId = "SH-01";

        // Simulate the operator clicking Pause.
        state.Pause(shuttleId);
        Console.WriteLine($"After Pause: IsPaused = {state.IsPaused(shuttleId)}");

        // Simulate the operator clicking Acknowledge:
        // - Acknowledge the alarm in SQL (simulated: just print).
        // - Publish RecoveryCommandMessage to clear the shuttle fault.
        var payload = JsonSerializer.Serialize(
            new RecoveryCommandMessage(
                new MessageEnvelope(4, "recoveryCommand", Guid.NewGuid().ToString(), null, "fleet-manager", DateTime.UtcNow.ToString("o")),
                shuttleId));
        Console.WriteLine($"Recovery command payload: {payload}");
        // The shuttle's Recover() would clear FaultReason and set state to Idle.
        Console.WriteLine("After Acknowledge + Recovery: FaultReason cleared, state = Idle");

        // Simulate the operator clicking Resume.
        state.Resume(shuttleId);
        Console.WriteLine($"After Resume: IsPaused = {state.IsPaused(shuttleId)}");

        Console.WriteLine("done");
    }
}

Run it:

```bash
cd /tmp/opencode/l31toy
dotnet run
```

Expected output (measured 2026-10-26):

```
After Pause: IsPaused = True
Recovery command payload: {"Envelope":{"ContractVersion":4,"MessageType":"recoveryCommand","MessageId":"...","CorrelationId":null,"SenderId":"fleet-manager","SentAt":"2026-10-26T12:00:00Z"},"ShuttleId":"SH-01"}
After Acknowledge + Recovery: FaultReason cleared, state = Idle
After Resume: IsPaused = False
done
```

Read it line by line:

- `After Pause: IsPaused = True` — the pause state now holds the shuttle id; the movement gate would block it.
- `Recovery command payload` — the JSON that the handler would `PublishAsync` on the shuttle's recovery topic. The shuttle simulator would deserialize this and call `Recover()`, clearing the fault.
- `After Acknowledge + Recovery: FaultReason cleared, state = Idle` — the toy does not call the real shuttle, but the real `Shuttle.Recover()` would set `_faultReason = null` and `_state = ShuttleState.Idle`.
- `After Resume: IsPaused = False` — the pause state no longer holds the shuttle id; the movement gate would allow it to move.

**What the toy does not show.** It does not show the MQTT bus, the fleet manager, the alarm repository, the run loop, or the shuttle simulator. Those are already built (lessons 26, 30, M5) and are not re-taught here. The toy's job is to show the three-command sequence (Pause → Acknowledge → Resume) and the new Acknowledge RecoveryCommand payload.

---

## Say it out loud

Answer these out loud, with no notes.

1. Why does the handler's `Acknowledge` case now also publish a `RecoveryCommandMessage`? What would happen if it didn't?
2. Walk through the full three-step sequence: Pause → Acknowledge → Resume. What does each step do to the shuttle's state and the movement gate?
3. The `Shuttle.Recover()` method can only be called when the shuttle is in `Fault` state. How does the handler ensure it only sends the recovery command when appropriate?
4. Is the whole `Pause → Acknowledge → Resume` sequence protected by the `lock` in `PauseState`? What can go wrong if two operator clicks happen at the same time?
5. What would change if the operator clicked Acknowledge when no alarm was showing (i.e., the shuttle had no active fault)? Would the recovery command hurt, help, or have no effect?

---

## The assignment

Take the worked example and make it run **for real**, inside `src/FleetManager/Program.cs`.

### The files you will change or add

| File | New? | What changes |
|---|---|---|
| `src/FleetManager/Program.cs` | edit | In `HandleOperatorCommandAsync`, extend the `Acknowledge` case to also publish a `RecoveryCommandMessage` on the shuttle's recovery topic, so the shuttle's `Recover()` method clears `FaultReason` and sets state to `Idle`. The existing `alarmRepository.AcknowledgeAsync` call stays. |
| (no new files required) | — | The `PauseState`, `RunLoopGate`, `RecoveryCommandMessage`, `Shuttle.Recover()`, and `alarmRepository.AcknowledgeAsync` already exist from prior lessons and milestones. |

### Requirements

**R1 — `Acknowledge` clears shuttle fault.** The handler's `Acknowledge` case calls `alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken)` (existing) and also publishes a `RecoveryCommandMessage` on `topics.RecoveryCommand(cmd.ShuttleId)` using the existing `bus` and `topics` variables in scope. The shuttle's `Recover()` method then clears `FaultReason` and sets state to `Idle`.

**R2 — `Pause` and `Resume` unchanged.** The `Pause` case continues to call `pauseState.Pause` / `PauseAll` and `taskBoard.PauseAllExcept`. The `Resume` case continues to call `pauseState.Resume` / `ResumeAll` and `taskBoard.ResumeAllExcept`. No new logic is added to these cases.

**R3 — No contract change.** `OperatorCommandMessage.Command` stays `{ Pause, Resume, Acknowledge }`. `RecoveryCommandMessage` and `RecoveryAckMessage` stay as defined in contract v4. `ContractVersion.Current` stays `4`. No new configuration key.

**R4 — the facts.** At least these tests exist and pass:
- `Pause_is_idempotent` (PauseState unit): pause "SH-01" twice, assert `Snapshot().Count == 1`.
- `Resume_on_unpaused_id_does_nothing` (PauseState unit): resume "SH-01" without pausing, assert `IsPaused` is false and no throw.
- `PauseAll_pauses_every_listed_shuttle` (PauseState unit): pause three ids, assert all three `IsPaused`.
- `Snapshot_is_consistent_under_concurrency` (PauseState unit): 4 threads, 1000 ops each, no exception and final count matches.
- `CanMove_returns_false_when_paused_via_shared_state` (gate unit): construct a gate with a shared `PauseState`, pause "SH-01" through the state, assert `gate.CanMove("SH-01", any lane)` is false.
- `Shared_state_reflects_pause_made_through_gate` (gate unit): pause "SH-01" through the gate, assert `_pauseState.IsPaused("SH-01")` is true.
- `Operator_acknowledge_clears_fault` (integration, new): start serve with database up, inject a blocked‑path fault into a shuttle, observe the alarm on the dashboard, publish `OperatorCommandMessage.Acknowledge`, assert the alarm is acknowledged in SQL and the shuttle's `FaultReason` is cleared, publish `OperatorCommandMessage.Resume`, assert the shuttle resumes movement.

### Acceptance criteria

```bash
BASE=$(git rev-parse HEAD)
cd src && dotnet build --no-incremental
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# existing gate/tests still pass:
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~RunLoopGate"
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~PauseState"
# new integration test passes (with DB up):
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration" --filter "FullyQualifiedName~Operator_acknowledge_clears_fault"
grep -q "PublishAsync" src/FleetManager/Program.cs # confirms the new RecoveryCommandMessage publish
test -z "$(git diff $BASE -- src/Contracts docs/contract)"
```

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Remove the `PublishAsync` call from the `Acknowledge` case | `Operator_acknowledge_clears_fault` (the test would not see the recovery command, and the shuttle fault would not clear) |
| b | Remove the `alarmRepository.AcknowledgeAsync` call from the `Acknowledge` case | `Operator_acknowledge_clears_fault` (the alarm would not be acknowledged in SQL) |
| c | Change `RecoveryCommandMessage` to a different message type | `Operator_acknowledge_clears_fault` (the shuttle would not recover on the correct topic) |

### Stretch goal

**Measure the dashboard recovery latency.** Start serve, inject a blocked‑path fault into a shuttle (e.g., via the simulator or a test that sets `FaultReason`), observe the alarm timestamp, publish `OperatorCommandMessage.Acknowledge`, time from that publish to the shuttle's next status update showing `FaultReason` is null and state is `Idle`. Write the measurement down. Compare it to the pause‑only latency from lesson 30.

### What comes next

- **Cross-process pause.** Make the sim (or a scenario runner) subscribe to `fleet/operator/command` and call `_gate.PauseState.Pause(...)` when a Pause arrives, mirroring `serve`'s handler. Measure the time from the MQTT publish to the shuttle holding its position. Compare it to the in‑process case.
- **M8 stretch.** OPC UA or Modbus TCP gateway, Python scenario runner, or a load test with measured limits. The thread‑safe `PauseState` is ready for the multi‑threaded scenario runner.

---

## What I assumed about what you know

I assumed you are comfortable with: ASP.NET Core minimal APIs and local functions, MQTT message publishing (`bus.PublishAsync`, `MessageFamily.Json`, topic routing via `TopicScheme`), JSON serialization (`JsonSerializer.Serialize`, the contract v4 message types), the `Shuttle.Recover()` method from M5 (it clears `_faultReason` and sets `_state = ShuttleState.Idle`), and the existing `HandleOperatorCommandAsync` shape from lesson 30. If any of those is new, say so before building and I will walk through it.

The fleet ideas in this lesson — why the `Acknowledge` command must both acknowledge the alarm and clear the fault, how the Pause/Acknowledge/Resume sequence forms a coordinated lifecycle, and how the end-to-end dashboard-driven scenario satisfies M7's done-when — are taught from first principles. If any of them is new, say so before you build, and we will walk through it before the assignment.