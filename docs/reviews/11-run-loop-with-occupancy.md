# Review 11: Physical occupancy, the fleet-side occupancy view, and the run-loop gate (lesson 11, second M4 slice) — re-review

**Date:** 2026-10-12 (re-review)
**Reviewed:** the owner's uncommitted working-tree fixes on top of `edd8621` (`M4: feat: add RunLoopGate and OccupancyMap for route management and occupancy tracking`), which was itself reviewed in the first pass of this file. Working tree at review time: `M src/FleetCore.Tests/FleetCore.Tests.csproj`, `D src/FleetCore.Tests/RunLoopGateTests.cs`, `M src/FleetManager/FleetManager.csproj`, `?? src/FleetManager.Tests/RunLoopGateTests.cs`. The fixes are not committed yet; the owner should commit them (one concept per commit, per AGENTS.md).
**Scope:** lesson 11 against its own assignment and [record 17](../decisions/17-occupancy-and-fault-reservations.md), on top of [record 16](../decisions/16-traffic-segments-and-deadlock-rule.md) and [record 15a](../decisions/15a-task-dispatcher-and-db-test-fixtures.md). This is the second half of M4; wiring the gate into a real `Program.cs` run loop is explicitly the next lesson.
**Contract checked:** `docs/contract/v2.md` still `Version: 2`, no `v3.md`. Occupancy is fleet-internal state driven by the existing `ShuttleStatus.Position` field; no wire change. Correct.

**Verdict: pass.**

Both should-fix issues from the first pass are resolved, and the full acceptance run is green:

1. **Tests in the wrong project — fixed.** `RunLoopGateTests.cs` now lives at `src/FleetManager.Tests/RunLoopGateTests.cs` (the copy in `src/FleetCore.Tests/` is deleted). The moved file is byte-identical to the original except one line — the namespace, changed from `FleetManager` to `FleetManager.Tests`, which matches the convention of every other file in that project. The layering inversion is gone: `FleetCore.Tests.csproj` no longer references `FleetManager` (or `ShuttleCore`). The four tests now execute inside `FleetManager.Tests.dll` (verified by a `--filter` run: `Passed: 4 … FleetManager.Tests.dll`), and their fully-qualified names are `FleetManager.Tests.RunLoopGateTests.*` — namespace and assembly agree, so the mismatch symptom noted in the first pass is gone. `FleetManager.Tests.dll` runs **15** tests (11 + 4); `FleetCore.Tests.dll` is back to its baseline **18**.
2. **`FleetManager.csproj` reference count — fixed.** The redundant `..\ShuttleCore\ShuttleCore.csproj` line is removed; the file now has exactly 5 `ProjectReference` lines (FleetCore, Contracts, Messaging, MqttMessaging, TrafficCore), matching the lesson. `RunLoopGate` still compiles unchanged — `Track` arrives transitively via `TrafficCore`, exactly as the first pass predicted.

One new minor finding (a leftover reference, not a blocker) and one new lesson defect (a check pattern that can never match) are documented below. Neither blocks M4's core claim.

---

## Commands run, verbatim key output

Run from the repo root unless a phase says `src/`. The mutation ran against a throwaway copy of the working tree at `/tmp/opencode/rev11-r2`, deleted afterward; no experiment touched `src/`, and no file under `src/` was modified by this review. No broker, container, or `dotnet run` process is left running.

### Environment

```
$ dotnet --version        → 10.0.112
$ git status --short     → M src/FleetCore.Tests/FleetCore.Tests.csproj
                            D src/FleetCore.Tests/RunLoopGateTests.cs
                            M src/FleetManager/FleetManager.csproj
                            ?? docs/reviews/11-run-loop-with-occupancy.md
                            ?? src/FleetManager.Tests/RunLoopGateTests.cs
$ pgrep -af 'dotnet run'  → none
```

### The two fixes, exactly as the owner made them

