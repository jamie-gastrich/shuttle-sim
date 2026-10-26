# Lesson 29: Traffic control under the dashboard

**Milestone:** M7 (operator dashboard and diagnostics) · **Language:** C#/.NET 10 · **Prerequisite:** [lesson 26](26-dashboard-diagnostics.md) (operator pause/resume/acknowledge) and [lesson 11](11-run-loop-with-occupancy.md) (the run-loop gate and traffic control)
**Concepts this assignment requires:**

- [ ] **Pause state on the gate** — a mutable set of paused shuttle ids lives in `RunLoopGate`, with `Pause`, `Resume`, `IsPaused`, and all-shuttle helpers → [§1](#1-pause-state-on-the-gate)
- [ ] **The gate combines pause and traffic** — `CanMove` returns `false` when the shuttle is paused, so one method answers every "may this shuttle move?" question → [§2](#2-the-gate-combines-pause-and-traffic)
- [ ] **In-progress moves freeze on pause** — the tick's move-completion loop skips a paused shuttle, so an in-progress move holds its position and its reservation until resumed → [§3](#3-in-progress-moves-freeze-on-pause)

---

## Where this fits

The spec's M7 says: *"a small web dashboard shows the rack map, shuttle states, task queue, and alarms. The operator can pause, resume, and acknowledge recovery."* Lesson 26 built the dashboard data endpoints, alarm persistence, and the operator command topics, including `Pause` and `Resume`. The operator can already ask the fleet manager to pause.

But that pause only stops *task dispatch*. `TaskBoard.NextQueued()` and `TaskBoard.Queued()` filter out tasks whose ids are in the paused set, so a paused task is never handed to a shuttle. Two things are still missing:

1. A shuttle that is *already moving* keeps moving. The pause does not freeze an in-progress move.
2. The traffic gate (`RunLoopGate.CanMove`) has no idea a pause exists. It checks occupancy and transit reservations only.

Without this slice, an operator who clicks "Pause" to look at a jammed lane watches the shuttles sail past. The pause feels broken. This lesson closes that gap in the sim run loop, which is where the traffic gate lives.

This slice does not need lesson 27 (order cancel). It builds on lesson 11's gate and lesson 26's pause idea only.

---

## Verification table

Every row was measured on **2026-10-26**, on this tree.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | `RunLoopGate` has no pause state today | `grep -n "Pause\|IsPaused" src/FleetManager/RunLoopGate.cs` | **no matches** (exit 1) |
| 2 | `RunLoop.TickAsync` completion loop has no pause check | read `src/FleetManager/RunLoop.cs` lines 95-125 | loop only checks `ShuttleState.Moving`; no pause call |
| 3 | `RunLoopGateTests` pass today | `cd src && dotnet test ... --filter "FullyQualifiedName~RunLoopGate"` | **4 passed, 0 failed** |
| 4 | `RunLoopGate.CanMove` checks occupancy + traffic only | read `src/FleetManager/RunLoopGate.cs` | `IsOccupiedByOthers` and `Traffic.IsRouteFree` only |
| 5 | `ContractVersion.Current` is unchanged by this slice | read `src/Contracts/ContractVersion.cs` | value is `4`; no edit planned |
| 6 | Build is clean before the change | `cd src && dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |

The broker and database were left down after these checks.

---

## Options survey

### Survey 1: where the pause state lives

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. Per-shuttle set inside `RunLoopGate`** (chosen) | The gate owns a `HashSet<string>` of paused shuttle ids, plus `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`. The gate already answers "may this shuttle move?"; pause is one more input. | Low. One new field and five small methods. | The gate becomes mutable; tests must reset it between cases. | Many fleet systems keep the movement permission in the same place that holds the occupancy and reservation state, so one object answers the question. |
| **B. A separate `OperatorPause` class** | A new pure class holds the paused set; the gate takes it as a dependency. | Medium. New file, new constructor parameter, new wiring in every test and in `Program.cs`. | Two objects to keep in sync; an interviewer asks why pause is not in the gate. | Used when pause is a cross-cutting concern shared by many subsystems. Overkill here. |
| **C. Reuse `TaskBoard._pausedTaskIds`** | The gate reads the board's paused task set and maps task id → shuttle id via `AssignedShuttleId`. | Medium. The gate needs the board and a mapping rule. | Task ids and shuttle ids are different keys. A task with no assigned shuttle, or a shuttle with no task, has no pause state. The "pause stops movement" rule becomes untestable in isolation. | Rare. Most systems keep movement permission keyed by the moving thing (the shuttle), not by the work item. |

**Decision A.** Full reasons: the gate is already the single answer to "may this shuttle move?". Putting pause there means one rule, one place, one test surface.

### Survey 2: how the tick uses the pause flag

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. Fold pause into `CanMove` and check `IsPaused` in the completion loop** (chosen) | `CanMove` returns `false` when the shuttle is paused, so dispatch is blocked. The move-completion loop skips a paused shuttle so an in-progress move holds until resumed. One rule, two call sites. | Low. Two small edits to `RunLoop.TickAsync`. | A paused shuttle's move is frozen mid-route; the reservation it holds stays held. That is the intended behaviour (pause, not cancel). | This is how a real pause works: the vehicle stops where it is and the lane stays reserved. |
| **B. Skip paused shuttles only in the dispatch loop** | Paused shuttles are not given new work, but in-progress moves still complete. | Low. | The operator's "pause" does not actually stop movement — the very thing this slice exists to fix. | Some simple systems do this and call it "pause dispatch", but operators call it broken. |
| **C. Cancel the in-progress move on pause** | Pause releases the reservation and requeues the task. | Medium. | Pause becomes cancel. An operator who clicks pause to look at a jammed lane loses the work in flight and risks duplicating a pallet. Wrong semantics. | Emergency stop does this, not pause. |

**Decision A.** Full reasons: freezing an in-progress move matches what an operator expects from "pause". Cancelling would discard work and could duplicate a pallet.

### Survey 3: global pause vs per-shuttle pause

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. Per-shuttle set, with `PauseAll`/`ResumeAll` helpers** (chosen) | The gate tracks individual shuttle ids; `PauseAll(IEnumerable<string>)` pauses every shuttle in one call. Matches the dashboard's "pause all except this one" shape. | Low. | Callers must pass the shuttle list for "all"; a forgotten shuttle stays running. | Dashboards usually let an operator pause one shuttle or all of them; per-shuttle state supports both. |
| **B. Single global bool** | One flag pauses everything. | Lowest. | Cannot pause one shuttle while another keeps working — the dashboard's per-shuttle commands would have to be faked. | Only seen in very small systems with one vehicle. |

**Decision A.** Full reasons: per-shuttle state matches the dashboard's command shape. A global bool would force the dashboard to fake per-shuttle control.

---

## Decision

The full decision is [record 38](../decisions/38-traffic-control-under-dashboard.md). Short version:

- **A.** Per-shuttle pause set inside `RunLoopGate`, with `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`.
- **A.** Fold pause into `CanMove` (returns `false` when paused) and check `IsPaused` in the move-completion loop so in-progress moves freeze.
- **A.** Per-shuttle state with all-shuttle helpers, not a single global bool.

Rejected: a separate `OperatorPause` class (option B in Survey 1) — pause belongs where the movement permission already lives. Rejected: skip-only-in-dispatch (option B in Survey 2) — that does not stop movement. Rejected: cancel-on-pause (option C in Survey 2) — pause is not cancel. Rejected: global bool (option B in Survey 3) — cannot express per-shuttle pause.

No new message contract version. `ContractVersion.Current` stays `4`. No new configuration key.

---

## The lecture

Today we are going to learn about **three concepts** that make an operator pause actually stop shuttle movement:

1. **Pause state on the gate** — a mutable set of paused shuttle ids lives in `RunLoopGate`
2. **The gate combines pause and traffic** — `CanMove` returns `false` when the shuttle is paused
3. **In-progress moves freeze on pause** — the tick's move-completion loop skips a paused shuttle

### 1 Pause state on the gate

**What it is.** The gate already decides whether a shuttle may move: it checks occupancy and transit reservations. This concept adds one more input to that decision — a set of paused shuttle ids. The gate gets five small methods: `Pause(id)`, `Resume(id)`, `IsPaused(id)`, `PauseAll(ids)`, `ResumeAll(ids)`. The set starts empty, so nothing changes unless someone calls `Pause`.

**Why we need it.** The dashboard's `Pause` command arrives as an MQTT message and is handled in `Program.cs` (lesson 26). That handler today calls `TaskBoard.PauseAllExcept`, which only affects task dispatch. To stop *movement*, the pause must reach the object that decides whether a shuttle moves — the gate. Keeping the set inside the gate means the pause and the traffic rule live in one place.

**A real-world example.** A traffic light has a "hold" button for maintenance. When pressed, the light stays red for that lane no matter what the schedule says. The pause state is stored in the same controller that runs the schedule, so the two rules cannot disagree.

**The code.** Add the field and methods to `RunLoopGate`:

```csharp
private readonly HashSet<string> _pausedShuttles = new(StringComparer.Ordinal);

public void Pause(string shuttleId) => _pausedShuttles.Add(shuttleId);
public void Resume(string shuttleId) => _pausedShuttles.Remove(shuttleId);
public bool IsPaused(string shuttleId) => _pausedShuttles.Contains(shuttleId);

public void PauseAll(IEnumerable<string> shuttleIds)
{
    foreach (var id in shuttleIds) _pausedShuttles.Add(id);
}

public void ResumeAll(IEnumerable<string> shuttleIds)
{
    foreach (var id in shuttleIds) _pausedShuttles.Remove(id);
}
```

Line by line:

- `private readonly HashSet<string> _pausedShuttles` — the set of paused shuttle ids. `readonly` means the field reference never changes; the set's contents do. Ordinal comparison means "SH-01" and "sh-01" are different ids.
- `Pause(id)` — adds the id. Adding an id that is already there does nothing (a set has no duplicates).
- `Resume(id)` — removes the id. Removing an id that is not there does nothing.
- `IsPaused(id)` — the query the tick and `CanMove` will call.
- `PauseAll` / `ResumeAll` — convenience for "pause everything". They loop over the given ids and add or remove each one.

**Why the set lives in the gate, not in a new class.** The gate already owns occupancy and reservations. Adding pause to the same object keeps the "may this shuttle move?" answer in one place. A separate class would force the gate to ask another object every time, and a reviewer would have to read two files to understand one rule.

### 2 The gate combines pause and traffic

**What it is.** `CanMove(shuttleId, destinationLane)` is the single method the tick calls before dispatching a move. Today it checks occupancy and transit reservations. This concept adds one line: if the shuttle is paused, return `false`. After this change, `CanMove` answers every reason a move is refused — paused, lane occupied, or route reserved — in one place.

**Why we need it.** The dispatch loop in `RunLoop.TickAsync` already calls `_gate.CanMove(shuttle.ShuttleId, task.ToLane)` before assigning a task. By folding pause into `CanMove`, the dispatch loop needs no new code: a paused shuttle is never given work. The rule is also testable in isolation, because `CanMove` is a pure method on the gate.

**A real-world example.** A forklift dispatcher checks three things before sending a forklift to aisle 7: is the forklift on hold? is the aisle occupied? is the route reserved? If any answer is no, the forklift waits. The dispatcher does not check these in three different places.

**The code.** Update `CanMove`:

```csharp
public bool CanMove(string shuttleId, string destinationLane)
{
    if (IsPaused(shuttleId))
    {
        return false;
    }

    var currentLane = Occupancy.GetOccupiedLane(shuttleId);
    if (currentLane is null)
    {
        return false;
    }

    var route = Track.Route(currentLane, destinationLane);
    if (Occupancy.IsOccupiedByOthers(route, shuttleId))
    {
        return false;
    }

    if (!Traffic.IsRouteFree(shuttleId, currentLane, destinationLane))
    {
        return false;
    }
    return true;
}
```

Line by line:

- `if (IsPaused(shuttleId)) return false;` — the new check. It comes first, so a paused shuttle is refused before any lane or route work is done. This is cheap and keeps the gate fast.
- The rest is unchanged from lesson 11: get the shuttle's current lane, compute the route, check occupancy, check traffic reservations. Each `return false` is one reason a move is refused.

**Why the pause check comes first.** A paused shuttle should not touch the occupancy map or the route table at all. Checking pause first keeps the gate's other work from running for a shuttle that will be refused anyway. It also makes a test that pauses a shuttle and calls `CanMove` fail for the right reason.

### 3 In-progress moves freeze on pause

**What it is.** The dispatch loop is now covered: a paused shuttle gets no new task. But a shuttle that is already moving still completes its move, because the move-completion loop at the bottom of `TickAsync` calls `shuttle.CompleteMove` for every moving shuttle. This concept adds a pause check there: if the shuttle is paused, skip it this tick. The move holds its position and its reservation until the shuttle is resumed.

**Why we need it.** Without this, "pause" only means "no new work". An operator who pauses to inspect a jammed lane still sees shuttles finishing their current moves. That is not a pause; that is a delayed stop. Freezing the in-progress move makes pause immediate and predictable.

**A real-world example.** A warehouse conveyor has an emergency stop button. Pressing it stops the belt where it is, with pallets still on it. The pallets do not fall off and the belt does not finish its current cycle. When the button is released, the belt resumes from where it stopped.

**The code.** In `RunLoop.TickAsync`, the move-completion loop:

```csharp
foreach (var shuttle in _shuttles)
{
    if (shuttle.State != ShuttleState.Moving)
    {
        continue;
    }

    if (_gate.IsPaused(shuttle.ShuttleId))
    {
        continue;   // frozen: hold position and reservation this tick
    }

    try
    {
        shuttle.CompleteMove(_currentTime);
        _occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
        _traffic.Release(shuttle.ShuttleId);

        if (_activeTasksByShuttle.TryGetValue(shuttle.ShuttleId, out var task))
        {
            await _dispatcher.CompleteAsync(task.TaskId, _currentTime, cancellationToken);
            _activeTasksByShuttle.Remove(shuttle.ShuttleId);
        }
    }
    catch (ShuttleRejectedException)
    {
        // The move is not complete for this time step yet.
    }
}
```

Line by line:

- `if (shuttle.State != ShuttleState.Moving) continue;` — unchanged. Only moving shuttles are in this loop.
- `if (_gate.IsPaused(shuttle.ShuttleId)) continue;` — the new check. A paused, moving shuttle is skipped this tick. Its position, its occupancy, and its transit reservation all stay as they are.
- `shuttle.CompleteMove(_currentTime)` — unchanged. When the shuttle is not paused, the move advances as before.
- `_occupancy.SetOccupiedLane(...)` and `_traffic.Release(...)` — unchanged. They run only when the move actually completes.
- The `catch (ShuttleRejectedException)` — unchanged. A move that is not finished yet (timing) is retried next tick.

**What happens to the reservation.** Because the paused shuttle is skipped, `_traffic.Release` is not called, so the segments it reserved stay reserved. No second shuttle can be granted a route through those segments. This is the safe behaviour: a paused shuttle is treated as still occupying its route. When it is resumed and completes, the reservation is released normally.

**What happens to `IsDone()`.** `IsDone()` is unchanged. A paused, in-progress shuttle keeps `_activeTasksByShuttle` non-empty and `ShuttleState.Moving` true, so `IsDone()` stays `false`. A paused run loop does not falsely report completion. A scenario that pauses forever must be cancelled by the caller (via the `CancellationToken`).

---

## Recap in plain words

- **Pause state on the gate** — `RunLoopGate` now holds a set of paused shuttle ids and exposes `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`. The set starts empty.
- **The gate combines pause and traffic** — `CanMove` returns `false` when the shuttle is paused, before checking occupancy or reservations. The dispatch loop needs no new code.
- **In-progress moves freeze on pause** — the move-completion loop skips paused shuttles, so an in-progress move holds its position and its reservation until resumed. No task is lost and no reservation is dropped.

---

## Worked example (toy — NOT the assignment)

This toy shows the pause-gate idea on a tiny fleet, with no database, no MQTT, and no traffic table. **It is not the assignment** — the assignment wires the real `RunLoopGate` and `RunLoop` in `src/FleetManager`.

Create it in a scratch directory:

```bash
mkdir -p /tmp/opencode/l29toy
cd /tmp/opencode/l29toy
dotnet new console --force -o .
```

Put this in `Program.cs`:

```csharp
using System;

class PauseGate
{
    private readonly HashSet<string> _paused = new(StringComparer.Ordinal);
    public void Pause(string id) => _paused.Add(id);
    public void Resume(string id) => _paused.Remove(id);
    public bool IsPaused(string id) => _paused.Contains(id);
    // Combined rule: a shuttle may move only if it is not paused.
    public bool CanMove(string id) => !IsPaused(id);
}

class Program
{
    static void Main()
    {
        var gate = new PauseGate();
        string[] shuttles = { "SH-01", "SH-02" };

        Console.WriteLine("before pause:");
        foreach (var s in shuttles)
            Console.WriteLine($"  {s}: CanMove = {gate.CanMove(s)}");

        gate.Pause("SH-01");
        Console.WriteLine("after pausing SH-01:");
        foreach (var s in shuttles)
            Console.WriteLine($"  {s}: CanMove = {gate.CanMove(s)}, IsPaused = {gate.IsPaused(s)}");

        Console.WriteLine("tick dispatch:");
        foreach (var s in shuttles)
        {
            if (gate.CanMove(s))
                Console.WriteLine($"  {s} moves");
            else
                Console.WriteLine($"  {s} holds (paused)");
        }

        gate.Resume("SH-01");
        Console.WriteLine("after resuming SH-01:");
        foreach (var s in shuttles)
            Console.WriteLine($"  {s}: CanMove = {gate.CanMove(s)}");
    }
}
```

Run it:

```bash
cd /tmp/opencode/l29toy
dotnet run
```

Expected output (measured 2026-10-26):

```
before pause:
  SH-01: CanMove = True
  SH-02: CanMove = True
after pausing SH-01:
  SH-01: CanMove = False, IsPaused = True
  SH-02: CanMove = True, IsPaused = False
tick dispatch:
  SH-01 holds (paused)
  SH-02 moves
after resuming SH-01:
  SH-01: CanMove = True
  SH-02: CanMove = True
```

Read it line by line:

- Before any pause, both shuttles may move.
- After `gate.Pause("SH-01")`, `CanMove` returns `false` for SH-01 and `true` for SH-02. One rule, two shuttles, different answers.
- The toy tick dispatches only the shuttles for which `CanMove` is true: SH-02 moves, SH-01 holds.
- After `gate.Resume("SH-01")`, both shuttles may move again. The pause is reversible and leaves no leftover state.

**Note:** The toy uses a hand-written `PauseGate` that only models the pause rule. It does not model occupancy, transit reservations, timing, or the move-completion loop. Those are already built (lessons 10 and 11) and are not re-taught here. The toy's job is to show the pause mechanism in isolation.

---

## Say it out loud

Answer these out loud, with no notes.

1. The operator pauses shuttle SH-03. Name the two places in `RunLoop.TickAsync` where that pause is now felt, and what each one does.
2. `CanMove` is called for a paused shuttle. Walk through the method top to bottom. Which check fires first, and why does the pause check come before the occupancy check?
3. A shuttle is mid-move when it is paused. What happens to its position, its occupancy, and its transit reservation on the next tick? What happens when it is resumed?
4. Why does this slice add `IsPaused` to the move-completion loop instead of calling `CanMove` there?
5. `IsDone()` is unchanged. A run loop is paused with one shuttle mid-move. Does `IsDone()` ever become true? What must the caller do?

---

## The assignment

Take the worked example and make it run **for real**, inside `src/FleetManager`, on the real `RunLoopGate` and `RunLoop`.

### The files you will change or add

| File | New? | What changes |
|---|---|---|
| `src/FleetManager/RunLoopGate.cs` | edit | Add the `_pausedShuttles` set and `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`. Update `CanMove` to return `false` when paused. |
| `src/FleetManager/RunLoop.cs` | edit | In the move-completion loop, skip a shuttle when `_gate.IsPaused(shuttle.ShuttleId)` is true. |
| `src/FleetManager.Tests/RunLoopGateTests.cs` (or new file) | edit/new | Unit tests: `CanMove_returns_false_when_paused`, `CanMove_returns_true_after_resume`, `PauseAll_pauses_every_listed_shuttle`. |
| `src/FleetManager.Tests/RunLoopStressTests.cs` (or new file) | edit/new | A RunLoop test: pause a moving shuttle, run ticks, assert it does not complete the move and `IsDone()` stays false; resume, run ticks, assert it completes. |

### Requirements

**R1 — Pause state on the gate.** `RunLoopGate` gains a `HashSet<string> _pausedShuttles` (ordinal). Methods `Pause(string shuttleId)`, `Resume(string shuttleId)`, `IsPaused(string shuttleId)`, `PauseAll(IEnumerable<string> shuttleIds)`, `ResumeAll(IEnumerable<string> shuttleIds)`. `Pause` is idempotent; `Resume` on an unpaused id does nothing.

**R2 — `CanMove` includes pause.** `CanMove` returns `false` when `IsPaused(shuttleId)` is true, checked before occupancy and traffic. With no pauses, behaviour is identical to today.

**R3 — In-progress moves freeze.** In `RunLoop.TickAsync`, the move-completion loop skips a shuttle when `_gate.IsPaused(shuttle.ShuttleId)` is true. The shuttle keeps its position, occupancy, and transit reservation.

**R4 — No contract or config change.** `src/Contracts/`, `docs/contract/`, and `appsettings.json` are untouched. `ContractVersion.Current` stays `4`. No new configuration key.

**R5 — The facts.** At least these new tests pass:
- `CanMove_returns_false_when_paused` (unit): construct a gate, pause "SH-01", assert `CanMove("SH-01", any lane)` is false.
- `CanMove_returns_true_after_resume` (unit): pause then resume "SH-01", assert `CanMove` is true again (with a clear route).
- `PauseAll_pauses_every_listed_shuttle` (unit): call `PauseAll` with three ids, assert `IsPaused` is true for all three.
- `Paused_shuttle_does_not_complete_its_move` (RunLoop test): start a move, pause the shuttle mid-move, run several ticks, assert `ShuttleState` stays `Moving` and the task stays `InProgress`; resume, run ticks, assert the task becomes `Done`.

**R6 — what you must not touch.** `src/FleetCore/TaskBoard.cs` (pause semantics stay as lesson 26 defined them), `src/FleetCore/FleetTask.cs`, `src/Contracts/`, `docs/contract/`, `src/FleetManager/TaskExecutionLoop.cs` (serve-side wiring is a follow-up slice). The gate's new pause state is independent of `TaskBoard`'s paused task set.

### Acceptance criteria

```bash
BASE=$(git rev-parse HEAD)
cd src && dotnet build --no-incremental
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# existing gate tests still pass:
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~RunLoopGate"
grep -q "IsPaused" src/FleetManager/RunLoopGate.cs
grep -q "void Pause" src/FleetManager/RunLoopGate.cs
grep -q "IsPaused(shuttle.ShuttleId)" src/FleetManager/RunLoop.cs
test -z "$(git diff $BASE -- src/Contracts docs/contract)"
```

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Remove the pause check from `CanMove` | `CanMove_returns_false_when_paused` |
| b | Remove the pause check from the move-completion loop | `Paused_shuttle_does_not_complete_its_move` |
| c | Make `Pause` a no-op (do not add to the set) | `CanMove_returns_false_when_paused` and `PauseAll_pauses_every_listed_shuttle` |
| d | Check pause after occupancy in `CanMove` (swap order) | a test that pauses a shuttle with no current lane still sees `false` — the pause must win regardless of occupancy |

### Stretch goal

**Wire the gate's pause to the dashboard.** In `Program.cs`, when an `OperatorCommandMessage` with `Command == Pause` arrives on `fleet/operator/command`, call `_runLoop.Gate.PauseAll(_shuttles.Select(s => s.ShuttleId))` (or the per-shuttle variant that keeps the named shuttle running). Measure the time from the MQTT publish to the shuttle holding its position, and write it down. Compare it to the pause-to-task-dispatch delay from lesson 26. This is real M7 work, not a toy, and it closes the gap between the dashboard button and the sim movement.

### What comes next

- **The mock's submit and cancel client.** The stretch goal from lesson 28 (`POST /mock/orders`), or the next slice.
- **The cancel slice (lesson 27).** Still unbuilt; it is independent of this slice and can be built in parallel.
- **M8 stretch.** OPC UA or Modbus TCP gateway, Python scenario runner, or a load test with measured limits.

---

## What I assumed about what you know

I assumed you are comfortable with: `HashSet<T>` and its `Add`/`Remove`/`Contains` methods, `IEnumerable<T>` and `foreach`, `enum` values and `switch` expressions (not used here, but nearby), and the existing `RunLoop`/`RunLoopGate` shape from lesson 11. If any of those is new, say so before building and I will walk through it.

The fleet ideas in this lesson — why pause must freeze in-progress movement and not just dispatch, and why the reservation must stay held while paused — are taught from first principles. If either is new, say so before building, and we will walk through it before the assignment.