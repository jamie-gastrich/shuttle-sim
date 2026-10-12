# Review 10: The track as segments, the segment reservation, and the deadlock rule (lesson 10, first M4 slice)

**Date:** 2026-10-12
**Reviewed:** `8cc815a` (`M4: feat: add TrafficController and SegmentReservations for route management and reservation logic`) on top of `16352a6` (lesson 10 + hints + decision record 16, docs only) and `54a850b` (lesson 09 model answers, docs only). HEAD `8cc815a`; working tree clean at review start.
**Scope:** lesson 10 against its own assignment and [record 16](../decisions/16-traffic-segments-and-deadlock-rule.md), on top of [record 05](../decisions/05-shuttle-core-and-rack-model.md), [record 11](../decisions/11-task-lifecycle-and-queue.md), [record 15](../decisions/15-m3-end-to-end-assignment-and-restart.md) and [record 15a](../decisions/15a-task-dispatcher-and-db-test-fixtures.md). This is the first half of M4; the run-loop gate is explicitly the next lesson.
**Contract checked:** `docs/contract/v1.md` `Version: 1`, `docs/contract/v2.md` `Version: 2`, no `v3.md`; `docs/contract/`, `docker/Dockerfile.build`, `TaskDispatcher.cs` and `Program.cs` unchanged (`git diff --quiet` exit 0). Traffic is internal; no wire change. Correct.

**Verdict: not yet.**

**Why not yet.** The product code is correct and pure: `Track` derives its segments from the `Rack`, `SegmentReservations` is the single owner with a true two-pass atomic grant, `TrafficController` is the pinned two-step rule, and `TrafficDecision` matches the pinned record exactly. Every acceptance phase runs green in substance, the M3 integration tests still pass, and the fresh container exits 0. But the assignment's **Requirement 3 stress test is not implemented**: `Four_or_more_shuttles_never_share_a_segment_and_all_finish` (`TrafficControllerTests.cs:78-99`) builds four shuttles on **disjoint** routes with **no loop, no round, no release and no bounded finish**, and it stays green when the reservation table is broken to grant every request and overwrite owners (mutation M2). It is the M4 done-when's only collision/no-deadlock proof at the decision level, and it is vacuous. Two more tests that the lesson names as proof points do not prove what the lesson claims: the refusal test does not catch a write-as-you-check partial hold (M1), and the head-on test does not distinguish all-or-nothing from per-segment hold-and-wait (M7). The product can stay; the proof cannot.

M4 is **not** done on this slice for a second, independent reason the lesson states itself (`docs/lessons/10-track-segments-and-reservation.md:22,33,48`): the run-loop gate that calls `TrafficController` before `TaskDispatcher.AssignNextAsync` is the next lesson, so M4's done-when ("a stress scenario … **runs**") is not met end to end. README is left untouched (see [README](#readme)).

---

## Commands run, verbatim key output

Run from the repo root unless a phase says `src/`. Exit codes read directly. Mutations ran against a throwaway `git archive HEAD` copy at `/tmp/opencode/rev10`, deleted afterward; no experiment touched `src/`, and no file under `src/` was modified by this review.

### Environment

```
$ dotnet --version        → 10.0.112
$ docker --version        → Docker version 29.8.1, build 4a63305
$ docker compose -f docker/compose.yml ps
  NAME  IMAGE  COMMAND  SERVICE  CREATED  STATUS  PORTS      ← nothing up at review start, nothing up at end
$ grep -c '<Project Path' src/ShuttleFleet.slnx  → 16        ← 14 → 16 as record 16 §7 predicted
$ git status --short     → (clean)
```

### Phase 1 — the track, the traffic core and the pinned names exist

```
$ test -f src/ShuttleCore/Track.cs || echo missing Track.cs                                  (no output)
$ grep -qE 'class Track' src/ShuttleCore/Track.cs                          → Track class OK
$ grep -qE 'class SegmentReservations' src/TrafficCore/SegmentReservations.cs → SegmentReservations class OK
$ grep -qE 'class TrafficController' src/TrafficCore/TrafficController.cs  → TrafficController class OK
$ grep -qE 'record TrafficDecision' src/TrafficCore/TrafficDecision.cs     → TrafficDecision record OK
$ for m in Route SegmentIds IsSegment; do grep -qE "\b$m\b" src/ShuttleCore/Track.cs || echo missing $m; done   (no output)
$ for m in TryReserveAll Release OwnerOf SegmentsOwnedBy; do grep -qE "\b$m\b" src/TrafficCore/SegmentReservations.cs || echo missing $m; done   (no output)
$ for m in TryReserveRoute Release; do grep -qE "\b$m\b" src/TrafficCore/TrafficController.cs || echo missing $m; done   (no output)
$ grep -qE 'Track\(Rack' src/ShuttleCore/Track.cs                                     (match; no output)
$ grep -qE 'TryReserveAll' src/TrafficCore/TrafficController.cs                       (match; no output)
```

Phase 1: **pass.**

### Phase 2 — purity and no magic lane list

```
$ grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' src/TrafficCore --include='*.cs'      (no matches) rc 1
$ grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts|Postgres' src/TrafficCore --include='*.cs' --include='*.csproj'  (no matches) rc 1
$ grep -rnE '"A[0-9]{2}-L[0-9]{2}' src/TrafficCore --include='*.cs'                                          (no matches) rc 1
$ grep -rniE 'Npgsql|MQTTnet|Postgres|Messaging|Contracts' src/ShuttleCore --include='*.cs' --include='*.csproj'  (no matches) rc 1
$ grep -c 'PackageReference' src/TrafficCore/TrafficCore.csproj   → 0
$ grep -c 'ProjectReference' src/TrafficCore/TrafficCore.csproj   → 1
```

Phase 2: **pass** as written. Note the literal-lane grep scans `TrafficCore` only — see mutation M6 and lesson defect **L2**: a hard-coded lane list in `ShuttleCore/Track.cs` slips past this gate and every test.

### Phase 3 — build and the pure suite with no database (the container's command)

```
$ cd src && dotnet build
  Build succeeded. 0 Warning(s) 0 Error(s)                                                   exit 0
$ dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"                       RC 0
  Passed!  - Failed: 0, Passed:  8, … Messaging.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 25, … ShuttleCore.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed:  4, … Contracts.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 18, … FleetCore.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed: 11, … FleetManager.Tests.dll (net10.0)
  Passed!  - Failed: 0, Passed:  6, … TrafficCore.Tests.dll (net10.0)
  assemblies-passed=8 assemblies-failed=0 total=79
  suite gate OK
  FAIL: TrafficCore.Tests should show 6
  FAIL: ShuttleCore.Tests should show 25
```

The suite gate **passes** exactly (RC 0, 8 `Passed!`, 0 `Failed!`, 79 tests, `TrafficCore.Tests` 6, `ShuttleCore.Tests` 25). The two per-assembly lines print `FAIL` **on a correct tree**: the lesson's grep `TrafficCore\.Tests\.dll.*Passed: +6,` puts the assembly name before `Passed:`, but `dotnet test -v m` prints `Passed:     6, … - TrafficCore.Tests.dll`. The regex is reversed and can never match. The numbers are right; the gate string is wrong. See lesson defect **L1**.

### Phase 3b — the nine new pure tests are really there, by anchored name

```
$ dotnet test ShuttleFleet.slnx --list-tests
    ShuttleCore.Tests.TrackTests.Route_between_a_lane_and_itself_is_that_single_segment
    ShuttleCore.Tests.TrackTests.Route_covers_every_segment_between_two_lanes_in_order
    ShuttleCore.Tests.TrackTests.Route_to_an_unknown_lane_is_rejected
    TrafficCore.Tests.TrafficControllerTests.TryReserveRoute_grants_the_whole_route_when_every_segment_is_free
    TrafficCore.Tests.TrafficControllerTests.TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken
    TrafficCore.Tests.TrafficControllerTests.Release_frees_every_segment_the_owner_held
    TrafficCore.Tests.TrafficControllerTests.Two_shuttles_cannot_hold_the_same_segment
    TrafficCore.Tests.TrafficControllerTests.Head_on_requests_resolve_without_deadlock
    TrafficCore.Tests.TrafficControllerTests.Four_or_more_shuttles_never_share_a_segment_and_all_finish
  (the anchored name walk printed "present:" for all nine)
```

Phase 3b: **pass.**

### Phase 4 — no test side-file launches docker, and the traffic tests are pure

```
$ grep -rnE 'Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))' src/*.Tests --include='*.cs'
  src/FleetManager.Tests/StartTests.cs:17:        using var process = Process.Start(psi)!;
  src/ShuttleSim.Tests/DemoTests.cs:18:        using var process = Process.Start(psi)!;
  → FAIL: a test must not start docker
$ grep -rn 'Trait' src/TrafficCore.Tests --include='*.cs'                                                    (no matches) rc 1
$ grep -rniE 'Npgsql|Postgres' src/TrafficCore.Tests --include='*.cs' --include='*.csproj'                  (no matches) rc 1
```

Phase 4: the traffic-test purity checks **pass**; the no-docker check **false-positives** on the committed tree. `StartTests.cs:17` and `DemoTests.cs:18` spawn `dotnet` (not docker) and predate this commit (`e187056` and `64fbaa9`). None of the files in `8cc815a` matches. The requirement ("no test starts docker") is met; the widened gate is over-broad. See lesson defect **L3**.

### Phase 5 — solution and project graph

```
$ grep -c '<Project Path' src/ShuttleFleet.slnx   → 16
$ grep -q 'TrafficCore' src/ShuttleFleet.slnx              → OK
$ grep -q 'TrafficCore.Tests' src/ShuttleFleet.slnx        → OK
$ grep -q 'ShuttleCore' src/TrafficCore/TrafficCore.csproj → OK
```

Phase 5: **pass.**

### Phase 6 — protected files unchanged, contract still v2, dispatcher shape intact

```
$ git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs docs/contract docker/Dockerfile.build
  → OK: dispatcher, Program.cs, contract and Dockerfile unchanged
$ grep -qE '^Version: 2$' docs/contract/v2.md     → v2 OK
$ grep -qE '^Version: 1$' docs/contract/v1.md     → v1 OK
$ test ! -f docs/contract/v3.md                   → no v3 OK
$ for m in AssignNextAsync StartAsync CompleteAsync FailAsync; do grep -qE "${m}\s*\(" src/FleetManager/TaskDispatcher.cs || echo missing $m; done   (no output)
```

Phase 6: **pass.**

### Regression — M3 integration still passes, against a live database

```
$ docker compose -f docker/compose.yml up -d            (broker + database up)
$ cd src
$ dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m \
    --logger "console;verbosity=detailed" --filter "Category=Integration"               RC 0
  Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks [442 ms]
  Passed FleetManager.Tests.EndToEndTests.Three_shuttles_finish_queue_and_state_survives_restart [183 ms]
  Passed: 2
$ docker compose -f docker/compose.yml down             (broker + database removed)
```

Both M3 DB tests still execute and pass; nothing regressed.

### Fresh container — no database, exit 0

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build .     exit 0
$ docker run --rm shuttle-fleet-build                                   exit 0, 8 Passed! lines
  Passed!  … TrafficCore.Tests.dll (net10.0)   ← the new suite is in the DB-less gate
  Passed!  … ShuttleCore.Tests.dll (net10.0)   ← 25 (22 + 3 track tests)
  Passed!  … FleetManager.Tests.dll (net10.0)  ← 11 (integration excluded)
