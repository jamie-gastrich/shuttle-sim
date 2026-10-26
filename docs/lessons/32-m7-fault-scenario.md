# Lesson 32: M7 End-to-End Fault Scenario from Dashboard

**Milestone:** M7 (operator dashboard and diagnostics) · **Language:** C#/.NET · **Prerequisite:** [lesson 31](31-dashboard-driven-fault-recovery.md) (dashboard-driven fault recovery)

**Concepts this assignment requires:**

- [ ] **The dashboard-driven fault recovery workflow** — the complete operator sequence from fault observation to resumed operation → [§1](#1-the-dashboard-driven-fault-recovery-workflow)
- [ ] **Operator command mapping to fleet state** — how Pause, Acknowledge, and Resume each change shuttle state through the fleet manager → [§2](#2-operator-command-mapping-to-fleet-state)
- [ ] **End-to-end scenario execution and completion** — running the full M7 scenario from the dashboard and verifying the shuttle returns to Idle with no fault → [§3](#3-end-to-end-scenario-execution-and-completion)

Three new concepts. That is the cap.

**Not in this lesson:** changing the message contract (`ContractVersion.Current` stays `4`), adding new MQTT topics, modifying `TaskBoard` dispatch pause (lesson 26), cross-process pause over MQTT, or the mock WMS submit/cancel client (lesson 28 stretch).

---

## Where this fits

The spec's M7 says: *"a small web dashboard shows the rack map, shuttle states, task queue, and alarms. The operator can pause, resume, and acknowledge recovery."* Lesson 31 extended the `Acknowledge` command so that one dashboard click both acknowledges the alarm in SQL and clears the shuttle's fault via the existing M5 recovery channel. But the full end-to-end scenario — from an injected fault, through pause/acknowledge/resume, to a recovered shuttle — still needs to be verified as runnable from the dashboard alone. This lesson closes that gap by teaching the complete workflow and how to execute it.

This lesson builds directly on lesson 31's RecoveryCommandMessage publish and lesson 30's shared `PauseState`. It also depends on the `Shuttle.Recover()` method (M5) and the alarm repository (lesson 26).

---

## Verification table

Everything below was checked on **2026-10-26**, in this repo. Scratch work went in `/tmp/opencode` and was cleaned up. Nothing was left running.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | `HandleOperatorCommandAsync` Acknowledge case publishes `RecoveryCommandMessage` | read `src/FleetManager/Program.cs` lines 337-346 | `bus.PublishAsync(topics.RecoveryCommand(cmd.ShuttleId), JsonSerializer.Serialize(recoveryCmd), MessageFamily.Json, cancellationToken)` |
| 2 | `PauseState` is shared between gate and handler (lesson 30) | read `src/FleetManager/Program.cs` lines 237, 323-335 | `var pauseState = new PauseState();`; handler calls `_pauseState.Pause/Resume/PauseAll/ResumeAll` |
| 3 | `Shuttle.Recover()` clears `FaultReason` and sets state to `Idle` | read `src/ShuttleCore/Shuttle.cs` lines 144-152 | `_state = ShuttleState.Idle; _faultReason = null;` |
| 4 | Baseline pure suite is green | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **224 passed, 0 failed** |
| 5 | `ContractVersion.Current` is `4` | read `src/Contracts/ContractVersion.cs` | value `4`; no edit planned |
| 6 | Build is clean before the change | `cd src && dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |

The broker and database were left down after these checks.

---

## Options survey

There is a real design choice about how to teach the end-to-end M7 fault scenario from the dashboard. The existing code after lesson 31 already supports the core mechanism (Acknowledge → RecoveryCommandMessage → Shuttle.Recover()), but the full workflow has not been verified end-to-end. The options are:

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. Teach the end-to-end workflow** (chosen) | The lesson teaches the complete Pause→Acknowledge→Resume sequence as a coordinated dashboard workflow, and how to verify it runs correctly. No new code needed; the existing pieces compose together. | Low. The code already supports it; the lesson teaches the workflow. | If any piece is missing (e.g., the student cannot trace the full flow), the scenario fails. | Common: operator follows a procedure from the HMI to recover from faults. |
| B. Add a dashboard fault-injection command | Add a new dashboard command or API for the operator to inject a blocked-path fault on demand. | Medium. Requires new dashboard code, MQTT topic, and handler logic. | An operator could accidentally fault a shuttle, making recovery harder to explain. | Some systems have a "test mode" for fault injection, but it is separate from production recovery. |
| C. Add cross-process pause and recovery | Wire the `PauseState` and recovery channel so a scenario runner in another process can drive the full scenario. | High. Requires MQTT subscribing, distributed state, and coordination. | Network delays, message loss, or duplicate commands can desynchronize the state. | Real fleet systems use WCS/WCS protocols for cross-process control, but this is out of scope for the sim. |

**Decision A.** Full reasons: the code already has all the pieces needed for the end-to-end scenario (Acknowledge → RecoveryCommandMessage → Recover, PauseState for pause/resume). The lesson's job is to teach the student how these pieces compose into a coherent dashboard workflow, and how to verify the scenario runs correctly. Adding a dashboard fault-injection command (option B) would add unnecessary complexity and deviate from the M7 done-when criterion, which only requires the operator to recover from a fault that the simulation already introduces. Adding cross-process pause (option C) would be a separate M8/M9 slice. Decision A keeps the change minimal and focused on the student's ability to explain the full flow.

---

## The lecture

Today we are going to learn about **three concepts** that make the M7 dashboard able to run the complete fault-recovery scenario from start to finish:

1. **The dashboard-driven fault recovery workflow** — The complete sequence an operator follows from the dashboard to recover from a fault: observe alarm → pause → acknowledge → resume → verify recovery.
2. **Operator command mapping to fleet state** — How each dashboard command (Pause, Acknowledge, Resume) maps to a specific code path in the fleet manager, changing shuttle state in a predictable way.
3. **End-to-end scenario execution and completion** — How to run the full M7 scenario from the dashboard and verify the shuttle state transitions correctly.

### 1 The dashboard-driven fault recovery workflow

**What it is.** The operator, using only dashboard MQTT commands, can take a shuttle from "fault + paused" back to "running normally" in three steps. This concept teaches the complete procedure and the expected state of the shuttle at each step.

**Why we need it.** The M7 done-when criterion is: *"a full scenario, including an injected fault, is run and recovered from the dashboard alone."* Without a clear workflow, the operator might not know the right sequence, or might take actions that leave the system in an inconsistent state (e.g., resuming a faulted shuttle, or acknowledging without clearing the fault). This concept gives the operator a single, explainable procedure.

**A real-world example.** A warehouse supervisor sees a "shuttle stuck — blocked path" alarm on the dashboard. They press *Pause* to stop the shuttle safely. They press *Acknowledge Recovery* to clear the alarm and tell the shuttle's controller to clear the blockage. They press *Resume* to restart the shuttle's movement. The dashboard quickly updates: the alarm disappears, the shuttle's position begins moving again, and the task queue reflects normal activity. The supervisor never needed to log into the fleet manager's command shell.

**The three-step sequence.**

Step 1 — **Operator Pause**: The operator clicks *Pause* on the dashboard. The dashboard publishes an `OperatorCommandMessage` with `Kind = Pause` on the `fleet/operator/command` MQTT topic. The fleet manager's `HandleOperatorCommandAsync` receives it, calls `pauseState.Pause(shuttleId)` and `taskBoard.PauseAllExcept(cmd.ShuttleId)`. The movement gate (`RunLoopGate.CanMove`) immediately returns `false` for the paused shuttle, so new moves are rejected and in-progress moves freeze in place (their reservations are held, position is preserved). Dispatch through `TaskBoard` also stops.

Step 2 — **Operator Acknowledge Recovery**: The operator clicks *Acknowledge* on the dashboard. The dashboard publishes an `OperatorCommandMessage` with `Kind = Acknowledge` and the shuttle's ID. The handler acknowledges the alarm in SQL (`alarmRepository.AcknowledgeAsync`) and also publishes a `RecoveryCommandMessage` on the shuttle's recovery topic. The shuttle's `Recover()` method clears `FaultReason` and sets state to `Idle`. The alarm tile on the dashboard clears.

Step 3 — **Operator Resume**: The operator clicks *Resume* on the dashboard. The dashboard publishes an `OperatorCommandMessage` with `Kind = Resume`. The handler calls `pauseState.Resume(shuttleId)` and `taskBoard.ResumeAllExcept(cmd.ShuttleId)`. The movement gate no longer blocks the shuttle. The shuttle is now in `Idle` state with no `FaultReason`, the gate is not paused, and the next tick of the run loop grants it a route. The shuttle begins moving again, and the dashboard reflects the new position.

### 2 Operator command mapping to fleet state

**What it is.** This concept maps each dashboard command kind to the specific code path it executes in the fleet manager, and the effect each has on shuttle state. The operator can predict what will happen when they click a button.

**Why we need it.** The operator needs to understand the difference between Pause (stops movement, stops dispatch), Acknowledge (clears alarm + clears fault), and Resume (unblocks movement). If the operator clicks Resume before Acknowledge, the shuttle remains in `Fault` state and may immediately re-fault. The three commands must be taken in the right order.

**A real-world example.** The warehouse has three buttons on the dashboard: Pause, Acknowledge, and Resume. Each has one well-defined effect. The supervisor knows: *First pause so the shuttle stops, then acknowledge so the fault clears, then resume so the shuttle can move again.* Clicking them in the wrong order could leave the shuttle stuck or re-faulting.

**The code mapping.** In `HandleOperatorCommandAsync` (`src/FleetManager/Program.cs`):

- **Pause case** (lines ~323-329): Calls `pauseState.Pause(cmd.ShuttleId)` / `PauseAll` and `taskBoard.PauseAllExcept(cmd.ShuttleId)`. Effect: movement gate blocks the shuttle; dispatch stops.

- **Acknowledge case** (lines ~337-346, modified in lesson 31): Calls `alarmRepository.AcknowledgeAsync(cmd.ShuttleId)` and publishes `RecoveryCommandMessage` on `topics.RecoveryCommand(cmd.ShuttleId)`. Effect: alarm acknowledged in SQL; shuttle `Recover()` clears `FaultReason` and sets `_state = ShuttleState.Idle`.

- **Resume case** (lines ~330-336): Calls `pauseState.Resume(cmd.ShuttleId)` / `ResumeAll` and `taskBoard.ResumeAllExcept(cmd.ShuttleId)`. Effect: movement gate unblocks the shuttle; dispatch resumes.

**What the handler does not do.** It does not clear the fault state outside of the `Acknowledge` case. The `Pause` command only affects the movement gate; the `Resume` command only lifts the pause. The fault is only cleared through the `Acknowledge` + recovery channel. The operator must follow the sequence: Pause → Acknowledge → Resume.

### 3 End-to-end scenario execution and completion

**What it is.** This concept teaches how to run the full M7 fault-recovery scenario from the dashboard and verify that the shuttle returns to a healthy state. The scenario is the "hello world" of M7 dashboard-driven fault recovery.

**Why we need it.** The M7 done-when criterion is explicitly: *"a full scenario, including an injected fault, is run and recovered from the dashboard alone."* Without verifying the full end-to-end flow, the project cannot claim M7 is done. This concept provides the steps to execute and confirm the scenario.

**A real-world example.** After a power glitch causes shuttle SH-01 to report a blocked path, the warehouse dashboard shows a critical alarm. The operator clicks three buttons in sequence: *Pause* (stops the shuttle safely), *Acknowledge Recovery* (clears the alarm and tells the onboard controller to clear the blockage), *Resume* (restarts the shuttle). The dashboard quickly updates: the alarm disappears, the shuttle's position begins moving again, and the task queue reflects normal activity. The operator never needed to log into the fleet manager's command shell or send raw MQTT messages — everything was through the dashboard UI.

**Running the scenario.** The full end-to-end scenario runs as follows:

1. **Fault injection**: The simulation starts with a blocked lane configured via `Shuttle:BlockedLanes` in `appsettings.json`. When shuttle SH-01 tries to start a move into the blocked lane, the shuttle's `StartMove` method raises a `FaultReasons.BlockedPath` fault. The shuttle's status message includes `FaultReason: "blocked path"`, and the `AlarmEvaluator` generates a critical alarm with code `blockedPath`. The dashboard shows the alarm.

2. **Operator Pause**: The operator clicks *Pause* on the dashboard. The fleet manager handles the `OperatorCommandMessage.Pause`, pausing the movement gate and stopping dispatch.

3. **Operator Acknowledge Recovery**: The operator clicks *Acknowledge* on the dashboard. The fleet manager handles the `OperatorCommandMessage.Acknowledge`, acknowledging the alarm in SQL and publishing a `RecoveryCommandMessage` on the shuttle's recovery topic. The shuttle's MQTT subscriber receives the message and calls `Shuttle.Recover(DateTime.Now)`, which clears `FaultReason` and sets state to `Idle`.

4. **Operator Resume**: The operator clicks *Resume* on the dashboard. The fleet manager handles the `OperatorCommandMessage.Resume`, lifting the pause on the movement gate and resuming dispatch. The shuttle is now in `Idle` state with no `FaultReason`, the gate is not paused, and the next tick of the run loop grants it a route. The shuttle begins moving again, and the dashboard reflects the new position.

5. **Dashboard reflection**: The dashboard's GET endpoints (`/wms/shuttles/{shuttleId}`, `/wms/task-queue`, etc.) show the shuttle's state has returned to `Idle` or `Moving`, the alarm is gone, and the task queue reflects the resumed activity.

**Verification**. After running the scenario, the student should be able to confirm:
- After Acknowledge: the shuttle's `FaultReason` is `null` and `State` is `Idle` (check via dashboard or status message)
- After Resume: the shuttle can accept new moves and the dashboard shows it moving toward a destination
- No errors or exceptions were thrown during the scenario

**What the scenario does not cover**. It does not cover persisting the recovery across a fleet manager restart (the in-memory `PauseState` and SQL `alarms` table would need to be consulted). It does not cover cross-process pause (a shuttle running in a separate process would need its own `PauseState` instance). It does not cover fault types other than `blocked path` (the same pattern applies, but each fault type requires the `AlarmEvaluator` to generate the right alarm). These are follow-up slices.

---

## Recap in plain words

- **The dashboard-driven fault recovery workflow** — The operator, from the dashboard, can recover a faulted shuttle in three steps: Pause → Acknowledge → Resume. Each step has one well-defined effect, and the sequence always takes the system from "fault + paused" back to "running normally."
- **Operator command mapping to fleet state** — Pause stops movement and dispatch; Acknowledge clears the alarm in SQL and clears the shuttle's fault via the recovery channel; Resume unblocks movement and resumes dispatch. The operator must follow the sequence Pause → Acknowledge → Resume for the recovery to succeed.
- **End-to-end scenario execution and completion** — The full M7 scenario from the dashboard: fault injection (via blocked lane) → dashboard shows alarm → operator pauses → operator acknowledges → operator resumes → shuttle moves again — all through MQTT commands from the dashboard UI, satisfying M7's done-when criterion.

---

## Worked example (toy — NOT the assignment)

This toy shows the three-command sequence (Pause → Acknowledge → Resume) and verifies the shuttle state transitions. **It is not the assignment** — the assignment runs the real scenario inside `src/`.

Create it in a scratch directory:

```bash
mkdir -p /tmp/opencode/l32toy
cd /tmp/opencode/l32toy
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
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString()
)

// Simulate the pause state, mirroring FleetCore.PauseState.
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
        var pauseState = new PauseState();
        const string shuttleId = "SH-01";

        // Step 1: Operator Pause.
        pauseState.Pause(shuttleId);
        Console.WriteLine($"After Pause: IsPaused = {pauseState.IsPaused(shuttleId)}");

        // Step 2: Operator Acknowledge Recovery.
        // Simulate: alarm acknowledged in SQL + RecoveryCommandMessage published.
        var payload = JsonSerializer.Serialize(
            new RecoveryCommandMessage(
                new MessageEnvelope(4, "recoveryCommand", Guid.NewGuid().ToString(), null, "fleet-manager", DateTime.UtcNow.ToString("o")),
                shuttleId));
        Console.WriteLine($"Recovery command payload: {payload}");
        Console.WriteLine("After Acknowledge + Recovery: FaultReason cleared, state = Idle");

        // Step 3: Operator Resume.
        pauseState.Resume(shuttleId);
        Console.WriteLine($"After Resume: IsPaused = {pauseState.IsPaused(shuttleId)}");

        Console.WriteLine("done");
    }
}

