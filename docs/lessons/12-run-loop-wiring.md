# Lesson 12: The run loop, concurrency, and the gate-then-dispatcher sequence

**Milestone:** M4 (Traffic control) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 11: Physical occupancy, the fleet-side occupancy view, and the run-loop gate](11-run-loop-with-occupancy.md) · **Decision:** [record 19](../decisions/19-run-loop-wiring-and-concurrency.md); builds on [record 17](../decisions/17-occupancy-and-fault-reservations.md), [record 16](../decisions/16-traffic-segments-and-deadlock-rule.md), and [record 15a](../decisions/15a-task-dispatcher-and-db-test-fixtures.md)

**Concepts this assignment requires:**

- [ ] **The run loop** — a loop that peeks at the next task, asks the gate, assigns, moves, and updates state. It is the piece that makes the gate and dispatcher work together. Taught in [The run loop](#1-the-run-loop). Fixed by [record 19 §2](../decisions/19-run-loop-wiring-and-concurrency.md).
- [ ] **Concurrency: protecting shared state** — the occupancy map and the reservation table are touched by the run loop and the status path at the same time. A `lock` makes multi-step operations atomic. Taught in [Concurrency](#2-concurrency-protecting-shared-state). Fixed by [record 19 §3](../decisions/19-run-loop-wiring-and-concurrency.md).
- [ ] **The gate-then-dispatcher sequence** — `CanMove` is a pure check (no side effects); `CommitMove` reserves the route. The run loop calls `CanMove`, then `AssignNextAsync`, then `CommitMove`. Taught in [The gate-then-dispatcher sequence](#3-the-gate-then-dispatcher-sequence). Fixed by [record 19 §4](../decisions/19-run-loop-wiring-and-concurrency.md).

Three new concepts. That is the cap.

Four things this assignment needs are **assignment requirements, not concepts**. They change where code lives and how the tests run, not the ideas above:

- a `RunLoop` class in `FleetManager` that drives `Shuttle` objects directly ([record 19 §2](../decisions/19-run-loop-wiring-and-concurrency.md));
- the gate split into `CanMove` (pure) and `CommitMove` (reserve), plus `IsRouteFree` on `TrafficController` ([record 19 §4](../decisions/19-run-loop-wiring-and-concurrency.md));
- a `lock` around `OccupancyMap` and `SegmentReservations` ([record 19 §3](../decisions/19-run-loop-wiring-and-concurrency.md));
- the 4+ shuttle stress test, which is the M4 done-when ([record 19 §7](../decisions/19-run-loop-wiring-and-concurrency.md)).

They are specified under [The assignment](#the-assignment).

**Not in this lesson:** heartbeats, `lastSeenAt`, timeout detection, alarms, reconciliation on restart, auto-release on fault (all M5), WMS orders (M6), the dashboard (M7), real shuttle movement over MQTT, and any wire-contract change.

---

## Where this fits

M4's done-when requires: *"a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock, and the rule used is written down and explained in a decision record."*

Lesson 10 built the pure traffic core: `Track`, `SegmentReservations`, and `TrafficController` with the all-or-nothing rule. Lesson 11 added the occupancy view and the run-loop gate. Both are proven by unit tests. But **nothing calls them.** There is no loop that drives a shuttle through the gate, assigns a task, and updates state. That is the gap this lesson closes.

```text
     run loop (this lesson)                     lessons 10-11 (built)
     ──────────────────────                     ──────────────────────
     peek at next queued task
          │
          ▼
     RunLoopGate.CanMove(…) ──────────────► OccupancyMap            "who is physically here?"
          │  pure check, no side effects     TrafficController        "who holds each segment?"
          │  allows?
          ├── yes ─► TaskDispatcher.AssignNextAsync(…)   (M3, unchanged)
          │          RunLoopGate.CommitMove(…)   (reserves the route)
          │          shuttle.StartMove(…)
          └── no  ─► skip this shuttle this tick
          │
          ▼ (when the move completes)
     occupancy.SetOccupiedLane(…)  ← update the fleet-side view
     TrafficController.Release(…)   ← free the transit reservation
```

**The honest weak spot.** The run loop drives `Shuttle` objects directly, in one process. In a real deployment, the fleet manager sends MQTT commands to shuttle processes. The simulation is kinder: no wireless delays, no dropped messages, no repeated commands. M5 will add the MQTT status path that makes the concurrency real. Say this plainly to a reviewer rather than overclaiming a live multi-process demo.

There is a second requirement from AGENTS.md: *"Decision logic is separated from input and output."* The run loop is **not** decision logic — it is the caller that ties decision logic to action. The gate (`CanMove`) stays pure. The dispatcher stays pure. The run loop is the only piece that touches both, and it does so in a testable way: the test drives the `RunLoop` class directly, with no broker and no database.

### No hard-coded values

AGENTS.md is flat: *"No hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration."* The run loop's tick interval and the stress test's shuttle count and round bound are **test fixtures**, not product configuration — they belong in the test, at known values, so the test is deterministic. The `RunLoop` class itself takes its tick interval as a constructor parameter, not a literal. There is no timeout or retry count in this slice; when M5 adds heartbeat timeouts, that duration comes from configuration, not a literal.

---

## Verification table

Everything below was checked by a command or a file read on **2026-10-13**. The baseline build and suite ran against the real `src/`; the toy ran at `/tmp/opencode/l12toy`. The toy directory is deleted afterwards. No file under `src/` was changed.

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no DB | `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; `25+4+18+8+2+5+6+15` = **83** tests |
| `OccupancyMap` is a plain `Dictionary` (not thread-safe) | read `src/FleetManager/OccupancyMap.cs` | `:5` `private readonly Dictionary<string, string> _shuttleToLane` |
| `SegmentReservations` is a plain `Dictionary` (not thread-safe) | read `src/TrafficCore/SegmentReservations.cs` | `:5` `private readonly Dictionary<string, string> _ownerBySegment` |
| `RunLoopGate.CanMove` reserves as a side effect | read `src/FleetManager/RunLoopGate.cs` | `:35` `Traffic.TryReserveRoute(shuttleId, currentLane, destinationLane)` |
| `TrafficController` has no pure check method | `grep -n 'public' src/TrafficCore/TrafficController.cs` | only `TryReserveRoute` and `Release` |
| The toy run loop drives 3 shuttles through the gate with no collisions and no deadlock | toy at `/tmp/opencode/l12toy`, `dotnet run` | 3 shuttles finish in 3 rounds; collisions 0; exit 0 |
| A head-on scenario deadlocks (contrast) | same toy, round-robin destinations | all shuttles blocked forever; 0 done in 20 rounds |
| Contract is still v2 | `grep -qE '^Version: 2$' docs/contract/v2.md` | match |

Not yet verified: the `RunLoop` class, the `IsRouteFree` method, the split gate, the `lock`-protected `OccupancyMap` and `SegmentReservations`, and the stress test — this assignment specifies them and the reviewer measures them.

---

## Options survey

The spec's learning method asks for up to three options per real choice, compared, before a decision record picks one. There are two real choices in this slice.

### Survey 1: How do we protect shared state? (→ [record 19 §3](../decisions/19-run-loop-wiring-and-concurrency.md))

| | A: `lock` around all access (chosen) | B: `ConcurrentDictionary` | C: leave as-is (plain `Dictionary`) |
|---|---|---|---|
| Multi-step operations are atomic | yes — the lock covers the whole sequence | **no** — `TryReserveAll` checks then writes; two threads can interleave | **no** |
| Simple to reason about | yes — one lock, one owner | harder — fine-grained but still racy for check-then-write | simplest, but unsafe |
| Matches the dotnet-style rule | yes — *"protect shared state; prefer a single owner"* | partly | **no** |

**Decision 1: a `lock` around all access to `OccupancyMap` and `SegmentReservations`.** The operations are multi-step: `IsOccupiedByOthers` reads many entries; `TryReserveAll` checks every segment and then writes every segment. `ConcurrentDictionary` protects each individual read or write, but it does not make a check-then-write sequence atomic. A single `lock` makes the whole sequence atomic. Rejected: `ConcurrentDictionary` (does not fix the check-then-write race); leaving as-is (unsafe).

### Survey 2: How do we sequence the gate and the dispatcher? (→ [record 19 §4](../decisions/19-run-loop-wiring-and-concurrency.md))

| | A: split into `CanMove` (pure) + `CommitMove` (reserve) (chosen) | B: keep `CanMove` as-is, release on failure | C: a reservation token |
|---|---|---|---|
| `CanMove` is pure (no side effects) | yes | **no** — it reserves | yes |
| No reservation leak | yes — nothing is reserved until `CommitMove` | **no** — a failure between `CanMove` and `AssignNextAsync` leaks | yes |
| Simple to explain and test | yes | harder — the release path is hidden | harder — more machinery |

**Decision 2: split the gate.** `CanMove` becomes a pure check — it reads occupancy and transit reservations but writes nothing. `CommitMove` reserves the route. The run loop calls `CanMove`, then `AssignNextAsync`, then `CommitMove`. If `CommitMove` fails, the task stays assigned but the shuttle does not move; the loop retries next tick. Rejected: keeping `CanMove` as-is with release-on-failure (the release path is hidden and easy to get wrong); a reservation token (more machinery than M4 needs).

---

## The lecture

Today we are going to learn about three things: **the run loop**, **concurrency**, and **the gate-then-dispatcher sequence**.

### 1) The run loop

A **run loop** is a loop that drives the system. It repeatedly checks each shuttle, asks the gate for permission, assigns a task, starts a move, and updates state when the move completes.

Real-world picture: a dispatcher at a warehouse who constantly watches which forklifts are free and assigns them work. The dispatcher does not drive the forklifts — they drive themselves. The dispatcher just decides who goes where and when.

Why do we need this now? Because the gate and the dispatcher are just pieces that nothing calls. The gate answers *"may this shuttle move?"* The dispatcher assigns a task. But nothing ties them together. The run loop is the piece that does.

The run loop's shape:

1. **Peek** at the next queued task (without assigning it).
2. **Ask the gate**: `CanMove(shuttleId, task.ToLane)`.
3. If yes, **assign** the task: `TaskDispatcher.AssignNextAsync`.
4. **Reserve** the route: `RunLoopGate.CommitMove`.
5. **Start the move**: `shuttle.StartMove(task.ToLane, now)`.
6. When the move completes, **update occupancy** and **release** the transit reservation.
7. Repeat.

The run loop is **not** decision logic. It is the caller that ties decision logic to action. The gate stays pure. The dispatcher stays pure. The run loop is the only piece that touches both.

Where this appears later: M5 will add the MQTT status path that calls `OccupancyMap.SetOccupiedLane`. The run loop reads the same map. Both are protected by the same `lock` (next section). M7's dashboard can read the run loop's state to show what is happening.

### 2) Concurrency: protecting shared state

**Concurrency** means two things happening at the same time. In a real fleet manager, the run loop and the status-reporting path run at the same time. The run loop reads the occupancy map. The status path writes it.

Real-world picture: two people writing on the same whiteboard at the same time. They overwrite each other's notes. One person writes "SH-01 is in lane 3." The other person writes "SH-02 is in lane 4." But they write at the same time, and the whiteboard ends up with a mix of both notes that makes no sense.

The occupancy map and the reservation table are plain `Dictionary` fields. A `Dictionary` is not thread-safe. If two threads write to it at the same time, it can corrupt the internal state or throw an exception.

The fix is a **lock**. A lock is a gate that only one thread can hold at a time. When a thread holds the lock, no other thread can enter. This makes multi-step operations atomic — the whole sequence runs without interruption.

```csharp
// src/FleetManager/OccupancyMap.cs — shape only; you write the body
public sealed class OccupancyMap
{
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _shuttleToLane = new(StringComparer.Ordinal);

    public string? GetOccupiedLane(string shuttleId)
    {
        lock (_lock)
        {
            // read the dictionary
        }
    }

    public void SetOccupiedLane(string shuttleId, string lane)
    {
        lock (_lock)
        {
            // write the dictionary
        }
    }

    public bool IsOccupiedByOthers(IEnumerable<string> route, string requesterId)
    {
        lock (_lock)
        {
            // read the dictionary
        }
    }
}
```

Line by line:

- `_lock` is a private object that all threads must acquire before touching the dictionary. It is never exposed.
- `lock (_lock)` blocks until the lock is available. Only one thread can hold it at a time.
- The whole method body runs inside the lock, so a multi-step operation (like `IsOccupiedByOthers`, which reads many entries) runs without interruption.

**Why a lock and not `ConcurrentDictionary`?** `ConcurrentDictionary` protects each individual read or write. But our operations are multi-step: `TryReserveAll` checks every segment and then writes every segment. Two threads can interleave between the check and the write, leaving a half-held route. A single `lock` makes the whole sequence atomic.

Where this appears later: M5's status-reporting path calls `SetOccupiedLane` from the MQTT handler. The run loop calls `GetOccupiedLane` and `IsOccupiedByOthers`. Both are protected by the same `lock`. The same treatment applies to `SegmentReservations` in `TrafficCore`.

### 3) The gate-then-dispatcher sequence

The **gate-then-dispatcher sequence** is the order in which the run loop calls the gate and the dispatcher. The current `CanMove` reserves the route as a side effect. That is a problem.

Real-world picture: you check if a meeting room is free, then walk to your car to get your laptop. By the time you get back, someone else has booked the room. If you had checked and booked as one step, you would have been safe. But you checked and booked as two steps, and someone else took the room in between.

The fix is to split the gate into two methods:

- **`CanMove`** — a pure check. It reads occupancy and transit reservations but writes nothing. It answers *"may this move proceed?"*
- **`CommitMove`** — reserves the route. It may fail if another shuttle grabbed the route first.

```csharp
// src/FleetManager/RunLoopGate.cs — shape only; you write the body
public sealed class RunLoopGate
{
    public bool CanMove(string shuttleId, string destinationLane)
    {
        var currentLane = _occupancy.GetOccupiedLane(shuttleId);
        if (currentLane is null)
            return false;

        var route = _track.Route(currentLane, destinationLane);
        if (_occupancy.IsOccupiedByOthers(route, shuttleId))
            return false;

        return _traffic.IsRouteFree(shuttleId, currentLane, destinationLane);
    }

    public bool CommitMove(string shuttleId, string destinationLane)
    {
        var currentLane = _occupancy.GetOccupiedLane(shuttleId);
        if (currentLane is null)
            return false;

        var decision = _traffic.TryReserveRoute(shuttleId, currentLane, destinationLane);
        return decision.Granted;
    }
}
```

Line by line:

- `CanMove` reads the shuttle's current position, gets the route, checks occupancy, and checks transit reservations. It writes **nothing**. It is pure.
- `CommitMove` reads the shuttle's current position and calls `TryReserveRoute`, which reserves the whole route atomically. It returns `true` if the reservation succeeded.
- `IsRouteFree` is a new method on `TrafficController`. It reads every segment's owner and returns `true` if every segment is free or owned by the requesting shuttle. It writes nothing.

**Why this matters.** The old `CanMove` reserved the route as a side effect. If the process stopped between `CanMove` returning `true` and `AssignNextAsync` running, the route would stay blocked forever. The split eliminates that risk: nothing is reserved until `CommitMove`, and `CommitMove` is the last step before the move starts.

**The run loop's sequence:**

1. `CanMove` — if `false`, skip this shuttle this tick.
2. `AssignNextAsync` — assign the task.
3. `CommitMove` — if `false`, the task stays assigned but the shuttle does not move; retry next tick.
4. `shuttle.StartMove` — start the move.

Where this appears later: M5's recovery path will call `CommitMove` when a shuttle is ready to move after a fault. The split means the recovery path can check `CanMove` without reserving, and only reserve when it is ready.

### Recap in plain words

- The **run loop** drives the system: peek, ask the gate, assign, reserve, move, update. It is the caller, not the decision logic.
- **Concurrency** means two things at once. A `lock` makes multi-step operations atomic. `ConcurrentDictionary` does not.
- The **gate-then-dispatcher sequence** is: `CanMove` (pure check) → `AssignNextAsync` (assign) → `CommitMove` (reserve). Nothing is reserved until the last step.

---

## Worked example (toy, not the assignment)

This toy shows the key idea: a run loop that drives shuttles through a gate. The gate blocks moves through occupied or reserved lanes. The run loop updates the occupancy map when shuttles move. No collisions and no deadlock. It uses the **real** `Track`, `SegmentReservations`, and `TrafficController` from `src/`. Run in `/tmp/opencode/l12toy`, delete after. It is **not** the assignment. The assignment uses the real `RunLoop`, `RunLoopGate`, and `OccupancyMap` in `FleetManager`, with a stress test. Do not copy this into `src/`.

Everything below ran in `/tmp/opencode/l12toy`, compiled against the real `src/ShuttleCore` and `src/TrafficCore` source files, and was deleted afterwards. It is one console project. `L12Toy.csproj` (complete):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="/home/jamie/shuttle-sim/src/ShuttleCore/*.cs" />
    <Compile Include="/home/jamie/shuttle-sim/src/TrafficCore/*.cs" />
  </ItemGroup>

</Project>
```

`Program.cs` (complete):

```csharp
// Worked-example toy for lesson 12: a run loop that drives shuttles through
// a traffic gate. This is a TOY, clearly not the assignment. It compiles the
// real ShuttleCore and TrafficCore sources; nothing under src/ is changed.

using ShuttleCore;
using TrafficCore;

// ── The track and traffic core (real, from src/) ──
var rack = new Rack(1, 6, 1);
var track = new Track(rack);
var reservations = new SegmentReservations();
var traffic = new TrafficController(track, reservations);

// ── Toy occupancy map: shuttleId -> last known occupied lane ──
var occupancy = new Dictionary<string, string>(StringComparer.Ordinal)
{
    ["SH-01"] = "A01-L01",
    ["SH-02"] = "A01-L04",
    ["SH-03"] = "A01-L02",
};

// ── Toy gate: pure check, then reserve ──
bool CanMove(string shuttleId, string destinationLane)
{
    var currentLane = occupancy[shuttleId];
    var route = track.Route(currentLane, destinationLane);

    // 1) Occupancy check: no segment occupied by a different shuttle.
    foreach (var segment in route)
    {
        foreach (var (otherShuttle, occupiedLane) in occupancy)
        {
            if (otherShuttle != shuttleId && occupiedLane == segment)
            {
                Console.WriteLine($"    gate refuses: {segment} occupied by {otherShuttle}");
                return false;
            }
        }
    }

    // 2) Transit reservation check (pure): is the route free?
    foreach (var segment in route)
    {
        var owner = reservations.OwnerOf(segment);
        if (owner is not null && owner != shuttleId)
        {
            Console.WriteLine($"    gate refuses: {segment} reserved by {owner}");
            return false;
        }
    }

    return true;
}

bool CommitMove(string shuttleId, string destinationLane)
{
    var currentLane = occupancy[shuttleId];
    var decision = traffic.TryReserveRoute(shuttleId, currentLane, destinationLane);
    if (!decision.Granted)
    {
        Console.WriteLine($"    commit failed: {decision.Reason}");
        return false;
    }
    return true;
}

// ── Toy shuttles and tasks ──
var shuttlePositions = new Dictionary<string, string>(occupancy);
var shuttleDestinations = new Dictionary<string, string>
{
    ["SH-01"] = "A01-L03",
    ["SH-02"] = "A01-L06",
    ["SH-03"] = "A01-L05",
};
var shuttleDone = new HashSet<string>();
var shuttleMoveStarted = new Dictionary<string, bool>();

// ── The run loop ──
Console.WriteLine("=== Run loop: 3 shuttles, overlapping routes ===");
var round = 0;
var maxRounds = 20;
while (shuttleDone.Count < 3 && round < maxRounds)
{
    round++;
    Console.WriteLine($"-- round {round} --");

    // Phase 1: for each idle shuttle, check the gate and start a move.
    foreach (var shuttleId in new[] { "SH-01", "SH-02", "SH-03" })
    {
        if (shuttleDone.Contains(shuttleId) || shuttleMoveStarted.ContainsKey(shuttleId))
            continue;

        var destination = shuttleDestinations[shuttleId];
        Console.WriteLine($"  {shuttleId} at {shuttlePositions[shuttleId]} wants {destination}");

        if (!CanMove(shuttleId, destination))
            continue;

        if (!CommitMove(shuttleId, destination))
            continue;

        shuttleMoveStarted[shuttleId] = true;
        Console.WriteLine($"  {shuttleId} moving {shuttlePositions[shuttleId]} -> {destination}");
    }

    // Phase 2: complete any move that was started (toy: moves complete in one round).
    foreach (var shuttleId in shuttleMoveStarted.Keys.ToList())
    {
        var destination = shuttleDestinations[shuttleId];
        shuttlePositions[shuttleId] = destination;
        occupancy[shuttleId] = destination;
        traffic.Release(shuttleId);
        shuttleMoveStarted.Remove(shuttleId);
        shuttleDone.Add(shuttleId);
        Console.WriteLine($"  {shuttleId} arrived at {destination}, released transit reservation");
    }

    // Phase 3: check for collisions (no two shuttles in the same lane).
    var allPositions = shuttlePositions.Values.ToList();
    var collisions = allPositions.Count - allPositions.Distinct().Count();
    if (collisions > 0)
        Console.WriteLine($"  COLLISION: {collisions} shuttles share a lane!");
}

Console.WriteLine();
Console.WriteLine($"rounds: {round}");
Console.WriteLine($"shuttles done: {shuttleDone.Count}/3");
var finalPositions = shuttlePositions.Values.ToList();
var finalCollisions = finalPositions.Count - finalPositions.Distinct().Count();
Console.WriteLine($"collisions: {finalCollisions}");
Console.WriteLine(shuttleDone.Count == 3 && finalCollisions == 0
    ? "RESULT: all shuttles finished, no collisions, no deadlock"
    : "RESULT: FAILED");
```

Run it:

```bash
cd /tmp/opencode/l12toy
dotnet run
```

Observed output (exit 0):

```
=== Run loop: 3 shuttles, overlapping routes ===
-- round 1 --
  SH-01 at A01-L01 wants A01-L03
    gate refuses: A01-L02 occupied by SH-03
  SH-02 at A01-L04 wants A01-L06
  SH-02 moving A01-L04 -> A01-L06
  SH-03 at A01-L02 wants A01-L05
    gate refuses: A01-L04 occupied by SH-02
  SH-02 arrived at A01-L06, released transit reservation
-- round 2 --
  SH-01 at A01-L01 wants A01-L03
    gate refuses: A01-L02 occupied by SH-03
  SH-03 at A01-L02 wants A01-L05
  SH-03 moving A01-L02 -> A01-L05
  SH-03 arrived at A01-L05, released transit reservation
-- round 3 --
  SH-01 at A01-L01 wants A01-L03
  SH-01 moving A01-L01 -> A01-L03
  SH-01 arrived at A01-L03, released transit reservation

rounds: 3
shuttles done: 3/3
collisions: 0
RESULT: all shuttles finished, no collisions, no deadlock
```

Read the output.

- `gate refuses: A01-L02 occupied by SH-03` — SH-01's route passes through L02, which SH-03 occupies. The gate refuses. This is the **occupancy** check firing.
- `SH-02 moving A01-L04 -> A01-L06` — SH-02's route is clear. The gate allows, the run loop reserves, and the move starts.
- `SH-02 arrived at A01-L06, released transit reservation` — the move completes. The occupancy map updates to L06. The transit reservation is released.
- `collisions: 0` — no two shuttles ever share a lane. That is "no collisions."
- `shuttles done: 3/3` — every shuttle finished. Nobody was stuck forever. That is "no permanent deadlock."

Your assignment is this toy with the real names and the real plumbing: a `RunLoop` class in `FleetManager`, a split gate (`CanMove` + `CommitMove`), `lock`-protected shared state, and a stress test with four or more shuttles.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. What does the run loop do, tick by tick? Why is it the caller and not the decision logic?
2. Why does the occupancy map need a `lock`? What goes wrong without it, and which two threads exist once M5 wires the status path?
3. Why is `ConcurrentDictionary` not enough for `SegmentReservations`? What is the check-then-write race?
4. The old `CanMove` reserved the route as a side effect. What leaks, and when? How does splitting into `CanMove` + `CommitMove` fix it?
5. In the toy, why does SH-02 move first? What would happen if all three shuttles wanted to pass through each other's lanes at the same time?

---

## The assignment

Build the M4 run loop: a `RunLoop` class that drives shuttles through the gate, a split gate (`CanMove` + `CommitMove`), `lock`-protected shared state, and a stress test with four or more shuttles. Prove no collisions and no permanent deadlock.

### Requirements

1. **Add `IsRouteFree` to `TrafficController`: `src/TrafficCore/TrafficController.cs`.**
   - `public bool IsRouteFree(string shuttleId, string fromLane, string toLane)` — a pure check. It gets the route via `track.Route(fromLane, toLane)` and returns `true` if every segment is free or owned by `shuttleId`. It writes nothing.
   - The file must contain **no** `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts`. It stays pure.

2. **Split the gate: `src/FleetManager/RunLoopGate.cs`.**
   - `public bool CanMove(string shuttleId, string destinationLane)` — a pure check. It gets the shuttle's current position from `occupancy.GetOccupiedLane(shuttleId)`. If `null`, refuse. It gets the route via `track.Route(currentLane, destinationLane)`. It checks occupancy: if `occupancy.IsOccupiedByOthers(route, shuttleId)` is `true`, refuse. It checks transit reservations: if `traffic.IsRouteFree(shuttleId, currentLane, destinationLane)` is `false`, refuse. It writes **nothing**.
   - `public bool CommitMove(string shuttleId, string destinationLane)` — reserves the route. It gets the shuttle's current position. If `null`, refuse. It calls `traffic.TryReserveRoute(shuttleId, currentLane, destinationLane)` and returns `decision.Granted`.
   - The file must contain **no** `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts`. It references `TrafficCore` and `ShuttleCore` for `Track` and `TrafficController`.

3. **Add a `lock` to `OccupancyMap`: `src/FleetManager/OccupancyMap.cs`.**
   - Add a `private readonly object _lock = new();` field.
   - Wrap every public method body in `lock (_lock) { ... }`.
   - The dictionary field stays `private readonly Dictionary<string, string>`. The lock protects it.
   - The file must contain **no** `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts`. It stays a plain in-memory data structure.

4. **Add a `lock` to `SegmentReservations`: `src/TrafficCore/SegmentReservations.cs`.**
   - Add a `private readonly object _lock = new();` field.
   - Wrap every public method body in `lock (_lock) { ... }`.
   - The dictionary field stays `private readonly Dictionary<string, string>`. The lock protects it.
   - The file must contain **no** `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts`. It stays pure.

5. **Add the run loop: `src/FleetManager/RunLoop.cs`.**
   - `public sealed class RunLoop` with constructor `RunLoop(OccupancyMap occupancy, Track track, TrafficController traffic, RunLoopGate gate, TaskBoard board, TaskDispatcher dispatcher, IReadOnlyList<Shuttle> shuttles, DateTime startTime, TimeSpan tickInterval)`.
   - `public async Task<RunResult> RunAsync(CancellationToken cancellationToken)` — runs the loop until all tasks are done or the cancellation token is fired.
   - Each tick:
     1. For each idle shuttle, peek at the next queued task via `board.NextQueued()`. If there is a task and `gate.CanMove(shuttleId, task.ToLane)` is `true`, call `dispatcher.AssignNextAsync(shuttleId, now, cancellationToken)`, then `gate.CommitMove(shuttleId, task.ToLane)`, then `shuttle.StartMove(task.ToLane, now)`.
     2. Advance `now` by `tickInterval`.
     3. For each moving shuttle, try `shuttle.CompleteMove(now)`. If it succeeds, call `occupancy.SetOccupiedLane(shuttleId, shuttle.Position)` and `traffic.Release(shuttleId)`. If it throws `ShuttleRejectedException`, the move is not complete yet; skip.
   - The loop stops when `board.NextQueued()` returns `null` and all shuttles are idle, or when the cancellation token is fired.
   - `public sealed record RunResult(int Ticks, bool AllDone);`
   - The file must contain **no** `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts`. It references `ShuttleCore` for `Shuttle` and `FleetCore` for `TaskBoard`.

6. **Add the stress test: `src/FleetManager.Tests/RunLoopStressTests.cs`.**
   - Build a real `Track` from `new Rack(1, 8, 1)`, a real `TrafficController`, an `OccupancyMap`, a `RunLoopGate`, a `TaskBoard`, a `TaskDispatcher` (with an in-memory repository), and four or more `Shuttle` objects.
   - Create four or more tasks with overlapping routes. Add them to the board.
   - Run the loop with a fixed start time and a tick interval. Use a `CancellationTokenSource` with a timeout so a deadlock fails the test instead of hanging.
   - Assert `result.AllDone` is `true` (all shuttles finished).
   - After every tick, assert that no segment has more than one owner (no collisions). You can check this by reading `SegmentReservations.OwnerOf` for every segment in the track, or by checking that no two shuttles occupy the same lane.
   - These tests are **pure**: no `Integration` trait, no database, no broker.

7. **Keep dispatcher shape intact.** `src/FleetManager/TaskDispatcher.cs` is unchanged. `AssignNextAsync` keeps its exact signature and behavior (record 15a §2). The run loop calls the dispatcher, never the other way around.

8. **Release on departure semantics.** When a move completes, call `traffic.Release(shuttleId)` to free the transit reservation. The destination remains occupied by physical position (update the occupancy map when the shuttle arrives). Document this in a comment in the run loop.

9. **Faulted/lost shuttle is an obstacle.** Do not auto-release on timeout. Last-known occupancy remains until recovery clears it (record 17 §4). No `lastSeenAt`, no timeout, no heartbeat logic.

10. **M5 items are not present.** No heartbeats, no `lastSeenAt`, no timeout detection, no alarms, no reconciliation persistence. The occupancy map is in-memory only.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are asserted, not admired. Read every line's purpose before you run it.

```bash
# Phase 1 — the new pieces exist, with the pinned names
test -f src/FleetManager/RunLoop.cs || echo "missing RunLoop.cs"
test -f src/FleetManager.Tests/RunLoopStressTests.cs || echo "missing RunLoopStressTests.cs"
grep -qE 'class RunLoop' src/FleetManager/RunLoop.cs
grep -qE 'record RunResult' src/FleetManager/RunLoop.cs
grep -qE 'IsRouteFree' src/TrafficCore/TrafficController.cs
grep -qE 'CommitMove' src/FleetManager/RunLoopGate.cs
grep -qE 'CanMove' src/FleetManager/RunLoopGate.cs
for m in GetOccupiedLane SetOccupiedLane IsOccupiedByOthers; do grep -qE "\b$m\b" src/FleetManager/OccupancyMap.cs || echo "missing OccupancyMap member: $m"; done
for m in TryReserveAll Release OwnerOf SegmentsOwnedBy; do grep -qE "\b$m\b" src/TrafficCore/SegmentReservations.cs || echo "missing reservations member: $m"; done
```
```bash
# Phase 2 — purity: no clock, no ids, no I/O types in the new and changed files
grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' src/FleetManager/RunLoop.cs src/FleetManager/RunLoopGate.cs src/FleetManager/OccupancyMap.cs src/TrafficCore/TrafficController.cs src/TrafficCore/SegmentReservations.cs --include='*.cs'  # nothing
grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts|Postgres' src/FleetManager/RunLoop.cs src/FleetManager/RunLoopGate.cs src/FleetManager/OccupancyMap.cs src/TrafficCore/TrafficController.cs src/TrafficCore/SegmentReservations.cs --include='*.cs'  # nothing
grep -rnE '"A[0-9]{2}-L[0-9]{2}' src/FleetManager/RunLoop.cs src/FleetManager/RunLoopGate.cs src/FleetManager/OccupancyMap.cs src/TrafficCore/TrafficController.cs src/TrafficCore/SegmentReservations.cs --include='*.cs'  # nothing
```
```bash
# Phase 3 — the lock is really there
grep -qE 'lock *\(' src/FleetManager/OccupancyMap.cs || echo "FAIL: OccupancyMap must use lock"
grep -qE 'lock *\(' src/TrafficCore/SegmentReservations.cs || echo "FAIL: SegmentReservations must use lock"
grep -qE 'private readonly object _lock' src/FleetManager/OccupancyMap.cs
grep -qE 'private readonly object _lock' src/TrafficCore/SegmentReservations.cs
```
```bash
# Phase 4 — build, and the pure suite with NO database needed (the container's command).
# The counts are gated, not admired.
cd src
dotnet build
T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed!|Failed!' "$T"
P=$(grep -c 'Passed!' "$T"); F=$(grep -c 'Failed!' "$T")
TOTAL=$(grep -oE 'Passed: +[0-9]+' "$T" | grep -oE '[0-9]+' | awk '{s+=$1} END{print s}')
echo "assemblies-passed=$P assemblies-failed=$F total=$TOTAL"
[ "$RC" -eq 0 ] && [ "$P" -eq 8 ] && [ "$F" -eq 0 ] && [ "$TOTAL" -eq 84 ] \
  && echo "suite gate OK" || echo "FAIL: expected RC 0, 8 Passed!, 0 Failed!, 84 tests"
grep -E 'Passed: +16,.*FleetManager\.Tests\.dll' "$T" && echo "FleetManager.Tests=16 OK" || echo "FAIL: FleetManager.Tests should show 16"
rm -f "$T"

# Phase 4b — the new stress test is really there, by anchored name
L=$(mktemp); dotnet test ShuttleFleet.slnx --list-tests > "$L" 2>&1
for t in Four_or_more_shuttles_finish_with_no_collisions_and_no_deadlock; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                                            # nothing
rm -f "$L"
cd ..
```
```bash
# Phase 5 — no test side-file launches docker, and the stress test is pure.
# Scan EVERY test project, not one file.
grep -rnE 'Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))' src/*.Tests --include='*.cs' \
  && echo "FAIL: a test must not start docker" || echo "OK: no test starts docker"
grep -rn 'Trait' src/FleetManager.Tests/RunLoopStressTests.cs --include='*.cs'                         # nothing
grep -rniE 'Npgsql|Postgres' src/FleetManager.Tests/RunLoopStressTests.cs --include='*.cs'              # nothing
```
```bash
# Phase 6 — protected files unchanged, dispatcher shape intact, contract still v2
git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs docs/contract docker/Dockerfile.build \
  && echo "OK: dispatcher, Program.cs, contract and Dockerfile unchanged" \
  || echo "FAIL: a protected file changed"
grep -qE '^Version: 2$' docs/contract/v2.md
test ! -f docs/contract/v3.md
for m in AssignNextAsync StartAsync CompleteAsync FailAsync; do grep -qE "${m}\s*\(" src/FleetManager/TaskDispatcher.cs || echo "missing dispatcher method: $m"; done
```

**Proof details.** The assignment adds two product files (`RunLoop.cs`, `RunLoopStressTests.cs`), modifies four existing files (`RunLoopGate.cs`, `OccupancyMap.cs`, `TrafficController.cs`, `SegmentReservations.cs`), and adds one test file; the pure test total goes from **83 to 84** (`FleetManager.Tests` gains one test, 15 → 16). The stress test must show `AllDone == true` and zero segments with more than one owner. If a reviewer makes the gate skip the occupancy check, the stress test must go red; if the gate calls `TaskDispatcher` itself, the dispatcher-shape check fails; if the gate checks occupancy **after** `AssignNextAsync`, the ordering is wrong; if the `lock` is removed, the stress test may still pass (it is single-threaded) but the Phase 3 grep fails.

### Stretch goal

Prove the concurrency claim in a test: start two `Task` instances that call `OccupancyMap.SetOccupiedLane` and `OccupancyMap.IsOccupiedByOthers` in a loop, and assert no exception is thrown and the map stays consistent. Comment that this is the M5 wiring's safety guarantee.

### Not in this assignment

- **Wiring the run loop into `Program.cs`** — a later lesson. The `RunLoop` class is proven by the stress test here.
- **Heartbeats, `lastSeenAt`, timeout detection, alarms, reconciliation** — M5.
- **Auto-release on fault** — unsafe and not done (record 17 §4).
- **WMS orders** — M6. **Dashboard** — M7.
- **Real shuttle movement over MQTT.** The contract stays v2.

---

## Closing

Read the spec's M4 line once more: *"Reserve lane segments so two shuttles never occupy the same space. Detect or avoid deadlock. Done when: a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock, and the rule used is written down and explained in a decision record."*

Lesson 10 built the pure traffic core. Lesson 11 added the occupancy view and the gate. Lesson 12 wires them together: the run loop drives shuttles through the gate, the gate is split into a pure check and a reserve, and shared state is protected by a lock. The stress test proves the M4 done-when: four or more shuttles, no collisions, no permanent deadlock.

Before you call this done, be ready to explain without notes:

- what the run loop does and why it is the caller, not the decision logic;
- why the occupancy map and the reservation table need a `lock`, and what `ConcurrentDictionary` does not fix;
- why the gate is split into `CanMove` and `CommitMove`, and what leaks if it is not;
- what happens to a lost shuttle's occupancy, and why we never auto-release on timeout.

When the reviewer passes this, M4 is done: the stress scenario has run, the rule is written down in records 16, 17, and 19, and the fleet manager can drive shuttles through the gate safely.
