# Hints: Lesson 34 — Blocked-lane removal and dispatch-pause semantics

Open these only when stuck. Try tier 1 first.

## Tier 1 — nudge

**Blocked lanes.**
- Where is the set of blocked lanes? It is a `HashSet<string>` inside `Rack`. Is there any method that removes from it?
- `Shuttle.StartMove` checks `_rack.IsRouteBlocked(_position, toLane)`. When that is true, what does the shuttle remember about *why*? Today, nothing. For recovery to clear the right lane, the shuttle must remember something.
- `Shuttle.Recover` already clears `_faultReason` and sets `_state`. What else must it do so the next move succeeds?

**Pause semantics.**
- Read `PauseState.Pause`. Which shuttle does it pause? Now read `TaskBoard.PauseAllExcept`. Which shuttle does its condition pause? Do they agree?
- Lesson 26 says the dispatch pause "stops dispatching new work to the named shuttle while letting others continue." Does the current condition do that?
- `ResumeAllExcept` must make the round-trip symmetric. If a task was paused and then requeued (so `AssignedShuttleId` became null), will your resume condition catch it?

## Tier 2 — direction

**Rack.**
Add a method next to `MarkBlocked`:

```csharp
public void UnmarkBlocked(string laneId)
{
    if (!_laneOrdinals.ContainsKey(laneId))
        throw new ArgumentException($"unknown lane {laneId}", nameof(laneId));
    _blockedLanes.Remove(laneId);
}
```

**Shuttle recording.**
Add a field:

```csharp
private readonly List<string> _blockedLanesOnFault = new();
```

In `StartMove`, in the blocked-route branch, fill it before `RaiseFault`. The simplest correct way is to walk `_rack.LaneId` and keep the lanes that are both blocked and on the route. You can check "on the route" with ordinals: a lane is on the route between `from` and `to` when its ordinal is between `Ordinal(from)` and `Ordinal(to)` inclusive. `Rack` does not expose `Ordinal` publicly, so either add a small public helper on `Rack` (for example `BlockedLanesOnRoute(string from, string to)`) or compare against `IsRouteBlocked` per lane. Keep it simple and testable.

**Shuttle recovery.**
In `Recover`, before clearing state:

```csharp
foreach (var lane in _blockedLanesOnFault)
    _rack.UnmarkBlocked(lane);
_blockedLanesOnFault.Clear();
```

Only lanes actually recorded are unmarked. A fault raised for another reason records none.

**TaskBoard.**
Change the condition in `PauseAllExcept` from "other shuttle's task" to "this shuttle's task":

```csharp
if (task.AssignedShuttleId == shuttleId)
    _pausedTaskIds.Add(task.TaskId);
```

And in `ResumeAllExcept`:

```csharp
if (task.AssignedShuttleId == shuttleId || task.AssignedShuttleId is null)
    _pausedTaskIds.Remove(task.TaskId);
```

Blank id still pauses or clears everything. Add a comment at the top of the class stating the settled meaning so the condition is not flipped again.

## Tier 3 — near-solution

**Rack.UnmarkBlocked:**

```csharp
public void UnmarkBlocked(string laneId)
{
    if (!_laneOrdinals.ContainsKey(laneId))
        throw new ArgumentException($"unknown lane {laneId}", nameof(laneId));
    _blockedLanes.Remove(laneId);
}
```

**Shuttle fields and StartMove:**

```csharp
private readonly List<string> _blockedLanesOnFault = new();

public void StartMove(string toLane, DateTime now)
{
    EnsureInOrder(now);
    EnsureState(ShuttleState.Idle, "start a move");
    if (!_rack.IsLane(toLane)) throw new ShuttleRejectedException($"unknown lane {toLane}");
    if (_rack.IsRouteBlocked(_position, toLane))
    {
        _blockedLanesOnFault.Clear();
        foreach (var lane in _rack.LaneId)
            if (_rack.IsBlocked(lane) && IsOnRoute(_position, toLane, lane))
                _blockedLanesOnFault.Add(lane);
        RaiseFault(FaultReasons.BlockedPath, now);
        return;
    }
    _moveToLane = toLane;
    _moveStartedAt = now;
    _state = ShuttleState.Moving;
    _lastEventAt = now;
}
```

You will need an `IsOnRoute` helper. The cleanest is to ask `Rack` for it. Add to `Rack`:

```csharp
public IEnumerable<string> BlockedLanesOnRoute(string fromLane, string toLane)
{
    var start = Math.Min(Ordinal(fromLane), Ordinal(toLane));
    var end = Math.Max(Ordinal(fromLane), Ordinal(toLane));
    foreach (var lane in _blockedLanes)
        if (Ordinal(lane) >= start && Ordinal(lane) <= end)
            yield return lane;
}
```

Then in `StartMove`:

```csharp
_blockedLanesOnFault.Clear();
_blockedLanesOnFault.AddRange(_rack.BlockedLanesOnRoute(_position, toLane));
```

**Shuttle.Recover:**

```csharp
public void Recover(DateTime now)
{
    EnsureInOrder(now);
    if (_state != ShuttleState.Fault)
        throw new ShuttleRejectedException($"cannot recover while {_state.ToWireName()}");
    foreach (var lane in _blockedLanesOnFault)
        _rack.UnmarkBlocked(lane);
    _blockedLanesOnFault.Clear();
    _state = ShuttleState.Idle;
    _faultReason = null;
    _lastEventAt = now;
}
```

**TaskBoard:**

```csharp
// Settled meaning (decision 43): PauseAllExcept(shuttleId) pauses dispatch for
// the named shuttle. A blank id pauses every queued task.
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

**Updated TaskBoardPauseTests expectations:**
- `Pausing_one_shuttle_leaves_unassigned_queued_tasks_dispatchable`: unchanged (unassigned tasks stay dispatchable).
- Replace `Pausing_one_shuttle_excludes_work_owned_by_that_shuttle` with `Pausing_one_shuttle_pauses_only_its_own_assigned_work`: pausing `SH-01` with an `SH-01`-owned task and an `SH-02`-owned task pauses only the `SH-01` task; after requeue, the `SH-01` task is absent from `Queued()` and the `SH-02` task is present.
- `Resuming_one_shuttle_restores_its_paused_work`: after pausing `SH-01`, requeuing its task, then resuming `SH-01`, the task is dispatchable again.
- `A_blank_shuttle_id_pauses_and_resumes_every_queued_task`: unchanged.
