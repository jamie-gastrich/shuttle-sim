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
- **The gate only reads two things.** `RunLoopGate.CanMove` looks at occupancy (who is parked where) and reservations (which routes are claimed). It never reads `rack.IsBlocked`. So an occupancy "blocker" makes `CanMove` return `false`, the loop hits `if (!_gate.CanMove(...)) continue;` at `RunLoop.cs` line 85, and `StartMove` is never called. No move, no fault, no reassignment. That is why the bodies copied from `RunLoopGateTests` cannot work here: they test the gate alone, with no `RunLoop`, no `TaskBoard`, and no fault at all.
- **Faults come from `MarkBlocked`, not occupancy.** A fault happens only when the gate **allows** the move and the shuttle refuses. `Shuttle.StartMove` checks `_rack.IsRouteBlocked(...)`. So block the lane with `rack.MarkBlocked("A01-L02")`. The gate ignores the block and returns `true`; `CommitMove` reserves the route; then `StartMove` faults. That is the lesson 16 seam. In the arrange you can even prove it: `Assert.True(gate.CanMove("SH-01", "A02-L01"));`.
- **The 2-shuttle world for tests 1–3.** This refines the "block a lane" bullet above: the block lane that works is `A01-L02`, not `A01-L03`. Use `new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 1)`. Lane ordinals become `A01-L01=0, A01-L02=1, A02-L01=2, A02-L02=3`. Call `rack.MarkBlocked("A01-L02")`. Put SH-01 at `A01-L01` (ordinal 0) and SH-02 at `A02-L02` (ordinal 3). Enqueue one task to `A02-L01` (ordinal 2). Set occupancy for both shuttles at their start lanes (`occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position)`) so the gate can find their routes. Do **not** add an occupancy blocker — it would stop the gate and hide the fault.
  - SH-01's route 0→2 crosses ordinal 1 (`A01-L02`), so it faults at `StartMove`.
  - SH-02's route 3→2 stays at ordinals 2–3, so it misses the block and can still finish the task.
- **The 1-shuttle world for test 4.** Same block and same task, but only SH-01 at `A01-L01`. No SH-02. One tick is enough: SH-01 faults, the task goes back to `Queued`, and no other shuttle exists to take it, so it stays `Queued`. That is the proof it is not left `InProgress`.
- **Pick the right tick-loop shape.**
  - **Shape A — run to completion.** `var result = new RunResult(0, false); for (var tick = 0; tick < 50 && !result.AllDone; tick++) result = await loop.TickAsync(cts.Token); Assert.True(result.AllDone, $"...");` Use for test 1.
  - **Shape B — run until the fault.** `for (var tick = 0; tick < 50 && shuttles[0].State != ShuttleState.Fault; tick++) await loop.TickAsync(cts.Token); Assert.Equal(ShuttleState.Fault, shuttles[0].State);` Use for tests 2 and 3. You only need the fault to have happened; you do not need the whole run.
  - **Shape C — exactly one tick.** `await loop.TickAsync(cts.Token);` Use for test 4.
  - All three wrap the loop with `using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));`.
