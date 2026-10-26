# Lesson 30: One pause state, shared and thread-safe

**Milestone:** M7 (operator dashboard and diagnostics) · **Language:** C#/.NET 10 · **Prerequisite:** [lesson 29](29-traffic-control-under-dashboard.md) (pause state on the gate) and [lesson 26](26-dashboard-diagnostics.md) (operator command topics)
**Concepts this assignment requires:**

- [ ] **Shared pause state in FleetCore** — a `PauseState` class owns the paused shuttle-id set so both the gate and `serve` use one set → [§1](#1-shared-pause-state-in-fleetcore)
- [ ] **Thread safety** — the set is touched by two threads; a lock makes every read and write safe → [§2](#2-thread-safety)
- [ ] **Wiring into serve** — `serve` creates one `PauseState` and the operator-command handler calls it; the gate reads the same instance → [§3](#3-wiring-into-serve)

Three new concepts. That is the cap.

**Not in this lesson:** changing `TaskBoard`'s dispatch-pause semantics (lesson 26), cross-process pause over MQTT (the sim must subscribe to operator commands; tracked as a follow-up), recovery command sending from `serve` (the recovery flow already works at the message level, M5), the mock WMS submit/cancel client (lesson 28 stretch goal), and any change to `src/Contracts/` or `docs/contract/`. The MQTT contract stays `4`. The WMS REST contract stays `2`.

---

## Where this fits

The spec's M7 says: *"a small web dashboard shows the rack map, shuttle states, task queue, and alarms. The operator can pause, resume, and acknowledge recovery."* Lesson 26 built the operator command topics and the `HandleOperatorCommandAsync` handler. Lesson 29 put pause on the gate so movement actually stops. But two gaps remain:

1. The handler in `serve` only calls `taskBoard.PauseAllExcept` / `ResumeAllExcept`. It never touches the gate. A dashboard Pause stops new task dispatch but shuttles that are already moving keep going.
2. The gate's set is a plain `HashSet<string>`. Once `serve` starts calling it from the MQTT handler thread while a tick loop reads it, the set can corrupt.

This slice makes the pause state **one object**, **thread-safe**, and **reachable from `serve`**, so a dashboard Pause reaches the movement gate. It builds directly on review 29's second top issue ("two independent, in-memory pause sets") and on the lesson 29 stretch goal ("wire the gate's pause to the dashboard").

---

## Verification table

Everything below was checked on **2026-10-26**, in this repo. Scratch work went in `/tmp/opencode` and was cleaned up. Nothing was left running.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | `RunLoopGate` owns its own `HashSet` today | read `src/FleetManager/RunLoopGate.cs` | field `_pausedShuttles` at line 7; `Pause`/`Resume`/`IsPaused`/`PauseAll`/`ResumeAll` delegate to it |
| 2 | `serve` does not instantiate a gate | `grep -n "RunLoopGate\|new PauseState" src/FleetManager/Program.cs` | no matches |
| 3 | `HandleOperatorCommandAsync` only calls `taskBoard` | read `src/FleetManager/Program.cs` lines 315-335 | `PauseAllExcept` / `ResumeAllExcept` / `AcknowledgeAsync` only |
| 4 | `FleetCore` has no pause type | `grep -rn "PauseState" src/FleetCore` | no matches |
| 5 | `TaskBoard` keeps its own `_pausedTaskIds` set | read `src/FleetCore/TaskBoard.cs` | field at line 5; `NextQueued`/`Queued` filter on it |
| 6 | Baseline pure suite is green | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **224 passed, 0 failed** |
| 7 | `ContractVersion.Current` is `4` | read `src/Contracts/ContractVersion.cs` | value `4`; no edit planned |
| 8 | Build is clean before the change | `cd src && dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |
| 9 | No `PauseState` or lock exists in the gate | `grep -n "lock\|PauseState" src/FleetManager/RunLoopGate.cs` | no matches |

The broker and database were left down after these checks.

---

## Options survey

### Survey 1: where the pause set lives

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| A. Keep the set inside `RunLoopGate` | The gate owns a `HashSet<string>` as today. | Lowest. | `serve` has no gate, so the dashboard cannot reach movement pause without instantiating a whole gate. The two sets stay separate. | Seen in small single-process systems. |
| **B. Extract a `PauseState` in `FleetCore`** (chosen) | A small class in `FleetCore` owns the paused shuttle-id set and exposes `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`. `RunLoopGate` holds one. `serve` instantiates one directly. | Low. One new file in `FleetCore`; the gate delegates. | Two instances must be the *same* instance to stay in sync; the composition root must pass one instance around. | Common: a shared "movement permission" object that both the gate and the operator-facing API use. |
| C. Put the pause set in `TaskBoard` | `TaskBoard` owns the set; the gate asks the board. | Medium. | Mixing task dispatch and movement permission in one class blurs the boundary the reviewer already flagged. | Rare. |

**Decision B.** Full reasons: the pause set is a movement concern, but it must be reachable from `serve` without a rack model. Extracting it into `FleetCore` lets `serve` create one instance and hand the same instance to any in-process gate. The keys stay shuttle ids, so `TaskBoard`'s dispatch-pause semantics from lesson 26 are untouched.

### Survey 2: thread safety

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. `lock` around a `HashSet`** (chosen) | A private lock object guards every read and write of the set. | Low. | Lock contention is negligible for a tiny set touched once per tick and once per operator command. | The default choice for a small shared set. |
| B. `ConcurrentDictionary<string, byte>` | Use the concurrent collection as a set. | Low. | The all-shuttle helpers loop under no lock, so a snapshot can be stale. Less obvious to an interviewer. | Used when contention is high. Not here. |
| C. No lock, rely on single thread | Document that the set is single-threaded. | Lowest. | The moment a second thread touches it (the MQTT handler), it is wrong. The dashboard wiring makes that moment certain. | Only valid before any concurrency exists. |

**Decision A.** Full reasons: a `lock` is the simplest correct answer and is easy to defend in an interview. The set is tiny and touched rarely, so contention is not a concern.

### Survey 3: who creates the instance

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. `serve` creates one and passes it to the handler and to any gate** (chosen) | The composition root owns the instance. | Low. | If a caller forgets to pass it, a second instance is created and the sets drift. Mitigated by keeping the gate's 3-arg constructor (creates its own) for tests and sim-only code. | Standard composition-root ownership. |
| B. A static singleton | One global instance. | Lowest. | Hidden dependency; tests cannot isolate; two fleet managers in one process would share pause state. | Avoided in testable code. |

**Decision A.** Full reasons: the composition root owns the instance. Tests keep using the gate's 3-arg constructor, which creates its own `PauseState`, so no test changes.

---

## Decision

The full decision is [record 39](../decisions/39-shared-pause-state.md). Short version:

- **A.** Extract `PauseState` into `FleetCore` (new file `PauseState.cs`). It owns a `HashSet<string>` of paused shuttle ids (ordinal) guarded by a lock, and exposes `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`, plus a `Snapshot()` for diagnostics.
- **B.** `RunLoopGate` holds a `PauseState`. The 3-arg constructor creates one internally (backward compatible). A new 4-arg constructor accepts a shared instance. `Pause`/`Resume`/`IsPaused`/`PauseAll`/`ResumeAll` delegate.
- **C.** `serve` creates one `PauseState` and the operator-command handler calls `_pauseState.Pause`/`Resume`/`PauseAll`/`ResumeAll`. `TaskBoard`'s dispatch pause from lesson 26 is unchanged; the handler drives both, so a dashboard Pause stops dispatch (TaskBoard) and movement (`PauseState`).

No change to the message contract. `ContractVersion.Current` stays `4`. No new configuration key. `TaskBoard` is unchanged.

---

## The lecture

Today we are going to learn about **three concepts** that make a dashboard Pause actually reach the movement gate, safely:

1. **Shared pause state in FleetCore** — a `PauseState` class owns the paused shuttle-id set
2. **Thread safety** — the set is touched by two threads; a lock makes every read and write safe
3. **Wiring into serve** — `serve` creates one `PauseState` and the operator-command handler calls it

### 1 Shared pause state in FleetCore

**What it is.** Today the paused set lives inside `RunLoopGate`, a `FleetManager` class. `serve` has no gate, because `serve` has no rack, track, or traffic model. So the dashboard cannot reach the movement pause at all. This concept moves the set into a small `FleetCore` class called `PauseState`. `FleetCore` is lower than `FleetManager`, so `serve` can create one directly, and `RunLoopGate` can hold one too. One type, one set, two owners.

**Why we need it.** The gate is the right *place* for the pause rule (lesson 29), but the wrong *package* for `serve` to depend on. `serve` would have to build a whole `Track` and `TrafficController` just to pause a shuttle. Extracting the set into `FleetCore` keeps the rule where it is and makes the data reachable where it is needed.

**A real-world example.** A warehouse has a "movement hold" board on the wall. The traffic controller reads it before granting a route. The supervisor can walk up and flip a switch on the same board. The board is one object; both the controller and the supervisor touch it. Neither has to own the whole warehouse model to flip a switch.

**The code.** The new class:

```csharp
namespace FleetCore;

public sealed class PauseState
{
    private readonly object _gate = new();
    private readonly HashSet<string> _paused = new(StringComparer.Ordinal);

    public void Pause(string shuttleId)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) return;
        lock (_gate) { _paused.Add(shuttleId); }
    }

    public void Resume(string shuttleId)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) return;
        lock (_gate) { _paused.Remove(shuttleId); }
    }

    public bool IsPaused(string shuttleId)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) return false;
        lock (_gate) { return _paused.Contains(shuttleId); }
    }

    public void PauseAll(IEnumerable<string> shuttleIds)
    {
        lock (_gate)
        {
            foreach (var id in shuttleIds)
                if (!string.IsNullOrWhiteSpace(id)) _paused.Add(id);
        }
    }

    public void ResumeAll(IEnumerable<string> shuttleIds)
    {
        lock (_gate)
        {
            foreach (var id in shuttleIds)
                if (!string.IsNullOrWhiteSpace(id)) _paused.Remove(id);
        }
    }

    public IReadOnlyCollection<string> Snapshot()
    {
        lock (_gate) { return _paused.ToArray(); }
    }
}
```

Line by line:

- `namespace FleetCore;` — the class lives in the lower layer so both `FleetManager` and `serve` can use it without a cycle.
- `private readonly object _gate = new();` — the lock object. It is separate from the set so the lock identity never changes. `readonly` means the field is set once.
- `private readonly HashSet<string> _paused` — the set of paused shuttle ids. Ordinal comparison means "SH-01" and "sh-01" are different ids.
- `Pause(id)` — adds the id. Adding an id that is already there does nothing (a set has no duplicates). The `lock` makes the add safe.
- `Resume(id)` — removes the id. Removing an id that is not there does nothing.
- `IsPaused(id)` — the query the gate's `CanMove` and the tick loop call.
- `PauseAll` / `ResumeAll` — convenience for "pause everything". They loop under one lock so the whole batch is atomic.
- `Snapshot()` — returns a copy of the set for diagnostics and tests. The copy is taken under the lock, so it is consistent.

**Why `FleetCore` and not `FleetManager`.** `serve` is in `FleetManager`. If the pause state stayed in `FleetManager`, `serve` could use it but a lower layer could not. `FleetCore` is the lowest layer that both the gate and the dashboard backend can reach. Putting it there costs nothing and removes the "serve has no gate" problem.

**What does not change.** `TaskBoard` keeps its own paused task-id set from lesson 26. The two sets are still keyed differently (shuttle id vs task id). The handler drives both together so a single operator click stops dispatch and movement. Unifying the keys is a later slice, tracked in decision 39.

### 2 Thread safety

**What it is.** A `HashSet` is not safe to use from two threads at once. If thread A is adding an id while thread B is checking membership, the internal hash table can be in the middle of a resize. The result is a corrupted set, a lost id, or an exception. This concept wraps every access in a `lock` so only one thread touches the set at a time.

**Why we need it.** Once `serve` calls `PauseState.Pause` from the MQTT handler thread while a tick loop (possibly in the same process, in a scenario runner) calls `IsPaused`, the set is shared. Without a lock, the race is real and intermittent — the worst kind of bug, because it passes tests and fails in demos.

**A real-world example.** A whiteboard in a warehouse office. Two people cannot write on it at the same time without smudging. A single marker (the lock) means one person writes, then hands it over. The rule is simple: no writing without the marker.

**The code.** The lock is already in the `PauseState` above. Here is how the gate uses it:

```csharp
public sealed class RunLoopGate
{
    private readonly PauseState _pauseState;

