# Decision 38: Traffic control under the dashboard

**Status:** accepted
**Date:** 2026-10-26
**Milestone:** M7 (operator dashboard and diagnostics)
**Supersedes:** none (extends decision 17 — occupancy and fault reservations)

## Context

M7's done-when requires that a full scenario, including an injected fault, can be run and recovered from the dashboard alone. Lesson 26 (record 35) added the dashboard data endpoints, alarm persistence, and the operator command topics (`fleet/operator/command` with `Pause`, `Resume`, `Acknowledge`). The operator can already ask the fleet manager to pause.

The gap: the pause only stops *task dispatch*. `TaskBoard.NextQueued()` and `TaskBoard.Queued()` filter out tasks whose ids are in `_pausedTaskIds`, so a paused task is never handed to a shuttle. But two things are not covered:

1. A shuttle that is *already moving* keeps moving. The pause does not freeze an in-progress move.
2. The traffic gate (`RunLoopGate.CanMove`) has no idea a pause exists. It checks occupancy and transit reservations only. So a paused shuttle's lane can still be routed through, and the tick can still dispatch work that the gate would otherwise allow.

The spec's M4 also requires that the run loop consult the traffic gate before a move. Today the dispatch loop does consult it (`_gate.CanMove`), but the move-completion loop does not re-check anything — it completes whenever the shuttle reports done. There is no single place where "may this shuttle move right now?" is answered for both dispatch and completion.

## Options considered

### 1. Where does the pause state live?

| Option | What it means | Complexity | Failure mode |
|---|---|---|---|
| **A. Per-shuttle set inside `RunLoopGate`** (chosen) | The gate owns a `HashSet<string>` of paused shuttle ids, plus `Pause`/`Resume`/`IsPaused`/`PauseAll`/`ResumeAll`. The gate already answers "may this shuttle move?"; pause is just one more input to that answer. | Low. One new field and five small methods on a class that already exists. | The gate becomes mutable; tests must reset it between cases. |
| **B. A separate `OperatorPause` class** | A new pure class holds the paused set; the gate takes it as a dependency. | Medium. New file, new constructor parameter, new wiring in every test and in `Program.cs`. | Two objects to keep in sync; an interviewer asks why pause is not in the gate. |
| **C. Reuse `TaskBoard._pausedTaskIds`** | The gate reads the board's paused task set and maps task id → shuttle id via `AssignedShuttleId`. | Medium. The gate needs the board and a mapping rule; task ids and shuttle ids are different keys. | The mapping is implicit and fragile: a task with no assigned shuttle, or a shuttle with no task, has no pause state. The "pause stops movement" rule becomes untestable in isolation. |

### 2. How does the tick use the pause flag?

| Option | What it means | Complexity | Failure mode |
|---|---|---|---|
| **A. Fold pause into `CanMove` and check `IsPaused` in the completion loop** (chosen) | `CanMove` returns `false` when the shuttle is paused, so dispatch is blocked. The move-completion loop skips a paused shuttle so an in-progress move holds until resumed. One rule, two call sites. | Low. Two small edits to `RunLoop.TickAsync`. | A paused shuttle's move is frozen mid-route; the reservation it holds stays held. That is the intended behaviour (pause, not cancel). |
| **B. Skip paused shuttles only in the dispatch loop** | Paused shuttles are not given new work, but in-progress moves still complete. | Low. | The operator's "pause" does not actually stop movement — the very thing the slice exists to fix. |
| **C. Cancel the in-progress move on pause** | Pause releases the reservation and requeues the task. | Medium. | Pause becomes cancel. An operator who clicks pause to look at a jammed lane loses the work in flight and risks duplicating a pallet. Wrong semantics. |

### 3. Global pause vs per-shuttle pause

