# Hints — Lesson 29: Traffic control under the dashboard

Open these only when stuck. Try the nudge first.

## Tier 1 — Nudge

- The pause state is a `HashSet<string>` of shuttle ids. Where should it live so one object answers "may this shuttle move?"?
- `CanMove` already returns `false` for several reasons. Add one more reason at the top, before any lane or route work.
- The move-completion loop calls `shuttle.CompleteMove`. How do you skip a shuttle this tick without removing it from the loop?

## Tier 2 — Direction

- Put the set in `RunLoopGate` (not a new class). Add `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`. The set starts empty.
- In `CanMove`, the first statement should be `if (IsPaused(shuttleId)) return false;`. Put it before `Occupancy.GetOccupiedLane`.
- In `RunLoop.TickAsync`, find the `foreach (var shuttle in _shuttles)` loop that handles `ShuttleState.Moving`. Add `if (_gate.IsPaused(shuttle.ShuttleId)) continue;` right after the `ShuttleState.Moving` check.
- `PauseAll` takes `IEnumerable<string>`. Loop over it and `Add` each id. `ResumeAll` loops and `Remove`s.
- Do not change `TaskBoard`, `FleetTask`, the contract, or `TaskExecutionLoop`. The gate's pause set is independent.

## Tier 3 — Near-solution

The gate changes (in `src/FleetManager/RunLoopGate.cs`):

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

And at the top of `CanMove`:

```csharp
if (IsPaused(shuttleId))
{
    return false;
}
```

The `RunLoop` change (in `src/FleetManager/RunLoop.cs`), inside the moving-shuttle loop, right after the `ShuttleState.Moving` check:

```csharp
if (_gate.IsPaused(shuttle.ShuttleId))
{
    continue;
}
```

A test sketch for the freeze behaviour:

```csharp
var gate = new RunLoopGate(occupancy, track, traffic);
// ... set up shuttles, board, dispatcher, loop ...
// start a move on tick 0
await loop.TickAsync(cts.Token);
// pause mid-move
gate.Pause("SH-01");
// run several ticks; shuttle must stay Moving, task stays InProgress
for (var i = 0; i < 5; i++) await loop.TickAsync(cts.Token);
Assert.Equal(ShuttleState.Moving, shuttles[0].State);
Assert.Equal(TaskState.InProgress, board.Get("task-1").State);
// resume and finish
gate.Resume("SH-01");
for (var i = 0; i < 50 && !result.AllDone; i++)
    result = await loop.TickAsync(cts.Token);
Assert.Equal(TaskState.Done, board.Get("task-1").State);
```

If the test deadlocks because `IsDone()` stays false while paused, that is expected — cancel the token or resume first. Do not change `IsDone()`.