# Hints for lesson 12: The run loop, concurrency, and the gate-then-dispatcher sequence

**Decision:** [record 19](../decisions/19-run-loop-wiring-and-concurrency.md) — run loop wiring, concurrency, and the gate-then-dispatcher sequence.

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- **The run loop** is the caller that ties the gate and dispatcher together. It peeks at the next task, asks the gate (`CanMove`), assigns (`AssignNextAsync`), reserves (`CommitMove`), and starts the move (`shuttle.StartMove`). It is not decision logic — the gate and dispatcher stay pure.
- **Concurrency:** `OccupancyMap` and `SegmentReservations` are plain `Dictionary` fields. Two threads (the run loop and the status path) touch them at once. A `lock` makes multi-step operations atomic. `ConcurrentDictionary` does not fix the check-then-write race.
- **The gate sequence:** `CanMove` is a pure check (reads occupancy and transit reservations, writes nothing). `CommitMove` reserves the route. The run loop calls `CanMove`, then `AssignNextAsync`, then `CommitMove`. Nothing is reserved until the last step.
- **`IsRouteFree`** is a new method on `TrafficController`. It reads every segment's owner and returns `true` if every segment is free or owned by the requesting shuttle. It writes nothing.
- **The stress test** is the M4 done-when: four or more shuttles, overlapping routes, no collisions, no permanent deadlock. Use a `CancellationTokenSource` with a timeout so a deadlock fails the test instead of hanging.
- **Do not change** `TaskDispatcher`, `Program.cs`, the contract, or the Dockerfile.

---

## Tier 2 — direction

1. **`IsRouteFree`.** Add to `src/TrafficCore/TrafficController.cs`. Get the route via `track.Route(fromLane, toLane)`. Return `true` if every segment's `OwnerOf` is `null` or equals `shuttleId`. Pure read, no writes.
2. **Split the gate.** Modify `src/FleetManager/RunLoopGate.cs`. `CanMove` gets the position, gets the route, checks `IsOccupiedByOthers`, checks `IsRouteFree`. Writes nothing. `CommitMove` gets the position, calls `TryReserveRoute`, returns `decision.Granted`.
3. **Add `lock` to `OccupancyMap`.** Add `private readonly object _lock = new();`. Wrap every public method body in `lock (_lock) { ... }`. The dictionary field stays.
4. **Add `lock` to `SegmentReservations`.** Same pattern. Add `private readonly object _lock = new();`. Wrap every public method body.
5. **The run loop.** Add `src/FleetManager/RunLoop.cs`. Constructor takes `OccupancyMap`, `Track`, `TrafficController`, `RunLoopGate`, `TaskBoard`, `TaskDispatcher`, `IReadOnlyList<Shuttle>`, `DateTime startTime`, `TimeSpan tickInterval`. `RunAsync` loops: for each idle shuttle, peek at `board.NextQueued()`, check `gate.CanMove`, call `dispatcher.AssignNextAsync`, call `gate.CommitMove`, call `shuttle.StartMove`. Advance `now` by `tickInterval`. For each moving shuttle, try `shuttle.CompleteMove(now)`; on success, call `occupancy.SetOccupiedLane` and `traffic.Release`. Stop when `board.NextQueued()` is `null` and all shuttles are idle.
6. **The stress test.** Add `src/FleetManager.Tests/RunLoopStressTests.cs`. Build a real `Track` from `new Rack(4, 4, 1)`, a real `TrafficController`, an `OccupancyMap`, a `RunLoopGate`, a `TaskBoard`, a `TaskDispatcher` (with an in-memory repository), and four or more `Shuttle` objects. Create four or more tasks with overlapping routes. Run the loop with a `CancellationTokenSource` timeout. Assert `result.AllDone` is `true`. After every tick, assert no segment has more than one owner.
7. **Run the phases in order.** The counts are gated, not admired: Phase 4 expects `RC 0`, **8** `Passed!`, **0** `Failed!`, **84** tests, `FleetManager.Tests` = 16. Phase 5 scans **every** test project for docker launches.