```
$ git diff -- src/FleetManager/FleetManager.csproj
-    <ProjectReference Include="..\ShuttleCore\ShuttleCore.csproj" />
  (5 ProjectReference lines remain: FleetCore, Contracts, Messaging, MqttMessaging, TrafficCore)

$ git diff -- src/FleetCore.Tests/FleetCore.Tests.csproj
-    <ProjectReference Include="..\ShuttleCore\ShuttleCore.csproj" />
-    <ProjectReference Include="..\FleetManager\FleetManager.csproj" >
  (FleetCore + TrafficCore remain; baseline at 54a850b was FleetCore alone — see issue 3)

$ git show HEAD:src/FleetCore.Tests/RunLoopGateTests.cs > /tmp/opencode/orig.cs
$ diff /tmp/opencode/orig.cs src/FleetManager.Tests/RunLoopGateTests.cs
4c4
< namespace FleetManager;
---
> namespace FleetManager.Tests;
  (the only change in the moved file)
```

### Phase 1 — the occupancy map, gate, and test exist, with the pinned names

```
$ test -f src/FleetManager/OccupancyMap.cs   → OK
$ test -f src/FleetManager/RunLoopGate.cs    → OK
$ test -f src/FleetManager.Tests/RunLoopGateTests.cs   → OK
$ test ! -f src/FleetCore.Tests/RunLoopGateTests.cs    → OK (no longer there)
$ grep -qE 'class OccupancyMap' src/FleetManager/OccupancyMap.cs   → OK
$ grep -qE 'class RunLoopGate' src/FleetManager/RunLoopGate.cs     → OK
$ for m in GetOccupiedLane SetOccupiedLane IsOccupiedByOthers; do grep -qE "\b$m\b" src/FleetManager/OccupancyMap.cs; done
  → all three OK
$ for m in CanMove; do grep -qE "\b$m\b" src/FleetManager/RunLoopGate.cs; done  → OK
$ grep -qE 'OccupancyMap.*Track.*TrafficController' src/FleetManager/RunLoopGate.cs  → OK
```

Phase 1: **pass** — including the file-path check that failed in the first pass.

### Phase 2 — purity: no clock, no ids, no I/O types in the new files

```
$ grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' src/FleetManager/OccupancyMap.cs src/FleetManager/RunLoopGate.cs  → no matches (rc 1)
$ grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts|Postgres'            src/FleetManager/OccupancyMap.cs src/FleetManager/RunLoopGate.cs  → no matches (rc 1)
$ grep -rnE '"A[0-9]{2}-L[0-9]{2}'                                     src/FleetManager/OccupancyMap.cs src/FleetManager/RunLoopGate.cs  → no matches (rc 1)
```

Phase 2: **pass.** Unchanged from the first pass (the product files were not touched — `git diff HEAD` on them is empty).

### Phase 3 — project references are wired

```
$ grep -q 'TrafficCore' src/FleetManager/FleetManager.csproj            → OK
$ grep -q 'TrafficCore' src/FleetManager.Tests/FleetManager.Tests.csproj → OK
$ grep -q 'ShuttleCore' src/FleetManager.Tests/FleetManager.Tests.csproj → OK
$ grep -c 'ProjectReference' src/FleetManager/FleetManager.csproj       → 5   (lesson expects 5) ✓
$ grep -c 'ProjectReference' src/FleetManager.Tests/FleetManager.Tests.csproj → 5   (matches lesson) ✓
$ grep -c 'ProjectReference' src/FleetCore.Tests/FleetCore.Tests.csproj → 2   (baseline was 1 — see issue 3)
```

Phase 3: **pass.** Both pinned counts are exactly 5.

### Phase 4 — build, and the pure suite with NO database needed (the container's command)

