# Decision 43: Blocked-lane removal and dispatch-pause semantics

**Status:** accepted
**Date:** 2026-10-29
**Milestone:** M7 (operator dashboard and diagnostics)
**Supersedes:** narrows record 38 (traffic control under the dashboard) and clarifies record 35 (dashboard diagnostics) on `TaskBoard.PauseAllExcept`
**Builds on:** records 38, 39, 40, 41, 42

## Context

Two things are still unsettled at the end of lesson 33:

1. **Blocked lanes never clear.** A shuttle that hits a blocked lane raises `FaultReasons.BlockedPath`, and `Shuttle.Recover()` clears the fault state, but the lane stays in `Rack._blockedLanes`. The next task that routes through that lane faults again. The dashboard "Acknowledge" therefore does not produce a real recovery. Record 42 said this must be decided before claiming the M7 scenario.
2. **`PauseAllExcept(shuttleId)` means two different things.** The movement pause (`PauseState`) pauses the named shuttle. The dispatch pause (`TaskBoard`) currently pauses tasks assigned to *other* shuttles (the rev2 reading). That is the opposite of the movement pause and of lesson 26's sentence "stops dispatching new work to the named shuttle while letting others continue". Records 38 and 39 describe it as "pause all except the named shuttle", which is the other reading. The professor must pick one.

`ContractVersion.Current` stays `4`. No new message, topic, or configuration key.

## Survey 1: How does a blocked lane become unblocked?

| Option | What it means | Complexity | Failure mode | How real fleet systems do it |
|---|---|---|---|---|
| **A. Recovery clears the recorded blocked lane(s)** (chosen) | When `StartMove` faults on a blocked route, the shuttle records which lane(s) along that route are blocked. `Recover()` unmarks exactly those lanes in the `Rack` before clearing the fault. The existing `Acknowledge` command already drives `Recover()`, so no new command or topic is needed. | Low. One new `Rack` method and a small list in `Shuttle`. | The blockage is cleared automatically on recovery, so the sim cannot model "the lane is still blocked after recovery". That is acceptable for the sim's injected faults; a real system would keep them separate. | Real systems keep physical removal (maintenance) separate from software recovery. The sim couples them deliberately so the dashboard scenario can finish. |
| B. External config change + restart | The operator edits `Shuttle:BlockedLanes` and restarts `ShuttleSim`. Recovery only clears the shuttle fault. | Lowest in code. | Not runnable "from the dashboard alone" (M7 done-when). A retried task re-faults until the restart happens. | Common in test rigs, not in production. |
| C. A new `ClearBlockage` operator command | A new command kind (and possibly a new topic) tells `ShuttleSim` to unmark a lane. The dashboard gets a separate button. | High. Contract change, new topic or field, new handler in `ShuttleEndpoint`, new UI control. | More surface to get wrong; needs a contract version bump. | Seen in real systems, where maintenance confirms removal. |

**Decision A.** The sim's blocked lane is a software-injected fault, not a real pallet. Clearing it on recovery keeps the M7 scenario runnable from the dashboard and needs no contract change. The shuttle records exactly which lane(s) blocked its last attempted route and unmarks only those, so an unrelated blocked lane is not cleared. `Rack.UnmarkBlocked(laneId)` mirrors `MarkBlocked`. If a future requirement needs the blockage to survive recovery (for example, to model a lane that stays blocked until maintenance confirms removal), option C replaces A and bumps the contract version. `Shuttle.Recover()` logs which lane it cleared so the coupling is visible, not silent.

## Survey 2: What does `PauseAllExcept(shuttleId)` pause?

| Option | What it means | Complexity | Failure mode | How real fleet systems do it |
|---|---|---|---|---|
| **A. Pause the named shuttle's dispatch** (chosen) | `PauseAllExcept(id)` pauses tasks whose `AssignedShuttleId == id`. Unassigned queued tasks stay dispatchable to other shuttles. A blank id pauses every queued task. This matches the movement pause (`PauseState.Pause(id)` pauses shuttle `id`) and lesson 26. | Low. One condition change in `TaskBoard`. | A task that is unassigned when the pause starts can later be assigned to the paused shuttle, because the pause is tracked by task id. For the sim this is acceptable; a future per-shuttle pause flag would close it. | Common: pause one vehicle's work, others keep going. |
| B. Pause every shuttle except the named one (the rev2 reading) | Pauses tasks assigned to *other* shuttles. The named shuttle keeps getting work. | Low. | Movement pause stops the named shuttle while dispatch stops everyone else — the two pauses disagree, which is the trust problem review 33 raised. | Rare; would be labelled "pause all except". |
| C. Global pause only | One flag pauses all dispatch. | Lowest. | Cannot pause one shuttle without pausing the fleet, so the dashboard's per-shuttle button would be a lie. | Used in small systems only. |

**Decision A.** The dashboard's Pause button is pressed on one shuttle, and the operator expects that shuttle to stop. The movement pause already does that. The dispatch pause must do the same, or the two halves of one click disagree. The method keeps its name (`PauseAllExcept` / `ResumeAllExcept`) so existing references and tests keep their shape, but its settled meaning is: *pause the named shuttle's dispatch*. The name is historical; the comment in `TaskBoard.cs` records the settled meaning so nobody re-reads the name and flips the condition again. `ResumeAllExcept(id)` clears the pause mark for tasks assigned to `id` (and for unassigned tasks that were paused globally), which makes the round-trip symmetric. Empty id pauses or resumes everything, as before.

## What would change the choice

- If the M7 scenario needs the blockage to survive recovery (maintenance confirmation), replace Survey 1 option A with C and bump the contract version.
- If a second process must observe pause, the in-memory `TaskBoard` set is not enough; pause would travel over MQTT or live in SQL. Tracked as a follow-up, not part of this decision.
- If pause must also stop a task from being assigned to a paused shuttle, add a paused-shuttle set to `TaskBoard` and consult it in `AssignNextAsync` (or pass `PauseState` in). That is a larger change and is not needed for the current scenario.

## Consequences

- `Rack` gains `UnmarkBlocked(string laneId)`. `Shuttle` gains a small list of lanes it recorded as blocked on its last faulted move, and `Recover()` unmarks them.
- `TaskBoard.PauseAllExcept` / `ResumeAllExcept` change condition from "other shuttles' tasks" to "the named shuttle's tasks". The four existing `TaskBoardPauseTests` must be updated to the new expectation; the implementer does this, and the lesson lists the new expectations.
- `ContractVersion.Current` stays `4`. No message, topic, or configuration change. `Program.cs` needs no edit for the pause change (same method names) and no edit for the blockage change (recovery already drives `Recover()`).
- The M7 scenario can now finish end to end: after Acknowledge the shuttle's fault clears *and* the blocked lane clears, so a retried task no longer re-faults.

## Out of scope

- Cross-process pause (tracked separately).
- Alarm history vs current state (consider, not settled here).
- A separate `ClearBlockage` command (rejected option C).