    public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic)
        : this(occupancy, track, traffic, new PauseState()) { }

    public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic, PauseState pauseState)
    {
        Occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        Track = track ?? throw new ArgumentNullException(nameof(track));
        Traffic = traffic ?? throw new ArgumentNullException(nameof(traffic));
        _pauseState = pauseState ?? throw new ArgumentNullException(nameof(pauseState));
    }

    public void Pause(string shuttleId) => _pauseState.Pause(shuttleId);
    public void Resume(string shuttleId) => _pauseState.Resume(shuttleId);
    public bool IsPaused(string shuttleId) => _pauseState.IsPaused(shuttleId);
    public void PauseAll(IEnumerable<string> shuttleIds) => _pauseState.PauseAll(shuttleIds);
    public void ResumeAll(IEnumerable<string> shuttleIds) => _pauseState.ResumeAll(shuttleIds);

    public bool CanMove(string shuttleId, string destinationLane)
    {
        if (_pauseState.IsPaused(shuttleId)) return false;
        // ... occupancy and traffic checks, unchanged ...
    }
}
```

Line by line:

- Two constructors. The 3-arg one creates a private `PauseState` so every existing test and sim keeps working unchanged. The 4-arg one accepts a shared instance so `serve` (or a scenario runner) can inject the one it owns.
- `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll` all delegate to `_pauseState`. The gate keeps its public shape, so no caller changes.
- `CanMove` calls `_pauseState.IsPaused`. The lock is taken inside `IsPaused`, so the gate does not need to know about locking.

**Why the lock lives in `PauseState`, not in the gate.** The gate is only one user of the state. If the lock lived in the gate, `serve`'s handler would have to go through the gate to pause, and `serve` has no gate. Putting the lock in `PauseState` makes every user safe by construction, with no coordination between callers.

**What the lock does not protect.** The lock protects the set. It does not protect the whole "pause then check CanMove" sequence. A shuttle could be paused between `IsPaused` returning false and the move starting. That is acceptable: pause is best-effort at tick granularity, and the next tick sees the pause. A real system has the same window.

### 3 Wiring into serve

**What it is.** `serve` creates one `PauseState` at startup and the operator-command handler calls it. A dashboard Pause now stops movement (when a RunLoop shares the instance) and, independently, stops task dispatch through `TaskBoard`. A dashboard Resume frees both.

**Why we need it.** Today the handler is the gap: it calls `taskBoard.PauseAllExcept` and nothing else. The gate never hears about the operator's click. Wiring the handler to `_pauseState` closes that gap for any in-process consumer of the state.

**A real-world example.** The supervisor's switch (concept 1) is on the wall, but nobody told the traffic controller where the wall is. This concept runs a wire from the supervisor's switch to the controller's board. The switch and the board are still the same board; the wire just makes the connection that was missing.

**The code.** In `RunServeAsync`, after `taskBoard` is created:

```csharp
var pauseState = new FleetCore.PauseState();
```

And the handler becomes:

```csharp
async Task HandleOperatorCommandAsync(MessageReceived message, CancellationToken cancellationToken)
{
    var cmd = JsonSerializer.Deserialize<OperatorCommandMessage>(message.Payload);
    if (cmd is null) return;

    switch (cmd.Kind)
    {
        case OperatorCommandMessage.Command.Pause:
            await taskBoard.PauseAllExcept(cmd.ShuttleId, cancellationToken);
            if (string.IsNullOrWhiteSpace(cmd.ShuttleId))
                pauseState.PauseAll(_shuttleIds);   // all shuttles
            else
                pauseState.Pause(cmd.ShuttleId);
            break;
        case OperatorCommandMessage.Command.Resume:
            await taskBoard.ResumeAllExcept(cmd.ShuttleId, cancellationToken);
            if (string.IsNullOrWhiteSpace(cmd.ShuttleId))
                pauseState.ResumeAll(_shuttleIds);
            else
                pauseState.Resume(cmd.ShuttleId);
            break;
        case OperatorCommandMessage.Command.Acknowledge:
            await alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
            break;
    }
}
```

Line by line:

- `var pauseState = new PauseState();` — one instance, owned by the composition root.
- `taskBoard.PauseAllExcept(...)` — unchanged from lesson 26. This stops dispatch of new work.
- `pauseState.Pause(...)` / `PauseAll(...)` — the new call. This stops movement for the named shuttle (or all shuttles when the id is empty).
- The empty-id branch pauses every known shuttle. `_shuttleIds` is the list of shuttles `serve` knows about (from status messages, or a configured list). If `serve` does not track a list yet, pause-all is a stretch; pausing a named shuttle works today.
- `Acknowledge` is unchanged: it marks the alarm acknowledged in SQL.

**Why two calls in the handler.** The dispatch pause (`TaskBoard`) and the movement pause (`PauseState`) are still two sets with two keys. The handler is the one place that knows about both, so it drives both. An operator click cannot leave them out of step as long as both calls happen in the same handler. Cross-process consumers (a sim in another process) do not see this instance; making them see it is the follow-up in decision 39.

**What the handler does not do.** It does not send a recovery command. Recovery (M5) already works at the message level through `RecoverySender` and the shuttle's recovery endpoint. Wiring recovery into `serve` is a separate slice.

---

## Recap in plain words

- **Shared pause state in FleetCore** — `PauseState` owns the paused shuttle-id set. `RunLoopGate` holds one; `serve` creates one. Both use the same type, so the same instance can be shared.
- **Thread safety** — a `lock` guards every read and write. Two threads can pause and check at the same time without corrupting the set.
- **Wiring into serve** — `serve` creates one `PauseState` and the operator-command handler calls `Pause`/`Resume`/`PauseAll`/`ResumeAll` alongside the existing `TaskBoard` calls. A dashboard Pause now reaches the movement gate when the sim shares the instance.

---

## Worked example (toy — NOT the assignment)

This toy shows a shared, thread-safe pause state touched by two threads. **It is not the assignment** — the assignment wires the real `PauseState` and `RunLoopGate` in `src/`.

Create it in a scratch directory:

```bash
mkdir -p /tmp/opencode/l30toy
cd /tmp/opencode/l30toy
dotnet new console --force -o .
```

Put this in `Program.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