```
$ cd src && dotnet build
  Build succeeded.  0 Warning(s)  0 Error(s)

$ T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
---RC:0---
Passed!  - Failed:     0, Passed:    25, Skipped:     0, Total:    25, Duration: 493 ms - ShuttleCore.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    18, Skipped:     0, Total:    18, Duration: 371 ms - FleetCore.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8, Duration: 247 ms - Messaging.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 512 ms - Contracts.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 629 ms - MqttMessaging.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5, Duration: 499 ms - ShuttleSim.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:    15, Skipped:     0, Total:    15, Duration: 447 ms - FleetManager.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6, Duration: 138 ms - TrafficCore.Tests.dll (net10.0)
assemblies-passed=8 assemblies-failed=0 total=83
```

Phase 4 gate (`RC 0`, 8 `Passed!`, 0 `Failed!`, 83 total): **pass.** `FleetCore.Tests.dll` is back to its baseline 18; `FleetManager.Tests.dll` shows **15**.

**Note on the lesson's own diagnostic.** The lesson's Phase 4 check `grep -E 'FleetManager\.Tests\.dll.*Passed: +15,'` still prints `FAIL` — but not because of the count. In real `-v m` output the assembly name appears at the **end** of the summary line (`… Total: 15, Duration: 447 ms - FleetManager.Tests.dll (net10.0)`), never before `Passed: 15,`, so that pattern can never match any correct run. Verified directly:

```
$ grep -E 'FleetManager\.Tests\.dll.*Passed: +15,' "$T"
  → NO MATCH (pattern never matches -v m output format)
$ grep -E 'Passed: +15,.*FleetManager\.Tests\.dll' "$T"
  → Passed!  - Failed: 0, Passed: 15, … - FleetManager.Tests.dll (net10.0)   ✓
```

The count the pattern was meant to check is genuinely 15. This is lesson defect L4 below; the first pass conflated the two failures (the count was genuinely 11 then, and the pattern was already broken).

### Phase 4b — the four new pure tests are really there, by anchored name, in the right assembly

```
$ dotnet test ShuttleFleet.slnx --list-tests
    FleetManager.Tests.RunLoopGateTests.Parked_shuttle_blocks_route_through_its_lane
    FleetManager.Tests.RunLoopGateTests.Route_is_clear_after_occupied_shuttle_moves
    FleetManager.Tests.RunLoopGateTests.Gate_allows_route_when_no_occupancy_conflict
    FleetManager.Tests.RunLoopGateTests.Gate_refuses_when_transit_reservation_conflicts

$ dotnet test ShuttleFleet.slnx --filter "FullyQualifiedName~RunLoopGateTests" -v m
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 218 ms - FleetManager.Tests.dll (net10.0)
```

Phase 4b: **pass.** All four anchored names exist, all four pass, and all four execute inside `FleetManager.Tests.dll`. The fully-qualified name (`FleetManager.Tests.RunLoopGateTests`) now matches the assembly it runs in — the mismatch the first pass flagged as the visible symptom of issue 1 is gone.

### Phase 5 — no test launches docker; the gate tests are pure

```
$ grep -rnE 'Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))' src/*.Tests --include='*.cs'
  src/FleetManager.Tests/StartTests.cs:17:        using var process = Process.Start(psi)!;
  src/ShuttleSim.Tests/DemoTests.cs:18:     using var process = Process.Start(psi)!;
  → prints "FAIL: a test must not start docker"

$ grep -rn 'Trait' src/FleetManager.Tests/RunLoopGateTests.cs              → no matches (rc 1)
$ grep -rniE 'Npgsql|Postgres' src/FleetManager.Tests/RunLoopGateTests.cs → no matches (rc 1)
```

Phase 5: **substantive pass, with the same baseline caveat as the first pass.** Both `Process.Start` matches are pre-existing baseline files the owner did not touch; neither mentions docker — they launch plain `dotnet <Dll>` smoke tests. No test anywhere launches docker. The lesson's grep over-matches `Process.Start` of any kind (lesson defect L2, unchanged). The gate tests themselves are pure: no `Integration` trait, no database, no broker.

