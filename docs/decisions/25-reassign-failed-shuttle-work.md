# Decision Record 25: Reassign a failed shuttle's work (requeue edge, run-loop trigger, unstarted reservation)

**Date:** 2026-10-16
**Milestone:** M5 (Faults and recovery) — lesson 17: reassigning a failed shuttle's work.
**Status:** Accepted. Append-only. Builds on [record 23](23-blocked-path-fault-and-second-alarm.md) (blocked-path fault signal exists), [record 17](17-occupancy-and-fault-reservations.md) (occupancy vs transit reservations; auto-release on fault forbidden), [record 11](11-task-lifecycle-and-queue.md) (task lifecycle), [record 15](15-m3-end-to-end-assignment-and-restart.md) (dispatcher/persistence), [record 19](19-run-loop-wiring-and-concurrency.md) (run-loop structure, concurrency discipline).
**Wire contract:** **no change, no version bump.** Reassignment is an internal fleet state change (task board + run loop). `ContractVersion.Current` stays `3`. `docs/contract/` and `src/Contracts/` are untouched.

---

## 1. What we are deciding

M5 says: *"Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges. Done when: every injected fault leads to the right alarm and the right recovery, and the saved state never loses or duplicates a pallet."*

Lesson 16 produced a real fault signal (a shuttle in `Fault`, visible via status and the `blockedPath` alarm). The current run loop does not react: when a shuttle faults at `StartMove` (blocked path), it leaves the assigned task `InProgress`, adds the task to `_activeTasksByShuttle`, and never completes it. The transit reservation for the unstarted move stays held. The loop stalls.

This record decides three things:
- **A.** Which states a task can be `Requeue`d from (transition shape).
- **B.** Where the reassignment trigger lives in the run loop.
- **C.** How the faulted shuttle's unstarted reservation is handled (occupancy vs transit).

---

## 2. Decision A: A task can return to `Queued` only from `Assigned` (the `Requeue` edge)

We need a transition to put a task back into the queue when a shuttle faults before starting. Two alternatives exist conceptually: requeue to `Queued` (preserves the task identity and ordering context), or mark `Failed` and later reopen. 

| | A: `Requeue` to `Queued` from `Assigned` only (chosen) | B: `Requeue` from `Assigned`, `InProgress`, and/or `Failed` | C: Fail the task, don't reopen now (M5 split) |
|---|---|---|---|
| Semantics | A task never started and the shuttle failed — it is still viable work. | Allows reassigning mid-move; dangerous if carrying pallet. | Preserves audit trail but loop stalls (no recovery). |
| Matches run-loop case | **Yes** — fault happens after `CommitMove` but before `StartAsync`, task still `Assigned`. | Over-general for this slice. | No, doesn't reassign. |
| Safety (pallet not lost/duplicated) | **Safe** — no pallet in motion. | Risky: a pallet in motion must not be blindly requeued. | Safe but incomplete. |
| Fits "Failed is terminal now" | **Yes** — `Failed` means operator must look; requeue means automatic reassignment of unstarted work. | Blurs distinction. | Yes. |

**Decision A:** Add `FleetTask.Requeue(DateTime now)` which allows transition **only from `TaskState.Assigned`**. On success: set `State = TaskState.Queued`, clear `AssignedShuttleId = null`, update `_lastEventAt = now`. Do **not** change `FailureReason`. `TaskRejectedException` if the state is not `Assigned`. States `Queued`, `InProgress`, `Done`, `Failed` cannot be requeued. This preserves `Failed` as a terminal category for genuinely failed work while allowing automatic reassignment when no work has started.

**Dispatcher:** `TaskDispatcher.RequeueAsync(string taskId, DateTime now, CancellationToken)` calls `task.Requeue(now)` and `await _repository.SaveAsync(task, cancellationToken)`. The single-row `INSERT ... ON CONFLICT (task_id) DO UPDATE` in the repository (record 15, 13) means the persisted row reflects `Queued` with `assigned_shuttle_id = NULL`; a crash between operations leaves the persisted state at the last committed save (the task remains `Queued` or `Assigned` as committed — this slice keeps operations sequential). The "saved state never loses or duplicates a pallet" constraint is satisfied for the unstarted case.

Rejected: allowing `InProgress` (mid-move). A shuttle that faults mid-move may be carrying a pallet; blindly requeuing the task to another shuttle could create a duplicate assignment or lose track of the physical pallet. That is a later M5 slice with its own record. Rejected: reopening `Failed` (terminal audit semantics).

---

## 3. Decision B: Reassign inline in the run loop, after `StartMove` detects a fault

We must trigger reassignment when the shuttle faults on `StartMove` (blocked path). Where?