---

## Tier 3 — near-solution shapes

These are **shapes**, not the solution. You still write the bodies and the tests.

**`IsRouteFree` shape**
```csharp
public bool IsRouteFree(string shuttleId, string fromLane, string toLane)
{
    var route = _track.Route(fromLane, toLane);
    return route.All(segment =>
    {
        var owner = _reservations.OwnerOf(segment);
        return owner is null || string.Equals(owner, shuttleId, StringComparison.Ordinal);
    });
}
```

**Split gate shape**
```csharp
public bool CanMove(string shuttleId, string destinationLane)
{
    var currentLane = _occupancy.GetOccupiedLane(shuttleId);
    if (currentLane is null) return false;
    var route = _track.Route(currentLane, destinationLane);
    if (_occupancy.IsOccupiedByOthers(route, shuttleId)) return false;
    return _traffic.IsRouteFree(shuttleId, currentLane, destinationLane);
}

public bool CommitMove(string shuttleId, string destinationLane)
{
    var currentLane = _occupancy.GetOccupiedLane(shuttleId);
    if (currentLane is null) return false;
    return _traffic.TryReserveRoute(shuttleId, currentLane, destinationLane).Granted;
}
```

**Lock shape (both `OccupancyMap` and `SegmentReservations`)**
```csharp
private readonly object _lock = new();

public string? GetOccupiedLane(string shuttleId)
{
    lock (_lock)
    {
        // existing body
    }
}
```

**Run loop shape**
```csharp
public async Task<RunResult> RunAsync(CancellationToken cancellationToken)
{
    var now = _startTime;
    var ticks = 0;
    while (!cancellationToken.IsCancellationRequested)
    {
        var nextTask = _board.NextQueued();
        if (nextTask is null && _shuttles.All(s => s.State == ShuttleState.Idle))
            break;

        foreach (var shuttle in _shuttles)
        {
            if (shuttle.State != ShuttleState.Idle) continue;
            var task = _board.NextQueued();
            if (task is null) break;
            if (!_gate.CanMove(shuttle.ShuttleId, task.ToLane)) continue;
            await _dispatcher.AssignNextAsync(shuttle.ShuttleId, now, cancellationToken);
            if (!_gate.CommitMove(shuttle.ShuttleId, task.ToLane)) continue;
            shuttle.StartMove(task.ToLane, now);
        }

        now += _tickInterval;
        foreach (var shuttle in _shuttles)
        {
            if (shuttle.State != ShuttleState.Moving) continue;
            try
            {
                shuttle.CompleteMove(now);
                _occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
                _traffic.Release(shuttle.ShuttleId);
            }
            catch (ShuttleRejectedException)
            {
                // move not complete yet
            }
        }

        ticks++;
    }

    return new RunResult(ticks, _board.NextQueued() is null);
}
```

**Gate order reminder (say it out loud)**
> `CanMove` (pure check) → `AssignNextAsync` (assign) → `CommitMove` (reserve) → `StartMove` (move). Nothing is reserved until `CommitMove`.

**Test name the acceptance walks**
```
Four_or_more_shuttles_finish_with_no_collisions_and_no_deadlock
```

**Suite gate (the container's command)**
```bash
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# expects RC 0, 8 Passed!, 0 Failed!, 84 tests, FleetManager.Tests=16
```

**If stuck on the stress test:** Use `new Rack(4, 4, 1)` so there is room for four shuttles with overlapping routes. Give each shuttle a destination that requires passing through at least one other shuttle's lane. Use a `CancellationTokenSource` with a 5-second timeout so a deadlock fails the test instead of hanging. Assert `result.AllDone` is `true` and that no two shuttles ever occupy the same lane.

**Acceptance sanity:** `TaskDispatcher.cs` diff --quiet vs HEAD is true. Pure suite (Category!=Integration) stays green. No new message kinds, no contract version bump.

---

Stuck? These hints cover the key ideas without giving solution code. The acceptance criteria in the lesson file are the authoritative spec.
