# Review 16 — Blocked-path fault and the second alarm (M5, lesson 16)

**Reviewer:** Reviewer agent
**Verdict (first review): not yet**

Commit under review: `a2c63f3` ("M5: Lesson 16 First attempt"), base `cfc72a6`.
Working tree clean at review time.

> **Review passes (2026-10-16):** the owner's fixes are in the working tree on
> top of `be66445`. Two re-reviews and a final confirmation follow:
> [pass 2](#re-review--2026-10-16-the-fixes-re-verified),
> [pass 3](#re-review-pass-3--2026-10-16-second-round-of-fixes), and
> [final confirmation](#final-confirmation--2026-10-16-pass). **Current verdict:
> pass** — `dotnet build` RC 0, 0 warnings/errors; pure suite RC 0, 8 `Passed!`,
> 0 `Failed!`, 124 tests (ShuttleCore 29, ShuttleSim 7, FleetManager 46). The route
> test and the two test-quality holes are fixed and load-bearing, and the
> corrected `A_healthy_status_clears_the_blocked_path_latch` is now proven by
> mutation M12. This first review is kept below as the log.

## Gate results

1. `cd src && dotnet build` — **exit 1**. One error:
   ```
   src/FleetManager.Tests/FleetServeLoopIntegrationTests.cs(39,29): error CS7036:
   There is no argument given that corresponds to the required parameter
   'blockedPathSeverity' of 'AlarmEvaluator.AlarmEvaluator(string, string)'
   ```
   The production projects build clean on their own:
   `dotnet build ShuttleSim/ShuttleSim.csproj` → exit 0; `dotnet build
   FleetManager/FleetManager.csproj` → exit 0. The failure is test-only.
2. Pure suite (no database, no broker):
   ```
   cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
   ```
   **RC 1**, 6 `Passed!` lines, 1 `Failed!` line, **75** tests total
   (requested gate: RC 0, 8 `Passed!`, 0 `Failed!`, 124). `FleetManager.Tests`
   did not build, so it did not run; `ShuttleCore.Tests` was 26 passed / 3 failed:
   ```
   Failed ShuttleCore.Tests.RackTests.IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked
     Assert.False() Failure  Expected: False  Actual: True
   Failed ShuttleCore.Tests.StateTransitionTests.StartMove_through_a_lane_blocked_between_two_lanes_enters_fault
     ShuttleCore.ShuttleRejectedException : already faulted
   Failed ShuttleCore.Tests.StateTransitionTests.StartMove_across_a_blocked_lane_enters_fault_with_the_blocked_path_reason
     ShuttleCore.ShuttleRejectedException : already faulted
   ```
3. Phase 6b (anchored test names) could not complete: `dotnet test
   --list-tests` fails to build `FleetManager.Tests`, so the ten new
   `FleetManager` names cannot be listed.

A failing build is an automatic **not yet**. The rest of this review is
written anyway so the fixes are concrete.

## Acceptance criteria

Production requirements (1–9, 13, 14) read clean and the two apps build. Every
failure is in the new tests.

- [x] Req 1 — `FaultReasons` with `BlockedPath = "blocked path"`. Evidence:
  `src/ShuttleCore/FaultReasons.cs`; phase 1 grep passes.
- [x] Req 2 — `Rack.MarkBlocked`/`IsBlocked`/`IsRouteBlocked` with inclusive
  range and `ArgumentException` for an unknown lane. Evidence: `Rack.cs:79-98`;
  phase 1 grep passes.
- [x] Req 3 — `StartMove` calls `IsRouteBlocked(_position, toLane)`, then
  `RaiseFault(FaultReasons.BlockedPath, now)` and returns before setting
  `_moveToLane`. Evidence: `Shuttle.cs:39-43`; matches the required order.
- [x] Req 4 — endpoint fault branch acks `accepted: false` with
  `_shuttle.FaultReason`, publishes status, does **not** call `MarkCompleted`,
  and does not publish `moving`/`idle`. Evidence: `ShuttleEndpoint.cs:73-78`.
- [x] Req 5 — `Shuttle:BlockedLanes` in config, marked before the endpoint is
  built, no literal lane in code. Evidence: `appsettings.json:11`,
  `Program.cs:79-82`; phase 2 grep passes.
- [x] Req 6 — `FaultAlarmCodes.ForReason` is pure and returns `null` for
  `null`/blank/unknown. Evidence: `FaultAlarmCodes.cs`; phase 3 purity grep
  prints nothing.
- [x] Req 7 — `AlarmEvaluator(string, string)`, both severities validated;
  `Evaluate(id, lost, faultCode)` rejects an unknown code; latch keyed
  `(shuttleId, code)`. Evidence: `AlarmEvaluator.cs:12-49`.
- [x] Req 8 — `_faultCodes` guarded by `_knownLock`; `HandleStatusAsync`
  computes the code inside the lock; `EvaluateAndPublishAsync` snapshots ids and
  codes in one lock then works outside it. Evidence: `FleetServeLoop.cs:70-99`;
  phase 3 grep passes (`using ShuttleCore` present).
- [x] Req 9 — manager config + wiring: `BlockedPathSeverity: "warning"`,
  both severities read in `Program.cs:144-151`, explicit `ShuttleCore`
  `ProjectReference`. Evidence: phase 2 greps and `FleetManager.csproj:16`.
- [ ] Req 10 — `ShuttleCore.Tests` +4. **FAIL.** Two tests are named
  correctly but throw `already faulted` because they call `shuttle.RaiseFault(...)`
  *after* `StartMove` already faulted (`StateTransitionTests.cs:150` and
  `:166`); the third, `RackTests.IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked`
  (`RackTests.cs:57-62`), marks `A01-L02` blocked but then checks the route
  `A01-L01 → A02-L02`, which includes `A01-L02`, so it asserts `False` against a
  `True` result.
- [ ] Req 11 — `ShuttleSim.Tests` +1. Passes (`ShuttleSim.Tests` 7/7), but its
  `Assert.Equal(FaultReasons.BlockedPath, ack.Reason)` only holds because the
  paths on both sides currently use the same constant; see issue 4.
- [ ] Req 12 — `FleetManager.Tests` +10. **FAIL.** The project does not build
  (issue 1). Even after adding the missing severity, several new and existing
  tests are wrong (issues 2–5). The mapper pair (`FaultAlarmCodesTests`) is
  correct.
- [x] Req 13 / 14 — contract and transport untouched; `Current = 3`,
  `docs/contract/v3.md` `Version: 3`, no `v4.md`; protected files unchanged
  (phase 4 and 5 `git diff --quiet` both print OK).
- [x] Req 15 / phase 8 — no later-M5 scope leak. The grep matches only
  pre-existing command-ack code (`ReliableCommandSender`, the `move` path,
  `ShuttleEndpoint` acks); no `palletNotDetected`, reassignment, or recovery.
- [x] Phase 7 — no `docker` reference in any `*.Tests/*.cs`.

## Top issues (max 5), ranked

1. **blocker — the solution does not build.**
   `FleetServeLoopIntegrationTests.cs:39` still constructs
   `new AlarmEvaluator(AlarmSeverities.Critical)` with one argument. The
   `AlarmEvaluator` constructor now requires two severities, so `FleetManager.Tests`
   never compiles and the whole gate returns RC 1. This is the single line that
   turns an otherwise-running suite into a red gate. Until it is fixed, no test
   in `FleetManager.Tests` runs, including the ten you just added.

2. **blocker — every test that passes `""` as a severity dies in the
   constructor.** The new contract validates *both* severities and throws
   `ArgumentOutOfRangeException` on `""`. That means all 8 pre-existing
   `FleetServeLoopTests` (`:50, :62, :77, :112, :133, :161, :202, :222`), the
   three new loop tests (`:250, :280, :307`), and the five new blocked-path
   `AlarmEvaluatorTests` (`:80, :91, :100, :110, :117`) throw before they assert
   anything. In a real system a misspelled severity in `appsettings.json` must
   stop startup — you wrote that guard correctly — but the tests must pass a
   *valid* second severity (e.g. `AlarmSeverities.Warning`), not `""`.

3. **blocker — the two new state-transition tests do not test the route rule
   as written.** `StateTransitionTests.cs:150` and `:166` both do
   `shuttle.StartMove(...); shuttle.RaiseFault(reason, Start);`. `StartMove`
   already raised the fault, so the explicit `RaiseFault` throws
   `already faulted`. Delete that line and assert purely on the state
   `StartMove` produced (`State == Fault`, unchanged `Position`,
   `FaultReason == FaultReasons.BlockedPath`). As written they would fail even
   if the route check were deleted, so they prove nothing about the rule they are
   named for.

4. **blocker — the route test contradicts the rule.**
   `RackTests.IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked`
   (`:57-62`) blocks `A01-L02` and then asks about `A01-L01 → A02-L02`, a route
   that includes `A01-L02`. The method correctly returns `True`; the test asserts
   `False`. Mark a lane that is genuinely off the route (e.g. block `A02-L02`,
   check `A01-L01 → A01-L02`) so the name matches the assertion.

5. **blocker — the recovery loop test was rewritten to a fixed clock and now
   cannot recover.** Requirement 12 says the existing 8 loop tests keep passing
   and *only the evaluator construction changes*. Instead,
   `A_recovered_shuttle_clears_the_latch_and_can_raise_again`
   (`FleetServeLoopTests.cs:128-153`) was changed from a mutable `now` to a
   pinned `() => Start`. With a pinned clock, `HandleStatusAsync` records the
   "recovered" heartbeat at `Start`, so the later sweeps still see the shuttle
   as lost and the latch never clears — the test publishes one alarm, not two,
   and fails. The duplicate `Assert.Equal(2, bus.Published.Count);`
   (`:150` and `:152`) is still there too. Restore the mutable-clock form from
   review 15. This is the most important one to understand: the test was the
   proof that an alarm can fire again after a clear, and pinning the clock
   quietly removed that proof.

Two more worth a line, folded into the fixes above rather than the ranking:
`AlarmEvaluatorTests.An_unknown_fault_code_is_rejected` (`:114-118`) constructs
an evaluator with an invalid *severity*; it passes for the wrong reason and
never exercises `Evaluate`'s `faultCode` guard (mutation M11 below stayed green
until I made the test call `Evaluate`). `A_cleared_blocked_path_fault_can_raise_again`
(`:96-104`) never passes a healthy (`null`) fault code, so its "clear re-arms"
branch does not exist. And `A_healthy_status_clears_the_blocked_path_latch`
(`:275-299`) asserts `Assert.Empty(bus.Published)` after an earlier sweep already
published one — the assertion can never be true; assert the count does not
change instead. Requirement 12 also asked you to extend
`An_unknown_severity_is_rejected` to cover both severities; only the first is
checked.

## What is solid

- The production code is clean and the two apps build. `Shuttle.StartMove`
  inserts the route check in exactly the required place and order, and leaves
  the position unchanged on fault.
- `AlarmEvaluator` is a good shape: one lock, a `(shuttleId, code)` latch, the
  same `Add`/`Remove` edge rule per condition, and independent severities. It is
  pure and testable with no broker.
- `FleetServeLoop` keeps its discipline: no `DateTime.UtcNow`, no `Guid`, no
  `Thread.Sleep`, no transport types; one lock acquisition snapshots ids and
  `_faultCodes` before evaluating outside the lock. Adding `ShuttleCore` as an
  explicit reference instead of leaning on the transitive one is the honest fix.
- The endpoint's fault branch is exactly right: ack `accepted: false`, publish
  the fault status, and deliberately **not** `MarkCompleted`, so a retried
  command is not silently swallowed.
- `FaultAlarmCodes` and its two tests are correct, and the config split (wire
  constants in code, tunables in `appsettings.json`) is respected.

## Explain-it check

Answer these out loud or in writing before the next lesson. They are not
answered here.

1. `Shuttle.StartMove` handles a blocked route by calling `RaiseFault(...)` and
   returning, while an unknown lane throws `ShuttleRejectedException`. Why is a
   blocked route a *state transition on the shuttle* rather than a rejected
   command? How does `ShuttleEndpoint` tell the two apart, and what would break
   downstream if a blocked route threw instead of latching `Fault`?

2. `AlarmEvaluator` keys its latch on `(shuttleId, code)` but the serve loop
   hands it a single `faultCode` per shuttle. Walk through an `Evaluate` call
   where the shuttle is both silent and faulted. What goes wrong if the latch
   key is `shuttleId` alone — and what does that tell you about the relationship
   between the alarm condition and its identity?

3. `FleetServeLoop` keeps its own `_faultCodes` dictionary (under `_knownLock`)
   and passes each code into a pure evaluator, rather than letting the evaluator
   observe the status stream directly. Why is that extra dictionary worth it?
   Name the seam and the test it makes possible, and say what would be harder to
   test if the evaluator read the bus itself.

Also study these lines/blocks; they look unclear, copied, or easy to gloss over:

- `src/FleetManager.Tests/FleetServeLoopTests.cs:128-153`,
  `A_recovered_shuttle_clears_the_latch_and_can_raise_again`: the pinned
  `() => Start` clock and the duplicated `Assert.Equal(2, bus.Published.Count);`.
  Compare with the lesson-15 version and explain why the clock must be mutable
  for a recovery scenario to mean anything.
- `src/FleetManager.Tests/AlarmEvaluatorTests.cs:114-118`,
  `An_unknown_fault_code_is_rejected`: it constructs `new AlarmEvaluator("",
  "Something Wrong")`. Which guard actually fires, and what test would you write
  to pin `Evaluate`'s `faultCode` check?
- `src/FleetManager.Tests/AlarmEvaluatorTests.cs:96-104` and
  `FleetServeLoopTests.cs:275-299`: both comment "clear re-arms" / "clears the
  latch" but neither feeds the evaluator a healthy input or resets the bus.
  Trace what value actually clears each latch (`null` fault code / `lost=false`).
- `src/ShuttleCore.Tests/StateTransitionTests.cs:141-171`: the redundant
  `RaiseFault` after `StartMove`. Which call sets `State = Fault`, and why does
  the second throw?
- `src/FleetManager/FleetServeLoop.cs:28-31` and `:73`: inconsistent
  indentation and a trailing space; cosmetic, but it suggests the file was
  hand-merged.
- `src/ShuttleSim/ShuttleEndpoint.cs:76`: the trailing space after
  `PublishStatusAsync(now, cancellationToken);` in the fault branch.

## Revisit list

- Constructor validation of *both* parameters (the two-severity `AlarmEvaluator`)
  and why `""` is not "unused".
- Per-condition edge latches keyed by identity, and what "identity" must include
  so one condition cannot erase another.
- Tests that prove a rule vs. tests that pass by accident: asserting after the
  code already raised the fault, asserting `Empty` on a bus that was never
  cleared, and choosing a route that does not contain the obstacle.
- Clock injection: a fixed clock is right for "the same instant is used for the
  check and the envelope"; a *mutable* clock is required to exercise recovery
  over time.
- The `ICollection`/`HashSet` snapshot-under-lock pattern in
  `EvaluateAndPublishAsync`, now extended to the `_faultCodes` copy.

## End-product check

The intended slice — one shuttle detects a blocked route, faults, reports it on
the existing `status` lane, and the manager raises exactly one `blockedPath`
alarm per episode at a configured severity on the existing `alarm` lane — fits
M5 well and adds no wire kind, so the contract stays v3 and the later M7
dashboard path is undisturbed. The production code is, as far as I can read it,
correct and the apps build; the deliverable fails because the *tests* that are
supposed to prove this do not compile and, where they do run, several assert the
wrong thing. For the end product the risk is not the fault itself but the
evidence: if `FleetServeLoop`'s fault-code plumbing or the latch keying silently
regress later, a warehouse would miss a blocked-path alarm — a shuttle sitting
faulted in an aisle with no operator alert, and traffic control (still not
honoring blocked lanes, per record 23 §10) routing other shuttles straight at it.
That is exactly the kind of failure these tests must catch, so they have to be
green and load-bearing before this slice counts.

## Mutation results

Run in throwaway copies under `/tmp/opencode` only; the workspace `src/` was
never modified (working tree clean throughout). Because the committed tree does
not build, I first applied the minimal test repairs described above to a scratch
copy to get a green baseline (RC 0, 8 `Passed!`, 0 `Failed!`, 124 tests), then
mutated the product code there. Results are for the repaired copy; the caveat on
M11 matters.

| Mutation | Expected (lesson) | Result |
|---|---|---|
| M1 delete the `IsRouteBlocked` check in `StartMove` | 2 state tests red | **red:** both state tests + the endpoint blocked-move test |
| M2 check only the destination (`IsBlocked(toLane)`) | intermediate-lane test red | **red:** `StartMove_through_a_lane_blocked_between_two_lanes_enters_fault` (only) |
| M3 raise the fault with a different reason string | endpoint + mapper/loop red | **red:** both state tests + endpoint test |
| M4 `FaultAlarmCodes.ForReason` returns `null` for blocked path | mapper + both loop fault tests red | **red:** mapper test + both loop fault tests + healthy-latch test |
| M5 pass the fault code always `null` in the sweep | both loop fault tests red | **red:** both loop fault tests + healthy-latch test |
| M6 key the evaluator latch on `shuttleId` alone | two-alarm test red | **red:** two-alarm test + 4 more (`Two_shuttles_…`, `A_blocked_path_fault_that_persists_…`, `A_condition_that_stays_true_…`, `A_latched_lost_shuttle_…`) |
| M7 return the wrong severity for blocked path | blocked-path severity test red | **red:** `A_blocked_path_fault_raises_one_alarm_with_the_blocked_path_severity` + loop severity test |
| M8 skip `PublishStatusAsync` in the fault branch | endpoint blocked test red | **red:** `A_blocked_move_is_acknowledged_as_not_accepted_and_reports_a_fault_status` |
| M9 ack `Accepted = true` in the fault branch | same test red | **red:** same test |
| M10 mark the guard in the fault branch | code-review item (no test) | **green:** no test catches it — matches the lesson's own caveat |
| M11 accept any string as `faultCode` in `Evaluate` | `An_unknown_fault_code_is_rejected` red | **red only after repair.** In the committed code that test asserted the *constructor's* severity guard, so M11 stayed green. Fix the test to call `Evaluate(id, false, "Something Wrong")` and it turns red. |

Ten of eleven mutations are caught once the tests are fixed; M10 is a genuine
coverage gap the lesson already flags, and M11 shows the current
`An_unknown_fault_code_is_rejected` is not load-bearing.

## Note for the professor

- The lesson is clear and its 3-concept list is fully covered; no lesson defect
  found. One small clarification worth making for the next student: requirement
  12's `A_healthy_status_clears_the_blocked_path_latch` copy ("fault status →
  sweep publishes one; healthy status → sweep publishes nothing") reads as if
  `bus.Published` is empty after the first sweep. Say explicitly "assert the
  publish count does not increase" so the intended assertion is unambiguous.
- The reviewer baseline for this lesson (109 tests) predates the extra
  `Sweep_uses_the_injected_clock_not_UtcNow_for_the_liveness_check` test noted in
  review 15; the gate of 124 is still the right target (it already includes it),
  so no lesson change is needed — just noting the earlier review's 109/110
  discrepancy is not relevant here.

---

# Re-review — 2026-10-16: the fixes, re-verified

**Date:** 2026-10-16
**Review of:** the owner's uncommitted working tree on top of `be66445` ("M5:
Lesson 16 First Review"). `git status` shows five modified test files:
`src/FleetManager.Tests/AlarmEvaluatorTests.cs`,
`src/FleetManager.Tests/FleetServeLoopIntegrationTests.cs`,
`src/FleetManager.Tests/FleetServeLoopTests.cs`,
`src/ShuttleCore.Tests/RackTests.cs`,
`src/ShuttleCore.Tests/StateTransitionTests.cs`.
No production file changed in this round. This review modified no file under
`src/`.
**Scope:** the five blockers from the first review, lesson 16's acceptance
criteria, [record 23](../decisions/23-blocked-path-fault-and-second-alarm.md),
and the end-product spec's M5 line.
**Process note:** scratch mutation builds ran in `/tmp/opencode/l16rr` and were
deleted. No broker or container was started; `docker ps` shows none. `dotnet
build-server shutdown` was run. `git status` after the review is identical to
before it.

**Verdict change: none — `not yet` stands (one blocker).**

Four of the five blockers are genuinely fixed and I verified the semantics, not
just the color: the build error is gone, every test now passes a valid severity,
the two state tests no longer double-fault, and the recovery loop test is back to
a mutable clock. But blocker 4 (the route test) is **not** fixed — the owner
changed the blocked lane to another lane that is still on the route, so the test
is still red and the suite gate is still RC 1 (123/124). Two further test-quality
holes that the first review named are also still open: `An_unknown_fault_code_is_rejected`
still does not call `Evaluate`, and the loop test that claims to clear the
blocked-path latch does not actually pin clearing.

## Previous blockers — resolution status

| # | Blocker from the first review | Status | Evidence |
|---|---|---|---|
| 1 | Build error: `FleetServeLoopIntegrationTests.cs:39` one-arg `AlarmEvaluator` | **fixed** | `FleetServeLoopIntegrationTests.cs:39` is now `new AlarmEvaluator(AlarmSeverities.Critical, AlarmSeverities.Warning)`. `dotnet build` → RC 0, 0 warnings, 0 errors. |
| 2 | Every test passing `""` as a severity throws in the constructor | **fixed** | All `""` constructor args replaced with a valid severity in `AlarmEvaluatorTests.cs` and `FleetServeLoopTests.cs`. `FleetManager.Tests` now runs 46/46. |
| 3 | Two state tests call `RaiseFault` after `StartMove` already faulted | **fixed** | The `shuttle.RaiseFault(reason, Start);` lines are removed from `StateTransitionTests.cs` (both tests). Both pass; M1/M2/M3 below turn them red, so they are load-bearing. |
| 4 | `IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked` checks a route that includes the blocked lane | **NOT fixed** | `RackTests.cs:60-61` now blocks `A02-L01` and checks `A01-L01 → A02-L02`. Ordinals: `A01-L01`=0, `A01-L02`=1, `A02-L01`=2, `A02-L02`=3, so the blocked lane (2) is still inside the route span [0,3] and `IsRouteBlocked` correctly returns `True`; the test asserts `False` and fails (see gate output). |
| 5 | `A_recovered_shuttle_clears_the_latch_and_can_raise_again` rewritten to a pinned clock | **fixed** | `FleetServeLoopTests.cs:135-153` now uses `var now = Start; … () => now` with `now` advanced per sweep. It passes and the scenario is real again (31 s → alarm #1, +1 s heartbeat → clear, +31 s → alarm #2). |

### Two test-quality holes carried over, not fixed

- **`An_unknown_fault_code_is_rejected` still does not call `Evaluate`.**
  `AlarmEvaluatorTests.cs:116-119` is now
  `Assert.Throws<ArgumentOutOfRangeException>(() => new AlarmEvaluator(AlarmSeverities.Critical, "Something Wrong"));`
  — that fires the **constructor's** `blockedPathSeverity` guard, which is already
  covered by `An_unknown_severity_is_rejected` (`:74`). It never exercises
  `Evaluate`'s `faultCode` check. Mutation **M11** confirms it: deleting that
  check from `Evaluate` leaves the whole suite green.
- **`A_healthy_status_clears_the_blocked_path_latch` does not pin clearing through
  the loop.** `FleetServeLoopTests.cs:276-299` does two phases (fault → sweep,
  healthy → sweep) and asserts `Assert.Single(bus.Published)`. A *latched*
  evaluator also publishes nothing on the healthy sweep, so the assertion cannot
  tell "cleared" from "still latched". Requirement 12 asks for a third phase
  (fault again → publishes again). Mutation **M12** (remove the evaluator's
  clear-branch) is caught only by the *pure* test
  `A_cleared_blocked_path_fault_can_raise_again`; this loop test stays green.

## Gate results (re-run, verbatim)

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
   **RC 1**, 7 `Passed!` lines, **1 `Failed!`** line, **123** tests total
   (requested gate: RC 0, 8 `Passed!`, 0 `Failed!`, 124):
   ```
   Failed!  - Failed:     1, Passed:    28, Skipped:     0, Total:    29 - ShuttleCore.Tests.dll
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7 - Contracts.Tests.dll
   Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9 - Messaging.Tests.dll
   Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6 - TrafficCore.Tests.dll
   Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18 - FleetCore.Tests.dll
   Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2 - MqttMessaging.Tests.dll
   Passed!  - Failed:     0, Passed:    46, Skipped:     0, Total:    46 - FleetManager.Tests.dll
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7 - ShuttleSim.Tests.dll
   ```
   `FleetManager.Tests`=46 OK, `ShuttleSim.Tests`=7 OK, `ShuttleCore.Tests`=28/29.
   The single failure:
   ```
   Failed ShuttleCore.Tests.RackTests.IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked
     Assert.False() Failure
   Expected: False
   Actual:   True
   ```
3. Phases 1–5, 7 clean: files/names present; config keys present; `FaultAlarmCodes`
   and `FleetServeLoop` pure; `_knownLock` present and used; contract still v3, no
   `v4.md`; contracts/transport and protected files unchanged; no `docker` in
   tests. Phase 8 matches only pre-existing command-ack code (no later-M5 scope).

## Acceptance criteria (re-check)

| Req | Previous | Now | Evidence |
|---|---|---|---|
| 1–9 (production) | pass | pass | unchanged; build RC 0 |
| 10 `ShuttleCore.Tests` +4 | fail | **partial (gate red)** | Two state tests fixed and load-bearing; `MarkBlocked_for_an_unknown_lane_is_rejected` passes; `IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked` still fails. |
| 11 `ShuttleSim.Tests` +1 | pass | pass | `A_blocked_move_is_acknowledged_as_not_accepted_and_reports_a_fault_status` passes; M8/M9/M1 turn it red. |
| 12 `FleetManager.Tests` +10 | fail | **partial** | All 46 compile and pass; but `An_unknown_fault_code_is_rejected` is not load-bearing (M11) and `A_healthy_status_clears_the_blocked_path_latch` does not pin clearing (M12). Mapper pair correct. |
| 13/14/15 | pass | pass | unchanged; phase 4/5 `git diff --quiet` clean; no scope leak. |

## Remaining blockers

1. **blocker — `src/ShuttleCore.Tests/RackTests.cs:57-62` is still wrong.**
   Blocking `A02-L01` does not take it off the route `A01-L01 → A02-L02`; the
   blocked lane must be **outside** the route span, or the route must be changed.
   Simplest fixes: block `A02-L02` and check `A01-L01 → A01-L02` (span [0,1],
   blocked at 3 → `False`), or block `A01-L01` and check `A02-L01 → A02-L02`
   (span [2,3] → `False`). This is the one line keeping the gate red.

2. **should-fix — `An_unknown_fault_code_is_rejected` must call `Evaluate`.**
   Replace the constructor call with
   `var evaluator = new AlarmEvaluator(AlarmSeverities.Critical, AlarmSeverities.Warning);`
   then `Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate("SH-01", false, "Something Wrong"));`
   Requirement 7 specifies the `Evaluate` guard; today nothing tests it (M11).

3. **should-fix — `A_healthy_status_clears_the_blocked_path_latch` must re-fault.**
   Add the third phase (fault status again → sweep → second publish), as
   requirement 12 spells out, so the test actually distinguishes a cleared latch
   from a still-latched one (M12). Or rename it to what it proves
   ("a healthy sweep does not publish a second alarm").

## Mutation results (re-run in `/tmp/opencode/l16rr`)

I copied the working tree `src/` into `/tmp/opencode/l16rr`, repaired only the
one failing `RackTests` route line **in the copy** to get a green baseline
(RC 0, 8 `Passed!`, 124), then applied each mutation there. The workspace `src/`
was never touched. M10–M12 are the holes.

| Mutation | Targeted test | Result |
|---|---|---|
| M1 delete the `IsRouteBlocked` check in `StartMove` | 2 state tests | red: both state tests + endpoint blocked test |
| M2 check only the destination (`IsBlocked(toLane)`) | intermediate-lane test | red: `StartMove_through_a_lane_blocked_between_two_lanes_enters_fault` |
| M3 raise the fault with a different reason string | endpoint + mapper/loop | red: both state tests + endpoint test |
| M4 `ForReason` returns `null` for blocked path | mapper + 2 loop tests | red: mapper + 2 loop fault tests + healthy-latch |
| M5 pass the fault code always `null` in the sweep | 2 loop fault tests | red: 2 loop fault tests + healthy-latch |
| M6 key the evaluator latch on `shuttleId` alone | two-alarm test | red: two-alarm test + 5 more |
| M7 return the wrong severity for blocked path | blocked-path severity test | red: evaluator severity + loop severity |
| M8 skip `PublishStatusAsync` in the fault branch | endpoint blocked test | red: endpoint blocked test |
| M9 ack `Accepted = true` in the fault branch | endpoint blocked test | red: endpoint blocked test |
| **M10** mark the guard in the fault branch | code-review item | **green: no test catches it** (lesson caveat) |
| **M11** accept any `faultCode` in `Evaluate` | `An_unknown_fault_code_is_rejected` | **green: the test never calls `Evaluate`** (hole 2) |
| **M12** remove the evaluator's clear-branch for blocked path | `A_healthy_status_clears_the_blocked_path_latch` | **green for the loop test**; caught only by the pure `A_cleared_blocked_path_fault_can_raise_again` (hole 3) |

M1–M9 are load-bearing. M10 is the coverage gap the lesson already names. M11
and M12 are two tests whose names promise more than they assert.

## New explain-it questions (this round)

1. `RackTests.cs:57-62` blocks `A02-L01` and asks about `A01-L01 → A02-L02`.
   Walk the lane ordinals and say exactly why the method returns `True`. Then
   state the general rule for choosing a blocked lane when you want
   `IsRouteBlocked` to be `False`, and pick one valid pair for this rack.
2. `An_unknown_fault_code_is_rejected` constructs an evaluator with
   `"Something Wrong"` as the *second severity*. Which guard throws, and why does
   that make the test pass without testing the `faultCode` argument? Write the
   two-line fix that makes it fail when `Evaluate`'s check is removed.
3. A healthy status arrives after a fault. Trace, step by step, how the manager's
   latch for that shuttle gets cleared (`HandleStatusAsync` → `_faultCodes` →
   `Evaluate`). Why does `Assert.Single(bus.Published)` after the healthy sweep
   *not* prove the latch cleared, and what extra step does requirement 12 ask for?
4. The `AlarmEvaluator` clear-branch is `else if (faultCode != AlarmCodes.BlockedPath) _raised.Remove(blockedPath);`.
   Why is the `else if` needed at all, given the `if` above it already handles the
   blocked-path case? What happens to the latch on a healthy status if the
   `else if` is deleted — and which test (if any) notices?

## Revisit list (unchanged, plus)

- Choosing fixture data that actually exercises the branch under test (the route
  test and the cleared-latch test are the same lesson).
- A test name is a claim: make the assertions prove the claim, or rename it.
- Constructor validation vs. method validation — the two `AlarmEvaluator` guards
  are different and need different tests.
- Clock injection: fixed clock for "same instant", mutable clock for "over time"
  (blocker 5 is now a good example of getting this right — study the fix).

## End-product check

Unchanged from the first review, with one note. The production design is correct
and now builds clean; this re-review changes nothing about the end product's
shape. The route-test blocker is a test-only defect, but it is the test that
pins `Rack.IsRouteBlocked`'s inclusive-span rule — the exact rule the shuttle
relies on to decide a move is blocked. If that rule is wrong, a shuttle drives
into an obstacle and no `blockedPath` alarm fires. The lesson's own end-product
risk from the first review stands: the tests must be green *and* load-bearing,
because traffic control does not yet honor blocked lanes (record 23 §10).

## README

No change. M5 remains `in progress`, and the verification-note convention used by
prior passing lessons was only to append a slice note after a **pass**; this
re-review is not a pass. The milestone table is untouched.

---

# Re-review (pass 3) — 2026-10-16: second round of fixes

**Date:** 2026-10-16
**Review of:** the owner's uncommitted working tree on top of `be66445`. Since
pass 2, exactly three files changed:
`src/ShuttleCore.Tests/RackTests.cs`,
`src/FleetManager.Tests/AlarmEvaluatorTests.cs`,
`src/FleetManager.Tests/FleetServeLoopTests.cs`.
No production file changed in this round. This review modified no file under
`src/`. (`docs/decisions/24-aisle-lane-model-seam.md` is a separate untracked
record, out of scope, untouched.)
**Scope:** the three open items from pass 2, lesson 16's acceptance criteria,
[record 23](../decisions/23-blocked-path-fault-and-second-alarm.md), and the
end-product spec's M5 line.
**Process note:** scratch mutation builds ran in `/tmp/opencode/l16r3` and were
deleted. No broker or container was started; no `dotnet run` was left running.
`dotnet build-server shutdown` was run.

**Verdict change: none — `not yet (fail)` stands (one blocker, a red gate).**

Two of the three pass-2 items are genuinely fixed and I verified the semantics,
not just the colour: the route test now blocks a lane **outside** the route span
and is load-bearing, and `An_unknown_fault_code_is_rejected` now calls `Evaluate`
so M11 goes red. The third item was *attempted* but is now a **failing test**: the
owner added requirement 12's third phase (fault again → sweep) but kept
`Assert.Single`, which asserts exactly one publish while the correct behaviour
produces two. That one line keeps the suite gate red (123/124).

## Pass-2 items — resolution status

| # | Item from pass 2 | Status | Evidence |
|---|---|---|---|
| 4 | Route test blocked a lane still on the route | **fixed** | `RackTests.cs:60-61` now blocks `A02-L01` and checks `A01-L01 → A01-L02`. Ordinals: `A01-L01`=0, `A01-L02`=1, `A02-L01`=2 — blocked lane 2 is outside span [0,1] → `False`. Test passes. **M13** (force `IsRouteBlocked` always-true) turns this test red, so it is load-bearing against over-reporting. |
| 2 | `An_unknown_fault_code_is_rejected` did not call `Evaluate` | **fixed** | `AlarmEvaluatorTests.cs:115-120` is now `new AlarmEvaluator(AlarmSeverities.Critical, AlarmSeverities.Warning)` then `Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate("SH-01", false, "unknown-fault"))`. It exercises `Evaluate`'s guard, not the constructor's. **M11** (delete that guard) now turns it **red**. |
| 3 | `A_healthy_status_clears_the_blocked_path_latch` did not pin clearing | **attempted, now red** | `FleetServeLoopTests.cs:299-303` adds the third phase (fault status again → sweep at `:301`), but the assertion at `:303` is still `Assert.Single(bus.Published)`. Correct count is **2** (fault → publish #1, healthy → clear, fault again → publish #2). The test fails: `Assert.Single() Failure: The collection contained 2 items`. |

## Why the assertion is wrong (traced, not guessed)

1. `:291` fault status → `HandleStatusAsync` sets `_faultCodes["SH-01"] = blockedPath`.
2. `:293` sweep at `Start+1` → lost=false, faultCode=blockedPath → `_raised.Add` true → **publish #1**. Count = 1.
3. `:295` healthy status (idle, no reason) → `_faultCodes.Remove("SH-01")`.
4. `:297` sweep at `Start+16` → faultCode=null → `_raised.Remove(blockedPath)` → **latch cleared**, nothing published. Count = 1.
5. `:299` fault status again → `_faultCodes["SH-01"] = blockedPath` again.
6. `:301` sweep at `Start+1` → faultCode=blockedPath, latch was cleared → `_raised.Add` true → **publish #2**. Count = 2.
7. `:303` `Assert.Single` expects 1 → **fails**.

Requirement 12 explicitly asks for "fault status again → sweep publishes one
again", so **count 2 is the correct production behaviour** and the assertion is
the defect. The fix is `Assert.Equal(2, bus.Published.Count);`.

**On the `now` values (asked explicitly):** the third sweep uses `Start+1`, which
is *earlier* than the second sweep's `Start+16`. It does **not** change this
outcome, because `EvaluateAndPublishAsync` uses `now` only for
`_health.IsUnresponsive(id, now)` (`FleetServeLoop.cs:95`) and the envelope
`SentAt` (`:98`), and `HandleStatusAsync` records `lastSeen` from the pinned
`_clock()` = `Start` (`:69`). At `Start+1` the shuttle is 1 s stale (< 30 s), so it
is never lost and only the blocked-path edge is evaluated. Still, a sweep whose
clock moves backwards is a smell and will confuse the next reader — advance it
(for example `Start+32`) in the fix.

## Gate results (pass 3, verbatim)

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
   `---RC:1---`, `assemblies-passed=7 assemblies-failed=1 total=123`
   (requested gate: RC 0, 8 `Passed!`, 0 `Failed!`, 124):
   ```
   Passed!  - Failed:     0, Passed:    29, Skipped:     0, Total:    29, Duration: 430 ms - ShuttleCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18, Duration: 374 ms - FleetCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 111 ms - Messaging.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 198 ms - Contracts.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 992 ms - MqttMessaging.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 222 ms - TrafficCore.Tests.dll (net10.0)
   Failed!  - Failed:     1, Passed:    45, Skipped:     0, Total:    46, Duration: 563 ms - FleetManager.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 295 ms - ShuttleSim.Tests.dll (net10.0)
   ```
   `ShuttleCore.Tests`=29/29 OK, `ShuttleSim.Tests`=7/7 OK,
   `FleetManager.Tests`=45/46. The single failure:
   ```
   Failed FleetManager.Tests.FleetServeLoopTests.A_healthy_status_clears_the_blocked_path_latch [7 ms]
   Error Message:
     Assert.Single() Failure: The collection contained 2 items
   ```
3. Phases 1–5, 6b, 7 clean: files/names present; config keys present;
   `FaultAlarmCodes` and `FleetServeLoop` pure; `_knownLock` present and used;
   contract still v3, no `v4.md`; contracts/transport and protected files
   unchanged; all 15 anchored test names present; no `docker` in tests. Phase 8
   matches only pre-existing command-ack code (`ReliableCommandSender`,
   `Program.cs`, `AcknowledgeAsync`) — no `palletNotDetected`, no `reassign`, no
   new recovery.

## Acceptance criteria (pass 3)

| Req | Pass 2 | Now | Evidence |
|---|---|---|---|
| 1–9 (production) | pass | pass | unchanged; build RC 0 |
| 10 `ShuttleCore.Tests` +4 | partial | **pass** | Route test fixed and load-bearing (M13 red); all 29 green. |
| 11 `ShuttleSim.Tests` +1 | pass | pass | unchanged; M8/M9/M1 turn it red. |
| 12 `FleetManager.Tests` +10 | partial | **fail (gate red)** | `An_unknown_fault_code_is_rejected` now load-bearing (M11 red); but `A_healthy_status_clears_the_blocked_path_latch` is red — assertion contradicts requirement 12. |
| 13/14/15 | pass | pass | unchanged; phase 4/5 clean; no scope leak. |

## Remaining blockers / issues

1. **blocker — `src/FleetManager.Tests/FleetServeLoopTests.cs:303` asserts the wrong count.**
   `Assert.Single(bus.Published);` must be
   `Assert.Equal(2, bus.Published.Count);` for requirement 12's
   fault → healthy → fault-again sequence. This is the one line keeping the gate
   red. While there, advance the third sweep off `Start.AddSeconds(1)` so the
   clock moves forward (e.g. `Start.AddSeconds(32)`).
2. **consider — M10 is still green.**
   Marking the command guard completed inside the endpoint's fault branch is
   caught by no test (lesson 16 names this as a code-review item). Only worth a
   test if the owner wants duplicate-fault-command coverage; not required here.

No other issues. The route test, the unknown-fault-code test, the severity guard,
the double-fault state tests, and the recovery-clock test from earlier rounds are
all correct and load-bearing.

## Mutation results (pass 3, in `/tmp/opencode/l16r3`)

Baseline: copied `src/` into `/tmp/opencode/l16r3` and repaired **only** the one
broken assertion in the copy (`Assert.Single` → `Assert.Equal(2, ...)`) to reach
green (RC 0, 8 `Passed!`, 124). The workspace `src/` was never touched.

| Mutation | Targeted test | Result |
|---|---|---|
| M1 delete the `IsRouteBlocked` check in `StartMove` | 2 state tests | red: 2 state tests + endpoint blocked test |
| M2 check only the destination (`IsBlocked(toLane)`) | intermediate-lane test | red: `StartMove_through_a_lane_blocked_between_two_lanes_enters_fault` |
| M3 raise the fault with a different reason string | endpoint + mapper/loop | red: 2 state tests + endpoint test |
| M4 `ForReason` returns `null` for blocked path | mapper + loop fault tests | red: mapper + 2 loop fault tests + healthy-latch |
| M5 pass the fault code always `null` in the sweep | 2 loop fault tests | red: 2 loop fault tests + healthy-latch |
| M6 key the evaluator latch on `shuttleId` alone | two-alarm test | red: 6 tests |
| M7 return the wrong severity for blocked path | evaluator severity test | red: evaluator severity + loop severity |
| M8 skip `PublishStatusAsync` in the fault branch | endpoint blocked test | red: endpoint blocked test |
| M9 ack `Accepted = true` in the fault branch | endpoint blocked test | red: endpoint blocked test |
| **M10** mark the guard in the fault branch | code-review item | **green: no test catches it** (lesson caveat) |
| **M11** accept any `faultCode` in `Evaluate` | `An_unknown_fault_code_is_rejected` | **red now** — fixed; the test calls `Evaluate` |
| **M12** remove the evaluator's clear-branch for blocked path | healthy-latch test | **red now** — caught by the pure `A_cleared_blocked_path_fault_can_raise_again` **and** (with the corrected assertion) by `A_healthy_status_clears_the_blocked_path_latch` |
| **M13** `IsRouteBlocked` over-reports (always-true) | route false-test | **red** — `IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked` catches it |
| **M14** `IsRouteBlocked` under-reports (always-false) | route/state tests | **red** — the route false-test stays green (it asserts `False`), but the 2 `StartMove` state tests + endpoint test catch it |

Notes: M12's red-for-the-loop-test only holds with the *corrected* assertion; under
the current `Assert.Single` the loop test is already red unmutated and would be
green under M12 (count 1). That is further proof the current assertion is wrong
rather than merely weak. M13/M14 are the new pairs requested for the route test:
the false-test guards over-reporting, and the `StartMove` tests guard
under-reporting.

## New explain-it questions (pass 3)

1. `A_healthy_status_clears_the_blocked_path_latch` now feeds the fault a second
   time and asserts `Assert.Single(bus.Published)`. Walk the publish count across
   all three sweeps and say what the count actually is. Then say what requirement
   12 demands and what the assertion should be.
2. The third sweep passes `Start.AddSeconds(1)` while the second passed
   `Start.AddSeconds(16)`. Trace exactly how `now` is used in
   `EvaluateAndPublishAsync` and explain why the backwards clock does not change
   the result here — and why it is still worth fixing.
3. `RackTests.cs:57-62` blocks `A02-L01` and checks `A01-L01 → A01-L02`. Give the
   lane ordinals and the route span, and say why the method returns `False`. Then
   explain the difference between a mutation that makes `IsRouteBlocked`
   always-true (caught by this test) and one that makes it always-false (caught by
   the `StartMove` tests instead) — why does each test guard one direction?
4. `An_unknown_fault_code_is_rejected` now calls `Evaluate("SH-01", false, "unknown-fault")`.
   Which guard throws, and why would the previous version (passing
   `"Something Wrong"` as the constructor's second argument) stay green if the
   `Evaluate` guard were deleted?

## Revisit list (unchanged, plus)

- An assertion is part of the test's contract: `Assert.Single` claims "exactly
  one", and a two-publish scenario needs `Assert.Equal(2, …)`. Read every
  assertion as a claim before trusting the green/red.
- Non-monotonic injected clocks: a sweep that goes backwards hides nothing here
  only because the shuttle is never stale. Trace `now`'s full path before
  believing it is harmless.
- Constructor validation vs. method validation (still worth a second look; the
  fix for `An_unknown_fault_code_is_rejected` is a good model).
- Clock injection: fixed clock for "same instant", mutable clock for "over time".

## End-product check

The production design is unchanged and remains correct; this pass is again
test-only. Two of the three test defects that mattered for the end product are
now closed, and the route rule (`Rack.IsRouteBlocked`'s inclusive span) is now
pinned in both directions — a shuttle that fails to detect an obstacle is the
risk this rule guards, and M13/M14 show it is guarded. The one remaining blocker
is a self-contradictory assertion, not a production defect, but it keeps the gate
red, so the milestone cannot advance. Same end-product risk as before: the tests
must be green *and* load-bearing, because traffic control does not yet honour
blocked lanes (record 23 §10).

## README

No change. M5 remains `in progress`, and the verification-note convention used by
prior passing lessons was only to append a slice note after a **pass**; this
re-review is not a pass. The milestone table is untouched.

---

# Final confirmation — 2026-10-16: pass

**Date:** 2026-10-16
**Review of:** the owner's uncommitted working tree on top of `be66445`. The only
change since pass 3 is in `src/FleetManager.Tests/FleetServeLoopTests.cs`
(`A_healthy_status_clears_the_blocked_path_latch`): the assertion is now
`Assert.Equal(2, bus.Published.Count)` (`:308`) and the third sweep uses
`Start.AddSeconds(18)` (`:306`). The sweep clock now moves forward 1 → 16 → 18.
No production file changed; this review modified no file under `src/`.
**Scope:** the one blocker from pass 3, lesson 16's acceptance criteria,
[record 23](../decisions/23-blocked-path-fault-and-second-alarm.md), and the
end-product spec's M5 line.
**Process note:** the mutation ran in a throwaway copy `/tmp/opencode/l16final` and
was deleted. No broker or container was started; no `dotnet run` was left behind;
`dotnet build-server shutdown` was run.

**Verdict: pass.**

## The fix, confirmed

`FleetServeLoopTests.cs:299-308` now:
- re-feeds the blocked-path fault a second time with a fresh envelope (`faultJson2`, `Start+17`),
- sweeps with a forward-moving clock at `Start+18`,
- asserts `Assert.Equal(2, bus.Published.Count)`.

The trace is unchanged from pass 3 and now agrees with the assertion: publish #1
at `Start+1`, latch cleared on the healthy sweep at `Start+16`, publish #2 at
`Start+18`. `now` still only drives `IsUnresponsive` (`FleetServeLoop.cs:95`) and
the envelope `SentAt` (`:98`); at `Start+18` the shuttle (last seen `Start`) is
18 s stale, well under the 30 s window, so only the blocked-path edge is
evaluated. The backwards-clock smell from pass 3 is gone.

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
   `---RC:0---`, `assemblies-passed=8 assemblies-failed=0 total=124`:
   ```
   Passed!  - Failed:     0, Passed:    29, Skipped:     0, Total:    29, Duration: 388 ms - ShuttleCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 369 ms - TrafficCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 223 ms - Contracts.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9, Duration: 149 ms - Messaging.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 655 ms - MqttMessaging.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18, Duration: 159 ms - FleetCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 428 ms - ShuttleSim.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    46, Skipped:     0, Total:    46, Duration: 362 ms - FleetManager.Tests.dll (net10.0)
   ```
   `SUITE GATE OK` — RC 0, 8 `Passed!`, 0 `Failed!`, 124. `ShuttleCore.Tests`=29
   OK, `ShuttleSim.Tests`=7 OK, `FleetManager.Tests`=46 OK.
3. Phases 1–5, 6b, 7 clean: files/names present; config keys present;
   `FaultAlarmCodes` and `FleetServeLoop` pure; `_knownLock` present and used;
   contract still v3, no `v4.md`; contracts/transport and protected files
   unchanged; all 15 anchored test names present; no `docker` in tests. Phase 8
   matches only pre-existing command-ack code (`ReliableCommandSender`,
   `Program.cs`, `AcknowledgeAsync`) — no `palletNotDetected`, no `reassign`, no
   new recovery.

## M12 — the corrected test is load-bearing

Throwaway copy `/tmp/opencode/l16final` (deleted after). Baseline (unmutated
working tree) → RC 0, 8 `Passed!`, 124. Applied M12 (delete the evaluator's
`else if (faultCode != AlarmCodes.BlockedPath) _raised.Remove(blockedPath);`
clear-branch in `AlarmEvaluator.cs`):
```
M12 RC=1
  Failed FleetManager.Tests.AlarmEvaluatorTests.A_cleared_blocked_path_fault_can_raise_again
  Failed FleetManager.Tests.FleetServeLoopTests.A_healthy_status_clears_the_blocked_path_latch
    Assert.Equal() Failure: Values differ
    Expected: 2
    Actual:   1
    ...FleetServeLoopTests.cs(308,0)
   ```
The corrected loop test is now load-bearing: removing the clear-branch leaves the
latch set, so the third fault does not republish and the count is 1, not 2. (In
pass 3, with the old `Assert.Single`, this mutation left the loop test green —
which was itself proof the assertion was wrong.)

The full M1–M14 set was last run in pass 3 and is unchanged by this one-line
edit: M1–M9 red, **M10 green** (the lesson's named coverage gap), M11 red, M12
red, M13 (route over-report) red, M14 (route under-report) red. M12 was the one
re-run here because it is the mutation that this pass's change targets.

## Acceptance criteria

| Req | Pass 3 | Final | Evidence |
|---|---|---|---|
| 1–9 (production) | pass | pass | unchanged; build RC 0 |
| 10 `ShuttleCore.Tests` +4 | pass | pass | 29/29; route test load-bearing (M13/M14) |
| 11 `ShuttleSim.Tests` +1 | pass | pass | 7/7; M8/M9/M1 red |
| 12 `FleetManager.Tests` +10 | fail | **pass** | 46/46; `An_unknown_fault_code_is_rejected` (M11 red) and `A_healthy_status_clears_the_blocked_path_latch` (M12 red) both load-bearing |
| 13/14/15 | pass | pass | unchanged; phase 4/5 clean; no scope leak |

## Remaining blockers

None. The only open item is the lesson's own acknowledged coverage gap:

- **consider (pre-existing, lesson-named) — M10.** Marking the guard completed
  inside the fault branch (`ShuttleEndpoint`) is not covered by a test. The lesson
  calls this a code-review item, not a requirement; no action needed for this
  slice.

## Explain-it questions (carried, now all answerable)

The four questions from pass 3 stand and can now be answered against a green
suite:
1. Walk the publish count across all three sweeps of
   `A_healthy_status_clears_the_blocked_path_latch` — what is the count, and what
   does requirement 12 demand?
2. How is `now` used in `EvaluateAndPublishAsync`, and why does the sweep clock
   need to move forward?
3. Walk the lane ordinals for `RackTests.cs:57-62` and explain why `IsRouteBlocked`
   is `False`; why does M13 (always-true) hit this test but M14 (always-false) hit
   the `StartMove` tests instead?
4. Which guard does `An_unknown_fault_code_is_rejected` now exercise, and why did
   the old constructor-based version not test the `faultCode` argument?

## End-product check

This slice is complete and correct. The blocked-path fault is detected by the
shuttle's pure state machine, surfaced on the status lane, translated by the pure
`FaultAlarmCodes` mapper, and raised as a per-code, per-severity alarm with a
rising-edge latch that clears and can re-raise — all verified green and
mutation-proven (M1–M14, except the lesson-named M10). The route rule
(`Rack.IsRouteBlocked`'s inclusive span) is pinned in both directions, which is
the guard against a shuttle driving into an obstacle with no alarm. The known
end-product risk is unchanged and by design: the alarm is *knowledge, not action*
(record 23 §10) — traffic control still does not honor blocked lanes, a faulted
shuttle stays faulted, and the latch/fault codes are in-memory only (a manager
restart forgets them). Those are later M5 slices, not defects in this one.

## README

A verification note for this slice was appended to the **M5** (in progress) entry
in the README's Verification notes, matching the convention recorded for lessons
13–15. The milestone table is untouched and M5 stays `in progress` — reassignment,
operator-acknowledged recovery, and `palletNotDetected` remain, as does the M5
done-when proof.