| | A: Inline after `StartMove` in `RunLoop.TickAsync` (chosen) | B: A separate reconciliation pass over faulted shuttles | C: The MQTT serve loop (FleetServeLoop) |
|---|---|---|---|
| Has access to the run loop state | **Yes** — has `_activeTasksByShuttle`, `_traffic`, `_board`, `_dispatcher`. | Yes but needs a shared view. | **No** — `FleetServeLoop` has no `TaskBoard`, no `RunLoop`, no `_activeTasksByShuttle`. |
| Testable without MQTT/broker | **Yes** — the run loop is a pure class driven directly by tests. | Yes. | No. |
| Timing | Synchronous, same tick; avoids races. | Could miss or delay. | Asynchronous via messages. |
| Matches M4 architecture | **Yes** — record 19 established the run loop as the decision core; serve loop is the alarm/heartbeat path. | Possible but extra machinery. | Wrong seam. |

**Decision B:** In `RunLoop.TickAsync`, after `shuttle.StartMove(task.ToLane, _currentTime)` returns normally, check `shuttle.State == ShuttleState.Fault`. If true:
1. `await _dispatcher.RequeueAsync(assigned.TaskId, _currentTime, cancellationToken)` — put task back to `Queued`.
2. `_traffic.Release(shuttle.ShuttleId)` — free the unstarted route reservation (see Decision C).
3. `continue` — do not set `_activeTasksByShuttle[shuttle.ShuttleId] = assigned`, do not call `StartAsync`. The faulted shuttle is skipped (its `State` is `Fault`, so it never becomes `Moving`).

This happens in the same tick, before advancing time, and before the completion loop. It requires no new dependencies; the run loop already owns the dispatcher and traffic controller. The fault is detected inline, preserving the sequence: `CommitMove` reserves → `StartMove` may fault → requeue and release → let another shuttle pick up the task.

Rejected: placing this in `FleetServeLoop` (it lacks the task/board/active-task state). Rejected: a separate reconciliation pass (more state tracking, delays reassignment).

---

## 4. Decision C: Release the unstarted transit reservation; occupancy stays

When `CommitMove` succeeds, the transit route is reserved (record 16). If `StartMove` faults immediately, the shuttle never departed. Record 17 says: "A shuttle's last-known occupied position remains occupied... Declaring a shuttle lost/faulted does not automatically call `Release`." That principle holds: we do not touch occupancy. But the **transit reservation** for a move that never began is a different claim.

| | A: Call `_traffic.Release(shuttleId)` for the unstarted move (chosen) | B: Keep the transit reservation | C: Also clear occupancy |
|---|---|---|---|
| Correctness | **Yes** — the shuttle never used the route. The claim is spurious. | Blocks other shuttles from routing through the claimed segments (even though no move started). | Violates record 17: the shuttle's physical position is still its lane. |
| Consistency with record 17 | **Yes** — record 17 distinguishes occupancy (physical) from reservation (transit). Release on departure: no departure occurred. | Inconsistent with "transit reservation exists only while that move is in progress". | Violates record 17 §4. |
| Deadlock/stall | **Prevents stall.** The requeued task becomes routable by another shuttle. | Can leave the task stuck (as measured). | Unsafe. |

**Decision C:** Release only the transit reservation via `_traffic.Release(shuttleId)`. Do **not** modify the occupancy map. The faulted shuttle remains physically at its last known lane (occupancy unchanged), so no other shuttle can route through that lane. Only the segments held by the unstarted move reservation are freed. This is not "auto-release on fault" in the sense record 17 forbids — it is releasing a reservation that was never consumed, for a move that never left the source. The physical obstacle rule (faulted shuttle stays present) is preserved.

Rejected: keep the reservation (causes the requeued task to be unroutable in common cases). Rejected: clear occupancy (unsafe, contrary to record 17).

---

## 5. Options survey (why we rejected other paths)

**Survey 1: Which state(s) can be requeued?**
- A: Assigned only (chosen) — safe; matches the run-loop fault case; preserves `Failed` audit semantics.
- B: Assigned + InProgress — allows mid-move reassignment but risks losing/duplicating a pallet. Needs pallet-tracking (later slice).
- C: Failed → requeue — would reopen terminal failures. Requires operator-ack semantics.

**Survey 2: Trigger location?**
- A: Inline in RunLoop.TickAsync (chosen) — has all state, testable, synchronous, matches architecture.
- B: Separate reconciliation pass — extra state, timing uncertainty.
- C: FleetServeLoop (MQTT path) — lacks TaskBoard/active tasks; wrong seam.

**Survey 3: Reservation on unstarted fault?**
- A: Release transit reservation only (chosen) — frees route, keeps occupancy. Consistent with record 17.
- B: Keep reservation — blocks others; can stall.
- C: Release occupancy — unsafe (violates record 17).

**Survey 4: Requeue vs Fail?**
- A: Requeue (move back to Queued) (chosen) — automatic reassignment for unstarted work, no new "reopened failed" semantics.
- B: Fail the task and have a later "reopen" transition — also correct but creates two steps for the common case and requires recovery to "reopen".
- C: Leave as-is (Fail later) — loop never finishes, not a solution.