| Option | What it means | Complexity | Failure mode |
|---|---|---|---|
| **A. Per-shuttle set, with `PauseAll`/`ResumeAll` helpers** (chosen) | The gate tracks individual shuttle ids; `PauseAll(IEnumerable<string>)` pauses every shuttle in one call. Matches the dashboard's "pause all except this one" shape and keeps individual control. | Low. | Callers must pass the shuttle list for "all"; a forgotten shuttle stays running. |
| **B. Single global bool** | One flag pauses everything. | Lowest. | Cannot pause one shuttle while another keeps working — the dashboard's per-shuttle commands would have to be faked. |

## Decision

- **A (per-shuttle set in the gate)**, **A (fold into `CanMove` + check in completion loop)**, and **A (per-shuttle with all-helpers)**.

Concretely:

1. `RunLoopGate` gains a `HashSet<string> _pausedShuttles` (ordinal comparison), and methods:
   - `void Pause(string shuttleId)` — adds the id.
   - `void Resume(string shuttleId)` — removes the id.
   - `bool IsPaused(string shuttleId)`.
   - `void PauseAll(IEnumerable<string> shuttleIds)` — adds every id.
   - `void ResumeAll(IEnumerable<string> shuttleIds)` — removes every id.
2. `CanMove` returns `false` when `IsPaused(shuttleId)` is true, in addition to the existing occupancy and traffic checks.
3. `RunLoop.TickAsync` skips a paused shuttle in the move-completion loop, so an in-progress move holds its position and its reservation until resumed.
4. The dispatch loop needs no new code: it already calls `_gate.CanMove`, which now includes the pause rule.

## Reasons

- The gate is already the single answer to "may this shuttle move?". Putting pause there means one rule, one place, one test surface. A reviewer can read `CanMove` top to bottom and see every reason a move is refused.
- Freezing an in-progress move (rather than cancelling it) matches what an operator expects from "pause": the shuttle stops where it is, the lane stays reserved, and resuming continues the same move. Cancelling would discard work and could duplicate a pallet.
- Per-shuttle state matches the dashboard's command shape (`PauseAllExcept(shuttleId)` keeps one shuttle running). A global bool would force the dashboard to fake per-shuttle control.
- No change to the message contract, no new topic, no new configuration key. `ContractVersion.Current` stays `4`.

## What would change the choice

- If the operator pause were later required to cancel in-flight work (for example, an emergency stop that drops reservations), option 2C would replace 2A. That is a different command (`EmergencyStop`), not `Pause`, and would need its own decision record.
- If pause state had to survive a fleet-manager restart, the set would move from the in-memory gate into SQL (like alarms in record 35) and be restored on startup. For the simulator, in-memory is correct and matches the honest limit documented in record 28.
- If the real fleet manager (`TaskExecutionLoop`) also needed to consult pause, it would take the same gate or a shared pause set. That wiring is a follow-up slice; this record covers the sim run loop where the traffic gate lives.

## Consequences

- `RunLoopGate` becomes mutable. Tests that construct a gate and call `CanMove` must not assume a clean pause state; new tests should assert both the paused and unpaused outcomes.
- `IsDone()` is unchanged: a paused, in-progress shuttle keeps `IsDone()` false, so a paused run loop does not falsely report completion. A scenario that pauses forever must be cancelled by the caller.
- Existing `RunLoopGateTests` (4 facts) and `RunLoopStressTests`/`RunLoopReassignmentTests` keep passing: with no `Pause` calls, `_pausedShuttles` is empty and `CanMove` behaves exactly as before.
- The shuttle that is frozen mid-move keeps its transit reservation, so a second shuttle cannot be granted a route through its lane until it resumes and completes. This is the safe behaviour and matches M4's no-collision rule.

## Out of scope

- Wiring the gate's pause into the serve process (`TaskExecutionLoop`) or the dashboard's MQTT handler. The dashboard pause already stops task dispatch via `TaskBoard`; this slice adds the movement side in the sim run loop. Connecting the two is a later slice.
- Persisting pause state across restarts.
- Changing `TaskBoard.PauseAllExcept` semantics (record 35, issue 2). The gate's per-shuttle set is independent of the board's task-id set.
- Any change to `src/Contracts/` or `docs/contract/`. `ContractVersion.Current` stays `4`.
