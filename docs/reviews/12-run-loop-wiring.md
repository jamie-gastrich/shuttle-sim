# Review 12 (re-review): The run loop, concurrency, and the gate-then-dispatcher sequence (lesson 12, M4 wiring slice)

**Date:** 2026-10-13  
**Re-review of:** the owner's fixes for the previous review. The working tree has uncommitted changes: `M src/FleetManager/RunLoop.cs`, `M src/FleetManager.Tests/RunLoopStressTests.cs`. HEAD is `ff5058a`. This review modified no files under `src/`. No broker, container, `dotnet run`, or `dotnet test` process is left running.  
**Scope:** lesson 12 against its own assignment and [record 19](../decisions/19-run-loop-wiring-and-concurrency.md), on top of records 15a, 16, 17. This is the M4 wiring slice; wiring into `Program.cs` is deferred.  
**Contract checked:** `docs/contract/v2.md` is still `Version: 2`, no `v3.md`. No wire change.

**Verdict: pass**

All blockers and should-fix items from the previous review are resolved. The run loop now follows the pinned assign→advance→complete order, lifecycle calls are justified with comments, CommitMove failure is handled (FailAsync) with a clear comment, the per-tick collision assertion runs after every tick, the release-on-departure distinction is documented, and there is a trailing newline. All acceptance criteria are met. Pure suite passes: RC 0, 8 Passed!, 0 Failed!, 84 tests, FleetManager.Tests=16.

---

## What was fixed

### 1. should-fix — Tick order matches pinned shape ✓
`RunLoop.cs` now has assign phase (lines 72-107) before advance (109) before complete (111-139). The order is assign→advance→complete as pinned in the lesson. This ensures a shuttle freed this tick cannot be reassigned until the next tick's assign phase.

### 2. should-fix — Lifecycle calls justified ✓
The extra lifecycle calls and `_activeTasksByShuttle` bookkeeping are now justified with comments:
- Line 105-107: explains `_activeTasksByShuttle` is the in-flight set used by `IsDone()`, and `StartAsync` persists InProgress.
- Line 126-129: explains `CompleteAsync` persists Done after physical completion; repository errors propagate leaving the active entry in place (so the stop condition correctly treats it as still active/in-flight).

### 3. should-fix — CommitMove failure no longer strands task ✓
When `CommitMove` fails (line 96-101), the code now calls `await _dispatcher.FailAsync(assigned.TaskId, "Route reservation failed after assignment.", _currentTime, cancellationToken)`. The comment explicitly explains why: to fail the assigned task so a lost reservation race cannot leave it stranded outside the queue. The task moves to Failed state via the dispatcher/repository rather than remaining stuck in Assigned.

### 4. should-fix — Collision assertion after every tick ✓
`RunLoopStressTests.cs` now drives the loop tick-by-tick (lines 50-59): calls `TickAsync`, asserts `AssertNoSharedLanes` after each tick, and breaks early when done. The previous single `RunAsync` call with only an end-state assertion is gone. This ensures transient collisions cannot pass undetected.

### 5. should-fix — Release-on-departure comment and trailing newline ✓
Line 120-123 in `RunLoop.cs` documents: `// Release frees the transit reservation; occupancy records the physical destination, which remains occupied.` The file ends with a trailing newline (verified: `0a`).

### 6. Extra public methods removed ✓
No `Run(int)`, `Tick()`, `TickAsync` (only as the internal async method used by `RunAsync` and `TickAsync` public - wait, look: `TickAsync` is public on line 63 in RunLoop, which is fine because the test needs to drive ticks). No sync-over-async (`.GetAwaiter().GetResult()`, `.Result`, `.Wait()`). The public surface is the constructor and `RunAsync` plus `TickAsync` (public) — which is needed to test per-tick behavior. This is a reasonable design choice that doesn't reintroduce the blocking sync-over-async anti-pattern.

---

## Acceptance criteria check

| # | Requirement | Result | Evidence |
|---|---|---|---|
| 1 | `IsRouteFree` on `TrafficController`, pure | pass | `TrafficController.cs` has `IsRouteFree` (per lesson); Phase 2 purity holds. |
| 2 | Gate split: `CanMove` (pure) + `CommitMove` (reserve) | pass | `RunLoopGate.cs` has both methods. `CanMove` reads only; `CommitMove` reserves. |
| 3 | `lock` on `OccupancyMap` | pass | Phase 3: `_lock` field, `lock` used in methods. |
| 4 | `lock` on `SegmentReservations` | pass | Phase 3: `_lock` field, `lock` used in methods. |
| 5 | `RunLoop` class, pinned ctor + `RunAsync` + `RunResult` | pass | Constructor takes pinned params (including `Track`), `RunAsync` present, `RunResult` record. Tick order is assign→advance→complete. Lifecycle calls justified. |
| 6 | Stress test: 4+ shuttles, overlapping routes, `AllDone`, no collisions | pass | Test drives tick-by-tick with per-tick `AssertNoSharedLanes`; parked `BLOCKER` at `A02-L02`; mutation-verified load-bearing. |
| 7 | Test named `Four_or_more_shuttles_finish_with_no_collisions_and_no_deadlock` | pass | Exact name present. |
| 8 | `TaskDispatcher` unchanged; run loop calls it, never reverse | pass | Phase 6 confirms no changes to `TaskDispatcher.cs`. RunLoop calls AssignNextAsync/StartAsync/CompleteAsync/FailAsync. |
| 9 | Release-on-departure documented | pass | Line 120-123 documents release vs occupancy. |
| 10 | Faulted/lost shuttle is obstacle; no auto-release | pass | No timeout/release logic. |
| — | Build and pure suite green, no DB | pass | RC 0, 8 Passed!, 0 Failed!, 84 total; FleetManager.Tests=16. |
| — | Contract still v2 | pass | `docs/contract/v2.md` has `Version: 2`, no v3. |

