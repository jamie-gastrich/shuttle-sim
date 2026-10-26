# Review 29: Traffic control under the dashboard (re-review)

**Verdict: pass**

## Build and test results

- `cd src && dotnet build --no-incremental` — **0 Warning(s), 0 Error(s)** (re-verified 2026-10-26).
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` — **224 passed, 0 failed** (FleetManager.Tests 80).
- Focused: `FullyQualifiedName~RunLoopGate|FullyQualifiedName~Paused_shuttle_does_not_complete_its_move` — **9 passed, 0 failed** (FleetManager.Tests).
- Integration (Docker up, then down): `Category=Integration` — **4 passed** (FleetManager.Tests). Broker/database stopped afterward.
- Mutation (re-verified by the reviewer, temporarily editing then restoring):
  - (a) remove pause check from `CanMove` → `CanMove_returns_false_when_paused` goes red. Restored.
  - (b) remove pause check from completion loop → `Paused_shuttle_does_not_complete_its_move` goes red. Restored.
  - (c) not re-run this pass; prior pass verified it turns `CanMove_returns_false_when_paused` and `PauseAll_pauses_every_listed_shuttle` red, and the new `CommitMove_returns_false_when_paused` test is additionally sensitive to a no-op `Pause`.
- Contract scope: `git diff` on `src/Contracts` and `docs/contract` is empty. `ContractVersion.Current` is still `4`. `appsettings.json` untouched. Protected files (`TaskBoard.cs`, `FleetTask.cs`, `TaskExecutionLoop.cs`) unchanged.
- `git diff --check` clean.

## Acceptance criteria

| Criterion | Status | Evidence |
|---|---|---|
| R1 pause state on gate (`HashSet<string>`, ordinal; `Pause`/`Resume`/`IsPaused`/`PauseAll`/`ResumeAll`; idempotent) | met | `RunLoopGate.cs:9,15-34` |
| R2 `CanMove` includes pause, checked before occupancy and traffic | met | `RunLoopGate.cs:36-41` |
| R3 in-progress moves freeze | met | `RunLoop.cs:125-129` |
| R4 no contract/config change | met | empty diff on `src/Contracts`, `docs/contract`; `ContractVersion.Current == 4` |
| R5 required tests pass | met | 9 focused + 224 total pass |
| R6 protected files untouched | met | no changes to `TaskBoard.cs`, `FleetTask.cs`, `Contracts/`, `TaskExecutionLoop.cs` |
| Mutation (a) | met | `CanMove_returns_false_when_paused` goes red |
| Mutation (b) | met | `Paused_shuttle_does_not_complete_its_move` goes red |
| Mutation (c) | met (prior pass; plus new CommitMove test) | `CanMove_returns_false_when_paused`, `PauseAll_pauses_every_listed_shuttle`, `CommitMove_returns_false_when_paused` |
| Mutation (d) | not distinguishable | see Top issues #1 |

## Top issues

1. **(consider) Mutation (d) is still untestable by return value.** The lesson's mutation table asks for a test where a paused shuttle with no current lane still sees `false` so that swapping the pause check after occupancy would go red. But a shuttle with no current lane already returns `false` from the null-lane branch, so the pause check's position cannot be observed through `CanMove`'s bool. The ordering (pause before occupancy) is therefore enforced by code review, not by a test. This is partly a lesson/assignment defect, not a student failure. *Why it matters:* a future refactor could move the pause check after the null-lane check and every test would stay green; the refusal would still be `false` but for a different reason, which muddies diagnostics for an operator. Recommend either documenting the ordering as an invariant in `CanMove` or exposing a refusal reason; until then, treat as code-review enforced.

2. **(consider) Two independent, in-memory pause sets.** The gate's shuttle-id set is separate from `TaskBoard`'s paused task-id set, and neither survives a restart. This is explicitly out of scope in record 38 and acceptable for this slice. *Why it matters:* an operator could pause movement while tasks still dispatch (or vice versa), and a fleet-manager restart silently drops every pause. M7's "dashboard alone" done-when will need one source of truth plus a locking discipline, because the gate is touched by an MQTT handler and the tick loop concurrently. Track as a follow-up slice.

3. **(consider) The sim clock keeps advancing during pause.** `_currentTime += _tickInterval` runs unconditionally in `TickAsync`, so paused wall-clock time counts toward a move's travel time. A shuttle paused for 30 s and resumed may complete on the next tick (position jumps). The strengthened test passes because of this. *Why it matters:* a long pause could make a resumed shuttle teleport to its destination, which is surprising and, in a real system, a safety/audit issue. Decide the intended semantics before wiring pause to the dashboard.

4. **(consider — minor) `RunLoopGateTests.cs` has no trailing newline**, and `docs/builds/28-mock-wms-reliability-report.md` now carries a sign-off with a double space (`Owner sign-off:  2026-...`). Cosmetic; fix when convenient.

## What is solid

- The pause check is first in `CanMove`, before occupancy and traffic, exactly as decision 38 specifies, and the dispatch loop needs no new code because it already calls `_gate.CanMove`.
- `Pause`/`Resume` use a `HashSet` with ordinal comparison, so they are idempotent and case-sensitive on shuttle ids, matching the contract's id conventions.
- The completion loop skips a paused shuttle *before* `CompleteMove`, occupancy update, reservation release, and task completion, so position, occupied lane, transit reservation, and task state are preserved as a unit.
- `CommitMove` now also refuses a paused shuttle before reserving any route, with a test confirming no reservation is created — good defense-in-depth against a paused shuttle holding a lane.
- `IsDone()` is unchanged, so a paused in-progress shuttle keeps the run loop from falsely reporting completion; a forever-paused run must be cancelled by the caller.
- No contract, config, or protected file was touched; the change is confined to `RunLoopGate`, `RunLoop`, and their tests, as required by R6.
- The strengthened paused-move test (1 s/lane, 5 paused ticks vs. a 2 s route) now actually exercises the freeze: removing the guard makes it fail.
- The vacuous no-occupancy test was removed, and the sign-off is now a single conforming line, resolving the two should-fix items from the prior review.

## Explain-it check

1. `RunLoop.TickAsync` adds `_tickInterval` to `_currentTime` before the move-completion loop, unconditionally — even ticks where every moving shuttle is paused. What happens to a shuttle paused for 30 s and then resumed: does it teleport to its destination on the next tick? Why, and is that acceptable for an operator pause?
2. `CanMove` tests `IsPaused` before `Occupancy.GetOccupiedLane`. Why must pause win even when the shuttle has no recorded current lane? What could a caller observe if the order were swapped, and which test is meant to catch it (and does it)?
3. The dispatch loop consults the gate through `CanMove(shuttleId, destinationLane)`, but the completion loop calls only `_gate.IsPaused(shuttleId)`. Why doesn't the completion loop call `CanMove`? What inputs does `CanMove` require that the completion loop does not have, and what would happen if it tried?
4. Lines/blocks to study because they look like they do more or less than they do: `CommitMove`'s new pause guard (defense-in-depth that is not in the lesson's required file list, but harmless and tested); `CanMove_returns_false_when_paused` now asserts both the paused refusal and the resumed allowance in one test; and the `reservedSegments` equality assertion in `Paused_shuttle_does_not_complete_its_move`, which is only meaningful because the move would otherwise have completed during the pause. Also note `RunLoopGateTests.cs` has no trailing newline.

## Revisit list

- Mutation testing / test sensitivity: writing a test that goes red when the feature it claims to protect is deleted (and deleting tests that cannot).
- Concurrency for shared mutable state: a `HashSet` gate touched by an MQTT handler and a tick loop at the same time; locking vs. single-owner.
- Simulation time vs. real time: whether paused wall-clock time should count toward a move's travel time, and how to freeze a move's clock.
- Idempotency and set semantics: why `Pause`/`Resume` on a `HashSet` are naturally idempotent, and how that differs from a counter or a bool.
- Refusal reasons: why a bool return hides which rule fired, and when that matters for operators and diagnostics.

## End-product check

This slice makes the operator's pause actually stop shuttle movement in the sim run loop: dispatch is blocked through `CanMove`, in-progress moves freeze in place with their reservations held, and `CommitMove` refuses paused shuttles, satisfying the movement half of M7's pause/resume and reinforcing M4's no-collision rule. It does **not** close M7: the gate's pause set is still independent of `TaskBoard`'s paused task set and of the serve process, the dashboard pause command is not yet wired to the gate (lesson 29's stretch goal), recovery wiring from M5 is still unbuilt, and no full scenario with an injected fault has been run and recovered from the dashboard alone. M7 therefore stays open and needs at least one more slice (dashboard/serve wiring plus the end-to-end fault scenario) before its done-when is met; this review does not mark it done. The main risks carried forward are integration (two pause sets that can drift, plus no restart survival) and test honesty (the pause-before-occupancy ordering is untested, and the sim clock keeps advancing during pause, so a resumed move may complete instantly).