### Phase 6 — protected files unchanged, dispatcher shape intact, contract still v2

```
$ git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs docs/contract docker/Dockerfile.build
  → OK (the owner's fixes touch none of these)
$ grep -qE '^Version: 2$' docs/contract/v2.md   → contract v2 OK
$ test ! -f docs/contract/v3.md                → no v3 OK
$ for m in AssignNextAsync StartAsync CompleteAsync FailAsync; do grep -qE "${m}\s*\(" src/FleetManager/TaskDispatcher.cs; done  → all four OK
```

Phase 6: **pass.**

### M5 scan — nothing from the next milestone leaked in

```
$ grep -rniE 'lastSeen|heartbeat|timeout|alarm|reconcil' \
    src/FleetManager/OccupancyMap.cs src/FleetManager/RunLoopGate.cs src/FleetManager.Tests/RunLoopGateTests.cs
  → no matches (rc 1)
```

No `lastSeenAt`, no heartbeat, no timeout detection, no alarms, no reconciliation persistence. The occupancy map is in-memory only, exactly as required.

### Mutation check — is the occupancy test still load-bearing?

Run in `/tmp/opencode/rev11-r2` (a copy of the current working tree, deleted after). The occupancy check in `RunLoopGate.CanMove` was disabled (`if (false && Occupancy.IsOccupiedByOthers(...))`), then the four gate tests were run:

```
$ dotnet test ShuttleFleet.slnx --filter "FullyQualifiedName~RunLoopGateTests"
  Failed FleetManager.Tests.RunLoopGateTests.Parked_shuttle_blocks_route_through_its_lane [2 ms]
     at FleetManager.Tests.RunLoopGateTests.Parked_shuttle_blocks_route_through_its_lane() in …/FleetManager.Tests/RunLoopGateTests.cs:line 20
Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4 - FleetManager.Tests.dll
```

`Parked_shuttle_blocks_route_through_its_lane` goes red when the occupancy check is removed — and it now does so inside `FleetManager.Tests.dll`, at the moved file's line 20. The test proves what the lesson claims it proves, in the assembly the lesson names. (The moved file is byte-identical to the first-pass original except the namespace line, so the first pass's mutation result carries over; it was re-run here anyway against the current tree.)

---

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | `src/FleetManager/OccupancyMap.cs` with pinned members, pure | **pass** | Phase 1, Phase 2; product file unchanged since the first pass |
| 2 | `src/FleetManager/RunLoopGate.cs`, pinned ctor + `CanMove`, pure, checks occupancy then reservation | **pass** | Phase 1, Phase 2; product file unchanged since the first pass |
| 3 | Unit test at **`src/FleetManager.Tests/RunLoopGateTests.cs`**, four named pure tests | **pass** | Phase 1 file check OK; Phase 4b — all four names present, all four execute in `FleetManager.Tests.dll` |
| 4 | Project references wired (`FleetManager` 4→5, `FleetManager.Tests` 3→5) | **pass** | Phase 3 — both counts exactly 5; `FleetCore.Tests` inversion removed (one leftover ref, issue 3) |
| 5 | `TaskDispatcher` unchanged; gate called before it, never inside | **pass** | Phase 6 diff vs HEAD empty; `RunLoopGate.cs` references no dispatcher |
| 6 | Release-on-departure semantics documented | **pass** | Comment at `RunLoopGate.cs:33-34` (wording imprecision noted in first-pass issue 5) |
| 7 | Faulted/lost shuttle is an obstacle; no auto-release | **pass** | No timeout/release logic anywhere in the new code; absence is the requirement |
| 8 | No M5 items present | **pass** | M5 scan above, no matches |
| — | Build and pure suite green, no DB | **pass** | Phase 4: RC 0, 8 `Passed!`, 0 `Failed!`, 83 total; `FleetManager.Tests=15`, `FleetCore.Tests=18` (baseline) |
| — | Contract still v2 | **pass** | Phase 6 |

---

## Top issues (max 5), ranked