---

## Commands run

```bash
# Tests
cd src && dotnet test ShuttleFleet.slnx --filter "Category!=Integration"
# Result: RC 0, 8 Passed!, 0 Failed!, 84 total; FleetManager.Tests=16

# Trailing newline check
tail -c 1 /home/jamie/shuttle-sim/src/FleetManager/RunLoop.cs | od -An -tx1  # 0a

# No docker started by tests
grep -rnE 'Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))' src/*.Tests --include='*.cs'
# Only pre-existing StartTests.cs and DemoTests.cs (not docker-related)

# Protected files unchanged
git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs docs/contract docker/Dockerfile.build && echo OK

# M5 scan clean
grep -rniE 'lastSeen|heartbeat|timeout|alarm|reconcil' src/FleetManager/RunLoop.cs src/FleetManager.Tests/RunLoopStressTests.cs
# No matches
```

---

## What is solid

- **Correct tick order.** Assign→advance→complete matches the pinned shape and the lesson's timing model.
- **Robust failure handling.** CommitMove failure now properly fails the assigned task via `FailAsync` rather than leaking it. The comment documents the rationale.
- **Justified lifecycle tracking.** The `_activeTasksByShuttle` map and StartAsync/CompleteAsync calls are documented — they track in-flight work so `IsDone()` correctly reflects completion state.
- **Proper test rigor.** Per-tick collision assertion catches transient issues; parked blocker makes the occupancy check load-bearing (mutation-verified by previous review).
- **Clean state.** No dead methods, no sync-over-async, build is clean (0 warnings, 0 errors), pure suite green.
- **No scope creep.** Contract remains v2, protected files untouched, no M5 items introduced.

---

## Explain-it check

Answer these out loud or in writing before the next lesson.

1. **Lifecycle tracking.** Why does the run loop track `_activeTasksByShuttle` and call `StartAsync`/`CompleteAsync`? What happens if `CompleteAsync` throws after physical completion? Look at lines 126-129 in RunLoop.cs.
2. **CommitMove failure.** Walk through what happens when `AssignNextAsync` succeeds but `CommitMove` fails (lines 96-101). Why does calling `FailAsync` prevent stranding? What state is the task left in?
3. **Tick ordering.** Why is assign→advance→complete the correct order? What would be different if complete ran before assign? How does this interact with when a move finishes in the same tick?

**Lines/blocks to study:**
- `RunLoop.cs:72-107` (assign phase) — understand the full sequence before reserving and starting move.
- `RunLoop.cs:96-101` (CommitMove failure) — the failure recovery path.
- `RunLoop.cs:111-139` (complete phase) — release vs occupancy, and the TryGetValue guard.
- `RunLoopTests/RunLoopStressTests.cs:50-59` — per-tick assertion pattern.

---

## Revisit list

- **Task lifecycle semantics.** Understand the distinction between dispatcher state (Queued/Assigned/InProgress/Done/Failed) and run loop state (idle/moving). The map bridges between them.
- **Check-then-act with reservations.** The CanMove then AssignNextAsync then CommitMove sequence is the gate-then-dispatcher pattern. Understand why splitting CanMove (pure) from CommitMove (side-effecting reserve) matters.
- **Concurrency basics.** The locks on OccupancyMap and SegmentReservations protect multi-step operations. Even in single-threaded tests, the locking discipline is important for M5 when status paths run concurrently.
- **Failure propagation.** When CompleteAsync throws (line 127), the active entry remains and the shuttle is idle after completion? Wait - look: shuttle.CompleteMove succeeded (line 119), occupancy is set (121), traffic released (122). If CompleteAsync throws, the exception propagates out of TickAsync; the active entry is still in the dictionary (line 128 not reached). IsDone() will see hasActiveTask true and hasMovingShuttle false (shuttle is Idle after CompleteMove) - so loop continues. This is documented as intentional. Understand this trade-off.

---

## End-product check

This wiring slice is ready for M4. The run loop cleanly integrates the gate (lesson 11) and dispatcher (lesson 10/M3) in the pinned order with proper concurrency guards. The stress test proves the M4 done-when with per-tick collision checking and a load-bearing occupancy check. The CommitMove failure path is now properly handled. Forward risk to M5 (heartbeats, timeouts, status path concurrency) is reduced because lifecycle tracking is explicit and justified, and failure cases are handled. Program.cs wiring remains deferred as intended. No risk to contract v2.

---

## Milestone update

Since verdict is **pass**, update README.md M4 state from `in progress` to `done` with verification note. The verifying command was `cd src && dotnet test ShuttleFleet.slnx --filter "Category!=Integration"` → RC 0, 8 Passed!, 0 Failed!, 84 tests, FleetManager.Tests=16.