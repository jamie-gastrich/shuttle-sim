# Lesson 17: Hints (nudge, direction, near-solution)

**Milestone:** M5 · **Lesson:** 17 · **Use only when stuck.**

These hints are in three tiers. Open the smallest tier that helps. Do not copy. Learn the reason.

## Tier 1: Nudge

- **1.A Requeue edge.** `FleetTask` already has other transitions (e.g., `Start`, `Complete`, `Fail`). Look at them. Notice the pattern: `EnsureInOrder`, a guard, mutate fields, update `_lastEventAt`. The guard is the only difference.
- **1.B Unstarted only.** The run loop case is "fault at StartMove" — the task is still `Assigned`. If a shuttle might be carrying a pallet, you cannot blindly requeue — so `Assigned` is the only allowed source. Also `Failed` is terminal in this lifecycle (no reopen in this slice).
- **1.C Persistence.** `TaskDispatcher` already saves after state changes (see `AssignAsync`, `StartAsync`, `CompleteAsync`, `FailAsync`). Follow that shape exactly.
- **2. Where to hook.** In `RunLoop.TickAsync`, after `shuttle.StartMove(...)`, before you assign into `_activeTasksByShuttle` and call `StartAsync`. That is the only place a synchronous fault from `StartMove` appears.
- **3. Release what?** `CommitMove` used the traffic controller to reserve. If the move never happens, release the reservation. Record 17: do **not** clear occupancy. Also the traffic controller's `Release(shuttleId)` frees segments owned by that shuttle (transit). That matches.

## Tier 2: Direction

- **FleetTask.Requeue:**
  - Add `public void Requeue(DateTime now) { EnsureInOrder(now); if (State != TaskState.Assigned) throw new TaskRejectedException("Cannot requeue a task that is not Assigned."); AssignedShuttleId = null; State = TaskState.Queued; _lastEventAt = now; }`
  - `TaskRejectedException` is in `FleetCore`/exceptions (see existing usage in tests).
- **TaskDispatcher.RequeueAsync:**
  - `public async Task RequeueAsync(string taskId, DateTime now, CancellationToken cancellationToken) { var task = _board.Get(taskId); task.Requeue(now); await _repository.SaveAsync(task, cancellationToken); }`
  - Follow `FailAsync`/`CompleteAsync` return/throw shape (no try-catch to hide `TaskRejectedException`).
- **RunLoop.TickAsync fault branch:**
  - After `shuttle.StartMove(task.ToLane, _currentTime);` check: `if (shuttle.State == ShuttleState.Fault) { await _dispatcher.RequeueAsync(assigned.TaskId, _currentTime, cancellationToken); _traffic.Release(shuttle.ShuttleId); continue; }`
  - The `assigned` variable is the `ShuttleTaskAssignment` from `CommitMove` (when it succeeded). You already have `task.ToLane` and `assigned.TaskId`. Also `_traffic` is the traffic controller; `_dispatcher` is TaskDispatcher.
- **Tests: Requeue from other states.** Use the task factory methods: `CreateQueued()`, `CreateAssigned()`, `CreateInProgress()`, `CreateDone()`, `CreateFailed()` as in `TaskLifecycleTests`. For `Done` and `Failed`, construct appropriately (you may need to Complete/Fail first). Follow existing patterns.
- **RunLoopReassignmentTests: block a lane.** Lesson 16 pattern: `rack.MarkBlocked("A01-L03")`. Set up two shuttles, one task to a lane beyond the block. The first shuttle will hit the blocked lane at StartMove and fault; the second must finish it. Use `RunLoop` with real in-memory collaborators.

## Tier 3: Near-solution (structural only, do not copy full tests)

- **Minimal fault branch sketch:**
  ```csharp
  // inside the idle-shuttle loop, after CommitMove
  var ok = _board.CommitMove(assigned, out var task);
  shuttle.StartMove(task.ToLane, _currentTime);
  if (shuttle.State == ShuttleState.Fault)
  {
      await _dispatcher.RequeueAsync(assigned.TaskId, _currentTime, cancellationToken);
      _traffic.Release(shuttle.ShuttleId);
      continue;
  }
  // persist InProgress and StartAsync...
  ```
- **Dispatcher RequeueAsync sketch:**
  ```csharp
  public async Task RequeueAsync(string taskId, DateTime now, CancellationToken cancellationToken)
  {
      var task = _board.Get(taskId);
      task.Requeue(now);
      await _repository.SaveAsync(task, cancellationToken);
  }
  ```
- **FleetTask.Requeue sketch:**
  ```csharp
  public void Requeue(DateTime now)
  {
      EnsureInOrder(now);
      if (State != TaskState.Assigned)
          throw new TaskRejectedException("Cannot requeue a task that is not Assigned.");
      AssignedShuttleId = null;
      State = TaskState.Queued;
      _lastEventAt = now;
  }
  ```
- **Test idea (name only):** "A_faulted_shuttle_requeues_its_task_and_another_shuttle_finishes_it" — arrange 2 shuttles at positions, mark lane blocked, enqueue task, run loop until done; assert done. Release: check `reservations.SegmentsOwnedBy(faultedId)` empty. Occupancy: `occupancy.GetOccupiedLane(faultedId)` unchanged. Not InProgress: final task state is `Done`.

**Remember:** This is a toy/structural aid. The assignment is about the real M5 slice. Do not turn this into the assignment solution. The lesson already gives the rationale (decisions 25, 17). Use only if blocked.