### 1. RESOLVED (was should-fix, blocker for closing M4) — the four gate tests are now in the right project

Fixed exactly as the first pass prescribed: the file moved to `src/FleetManager.Tests/RunLoopGateTests.cs`, the `FleetCore.Tests` copy is deleted, the namespace was corrected to `FleetManager.Tests` (matching every other file in the project), and `FleetCore.Tests.csproj` no longer references `FleetManager`. Evidence: Phase 1 file check OK; the four tests run inside `FleetManager.Tests.dll` (Phase 4b); `FleetManager.Tests.dll` = 15, `FleetCore.Tests.dll` = 18 (baseline); fully-qualified names now match their assembly. The layering inversion — a core test project depending on the fleet-manager application — is gone.

### 2. RESOLVED (was should-fix) — `FleetManager.csproj` is back to 5 `ProjectReference` lines

The redundant `..\ShuttleCore\ShuttleCore.csproj` line is removed; the count is exactly the pinned 5 and `RunLoopGate.cs` compiles unchanged (`Track` arrives transitively via `TrafficCore`, as the first pass predicted). Evidence: Phase 3.

### 3. consider — one leftover reference in `FleetCore.Tests.csproj`: `TrafficCore` is unused there

The first pass's fix instruction was "revert `FleetCore.Tests.csproj` to its baseline single `FleetCore` reference." The owner removed the two references that caused the inversion (`ShuttleCore`, `FleetManager`) but left `TrafficCore`, which the lesson-11 commit had also added. The baseline at `54a850b` had exactly one reference (`FleetCore`); the file now has two. No file under `src/FleetCore.Tests/` uses `TrafficCore` (grep over `*.cs` finds no usage), so this is unused residue, not a dependency.

Why it is only a consider: it changes nothing at build time, inverts nothing (`TrafficCore` references only `ShuttleCore`, so there is no cycle), and the lesson never checks this file. But it is exactly the kind of leftover that confuses the next person reading the test projects, and the fix is one line: delete `src/FleetCore.Tests/FleetCore.Tests.csproj:23`.

### 4. consider (carried from the first pass) — `CanMove` answers a question *and* reserves the route; the reservation can leak

Unchanged: `RunLoopGate.cs:35` calls `Traffic.TryReserveRoute`, which reserves the whole route as a side effect of a `bool`-returning query. Today nothing leaks because no production caller exists. The day the run loop is wired, gate-then-`AssignNextAsync` is a two-step sequence with a reservation held across it; if the process stops between the calls or the assignment throws, that route stays blocked for every shuttle with no alarm. When the run loop is wired, treat gate-then-dispatcher as one inseparable step (release on any failure path after a `true`), or split the gate into `CanMove` (pure check) and `CommitMove` (reserve). Decide it in the wiring lesson, not now.

### 5. consider (carried from the first pass) — `OccupancyMap` is not thread-safe, and M5 wiring will hit it

Unchanged: `OccupancyMap.cs:5` is a plain `Dictionary<string, string>`, matching the current codebase (`SegmentReservations` is also plain, lesson 10). The day the gate is wired, the MQTT status-reporting path (`SetOccupiedLane`) and the run loop (`GetOccupiedLane` / `IsOccupiedByOthers`) touch the map concurrently; concurrent dictionary access can corrupt state or throw — a lost occupancy update is exactly the lost/duplicated-pallet class of bug. Fix when wiring (a lock or `ConcurrentDictionary`), and the same treatment for `SegmentReservations`.

(The first pass's consider-5 — test 2's name overclaims slightly and two cheap cases are untested — also carries over unchanged; the test file is byte-identical except the namespace.)

---

## What is solid

