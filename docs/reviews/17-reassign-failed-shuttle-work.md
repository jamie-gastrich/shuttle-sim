# Review 17 — Reassign a failed shuttle's work (M5, lesson 17)

**Reviewer:** Reviewer agent
**Verdict: pass**

Commit under review: `0198382` ("M5: Lesson 17 First Attempt"), base `9cc3cad`
("M5: Lesson 17"). The commit landed while the review was starting; the review
was run against `0198382` and the working tree was clean at review time.

**Scope:** lesson 17's seven requirements and its acceptance criteria,
[record 25](../decisions/25-reassign-failed-shuttle-work.md), and the end-product
spec's M5 line ("Reassign a failed shuttle's work"). This review modified no file
under `src/`.

**Process note:** scratch mutation builds ran in `/tmp/opencode/l17mut` and were
deleted. Broker and database were started only for the integration run and
brought down with `docker compose -f docker/compose.yml down`; no container,
broker, or `dotnet run` was left behind; `dotnet build-server shutdown` was run.

## Gate results (verbatim)

1. `cd src && dotnet build` — **RC 0**:
   ```
   Build succeeded.
       0 Warning(s)
       0 Error(s)
   ```
2. Pure suite (no database, no broker):
   ```
   cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
   ```
   `---RC:0---`, 8 `Passed!`, 0 `Failed!`, **134** tests total:
   ```
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7 - Contracts.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    29, Skipped:     0, Total:    29 - ShuttleCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9 - Messaging.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    22, Skipped:     0, Total:    22 - FleetCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2 - MqttMessaging.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7 - ShuttleSim.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6 - TrafficCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52 - FleetManager.Tests.dll (net10.0)
   ```
   `FleetCore.Tests` 18→22 (+4) and `FleetManager.Tests` 46→52 (+6): exactly the
   lesson-17 spread.
3. Integration suite (broker + Postgres up via `docker/compose.yml`):
   `dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"` → **RC 0**,
   3 `Passed!`, 0 `Failed!` (FleetManager.Tests 3). Services brought down after.
4. All ten anchored test names are present via `--list-tests` (the four
   `TaskLifecycleTests`, the two `TaskDispatcherTests`, and the four
   `RunLoopReassignmentTests`).

## Acceptance criteria

| Req | Status | Evidence |
|---|---|---|
| 1 `FleetTask.Requeue` | pass | `FleetTask.cs:90-98`: `EnsureInOrder`, guard `State != Assigned` → `TaskRejectedException`, clears `AssignedShuttleId`, sets `Queued`, updates `_lastEventAt`, leaves `FailureReason` alone. |
| 2 `TaskDispatcher.RequeueAsync` | pass | `TaskDispatcher.cs:42-47`: `_board.Get` → `task.Requeue` → `await _repository.SaveAsync`; no try/catch that would swallow the rejection. |
| 3 Run-loop fault branch | pass | `RunLoop.cs:104-111`: after `shuttle.StartMove(...)`, `if (shuttle.State == ShuttleState.Fault)` → `RequeueAsync`, `_traffic.Release`, `continue`; no `_activeTasksByShuttle` entry, no `StartAsync`. |
| 4 `FleetCore.Tests` +4 | pass | Four facts named exactly as the lesson asks; all green; each is load-bearing (mutations M3/M4 below). |
| 5 `FleetManager.Tests` dispatcher +2 | pass | `RequeueAsync_returns_the_task_to_queued_and_saves_it` and `RequeueAsync_for_a_task_that_is_not_assigned_throws_and_saves_nothing`; both green; save-count behaviour is mutation-proven (M5). |
| 6 `RunLoopReassignmentTests` +4 | pass | New file, four facts, all pure (no `Integration` trait), all exercise the real `RunLoop` with `MarkBlocked` (fault seam) and assert `gate.CanMove` is true first; tick-loop stopping conditions are correct (no `shuttles.All(Idle)` break). Load-bearing per M1/M2/M6/M7. |
| 7 No contract change | pass | `ContractVersion.Current == 3` (`src/Contracts/MessageTypes.cs:13`); `docs/contract/` unchanged (v1/v2/v3 only, no `v4.md`); the commit touches only the three product/test files and the hints doc — `Contracts`, `Messaging`, `MqttMessaging`, `FleetServeLoop`, `Program.cs` are untouched. |

