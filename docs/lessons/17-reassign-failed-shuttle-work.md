# Lesson 17: Reassigning a failed shuttle's work

**Milestone:** M5 (Faults and recovery) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 16: The blocked-path fault and the second alarm source](16-blocked-path-fault-and-alarm.md) · **Decision:** [record 25](../decisions/25-reassign-failed-shuttle-work.md); builds on [record 23](../decisions/23-blocked-path-fault-and-second-alarm.md), [record 17](../decisions/17-occupancy-and-fault-reservations.md), [record 11](../decisions/11-task-lifecycle-and-queue.md), [record 19](../decisions/19-run-loop-wiring-and-concurrency.md), [record 15](../decisions/15-m3-end-to-end-assignment-and-restart.md)

**Concepts this assignment requires:**

- [ ] **A task can go back to the queue (the `Requeue` transition)** — the task lifecycle adds an edge back to `Queued` from `Assigned`. A task that never started returns to the queue; `InProgress`/`Done`/`Failed` cannot be requeued. Taught in [1) The requeue transition](#1-the-requeue-transition). Fixed by [record 25 §2](../decisions/25-reassign-failed-shuttle-work.md).
- [ ] **The run loop reassigns a faulted shuttle's work** — after `StartMove`, if the shuttle is in `Fault`, do not start the task; requeue it and free the unstarted reservation. Taught in [2) The run loop reassigns](#2-the-run-loop-reassigns). Fixed by [record 25 §3](../decisions/25-reassign-failed-shuttle-work.md).
- [ ] **Free the route the faulted shuttle never used** — the transit reservation for a move that never began must be released; occupancy (the physical lane) stays. Taught in [3) Freeing an unstarted route](#3-freeing-an-unstarted-route). Fixed by [record 25 §4](../decisions/25-reassign-failed-shuttle-work.md).

Three new concepts. That is the cap.

**Not in this lesson:** mid-move faults (shuttles fault while `InProgress`, possibly carrying a pallet) — that is a later M5 slice; reopening `Failed` tasks (reassignment from `Failed`); operator-acknowledged recovery (a later slice); traffic control honoring blocked lanes (record 23 §10) — the alarm exists, routing may still use blocked lanes; the MQTT serve loop path (reassignment here is in the run loop, the pure decision core that is exercised by tests; wiring to the live MQTT path is a later slice, matching M4's run-loop deferral).

**The end-product fact:** the run loop currently leaves a task `InProgress` when `StartMove` faults, holds the reservation, and stalls. This lesson fixes that.

---

## Where this fits

M5 includes *"Reassign a failed shuttle's work."* Lesson 16 gave us a fault signal (a shuttle in `Fault` with `blockedPath`). When `StartMove` faults synchronously, the task is still `Assigned` (not yet `InProgress` in the sense of having started successfully). The work never began, so it can safely return to `Queued` for another shuttle.

The run loop is the fleet manager's decision core: it checks the gate, assigns tasks, commits reservations, calls `StartMove`, and starts/completes tasks. It is driven directly by tests (no MQTT/broker required), and the serve loop is the heartbeat/alarm path. This reassignment lives in the run loop.

---

## Verification table

| Claim | How I checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| Baseline pure suite green | `/tmp/opencode/l17base`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, 8 `Passed!`, 0 `Failed!`, **124** tests (FleetCore 18, FleetManager 46, ShuttleCore 29, ShuttleSim 7) |
| Current bug exists | Scratch copy probe test with 2 shuttles, block at A01-L03, task to A01-L04 | `AllDone=False` after 10 ticks; task `InProgress`, assigned to SH-01; SH-01 `Fault` at A01-L01; SH-02 `Idle`; SH-01 holds reservations [A01-L01..A01-L04]; route for SH-02 not free |
| Fix restores progress | Same scratch copy after adding `Requeue`, `RequeueAsync`, fault branch in RunLoop | `AllDone=True` in 2 ticks; task `Done`, assigned to SH-02; SH-01 `Fault` at A01-L01; SH-02 `Idle` at A01-L04; reservations released; route free |
| Requeue guards | Scratch test | Assigned→Queued, owner cleared; Queued/InProgress/Done/Failed → `TaskRejectedException` |
| TaskState has no `Cancelled` | Read `src/FleetCore/TaskState.cs` | 5 states: Queued, Assigned, InProgress, Done, Failed |
| No `Requeue` yet in source | `grep -rn '\bRequeue\b' src/FleetCore/FleetTask.cs src/FleetManager/TaskDispatcher.cs src/FleetManager/RunLoop.cs` | No matches |
| Run loop has no fault branch | Read `src/FleetManager/RunLoop.cs:103-107` | Unconditional: `shuttle.StartMove(...)`, add to `_activeTasksByShuttle`, call `StartAsync` |
| Record 17 forbids auto-release on fault | Read `docs/decisions/17-occupancy-and-fault-reservations.md` §4 | States release is not automatic on fault; release only for the unstarted transit reservation is consistent (no occupancy cleared) |
| Record 11 names Requeue for M5 | Read `docs/decisions/11-task-lifecycle-and-queue.md` §11 | "M5 adds reassignment → `Failed` stops being a sink; a new transition (for example `Requeue`)" |
| Record 23 names reassignment next | Read `docs/decisions/23-blocked-path-fault-and-second-alarm.md` §12 | Reassignment is the next slice after blocked-path fault |

Not yet verified: the production edits and the new tests (the student will make them).

---

## Options survey

### Survey 1: Which state(s) can be requeued?
| | A: Assigned only (chosen) | B: Assigned + InProgress | C: Failed → requeue |
|---|---|---|---|
| Matches run-loop case | **yes** — fault before StartAsync | also covers mid-move but needs pallet tracking | reopens terminal audit state |
| Safety (pallet not lost/duplicated) | **safe** — work never started | unsafe without pallet state | may be needed later but separate |
| Preserves `Failed` semantics | **yes** — failed means operator must look | blurs distinction | contradicts "failed is terminal now" |

**Decision 1:** `Requeue` from `Assigned` only (to `Queued`, owner cleared).

### Survey 2: Trigger location?
| | A: Inline in `RunLoop.TickAsync` (chosen) | B: Separate reconciliation pass | C: In `FleetServeLoop` (MQTT path) |
|---|---|---|---|
| Has board/active tasks/dispatcher | **yes** | needs shared state | **no** — serve loop lacks them |
| Testable without broker | **yes** — run loop is pure, driven directly | yes | no |
| Matches M4 architecture | **yes** — run loop is the decision core | possible but extra | wrong seam |

**Decision 2:** Trigger inline after `StartMove`; if `State == Fault`, requeue and release.

### Survey 3: What about the reservation?
| | A: Release only the transit reservation (chosen) | B: Keep it | C: Also clear occupancy |
|---|---|---|---|
| Correctness | **yes** — move never happened | blocks others | violates record 17 (occupancy stays) |
| Prevents stall | **yes** — requeued task routable | can stall | unsafe |
| Consistent with record 17 | **yes** — distinguishes occupancy vs reservation | no | no |

**Decision 3:** Call `_traffic.Release(shuttleId)` only; occupancy unchanged.

### Survey 4: Requeue vs Fail + reopen?
| | A: Requeue to Queued (chosen) | B: Fail the task now, reopen later | C: Leave as-is (no fix) |
|---|---|---|---|
| Fixes the stall | **yes** | no, not in this slice | no |
| Minimal, safe | **yes** for unstarted work | needs two transitions + recovery | no |
| Preserves audit | `Failed` unchanged | adds reopen transition | broken |

**Decision 4:** `Requeue` returns the task to `Queued` for automatic reassignment.

---

## The lecture

Today we are going to learn about three things: **the `Requeue` transition**, **the run-loop trigger for a faulted shuttle**, and **freeing the unstarted route reservation**.

### 1) The requeue transition (returning a task to the queue)

A **task** is work. A task starts in `Queued`. It becomes `Assigned` to a shuttle. Then `InProgress`. Then `Done` (or `Failed`). A terminal state is usually a sink.

But not every failure means "give up forever." Sometimes a shuttle fails **before** it does any work. The task was never touched. It can go back to the front of the work queue for another shuttle.

A real-world picture: you are at a checkout. You get in a line for checkout lane 2. The cashier there trips and must stop working. No one has rung up your items yet. A manager waves you over to lane 3 and you get back in the queue for lane 3 — same job, different cashier. No work was lost. If you had already been rung up and paid, you wouldn't be sent back to the line.

In code terms, the task was `Assigned` to a shuttle that then entered `Fault`. No `Start` (no successful start of the move) has occurred. So the task's "work done" is zero. It is safe to return to `Queued`, clear its owner, and let the dispatcher give it to someone else.

The `FleetTask` gains a new method:

```csharp
// src/FleetCore/FleetTask.cs (add this method)
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

- **Only from `Assigned`.** `Queued` is already in the queue. `InProgress` might mean the shuttle is carrying a pallet mid-move — blindly sending that task back would be dangerous (the pallet exists physically somewhere). `Done`/`Failed` are terminal. So the guard is strict: `Assigned` only. This pins the idea of "never started."
- **Clears the owner.** `AssignedShuttleId` becomes `null`. The task is no longer owned by the faulted shuttle.
- **Goes to `Queued`.** The task returns to the queue. The board will hand it out again later.
- **Preserves identity and other fields.** `TaskId`, `ToLane`, `CreatedAt` stay the same. `FailureReason` remains as it was (it is `null` when coming from `Assigned`, and we don't touch it). The single-row upsert in the repository means persistence keeps one row for this task, updated to `Queued` with `assigned_shuttle_id = NULL`.

The dispatcher gains a thin wrapper:

```csharp
// src/FleetManager/TaskDispatcher.cs (add this method)
public async Task RequeueAsync(string taskId, DateTime now, CancellationToken cancellationToken)
{
    var task = _board.Get(taskId);
    task.Requeue(now);
    await _repository.SaveAsync(task, cancellationToken);
}
```

**Where this is used later.** Any path that must return an unstarted assignment to the queue uses this. Mid-move faults are a later slice — they need different handling (the pallet's location matters). Recovery paths may also use this when a fault is cleared and work can restart.

### 2) The run loop reassigns

The run loop iterates shuttles. For an `Idle` shuttle, it tries to assign the next `Queued` task: checks the gate (`CanMove`), dispatches (`AssignNextAsync` which assigns the task), commits (`CommitMove` reserves the route), then calls `shuttle.StartMove`.

If the route is blocked (or the world is wrong), `StartMove` faults. In lesson 16, `Shuttle.StartMove` calls `RaiseFault(FaultReasons.BlockedPath, now)` and returns normally with `State == Fault`, position unchanged. The shuttle never leaves `Idle`-in-effect for movement and never becomes `Moving`.

But the run loop must react. As measured, without this branch the loop adds the task to `_activeTasksByShuttle` and calls `StartAsync`, leaving the task `InProgress` and the shuttle `Fault` — and stalls.

The fix is inline, right after `StartMove`:

```csharp
// src/FleetManager/RunLoop.cs (in TickAsync, inside the first foreach after CommitMove)
shuttle.StartMove(task.ToLane, _currentTime);
if (shuttle.State == ShuttleState.Fault)
{
    // The move never began. Give the work back to the queue and free the
    // route reservation that was made for a move that did not happen.
    await _dispatcher.RequeueAsync(assigned.TaskId, _currentTime, cancellationToken);
    _traffic.Release(shuttle.ShuttleId);
    continue;
}
_activeTasksByShuttle[shuttle.ShuttleId] = assigned;
// Persist InProgress...
await _dispatcher.StartAsync(assigned.TaskId, _currentTime, cancellationToken);
```

- **Detect after `StartMove`.** `StartMove` returns normally on fault (state changes), so the branch checks `shuttle.State == ShuttleState.Fault`.
- **Requeue instead of starting.** The task never started, so we requeue it (back to `Queued`, no owner). We do **not** add it to `_activeTasksByShuttle`, and we do **not** call `StartAsync`.
- **Release the reservation.** The `CommitMove` call reserved the route before `StartMove`. That reservation is for a move that never happened; we must free it so another shuttle can route to that work (see concept 3).
- **Continue.** The faulted shuttle stays in `Fault` (and will be skipped next ticks since its state is not `Idle`). Another `Idle` shuttle may pick up the requeued task in the same tick or a later tick.

This is synchronous, same-tick reassignment. The run loop's structure (one lock discipline for its own shared state) is unchanged.

**Where this is used later.** Any synchronous fault that occurs at `StartMove` (blocked path is the first) can flow through this branch. Mid-move faults are a different slice because the task is `InProgress` and the physical state differs.

### 3) Freeing an unstarted route

A transit reservation is a claim on a route for a move in progress. When `CommitMove` returns `true`, `TrafficController.TryReserveRoute` has reserved every segment in the route for that shuttle.

If the move never begins (the shuttle faults at `StartMove`), those segments must be freed. If we leave them reserved, the faulted shuttle holds space it never used. The requeued task may be unroutable by another shuttle, and the loop can stall — exactly what was measured.

Record 17 draws a careful line: a **faulted shuttle's occupancy** (its physical lane) stays — we do not clear it. A **transit reservation for a move that never started** is not occupancy; it is a spurious claim. We call `_traffic.Release(shuttleId)` to free all segments the transit controller has reserved for that shuttle. `SegmentReservations.Release(ownerId)` removes every segment owned by that owner. This frees only the transit reservation; it does not touch the occupancy map.

So the call is correct and consistent: no automatic release of physical occupancy, but we do release a reservation that was never consumed.

**Where this is used later.** Any case where a move is reserved but never completed/started and the shuttle faults requires freeing the unstarted reservation. The exact rule for mid-move faults differs, but this slice establishes the principle: release what was claimed for the move that didn't happen.

---

## Recap in plain words

- **Requeue** means "send this task back to the queue." Only tasks that were `Assigned` (never started) can be requeued. The owner is cleared, state becomes `Queued`.
- **The run loop reassigns.** After `StartMove` faults, it requeues the task, releases the reservation, and continues — so another shuttle can do the work.
- **Free the unstarted route.** A reservation for a move that never began is released; the faulted shuttle's physical lane (occupancy) stays put.

---

## Worked example (toy, not the assignment)

This toy mirrors the scenario: two workers, one blocked lane, one task. Worker A faults before starting; the task is requeued, the reservation released, worker B does the work. It's a toy — do not copy to `src/`.

Everything below ran in `/tmp/opencode/l17toy`.

```csharp
using System;
using System.Collections.Generic;

class Worker { public string Id=""; public int Lane; public bool Idle=true; }

class Program
{
    static string LaneRange(int from, int to) {
        var s = Math.Min(from,to);
        var e = Math.Max(from,to);
        return "[" + s + ".." + e + "]";
    }

    public static void Main()
    {
        Console.WriteLine("=== Lesson 17 toy: reassign a faulted shuttle's work ===");
        var blocked = new HashSet<int> { 3 };

        bool RouteBlocked(int from, int to) {
            var s = Math.Min(from,to);
            var e = Math.Max(from,to);
            for (int i=s; i<=e; i++) if (blocked.Contains(i)) return true;
            return false;
        }

        var reservations = new HashSet<int>();
        void ReserveRoute(int from, int to) {
            var s = Math.Min(from,to);
            var e = Math.Max(from,to);
            for (int i=s; i<=e; i++) reservations.Add(i);
        }
        void Release(int from, int to) {
            var s = Math.Min(from,to);
            var e = Math.Max(from,to);
            for (int i=s; i<=e; i++) reservations.Remove(i);
        }
        bool IsRouteFree(int from, int to) {
            var s = Math.Min(from,to);
            var e = Math.Max(from,to);
            for (int i=s; i<=e; i++) if (reservations.Contains(i)) return false;
            return true;
        }

        var queue = new Queue<(string id, int dest)>();
        queue.Enqueue(("task-1", 4));

        var a = new Worker { Id="A", Lane=0 };
        var b = new Worker { Id="B", Lane=5 };
        var workers = new[] { a, b };

        for (int tick = 1; tick <= 3 && queue.Count>0; tick++) {
            foreach (var w in workers) {
                if (!w.Idle) continue;
                if (!queue.TryPeek(out var task)) break;
                if (!IsRouteFree(w.Lane, task.dest)) { Console.WriteLine($"[{tick}] {w.Id} cannot reach lane{task.dest}: route overlaps reserved segments"); continue; }
                ReserveRoute(w.Lane, task.dest);
                Console.WriteLine($"[{tick}] {w.Id} starts move to lane{task.dest} (reserves {LaneRange(w.Lane, task.dest)})");
                if (RouteBlocked(w.Lane, task.dest)) {
                    Console.WriteLine($"[{tick}] {w.Id} detects BLOCKED on route -> FAULT");
                    Console.WriteLine($"[{tick}]  -> requeue {task.id}: Queued (owner cleared); {w.Id} stays at lane{w.Lane}");
                    Console.WriteLine($"[{tick}]  -> release {LaneRange(w.Lane, task.dest)}");
                    var t = queue.Dequeue();
                    queue.Enqueue(t);
                    Release(w.Lane, task.dest);
                    continue;
                }
                var t2 = queue.Dequeue();
                w.Idle = false;
                w.Lane = task.dest;
                Console.WriteLine($"[{tick}] {w.Id} arrives at lane{task.dest}");
            }
            if (queue.Count == 0) break;
        }
        Console.WriteLine($"queue empty? {queue.Count==0}; B is at lane {b.Lane}");
    }
}
```

Output (real run):
```text
=== Lesson 17 toy: reassign a faulted shuttle's work ===
[1] A starts move to lane4 (reserves [0..4])
[1] A detects BLOCKED on route -> FAULT
[1]  -> requeue task-1: Queued (owner cleared); A stays at lane0
[1]  -> release [0..4]
[1] B starts move to lane4 (reserves [4..5])
[1] B arrives at lane4
queue empty? True; B is at lane 4
```

What to notice:
- A reserves `[0..4]` (including the current lane). 
- On fault, the reservation is released (`release [0..4]`), so B's route `[4..5]` is free. 
- A stays at `lane0` (occupancy conceptually remains). 
- The task goes back to `Queued` (owner cleared) and B completes it.

---

## Say it out loud

Before starting the assignment, answer these without notes:

1. Why is `Requeue` allowed only from `Assigned`, not from `InProgress`? What could go wrong if we requeued a task while the shuttle is carrying a pallet?
2. `StartMove` returns normally when it faults. Why does the run loop check `shuttle.State == ShuttleState.Fault` after `StartMove`, instead of catching an exception?
3. What does `_traffic.Release(shuttleId)` free in this case? Why must we call it, and why must we **not** clear the faulted shuttle's occupancy?
4. If we did **not** release the reservation, what happens to the requeued task in the scenario where another shuttle might take it? (Think of the overlap.)
5. Why does the run loop `continue` (and not call `StartAsync`) when the shuttle faults at `StartMove`?

---

## The assignment

Build M5's reassignment slice: add `Requeue` to the task lifecycle, add `RequeueAsync` to the dispatcher, and make the run loop reassign a faulted shuttle's work by releasing the unstarted route reservation.

### Requirements

1. **`src/FleetCore/FleetTask.cs`** — add `public void Requeue(DateTime now)`.
   - Call `EnsureInOrder(now)`.
   - Guard: only allowed when `State == TaskState.Assigned`; otherwise throw `TaskRejectedException` with a clear message (e.g. `"Cannot requeue a task that is not Assigned."`).
   - Set `AssignedShuttleId = null`, `State = TaskState.Queued`, `_lastEventAt = now`.
   - Do not change `FailureReason`.

2. **`src/FleetManager/TaskDispatcher.cs`** — add `public async Task RequeueAsync(string taskId, DateTime now, CancellationToken cancellationToken)`.
   - Get the task from `_board.Get(taskId)`, call `task.Requeue(now)`, then `await _repository.SaveAsync(task, cancellationToken)`.
   - Throw `KeyNotFoundException` if the task is unknown (via `Get`), or propagate `TaskRejectedException` from `Requeue`.

3. **`src/FleetManager/RunLoop.cs`** — in `TickAsync`, after `shuttle.StartMove(task.ToLane, _currentTime)`, before `_activeTasksByShuttle[shuttle.ShuttleId] = assigned;` and `StartAsync`:
   - If `shuttle.State == ShuttleState.Fault`:
     - `await _dispatcher.RequeueAsync(assigned.TaskId, _currentTime, cancellationToken);`
     - `_traffic.Release(shuttle.ShuttleId);`
     - `continue;`
   - Do not call `StartAsync`, do not add to `_activeTasksByShuttle`.

4. **`src/FleetCore.Tests/TaskLifecycleTests.cs`** — add at least 4 facts (or a mix of facts/theories). Name them clearly:
   - `Requeue_from_assigned_returns_the_task_to_queued` — assert `State == TaskState.Queued` and `AssignedShuttleId == null`.
   - `Requeue_from_queued_is_rejected` — `TaskRejectedException`.
   - `Requeue_from_in_progress_is_rejected` — `TaskRejectedException`.
   - `Requeue_from_a_done_task_is_rejected` — `TaskRejectedException`.

5. **`src/FleetManager.Tests/TaskDispatcherTests.cs`** — add 2 facts:
   - `RequeueAsync_returns_the_task_to_queued_and_saves_it` — assign then requeue; assert persisted state is `Queued` with `AssignedShuttleId == null`; `repository.SaveCount` increases.
   - `RequeueAsync_for_a_task_that_is_not_assigned_throws_and_saves_nothing` — start from `Queued`, call `RequeueAsync`, expect `TaskRejectedException`; `SaveCount` unchanged.

6. **New file `src/FleetManager.Tests/RunLoopReassignmentTests.cs`** — add at least 4 facts:
   - `A_faulted_shuttle_requeues_its_task_and_another_shuttle_finishes_it` — 2 shuttles (one will fault), block a lane so SH-01 faults on StartMove, task goes back to queue, SH-02 finishes it; assert `AllDone`, task `Done`, SH-02's position correct.
   - `A_faulted_shuttle_releases_the_route_it_never_drove` — after fault, `reservations.SegmentsOwnedBy(faultedShuttleId)` is empty (or `traffic.IsRouteFree` is true for another shuttle).
   - `A_faulted_shuttle_keeps_its_lane_occupied` — `occupancy.GetOccupiedLane(faultedShuttleId)` is the faulted shuttle's position (unchanged).
   - `The_task_is_not_left_in_progress_when_the_shuttle_faults` — task `State != TaskState.InProgress`; assert `Queued` at the moment of reassignment outcome or that it's not `InProgress`.

All tests must be pure (no `Integration` trait). Use `InMemoryFleetTaskRepository`, real `RunLoop`, `RunLoopGate`, `OccupancyMap`, `TrafficController`, `TaskBoard`, `Dispatcher`, `Track`, `Rack`, `Shuttle`, `ShuttleTiming`.

7. **Do not change:** contracts, transport, protected M4/M5 files except what is specified; `FleetManager/Program.cs` wiring is unchanged (the run loop is test-only). `ContractVersion.Current` stays `3`. No `v4.md`.

---

## Acceptance criteria

```bash
# Phase 1 — files/methods exist
grep -qE 'public void Requeue' src/FleetCore/FleetTask.cs
grep -qE 'RequeueAsync' src/FleetManager/TaskDispatcher.cs
grep -qE 'shuttle\.State == ShuttleState\.Fault' src/FleetManager/RunLoop.cs
test -f src/FleetManager.Tests/RunLoopReassignmentTests.cs

# Phase 2 — build
cd src && dotnet build

# Phase 3 — pure suite: expected RC 0, 8 Passed!, 0 Failed!, 134 total
# (FleetCore.Tests 18→22 +4, FleetManager.Tests 46→52 +6 new total, ShuttleCore 29, ShuttleSim 7)
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > /tmp/l17test.txt 2>&1; RC=$?; echo "---RC:$RC---"
grep -E 'Passed!|Failed!' /tmp/l17test.txt
P=$(grep -c 'Passed!' /tmp/l17test.txt); F=$(grep -c 'Failed!' /tmp/l17test.txt)
TOTAL=$(grep -oE 'Passed: +[0-9]+' /tmp/l17test.txt | grep -oE '[0-9]+' | awk '{s+=$1} END{print s}')
echo "assemblies-passed=$P assemblies-failed=$F total=$TOTAL"
[ "$RC" -eq 0 ] && [ "$P" -eq 8 ] && [ "$F" -eq 0 ] && [ "$TOTAL" -eq 134 ] && echo "GATE OK" || echo "FAIL"

# Phase 4 — the new test names are present
cd src && dotnet test ShuttleFleet.slnx --list-tests | grep -q Requeue_from_assigned_returns_the_task_to_queued
dotnet test ShuttleFleet.slnx --list-tests | grep -q Requeue_from_queued_is_rejected
dotnet test ShuttleFleet.slnx --list-tests | grep -q Requeue_from_in_progress_is_rejected
dotnet test ShuttleFleet.slnx --list-tests | grep -q Requeue_from_a_done_task_is_rejected
dotnet test ShuttleFleet.slnx --list-tests | grep -q RequeueAsync_returns_the_task_to_queued_and_saves_it
dotnet test ShuttleFleet.slnx --list-tests | grep -q RequeueAsync_for_a_task_that_is_not_assigned_throws_and_saves_nothing
dotnet test ShuttleFleet.slnx --list-tests | grep -q A_faulted_shuttle_requeues_its_task_and_another_shuttle_finishes_it
dotnet test ShuttleFleet.slnx --list-tests | grep -q A_faulted_shuttle_releases_the_route_it_never_drove
dotnet test ShuttleFleet.slnx --list-tests | grep -q A_faulted_shuttle_keeps_its_lane_occupied
dotnet test ShuttleFleet.slnx --list-tests | grep -q The_task_is_not_left_in_progress_when_the_shuttle_faults

# Phase 5 — protected files unchanged (contracts/transport/protected run loop pieces not specified)
# (Only edit the three specified files; others unchanged.)
echo "DONE"
```

**Stretch goal:** Add one integration test (or a pure test variant) proving the persisted requeue survives a reload from the repository — e.g., assign a task, requeue it, load it back; assert `Queued` and `assigned_shuttle_id` null. Mark `[Trait("Category","Integration")]` if it hits the real database.

**Not in this assignment:** mid-move faults, reopening `Failed`, operator-acknowledged recovery, wiring reassignment into `FleetServeLoop` or `Program.cs`, honoring blocked lanes in traffic control.