- **The fix is exactly the mechanical change the first pass asked for, nothing more.** One file moved, one namespace line corrected, two `.csproj` lines removed. No product code was touched (`git diff HEAD` on `OccupancyMap.cs`, `RunLoopGate.cs`, `TaskDispatcher.cs`, `Program.cs` is empty), and the moved test's logic is untouched — the owner resisted the urge to "improve" anything else while fixing placement.
- **`OccupancyMap` remains exactly the pinned pure structure.** No clock, no I/O, no hard-coded lanes; ordinal string comparison; `route.Distinct(...)` before the occupancy scan; self-exclusion by skipping the requester; idiomatic `ArgumentException` validation.
- **`RunLoopGate` implements the pinned order faithfully:** unknown position → refuse; route from the *current* position; occupancy before transit reservation; holds nothing on refusal; never references the dispatcher (record 15a §2 holds structurally).
- **The suite is genuinely green with no database:** RC 0, 8 `Passed!`, 0 `Failed!`, 83 tests, `FleetManager.Tests=15`, `FleetCore.Tests=18` back at baseline. The mutation check re-run against the current tree proves `Parked_shuttle_blocks_route_through_its_lane` is load-bearing, now in the correct assembly.
- **No scope creep.** The M5 scan is clean; the contract is still v2; the dispatcher's four methods are all present and its file is untouched.

---

## Explain-it check

Answer these out loud or in writing before the next lesson. The professor will ask some of them. (Carried over from the first pass — the fixes did not change the decisions being questioned.)

1. **`CanMove` returns `true` only after it has already called `TryReserveRoute` — so answering "may I move?" also reserves the route. If the caller receives `true` and then the process stops before `TaskDispatcher.AssignNextAsync` runs, who releases that reservation? Is it acceptable for a query method to change state, and what would you do differently?**
2. **Why does the occupancy map live in `FleetManager` and not in `TrafficCore` next to `SegmentReservations`? Name one thing that breaks, or one awkward caller, if it moves into `TrafficCore` in a later refactor — remembering that shuttles will report position over MQTT in M5.**
3. **In `Route_is_clear_after_occupied_shuttle_moves`, two things change versus the blocking test: the blocker's lane and the destination. Walk me through exactly what that test proves. If I keep the destination fixed and only move the blocker, what does the gate do at each step — and why could you not do that with `Rack(1, 6, 1)` and destination `A01-L06`?**

**Lines and blocks to study until you can explain them cold:**

- `src/FleetManager/RunLoopGate.cs:33-34` — the comment says the destination "remains occupied … until a separate status update clears it." Occupancy is never *cleared*; it is *updated* to the shuttle's new lane when its position changes. Be ready to say precisely what clears a lane and what does not.
- `src/FleetManager/RunLoopGate.cs:8-10` — three expression-bodied properties used as fields. Why properties instead of `private readonly` fields? What would an interviewer ask about this choice?
- `src/FleetManager/OccupancyMap.cs:5` — a plain `Dictionary`. What happens when two threads call this map at the same time, and which two threads exist once M5 wires the status path?
- `src/FleetManager.Tests/RunLoopGateTests.cs:24-36` — the setup that changes two variables at once (see question 3). Note the file's namespace is now `FleetManager.Tests` — say why that line had to change when the file moved, and what would have broken if it had not.

---

## Revisit list

- **Shared-state concurrency.** Plain `Dictionary` vs `lock` vs `ConcurrentDictionary`; who writes occupancy and who reads it once MQTT status updates and the run loop run at the same time. (`SegmentReservations` will need the same answer.)
- **The gate-then-dispatcher sequence as one unit.** What "release on departure" means operationally: who calls `TrafficController.Release`, exactly when, and what happens to a reservation if the assignment after a `true` fails.
- **Test discipline: change one variable at a time.** A test's name must match what it actually proves; when the model is too small to show the story, say so in a comment rather than changing two things.
- **Why the gate is outside `TaskDispatcher`** (record 15a §2) — the boundary will be questioned again when the wiring lesson adds the real run loop.
- **Record 17 §4** — a lost shuttle is an obstacle until physically cleared; `Release` is post-recovery, never automatic. The stretch-goal test for this is the cheapest preparation for M5.
- **Project-reference hygiene (new).** A test project should reference only what its tests use. When you move a test file between projects, check the source project's `.csproj` for references that were added for the move and are no longer used — like the leftover `TrafficCore` line in `FleetCore.Tests.csproj` (issue 3).