Scope is right: only `Assigned` tasks can be requeued; `InProgress`/`Done`/`Failed`
are rejected; mid-task/carrying-pallet reassignment is explicitly deferred by
record 25 and is not smuggled in. Decision logic stays pure (`FleetTask.Requeue`),
the trigger is the I/O-free run loop, and the save seam is the dispatcher.

## Top issues (max 5), ranked

1. **consider — `A_faulted_shuttle_keeps_its_lane_occupied` cannot fail under any
   mutation of the current product code.** The test asserts
   `occupancy.GetOccupiedLane("SH-01") == "A01-L01"`, a value the arrange set,
   and the fault branch never touches `OccupancyMap` (there is no
   `Clear`/`Remove` on it). I removed the whole fault branch (M1), the `Release`
   (M2), the requeue (M6), and the `continue` (M7), and this test stayed green
   every time. It is still worth keeping as the recorded pin of record 17's
   "occupancy stays" invariant — a future fix that calls an occupancy clear on
   fault is exactly what it would catch — but the owner should know it is a
   *guard*, not mutation-proven coverage today, and not count it as one. The
   lesson asked for this test, so this is not the owner's defect; I note it for
   the professor too.

2. **consider — `Assert.NotNull(task);` in
   `RequeueAsync_returns_the_task_to_queued_and_saves_it` is tautological.**
   `task` is a local created three lines earlier, so the assertion can never
   fail. Worse, the test's state assertions (`State == Queued`,
   `AssignedShuttleId == null`) read back the same in-memory `FleetTask` the
   dispatcher mutated, because `InMemoryFleetTaskRepository.Upsert` stores the
   reference and `GetAsync` returns it. The one assertion in that test with real
   content is `Assert.Equal(2, repository.SaveCount);` — it is the only proof
   that `RequeueAsync` saved at all (and M5 confirms it goes red when the save is
   removed). Delete the `NotNull` line; if the owner wants a persistence claim
   that means something, the lesson's stretch goal (real DB reload) is the honest
   way to make it, since a reload through this fake is tautological by
   construction. The peer `FailAsync` test uses `repository.GetAsync(...)` and is
   equally tautological for the same reason — a pre-existing pattern, not new
   here.

3. **consider — the fault branch requeues before it releases.** `RunLoop.cs:108-109`
   calls `await _dispatcher.RequeueAsync(...)` and only then
   `_traffic.Release(shuttle.ShuttleId)`. If the repository save throws, the
   exception propagates (as decision 25 and record 15 intend, matching the
   `CompleteAsync` comment at `RunLoop.cs:134`), but the unstarted transit
   reservation is left held for that tick and the loop dies with the shuttle
   still faulted. This is within decision 25's stated sequential stance and is
   not a blocker, but it is the kind of ordering question recovery (the next M5
   slice) will have to answer: release is cheap and cannot fail, so releasing
   before the save would at least not strand the route. Worth a line in the next
   decision record.

4. **consider — two small test smells in the new file.** `RunLoopReassignmentTests`
   tests 2 and 3 declare `var result = new RunResult(0, false);` and assign it on
   every tick but never read it — the loop's stopping condition is
   `shuttles[0].State != ShuttleState.Fault`, so `result` is dead. And the comment
   on `RunLoop.cs:113` was shortened from the informative
   `// Persist InProgress; the active-task map is the in-flight set used by IsDone.`
   to `// Persist InProgress...` — the ellipsis is copied from the lesson sketch;
   the original sentence explained what `_activeTasksByShuttle` is for and was
   worth keeping.

5. **consider — cosmetic.** Missing final newlines at the end of
   `FleetTask.cs`, `TaskDispatcher.cs`, `TaskLifecycleTests.cs`, and
   `RunLoopReassignmentTests.cs`; a doubled blank line after the occupancy loop
   in each new test. `TaskDispatcherTests.cs` also renames the private fixture
   `BaseTime` → `Start` across five pre-existing tests, which is harmless but
   more churn than "add two facts". Nothing here affects behaviour.

## What is solid

- The three production changes are exactly the lesson's, in the right places and
  order. `Requeue` is a clean copy of the existing transition pattern
  (`EnsureInOrder` → guard → fields → `_lastEventAt`), and it correctly refuses
  everything but `Assigned`.
