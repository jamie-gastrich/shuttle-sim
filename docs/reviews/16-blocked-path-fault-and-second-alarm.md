# Review 16 — Blocked-path fault and the second alarm (M5, lesson 16)

**Reviewer:** Reviewer agent
**Verdict: not yet**

Commit under review: `a2c63f3` ("M5: Lesson 16 First attempt"), base `cfc72a6`.
Working tree clean at review time.

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
