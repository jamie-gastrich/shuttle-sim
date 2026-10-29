# Lesson 34: Blocked-lane removal and dispatch-pause semantics

**Milestone:** M7 (operator dashboard and diagnostics) · **Language:** C#/.NET · **Prerequisite:** [lesson 33](33-browser-dashboard.md)

**Concepts this assignment requires:**

- [ ] **Blocked-lane removal on recovery** — how a blocked lane becomes unblocked when the shuttle recovers → [§1](#1-blocked-lane-removal-on-recovery)
- [ ] **Dispatch-pause semantics** — what `PauseAllExcept(shuttleId)` now pauses → [§2](#2-dispatch-pause-semantics)

Two new concepts. That is under the cap.

**Not in this lesson:** changing the message contract (`ContractVersion.Current` stays `4`), adding a new operator command, cross-process pause, alarm history vs current state, or the mock WMS client.

---

## Where this fits

The M7 done-when says a full scenario, including an injected fault, must be run and recovered from the dashboard alone. Lesson 33 made the browser real. Lesson 31 made Acknowledge clear the shuttle's fault. But two gaps remain:

1. The blocked lane never clears. `Shuttle.Recover()` clears the fault state, but `Rack._blockedLanes` still holds the lane. The next task that routes through it faults again. So "recovery" is not real recovery.
2. `PauseAllExcept(shuttleId)` pauses the wrong work. It pauses tasks assigned to *other* shuttles, while the movement pause pauses the *named* shuttle. The two halves of one operator click disagree. An operator who pauses SH-01 sees SH-01 freeze, but dispatch pauses everyone else.

This lesson settles both. After it, an Acknowledge click clears the fault *and* the blocked lane, and a Pause click pauses one shuttle in both movement and dispatch.

---

## Verification table

Everything below was checked on **2026-10-29** in this repo. Scratch work went in `/tmp/opencode` and was cleaned up. Nothing was left running.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | `Rack` has `MarkBlocked` and `IsBlocked` but no removal method | read `src/ShuttleCore/Rack.cs` | `MarkBlocked(string laneId)` at line 79; `IsBlocked` at line 85; no `Unmark*` method |
| 2 | `Shuttle.StartMove` raises a fault on a blocked route but records nothing | read `src/ShuttleCore/Shuttle.cs` lines 34-42 | `if (_rack.IsRouteBlocked(...)) { RaiseFault(...); return; }` |
| 3 | `Shuttle.Recover` clears `_faultReason` and sets `_state = Idle` | read `src/ShuttleCore/Shuttle.cs` lines 144-152 | `_state = ShuttleState.Idle; _faultReason = null;` |
| 4 | `TaskBoard.PauseAllExcept` currently pauses tasks assigned to a *different* shuttle | read `src/FleetCore/TaskBoard.cs` lines 32-52 | `if (task.AssignedShuttleId is { } owner && owner != shuttleId)` |
| 5 | Movement pause pauses the named shuttle | read `src/FleetCore/PauseState.cs` | `Pause(string shuttleId)` adds the id to the set |
| 6 | `HandleOperatorCommandAsync` drives both pause sets with the same id | read `src/FleetManager/Program.cs` lines 326-341 | `taskBoard.PauseAllExcept(cmd.ShuttleId, ...)` then `pauseState.Pause(cmd.ShuttleId)` |
| 7 | Baseline non-integration tests | `cd src && dotnet test ShuttleFleet.slnx -v q --filter "Category!=Integration"` | 242 passed, 0 failed |
| 8 | Contract version | read `src/Contracts/MessageTypes.cs` | `ContractVersion.Current = 4` |
| 9 | Baseline build | `cd src && dotnet build ShuttleFleet.slnx -v q` | Build succeeded, 0 warnings, 0 errors |

The broker and database were left down after these checks.

---

## Options survey

The full design choice is [record 43](../decisions/43-blocked-lane-removal-and-pause-semantics.md). Short version:

**Survey 1 — blocked-lane removal.** Three options: (A) recovery clears the recorded blocked lane(s) (chosen); (B) external config change plus restart; (C) a new `ClearBlockage` command. A wins because it needs no contract change and lets the dashboard scenario finish.

**Survey 2 — dispatch pause.** Three options: (A) pause the named shuttle's dispatch (chosen); (B) pause every shuttle except the named one (the rev2 reading); (C) global pause only. A wins because it agrees with the movement pause and with lesson 26.

---

## Decision

The full decision is [record 43](../decisions/43-blocked-lane-removal-and-pause-semantics.md). Short version:

- `Rack` gains `UnmarkBlocked(string laneId)`.
- `Shuttle` records the lane(s) that blocked its last attempted move. `Recover()` unmarks those lanes, then clears the fault.
- `TaskBoard.PauseAllExcept(shuttleId)` now pauses tasks whose `AssignedShuttleId` equals `shuttleId`. A blank id pauses every queued task.
- `ContractVersion.Current` stays `4`. No message, topic, or configuration change.

---

## The lecture

Today we are going to learn about **blocked-lane removal on recovery** and **dispatch-pause semantics**.

### 1 Blocked-lane removal on recovery

**What it is.** When a shuttle tries to move and the route is blocked, the shuttle raises a fault. Today that fault is cleared by `Recover()`, but the lane stays blocked in the rack. This concept adds one step: the shuttle remembers which lane(s) blocked it, and `Recover()` unmarks those lanes. After recovery the route is open, so a retried task can succeed.

**Why we need it.** Without this, "recovery" is a lie. The shuttle returns to `Idle`, but the moment it tries the same task again it faults again. The dashboard scenario would loop forever on Acknowledge. M7's done-when needs a recovery that actually recovers.

**A real-world example.** A forklift meets a pallet left in an aisle. The driver stops, calls maintenance, and the pallet is moved. The driver then resumes. In the sim, the "pallet in the aisle" is the blocked lane, and the Acknowledge click is the driver resuming. The sim clears the blocked lane at that moment so the next trip through the aisle succeeds. A real system would keep physical removal separate from software recovery; the sim couples them deliberately so the scenario can finish, and logs which lane it cleared.

**The code.** Two small changes.

First, `Rack` gains a removal method that mirrors `MarkBlocked`:

```csharp
public void UnmarkBlocked(string laneId)
{
    if (!_laneOrdinals.ContainsKey(laneId))
        throw new ArgumentException($"unknown lane {laneId}", nameof(laneId));
    _blockedLanes.Remove(laneId);
}
```

Second, `Shuttle` records the blocked lane(s) when a move faults, and clears them on recovery. The list is populated only in the blocked-route branch of `StartMove`:

```csharp
// In StartMove, before RaiseFault:
var blockedOnRoute = new List<string>();
foreach (var lane in _rack.LaneId)
    if (_rack.IsBlocked(lane) && _rack.IsRouteBlocked(_position, lane) == false
        && OrdinalBetween(_rack, _position, toLane, lane))
        blockedOnRoute.Add(lane);
// simpler: iterate _rack.LaneId and keep lanes that are blocked and on the route
_blockedLanesOnFault.AddRange(blockedOnRoute);
```

And in `Recover`:

```csharp
foreach (var lane in _blockedLanesOnFault)
    _rack.UnmarkBlocked(lane);
_blockedLanesOnFault.Clear();
_state = ShuttleState.Idle;
_faultReason = null;
```

The exact way the shuttle finds the blocked lane(s) on the route is up to the implementer, as long as it records only lanes that are actually blocked and on the route between `_position` and `toLane`, and unmarks only those on recovery. A helper on `Rack` such as `BlockedLanesOnRoute(from, to)` is an acceptable alternative; if you add it, keep `UnmarkBlocked` as the single removal method.

**What this does not do.** It does not unblock lanes that were not on the faulted route. It does not change the contract. It does not add a new command. The dashboard's existing Acknowledge already drives `Recover()` through the recovery channel.

### 2 Dispatch-pause semantics

**What it is.** `PauseAllExcept(shuttleId)` is a dispatch pause: it decides which queued tasks the fleet manager may hand out. This concept settles its meaning: it pauses tasks whose `AssignedShuttleId` equals the named shuttle. A blank id pauses every queued task.

**Why we need it.** The movement pause (`PauseState.Pause(id)`) pauses shuttle `id`. The dispatch pause must pause the same shuttle, or one operator click pauses movement for one shuttle and dispatch for everyone else. That disagreement is the trust problem review 33 raised. Lesson 26 already said the dispatch pause "stops dispatching new work to the named shuttle while letting others continue." This concept makes the code match that sentence.

**A real-world example.** The operator clicks Pause on SH-01. SH-01 stops moving (movement pause) and stops receiving new tasks (dispatch pause). SH-02 and SH-03 keep moving and keep receiving tasks. The task list still shows unassigned queued tasks and other shuttles' tasks; only SH-01's in-flight task is hidden from dispatch.

**The code.** In `FleetCore/TaskBoard.cs`, change the condition in both methods:

```csharp
public Task PauseAllExcept(string shuttleId, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(shuttleId))
    {
        foreach (var task in _tasks)
            _pausedTaskIds.Add(task.TaskId);
        return Task.CompletedTask;
    }

    foreach (var task in _tasks)
    {
        if (task.AssignedShuttleId == shuttleId)
            _pausedTaskIds.Add(task.TaskId);
    }
    return Task.CompletedTask;
}

public Task ResumeAllExcept(string shuttleId, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(shuttleId))
    {
        _pausedTaskIds.Clear();
        return Task.CompletedTask;
    }

    foreach (var task in _tasks)
    {
        if (task.AssignedShuttleId == shuttleId || task.AssignedShuttleId is null)
            _pausedTaskIds.Remove(task.TaskId);
    }
    return Task.CompletedTask;
}
```

The name `PauseAllExcept` is historical and misleading; the comment at the top of the class records the settled meaning so the condition is not flipped back. `ResumeAllExcept` clears the mark for the named shuttle's tasks and for unassigned tasks that were paused globally, which keeps the round-trip symmetric.

**What this does not do.** It does not stop an unassigned task from later being assigned to a paused shuttle, because the pause is tracked by task id. For the sim this is acceptable: the movement pause still prevents the shuttle from moving, and cross-process pause is tracked separately. A future per-shuttle pause flag in `TaskBoard` would close that gap.

---

## Recap in plain words

- **Blocked-lane removal on recovery.** `Recover()` now unmarks the lane(s) that blocked the shuttle's last move. Acknowledge clears the fault and opens the route, so a retried task can succeed.
- **Dispatch-pause semantics.** `PauseAllExcept(shuttleId)` now pauses the named shuttle's dispatch, matching its movement pause. One click, one shuttle, both halves agree.

---

## Worked example (toy — NOT the assignment)

This toy shows a rack with a blocked lane, a shuttle that faults on it, and a recovery that clears the lane. **It is not the assignment** — the assignment edits `src/ShuttleCore/`.

Create it in a scratch directory:

```bash
mkdir -p /tmp/opencode/l34toy
cd /tmp/opencode/l34toy
dotnet new console --force -o .
```

Put this in `Program.cs`:

```csharp
using System;

class Program
{
    static void Main()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        rack.MarkBlocked("A01-L02");
        Console.WriteLine($"Before recovery: IsBlocked(A01-L02) = {rack.IsBlocked("A01-L02")}");

        // Simulate recovery clearing the recorded blocked lane.
        rack.UnmarkBlocked("A01-L02");
        Console.WriteLine($"After recovery:  IsBlocked(A01-L02) = {rack.IsBlocked("A01-L02")}");

        // Pause semantics sketch.
        var tasks = new[] { "t-owned-by-SH01", "t-owned-by-SH02", "t-unassigned" };
        var paused = new System.Collections.Generic.HashSet<string>();
        string pauseShuttle = "SH-01";
        // Pause tasks owned by the named shuttle only.
        if (pauseShuttle == "SH-01") paused.Add("t-owned-by-SH01");
        Console.WriteLine($"Paused tasks: {string.Join(", ", paused)}");
        Console.WriteLine("Unassigned and other-shuttle tasks remain dispatchable.");
        Console.WriteLine("done");
    }
}

public sealed class Rack
{
    private readonly System.Collections.Generic.HashSet<string> _blocked = new(System.StringComparer.Ordinal);
    private readonly System.Collections.Generic.Dictionary<string, int> _lanes = new()
    {
        ["A01-L01"] = 0, ["A01-L02"] = 1, ["A02-L01"] = 2, ["A02-L02"] = 3
    };
    public void MarkBlocked(string lane) => _blocked.Add(lane);
    public void UnmarkBlocked(string lane) => _blocked.Remove(lane);
    public bool IsBlocked(string lane) => _blocked.Contains(lane);
}
```

Run it:

```bash
cd /tmp/opencode/l34toy
dotnet run
```

Expected output (measured 2026-10-29):

```
Before recovery: IsBlocked(A01-L02) = True
After recovery:  IsBlocked(A01-L02) = False
Paused tasks: t-owned-by-SH01
Unassigned and other-shuttle tasks remain dispatchable.
done
```

Read it line by line:

- `Before recovery: ... True` — the lane is blocked, so a move through it would fault.
- `After recovery: ... False` — recovery unmarked the lane; the route is now open.
- `Paused tasks: t-owned-by-SH01` — only the named shuttle's dispatch pauses; the others stay dispatchable.

**What the toy does not show.** It does not show the MQTT bus, the fleet manager, the run loop, or the dashboard. Those are already built and are not re-taught here.

---

## Say it out loud

Answer these out loud, with no notes.

1. Why does `Recover()` need to unmark a lane, and not just clear `_faultReason`?
2. Which lane(s) does `Recover()` unmark, and why must it not unmark every blocked lane?
3. Walk through one operator click of Acknowledge in the M7 scenario. What changes in the shuttle, in the rack, and in the task queue?
4. Why must the dispatch pause pause the named shuttle and not the other shuttles?
5. What would go wrong if `PauseAllExcept` still paused tasks assigned to other shuttles while the movement pause paused the named shuttle?

---

## The assignment

Take the worked example and make it run **for real**, inside `src/ShuttleCore/` and `src/FleetCore/`.

### The files you will change or add

| File | New? | What changes |
|---|---|---|
| `src/ShuttleCore/Rack.cs` | edit | Add `UnmarkBlocked(string laneId)` that removes the lane from the blocked set and throws `ArgumentException` for an unknown lane, mirroring `MarkBlocked`. |
| `src/ShuttleCore/Shuttle.cs` | edit | In `StartMove`, when the route is blocked, record the blocked lane(s) on that route. In `Recover`, unmark those lanes in `_rack` before clearing `_faultReason` and setting state to `Idle`. Add a small field to hold the recorded lanes. |
| `src/FleetCore/TaskBoard.cs` | edit | Change `PauseAllExcept` so it pauses tasks whose `AssignedShuttleId` equals the argument (blank pauses all). Change `ResumeAllExcept` so it clears the mark for tasks owned by the argument and for unassigned tasks. Update the class comment to record the settled meaning. |
| `src/ShuttleCore.Tests/` | edit | Update or add tests for `UnmarkBlocked` and for recovery clearing the recorded lane. |
| `src/FleetCore.Tests/TaskBoardPauseTests.cs` | edit | Update the four tests to the new expectation (see R4). |

Do not edit `src/Contracts/`, `docs/contract/`, `src/FleetManager/Program.cs`, or `src/ShuttleSim/`. The recovery channel already drives `Recover()`; no handler change is needed. Do not add a configuration key.

### Requirements

**R1 — blocked-lane removal.** `Rack.UnmarkBlocked(laneId)` removes `laneId` from the blocked set. `Shuttle.Recover(now)` unmarks exactly the lane(s) that were recorded as blocked on the last faulted move, then sets `_state = ShuttleState.Idle` and `_faultReason = null`. A shuttle that faulted for a reason other than a blocked path records no lanes, so recovery unmarks nothing. The recorded list is cleared after recovery.

**R2 — recording.** In `StartMove`, when `_rack.IsRouteBlocked(_position, toLane)` is true, the shuttle records the blocked lane(s) on that route before calling `RaiseFault`. It must not record lanes outside the route, and must not record a lane that is not blocked.

**R3 — pause semantics.** `TaskBoard.PauseAllExcept(shuttleId, ct)`:
- blank id: every queued task's id is added to `_pausedTaskIds`;
- non-blank id: only tasks whose `AssignedShuttleId` equals `shuttleId` are added.

`TaskBoard.ResumeAllExcept(shuttleId, ct)`:
- blank id: `_pausedTaskIds` is cleared;
- non-blank id: the mark is removed for tasks whose `AssignedShuttleId` equals `shuttleId` or is null.

`NextQueued()` and `Queued()` keep filtering out paused task ids, unchanged.

**R4 — the facts.** At least these tests exist and pass:
- `UnmarkBlocked_removes_a_previously_blocked_lane` (Rack unit).
- `UnmarkBlocked_for_an_unknown_lane_is_rejected` (Rack unit).
- `Recover_clears_the_blocked_lane_that_caused_the_fault` (Shuttle unit): mark `A01-L02` blocked, `StartMove("A01-L02")`, `Recover`, assert `rack.IsBlocked("A01-L02")` is false.
- `Recover_does_not_clear_an_unrelated_blocked_lane` (Shuttle unit): block `A01-L02` and `A02-L01`, fault on a route through only `A01-L02`, recover, assert `A01-L02` is unblocked and `A02-L01` is still blocked.
- `Recover_for_a_non_blocked_fault_unmarks_nothing` (Shuttle unit): `RaiseFault("lost communication")`, `Recover`, assert no lane changed (use a rack with a blocked lane and confirm it stays blocked).
- `Pausing_one_shuttle_leaves_unassigned_queued_tasks_dispatchable` (updated): pausing `SH-01` with two unassigned tasks leaves both dispatchable.
- `Pausing_one_shuttle_pauses_only_its_own_assigned_work` (new): pausing `SH-01` with one task assigned to `SH-01` and one to `SH-02` pauses only the `SH-01` task; the `SH-02` task stays dispatchable.
- `Resuming_one_shuttle_restores_its_paused_work` (updated): after pausing `SH-01` and requeuing its task, resuming `SH-01` makes it dispatchable again.
- `A_blank_shuttle_id_pauses_and_resumes_every_queued_task` (unchanged).

**R5 — no contract or config change.** `src/Contracts/`, `docs/contract/`, `ContractVersion.Current` (stays `4`), `src/FleetManager/Program.cs`, and `src/ShuttleSim/` are untouched.

### Acceptance criteria

```bash
BASE=$(git rev-parse HEAD)
cd src && dotnet build ShuttleFleet.slnx
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~Rack"
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~TaskBoardPause"
grep -q "UnmarkBlocked" src/ShuttleCore/Rack.cs
grep -q "UnmarkBlocked" src/ShuttleCore/Shuttle.cs
test -z "$(git diff $BASE -- src/Contracts docs/contract src/FleetManager/Program.cs src/ShuttleSim)"
```

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Remove the `UnmarkBlocked` call from `Recover` | `Recover_clears_the_blocked_lane_that_caused_the_fault` |
| b | Unmark every blocked lane in `Recover` instead of only the recorded ones | `Recover_does_not_clear_an_unrelated_blocked_lane` |
| c | Flip the pause condition back to `owner != shuttleId` | `Pausing_one_shuttle_pauses_only_its_own_assigned_work` |
| d | Drop the `AssignedShuttleId is null` case from `ResumeAllExcept` | `Resuming_one_shuttle_restores_its_paused_work` |

### Stretch goal

**Measure the full recovery.** Run the M7 scenario with a blocked lane: fault, Pause, Acknowledge, Resume. Confirm the task is retried and completes, and time from Acknowledge to the shuttle's next status showing `Idle` with no fault. Compare to the pause-only latency from lesson 30.

### What comes next

- **Cross-process pause.** Make `ShuttleSim` observe the fleet manager's pause (over MQTT or a shared store) and prove a paused shuttle actually holds position. Still unproven.
- **Alarm history vs current state.** Decide whether alarms are current state or per-occurrence events. Tracked separately.
- **M8 stretch.** OPC UA or Modbus TCP gateway, Python scenario runner, or a load test.

---

## What I assumed about what you know

I assumed you are comfortable with: the `Rack` model (lanes, blocked set, route blocking), `Shuttle.StartMove` and `Shuttle.Recover`, `TaskBoard` and its paused-id set, and the operator command handler from lesson 31. If any of those is new, say so before building and I will walk through it.

The fleet ideas in this lesson — why a blocked lane must be cleared for recovery to be real, and why dispatch pause and movement pause must agree on which shuttle stops — are taught from first principles. If any of them is new, say so before you build, and we will walk through it before the assignment.