- The run-loop seam is right: the fault is detected *after* `StartMove` returns
  normally (state change, not an exception), the task is not added to
  `_activeTasksByShuttle`, `StartAsync` is not called, and `continue` skips the
  rest of that shuttle's turn. This is the escape from the measured stall.
- Decision C is implemented precisely: `_traffic.Release` frees the transit
  reservation only; `OccupancyMap` is never touched. Record 17's occupancy-vs-
  transit line is respected.
- The reassignment tests are real run-loop tests, not gate tests. They build a
  2×2 rack, block `A01-L02` with `rack.MarkBlocked` (so the gate allows the move
  and `StartMove` refuses — the lesson-16 seam), assert
  `gate.CanMove("SH-01","A02-L01")` is true in the arrange, and then drive
  `RunLoop.TickAsync` directly. Tests 1 uses `!result.AllDone`, tests 2/3 stop on
  `shuttles[0].State == Fault`, test 4 is a single tick — none of them copied the
  stress test's `shuttles.All(Idle)` break, which would never fire because SH-01
  ends `Fault`.
- Contract discipline is intact: no new message kind, no version bump, no
  `v4.md`, no transport or serve-loop edit.

## Explain-it check

Answer these out loud or in writing before the next lesson. They are not
answered here.

1. `FleetTask.Requeue` allows the transition only from `Assigned` and clears
   `AssignedShuttleId`. Walk the run-loop tick that triggers it: at the moment
   `StartMove` faults, which task state is it in and why is that "work never
   started"? Then explain what physical risk the `InProgress` guard avoids, and
   what would have to be true about pallet tracking before you could relax it.

2. The run loop detects the fault with `if (shuttle.State == ShuttleState.Fault)`
   after `StartMove` instead of catching an exception, while an *unknown lane* in
   `StartMove` throws `ShuttleRejectedException`. Why is a blocked route modeled
   as a state transition rather than a rejected command, and how does that choice
   make same-tick reassignment possible? What would break if `StartMove` threw on
   a blocked route instead?

3. The fault branch calls `_dispatcher.RequeueAsync(...)` then
   `_traffic.Release(shuttle.ShuttleId)`, but never `OccupancyMap`. Distinguish
   the transit reservation from the occupancy entry, say exactly which test
   catches a missing `Release` and why (name both), and explain why clearing
   occupancy here would violate record 17.

Also study these lines/blocks; they look unclear, copied, or easy to gloss over:

- `src/FleetManager.Tests/RunLoopReassignmentTests.cs:86-90` and `:133-137`
  (tests 2 and 3): `var result = new RunResult(0, false);` is assigned every tick
  and never read. Say what the loop actually stops on and either use or delete
  `result`.
- `src/FleetManager.Tests/TaskDispatcherTests.cs:139-148`
  (`RequeueAsync_returns_the_task_to_queued_and_saves_it`): `Assert.NotNull(task);`
  cannot fail, and the state assertions read the same object the dispatcher
  mutated. Explain why `SaveCount` is the only meaningful assertion here and why
  a `repository.GetAsync` round-trip would also be tautological with this fake.
- `src/FleetManager.Tests/RunLoopReassignmentTests.cs:110-121`
  (`A_faulted_shuttle_keeps_its_lane_occupied`): walk the fault branch line by
  line and say which line could ever change `SH-01`'s occupancy. If none can,
  what wrong fix is this test guarding against, and why is "it stayed green under
  every mutation" not a contradiction?
- `src/FleetManager/RunLoop.cs:113`: the `// Persist InProgress...` comment. What
  did the previous comment say about `_activeTasksByShuttle`, and why does
  `IsDone()` care about that dictionary?
- `src/FleetCore/FleetTask.cs:90-98`: the `Requeue` method has no
  `// EnsureInOrder; must be Assigned; ...` comment while every sibling
  transition does. Cosmetic, but note the inconsistency.

## Revisit list

- A test name is a claim: `A_faulted_shuttle_keeps_its_lane_occupied` is a
  record-17 regression guard, not mutation-proven coverage. Know the difference
  before you lean on it.
- The in-memory repository aliases references (`Upsert` stores the object,
  `GetAsync` returns it). `SaveCount` is persistence evidence; a reloaded-state
  assertion through this fake is not. The real-DB reload is where persistence
  gets proven.
- Decision logic vs. I/O: `FleetTask.Requeue` pure, `RunLoop` is the trigger
  (broker/database-free), `TaskDispatcher` is the save seam. This is the shape
  every later slice should keep.