Run it:

```bash
cd /tmp/opencode/l32toy
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

1. Why does the operator must follow the sequence Pause → Acknowledge → Resume, and what goes wrong if they click Resume before Acknowledge?
2. Walk through the full three-step sequence: Pause → Acknowledge → Resume. What does each step do to the shuttle's state and the movement gate?
3. The `Shuttle.Recover()` method can only be called when the shuttle is in `Fault` state. How does the handler ensure it only sends the recovery command when appropriate?
4. Is the whole `Pause → Acknowledge → Resume` sequence protected by the `lock` in `PauseState`? What can go wrong if two operator clicks happen at the same time?
5. What would change if the operator clicked Acknowledge when no alarm was showing (i.e., the shuttle had no active fault)? Would the recovery command hurt, help, or have no effect?

---

## The assignment

Take the worked example and make it run **for real**, inside `src/FleetManager/Program.cs` and the simulation end-to-end.

### The files you will change or add

| File | New? | What changes |
|---|---|---|
| (no edits required to prior code) | — | The `HandleOperatorCommandAsync` Acknowledge case already publishes `RecoveryCommandMessage` from lesson 31. The `PauseState` wiring and `RunLoopGate` delegation from lesson 30 are already in place. |
| `src/ShuttleCore/Shuttle.cs` | read-only | `Shuttle.Recover(DateTime.Now)` already clears `_faultReason` and sets `_state = ShuttleState.Idle` — no change needed. |
| `src/FleetCore/PauseState.cs` | read-only | Thread-safe paused shuttle-id set from lesson 30 — already exists. |
| `src/FleetManager/RunLoopGate.cs` | read-only | Delegates `Pause`/`Resume`/`IsPaused`/`PauseAll`/`ResumeAll` to `PauseState` from lesson 30 — already exists. |
| `src/FleetManager/Program.cs` | read-only (already edited in lesson 31) | The `Acknowledge` case already extends to publish `RecoveryCommandMessage` — no further edit needed. |

### Requirements

**R1 — the full workflow.** The student can trace the complete dashboard-driven fault recovery workflow: operator observes a fault alarm on the dashboard → operator pauses the shuttle via `OperatorCommandMessage.Pause` → operator acknowledges via `OperatorCommandMessage.Acknowledge` (which calls `alarmRepository.AcknowledgeAsync` and publishes `RecoveryCommandMessage` on `topics.RecoveryCommand(cmd.ShuttleId)` → shuttle `Recover()` clears `FaultReason` and sets state to `Idle`) → operator resumes the shuttle via `OperatorCommandMessage.Resume` → shuttle is now in `Idle` state with no `FaultReason`, the movement gate is unblocked, and the run loop grants it a route.

**R2 — command mapping.** The student can map each dashboard command kind to its code path and state effect:
- `Pause` → `pauseState.Pause` / `PauseAll` + `taskBoard.PauseAllExcept`: movement gate blocks shuttle, dispatch stops
- `Acknowledge` → `alarmRepository.AcknowledgeAsync` + `RecoveryCommandMessage` publish → shuttle `Recover()`: alarm acknowledged in SQL, `FaultReason` cleared, state → `Idle`
- `Resume` → `pauseState.Resume` / `ResumeAll` + `taskBoard.ResumeAllExcept`: movement gate unblocks shuttle, dispatch resumes

**R3 — end-to-end scenario.** The student can run the full M7 end-to-end fault recovery scenario from the dashboard:
1. Start serve with a blocked lane configured (fault injects when shuttle starts move into blocked lane)
2. Observe the alarm on the dashboard (FaultReason: "blocked path")
3. Click Pause → shuttle movement gate blocks, dispatch stops
4. Click Acknowledge → alarm acknowledged in SQL, shuttle FaultReason cleared, state → Idle
5. Click Resume → movement gate unblocks, dispatch resumes, shuttle moves again
6. Dashboard reflects: alarm gone, shuttle moving, task queue active

**R4 — no contract or config change.** `src/Contracts/`, `docs/contract/`, `ContractVersion.Current` (stays `4`), `TaskBoard.cs`, `WmsOrderBoard`, `appsettings.json` are untouched. No new configuration key.

**R5 — the facts.** At least these concepts are verified:
- The `Acknowledge` case in `HandleOperatorCommandAsync` publishes `RecoveryCommandMessage` (lesson 31)
- `PauseState` is shared between the gate and the handler (lesson 30)
- `Shuttle.Recover()` clears `FaultReason` and sets state to `Idle` (M5)
- The three-command sequence Pause → Acknowledge → Resume takes the shuttle from fault+idle back to running (lessons 30+31)

### Acceptance criteria

```bash
BASE=$(git rev-parse HEAD)
cd src && dotnet build --no-incremental
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# existing gate/tests still pass:
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~RunLoopGate"
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~PauseState"
# verify the Acknowledge publish is present:
grep -q "PublishAsync.*RecoveryCommandMessage" src/FleetManager/Program.cs
# verify no contract changes:
test -z "$(git diff $BASE -- src/Contracts docs/contract)"
```

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Remove the `PublishAsync` call from the `Acknowledge` case | The recovery command would not be sent, the shuttle would not recover, and the end-to-end scenario would fail |
| b | Remove the `alarmRepository.AcknowledgeAsync` call from the `Acknowledge` case | The alarm would not be acknowledged in SQL, and the integration test would fail |
| c | Change `topics.RecoveryCommand(cmd.ShuttleId)` to a different topic | The shuttle would not recover on the correct topic, and the scenario would fail |

### Stretch goal

**Measure the dashboard recovery latency.** Start serve, inject a blocked-path fault into a shuttle (e.g., via the simulator or a test that sets `FaultReason`), observe the alarm timestamp, publish `OperatorCommandMessage.Acknowledge`, time from that publish to the shuttle's next status update showing `FaultReason` is null and state is `Idle`. Write the measurement down. Compare it to the pause-only latency from lesson 30.

### What comes next

- **Cross-process pause.** Make the sim (or a scenario runner) subscribe to `fleet/operator/command` and call `_gate.PauseState.Pause(...)` when a Pause arrives, mirroring `serve`'s handler. Measure the time from the MQTT publish to the shuttle holding its position. Compare it to the in-process case.
- **M8 stretch.** OPC UA or Modbus TCP gateway, Python scenario runner, or a load test with measured limits. The end-to-end fault workflow is ready for the multi-threaded scenario runner.

---

## What I assumed about what you know

I assumed you are comfortable with: ASP.NET Core minimal APIs and local functions, MQTT message publishing (`bus.PublishAsync`, `MessageFamily.Json`, topic routing via `TopicScheme`), JSON serialization (`JsonSerializer.Serialize`, the contract v4 message types), the `Shuttle.Recover()` method from M5 (it clears `_faultReason` and sets `_state = ShuttleState.Idle`), and the existing `HandleOperatorCommandAsync` shape from lesson 31. If any of those is new, say so before building and I will walk through it.

The fleet ideas in this lesson — why the operator must follow the sequence Pause → Acknowledge → Resume, how the three commands compose into a complete recovery workflow, and how the end-to-end dashboard-driven scenario satisfies M7's done-when — are taught from first principles. If any of them is new, say so before you build, and we will walk through it before the assignment.