- **Delete the copied break line.** `RunLoopStressTests` ends its loop with `if (board.NextQueued() is null && shuttles.All(s => s.State == ShuttleState.Idle)) break;`. Drop it. In the stress test every shuttle ends `Idle`; here SH-01 ends `Fault`, so that condition can never fire. (In the file right now that line is half-written as `shuttles.` and will not compile; remove the whole `if (...) { break; }`.) Let `!result.AllDone` and the `tick < 50` cap end the loop.

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
- **Fuller sketch of test 1 (`A_faulted_shuttle_requeues_its_task_and_another_shuttle_finishes_it`).** This expands the "name only" idea above. It is still structural — type it in and make sure you can explain every line. Do not paste it blind.
  - **First build the world.** A 2×2 rack gives the lane ordinals 0,1 / 2,3.
  ```csharp
  var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 1);
  rack.MarkBlocked("A01-L02");                 // block, NOT an occupancy entry
  var track = new Track(rack);
  var reservations = new SegmentReservations();
  var traffic = new TrafficController(track, reservations);
  var occupancy = new OccupancyMap();
  var board = new TaskBoard();
  var repository = new InMemoryFleetTaskRepository();
  var dispatcher = new TaskDispatcher(board, repository);
  var gate = new RunLoopGate(occupancy, track, traffic);
  ```
  - **Then place the shuttles and the task.** SH-01 crosses the block; SH-02 misses it.
  ```csharp
  var timing = new ShuttleTiming(TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero);
  var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
  var tickInterval = TimeSpan.FromSeconds(1);

  var shuttles = new[]
  {
      new Shuttle("SH-01", rack, "A01-L01", timing),  // ordinal 0 -> crosses the block
      new Shuttle("SH-02", rack, "A02-L02", timing),  // ordinal 3 -> misses the block
  };
  foreach (var shuttle in shuttles)
      occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);

  board.Add(new FleetTask("task-1", "A02-L01", start));

  Assert.True(gate.CanMove("SH-01", "A02-L01"));  // gate says yes; StartMove will fault
  ```
  - **Then run the loop and assert the outcome (Shape A).**
  ```csharp
  var loop = new RunLoop(occupancy, track, traffic, gate, board, dispatcher, shuttles, start, tickInterval);
  using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

  var result = new RunResult(0, false);
  for (var tick = 0; tick < 50 && !result.AllDone; tick++)
      result = await loop.TickAsync(cts.Token);

  Assert.True(result.AllDone, $"run loop stalled after {result.Ticks} ticks");
  Assert.Equal(TaskState.Done, board.Get("task-1").State);
  Assert.Equal("A02-L01", shuttles[1].Position);        // SH-02 did the work
  Assert.Equal(ShuttleState.Fault, shuttles[0].State);   // SH-01 stays Fault
  ```
  - **Why these lines.** `gate.CanMove` is `true` because the gate ignores blocked lanes. `CommitMove` then reserves SH-01's route. `StartMove` faults because the route crosses `A01-L02`. The run loop requeues the task and releases SH-01, so SH-02 can pick it up and finish it. The `Shuttle` class exposes `Position`, not `CurrentLane` (see `ShuttleCore/Shuttle.cs`). Do **not** assert every shuttle is `Idle`: SH-01 ends `Fault`, never `Idle`.
- **The other three tests: what to change.** Each must become a real run-loop test. Do not keep the `RunLoopGateTests` body (1-aisle/6-lane rack, two occupancy entries, `gate.CanMove(...)` then `Assert.False`): that body never builds a `RunLoop`, never adds a `TaskBoard` task, and never faults.
  - **Test 2 `A_faulted_shuttle_releases_the_route_it_never_drove`.** World: the same 2-shuttle world as test 1. Loop: Shape B (stop once SH-01 is `Fault`). Assertion: `Assert.Empty(reservations.SegmentsOwnedBy("SH-01"));`. Why: `CommitMove` reserved SH-01's route before `StartMove`; the fault branch calls `_traffic.Release("SH-01")`, which removes every segment owned by that shuttle. After the fault, SH-01 owns no segments. `SegmentsOwnedBy` returns `IReadOnlyCollection<string>`.
  - **Test 3 `A_faulted_shuttle_keeps_its_lane_occupied`.** World: the same 2-shuttle world as test 1. Loop: Shape B. Assertion: `Assert.Equal("A01-L01", occupancy.GetOccupiedLane("SH-01"));`. Why: record 17 says a faulted shuttle keeps its physical lane. The fault branch releases the transit reservation but never touches occupancy. `GetOccupiedLane` returns `string?`; compare it to the start lane you set in the arrange.
  - **Test 4 `The_task_is_not_left_in_progress_when_the_shuttle_faults`.** World: **one shuttle only** — SH-01 at `A01-L01`, `rack.MarkBlocked("A01-L02")`, task to `A02-L01`, and no SH-02. (If your test still has SH-02 in the array, remove it.) Loop: Shape C (a single tick). Assertions: `Assert.Equal(ShuttleState.Fault, shuttles[0].State);` and `Assert.Equal(TaskState.Queued, board.Get("task-1").State);`. Why: on the fault the run loop requeues the task instead of calling `StartAsync`. With no second shuttle, nothing picks it up, so it is still `Queued`. `Queued` (not `InProgress`, not `Failed`) is the proof the work was not left stranded.

**Remember:** This is a toy/structural aid. The assignment is about the real M5 slice. Do not turn this into the assignment solution. The lesson already gives the rationale (decisions 25, 17). Use only if blocked.