- Record 17's occupancy vs. transit distinction, and why only the transit claim
  is released on a fault that never moved the shuttle.
- Ordering under failure: which of two operations is safe to run first when the
  second can throw (this slice's requeue-then-release).

## Mutation results

Run in a throwaway copy under `/tmp/opencode/l17mut`; the workspace `src/` was
never modified. Baseline (unmutated) was RC 0, 8 `Passed!`, 134. Each mutation
was applied alone and the pure suite re-run.

| Mutation | Targeted test(s) | Result |
|---|---|---|
| M1 delete the whole fault branch in `RunLoop` | reassignment tests | **red:** tests 1, 2, 4 (3 red) |
| M2 remove `_traffic.Release` from the fault branch | release test | **red:** test 2 + test 1 (SH-02 cannot route; 2 red) |
| M3 allow `Requeue` from `InProgress` too | lifecycle + dispatcher guards | **red:** `Requeue_from_in_progress_is_rejected`, `Requeue_from_queued_is_rejected`, `RequeueAsync_for_a_task_that_is_not_assigned_throws_and_saves_nothing` (3 red) |
| M4 `Requeue` leaves `AssignedShuttleId` set | owner-cleared assertions | **red:** `Requeue_from_assigned_returns_the_task_to_queued`, `RequeueAsync_returns_the_task_to_queued_and_saves_it` (2 red) |
| M5 `RequeueAsync` skips `SaveAsync` | dispatcher save test | **red:** `RequeueAsync_returns_the_task_to_queued_and_saves_it` (1 red) |
| M6 fault branch releases but does **not** requeue | reassignment tests | **red:** tests 1, 4 (2 red) |
| M7 fault branch omits `continue` | reassignment tests | **red:** all 4 (isolated run) |

Every new behaviour the lesson added is guarded: removing the branch, the
`Release`, the requeue, the `continue`, the guard, the owner-clear, or the save
each turns tests red. The single gap is that `A_faulted_shuttle_keeps_its_lane_occupied`
was green in every row above (issue 1) — it pins an invariant the current API
cannot violate, which is fine as a guard but should not be reported as proven.

## End-product check

This slice closes the specific M5 stall record 25 measured: a shuttle that faults
at `StartMove` (blocked path) no longer strands its task `InProgress` holding a
transit reservation. The task returns to `Queued` for automatic reassignment, the
unstarted route is freed, and the faulted shuttle's occupancy stays put — exactly
the first half of M5's "reassign a failed shuttle's work", with no wire change, so
the contract stays v3 and the later dashboard path is undisturbed. The risk is
what is deliberately *not* here: the branch lives in the test-driven run loop and
is not yet wired into the live MQTT serve path (`Program.cs`/`FleetServeLoop`),
so in a real manager the task would still stall until that wiring slice; a restart
with a task left `Assigned` to a dead shuttle is still unreconciled; and mid-move
faults (a shuttle possibly carrying a pallet) still need their own recovery path.
That is why M5 stays **in progress** — operator-acknowledged recovery,
`palletNotDetected`, and the M5 done-when proof remain. None of those is a defect
in this slice.

## Note for the professor

- No lesson defect. Lesson 17's "Concepts this assignment requires" list has
  exactly three concepts, all taught, and all three map to what the owner built.
  The lesson stayed within its 3-concept cap.
- One clarification worth adding for the next student: requirement 6's third
  bullet (`A_faulted_shuttle_keeps_its_lane_occupied`) reads like a normal
  behaviour test, but with `OccupancyMap`'s current API it cannot be made red by
  any mutation, because the fault path never touches occupancy. Say explicitly
  that it is a record-17 regression guard and not mutation-provable yet, so
  students don't mistake a green guard for proven coverage. (I will also carry
  this into the M5 verification note.)
- The lesson's hints correctly warned about the `shuttles.All(Idle)` break and the
  `MarkBlocked`-vs-occupancy seam; the owner followed both. Good hint quality.

## README

A lesson-17 slice note was appended to the **M5** (in progress) entry in the
README's Verification notes, matching the convention recorded for lessons 13–16.
The milestone table is untouched and M5 stays `in progress` — reassignment is one
part of the milestone, and operator-acknowledged recovery and `palletNotDetected`
remain, as does the M5 done-when proof.