// A tiny thread-safe pause state, mirroring FleetCore.PauseState.
sealed class PauseState
{
    private readonly object _gate = new();
    private readonly HashSet<string> _paused = new(StringComparer.Ordinal);

    public void Pause(string id) { lock (_gate) _paused.Add(id); }
    public void Resume(string id) { lock (_gate) _paused.Remove(id); }
    public bool IsPaused(string id) { lock (_gate) return _paused.Contains(id); }
    public int Count { get { lock (_gate) return _paused.Count; } }
}

class Program
{
    static async Task Main()
    {
        var state = new PauseState();
        const int opsPerThread = 1000;

        // Thread A pauses and resumes SH-01 repeatedly.
        var t1 = Task.Run(() =>
        {
            for (var i = 0; i < opsPerThread; i++)
            {
                state.Pause("SH-01");
                state.Resume("SH-01");
            }
        });

        // Thread B reads IsPaused concurrently.
        var t2 = Task.Run(() =>
        {
            for (var i = 0; i < opsPerThread; i++)
            {
                _ = state.IsPaused("SH-01"); // must never throw
            }
        });

        // Thread C pauses a different shuttle.
        var t3 = Task.Run(() =>
        {
            for (var i = 0; i < opsPerThread; i++)
            {
                state.Pause("SH-02");
            }
        });

        // Thread D snapshots the count.
        var t4 = Task.Run(() =>
        {
            for (var i = 0; i < opsPerThread; i++)
            {
                _ = state.Count; // must never throw
            }
        });

        await Task.WhenAll(t1, t2, t3, t4);

        Console.WriteLine($"done: SH-01 paused = {state.IsPaused("SH-01")}");
        Console.WriteLine($"done: SH-02 paused = {state.IsPaused("SH-02")}");
        Console.WriteLine($"done: count = {state.Count}");
    }
}
```

Run it:

```bash
cd /tmp/opencode/l30toy
dotnet run
```

Expected output (measured 2026-10-26):

```
done: SH-01 paused = False
done: SH-02 paused = True
done: count = 1
```

Read it line by line:

- `SH-01 paused = False` — thread A's last operation was `Resume`, so SH-01 is not paused. No crash despite 4000 concurrent operations.
- `SH-02 paused = True` — thread C paused SH-02 a thousand times; the set holds one entry (idempotent).
- `count = 1` — only SH-02 remains paused. The lock kept the set consistent through 4000 concurrent reads and writes.

**What the toy does not show.** It does not show the gate, the track, or the tick loop. Those are already built (lessons 10, 11, 29) and are not re-taught here. The toy's job is to show that a locked set survives concurrent access without throwing or corrupting.

---

## Say it out loud

Answer these out loud, with no notes.

1. Why does `PauseState` live in `FleetCore` instead of `FleetManager`? What would break if it stayed in `FleetManager`?
2. `RunLoopGate` has two constructors. When is each used, and why keep the 3-arg one at all?
3. The lock is inside `PauseState`, not inside `RunLoopGate`. Name two callers of `PauseState` and explain why the lock must be in the shared object, not in either caller.
4. `CanMove` calls `_pauseState.IsPaused` and then checks occupancy. Is the whole sequence protected by the lock? If not, what can go wrong, and is it acceptable?
5. The handler calls both `taskBoard.PauseAllExcept` and `pauseState.Pause`. Why both, and what would drift if one call were missing?

---

## The assignment

Take the worked example and make it run **for real**, inside `src/FleetCore` and `src/FleetManager`.

### The files you will change or add

| File | New? | What changes |
|---|---|---|
| `src/FleetCore/PauseState.cs` | new | Thread-safe paused shuttle-id set: `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`, `Snapshot`. |
| `src/FleetManager/RunLoopGate.cs` | edit | Hold a `PauseState`. Add a 4-arg constructor that accepts one. The 3-arg constructor creates its own. Delegate `Pause`/`Resume`/`IsPaused`/`PauseAll`/`ResumeAll`. |
| `src/FleetManager/Program.cs` | edit | In `RunServeAsync`, create one `PauseState`. In `HandleOperatorCommandAsync`, call `_pauseState.Pause`/`Resume`/`PauseAll`/`ResumeAll` alongside the existing `TaskBoard` calls. |
| `src/FleetCore.Tests/PauseStateTests.cs` | new | Unit tests for the set semantics and the lock. |
| `src/FleetManager.Tests/RunLoopGateTests.cs` | edit (maybe) | Add a test that a gate constructed with a shared `PauseState` reflects pauses made through that state. |

### Requirements

**R1 — `PauseState` is a thread-safe set.** `FleetCore.PauseState` owns a `HashSet<string>` of paused shuttle ids (ordinal comparison). Methods `Pause(string shuttleId)`, `Resume(string shuttleId)`, `IsPaused(string shuttleId)`, `PauseAll(IEnumerable<string> shuttleIds)`, `ResumeAll(IEnumerable<string> shuttleIds)`, and `Snapshot()` returning `IReadOnlyCollection<string>`. Every method is safe to call from any thread. `Pause` is idempotent; `Resume` on an unpaused id does nothing. Blank or null ids are ignored, not thrown.

**R2 — `RunLoopGate` delegates to `PauseState`.** `RunLoopGate` gains a `PauseState PauseState { get; }` property (or a private field with the same behaviour) and a constructor `RunLoopGate(OccupancyMap, Track, TrafficController, PauseState)`. The existing 3-arg constructor still works and creates its own `PauseState`. `CanMove` returns `false` when `_pauseState.IsPaused(shuttleId)` is true, checked before occupancy and traffic. `CommitMove` also refuses a paused shuttle. `Pause`/`Resume`/`IsPaused`/`PauseAll`/`ResumeAll` on the gate delegate to the state.

**R3 — `serve` creates one instance.** `RunServeAsync` creates `var pauseState = new PauseState();` (or `new FleetCore.PauseState();`) after `taskBoard`. `HandleOperatorCommandAsync` calls `pauseState.Pause(shuttleId)` on `Pause`, `pauseState.Resume(shuttleId)` on `Resume`, and the `*All` variants when `cmd.ShuttleId` is blank. The existing `TaskBoard` calls stay.

**R4 — no contract or config change.** `src/Contracts/`, `docs/contract/`, `ContractVersion.Current` (stays `4`), `TaskBoard.cs`, `WmsOrderBoard`, `appsettings.json` are untouched. No new configuration key.

**R5 — the facts.** At least these tests exist and pass:
- `Pause_is_idempotent` (PauseState unit): pause "SH-01" twice, assert `Snapshot().Count == 1`.
- `Resume_on_unpaused_id_does_nothing` (PauseState unit): resume "SH-01" without pausing, assert `IsPaused` is false and no throw.
- `PauseAll_pauses_every_listed_shuttle` (PauseState unit): pause three ids, assert all three `IsPaused`.
- `Snapshot_is_consistent_under_concurrency` (PauseState unit): 4 threads, 1000 ops each, no exception and final count matches.
- `CanMove_returns_false_when_paused_via_shared_state` (gate unit): construct a gate with a shared `PauseState`, pause "SH-01" through the state, assert `gate.CanMove("SH-01", any lane)` is false.
- `Shared_state_reflects_pause_made_through_gate` (gate unit): pause "SH-01" through the gate, assert `_pauseState.IsPaused("SH-01")` is true.

**R6 — what you must not touch.** `src/FleetCore/TaskBoard.cs` (its dispatch pause stays as lesson 26 defined it), `src/FleetCore/FleetTask.cs`, `src/Contracts/`, `docs/contract/`, `src/FleetManager/TaskExecutionLoop.cs`. `RunLoop.cs` is unchanged.

### Acceptance criteria

```bash
BASE=$(git rev-parse HEAD)
cd src && dotnet build --no-incremental
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# existing gate tests still pass:
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~RunLoopGate"
# new state tests pass:
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~PauseState"
grep -q "class PauseState" src/FleetCore/PauseState.cs
grep -q "lock" src/FleetCore/PauseState.cs
grep -q "PauseState" src/FleetManager/RunLoopGate.cs
grep -q "PauseState" src/FleetManager/Program.cs
test -z "$(git diff $BASE -- src/Contracts docs/contract)"
```

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Remove the lock from `PauseState.Pause` | `Snapshot_is_consistent_under_concurrency` (intermittent; run a few times) |
| b | Make `PauseState.Pause` a no-op | `CanMove_returns_false_when_paused_via_shared_state` and `Pause_is_idempotent` |
| c | Remove the `PauseState` call from the gate's `CanMove` | `CanMove_returns_false_when_paused_via_shared_state` |
| d | Give the gate a *new* `PauseState` instead of the shared one in `serve` | `Shared_state_reflects_pause_made_through_gate` (if the test injects the state) |

### Stretch goal

**Cross-process pause.** Make the sim (or a scenario runner) subscribe to `fleet/operator/command` and call `_gate.PauseState.Pause(...)` when a Pause arrives, mirroring `serve`'s handler. Measure the time from the MQTT publish to the shuttle holding its position. Compare it to the in-process case. This closes the gap between the dashboard button and a shuttle running in another process.

### What comes next

- **Injected-fault scenario run from the dashboard.** Use the pause wiring and the recovery flow (M5) to run a full scenario: inject a fault, pause, acknowledge recovery, resume, and confirm completion — all through operator commands.
- **The mock WMS submit/cancel client** (lesson 28 stretch goal). Real M6 work; independent of this slice.
- **M8 stretch.** OPC UA or Modbus TCP gateway, Python scenario runner, or a load test with measured limits. The thread-safe `PauseState` is ready for the multi-threaded scenario runner.

---

## What I assumed about what you know

I assumed you are comfortable with: `lock` statements and the `Monitor` they compile to, `HashSet<T>` and its `Add`/`Remove`/`Contains` methods, `IEnumerable<T>` and `foreach`, constructor overloading and delegation (`: this(...)`), and the existing `RunLoop`/`RunLoopGate` shape from lessons 11 and 29. If any of those is new, say so before building and I will walk through it.

The fleet ideas in this lesson — why pause state must be shared between the gate and the dashboard backend, why a plain `HashSet` is unsafe across threads, and why the lock belongs in the shared object rather than in either caller — are taught from first principles. If any of them is new, say so before you build, and we will walk through it before the assignment.