```

The container has no database, so this is green only because the two `Integration` tests were excluded by the trait filter. The two new projects build and run inside the image.

**Phase summary:** all six phases are green in substance; Phase 3's per-assembly greps and Phase 4's no-docker grep misfire because of lesson defects L1 and L3. Both regressions pass.

---

## Acceptance criteria

Checked against lesson 10's six Requirements (R1–R6) and its acceptance phases. Evidence is a command in this repo or a quoted file/line.

| # | Criterion | Evidence | Result |
|---|---|---|---|
| 1 | R1: `Track.cs` exists, `public sealed`, ctor `Track(Rack)`, `SegmentIds`, `IsSegment`, `Route` | Phase 1; `Track.cs:3-45` | met |
| 2 | R1: `SegmentIds` ordered, one per lane, segment id = lane id | `Track.cs:11,14`; cross-aisle probe printed `A01-L01 A01-L02 A02-L01 A02-L02` | met (order implicit — Finding 4) |
| 3 | R1: `Route` inclusive of both ends; same-lane returns one; unknown lane throws `ArgumentException` | `Track.cs:26-45`; mutations M4/M5 red; the three `Route_*` tests pass | met |
| 4 | R1: no hard-coded lane list and no I/O in `Track.cs` | committed file clean; **but** mutation M6 shows the requirement is unguarded (tests and Phase-2 grep both miss an 8-lane literal) | met in the committed code, **not gated** |
| 5 | R1: three `TrackTests` `[Fact]`s, each with an `Assert` | `TrackTests.cs:5-35`; Phase 3b names present | met |
| 6 | R2: `TrafficCore` package-free, one `ProjectReference` to `ShuttleCore` | `TrafficCore.csproj:9-11`; Phase 2 counts 0 and 1 | met |
| 7 | R2: `SegmentReservations` pinned members and two-pass all-or-nothing | `SegmentReservations.cs:7-52`; Phase 1 | met (product) |
| 8 | R2: `TrafficController` pinned members; calls atomic `TryReserveAll`; refusal names segment + owner | `TrafficController.cs:16-41`; Phase 1; `Assert.Contains("A01-L02 held by Shuttle1", …)` passes | met |
| 9 | R2: `TrafficDecision` record exactly as pinned | `TrafficDecision.cs:1` matches `(bool Granted, string ShuttleId, IReadOnlyList<string> Segments, string? Reason)` | met |
| 10 | R2: no forbidden clock/id/IO/literal tokens; never a partial write | Phase 2 clean; mutation M1 **not caught** by the tests (see Finding 2) | product met; proof partial |
| 11 | R3: `TrafficCore.Tests` is a pure xUnit project, template placeholder gone, six `[Fact]`s | Phase 3b (six names, total 6); Phase 4 (no `Trait`, no DB) | met |
| 12 | R3: **stress test** — 4+ shuttles with overlapping routes; loop grant/release; per-round no-double-owner assert; all finish within a bounded number of rounds | `TrafficControllerTests.cs:78-99`: routes disjoint, no loop, no rounds, no release, no bound; only two trivial asserts | **not met (blocker)** |
| 13 | R4: both projects added; solution 14 → 16 | Phase 5, `grep -c` = 16 | met |
| 14 | R5: `ShuttleCore` gains only `Track` and stays pure | Phase 2; commit adds only `Track.cs` + tests | met |
| 15 | R6: dispatcher, `Program.cs`, contract, Dockerfile unchanged | Phase 6 | met |
| 16 | Phase 3: build clean; RC 0; 8 `Passed!`; 0 `Failed!`; 79 tests; `TrafficCore.Tests` 6; `ShuttleCore.Tests` 25 | verbatim above | met (two gate greps broken by L1) |
| 17 | Phase 3b: nine pinned test names present, anchored | verbatim above | met |
| 18 | Phase 4: no test starts docker; traffic tests pure | traffic purity met; no-docker grep false-positives (L3) | met in substance, gate misfires |
| 19 | Phase 5/6: project graph and protected files | verbatim above | met |
| 20 | Regression: M3 integration still passes; fresh container green | verbatim above | met |
| 21 | M4 done-when: 4+ shuttle stress scenario **runs** with no collisions/no permanent deadlock; rule written and explained | rule in record 16 ✅; stress scenario **not run** and its decision-level test is vacuous ❌; run-loop deferred ❌ | **not met** |

**17 fully met; 2 met in the product but not gated or proven (rows 4 and 10); 2 not met (rows 12 and 21, both aspects of the same central proof).** The product is sound; the M4 proof is missing.

---

## Mutation testing (throwaway `git archive HEAD`, `/tmp/opencode/rev10`)

Baseline in the copy: pure suite 8 `Passed!` / 79 tests; `TrafficCore.Tests` 6. Each mutation was applied to the committed tree in the copy, the relevant suite run, then the file restored from a pristine backup and the next mutation applied. The copy was verified byte-identical to pristine afterwards.

| # | Mutation | File / line | Red test(s) | Observed |
|---|---|---|---|---|
| **M1** | Write-as-you-check in `TryReserveAll` (partial hold on refusal) | `SegmentReservations.cs:11-25` | lesson says `TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken` | **GREEN** — not caught. A probe that conflicts on a *later* segment fails (`Expected: empty, Actual: [A01-L01]`), proving the mutation is a real partial hold the owner's test misses |
| **M2** | Drop the "different owner" check (grant every request, overwrite owners) | `SegmentReservations.cs:11-18` | `Two_shuttles_cannot_hold_the_same_segment`, `Head_on…`, `refuses…` | **RED for the three**; **`Four_or_more_shuttles…` stays GREEN** |
| **M3** | `Release` frees only the first segment the owner holds | `SegmentReservations.cs:28-37` | `Release_frees_every_segment_the_owner_held` | **RED** |
| **M4** | `Track.Route` excludes the end (`+1` removed) | `Track.cs:44` | `Route_covers_every_segment_between_two_lanes_in_order` and `Route_between_a_lane_and_itself…` | **RED** (both) |
| **M5** | `Track.Route` stops rejecting unknown lanes | `Track.cs:28-36` | `Route_to_an_unknown_lane_is_rejected` | **RED** |
| **M6** | `Track` derives segments from a hard-coded 8-lane list instead of the rack | `Track.cs:11` | (none expected) | **all 79 GREEN**; Phase-2 `TrafficCore` literal grep clean; a direct `grep 'A[0-9]{2}-L[0-9]{2}' ShuttleCore/Track.cs` finds the literals |
| **M7** | `TrafficController` reserves per segment (route given one segment at a time, no rollback) | `TrafficController.cs:18-19` | lesson says `Head_on_requests_resolve_without_deadlock` | **all six GREEN**, including `Head_on…` |
| **M8** | M2 applied, then the lesson-spec stress test (overlapping routes, loop, per-round assert, bounded rounds) added to the copy | `SegmentReservations.cs:11-18` | owner's stress test | **owner's `Four_or_more…` GREEN; lesson-spec probe RED** on the per-round no-double-owner assert |

**Read it back.**

- Five of the eight mutations are load-bearing somewhere: M2, M3, M4, M5 and (via M2) M8 turn a named test red. The product's core is genuinely covered for the ordinary cases.
- **Three mutations that should be caught are not.** M1 (partial hold) leaves the refusal test green because the conflict sits on the *first* segment of the route; M7 (hold-and-wait) leaves every test green because no test ever retries after a refusal; M6 (hard-coded lanes) leaves every test green because the hard-coded list happens to cover the largest rack the tests use.
- **M8 is the decisive one.** The lesson's own proof details (`docs/lessons/10-track-segments-and-reservation.md:668`) claim M2 and M7 "must go red". They do not. A stress test with overlapping routes, a retry loop, a per-round invariant and a round bound — the test the assignment asked for — *does* turn M2 red. The owner's `Four_or_more…` test is not that test.
- **The lesson's stated mutation expectations are wrong for M1 and M7** (see lesson defects **L4**). That is a lesson defect, but it also means a reviewer following the lesson's promised proof would have signed off on a suite that proves much less than it appears to.

---

## Substance checks (read the code, with line numbers)

### Four ingredients: get-route + atomic-reserve + decision; refusal names the blocker

`TrafficController.TryReserveRoute` is exactly the rule (`TrafficController.cs:16-36`): `_track.Route(fromLane, toLane)` at `:18`, `_reservations.TryReserveAll(shuttleId, route)` at `:19`, a `TrafficDecision` on both paths (`:23`, `:33-35`). On refusal it scans the route for a segment owned by someone else (`:26-27`) and builds `"{segment} held by {owner}"` (`:35`). `Route` returns lower→higher, so the first blocker in route order is named. The `blockedSegment is null` branch (`:29-35`) can only occur on a race with a second caller, which record 16 §4 forbids; the `"route unavailable"` fallback is defensive. The rule is all-or-nothing in `SegmentReservations`: check every requested segment first (`:11-18`), and only if none has a *different* owner write them all (`:20-23`). `Distinct` at `:9` makes a repeated segment id harmless. Correct.

### `SegmentReservations` is the single owner

One mapping, `_ownerBySegment` (`:5`), ordinal-keyed. No second copy exists anywhere (`grep` for `OwnerOf`/`SegmentsOwnedBy` outside this file finds only the controller and tests). `OwnerOf` (`:39-44`) and `SegmentsOwnedBy` (`:46-52`) are the read side, used by the controller and the tests. `Release` (`:28-37`) materialises the matching keys before removing, so it is not mutating while enumerating. This matches record 16 §4. The class is thread-unsafe by design (plain `Dictionary`); record 16 §4 says the run loop is the single caller, so that is deferred correctly — but see Finding 5.

### `TrafficDecision` shape

`TrafficDecision.cs:1` is byte-for-byte the pinned record: `public sealed record TrafficDecision(bool Granted, string ShuttleId, IReadOnlyList<string> Segments, string? Reason);`. Field names, types and order match the lesson and record 16 §5.

### Track grammar and counts

`Track` is `sealed`, takes a `Rack`, and derives `_segments` from `rack.LaneId` (`Track.cs:7-12`) with no literals and no I/O. `IsSegment` is membership with an ordinal comparer plus a null/whitespace guard (`:16-24`). `Route` rejects either unknown lane with `ArgumentException` (`:28-36`) and returns `Skip(start).Take(end - start + 1)` (`:44`) — inclusive, same-lane → one segment (start == end). `ShuttleCore.Tests` is 25 = 22 + 3, exactly as the lesson predicted. The one gap is the *source of the order*: `rack.LaneId` is `_laneOrdinals.Keys` (`Rack.cs:42`), and `Track` relies on the dictionary's enumeration order. It happens to be aisle-major insertion order today, and the cross-aisle probe confirmed correct behaviour; the tests never exercise it (all three `TrackTests` routes are inside `A01`), and no other test crosses aisles. See Finding 4.

### The stress test is not the required stress test

`Four_or_more_shuttles_never_share_a_segment_and_all_finish` (`TrafficControllerTests.cs:78-99`) builds `Rack(1, 8, 2)`, four shuttle ids (`:83`) and four **disjoint** route pairs (`:84-90`): L01→L02, L03→L04, L05→L06, L07→L08. It calls `TryReserveRoute` once per shuttle (`:92-95`) and asserts all four were granted (`:97`) and that the union of segments has 8 distinct entries (`:98`). There is no overlap, so no request can ever be refused; no round loop, no completion, no `Release`, no per-round double-owner assert, and no round bound. `Assert.Equal(8, …Distinct().Count())` is true for four disjoint 2-segment routes regardless of the reservation logic. Mutation M2 proves it: break ownership entirely and this test still passes. This is the M4 done-when's only decision-level collision proof, and it proves nothing. Requirement 3 lists all four properties explicitly; none is present.

### Test quality

Every `[Fact]` has at least one `Assert`. `TryReserveRoute_refuses…` (`:21-35`) does check `reservations.SegmentsOwnedBy("Shuttle2")` is empty (`:33`) and the reason string (`:34`) — that is the right *shape* for the atomicity assertion, but the scenario puts the conflict on the first segment, so a write-as-you-check table still passes (M1). `Release_frees…` (`:37-49`) is real (M3 red). `Two_shuttles…` (`:51-63`) is real (M2 red). `Head_on…` (`:65-76`) is a near-duplicate of `Two_shuttles…`: same two requests, same two asserts, and it never retries, so it cannot distinguish all-or-nothing from hold-and-wait (M7 green). The `grants…` test (`:7-19`) checks all four decision fields. The tests use `new Rack(...)` fixtures throughout — no literal-lane track construction — which is correct.

### csproj sanity and commit hygiene

`TrafficCore.csproj` is a plain library: `net10.0`, implicit usings, nullable, one `ProjectReference` to `ShuttleCore`, zero packages. `TrafficCore.Tests.csproj` is a standard xUnit project whose four package versions (`coverlet.collector` 6.0.4, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4) are identical to `FleetManager.Tests` and `ShuttleCore.Tests`. `TrackTests.cs` builds a real `Rack`. The product commit `8cc815a` touches exactly the nine expected files and nothing else — no `TaskDispatcher`, no `Program.cs`, no contract, no Dockerfile. The two docs commits are clean: `16352a6` adds only lesson 10, its hints and record 16 (no `src/`); `54a850b` adds only model answers to lesson 09. No churn.

### Lesson 09 model-answer edit (`54a850b`)

Aligned and correct. It adds an indented model answer under all nine "Say it out loud" questions and updates the lead-in. Spot-checked citations against the current files: Q2 → `TaskDispatcherTests.cs:7,20` ✅ (line 7 `BaseTime`, line 20 the `AssignNextAsync` call); Q3 → `TaskDispatcher.cs:30/31/32` ✅ and `TaskDispatcherTests.cs:56/57` ✅ (save-count and state asserts); Q4 → `TaskDispatcherTests.cs:73/81` ✅ (driver and assert both `index % shuttleIds.Length`); Q6 → `EndToEndTests.cs:50-53` ✅ (the `task-hold` create/add/save); Q7 → `EndToEndTests.cs:55-59` ✅ (fresh repo/board/reload); Q8 → `DatabaseCollection.cs:3` ✅; Q9 → `TestDatabase.cs:37` ✅ (sentinel `GetAsync`). The two answers review 09 did **not** cover — Q2 (`now` as a parameter and deterministic tests) and Q5 (why the end-to-end test cannot also prove mixed states, and that `RestartSurvivalTests` does) — are both correct; Q5's "six rows spanning Queued/Queued/Assigned/InProgress/Done/Failed" matches `RestartSurvivalTests.cs:18-36` exactly. No defect.

---

## What is solid

- **The pure boundary is real and enforced.** `TrafficCore.csproj` has no packages, the Phase-2 greps find no clock, id, I/O or literal token in the whole project, and the project compiles against `ShuttleCore` alone. The compiler, not a reviewer, prevents traffic from reaching a broker or database. That is record 16 §2 done properly.
- **The atomic grant is written correctly.** `TryReserveAll` checks all then writes all (`SegmentReservations.cs:11-25`); `Release` frees every segment the owner holds (`:28-37`); `OwnerOf`/`SegmentsOwnedBy` are a clean read side. The product rule the next lesson will call is exactly right.
- **The decision record is honest and complete.** Record 16 surveys three options for each of three choices, names the rule, explains why removing hold-and-wait removes deadlock, states the trade-offs (starvation, blocked dead shuttle, lower throughput) and what would change each choice, and its verification table marks the implementation "not yet verified" and leaves it to the reviewer (`docs/decisions/16-…md:142`). That is the standard.
- **No throwaway work and no churn.** The track, the table and the controller are exactly what the run loop will call; the two docs commits are clean; protected files are untouched.
- **The pinned API is exact.** All nine test names, all class/record/member names and the `TrafficDecision` field order match the lesson and record 16.

---

## Findings (top issues, ranked)

**1. BLOCKER — the M4 stress test does not exist; `Four_or_more_shuttles_never_share_a_segment_and_all_finish` is vacuous.** `TrafficControllerTests.cs:78-99` uses four **disjoint** routes, calls each once, and asserts only "all granted" and "8 distinct segments". It has no overlapping routes, no loop, no grant-then-complete-then-release, no per-round double-owner assertion, and no bounded round count — every one of the four properties Requirement 3 names. Mutation M2 (grant everything, overwrite owners) leaves it green; a lesson-spec probe with overlapping routes and a retry loop turns red under the same mutation. **Why it matters for a real shuttle system:** this test is the *only* decision-level evidence for "no collisions and no permanent deadlock", which is M4's done-when. As written it would certify a reservation table that lets two shuttles hold the same lane. That is a collision, and in a real warehouse it is a crashed shuttle and a damaged pallet. Fix: build 4+ shuttles with overlapping routes (the lesson's toy uses L01→L04, L06→L03, L02→L05, L05→L02 on a 6-lane rack), loop `grant if the whole route is free → complete → release`, assert after every round that no segment is held by two shuttles, and assert every shuttle finishes within a bound a deadlock would blow past (the toy drains in 5 rounds; a 4-shuttle bound of, say, 40 is honest and not circular).

**2. SHOULD-FIX — the refusal test does not prove "holds nothing"; a write-as-you-check table passes it.** `TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken` (`:21-35`) requests the *same* route the first shuttle already holds, so the conflict is on the first segment and a write-as-you-check `TryReserveAll` returns before writing. Mutation M1 stays green. The test's assertion (`Assert.Empty(SegmentsOwnedBy("Shuttle2"))`, `:33`) is the right assertion; the scenario does not exercise it. **Why it matters:** the atomic two-pass is the whole reason a refused shuttle "holds nothing" — the property that removes hold-and-wait and therefore deadlock (record 16 §5). If it regressed, M1 says no test would notice. Fix: make the first shuttle hold a *later* segment (for example reserve L02→L02), then have the second request L01→L02 and assert it holds nothing and is refused.

**3. SHOULD-FIX — the head-on test does not test deadlock; it duplicates `Two_shuttles…`.** `Head_on_requests_resolve_without_deadlock` (`:65-76`) issues the same two overlapping requests as `Two_shuttles_cannot_hold_the_same_segment` (`:57-58`) and asserts the same two booleans. It never retries, so it can never observe a standstill. Mutation M7 — a per-segment, hold-and-wait controller, exactly the design the rule rejects — leaves it green. **Why it matters:** the rule's entire value is that a refused shuttle waits holding nothing and then goes; a test that only asks once cannot tell avoidance from a deadlock that would form on the second attempt. Fix: loop round-robin over the shuttles, retrying refused requests, until all finish or a bound is hit, and assert they all finish (or fold this into Finding 1's stress test and delete the near-duplicate).

**4. SHOULD-FIX — `Track`'s segment order depends on `Dictionary` enumeration order and is untested outside one aisle.** `Track.cs:11` does `rack.LaneId.ToList()`, and `Rack.LaneId` is `_laneOrdinals.Keys` (`Rack.cs:42`). Dictionary key enumeration is not a documented contract; it is insertion-ordered in current .NET, so the behaviour is right today (the cross-aisle probe returned `A01-L01 A01-L02 A02-L01 A02-L02` and `Route("A01-L02","A02-L01")` = `A01-L02, A02-L01`). But the lesson and record 16 §3 say to order the lanes the way `Rack.DistanceInLanes` orders them, and the tests only route within one aisle (and only ever two segments). A future `Rack` change or a platform difference would break `Route` silently, and `Track.SegmentIds` is what `Route` indexes into. **Why it matters:** the route decides which lanes a shuttle reserves; a wrong order reserves the wrong space. Fix: derive the order explicitly (order by `DistanceInLanes` from the first lane, or have `Rack` expose an ordered list), and add a cross-aisle / longer-route test.

**5. CONSIDER — the table and controller are not thread-safe, and the traffic decision leaves no trace.** `SegmentReservations` is a plain `Dictionary` with no lock; `TrafficController` holds it directly. Record 16 §4 makes the run loop the single caller, so this is fine *now*, but the run loop is the next lesson and it will call this per shuttle. If two calls overlap, `TryReserveAll`'s check-then-write has a classic time-of-check race: both can pass pass-one and both write, and because the table stores one owner per segment, a collision is recorded as a silent overwrite rather than an error. Also, nothing logs a refusal: the `Reason` string is returned but dropped by every caller and every test. **Why it matters:** a support engineer reconstructing why a shuttle waited needs the `(shuttle, route, blockedSegment, owner)` line, and the concurrency question must be answered in the run-loop lesson before it is a bug. Neither is a defect in this slice; both should be named in the next lesson.

---

## Review dimensions (the "what to judge" pass)

**Messaging — N/A and deliberately so.** No wire message changes (Phase 6; contract v1/v2, no v3). `TrafficCore` has no `MQTTnet`/`Messaging`/`Contracts` token (Phase 2, rc 1). Traffic is internal to the fleet manager; record 16 §"Wire contract" states this. Nothing was smuggled in.

**State — explicit, single-owner, restart-irrelevant in this slice.** Reservation state is one `Dictionary<string,string>` in one class. There is no persistence and none is needed: reservations are in-process decisions held for the duration of a move, released on `Release`. A fleet-manager restart drops them, which is correct — the run loop re-derives route safety after restart. The safety property that *is* stateful — "a waiting shuttle holds nothing" — is enforced structurally by the two-pass, not stored anywhere.

**Concurrency — correct single-caller shape, unprotected shared state.** See Finding 5. The `async`/`CancellationToken` dimension does not arise: the traffic code is synchronous and pure, which is exactly what the compiler-enforced package boundary buys. No timers, no clock, no sleeps (Phase 2).

**Separation — the point of the slice, and it holds.** Decision logic (route, reservation, rule) lives in a package-free `TrafficCore`; the task world stays in `FleetCore`; `FleetManager` is untouched and will grow only the wiring. Unit tests drive it with no broker and no database.

**Configuration and logging — no configuration needed; logging deferred.** There are no timeouts, retry counts or sizes in this slice, so there is nothing to configure (record 16 §3). Test shuttle ids and rack sizes are fixtures, which the lesson permits. Logging is absent (Finding 5); the pure project cannot use `ILogger` without a package, so the run loop is the right place to log the decision.

**Failure behavior — refusal is the only failure mode and it is safe.** A refused route writes nothing and names the blocker; a granted route is atomic. The "dead shuttle blocks its route until `Release`" case is a designed trade-off (record 16 §5) with the hook (`Release`) in place for M5. Mutation M3 shows `Release` frees everything; mutation M1 shows the partial-hold failure would be silent if it ever appeared.

**Language quality — idiomatic, two cosmetic items.** `Track` is `sealed`; `SegmentReservations` and `TrafficController` are `public class` where the lesson's shapes say `public sealed class` — nothing subclasses them, so sealing is the `dotnet-style` choice and would document intent. Null guards in the controller constructor (`TrafficController.cs:12-13`) and `ArgumentNullException.ThrowIfNull(rack)` in `Track` (`:9`) are nice. Naming (`TryReserveAll`, `OwnerOf`, `SegmentsOwnedBy`, `_ownerBySegment`) is clear. The comparator is `StringComparer.Ordinal` throughout, consistent with the rack. `TryReserveRoute` returns a fresh `List<string>` typed as `IReadOnlyList<string>`, which callers could downcast; not worth changing at this size.

---

## Lesson defects (for the professor)

Lesson 10, its hints and record 16 are *mostly* buildable as written: the pinned names are consistent across all three, the solution goes 14 → 16 exactly as predicted, the pure suite goes 70 → 79 exactly (`ShuttleCore.Tests` 22 → 25, `TrafficCore.Tests` 0 → 6), the two-pass/grant/release shapes are unambiguous, the concept list is exactly three (track-as-segments, segment reservation, deadlock rule) with its own section each, and the four assignment requirements are declared as requirements, not concepts. The three-concept cap is respected and nothing in the concept list is untaught. The defects below are gate and proof-precision issues; the first three are the ones that matter.

**L1 — Phase 3's per-assembly greps are written backwards and fail on a correct tree.** `docs/lessons/10-track-segments-and-reservation.md:623-624`:
`grep -E 'TrafficCore\.Tests\.dll.*Passed: +6,'` and `grep -E 'ShuttleCore\.Tests\.dll.*Passed: +25,'`. `dotnet test -v m` prints `Passed:     6, … - TrafficCore.Tests.dll (net10.0)` — the assembly name is *last*. Both greps can never match, so the gate prints `FAIL` even though the counts are exactly right (verified: the suite gate reports `TrafficCore.Tests=6` and `ShuttleCore.Tests=25` are both present in the output). Reverse the order: `'Passed: +6,.*TrafficCore\.Tests\.dll'`. This is the same class of defect review 09's L2 warned about (a gate that does not test what it names); it survived into lesson 10.

**L2 — the literal-lane gate does not scan `ShuttleCore`, where Requirement 1 says the lane list must not appear.** Phase 2 (`:606`) checks `'"A[0-9]{2}-L[0-9]{2}'` in `src/TrafficCore` only, while Requirement 1 (`:545`) says `Track.cs` "must contain **no** hard-coded lane list" and record 16 §7's own verification table ran the grep against `src/ShuttleCore`. Mutation M6 (an 8-lane literal in `Track.cs`) passes all 79 tests *and* the Phase-2 grep. Add the same literal-lane grep over `src/ShuttleCore` (or specifically `Track.cs`) to Phase 2. Without it, the "derived from the rack" property has no gate at all.

**L3 — Phase 4's no-docker grep over-matches pre-existing `dotnet` process spawns.** Phase 4 (`:645-646`) scans `src/*.Tests` for `Process\.Start|docker(...)`. `FleetManager.Tests/StartTests.cs:17` and `ShuttleSim.Tests/DemoTests.cs:18` spawn `dotnet` (the bounded start tests from M1/M2), so the gate prints `FAIL: a test must not start docker` on a correct tree. The L1 widening from review 09 is right in spirit, but the alternation `Process\.Start` matches *any* process launch, not a docker launch. Make it docker-specific (for example `Process\.Start\s*\(\s*"[^"]*docker` plus the `docker`/`docker-compose` command forms, or simply `\bdocker\b` near `Process.Start`). The requirement "no test starts docker" is met; the gate is wrong.

**L4 — the "Proof details" mutation promises are false for two of the three named mutations.** `docs/lessons/10-track-segments-and-reservation.md:668` says "If a reviewer makes `TryReserveAll` write as it checks … `TryReserveRoute_refuses_and_holds_nothing…` must go red; if the controller is changed to grant each segment separately, the head-on test must go red". Measured: M1 stays **green** (the refusal test's conflict is on the first segment) and M7 stays **green** (the head-on test never retries). Only the third promise — an excluded endpoint in `Track.Route` — holds. Rewrite these two promises, or change the prescribed test scenarios so they actually exercise a late-segment conflict and a retry. As written, a reviewer who trusted the proof details would pass a suite that cannot detect hold-and-wait.

**L5 (minor) — the stress test has no acceptance gate that distinguishes a real loop from a one-shot `Assert.All`.** Phase 3b checks only that the test *name* exists (`:637`). The task says the test "must" loop, assert per round and bound rounds, but nothing in the acceptance criteria can observe those properties. Consider a companion check (for example require the test file to contain the round-loop shape, or a documented mutation expectation) so the central M4 claim cannot be satisfied by a name.

**Lesson 09 model answers (`54a850b`)** are correct and aligned to each of the nine questions; the two fresh answers (Q2, Q5) cover questions review 09 did not and match the code at the cited lines. No defect.

---

## Explain-it questions — required before this slice can close

These do not change the findings, but the slice is not closed until you can answer them out loud. Mixed "why" and "what would break"; the first three are the ones an interviewer would reach for.

1. **Why does `Track.Route` include both endpoints, and why does `Route("A01-L05","A01-L03")` return `L03,L04,L05` rather than starting at `L05`?** (`Track.cs:38-44`). Then: why does reserving only the destination lane fail to keep two shuttles apart?
2. **Walk the two passes in `TryReserveAll`** (`SegmentReservations.cs:11-25`). If you merged them into one "write as you check" loop, what state can be left behind when a route is refused? Why does `TryReserveRoute_refuses_and_holds_nothing…` (`TrafficControllerTests.cs:21-35`) *not* catch that bug, and what single change to the test would? (Mutation M1.)
3. **In `TryReserveRoute` (`TrafficController.cs:18-35`), why is `Reason` built by re-scanning `route` for `_reservations.OwnerOf(...)` instead of being returned by `TryReserveAll`? Could `Reason` ever be `"route unavailable"` (`:34`), and when?**
4. **The "stress test" (`TrafficControllerTests.cs:78-99`) uses routes L01→L02, L03→L04, L05→L06, L07→L08.** Why can it pass even if `SegmentReservations` grants every request and overwrites owners (mutation M2)? Rewrite it on paper with 4+ overlapping routes, a retry loop, a per-round no-double-owner assert, and a round bound — and justify the bound you pick (why is the professor's 5-round toy result a fair input, and why is the bound not circularly trivial?).
5. **`Track` is built from `rack.LaneId` (`Track.cs:11`).** Where does the ordering come from, is it guaranteed by `Rack`'s public contract, and how would you derive it explicitly from `DistanceInLanes` instead? Why do no tests catch a wrong order today?
6. **Why is `TrafficCore` a package-free project referencing only `ShuttleCore` (`TrafficCore.csproj:9-11`)?** Which tokens does Phase 2 scan for, and what would `FleetCore` have to learn about the world if the reservation table lived there (record 16 §2)? (Mutation M6 also lives here: why does a hard-coded lane list in `Track` pass every test *and* the Phase-2 grep, and which gate should have caught it?)
7. **The head-on test (`TrafficControllerTests.cs:65-76`) asks each shuttle once and never retries.** Why does it still pass if the controller reserves segment-by-segment and never rolls back (mutation M7)? What happens to that scenario on the *second* attempt, and what test shape would actually prove the rule avoids deadlock rather than merely refusing once?
8. **A refusal returns a `TrafficDecision` with a `Reason` string, and nobody logs it.** Where should that line be logged when the run loop is wired, and what fields would a support engineer need to reconstruct why a shuttle waited? (Findings 5; review 09's Configuration/logging dimension.)

### Lines or blocks that look unclear, copied, or unexplained

- `src/TrafficCore.Tests/TrafficControllerTests.cs:78-99` — named "stress" and "all_finish" but has disjoint routes, no loop, no rounds and no completion. Compare it to the toy in lesson 10 (`docs/lessons/10-track-segments-and-reservation.md:372-406`), which *does* keep holders per segment, loop, and return a collision count. Be ready to say which parts of the toy were supposed to become the real test.
- `src/TrafficCore.Tests/TrafficControllerTests.cs:65-76` vs `:51-63` — `Head_on…` and `Two_shuttles…` are near-identical; explain what `Head_on…` adds.
- `src/TrafficCore/TrafficController.cs:29-35` — the `blockedSegment is null` / `blockedOwner = "unknown"` branches. When can each occur given the two-pass table?
- `src/ShuttleCore/Track.cs:11` — `rack.LaneId.ToList()`. Explain why this is aisle-major order and why you relied on it instead of ordering explicitly.
- `src/TrafficCore/SegmentReservations.cs:9` — `Distinct` before the passes. A contiguous route never repeats a segment; say why you kept it.
- `src/TrafficCore/SegmentReservations.cs:3` and `TrafficController.cs:5` — `public class` where the lesson's shapes show `public sealed class`. Say why not `sealed`.

---

## Revisit list

- **Designing an adversarial test.** A test that cannot fail is worse than no test: it buys false confidence. A stress test needs genuine contention (overlapping routes), an invariant checked every round, and a bound that a failure would breach.
- **Atomicity and the late-segment conflict.** Test the property where it actually bites: a conflict on a segment that is *not* first. This is the direct application of M1.
- **Avoidance vs detection, tested.** A one-shot refusal proves neither; a retry loop is what separates "holds nothing while waiting" from "holds and waits".
- **Explicit ordering over collection enumeration order.** Never let correctness rest on `Dictionary.Keys` iteration order; derive order from the model (`DistanceInLanes`) and test it across aisles and over longer routes.
- **Gate precision (again).** Three of this lesson's gates do not test what they name (L1, L2, L3). When you write an acceptance command, run it against both a correct tree and a deliberately broken one, and check it fails on the latter.
- **Next:** the run-loop gate at M4's second half — call `TrafficController.TryReserveRoute` before `TaskDispatcher.AssignNextAsync`, decide the retry policy when a route is refused, own the table from one caller, and log the decision. This suite must be strengthened *before* it is used as the gate's safety net.

---

## End-product check

`TrafficCore` is the right seam and the product code inside it is correct: a segment is a lane, the track is derived from the rack, one table owns who holds what, and the rule is an atomic whole-route grant that removes hold-and-wait. That is exactly what the run loop will call next lesson, and it keeps traffic out of `FleetCore` and off the wire, so two shuttles can never be recorded on the same segment. The risk this slice carries forward is **false confidence in the proof, not a defect in the decision logic**: the one test that is supposed to stand for M4's "no collisions, no permanent deadlock" does not exercise contention, and the two tests named for the atomic and deadlock-avoidance properties cannot fail on the mutations that break them. If the run-loop lesson wires this gate behind a suite like this, a regression to element-by-element reservation — the exact design record 16 §5 rejects — would ship green. Strengthen the stress test and the refusal/head-on scenarios before the run-loop gate is built on them, and the M4 done-when will then have a proof worth its name.

---

## README

**Not updated.** The verdict is not a pass, and M4's done-when is not met on this slice (the stress scenario does not run end to end, and the decision-level stress test is vacuous), so no milestone-table change is justified. The M4 row remains as it is. No other row or section of `README.md` was touched. (Separately: the M4 row currently reads `not started`, which is now stale — the owner may want to reflect that work is underway — but the reviewer may only change the table after a passing review, so it is left alone and flagged here.)

---

## What the owner must do next

1. **Replace the stress test** (`TrafficControllerTests.cs:78-99`) with one that meets Requirement 3: 4+ overlapping routes on one rack, a loop that grants a pending request only when its whole route is free, completes and releases a granted move, asserts after every round that no segment has two owners, and asserts all shuttles finish within a stated bound. Use the lesson toy's scenario; say why the bound is honest.
2. **Strengthen the refusal test** so the conflicting segment is not the first one (reserve one later lane for the first shuttle, then request a route that spans a free segment and the taken one). It must fail under mutation M1.
3. **Strengthen or remove the head-on test** so it retries refused requests in a loop and proves all shuttles eventually move (or fold it into the stress test).
4. **Order `Track`'s segments explicitly** and add a cross-aisle / longer-route test.
5. Answer the explain-it questions out loud; questions 2, 3 and 4 are the ones that would have caught this review's blockers.
6. Professor: fix L1 (per-assembly grep order), L2 (add the literal-lane grep over `ShuttleCore`/`Track.cs`), L3 (make the no-docker grep docker-specific), L4 (correct the two false mutation promises), L5 (gate the stress test's shape, not just its name).

---

## Reproduce this review

From the repo root, against `8cc815a`:

```bash
# toolchain
dotnet --version                       # expect 10.0.112
docker --version                       # expect Docker 29.x

# DB-less path (what the build container runs)
cd src
dotnet build                                                     # expect exit 0, 0 warnings, 0 errors
dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
                                                                 # expect RC 0, 8 Passed!, 79 tests
dotnet test ShuttleFleet.slnx --list-tests | grep -E 'Four_or_more_shuttles|Head_on_requests|Release_frees'
                                                                 # expect all three names present

# with-DB path (the two M3 integration tests still execute)
cd ..
docker compose -f docker/compose.yml up -d
cd src
dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m \
  --logger "console;verbosity=detailed" --filter "Category=Integration"
                                                                 # expect RC 0, "Passed: 2", zero skipped
cd ..
docker compose -f docker/compose.yml down

# fresh container (no database, integration excluded)
docker build -f docker/Dockerfile.build -t shuttle-fleet-build .
docker run --rm shuttle-fleet-build                              # expect RC 0, 8 Passed!
```

The mutations are not a one-liner: use a throwaway `git archive HEAD` copy under `/tmp/opencode`, apply each row of the table by hand, run the named project's suite, and restore before the next. The two decisive ones to replay are **M2** (delete the different-owner check in `SegmentReservations.TryReserveAll`; the owner's stress test stays green) and **M7** (make `TrafficController.TryReserveRoute` reserve per segment; all six traffic tests stay green). Do **not** mutate the working repo.

---

## Cleanup

- `docker compose -f docker/compose.yml down` run after the integration phase; `docker compose ps` shows no `shuttle-fleet` container, and the network is removed.
- Throwaway `/tmp/opencode/rev10` and `/tmp/opencode/rev10-pristine` deleted; experiment output files under `/tmp/opencode/` removed. `/tmp/opencode/mqttinspect` left as found.
- `dotnet build-server shutdown` run.
- No file under `src/` was modified by this review. `docs/reviews/10-track-segments-and-reservation.md` is the only file changed; `README.md` was not touched.

## Grade of the lesson's own verification table, claim by claim

Lesson 10's table (`docs/lessons/10-track-segments-and-reservation.md:62-76`) and record 16's table (`docs/decisions/16-…md:128-142`), checked against this review:

| Claim | Verdict now | Where |
|---|---|---|
| SDK `10.0.112` | correct | Environment |
| baseline pure suite 70 tests / 7 `Passed!` | correct | matches review 09; this review's baseline is 79/8 after the slice |
| rack builds lane ids; `IsLane` membership; `DistanceInLanes` ordinal | correct | `Rack.cs:27,45,50-51` |
| no lane id hard-coded in the warehouse model | true of the committed tree, but **the gate that should enforce it only scans `TrafficCore`** | phase 2; mutation M6; lesson defect L2 |
| `dotnet new xunit` versions match | correct | `TrafficCore.Tests.csproj` == `FleetManager.Tests.csproj` |
| package-free library can reference `ShuttleCore` | correct | `TrafficCore.csproj:9-11` |
| solution 14 → 16 | correct | phase 5 |
| new test project adds an 8th `Passed!` line | correct | phase 3 / container |
| all-or-nothing drains a 4-shuttle toy with zero collisions | true of the *toy* | not reproduced in `src/`; the real stress test does not do this |
| same head-on case deadlocks under hold-and-wait | true of the *toy* | not reproduced in `src/`; the real head-on test does not distinguish |
| "Not yet verified: Track, SegmentReservations, TrafficController, TrafficDecision, the tests, the fresh container" | **resolved by this review** | Phases 1–6, container, mutations — with the caveats above |

---

## Re-review 10b — the stress-test rework

**Date:** 2026-10-12
**Reviewed:** the uncommitted working tree on top of `8cc815a`. Only two files changed versus HEAD: `src/TrafficCore.Tests/TrafficControllerTests.cs` (+66 −19) and `src/ShuttleCore.Tests/TrackTests.cs` (+11). Verified with `git diff --stat -- src/TrafficCore src/ShuttleCore/Track.cs` → empty: **no product code changed**, which the rework path required. Mutations ran against a working-tree copy at `/tmp/opencode/rev10b` (rsync of the working tree, `bin`/`obj` excluded), restored byte-identical to the working tree afterwards; the two changed test files were sha256-verified identical to the tree before mutation work began.

**Verdict: not yet.**

**In one paragraph.** The owner did real, honest work: the stress test is now a loop over four overlapping routes with grant→release and a bounded round count, and it genuinely catches the Release-one-segment bug (M3) that the review-10 version could not. The cross-aisle track test is added and it catches both the endpoint-drop mutation and a hard-coded lane list. But neither blocker from review 10 is resolved. (1) The stress test still releases every grant in the same loop iteration in which it granted, so the table is empty before every request and two shuttles never hold overlapping routes at once; mutation M2 (grant every request, overwrite owners) still passes it, and a minimally-correct hold-across-rounds probe fails under M2. (2) The strengthened refusal test still places the conflict on the *first* segment of the second route, so mutation M1 (write-as-you-check) still passes it; a corrected scenario (first shuttle holds a later segment only) turns M1 red. And the head-on test still never retries, so M7 (per-segment hold-and-wait) still passes all six tests. The product code remains correct; the proof that M4's done-when relies on is still missing.

---

### The diff they made

`src/TrafficCore.Tests/TrafficControllerTests.cs` (+66 −19):

- `TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken` (`:21-35`): rack 2→3 lanes; Shuttle1 now reserves `A01-L01 → A01-L03` (holds L01, L02, L03) and Shuttle2 requests `A01-L02 → A01-L03`; reason assert strengthened to `Assert.Contains("A01-L02 held by Shuttle1", …)`.
- `Head_on_requests_resolve_without_deadlock` (`:65-77`): now genuinely head-on — Shuttle1 `A01-L01 → A01-L03`, Shuttle2 `A01-L03 → A01-L01`; adds a `Reason` assert.
- `Four_or_more_shuttles_never_share_a_segment_and_all_finish` (`:79-127`): replaced the one-shot disjoint version with a loop — overlapping routes L01→L04, L02→L05, L03→L06, L04→L07 on an 8-lane rack; `pending` set; `while (pending.Count > 0 && round < 12)`; per shuttle: try reserve, on grant assert `OwnerOf(segment) == shuttleId` for every segment, then `Release` and remove; after the loop `Assert.True(round <= 12)` and `Assert.Empty(pending)`.

`src/ShuttleCore.Tests/TrackTests.cs` (+11): adds `Route_orders_segments_across_aisles_in_aisle_major_order` — `Rack(2, 2, 2)`, `Route("A01-L02", "A02-L01")` equals `[A01-L02, A02-L01]` (a fourth `[Fact]`; see Finding 4 and lesson defect **L6**).

### Commands run, verbatim key output

Environment: `dotnet --version` → `10.0.112`; `docker --version` → `29.8.1`; `docker compose ps` → nothing up at start and end.

Phase 1 (track/traffic core exist, pinned names): **pass**, all greps matched, `Track(Rack)` and the `TryReserveAll` call present.

Phase 2 (purity, no literals): **pass** — clock/id/db scans rc 1 over `TrafficCore`; literal-lane scan rc 1 (over `TrafficCore` only — see L2); `PackageReference` 0, `ProjectReference` 1.

Phase 3 (build + pure suite):

```
Build succeeded. 0 Warning(s) 0 Error(s)
---RC:0---
Passed! ... Contracts.Tests.dll          4
Passed! ... ShuttleCore.Tests.dll       26
Passed! ... Messaging.Tests.dll          8
Passed! ... TrafficCore.Tests.dll        6
Passed! ... FleetCore.Tests.dll         18
Passed! ... MqttMessaging.Tests.dll      2
Passed! ... ShuttleSim.Tests.dll         5
Passed! ... FleetManager.Tests.dll      11
assemblies-passed=8 assemblies-failed=0 total=80
FAIL: expected RC 0, 8 Passed!, 0 Failed!, 79 tests
FAIL: TrafficCore.Tests should show 6
FAIL: ShuttleCore.Tests should show 25
```

RC 0, 8 `Passed!`, 0 `Failed!`, TrafficCore.Tests 6 — all correct **in substance**. Two things break the gate: (a) the lesson's per-assembly greps are still written backwards (L1 — `Passed: +6,` comes before the assembly name in the output, so `TrafficCore…dll.*Passed: +6,` can never match); (b) ShuttleCore.Tests is now **26**, not 25, and the total is 80, not 79 — the new cross-aisle test the review asked for. The count gate is stale for the very fix it requested. See **L6**.

Phase 3b (nine anchored names): **pass** — all nine present with `\.$t$` anchors, plus the new `Route_orders_segments_across_aisles_in_aisle_major_order` listed as a 10th traffic/track name (not in the pinned list, passes the phase).

Phase 4: traffic tests pure (no `Trait`, no Npgsql/Postgres, both rc 1). The no-docker grep still false-positives on the pre-existing `dotnet` process spawns in `StartTests.cs:17` and `DemoTests.cs:18` (L3 — the requirement is met, the gate over-matches).

Phase 5: **pass** — solution 16 projects, `TrafficCore`, `TrafficCore.Tests`, `ShuttleCore` reference all present.

Phase 6: **pass** — `git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs docs/contract docker/Dockerfile.build` → OK; contract v1/v2, no v3; dispatcher's four methods present.

Regression with DB up:

```
$ dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration"
  Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks [409 ms]
  Passed FleetManager.Tests.EndToEndTests.Three_shuttles_finish_queue_and_state_survives_restart [115 ms]
```

then `docker compose -f docker/compose.yml down` (broker + database removed, network removed).

Fresh container:

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build-rev10b .
$ docker run --rm shuttle-fleet-build-rev10b          RC 0, 8 Passed! lines
  Passed! … ShuttleCore.Tests.dll (net10.0)  26
  Passed! … TrafficCore.Tests.dll (net10.0)   6
```

### Blocker resolution, measured

**Blocker 1 (the stress test is vacuous): NOT RESOLVED.** The loop is real, but every grant is released in the same iteration, so the table is empty before each request and no request can ever be refused — the loop drains in one round (the round bound is never approached). Under M2 (drop the different-owner check) the test still passes. A proper probe that *holds* each granted route across rounds and asserts no two holders' routes overlap fails under M2 at the first round. Full evidence in the mutation table (M2, M9-1, M9-2, M9-bound, probe).

**Blocker 2 (refusal test does not prove "holds nothing"): NOT RESOLVED.** Shuttle2's route is `A01-L02 → A01-L03`; the conflicting segment `A01-L02` is its *first* segment, so a write-as-you-check table returns before writing anything and `Assert.Empty(SegmentsOwnedBy("Shuttle2"))` passes. The scenario the review prescribed (first shuttle holds a *later* segment only, e.g. `L02 → L02`; second requests `L01 → L02`) is what turns M1 red, verified with a probe. See M1 in the table.

**Finding 3 (head-on never retries): NOT ADDRESSED.** The scenario is now genuinely head-on, which is an improvement, but it still asks each shuttle once. Under M7 (per-segment reservation, no rollback) all six tests stay green — and note that in this specific scenario Shuttle2's entire route is already held by Shuttle1, so even hold-and-wait reserves nothing. The test proves a refusal happens, not that deadlock is avoided.

**Finding 4 (explicit ordering + cross-aisle test): PARTIALLY ADDRESSED.** The cross-aisle test now exists and is real value (it catches M4 and M6). But `Track.cs:11` still derives order from `rack.LaneId.ToList()` (dictionary enumeration order), and the new test cannot distinguish insertion order from ordinal string order for this id grammar (`"A01-L02"` sorts before `"A02-L01"` either way). The explicit-derivation half of the finding remains open — it is why the failing-phase lesson gate (L6) exists, and the professor decides whether to fix the lesson or accept the owner's interpretation (pristine `Rack` order is right today, verified by the cross-aisle probe in review 10).

### Mutation suite (working-tree copy `/tmp/opencode/rev10b`)

Baseline in the copy: TrafficCore.Tests 6 pass, ShuttleCore.Tests 26 pass, build RC 0. Each mutation applied to the copy, relevant suite run, file restored from pristine backup (verified by `diff`), next applied.

| # | Mutation | File / line | Red test(s) | Observed |
|---|---|---|---|---|
| **M1** | Write-as-you-check in `TryReserveAll` (partial hold on refusal) | `SegmentReservations.cs:11-23` | lesson expects `TryReserveRoute_refuses_and_holds_nothing…` to go red | **GREEN — all 6 pass.** The conflict sits on the first segment of Shuttle2's route, so the mutant returns before writing anything. A corrected-scenario probe (Shuttle1 holds only `L02`; Shuttle2 requests `L01 → L02`) goes **RED** under M1 and green on the correct table — the fix is one scenario change |
| **M2** | Drop the "different owner" check (grant every request, overwrite owners) | `SegmentReservations.cs:11-18` | `Head_on…`, `Two_shuttles…`, `refuses…` red; stress test? | **RED for the three; `Four_or_more…` stays GREEN (the blocker).** A proper hold-across-rounds probe with the same routes fails under M2 with "segment held by two shuttles" (probe red, owner's stress green) |
| **M3 (control)** | `Release` frees only the first held segment | `SegmentReservations.cs:30-35` | must stay red | **RED — `Release_frees_every_segment…` and `Four_or_more…`** (leftover segments block other shuttles, pending never drains). Genuine progress: review 10's stress test could not have caught this |
| **M4 (control)** | `Track.Route` excludes the end | `Track.cs:44` | must stay red | **RED — all three route tests, including the new cross-aisle test** |
| **M5 (control)** | `Track.Route` stops rejecting unknown lanes | `Track.cs:28-36` | must stay red | **RED — `Route_to_an_unknown_lane_is_rejected`** |
| **M6** | `Track` built from a hard-coded 8-lane list instead of the rack | `Track.cs:11` | (was: none) | ShuttleCore.Tests **RED 1** — the new cross-aisle test catches it, because the literal list has no `A02` lanes. TrafficCore.Tests still green, and the Phase-2 literal grep over `TrafficCore` still reports clean (rc 1): **L2 stands — the gate that should guard "derived from the rack" still does not scan `ShuttleCore`** |
| **M7** | `TrafficController` reserves per segment (one `TryReserveAll` per segment, no rollback) | `TrafficController.cs:18-19` | lesson expects `Head_on…` red | **GREEN — all 6 pass.** The head-on scenario's second route is fully covered by the first holder, and the test never retries, so per-segment hold-and-wait reserves nothing and looks identical to all-or-nothing |
| **M9-1** | Remove the per-round no-double-owner assert from the stress test (test mutation), then M2 | `TrafficControllerTests.cs:115-118` | (new test must be load-bearing) | No change: stress still **GREEN** under M2. The assert is not load-bearing for the collision property; it only re-checks the granted shuttle's own segments, and a single-owner table cannot even exhibit a double owner (collisions are silent overwrites) |
| **M9-2** | Revert the stress routes to review 10's disjoint set, keep loop/release/bound, then M3 | `TrafficControllerTests.cs:87-90` | (overlap must be load-bearing) | Stress test loses the M3 catch: with disjoint routes, even the broken `Release` drains everything in one round and the test passes. **The overlap is load-bearing** (for the Release/round property) |
| **M9-bound** | Remove the `&& round < 12` bound (test mutation), keep overlap, then M3 | `TrafficControllerTests.cs:96` | (bound must be load-bearing) | Non-terminating: `timeout 25 dotnet test` → exit 124 (hung). The bound is load-bearing as a hang→failure conversion, but a *correct* table and M2 both drain in round 1, so the bound never discriminates a collision bug |
| **Probe** | Proper stress test (grant held across rounds, per-round no-two-holders check) on the *correct* table vs M2 | test-side addition | — | Green on the correct table; **RED under M2** (segment held by two shuttles, line 43). This is the shape Requirement 3 describes and the owner's test is not it |

**Read it back.** Three of the previously-green blind spots are now genuinely covered: M3, M6 (partially, at the test level), M4's cross-aisle case. But the three that review 10 called out as the proof's core — M1, M2 (via the stress test), M7 — are **all still green**. The lesson's stated mutation promises (L4) remain false for M1 and M7 after the rework. The new test's loop, bound and per-round assert operate on a table that is empty before every request, so the collision property is still untested.

### Findings (top issues, ranked)

**1. BLOCKER — the stress test still never creates contention: overlapping routes are granted one at a time with the table empty in between.** `TrafficControllerTests.cs:100-122`: grant → per-segment owner assert → `Release` → remove, all inside one `foreach` iteration, so at no point do two shuttles hold anything. The loop drains in round 1; the bound (12) and the `round <= 12` assert never discriminate anything; and the per-round assert re-reads the granted shuttle's own segments, which a single-owner `Dictionary` can never report as a collision (a wrong owner is a silent overwrite, not an extra row). M2 stays green — the same mutation that certified a collision in review 10 still passes. **Why it matters:** M4's done-when is "a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock"; this test is the only decision-level proof of that claim, and it would still certify a table that lets two shuttles "hold" the same lane. Fix: hold each granted route until the *next* round (complete-then-release at the top of the loop, as the lesson toy does at `docs/lessons/10-…md:383-389`), keep the granted routes in the test, and assert after every round that no two holders' routes intersect — the verified probe in the mutation table is the shape.

**2. BLOCKER — the refusal test still conflicts on the first segment, so "holds nothing" is still unproven.** `TrafficControllerTests.cs:24-34`: Shuttle1 holds L01–L03; Shuttle2 requests L02→L03, whose first segment is the taken one. A write-as-you-check table returns before writing, so `Assert.Empty(SegmentsOwnedBy("Shuttle2"))` (the right assertion) passes on the bug (M1 green). **Why it matters:** atomicity is the mechanism that makes a waiting shuttle hold nothing — the property that removes hold-and-wait and therefore deadlock (record 16 §5). Fix (verified): first shuttle reserves only a later segment (e.g. `L02 → L02`), second requests `L01 → L02`; then M1 leaves L01 behind and the assertion goes red.

**3. SHOULD-FIX — the head-on test still cannot detect hold-and-wait.** `TrafficControllerTests.cs:65-77` now uses a true head-on pair but still issues one request per shuttle with no retry, and the second shuttle's route is entirely inside the first holder's route, so even a per-segment "hold what you can" controller holds nothing. M7 stays green. Fold a retry loop into this test or into the stress test; a refused-then-retried shuttle is the only observation that separates avoidance from a standstill.

**4. SHOULD-FIX (lesson-gate, with the owner) — the added cross-aisle test breaks the lesson's Phase 3 counts.** The test is good and was explicitly requested by review 10's Finding 4, but the lesson pins `ShuttleCore.Tests` at 25 and the total at 79, and it is now 26 and 80, so the gate prints FAIL on a correct tree even after L1 is fixed. The lesson should either count 26/80 (and say "at least these three") or require the cross-aisle assertion to live inside one of the three pinned tests. This is a lesson defect (**L6**), not a student failure — the owner followed the review.

**5. CONSIDER — the new cross-aisle test passes whether the order comes from rack insertion order or from ordinal string order; the derivation is still implicit.** With this id grammar both orderings agree, so `Route_orders_segments_across_aisles_in_aisle_major_order` cannot regress if `Rack.LaneId` enumeration order changed. Deriving explicitly via `DistanceInLanes` (review 10 Finding 4) is still the durable fix; it was out of scope for this rework path, so the professor should decide whether the run-loop lesson carries it.

### What is solid

- **The stress test now catches a real Release bug.** M3 (free only one segment) turns the test red via the round structure — the review-10 version could not have. The loop/complete/release skeleton the lesson asked for is real code now; only the contention is missing.
- **The new cross-aisle test is a genuine improvement.** It is the first test that forces `Track` to answer outside one aisle, and it is the first test to catch a hard-coded lane list (M6).
- **The refusal scenario and reason asserts are sharper.** `Assert.Contains("A01-L02 held by Shuttle1", …)` in two tests pins the "names the blocking segment and its owner" behavior more tightly than review 10's version.
- **Discipline held.** No product code changed, no protected file changed, no `DateTime.UtcNow`/`Guid`/literals anywhere near `TrafficCore`, contract untouched, integration tests still green, container still exits 0, and the changes are confined to the two test files the review asked for.

### Lesson defects (for the professor)

The rework validates review 10's L1–L5 and adds one:

- **L1 still stands** — the per-assembly greps in Phase 3 are written backwards and fail on a correct tree.
- **L2 still stands** — the literal-lane gate scans `TrafficCore` only; M6 passes it (even though the new cross-aisle test now incidentally catches the specific mutation, the *gate* is still wrong).
- **L3 still stands** — Phase 4's no-docker grep over-matches the pre-existing `dotnet` spawns.
- **L4 still stands, and is now measured twice** — the "Proof details" mutation promises for M1 and M7 are still false after the owner's rework. The lesson's own proof standard cannot be met by the scenarios it prescribes. This is the lesson defect review 10 called the most dangerous, and it is unchanged.
- **L5 still stands** — the acceptance criteria check the stress test's *name*, not whether it creates contention; the owner's loop-with-immediate-release satisfies the letter of "loop … bounded" and still proves nothing about collisions. Add a shape gate (e.g. require the release to happen in a later iteration than the grant, or require the per-round check to compare *holders' routes*, or document that M2 must turn the stress test red) — L5 is exactly why this rework shipped a loop that is still vacuous.
- **L6 (new)** — Phase 3's counts (79 total, ShuttleCore.Tests 25) are now wrong for the tree the review demanded; the gate must move to 80/26 or the cross-aisle assertion must fold into the pinned three. Also, Requirement 1's "these three `[Fact]`s" vs. Requirement 3's "exactly the six tests below" is inconsistent — R1 silently allows more, R3 does not; if R1 means exactly three, say so and keep the cross-aisle assertion in an existing test.

### Explain-it questions — still owed, updated

The owner answered none of review 10's eight questions; they remain required before this slice closes. Two are reworded for the new code; the rest stand as written.

1. **Why does `Track.Route` include both endpoints, and why does `Route("A01-L05","A01-L03")` return `L03,L04,L05`?** Then: why does reserving only the destination fail to keep two shuttles apart? (`Track.cs:38-44`)
2. **Walk the two passes in `TryReserveAll`.** If you merged them into one write-as-you-check loop, what state is left when a route is refused? Why does the current `refuses_and_holds_nothing` test (`TrafficControllerTests.cs:21-35`, conflict on the *first* segment of Shuttle2's route) still pass on that bug, and what one-line scenario change fixes it? (M1 is still green.)
3. **Why is `Reason` built by re-scanning the route with `OwnerOf` instead of returned by `TryReserveAll`?** Could `Reason` ever be `"route unavailable"` (`TrafficController.cs:34`), and when?
4. **Walk the new stress loop (`TrafficControllerTests.cs:96-123`) round by round on the correct table.** Why does it drain in one round? Why does it still pass when `SegmentReservations` grants every request and overwrites owners (mutation M2)? Rewrite it on paper with a hold-across-rounds loop (grant in round *n*, complete+release at the top of round *n+1*) and a per-round no-two-holders check over the *test's own* record of granted routes — and justify why the test cannot rely on the reservation table to detect a collision.
5. **`Track` is built from `rack.LaneId` (`Track.cs:11`).** Where does the order come from, why does the new cross-aisle test not pin its *source*, and how would you derive it from `DistanceInLanes`?
6. **Why is `TrafficCore` a package-free project referencing only `ShuttleCore`?** Which tokens does Phase 2 scan, and why does a hard-coded lane list in `Track.cs` still pass every gate even though a test now catches a specific example?
7. **The head-on test asks each shuttle once, and the second route lies entirely inside the first holder's route.** Why does it still pass under per-segment hold-and-wait (M7)? What happens to that scenario on the *second* attempt, and what test shape would actually prove avoidance?
8. **A refusal's `Reason` string is dropped by every caller.** Where should it be logged when the run loop is wired, and what fields would a support engineer need to reconstruct why a shuttle waited?

### Verdict and README

**Not yet — within this lesson's scope.** Both review-10 blockers are confirmed unresolved by measurement (M1, M2-via-stress, M7 all still green in the working tree), so the proof the M4 done-when needs is still missing. The rework is a real step — the loop is real, the overlap is real, M3 and M6 are now caught — but the lesson's central claim ("no collisions, no permanent deadlock") still has no test that can fail.

**README: not updated**, for two independent reasons: the verdict is not a pass, and M4's done-when requires "a stress scenario with four or more shuttles **runs**" — the run-loop gate that calls `TrafficController.TryReserveRoute` before `TaskDispatcher.AssignNextAsync` is the next lesson and does not exist yet (Phase 6 and the lesson both forbid touching the dispatcher). The M4 row stays as-is. Nothing else in `README.md` was touched.

### What the owner must do next

1. **Give the stress test real contention**: hold each granted route across a round (release at the top of the next round, as the lesson toy does), track grants in the test, assert per round that no two holders' routes intersect, and only then is the bound honest. The probe in this re-review is the shape; make it yours and explain it out loud.
2. **Move the refusal test's conflict off the first segment**: first shuttle reserves only a later segment; second requests a route that starts free and ends on the taken segment. M1 must go red.
3. **Make the head-on test retry** (or fold it into the stress test) so M7 goes red.
4. Commit these changes (small commit, e.g. `M4: strengthen traffic tests per review 10`), then answer the eight explain-it questions — questions 2, 4 and 7 are the ones that would have caught today's findings.
5. Professor: fix L1–L6, and specifically give L5 a gate that observes contention (or a documented M2-must-go-red expectation) so a loop that never contends cannot pass again.

### Cleanup

- `docker compose -f docker/compose.yml down` run after the integration phase; `docker compose ps` empty.
- `/tmp/opencode/rev10b` and `/tmp/opencode/rev10b-pristine` deleted after verification that the copy was restored byte-identical to the tree (all five product/test files `diff`-clean); container image `shuttle-fleet-build-rev10b` removed; experiment logs in `/tmp/opencode/` removed.
- `dotnet build-server shutdown` run (MSBuild node-reuse processes from the mutation builds shut down).
- `git status` shows exactly: `src/ShuttleCore.Tests/TrackTests.cs` and `src/TrafficCore.Tests/TrafficControllerTests.cs` modified (the owner's work, untouched by this review) and `docs/reviews/10-track-segments-and-reservation.md` untracked/added (this review). `README.md` not touched, no product file touched by the reviewer.

---

## Re-review 10c — the second rework

**Date:** 2026-10-12
**Reviewed:** the uncommitted working tree on top of `8cc815a`. `git status --short` shows exactly two modified files — `src/ShuttleCore.Tests/TrackTests.cs` and `src/TrafficCore.Tests/TrafficControllerTests.cs` — plus this untracked review; `git diff --stat -- src/TrafficCore src/ShuttleCore/Track.cs` is empty, so **no product code changed**. All acceptance and mutation runs are against the files on disk. Mutations ran against a working-tree copy at `/tmp/opencode/rev10c` (rsync, `.git` and `docs/reviews` and `bin`/`obj` excluded), deleted after a `diff` confirmed the source files were restored byte-identical to pristine.

**Verdict: not yet.**

**In one paragraph.** The decisive blocker is fixed. The stress test now creates real contention: each grant is held for the round while the other shuttles try, and the per-round "no segment twice" assert over the test's own record of held routes turns **M2** (drop the different-owner check) **red** for the first time — and the test also stays load-bearing for the `Release` bug (M3). The cross-aisle assertion was folded into the pinned `Route_covers_every_segment_between_two_lanes_in_order`, so `TrackTests` is back to exactly three `[Fact]`s and the lesson's count pin is satisfied again (`ShuttleCore.Tests` 25 / total 79). The head-on test now genuinely retries after `Release`, which makes it catch M3 too. But the other two prior blockers are still unresolved: the refusal test still puts the conflict on the **first** segment of the second route, so a write-as-you-check table still passes it (**M1 GREEN**; a corrected free-first/taken-second probe goes RED), and the head-on test's second route still **starts** on the segment the first shuttle holds, so a per-segment hold-and-wait controller still passes every traffic test (**M7 GREEN**). The product code was already correct; the proof still misses the two mutations that the lesson explicitly promises will be caught.

---

### The diff they made (`git diff`, working tree vs `8cc815a`)

`src/ShuttleCore.Tests/TrackTests.cs` (+2): the cross-aisle assertion was **folded into the existing** `Route_covers_every_segment_between_two_lanes_in_order` rather than added as a fourth `[Fact]`:

```diff
         var route = track.Route("A01-L01", "A01-L02");
+        var crossAisleRoute = track.Route("A01-L02", "A02-L01");
 
         Assert.Equal(new[] { "A01-L01", "A01-L02" }, route);
+        Assert.Equal(new[] { "A01-L02", "A02-L01" }, crossAisleRoute);
```

`src/TrafficCore.Tests/TrafficControllerTests.cs` (+83 −18):

- Refusal test (`:24,34`): rack 2→3 lanes; reason tightened to `Assert.Contains("A01-L01 held by Shuttle1", …)`. **The two `TryReserveRoute` calls are unchanged from HEAD** — Shuttle1 `L01→L02`, Shuttle2 `L01→L02` — so the conflict is still on Shuttle2's *first* segment (this is the exact HEAD scenario; the 10b working tree's `L01→L03` / `L02→L03` variant is not in this tree).
- Head-on (`:72-82`): now keeps the `SegmentReservations`, asserts the refusal reason, then `Release("Shuttle1")` and **retries** Shuttle2's `L02→L03`, asserting it is granted.
- Stress test (`:91-145`): overlapping routes `L01→L04`, `L02→L05`, `L03→L06`, `L04→L07` on an 8-lane rack; `inFlight` holds each granted route for the round; `while (done.Count < shuttleRoutes.Count && round < 12)`; a per-round `occupied` HashSet asserts no segment appears twice across `inFlight.Values`; granted shuttles are then released and marked done.

The full `git diff` is the two hunks quoted above plus the reason/rack lines; there are no other changes.

### Commands run, verbatim key output

Environment: `dotnet --version` → `10.0.112`; `docker --version` → `29.8.1`; `docker compose ps` empty at start and end.

Phase 1 (track/traffic core exist, pinned names): **pass** — all greps matched; `Track(Rack)` and the atomic `TryReserveAll` call present.

Phase 2 (purity, no literals): **pass** — clock/id scan, db/msg scan, literal-lane scan over `TrafficCore`, and the `ShuttleCore` db/msg scan are all `rc 1`; `PackageReference` 0, `ProjectReference` 1. (The literal-lane scan still covers `TrafficCore` only — L2.)

Phase 3 (build + the lesson's exact gate command):

```
Build succeeded. 0 Warning(s) 0 Error(s)
---RC:0---
Passed!  - Failed: 0, Passed: 25, … ShuttleCore.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  4, … Contracts.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  6, … TrafficCore.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  8, … Messaging.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 18, … FleetCore.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 11, … FleetManager.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll (net10.0)
assemblies-passed=8 assemblies-failed=0 total=79
suite gate OK
FAIL: TrafficCore.Tests should show 6
FAIL: ShuttleCore.Tests should show 25
```

**Counts observed:** RC 0, 8 `Passed!`, **0** `Failed!`, **79** tests, `TrafficCore.Tests` = **6**, `ShuttleCore.Tests` = **25**. The suite gate prints **`suite gate OK`**. The two per-assembly lines still print `FAIL` on a correct tree because their regexes are written backwards (L1) — the numbers are right.

Phase 3b (nine anchored names): **pass** — all nine `\.<name>$` matches present; `ShuttleCore.Tests --list-tests | grep -c TrackTests` = **3** (the fold-in worked).

Phase 4: traffic tests pure (no `Trait`, no Npgsql/Postgres, both `rc 1`). The no-docker grep still false-positives on the pre-existing `dotnet` spawns in `StartTests.cs:17` and `DemoTests.cs:18` (L3).

Phase 5: **pass** — 16 projects, `TrafficCore`/`TrafficCore.Tests`/`ShuttleCore` reference present.

Phase 6: **pass** — protected files unchanged; contract v1/v2, no v3; the four dispatcher methods present.

Regression with DB up:

```
$ dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration"
  Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks [281 ms]
  Passed FleetManager.Tests.EndToEndTests.Three_shuttles_finish_queue_and_state_survives_restart [90 ms]
```

then `docker compose -f docker/compose.yml down` (network and both containers removed). Fresh container:

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build-rev10c .
$ docker run --rm shuttle-fleet-build-rev10c        RC 0, 8 Passed! lines
  Passed! … ShuttleCore.Tests.dll (net10.0)  25
  Passed! … TrafficCore.Tests.dll (net10.0)   6
```

### Mutation suite (working-tree copy `/tmp/opencode/rev10c`)

Baseline in the copy: `TrafficCore.Tests` 6/6, `ShuttleCore.Tests` 25/25, build RC 0. Each mutation applied to the copy, the relevant suite run, file restored from the pristine backup (`diff`-verified), next applied. The copy's source was verified `diff`-clean against pristine before deletion.

| # | Mutation | File / line | Red test(s) | Observed |
|---|---|---|---|---|
| **M1** | Write-as-you-check in `TryReserveAll` (partial hold on refusal) | `SegmentReservations.cs:11-25` | lesson says `TryReserveRoute_refuses_and_holds_nothing…` red | **GREEN — all 6 pass.** Conflict sits on Shuttle2's first segment (`L01`), so the mutant returns before writing. Corrected probe (Shuttle1 holds only `L02`; Shuttle2 requests `L01→L02`) goes **RED** under M1 (`Expected: empty, Actual: [A01-L01]`) and green on the correct table |
| **M2** | Drop the "different owner" check (grant every request, overwrite owners) | `SegmentReservations.cs:11-18` | `Head_on…`, `Two_shuttles…`, `refuses…`, and now the stress test | **RED ×4.** The three as before, **plus `Four_or_more…` NEW: `segment A01-L02 is shared in round 1`** — the decisive blocker is caught |
| **M3 (control)** | `Release` frees only the first held segment | `SegmentReservations.cs:30-35` | must stay red | **RED ×3:** `Release_frees…`, `Head_on…` (the new retry needs the whole route freed), `Four_or_more…` |
| **M4 (control)** | `Track.Route` excludes the end | `Track.cs:44` | must stay red | **RED ×2:** `Route_covers_every_segment_between_two_lanes_in_order` (incl. the folded cross-aisle assertion) and `Route_between_a_lane_and_itself_is_that_single_segment` |
| **M5 (control)** | `Track.Route` stops rejecting unknown lanes | `Track.cs:28-36` | must stay red | **RED:** `Route_to_an_unknown_lane_is_rejected` |
| **M6** | `Track` built from a hard-coded 8-lane list instead of the rack | `Track.cs:11` | (was: none) | `ShuttleCore.Tests` **RED 1** via the folded cross-aisle assertion; `TrafficCore.Tests` **GREEN**; the Phase-2 literal grep over `TrafficCore` still reports clean — **L2 stands: the gate that should guard "derived from the rack" does not scan `ShuttleCore`** |
| **M7** | `TrafficController` reserves per segment (one `TryReserveAll` per segment, no rollback) | `TrafficController.cs:18-19` | lesson says `Head_on…` red | **GREEN — all 6 pass.** The head-on retry's second route still *starts* at the held `L02`, so hold-and-wait reserves nothing and looks identical to all-or-nothing. The free-first/taken-second probe goes **RED** under M7 and green on the correct table |
| **M9a** | Stress routes reverted to disjoint (`L01-L02`,`L03-L04`,`L05-L06`,`L07-L08`), keep loop/release/bound, then M3 | `TrafficControllerTests.cs:93-96` | overlap must be load-bearing | Stress test alone **GREEN** under M3: with disjoint routes the broken `Release` still drains in one round. **The overlap is load-bearing for the Release catch** |
| **M9b** | Remove the per-round `occupied` assert, then M2 | `TrafficControllerTests.cs:127-134` | the assert must be load-bearing | Stress test alone **GREEN** under M2 (assert gone → no collision check); the full suite is **RED** via the other three tests. **The per-round assert is what makes the stress test catch M2** |
| **M9-bound** | Remove `&& round < 12` (keep overlap), then M3 | `TrafficControllerTests.cs:104` | bound must be load-bearing | `timeout 40 dotnet test` → **exit 124 (hang)**. The bound converts a Release-wedged loop from a hang into a failing assert. It does *not* discriminate a collision (M2 is caught by the per-round assert in round 1) |

**Read it back.** M2, M3, M4, M5, M6 are all caught — the core product is genuinely covered and the stress test is no longer vacuous. M9a/M9b prove the overlap and the per-round assert are what carry the collision/Release proof, not incidental loop shape. But **M1 and M7 are still green**, and both are mutations the lesson's own "Proof details" (`docs/lessons/10-…md:668`) says must turn a named test red (**L4** stands, measured a third time).

### Blocker resolution, measured

1. **Stress test is vacuous — RESOLVED.** `Four_or_more…` (`TrafficControllerTests.cs:91-145`) now holds each granted route for the round and asserts no segment is held twice; M2 goes **RED** at `segment A01-L02 is shared in round 1`. M9b shows the per-round assert is load-bearing; M9a shows the overlapping routes are load-bearing for the M3 catch. The bound is load-bearing as a hang→failure conversion (M9-bound, exit 124 without it).
2. **Refusal test does not prove "holds nothing" — UNRESOLVED.** `TrafficControllerTests.cs:28-29`: Shuttle1 and Shuttle2 request the **same** route `A01-L01→A01-L02`, so the conflict is Shuttle2's first segment; M1 stays **GREEN**. The corrected scenario (first shuttle holds only `L02`; second requests `L01→L02`) turns M1 **RED**. One-line scenario change still owed. The 3-lane rack added here is unused beyond `L02`.
3. **Head-on cannot detect hold-and-wait — UNRESOLVED (improved).** `TrafficControllerTests.cs:72-82` now retries after `Release`, which is real progress (it now catches M3). But Shuttle2's route `L02→L03` begins on the segment Shuttle1 holds, so under M7 there is never a partial hold; all six tests stay **GREEN**. A free-first/taken-second step (or a second live mover) is what makes M7 red.

### Findings (top issues, ranked)

**1. BLOCKER — the refusal test still conflicts on the first segment, so "holds nothing" is unproven.** `TrafficControllerTests.cs:28-29`. A write-as-you-check `TryReserveAll` returns before writing because `A01-L01` (the first requested segment) is already held; `Assert.Empty(SegmentsOwnedBy("Shuttle2"))` (`:33`) — the right assertion — never fires on the bug. **Why it matters for a real shuttle system:** the two-pass atomic write is the *mechanism* that makes a waiting shuttle hold nothing, which is what removes hold-and-wait and therefore deadlock (record 16 §5). If it regressed, this suite would not notice, and the run-loop lesson would wire a false safety net. **Exact fix:** Shuttle1 reserves `A01-L02 → A01-L02` only; Shuttle2 requests `A01-L01 → A01-L02`; keep `Assert.Empty(SegmentsOwnedBy("Shuttle2"))`. Verified: M1 goes RED.

**2. BLOCKER — the head-on test still cannot detect hold-and-wait (M7).** `TrafficControllerTests.cs:72-82`. The retry is a genuine improvement (it now catches M3), but the second route *starts* on the held `L02`, so a per-segment controller reserves nothing and passes. **Why it matters:** M7 is the exact design record 16 §5 rejects; a suite that cannot distinguish all-or-nothing from hold-and-wait cannot certify the deadlock rule. **Exact fix:** make the contended segment the *second* segment of the mover's route (or add a second live mover with a free-first/taken-second overlap) — the verified probe shape in the mutation table.

**3. CONSIDER — the round-bound assertion is redundant as written.** `TrafficControllerTests.cs:144`: `Assert.True(round <= 12, …)` can never be false, because the `while` condition (`:104`) already guards `round < 12`; the real bound check is `Assert.Equal(shuttleRoutes.Count, done.Count)` (`:145`). The bound is doing useful work (it turns a wedged loop into a failure), so keep it; the extra assert could be dropped for clarity. Not a defect in behaviour.

**4. CONSIDER — both test files still end without a trailing newline.** `git diff` shows `\ No newline at end of file`; `tail -c 1` is non-empty for both `TrafficControllerTests.cs` and `TrackTests.cs`. This is **pre-existing at HEAD**, not introduced by this rework, but it is the `dotnet-style` convention to end a file with a newline; fix while touching the files.

**5. CONSIDER — the refusal test's 3-lane rack is now oversized.** `:24` builds `lanesPerAisle: 3` but the test only uses `L01`/`L02` (and the wasted lane is the same signal as Finding 1 — the scenario was meant to reach a later lane and did not). Revert to 2 or use the third lane to place the conflict late.

### What is solid

- **The stress test is now a real test.** It creates contention (overlapping routes), holds grants for the round, checks the invariant every round, and fails (not hangs) when the rule is broken. M2, which certified a collision in review 10, is now caught — that is the M4 done-when's decision-level proof.
- **The proof is load-bearing where it counts.** M9a and M9b isolate exactly which parts carry the M3 and M2 catches (overlap and the per-round no-double-owner assert), so the test's value is understood, not accidental.
- **The count pin is restored by folding, not by weakening.** The cross-aisle assertion still lives (and still catches M4 and M6), but inside the pinned test, so `TrackTests` is three `[Fact]`s and the suite is 79/25 — exactly the lesson's numbers.
- **Discipline held.** No product code changed, no protected file changed, no clock/id/db/broker token in `TrafficCore`, contract untouched, both M3 integration tests green, fresh container exits 0, and all edits are confined to the two test files.

### Lesson defects (for the professor)

The rework **resolves** one lesson defect and confirms the rest:

- **L1 still stands** — Phase 3's per-assembly greps are written backwards (`TrafficCore\.Tests\.dll.*Passed: +6,`), so they print `FAIL` on a correct tree. Confirmed again: the suite gate prints `suite gate OK` while both per-assembly lines print `FAIL`.
- **L2 still stands** — the literal-lane gate scans `TrafficCore` only; M6 passes it even though the folded cross-aisle test now catches that specific mutation. The *gate* is still mis-scoped.
- **L3 still stands** — Phase 4's no-docker grep over-matches the pre-existing `dotnet` process spawns.
- **L4 still stands (measured a third time)** — the "Proof details" mutation promises at `docs/lessons/10-…md:668` are false for M1 and M7: neither named test goes red when the corresponding bug is introduced. This is the lesson defect that keeps reproducing the same two findings across 10, 10b and 10c. Either correct the promised mutations or prescribe scenarios that actually exercise a late-segment conflict and a retry that can observe a partial hold.
- **L5 still stands** — the acceptance criteria check the stress test's *name and count*, not its shape; nothing in the gate can observe contention. The owner fixed the shape by hand this time, but the gate remains unable to catch a regression to the immediate-release loop.
- **L6 — RESOLVED / WITHDRAWN.** The owner folded the cross-aisle assertion into the pinned `Route_covers_…` test, so the lesson's Phase 3 counts (79 total, `ShuttleCore.Tests` 25) and Requirement 1's "these three `[Fact]`s" are both satisfied. The lesson does **not** need to move to 80/26, and the R1/R3 "three vs six" tension is gone. The rework was not blocked by lesson under-specification here.

### Verdict and README

**Not yet — scoped to lesson 10.** The central blocker (a vacuous stress test) is measured resolved: M2, the mutation that certified a collision, now turns the stress test red, and the load-bearing parts are isolated by M9a/M9b. But two prior blockers remain, each one small scenario change away: the refusal test still cannot fail on M1, and the head-on test still cannot fail on M7 — both mutations the lesson says must be caught. The product code is correct throughout; only the proof is short.

**README: not updated**, for two reasons. The verdict is not a pass, and even a clean pass would not move M4: the milestone's done-when is *"a stress scenario with four or more shuttles **runs** with no collisions and no permanent deadlock"*, and the run-loop gate that calls `TrafficController.TryReserveRoute` before `TaskDispatcher.AssignNextAsync` is the next lesson (Requirement 6 forbids wiring it here). The M4 row is left exactly as-is; no other part of `README.md` was touched.

### What the owner still owes

1. **Fix the refusal scenario** (`TrafficControllerTests.cs:28-29`): Shuttle1 reserves only a later segment (`A01-L02 → A01-L02`), Shuttle2 requests `A01-L01 → A01-L02`. M1 must go red. (Verified fix.)
2. **Fix the head-on scenario** (`:72-82`): make the contended segment the second segment of Shuttle2's route (or add a second live mover), so a per-segment hold-and-wait controller leaves a partial hold and the test goes red under M7. (Verified probe shape.)
3. **Commit** the changes (one small commit, e.g. `M4: fix refusal and head-on scenarios per review 10c`).
4. **Answer the explain-it questions** below out loud or in writing — none have been answered across review 10, 10b or 10c.
5. **Professor:** fix L1, L2, L3, L4 and L5 (L6 is withdrawn).

### Explain-it check

The three decisions an interviewer is most likely to probe here:

1. **`TryReserveAll` checks every segment before writing any of them (`SegmentReservations.cs:11-25`).** Walk the two passes. Why does a refusal on a *later* segment leave a partial hold if you merge the loops? Why does `TryReserveRoute_refuses_and_holds_nothing…` (`:28-34`) still pass on that bug, and what exact scenario change makes it fail? (M1.)
2. **The stress test holds each grant for the round and asserts over the test's own `inFlight` record (`TrafficControllerTests.cs:99-145`).** Why does that assert catch M2 while the 10b loop that released inside the same iteration did not? Why can the test not rely on the reservation table itself to detect a collision? What is the bound for, and why is it honest? (M2, M9a, M9b, M9-bound.)
3. **The head-on rule is "wait holding nothing" (`TrafficController.cs:16-36`, record 16 §5).** The retry test now passes under M7. Explain why the second route starting on the held segment cannot show a partial hold, and what observation separates deadlock *avoidance* from a one-shot refusal. (M7.)

### Lines or blocks that look unclear, copied, or unexplained

- `src/TrafficCore.Tests/TrafficControllerTests.cs:28-29` — the tested route is identical for both shuttles, yet the test is named "holds nothing". Explain what was intended versus what it proves. (The `lanesPerAisle: 3` at `:24` is a fossil of the intended late-lane conflict.)
- `src/TrafficCore.Tests/TrafficControllerTests.cs:144` — `Assert.True(round <= 12)` is unreachable-false behind the `round < 12` loop guard; be ready to say which assert actually enforces the bound.
- `src/TrafficCore.Tests/TrafficControllerTests.cs:127-134` — the per-round `occupied` assert checks the *test's* record, not `SegmentReservations`. Explain why that is the correct place to detect a collision (a single-owner `Dictionary` overwrites silently).
- `src/ShuttleCore.Tests/TrackTests.cs:24,27` — the cross-aisle assertion folded into `Route_covers_…`; explain why it catches M4 and M6 even though it is not a fourth `[Fact]`.

### Revisit list

- **Atomicity where it bites, not where it is easy.** A conflict on the first segment cannot distinguish check-all/write-all from write-as-you-check. Test the property on a later segment. (M1.)
- **Avoidance vs detection, again.** A retry that begins on the already-held segment never produces a partial hold; a free-first/taken-second route does. (M7.)
- **Load-bearing tests.** M9a/M9b are the model: mutate the test and the product and record which part carries the catch, so no loop shape is mistaken for a proof.
- **Gate precision (unchanged).** L1/L2/L3 still do not test what they name; L4's promised mutations still do not happen. Run every gate against a deliberately broken tree before trusting it.
- **Next:** the run-loop gate at M4's second half — call `TrafficController.TryReserveRoute` before `TaskDispatcher.AssignNextAsync`, decide the retry policy on refusal, own the table from one caller, and log the `(shuttle, route, blockedSegment, owner)` decision.

### End-product check

`TrafficCore` is still the right seam and the product inside it is unchanged and correct. This rework buys the thing that was missing: a decision-level stress scenario that genuinely fails when two shuttles can be recorded on the same segment. The remaining risk is narrower than in review 10 but the same in kind — **the proof still cannot fail on two named mutations** (atomicity, hold-and-wait), which are the two properties that make the deadlock rule work. Fix those two scenarios and lesson 10's proof matches the rule it claims to demonstrate; leave them and the run-loop lesson will inherit a safety net with two holes exactly where the safety claim is made.

### Cleanup

- `docker compose -f docker/compose.yml down` run after the integration phase; `docker compose ps` empty at the end.
- `/tmp/opencode/rev10c` and `/tmp/opencode/rev10c-pristine` deleted after verifying the copy's source was `diff`-clean against pristine; container image `shuttle-fleet-build-rev10c` removed.
- `dotnet build-server shutdown` run (MSBuild/compiler servers from the mutation builds shut down).
- `git status` shows exactly the two owner-modified test files and this review doc; no file under `src/` was modified by this review; `README.md` untouched.

---

## Re-review 10d — the third rework

**Date:** 2026-10-12
**Reviewed:** the uncommitted working tree on top of `8cc815a`. `git status --short` shows exactly two modified files — `src/ShuttleCore.Tests/TrackTests.cs` and `src/TrafficCore.Tests/TrafficControllerTests.cs` — plus this untracked review and the professor's untracked lesson-11/record-17 files (not part of this review, left alone). `git diff --stat -- src/TrafficCore src/ShuttleCore/Track.cs src/FleetManager` is empty, so **no product code changed**. All acceptance, regression and mutation runs are against the files on disk. Mutations ran against a working-tree copy at `/tmp/opencode/rev10d` (rsync, `.git`/`docs/reviews`/`bin`/`obj` excluded), and the copy's source was verified byte-identical to the tree after every restoration, ending with a `diff`-clean copy and a green full suite before deletion.

**Verdict: pass (clean), scoped to lesson 10.**

**In one paragraph — the two outstanding blockers are measured resolved.** The refusal test now puts the conflict on Shuttle2's **second** segment (`L01` free, `L02` taken), so mutation **M1** (write-as-you-check) leaves a partial hold `{A01-L01}` behind and the `Assert.Empty(SegmentsOwnedBy("Shuttle2"))` at `:33` goes **red** — the lesson's named `TryReserveRoute_refuses_and_holds_nothing_…` test catches it for the first time. The head-on test now has Shuttle1 hold late (`L02→L03`) while Shuttle2 asks free-first/taken-second (`L01→L02`), so mutation **M7** (per-segment controller) writes `L01` before refusing on `L02`, and the emptiness assert at `:77` goes **red** — the lesson's named `Head_on_…` test catches it for the first time. M2 stays red in four tests, including the stress test's per-round `occupied` assert; controls M3/M4/M5 all stay red; the counts are exactly the lesson's (RC 0, 8 `Passed!`, 0 `Failed!`, 79 tests, `TrafficCore.Tests` 6, `ShuttleCore.Tests` 25); both M3 DB regressions pass; the fresh container exits 0. The proof now fails on the two mutations the rule is built on. What remains is consider-level (the "head-on" name does not match the scenario's geometry — it is a same-direction follow, see Finding 1), process (commit the test changes; answer the explain-it questions), and the standing lesson defects L1/L2/L3/L5 (L4 is now resolved, L6 remains withdrawn).

M4 as a milestone is **not** done on this slice and the README is **not** touched: the done-when's "a stress scenario … **runs**" half is lesson 11's run-loop gate, which this lesson forbids wiring. The pass is for lesson 10's decision-level proof only.

---

### The diff they made (working tree vs `8cc815a`)

`src/ShuttleCore.Tests/TrackTests.cs` (+2): **unchanged from 10c** — the cross-aisle assertion still folded into the pinned `Route_covers_every_segment_between_two_lanes_in_order` (`:23-27`); exactly three `[Fact]`s (verified: `--list-tests | grep -c 'TrackTests\.'` = 3).

`src/TrafficCore.Tests/TrafficControllerTests.cs` (+83 −18 across 10b/10c/10d; +0 −? versus 10c):

- Refusal test (`:22-35`): Shuttle1 holds `A01-L02 → A01-L03`; Shuttle2 requests `A01-L01 → A01-L02` — **free-first/taken-second**; asserts refused, `SegmentsOwnedBy("Shuttle2")` empty (`:33`), reason contains `"A01-L02 held by Shuttle1"` (`:34`). The 3-lane rack (`:24`) is now fully used (L01 free, L02 blocker, L03 Shuttle1's destination) — 10c Finding 5 is resolved.
- Head-on (`:66-84`): Shuttle1 reserves `A01-L02 → A01-L03`; Shuttle2 requests `A01-L01 → A01-L02`; asserts refusal + emptiness (`:77`) + reason (`:78`); then `Release("Shuttle1")` (`:80`) and retries Shuttle2's route, asserting it is granted (`:83`). **Both shuttles move in the same direction** — see Finding 1.
- Stress test (`:87-147`): **unchanged from 10c** — overlapping routes `L01→L04, L02→L05, L03→L06, L04→L07` on an 8-lane rack (`:92-98`), `inFlight` holds each grant for the round, `while (done.Count < shuttleRoutes.Count && round < 12)` (`:105`), per-round `occupied` assert over `inFlight.Values` (`:128-135`), grants released at the end of the round (`:137-142`), then `Assert.True(round <= 12)` (`:145`) and `Assert.Equal(4, done.Count)` (`:146`). Drains in 4 rounds on the correct table.

### Commands run, verbatim key output

Run from the repo root unless a phase says `src/`. `dotnet --version` → `10.0.112`; `docker --version` → `29.8.1`; `docker compose ps` empty at start and end.

Phase 1 (track/traffic core exist, pinned names): **pass** — all greps matched, `Track(Rack)` and the atomic `TryReserveAll` call present.

Phase 2 (purity, no literals): **pass** — clock/id scan, db/msg scan, literal-lane scan over `TrafficCore`, and the `ShuttleCore` db/msg scan all `rc 1`; `PackageReference` 0, `ProjectReference` 1. (Literal-lane scan still covers `TrafficCore` only — L2.)

Phase 3 (build + the lesson's exact gate command):

```
Build succeeded. 0 Warning(s) 0 Error(s)
---RC:0---
Passed!  - Failed:     0, Passed:    25, … - ShuttleCore.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     8, … - Messaging.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     4, … - Contracts.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     6, … - TrafficCore.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    18, … - FleetCore.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     2, … - MqttMessaging.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    11, … - FleetManager.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     5, … - ShuttleSim.Tests.dll (net10.0)
assemblies-passed=8 assemblies-failed=0 total=79
suite gate OK
FAIL: TrafficCore.Tests should show 6
FAIL: ShuttleCore.Tests should show 25
```

**Counts observed: RC 0, 8 `Passed!`, 0 `Failed!`, 79 tests, `TrafficCore.Tests` = 6, `ShuttleCore.Tests` = 25, `suite gate OK`.** The two per-assembly lines still print `FAIL` on a correct tree because their regexes are written backwards (L1) — the numbers are right.

Phase 3b (nine anchored names): **pass** — all nine `\.<name>$` matches present; `ShuttleCore.Tests --list-tests | grep -c 'TrackTests\.'` = **3**.

Phase 4: traffic tests pure (no `Trait`, no Npgsql/Postgres, both `rc 1`). The no-docker grep still false-positives on the pre-existing `dotnet` spawns in `StartTests.cs:17` and `DemoTests.cs:18` (L3).

Phase 5: **pass** — 16 projects, `TrafficCore`/`TrafficCore.Tests`/`ShuttleCore` reference present.

Phase 6: **pass** — protected files unchanged; contract v1/v2, no v3; the four dispatcher methods present.

Regression with DB up:

```
$ docker compose -f docker/compose.yml up -d            (broker + database up)
$ dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration"
  Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks [625 ms]
  Passed FleetManager.Tests.EndToEndTests.Three_shuttles_finish_queue_and_state_survives_restart [144 ms]
$ docker compose -f docker/compose.yml down             (network and both containers removed)
```

Fresh container:

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build-rev10d .   exit 0
$ docker run --rm shuttle-fleet-build-rev10d          RC 0, 8 Passed!, 0 Failed!
  Passed! … ShuttleCore.Tests.dll (net10.0)  25
  Passed! … TrafficCore.Tests.dll (net10.0)   6
```

### Mutation suite (working-tree copy `/tmp/opencode/rev10d`)

Baseline in the copy: full pure suite green — RC 0, 8 `Passed!`, 79 tests, `TrafficCore.Tests` 6/6, `ShuttleCore.Tests` 25/25. Each mutation applied to the copy, the relevant project's suite run, files restored from pristine backups (`diff`-verified byte-identical), next applied; the copy ended `diff`-clean against the tree with a green full run. One protocol note: M4/M5/M6 mutate only `Track.cs`, but the M3 step had left a stale mutant in `SegmentReservations.cs` in the copy; before the clean M6 run both files were restored to pristine so the M6 result below is uncontaminated (the first M6 run showed 2 extra red TrafficCore tests caused by stale M3 — discarded and re-run clean).

| # | Mutation | File / line | Red test(s) | Observed |
|---|---|---|---|---|
| **M1** | Write-as-you-check in `TryReserveAll` (partial hold on refusal) | `SegmentReservations.cs:11-25` | lesson says `TryReserveRoute_refuses_and_holds_nothing_…` red | **RED ×2: `TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken`** and **`Head_on_requests_resolve_without_deadlock`**. The block is Shuttle2's second segment (L02), so the mutant writes L01 before refusing; the `Assert.Empty(SegmentsOwnedBy("Shuttle2"))` (`:33`, `:77`) sees `[A01-L01]` and fires. **RESOLVED — first time red.** |
| **M2** | Drop the "different owner" check (grant every request, overwrite owners) | `SegmentReservations.cs:11-18` | four tests | **RED ×4:** `refuses…`, `Two_shuttles…`, `Head_on…`, **and `Four_or_more…`** via the per-round `occupied` assert (round 1, `segment A01-L02 is shared`). **STAYS RESOLVED.** |
| **M3 (control)** | `Release` frees only the first held segment | `SegmentReservations.cs:28-37` | must stay red | **RED ×2: `Release_frees_every_segment_the_owner_held` and `Four_or_more…`** (a wedged `done.Count == 1` → `Assert.Equal(4, done.Count)` fails). **Note: `Head_on…` is now GREEN under M3** — the flipped scenario means M3's one-segment release frees exactly L02, which is all the retry needs; the "retry needs the whole route freed" property 10c reported is gone (see Finding 2). Control still satisfied. |
| **M4 (control)** | `Track.Route` excludes the end | `Track.cs:44` | must stay red | **RED ×2:** `Route_covers_every_segment_between_two_lanes_in_order` (incl. the folded cross-aisle assertion) and `Route_between_a_lane_and_itself…` |
| **M5 (control)** | `Track.Route` stops rejecting unknown lanes | `Track.cs:28-36` | must stay red | **RED: `Route_to_an_unknown_lane_is_rejected`** |
| **M6** | `Track` built from a hard-coded 8-lane list instead of the rack | `Track.cs:11` | (was: none) | `ShuttleCore.Tests` **RED 1** via `Route_covers_every_segment_…` (the cross-aisle assertion needs `A02-L01`, absent from the literal list); `TrafficCore.Tests` **all green**; the lesson's Phase-2 literal grep over `TrafficCore` still reports clean (`rc 1`); a direct `grep '"A[0-9]{2}-L[0-9]{2}' src/ShuttleCore/Track.cs` finds the literals — **L2 stands: the gate that should guard "derived from the rack" still does not scan `ShuttleCore`** |
| **M7** | `TrafficController` reserves per segment (one `TryReserveAll` per segment, no rollback) | `TrafficController.cs:18-19` | lesson says `Head_on_…` red | **RED ×2: `Head_on_requests_resolve_without_deadlock`** (line 77, the emptiness assert) **and `TryReserveRoute_refuses_and_holds_nothing_…`**. Shuttle2's route starts free (`L01` written) then hits held `L02`; the partial hold `{A01-L01}` fires the assert. **RESOLVED — first time red.** |
| **M9a** | Stress routes reverted to disjoint (`L01-L02`,`L03-L04`,`L05-L06`,`L07-L08`), keep loop/release/bound, then M3 | `TrafficControllerTests.cs:94-97` | overlap must be load-bearing | Stress test alone **GREEN** under M3 (disjoint routes drain in one round even with a broken `Release`); the full suite is still RED via `Release_frees…`. **The overlap is load-bearing for the stress test's Release catch.** |
| **M9b** | Remove the per-round `occupied` assert, then M2 | `TrafficControllerTests.cs:128-135` | the assert must be load-bearing | Stress test alone **GREEN** under M2 (no collision check left); full suite still RED via `refuses…`, `Two_shuttles…`, `Head_on…`. **The per-round assert is what makes the stress test catch M2.** |
| **M9-bound** | Remove `&& round < 12`, keep overlap, then M3 | `TrafficControllerTests.cs:105` | bound must be load-bearing | `timeout 40 dotnet test` → **exit 124 (hang)**. The bound converts a Release-wedged loop into a failing assert; it does not discriminate a collision (M2 is caught by the per-round assert in round 1). |

**Read it back.** M1 and M7 — the two mutations that review 10, 10b and 10c each measured green — are now **both red**, each caught by the exact test the lesson's "Proof details" names. The stress test's load-bearing parts are isolated again (M9a: overlap; M9b: per-round assert; M9-bound: bound as hang→failure). M2 stays red in four tests. All three controls (M3, M4, M5) stay red. One subtlety changed: the head-on retry is no longer sensitive to M3 (Finding 2) — M3 is still red via `Release_frees…` and the stress test.

### Blocker resolution, measured

1. **Stress test vacuous — RESOLVED (unchanged from 10c).** Overlapping routes held across rounds, per-round invariant over the test's own `inFlight` record, bounded rounds; M2 red at round 1; the load-bearing parts confirmed again by M9a/M9b/M9-bound.
2. **Refusal test does not prove "holds nothing" — RESOLVED.** Shuttle2's route is free-first (`L01`) / taken-second (`L02`); M1 leaves `{A01-L01}` behind and `Assert.Empty(SegmentsOwnedBy("Shuttle2"))` (`:33`) fires. Verified red under M1; green on the correct table.
3. **Head-on cannot detect hold-and-wait — RESOLVED (with a naming caveat).** M7 writes `L01` before refusing on `L02`, and the emptiness assert at `:77` fires. Verified red under M7; green on the correct table. The caveat: the scenario is geometrically a *follow*, not a face-to-face head-on (Finding 1).

### Findings (top issues, ranked)

**1. CONSIDER — the head-on test is named "Head_on" but models a same-direction *follow*, not a facing encounter.** `TrafficControllerTests.cs:66-84`: Shuttle1 moves `A01-L02 → A01-L03` and Shuttle2 moves `A01-L01 → A01-L02` — both travel in the same direction (increasing lane order). A head-on is two shuttles facing each other (the toy's safe case is `SH-A: L01→L04` vs `SH-B: L04→L01`, lesson 10 `:425-429`); this is a rear-end/follow scenario where the follower's destination is the leader's start. State plainly: **the test name does not match its behavior.** What it *does* prove — refusal → hold nothing → blocker releases → retry grants — is exactly the rule's observable contract, so the test has real value; but there is no facing geometry in it. The tension is structural: `Track.Route` normalizes to lower→higher, so a facing second shuttle's route is the *same* ordered list and its first segment is already held — a per-segment mutant could never leave a partial hold, and M7 would stay green. The free-first/taken-second geometry that makes the proof work *is* a follow. The name is pinned by the lesson (Phase 3b anchors it), so the owner cannot rename it alone: the professor should reword the lesson's description of this test (it demonstrates the no-hold-and-wait rule resolving opposing-traffic contention, of which the follow is the observable shape) or consciously accept the shorthand. Not a blocker; an interviewer will ask.

**2. CONSIDER — the head-on retry is no longer load-bearing for the `Release` proof (M3).** Under the flipped scenario, `Release`-frees-one-segment (M3) frees exactly `L02` — the only segment the retry needs — so `Head_on…` now passes under M3 where 10c measured it red. The `Release` property is still proven: `Release_frees_every_segment_the_owner_held` and the stress test (a wedged `done.Count`) both go red under M3. This is a correct trade (the scenario change is what bought the M7 catch), but be ready to say which tests carry the `Release` proof and why the head-on retry no longer does.

**3. CONSIDER — `Assert.True(round <= 12)` (`:145`) is unreachable-false behind the `round < 12` loop guard.** At exit `round ∈ [1..12]` by construction, so the assert cannot fail; the real bound enforcement is `Assert.Equal(shuttleRoutes.Count, done.Count)` (`:146`). The bound genuinely converts a wedged loop into a failure (M9-bound: without it, exit 124), so keep the guard; the `round <= 12` assert could be dropped for clarity. Carried from 10c.

**4. CONSIDER — both test files still end without a trailing newline.** `git diff` shows `\ No newline at end of file` for both `TrafficControllerTests.cs` and `TrackTests.cs`; `tail -c 1` is non-empty on both. Pre-existing at HEAD; the `dotnet-style` convention is to end files with a newline. Fix while committing.

**Resolved from 10c:** Finding 5 (oversized rack) — the refusal test's `Rack(1,3,2)` is now fully used (L01 free, L02 blocker, L03 destination) and the head-on rack is fully used too. Not a finding anymore.

### What is solid

- **The two mutations the rule is built on are now caught by the named tests.** M1 red via `TryReserveRoute_refuses_and_holds_nothing_…`; M7 red via `Head_on_requests_resolve_without_deadlock` — the exact promises in the lesson's "Proof details" (`:668`), true for the first time in four reviews. Lesson L4 is resolved.
- **The proof is load-bearing and understood.** M9a/M9b/M9-bound isolate which parts carry which catch (overlap, per-round assert, bound), and the stress test still fails (not hangs) under M2 and M3.
- **The count pin held.** The cross-aisle assertion stays inside the pinned `Route_covers_…` test: TrackTests is exactly 3, the suite is exactly 79, `ShuttleCore.Tests` 25, `TrafficCore.Tests` 6 — the lesson's numbers on a correct tree.
- **Discipline held.** No product code, no protected file, no contract change; both M3 DB regressions green; the fresh container exits 0 with 8 `Passed!`; edits confined to the two test files; no clock/id/IO/literal token anywhere near `TrafficCore`.

### Lesson defects (for the professor)

- **L1 still stands** — Phase 3's per-assembly greps are written backwards and print `FAIL` on a correct tree (reproduced verbatim above; `suite gate OK` while both per-assembly lines `FAIL`).
- **L2 still stands** — the literal-lane gate scans `TrafficCore` only; M6 passes it. The cross-aisle test catches that specific mutation, but the *gate* is still mis-scoped; a literal list covering A01 only will keep slipping past every gate except the test's `A02` route.
- **L3 still stands** — Phase 4's no-docker grep over-matches the pre-existing `dotnet` spawns.
- **L4 — RESOLVED.** Both "Proof details" mutation promises now hold empirically for the first time: M1 turns `TryReserveRoute_refuses_and_holds_nothing_…` red, M7 turns `Head_on_…` red. No lesson change required.
- **L5 still stands** — the acceptance criteria check the stress test's *name*, not its shape; nothing in Phase 3b observes contention, hold-across-rounds, or the per-round assert. The owner fixed the shape by hand again; the gate still cannot catch a regression to the immediate-release loop, and M9a/M9b are the demonstration that the shape is what carries the proof.
- **L6 remains withdrawn** — the folded cross-aisle assertion keeps R1's "these three `[Fact]`s" and Phase 3's counts exactly satisfied.

Lesson 10's concept list (three: track-as-segments, segment reservation, deadlock rule) is intact and each was taught; the cap holds; nothing in the concept list is unguarded by a gate that this re-review trips.

### Verdict and README

**Pass (clean), scoped to lesson 10.** The decision-level M4 proof — 4+ shuttles, no shared segment, no permanent deadlock — now fails on the exact mutations that would break the rule (M1, M2, M7), the three controls stay red, every acceptance phase is green in substance, and both regressions pass. The remaining findings are consider-level (naming, one lost M3 sensitivity in a single test that two others carry, one redundant assert, no trailing newline) and none blocks the lesson's acceptance.

**README: not updated**, deliberately. Even on this pass: (a) M4's done-when is *"a stress scenario with four or more shuttles **runs** with no collisions and no permanent deadlock"* — the "runs" half is lesson 11's run-loop gate calling `TrafficController.TryReserveRoute` before `TaskDispatcher.AssignNextAsync`, which lesson 10 Requirement 6 forbids wiring here and which does not exist yet; (b) the reviewer's rule is to move a milestone row only for a milestone that is actually done, and M4 is not. The M4 row stays exactly as-is; no other part of `README.md` was touched.

### What the owner still owes

1. **Commit the two test files** (one small commit, e.g. `M4: make refusal and head-on scenarios catch M1 and M7 per review 10d`).
2. **Answer the explain-it questions** below out loud or in writing — none have been answered across review 10, 10b, 10c or 10d.
3. Optionally (consider-level): drop the redundant `round <= 12` assert (`:145`), add the trailing newlines, and be ready to explain the follow-vs-head-on naming.
4. **Professor:** fix L1, L2, L3, L5 (L4 resolved, L6 withdrawn), and decide what to do about the pinned "Head_on" name — reword the lesson's description (the test proves the no-hold-and-wait rule on opposing-traffic contention; the observable geometry is a follow) or accept the shorthand and say so.

### Explain-it check

The three decisions an interviewer is most likely to probe here:

1. **The refusal test's scenario changed to free-first/taken-second (`TrafficControllerTests.cs:22-35`).** Walk `TryReserveAll`'s two passes. Why did the old scenario (both shuttles requesting the same route) leave M1 green, and why does Shuttle2's route now make a write-as-you-check table leave `{A01-L01}` behind? Which assert fires, and what does it observe? (M1, line 33.)
2. **The head-on test (`:66-84`) is named "Head_on" but both shuttles move in the same direction.** Is that a head-on? Given `Track.Route` normalizes lower→higher, could a genuinely facing scenario (L01→L03 vs L03→L01) ever leave a partial hold for M7 to catch? What observation separates deadlock avoidance from a one-shot refusal, and why does the retry after `Release` matter? (M7, Finding 1.)
3. **The stress loop (`:87-147`) checks its own `inFlight` record, not `SegmentReservations`.** Why can the reservation table not detect a double owner (a single-owner `Dictionary` overwrites silently)? Why does the per-round `occupied` assert catch M2 while the 10b loop that released in the same iteration could not? What is the bound for, and why is `Assert.Equal(4, done.Count)` the assert that enforces it while `Assert.True(round <= 12)` (`:145`) cannot fail? (M2, M9a, M9b, M9-bound.)

### Lines or blocks that look unclear, copied, or unexplained

- `src/TrafficCore.Tests/TrafficControllerTests.cs:66-84` vs `:22-35` — `Head_on…` is the refusal scenario plus a `Release` and a retry. Say what the retry adds and why the two tests share their first half; be ready to defend the "head-on" name against the follow geometry (Finding 1).
- `src/TrafficCore.Tests/TrafficControllerTests.cs:80-83` — the retry no longer proves `Release` frees everything (M3 passes it now). Say which two tests carry the `Release` proof and why this one lost it.
- `src/TrafficCore.Tests/TrafficControllerTests.cs:145` — `Assert.True(round <= 12)` is unreachable-false behind the loop guard; say which assert actually enforces the bound.
- Both test files still `\ No newline at end of file` — cosmetic, but a reviewer/compiler-convention detail worth fixing while committing.

### Revisit list

- **Atomicity where it bites, at last.** Free-first/taken-second is the geometry that makes a write-as-you-check table observable; the refusal test now has it. Teach why first-segment conflicts are blind to M1.
- **Avoidance vs detection, resolved, with its geometry cost.** A retry that begins on the held segment cannot show a partial hold; the fixed scenario trades geometry for observability, and the "head-on" name is leftover from that trade.
- **Which test carries which proof.** M9a/M9b/M9-bound are the model; the head-on retry lost its M3 sensitivity in the flip, and a support engineer should never have to guess which test stands for `Release` semantics.
- **Gate precision (unchanged).** L1/L2/L3/L5 still do not test what they name; run every gate against a deliberately broken tree before trusting it.
- **Next:** lesson 11's run-loop gate — call `TrafficController.TryReserveRoute` before `TaskDispatcher.AssignNextAsync`, own the table from one caller, decide the retry policy on refusal, and log the `(shuttle, route, blockedSegment, owner)` decision. It now inherits a proof that can fail on the mutations that matter.

### End-product check

`TrafficCore` is still the right seam, the product inside it is unchanged and correct, and — the change that matters — the decision-level proof now fails on exactly the two mutations the deadlock rule is designed against (M1 partial holds, M7 hold-and-wait) plus M2's overwrite-everything. Lesson 11's run loop will be built on a safety net that can actually tear. The risks carried forward are small and named: the head-on test's name/geometry mismatch (an explain-it and documentation issue, not a correctness one), the retry's lost M3 sensitivity (carried by two other tests), and the standing gate defects L1/L2/L3/L5. M4's full done-when still waits on lesson 11's "runs" half; this slice is its pure, proven decision core.

### Cleanup

- `docker compose -f docker/compose.yml down` run after the integration phase; `docker compose ps` empty at the end.
- Container image `shuttle-fleet-build-rev10d` removed; `/tmp/opencode/rev10d`, the debug repro project, pristine backups and log files deleted after the copy was verified `diff`-clean against the tree and the restored suite went green.
- `dotnet build-server shutdown` run.
- `git status` shows exactly: the two owner-modified test files, this review doc, and the professor's untracked lesson-11/record-17 files (untouched). No file under `src/` was modified by this review; `README.md` untouched.