---

## Lesson defects (for the professor, not the owner)

- **L1 — the project-reference count is under-specified (carried over).** Requirement 4 pins `FleetManager` at 5 references (gaining only `TrafficCore`), while the hints and the gate signature require `Track` from `ShuttleCore`. Since `TrafficCore` already references `ShuttleCore`, the explicit line is redundant — the owner's original 6 was defensible. Either accept 6 in the count, or tell the owner to rely on the transitive reference. (The owner now relies on the transitive reference, which is the cleaner reading.)
- **L2 — the Phase 5 grep over-matches (carried over).** `Process\.Start|docker…` fires on two pre-existing baseline files (`FleetManager.Tests/StartTests.cs:17`, `ShuttleSim.Tests/DemoTests.cs:18`) that launch plain `dotnet <Dll>` smoke tests, not docker. The criterion prints `FAIL` even on a clean baseline. Tighten the pattern to docker invocations, or exempt those two files by name.
- **L3 — nothing to fix (carried over).** The lesson's three-concept cap is respected (occupancy vs reservation; fleet-side occupancy view; run-loop gate), each taught in its own lecture section, and every item in "Concepts this assignment requires" was actually taught. The lesson's own warning — no live shuttle movement yet, the gate is proven by unit tests — is honest and should be repeated to the owner.
- **L4 (new) — the Phase 4 `FleetManager.Tests=15` diagnostic can never match real output.** The check is `grep -E 'FleetManager\.Tests\.dll.*Passed: +15,'`, but `-v m` summary lines put the assembly name at the **end**: `Passed! - Failed: 0, Passed: 15, … - FleetManager.Tests.dll (net10.0)`. A pattern requiring the dll name *before* `Passed: 15,` matches nothing, on a correct run or otherwise. The first pass ran this grep and reported FAIL alongside the (then-genuine) count of 11, hiding the fact that the pattern was already broken; this pass confirms the count is 15 and the pattern still prints FAIL. Fix: `grep -E 'Passed: +15,.*FleetManager\.Tests\.dll'`. Same class as L2 — a check that lies on a clean baseline.

---

## README

**Updated.** Verdict is pass, so the M4 row changes from `not started` to **`in progress`** — this slice is reviewed and green, but M4 is not done: the spec's done-when for M4 requires a stress scenario with four or more shuttles running with no collisions and no permanent deadlock, and the lesson explicitly defers wiring the gate into a real `Program.cs` run loop to the next lesson. A verification note for this pass was added under the table with the command that confirmed it (`dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` — RC 0, 8 `Passed!`, 0 `Failed!`, 83 tests, `FleetManager.Tests=15`). The row becomes `done` after the wiring lesson's review passes and the stress scenario has actually run.

---

## End-product check

This slice closes the parked-shuttle safety gap that lesson 10 left open, and this re-review confirms it closes it in the right place: the fleet manager holds an authoritative last-known occupancy view, the run-loop *decision* — occupancy and transit reservation both clear before dispatch — is proven pure and unit-tested inside the assembly the lesson names, with the dispatcher untouched and the contract unmoved. It fits the spec's M4 line (two shuttles never in the same space) and the AGENTS.md separation rule: the gate is a pure function of fleet state, testable without a broker or database. Two risks ride forward unchanged from the first pass: the reservation side-effect of `CanMove` (issue 4) is the one that can cause a real stuck route the day the run loop is wired, and the non-thread-safe map (issue 5) meets a concurrent status path at M5. Both should be settled in the wiring lesson's design, not discovered under load. The one new residue (issue 3, a leftover unused reference) is cosmetic and one line to clean up.