---

## 6. Verification table (measured)

All measurements were taken in scratch directories under `/tmp/opencode` and deleted afterwards. No file under `src/` was modified in the scratch copies used for measurement except transient probe edits that were removed.

| Claim | How checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| Baseline pure suite green | `/tmp/opencode/l17base`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, 8 `Passed!`, 0 `Failed!`, **124** tests (FleetCore 18, FleetManager 46, ShuttleCore 29, ShuttleSim 7) |
| Current behavior with blocked lane is a bug | Scratch copy with probe test (`L17ProbeTests`) simulating run loop with 2 shuttles, block at A01-L03, task to A01-L04 | **AllDone=False** after 10 ticks; task `InProgress`, assigned to SH-01; SH-01 `Fault` at A01-L01; SH-02 `Idle`; SH-01 holds reservations [A01-L01..A01-L04]; route for SH-02 not free |
| Fix: Requeue + release + continue | Same scratch copy after adding `FleetTask.Requeue`, `TaskDispatcher.RequeueAsync`, and run-loop fault branch | **AllDone=True** in 2 ticks; task `Done`, assigned to SH-02; SH-01 `Fault` at A01-L01; SH-02 `Idle` at A01-L04; reservations released; route free |
| Requeue guards | Scratch test (`L17GuardProbeTests`) | Assigned→Queued, owner cleared; Queued/InProgress/Done/Failed → `TaskRejectedException` |
| TaskState has 5 states, no `Cancelled` | Read `src/FleetCore/TaskState.cs` | States: Queued, Assigned, InProgress, Done, Failed; wire names match record 11 |
| `Requeue` does not exist yet in source | Grep for `Requeue` in `src/FleetCore` and `src/FleetManager` | No matches |
| Run loop has no fault-branch after StartMove | Read `src/FleetManager/RunLoop.cs:103-107` | Unconditional: `shuttle.StartMove(...)`, `_activeTasksByShuttle[...]`, `StartAsync` |
| Record 17 forbids auto-release on fault | Read `docs/decisions/17-occupancy-and-fault-reservations.md` | §4 explicit: "Declaring a shuttle lost/faulted does not automatically call Release" |
| Record 23 names reassignment as next | Read `docs/decisions/23-blocked-path-fault-and-second-alarm.md` §2,12 | Lists reassignment after blocked-path fault slice |
| Record 11 §11 names Requeue for M5 | Read `docs/decisions/11-task-lifecycle-and-queue.md` §11 | "M5 adds reassignment → `Failed` stops being a sink; a new transition (for example `Requeue`)..." |

Not yet verified: the test changes and production edits (the student will make them). This record specifies them.

---

## 7. What would change this choice

- **Mid-move fault / carrying-pallet case** arrives (shuttle faults while `InProgress`, possibly holding a pallet): would require allowing `Requeue` from `InProgress`? No. The correct response is different — do not blindly requeue. That means either (1) do not requeue; mark task appropriately, (2) create a new "held by physical shuttle" state, or (3) require operator recovery. Record 17 already says occupancy stays; the task may need to be marked `Failed` or moved to a different recovery path. New record, links back here.
- **Recovery that clears a fault** (M5 operator-acknowledged recovery) means a shuttle can leave `Fault` state. This reassignment slice does not add recovery; the faulted shuttle remains faulted. A later record changes the run loop's handling.
- **Traffic control starts honoring blocked lanes** (record 23 §10): the precondition that leads to fault at `StartMove` changes. The detection location (inline after `StartMove`) remains valid; the reason changes.
- **Restart must reconcile a task left `Assigned` to a dead shuttle**: M5 done-when mentions persistence. This slice assumes the shuttle is alive to report the fault. A reconciliation pass on restart is a later slice.
- **WMS integration** (M6) makes tasks richer (pallet transfers, not just moves): the `Requeue` semantics (only from `Assigned`, unstarted work) still hold if the unstarted phase is `Assigned`. If the task's phases change, this record is revisited.
- **Multi-instance fleet manager**: the in-process run loop becomes distributed; reassignment would move to a coordinator with a distributed lock. New record.

---

## 8. What this record unblocks

- Lesson 17 can assign the `Requeue` transition, `RequeueAsync`, and the run-loop fault branch with no unguided product choices.
- The measured bug (task stranded `InProgress`, loop stalls, reservation held) has a concrete fix that is testable in isolation.
- The M5 done-when gains a concrete reassignment case for an injected fault (blocked path) — the first step toward "the right recovery" in general.
- The next M5 slice (operator-acknowledged recovery) can build on the fact that faulted shuttles exist and their tasks can be handled explicitly.
- The single-row upsert persistence semantics (record 13) are preserved: the requeue is one save of the task's state.
