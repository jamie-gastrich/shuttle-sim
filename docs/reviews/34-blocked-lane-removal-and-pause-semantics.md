# Review: Lesson 34 — Blocked-lane removal and dispatch-pause semantics

**Milestone:** M7 (operator dashboard and diagnostics) · **Reviewed:** 2026-10-29
**Verdict:** **pass**

The implementer landed the two M7 behaviors the lesson asked for, mutation checks a–d are recorded (all red), `Rack.IsRouteBlocked` is deduplicated, and the code builds clean with 247 tests passing. Lesson-level review is complete. The milestone-level open item is the M7 full-recovery stretch scenario (fault → Pause → Acknowledge → Resume with timing), which remains unverified (service process not active, no injection script available). Milestone M7 stays `in progress`.

---

## Acceptance criteria checklist

| # | Criterion | Result and evidence |
|---|---|---|
| R1 | Blocked-lane removal | **Pass.** `Rack.UnmarkBlocked` mirrors `MarkBlocked` (unknown lane → `ArgumentException`) and removes from the blocked set. `Shuttle.Recover` unmarks `_blockedLanesOnFault` then clears the list, sets `Idle`, clears `_faultReason`. |
| R2 | Recording blocked lane(s) on faulted move | **Pass.** `Shuttle.StartMove` blocked branch clears `_blockedLanesOnFault`, `AddRange(_rack.BlockedLanesOnRoute(_position, toLane))`, then `RaiseFault`. Only blocked lanes inside the route range are recorded. |
| R3 | Dispatch-pause semantics | **Pass.** `TaskBoard.PauseAllExcept` pauses tasks whose `AssignedShuttleId == shuttleId` (blank → all); `ResumeAllExcept` clears marks for the named shuttle's tasks and unassigned tasks. `Queued()`/`NextQueued()` unchanged. |
| R4 | Required tests | **Pass.** All nine required test names exist and pass: `UnmarkBlocked_removes_a_previously_blocked_lane`, `UnmarkBlocked_for_an_unknown_lane_is_rejected`, `Recover_clears_the_blocked_lane_that_caused_the_fault`, `Recover_does_not_clear_an_unrelated_blocked_lane`, `Recover_for_a_non_blocked_fault_unmarks_nothing`, `Pausing_one_shuttle_leaves_unassigned_queued_tasks_dispatchable`, `Pausing_one_shuttle_pauses_only_its_own_assigned_work`, `Resuming_one_shuttle_restores_its_paused_work`, `A_blank_shuttle_id_pauses_and_resumes_every_queued_task`. |
| R5 | No contract/config change | **Pass.** `src/Contracts/`, `docs/contract/`, `src/FleetManager/Program.cs`, and `src/ShuttleSim/` untouched in this build (`git diff` between the pre-lesson-34 commit and the attempt shows only `Rack.cs`, `Shuttle.cs`, `TaskBoard.cs`, and the two test files under `ShuttleCore.Tests/` plus `TaskBoardPauseTests.cs`). `ContractVersion.Current` is still `4`. No configuration key added. |
| Build | `dotnet build ShuttleFleet.slnx` | **Pass.** Build succeeded, 0 warnings, 0 errors (SDK 10.0.113). |
| Tests | `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **Pass.** 247 passed, 0 failed (FleetCore 77, ShuttleCore 36, Contracts 8, Messaging 10, MqttMessaging 3, MockWms 9, ShuttleSim 10, TrafficCore 6, FleetManager 88). |
| Rack tests | `--filter "FullyQualifiedName~Rack"` | **Pass.** 11 passed, 0 failed. |
| Pause tests | `--filter "FullyQualifiedName~TaskBoardPause"` | **Pass.** 4 passed, 0 failed. |
| Recovery tests | `--filter "FullyQualifiedName~Recover"` | **Pass.** 3 passed, 0 failed. |
| Acceptance greps/diff | `grep -q UnmarkBlocked` in Rack.cs and Shuttle.cs; `git diff $BASE -- src/Contracts docs/contract src/FleetManager/Program.cs src/ShuttleSim` | **Pass.** Greps hit; diff empty. |

Baseline accounting: lesson 33 recorded 242 passing before this slice; the slice adds 5 net tests (Rack +2, Recovery +3) → 247 now. Delta is consistent.

## Top issues (max 5), ranked

### 1. [resolved] Mutation checks a–d recorded (all red)

Build report records all four mutations with targeted failing tests: removed `UnmarkBlocked` from `Recover`, unmarked all lanes instead of recorded ones, flipped pause condition back, and dropped the `null` resume arm. All produced red results. Confirmed in `/tmp/opencode` scratch runs (cleaned up afterward).

### 2. [should-fix, verification] Full M7 recovery scenario still unrun (stretch goal)

Lesson 34's stretch goal — run the M7 scenario with a blocked lane (fault, Pause, Acknowledge, Resume), confirm the task is retried and completes, and time Acknowledge → next status `Idle` with no fault — was not run or reported.

Until that runs, the M7 done-when ("a full scenario, including an injected fault, must be run and recovered from the dashboard alone") is not met, and the milestone should stay `in progress` in the README.

### 3. [resolved] `Rack.IsRouteBlocked` deduplicated

`IsRouteBlocked` now delegates to `BlockedLanesOnRoute(...).Any()`; the inclusive-ordinal range check lives in one place (`BlockedLanesOnRoute`). Verified in `src/ShuttleCore/Rack.cs` (`git diff` shows only the delegation change).

### 4. [consider] `TaskBoard` pause/resume ignore the `CancellationToken`

`TaskBoard.PauseAllExcept` / `ResumeAllExcept` accept a `CancellationToken` and return `Task.CompletedTask`; the token is never observed. Harmless today (pure in-memory set updates on the run-loop thread), but it is dead plumbing and a future async persistence-backed version would want the token threaded through.

### 5. [consider] Blocked-lane/fault memory is in-process only

`_blockedLanesOnFault` lives in the `Shuttle` instance (in the per-shuttle `ShuttleSim` process), and the blocked set lives in that process's `Rack`. Restart semantics for a faulted shuttle are therefore implicit; the lesson did not require persistence changes, but the persistence milestone should reconcile this with the fleet-control rule that recovery must leave exactly one place for every pallet.

## What is solid

- The fault-handling order is right: the blocked branch clears the recorded list, fills it from the route, then raises the fault. Clearing per attempt means a retried fault records only the current route's lanes.
- `UnmarkBlocked` mirrors `MarkBlocked` exactly (same validation, unknown lane rejected), keeping one removal method.
- `BlockedLanesOnRoute` and `IsRouteBlocked` use the same inclusive-ordinal convention, so recording and checking agree.
- The pause fix is minimal and surgical: one condition flip, plus the settled-meaning comment at the top of `TaskBoard`, so the historical name is not casually "flipped back".
- The handler side already agreed: `Program.cs` passes `cmd.ShuttleId` to both `taskBoard.PauseAllExcept` and `pauseState.Pause`, so one operator click now pauses movement and dispatch for the same shuttle. Acknowledge still drives the recovery channel that calls `Shuttle.Recover`.
- The new tests are behavior-named and the mutation checks I ran prove they are load-bearing.

## Explain-it check

The student should be able to answer these out loud before the next lesson. No answers are given here.

1. Why does `Shuttle.Recover` unmark lanes from `_blockedLanesOnFault` instead of clearing the whole blocked set, and why does a fault like `lost communication` end up unmarking nothing?
2. Why does `TaskBoard.ResumeAllExcept` clear the paused mark for unassigned tasks (`AssignedShuttleId is null`) as well as for the named shuttle's tasks, when `PauseAllExcept` never pauses unassigned tasks?
3. Why does the fleet manager keep the historical method names `PauseAllExcept` / `ResumeAllExcept` for methods whose settled semantics are "pause / resume the named shuttle"?

### Blocks that look unclear, copied, or unexplained

- `src/ShuttleCore/Shuttle.cs`, blocked-route branch: why `_blockedLanesOnFault.Clear()` before `AddRange(...)`, and why the early `return` with the comment that the caller inspects `State`. The student should be able to defend the reset semantics.
- `src/ShuttleCore/Rack.cs`, `BlockedLanesOnRoute` as the single source of truth: why delegation from `IsRouteBlocked` protects the route model from drift.
- `src/FleetCore/TaskBoard.cs`, pause/resume conditions and class comment: why the named-shuttle resume includes the `AssignedShuttleId is null` arm.

## Revisit list

- The lane-ordinal model and whether the inclusive-ordinal route check is the right representation for multi-lane routes (fleet-control / traffic control).
- Task-id-keyed vs shuttle-id-keyed pause state; cross-process pause remains an open follow-up.
- Recovery semantics: why only recorded lanes are unmarked, and how an unrelated blocked lane is protected (fleet-control fault/recovery).
- Cancellation-token plumbing through decision-logic methods that are currently synchronous.

## End-product check

This slice fits the M7 spec: blocked-lane removal makes `Acknowledge` a real recovery, so a retried task can route through a lane that previously faulted; dispatch-pause semantics make one operator click stop one shuttle in both movement and dispatch. No contract, topic, or config change was needed, which is the right shape for a behavior fix this size.

The risk it introduces later is architectural rather than behavioral: the paused-task set lives in the fleet manager process keyed by task id, so cross-process pause is still an open design item, and the blocked-lane state lives in the per-shuttle process. Both are already tracked as follow-ups, but they should inform how `TaskBoard` and the `Shuttle`/`Rack` state evolve when pause and restart semantics get harder.

## Process and lesson notes

- Lesson requirement coverage: the lesson lists two new concepts (blocked-lane removal, dispatch-pause semantics), stayed within the 3-concept cap, and taught both. Both were implemented and tested within the assignment's file list; no forbidden file (`src/Contracts/`, `docs/contract/`, `src/FleetManager/Program.cs`, `src/ShuttleSim/`) was touched.
- The lesson's verification table was written as a pre-implementation snapshot (items 1–6 describe the pre-fix state), which is fine as a pre-flight record but should not be read as post-fix evidence.
- No lesson defect was found that would require a professor fix before the next lesson.
- Lesson-level review is complete (`pass`). Milestone M7 remains `in progress`; the remaining open item is the full-recovery stretch scenario (fault → Pause → Acknowledge → Resume with timing verification), which could not be completed because no active `serve` process or scenario injection script is available.
