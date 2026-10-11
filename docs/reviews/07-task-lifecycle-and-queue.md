# Review 07: The task record, lifecycle, and pure board (lesson 07, first M3 slice)

**Date:** 2026-10-11  
**Reviewed:** Re-review after fixes. Previous review was pass-with-changes for commits `5769d7d` + `4c8cb1a`; fixes in `eed2b4c` (B1/S1–S3) and `b17ac44` (residual S2 in Assign/Fail; also carries professor's L1/L2 updates and decision record 12). HEAD `b17ac44`. Working tree clean.  
**Scope:** lesson 07 against its assignment and [record 11](../decisions/11-task-lifecycle-and-queue.md) plus [decision record 12](../decisions/12-task-blank-inputs-and-exception-types.md) (blank = `IsNullOrWhiteSpace`, blank-input → `ArgumentException`, parameter names in order).  
**Contract checked:** `docs/contract/v1.md` (v1) and `docs/contract/v2.md` (v2) unchanged; no `v3.md`; no task types in `src/Contracts`.

**Verdict: pass.**

The prior blockers/should-fixes B1 and S1–S3 have been cleared with concrete proof. The wire-name test now asserts all five names through `TaskStateWire.ToWireName()`; mutating `in_progress` to `inprogress` causes that test to fail (and all 18 FleetCore tests no longer stay green). Blank guards use `IsNullOrWhiteSpace` on taskId, toLane, shuttleId, and reason. `ArgumentException` messages/param names are correct in order (`(message, nameof(parameter))`) for the constructor, Assign, and Fail. The duplicated `_lastEventAt = now` in Fail is gone and the file has no placeholder comment bloat from the template. Phase 3b gate (content-level check for ToWireName + all five wire strings) passes on current code and would fail on a vacuous test. The full acceptance (Phases 1–6) holds, the whole suite gives 7 Passed!, M1/M2 regressions are clean, fresh Docker build/run passes (7 Passed!), contract checks are clean, and the load-bearing mutations (Complete guard, EnsureInOrder, NextQueued state-skip) still go red when broken. M3 is **not done** (persistence, restart survival, assignment policy are 08+).

## Re-run evidence (what was checked)

### 1) Build and test (baseline)
```
dotnet build src/ShuttleFleet.slnx → exit 0, 0 Warning(s), 0 Error(s)
dotnet test src → 7 Passed! lines (18/4/8/2/22/5/5; total 64 tests)
FleetCore.Tests: Passed 18/18
```

### 2) B1 cleared (wire-name test now tests wire names; mutation goes red)
The test `Task_wire_names_match_record_11` asserts all five names via `TaskStateWire.ToWireName()`:
```
queued/assigned/in_progress/done/failed all called and asserted
```
Phase 3b (from lesson) extracts the test body and requires `ToWireName` and all five quoted names — passes on HEAD.  
Mutation test (throwaway copy, rename `in_progress`→`inprogress` in `TaskStateWire`): `Task_wire_names_match_record_11` fails with `Assert.Equal() Failure` (expected `"in_progress"`, actual `"inprogress"`). All 18 FleetCore tests did not remain green (the wire-name test fails).

### 3) S1/S2 cleared (blank guards + exception shape)
`FleetTask`:
- Constructor guards use `string.IsNullOrWhiteSpace(taskId/toLane)` and throw `ArgumentException("TaskId cannot be empty.", nameof(taskId))` / similarly for `toLane` (message first, param name second).
- `Assign` uses `IsNullOrWhiteSpace(shuttleId)` with `(message, nameof(shuttleId))`.
- `Fail` uses `IsNullOrWhiteSpace(reason)` with `(message, nameof(reason))`.

### 4) S3 cleared (no duplication, no copied comments)
`Fail` has a single `_lastEventAt = now;`. The template comments that were copied are not present as-is; the code has minimal, intentional guard comments.

### 5) Phase 3b gate verification
On current `src/`, the content-level gate finds `ToWireName` and all five wire strings in the test body — OK. On a vacuous copy (test body replaced with unrelated assertions), the gate fails to find `ToWireName` and the five names — confirms the gate is effective.

### 6) Load-bearing mutations still prove guards
- Delete `Complete` guard → `Complete_while_assigned_is_rejected` fails; FleetCore has 1 Failed, 17 Passed.
- `EnsureInOrder` made to never reject backwards → `A_move_with_a_timestamp_before_the_previous_one_is_rejected` fails.
- `NextQueued` returns first regardless of state → `Board_next_queued_skips_assigned_and_finished_tasks` fails. All reverted; suite returns to 18/18.

### 7) Contract, projects, purity
- Phase 1–2: `FleetCore` has no references (0 Project/Package refs); no `DateTime.UtcNow`, `Guid.NewGuid`, delays, I/O (Npgsql/MQTTnet/Messaging/Contracts) in `FleetCore`. Five states + `TaskStateWire.ToWireName` match record 11.
- Phase 5: `v1.md` Version 1, `v2.md` Version 2, no `v3.md`; no task types in `src/Contracts`.

### 8) Fresh container
`docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` succeeded; `docker run --rm shuttle-fleet-build` produced 7 Passed! lines. No container processes left behind.

### 9) M1/M2 regression
M1 demo: `dotnet run --project ShuttleSim/ShuttleSim.csproj` exits 0 and produces 7 `"ShuttleId"` lines. FleetManager: start line `Fleet Manager Coming Online` present. M2 broker checks from prior review remain valid; the code changes don’t affect them.

## Acceptance (re-check)
| # | Criterion | Status | Evidence |
|---|---|---|---|
| 1–17 | Phases 1,2,4,5 (projects, purity, contract, demo) | met | build, greps, contract checks pass |
| 18–19, 24–29 | Guards, wire names mapping, FIFO, purity, all tests pass | met | mutations red as expected; 18/18 green |
| 20 | `Task_wire_names_match_record_11` asserts five names via `ToWireName()` | **met (B1 cleared)** | Phase 3b passes; mutation fails test |
| 21–23 | Blank guards use `IsNullOrWhiteSpace`; `ArgumentException` with (message, nameof(...)) for ctor/Assign/Fail | **met (S1,S2 cleared)** | code matches DR 12 and behavior |
| (S3) | No duplicated `_lastEventAt`; no hints-template comments | **met** | single assignment; clean code |

30/30 of the relevant criteria are now met. No blockers remain.

## Findings

### Blockers
None.

### Should-fix
None. The previously identified issues B1/S1–S3 are closed. C1 (exception type choice) was decided in [DR 12](../decisions/12-task-blank-inputs-and-exception-types.md) — blank-input → `ArgumentException`, wrong-state/clock → `TaskRejectedException`. This matches the code.

### Consider (non-blocking)
- Trailing whitespace/newlines as noted before — consistent with existing style; not required to block pass.
- Add a tiny test for `Fail_after_failed` or `TryGet`/`Get` edge cases later (nice-to-have, not in required list).

## Explain-it verification notes
The three explain-it questions from the first review remain appropriate. The new evidence shows:
1. The board predicate `State == TaskState.Queued` (not list order alone) is what skips in-flight work — proven by mutation M4 going red.
2. Time is injected (`DateTime now`) rather than read internally — enables deterministic tests and restart-time reasoning; equal timestamps allowed.
3. Blank inputs and wrong-state moves are cleanly separated by exception type per DR 12.

## End-product check
The pure task model is now sound: wire vocabulary is actually tested (dashboard/WMS won’t invent spelling later), blank inputs are treated as caller bugs with correct parameter reporting, and the board’s FIFO+skip behavior is mutation-proven. The risk identified earlier (untested wire vocabulary, sloppy blank guards) is eliminated. **M3 is in progress but not done** — SQL persistence, assignment policy, and restart survival are deferred to lessons 08 and 09. This slice is ready to support them.

## README
**Changed only as allowed (after a clean pass).** The M3 row is updated from `not started` to `in progress` in `README.md`, with a short verification note under the table stating the commands run (`dotnet build`, `dotnet test` with 7 Passed!, fresh Docker build/run with 7 Passed!, Phase 3b gate, and mutation checks) and explicitly that M3 is not done until lesson 08’s SQL persistence and lesson 09’s restart proof land. No other README edits.

## Cleanup
`docker compose -f docker/compose.yml down` (no broker containers left running in this session), `dotnet build-server shutdown`, and all throwaway `/tmp/opencode/*` copies removed. `/tmp/opencode/mqttinspect` left as found. `git status --porcelain` shows only `docs/reviews/07-task-lifecycle-and-queue.md` and `README.md` modified.
