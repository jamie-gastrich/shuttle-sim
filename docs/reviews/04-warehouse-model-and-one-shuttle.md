# Review 04: Warehouse model and one shuttle (lesson 04, M1)

**Date:** 2026-10-09
**Reviewed:** commits `df29688` (`M1: Refactor code structure…` — the professor's lesson 04,
hints, and records 05/06; docs-only, 4 files) and `64fbaa9` (`M1: Add core functionality and
tests for shuttle operations…` — the owner's implementation; src-only, 17 files), on top of
`11f82d1`. Branch `master`; `git status --porcelain` was empty before I started (exit 0).
**Scope:** lesson 04 against the M1 "done when" in the spec. M0 is already `done`.
**Contract checked:** untouched by both commits (`git diff 11f82d1..HEAD --stat -- docs/contract/
src/Contracts/` prints nothing).

**Verdict: not yet (M1).**

The rack, the state machine, the demo, and 24 of the 26 reference tests are green and genuine
— every acceptance gate I ran either passed or failed for the right reason, and the demo
really moves `PLT-001` from one slot to another. What blocks the milestone is exactly the two
tests the owner commented out. The M1 done-when says *"every state transition is covered by a
test"* — that is not a line to negotiate, and the class of fix "delete or comment out the
test" is the one class the evidence below shows is wrong here: the *design* already supports
fault-from-loading and fault-from-unloading and passes correct tests unchanged; only the two
test setups were broken, and I proved the corrected setups green in a throwaway copy. On top
of that blocker sits one R1-class finding (a named rejection test that passes for the wrong
reason) that the same exercise exposed.

The owner's handover note, quoted:

> "I commented out RaiseFault_from_loading_enters_fault and RaiseFault_from_unloading_enters_fault because they cause the exceptions that they are testing for..."

The reading is wrong, and it matters for M1's spec coverage. Full diagnosis in [Root cause of
the two commented tests](#root-cause-of-the-two-commented-tests) below.

---

## Commands run, verbatim key output

Exit codes read directly, never through a pipe, except where the lesson pipes (`| wc -l`) and
its `mktemp` capture pattern. The acceptance block was run **verbatim from the lesson**, from
the directories the block names.

### Phase 1 — four new projects, eight in the solution, ten edges, one graph

```
$ for p in ShuttleCore ShuttleCore.Tests ShuttleSim ShuttleSim.Tests; do
    test -f "src/$p/$p.csproj" || echo "missing project: $p"; done     (no output)
$ grep -c '<Project Path' src/ShuttleFleet.slnx                        prints 8
$ grep -rn 'ProjectReference' --include=*.csproj src | wc -l           prints 10
$ ! grep -q 'ProjectReference' src/ShuttleCore/ShuttleCore.csproj      exit 1 — no output (the core references nothing)
$ grep -q '<OutputType>Exe</OutputType>' src/ShuttleSim/ShuttleSim.csproj       exit 0
$ ! grep -q '<OutputType>Exe</OutputType>' src/ShuttleCore/ShuttleCore.csproj   exit 1 — no output (a class library)
$ ! grep -q 'FleetManager' src/ShuttleSim/ShuttleSim.csproj                     exit 1 — no output
```
All six `ref` edge greps satisfied (no `missing edge:` output). The md5 duplicate check over
all non-`bin`/`obj` `.cs` files prints nothing.

### Phase 2 — the fixed vocabulary lives in ShuttleCore

`enum ShuttleState` exists; all five wire names (`idle moving loading unloading fault`)
present; all seven events (`public void StartMove CompleteMove StartLoad CompleteLoad
StartUnload CompleteUnload RaiseFault`) present; `class ShuttleRejectedException`,
`record ShuttleTiming`, `class Rack`, and `class Shuttle` (anchored, not the exception) all
grep exit 0. Nothing missing.

### Phase 3 — the rules (each prints nothing)

`Thread\.Sleep|Task\.Delay` over all of `src/`: **nothing (exit 1)**. Duration literals
(`TimeSpan.FromSeconds|…|new TimeSpan(`) inside `ShuttleCore`: **nothing (exit 1)**. Network
vocabulary (`mqtt|broker|localhost|127.0.0.1|:1883|:5432|postgres|Host=`) in `src/`:
**nothing (exit 1)**. No `Class1.cs`/`UnitTest1.cs`, no `Test1`, no `Hello, World!` in
`FleetManager`, and every `[Fact]` file contains an `Assert`. I additionally grepped
`ShuttleCore` for `Sleep|Console|Stopwatch|DateTime.Now|Task.Delay|Process|Socket|HttpClient|
Task.Run` — **nothing (exit 1)**. Purity holds; nothing waits, nothing reads the wall clock.

### Phase 4 — from `src/`: build, run the suite, walk the names

```
$ dotnet build                                     exit 0, 0 Warning(s), 0 Error(s)
$ T=$(mktemp); dotnet test > "$T" 2>&1             exit 0
$ grep -c 'Passed!' "$T"                           prints 4
$ grep -c 'No test is available' "$T"              prints 0
Passed! - Failed: 0, Passed: 2,  … Contracts.Tests.dll     (net10.0)
Passed! - Failed: 0, Passed: 20, … ShuttleCore.Tests.dll   (net10.0)
Passed! - Failed: 0, Passed: 1,  … ShuttleSim.Tests.dll    (net10.0)
Passed! - Failed: 0, Passed: 1,  … FleetManager.Tests.dll  (net10.0)
```

**ShuttleCore.Tests has 20 tests; the reference has 22.** The difference is exactly the two
commented-out transitions. The suite is green but two transition rows are absent.

The name walk (anchored `\.$t\$`, exactly as the lesson writes it):

```
$ L=$(mktemp); dotnet test --list-tests > "$L" 2>&1
$ for t in … 23 names …; do grep -qE "\.$t\$" "$L" || echo "missing test: $t"; done
missing test: RaiseFault_from_loading_enters_fault
missing test: RaiseFault_from_unloading_enters_fault
```

This is the gate that catches the gap: **the anchored name walk goes red on this tree**, and
it names exactly the two commented tests. (The numeric gates — `Passed!` count 4, `≥10`
transition asserts, `≥6` throws asserts — all still pass, because they count *something*
existing. The name walk is the load-bearing gate here, as the lesson's audit promised.)

`grep -rho 'Assert.Equal(ShuttleState\.'` = **11** (≥ 10); `grep -rho
'Assert.Throws<ShuttleRejectedException>'` = **6** (≥ 6).

### Phase 5 — the demo (from `src/`)

```
$ F=$(mktemp); dotnet run --project ShuttleSim/ShuttleSim.csproj > "$F" 2>&1   exit 0
$ grep -c '"ShuttleId"' "$F"                                   prints 7
```
All four required states (`idle loading moving unloading`) appear; `00:00:17Z` and `00:00:19Z`
both present; `"Position":"A01-L01"` and `"Position":"A02-L02"` present; line 4 is
`"State":"moving"` **with** `"Position":"A01-L01"` (position does not jump early);
`"FaultReason":null` present. Full output (verbatim):

```
{"ShuttleId":"SH-01","State":"idle","Position":"A01-L01","Timestamp":"2026-01-01T00:00:00Z","FaultReason":null}
{"ShuttleId":"SH-01","State":"loading","Position":"A01-L01","Timestamp":"2026-01-01T00:00:00Z","FaultReason":null}
{"ShuttleId":"SH-01","State":"idle","Position":"A01-L01","Timestamp":"2026-01-01T00:00:02Z","FaultReason":null}
{"ShuttleId":"SH-01","State":"moving","Position":"A01-L01","Timestamp":"2026-01-01T00:00:02Z","FaultReason":null}
{"ShuttleId":"SH-01","State":"idle","Position":"A02-L02","Timestamp":"2026-01-01T00:00:17Z","FaultReason":null}
{"ShuttleId":"SH-01","State":"unloading","Position":"A02-L02","Timestamp":"2026-01-01T00:00:17Z","FaultReason":null}
{"ShuttleId":"SH-01","State":"idle","Position":"A02-L02","Timestamp":"2026-01-01T00:00:19Z","FaultReason":null}
```

The three-lane move honours `3 × 5 s` (departure report at 00:00:02, arrival at 00:00:17), the
unload lands `PLT-001` in `A02-L02-S02` at 00:00:19, and the seven-line count is exact. All
scenario-id greps (`A01-L01-S01 A01-L02-S02 PLT-001` in tests; `A01-L01-S01 A02-L02-S02 PLT-001
SH-01` in `Program.cs`), and the `JsonSerializer`/`ShuttleStatus` greps in `ShuttleSim.Tests`
— satisfied.

### Phase 6 — the contract is untouched and the fresh container still passes

All contract greps exit 0 (`Version: 1`, `## ShuttleStatus`, all five fields, the C# record
in `src/Contracts`). Contract untouched by the range.

```
$ T=$(mktemp); docker build -f docker/Dockerfile.build -t shuttle-fleet-build . >"$T" 2>&1
build exit=0 (layers CACHED — the owner's own build output was present)
$ U=$(mktemp); docker run --rm shuttle-fleet-build >"$U" 2>&1      exit 0
$ grep -c 'Passed!' "$U"                            prints 4
Passed! - … Passed: 20 … ShuttleCore.Tests.dll      (20, not the reference's 22 — same gap as Phase 4)
```

No compose services were started or needed; the only `docker` action was this build/run pair,
both `--rm`, cleaned up by the gate itself.

---

## Acceptance table

| # | Phase / criterion (lesson wording) | Evidence | Result |
|---|---|---|---|
| 1 | Phase 1: four `.csproj`s exist, loop prints nothing | printed nothing | met |
| 2 | `grep -c '<Project Path' src/ShuttleFleet.slnx` prints 8 | printed 8 | met |
| 3 | `ProjectReference` lines `\n wc -l` prints 10 | printed 10 | met |
| 4 | `ShuttleCore.csproj` references nothing | exit 1, nothing | met |
| 5 | `ShuttleSim` is `OutputType Exe` | exit 0 | met |
| 6 | `ShuttleCore` is not `Exe` | exit 1 | met |
| 7 | `ShuttleSim` never references `FleetManager` | exit 1 | met |
| 8 | all six new edges present (`ref` loop) | no `missing edge:` output | met |
| 9 | no `.cs` duplicated across projects (md5) | prints nothing | met |
| 10 | Phase 2: enum, five wire names, seven events, four types all greppable | all exit 0, no `missing` line | met |
| 11 | Phase 3: no `Thread.Sleep`/`Task.Delay` anywhere in `src/` | nothing (exit 1) | met |
| 12 | Phase 3: no duration literal / `new TimeSpan(` in `ShuttleCore` | nothing (exit 1) | met |
| 13 | Phase 3: no network vocabulary in `src/` | nothing (exit 1) | met |
| 14 | Phase 3: no placeholders / `Test1` / `Hello, World!` | nothing | met |
| 15 | Phase 3: every `[Fact]` file has an `Assert` | nothing | met |
| 16 | Phase 4: `dotnet build` exit 0 | exit 0, 0/0 | met |
| 17 | Phase 4: `dotnet test` exit 0 | exit 0 | met |
| 18 | Phase 4: `grep -c 'Passed!'` prints 4 | printed 4 | met |
| 19 | Phase 4: `grep -c 'No test is available'` prints 0 | printed 0 | met |
| 20 | Phase 4: anchored name walk — all 23 names | **2 missing**: `RaiseFault_from_loading_enters_fault`, `RaiseFault_from_unloading_enters_fault` | **not met** |
| 21 | Phase 4: `Assert.Equal(ShuttleState.` count ≥ 10 | 11 | met |
| 22 | Phase 4: `Assert.Throws<ShuttleRejectedException>` count ≥ 6 | 6 | met |
| 23 | Phase 5: demo exit 0, 7 `"ShuttleId"` lines | exit 0, printed 7 | met |
| 24 | Phase 5: `idle loading moving unloading` all shown | all present | met |
| 25 | Phase 5: `00:00:17Z` (3 lanes × 5 s) and `00:00:19Z` (final) | both present | met |
| 26 | Phase 5: `A01-L01` and `A02-L02` positions; line 4 = moving **and** `A01-L01` | all present; line 4 correct | met |
| 27 | Phase 5: `"FaultReason":null` | present | met |
| 28 | Phase 5: scenario ids in tests and demo; `JsonSerializer`/`ShuttleStatus` in test | all present | met |
| 29 | Phase 6: contract greps and unchanged contract (diff empty) | all exit 0; 0-line diff | met |
| 30 | Phase 6: `docker build` exit 0, `docker run` exit 0 | exit 0 / exit 0 | met |
| 31 | Phase 6: container `grep -c 'Passed!'` prints 4 | printed 4 | met |
| 32 | M0 regression: `dotnet build`/`dotnet test` still green | verified (rows 16–17) | met |

**31 of 32 met; the one miss is criterion 20 — the two commented transition tests.** No other
criterion failed, including the gates we distrust from reviews 02/03: the `Passed!` count is
correct, the name loop is anchored, `ShuttleCore` genuinely has no reference, and the fresh
container's run really carries the verdict (it reports 20 tests, matching the host).

---

## Root cause of the two commented tests

The owner's message says the tests "cause the exceptions that they are testing for." I
restored both tests byte-for-byte in a throwaway copy at `/tmp/opencode/rev04` (from
`git archive HEAD`) and ran them. What actually happens:

```
$ dotnet test ShuttleCore.Tests/ShuttleCore.Tests.csproj   (both tests restored verbatim)
  Failed ShuttleCore.Tests.StateTransitionTests.RaiseFault_from_loading_enters_fault
   Error Message:
    ShuttleCore.ShuttleRejectedException : slot A01-L01-S01 holds no pallet
   Stack Trace:
      at ShuttleCore.Shuttle.StartLoad(String slotId, DateTime now) in
         …/ShuttleCore/Shuttle.cs:line 63
  Failed ShuttleCore.Tests.StateTransitionTests.RaiseFault_from_unloading_enters_fault
   Error Message:
    ShuttleCore.ShuttleRejectedException : slot A01-L01-S02 is occupied
   Stack Trace:
      at ShuttleCore.Shuttle.StartUnload(String slotId, DateTime now) in
         …/ShuttleCore/Shuttle.cs:line 86
```

**The `RaiseFault` line is never reached in either test.** Both exceptions are thrown by the
test *setup*, before the transition under test runs:

1. `RaiseFault_from_loading_enters_fault` (commented at `StateTransitionTests.cs:113-123`):
   it calls `StartLoad("A01-L01-S01", …)` with **no pallet placed first**. `StartLoad`'s
   "slot holds no pallet" guard (`Shuttle.cs:63`) throws before the shuttle can enter
   `Loading`. In my restored copy the failure is at `StateTransitionTests.cs:118`, the
   `StartLoad` line.
2. `RaiseFault_from_unloading_enters_fault` (commented at `StateTransitionTests.cs:126-140`):
   it places `PLT-001` in `S01` **and** `PLT-002` in `S02`, then calls
   `StartUnload("A01-L01-S02", …)` — into the slot that still holds `PLT-002`. `StartUnload`'s
   "slot is occupied" guard (`Shuttle.cs:86`) throws before the shuttle can enter
   `Unloading`. In my restored copy the failure is at `StateTransitionTests.cs:135`, the
   `StartUnload` line.

So the working hypothesis given to me holds: **the design is fine, the test setup is wrong**.
`RaiseFault` itself (`Shuttle.cs:104-112`) rejects only an already-faulted shuttle and a blank
reason — it accepts a fault raised from `Loading` or `Unloading`, exactly as record 06's table
and the lesson require. If the *design* genuinely forbade fault-from-loading/unloading, that
would contradict records 05/06 and the lesson and would be a professor/record problem to flag —
but the design does *not* forbid it, and the same `RaiseFault` code passes both corrected
tests unchanged. Nothing in `Shuttle.cs` needs to change.

**Corrected setups (proven green in `/tmp/opencode/rev04`, then the copy deleted):**

```csharp
[Fact]
public void RaiseFault_from_loading_enters_fault()
{
    var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
    var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
    rack.Place("A01-L01-S01", "PLT-001");                     // <-- the missing setup
    var reason = "pallet not detected";

    shuttle.StartLoad("A01-L01-S01", Start);
    shuttle.RaiseFault(reason, Start);

    Assert.Equal(ShuttleState.Fault, shuttle.State);
}

[Fact]
public void RaiseFault_from_unloading_enters_fault()
{
    var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
    var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
    rack.Place("A01-L01-S01", "PLT-001");
    var reason = "slot is not empty";

    shuttle.StartLoad("A01-L01-S01", Start);
    shuttle.CompleteLoad(Start.Add(Timing.Load));
    shuttle.StartUnload("A01-L01-S01", Start.Add(Timing.Load));   // S01 is empty again now
    shuttle.RaiseFault(reason, Start.Add(Timing.Load));

    Assert.Equal(ShuttleState.Fault, shuttle.State);
}
```

The unloading test unloads back into `S01`, which is empty (the pallet is aboard the shuttle)
— so `StartUnload` passes its guards, the shuttle reaches `Unloading`, and `RaiseFault` is what
the test actually exercises. With these two bodies restored, `dotnet test` on the copy reports
**`Passed: 22, Total: 22`** for `ShuttleCore.Tests` — the reference count, every one green,
zero changes to `ShuttleCore`.

**The corrected tests are real, not vacuous** (mutation in the same copy): I added
`EnsureState(ShuttleState.Idle, "raise fault")` to `RaiseFault` — i.e. made the machine reject
fault-from-any-non-idle-state, the exact regression record 06 forbids. All three
`RaiseFault_from_*` tests (moving, loading, unloading) went red. The corrected tests genuinely
guard the fault transitions. Compared with the lesson's own claim, the fix restores coverage;
deleting the coverage is not an accepted alternative.

The same copy supplied two more non-vacuity proofs: (a) mutating `CompleteUnload` to place into
a fixed slot (`"A01-L02-S01"`) broke `Scripted_sequence_…` (`Expected: "PLT-001", Actual: null`
at `ScriptedMoveTests.cs:24`) — the scripted demo is real and can fail; (b) deleting the demo's
moving-status `Report` broke `Demo_prints_a_status_line_per_step` (`Expected: 7, Actual: 6` at
`DemoTests.cs:30`) — the demo test is real and can fail.

---

## State × event coverage table

Record 06's table. "✓" = the transition/guard cell is exercised by a test that genuinely
reaches that code; "✗ untested" = no test reaches it; "nominal/mis-targeted" marks the R1 case.

| Event | From Idle | From Moving | From Loading | From Unloading | From Fault |
|---|---|---|---|---|---|
| `StartMove` | ✓ →Moving (`StartMove_from_idle_enters_moving`) | ✗ reject (state) | ✓ reject — **state guard, real** (`StartMove_while_not_idle_is_rejected` throws "cannot start a move while loading") | ✗ reject (state) | ✓ reject (`Events_after_a_fault_are_rejected`) |
| `CompleteMove` | ✗ reject (state) | ✓ →Idle (`CompleteMove_from_moving_enters_idle`); early-arrival reject real (`CompleteMove_before_travel_time…`: 14 s < 15 s) | ✗ reject | ✗ reject | ✗ reject |
| `StartLoad` | ✓ →Loading (`StartLoad_from_idle_enters_loading`); not-in-lane reject real (`StartLoad_from_a_different_lane…`) | ✗ reject | ✗ reject | ✗ reject | ✗ reject |
| `CompleteLoad` | ✗ | ✗ | ✓ →Idle (`CompleteLoad_from_loading_enters_idle`) | ✗ | ✗ |
| `StartUnload` | ✓ →Unloading (`StartUnload_from_idle_enters_unloading`) | ✗ | ✗ reject (state) — but see R1 below | ✗ | ✗ |
| `CompleteUnload` | ✗ | ✗ | ✗ | ✓ →Idle (`CompleteUnload_from_unloading_enters_idle`) | ✗ |
| `RaiseFault` | ✓ →Fault (`RaiseFault_from_idle_enters_fault`) | ✓ →Fault (`RaiseFault_from_moving_enters_fault`) | **✗ →Fault — test commented out** | **✗ →Fault — test commented out** | ✗ "already faulted" message never asserted |

The ten transition rows of record 06: **eight covered, two not** — precisely the two
commented-out tests. That is the spec gap that drives the verdict.

Additional untested *guard* cells (beyond the lesson's six named rejections, so lower
severity, but on record): `StartLoad`'s "slot holds no pallet" (`Shuttle.cs:63`) and "already
carrying a pallet" (`Shuttle.cs:62`) guards, `StartUnload`'s "not carrying a pallet"
(`Shuttle.cs:85`) guard, and `RaiseFault`'s "already faulted" branch have no test that reaches
them. The "slot holds no pallet" and "slot is occupied" guards were, ironically, only ever hit
by the *broken* commented tests. The R1 finding below is the member of this set that has a
**named** requirement and fails to meet it.

---

## Findings

### Blocker (drives the verdict)

**1 — Spec coverage is removed, not fixed: `RaiseFault_from_loading_enters_fault` and
`RaiseFault_from_unloading_enters_fault` are commented out (`StateTransitionTests.cs:111-140`).**
M1's done-when is *"every state transition is covered by a test."* Two of record 06's ten rows
are now uncovered, the acceptance name walk prints both as missing, and the suite's
ShuttleCore.Tests count is **20 versus the reference's 22** (host and container alike). This is
the difference between M1 closing and M1 staying open. The fix is not a choice between "delete
the tests" and "keep them commented" — the diagnosis above is that the design already passes
correct tests unchanged, so the fix is to restore both tests with the corrected setups and
assert the fault transition. Details and the exact lines to change are in
[Root cause](#root-cause-of-the-two-commented-tests). Deleting the comment markers and adding
the one missing `Place` (loading) plus unloading into the now-empty source slot (unloading) is
all that is required; the `// These are commented out because…` comment block must be removed
with it.

### Should-fix

**2 — `StartUnload_into_an_occupied_slot_is_rejected` passes for the wrong reason (the review-02
R1 class, new instance).** `RejectionTests.cs:40-49` calls `StartLoad` and then `StartUnload`
without ever completing the load, so the shuttle is in **`Loading`**, and `StartUnload` throws
on the *state* guard ("cannot start unloading while loading", `Shuttle.cs:83`) before it reaches
the occupancy check at `Shuttle.cs:86`. I proved it by mutation on the copy: **delete the
occupied-slot guard from `StartUnload` and the named test still passes.** The lesson names this
test "into an occupied slot," but no test in the suite reaches that guard at all. Fix
direction: in the same test, load the pallet and *complete* the load, then target a genuinely
occupied slot in the current lane (place `PLT-002` in a second slot; e.g. the loading-from-`S01`
/ unloading-into-`S02`-still-holding-`PLT-002` shape — which is, amusingly, the shape of the
commented-out unloading test before it faults), and keep the `Assert.Throws<…>`. The owner's
own `//IDK about this one` comment on `RejectionTests.cs:41` was the right instinct — this one
needs a careful look, and it deserves one.

### Consider

**3 — `Rack`'s read surface names drift from the lesson's fixed names.** The lesson (req 2)
fixes `LaneIds` and `SlotIds`; the implementation names them `LaneId` and `SlotId`
(`Rack.cs:42-43`). No gate greps for these two, and no future consumer is wired yet, so it is
cosmetic today — but the lesson calls the names "fixed so the tests and gates mean something."
Rename to match before M7's dashboard (or get a professor record to fix the names the other
way); whichever side moves, one of them is already wrong.

**4 — `RackTests` runs weaker than record 05's own claims.** `Lane_distance_counts_lanes…`
(`RackTests.cs:28-35`) asserts two distances (1 and 3) where record 05 §5 explicitly measured
`1, 1, 3` (`A01-L01→A01-L02`, `A01-L02→A02-L01`, `A01-L01→A02-L02`). And
`Rack_builds_every_lane…` never asserts rejection of `A01-L01-S03` or `banana` — the records'
*point* of membership validation. The implementation *does* reject both (membership in
`_palletsBySlot`/`_laneOrdinals`), so this is coverage strength, not a defect — but the two
assertions are the ones the lesson's "Say it out loud" asks about, and one cheap `Assert.False`
each would turn the lesson's claims into the test suite's claims.

**5 — R2 and R4 are unchanged and still due before M2** — this time with the pattern they are
supposed to teach sitting right beside them: the new demo test `DemoTests.cs:9-21` is exactly
the bounded-wait shape review 03 asked for (`const int DemoTimeoutMs = 30_000;`,
`WaitForExit(DemoTimeoutMs)`, `Kill(entireProcessTree: true)`). `StartTests.cs` still has the
unbounded `ReadToEnd()`/`WaitForExit()` (`StartTests.cs:20-21`), the 9-space indent at
`StartTests.cs:10`, and no trailing newline. The lesson's requirement for *this* lesson — the
demo test demonstrates the R2 pattern — is met, so this stays a consider; it becomes a
should-fix when lesson 05 (M2) opens if it has not landed.

**6 — Cosmetic slips, named so they get studied:** the misleading `// These are commented out
because they cause the exceptions that they are testing for…` block (`StateTransitionTests.cs:111`),
the `//IDK about this one` (`RejectionTests.cs:41`), stray blank lines in `RackTests.cs:8` and
`RejectionTests.cs:8-9`. Match the file around you as you fix the blocker.

### AGENTS.md compliance (checked)

- **No hard-coded config where the records require it.** Judged against record 06 §4 and the
  lesson, not a blanket ban: `ShuttleCore` contains no duration literal and no `TimeSpan`
  constructor call (Phase 3, exit 1); the demo's `5 s / 2 s / 2 s`, `2 × 2 × 2`, `SH-01`,
  `PLT-001`, and the `2026-01-01` clock are the lesson's **scenario data**, which record 06 §4
  exempts by name; `DemoTimeoutMs` is a named constant. No violation.
- **Reference graph:** exactly ten lines (six new), `ShuttleCore` references nothing, the
  compiler enforces the separation. Verified Phase 1.
- **Decision logic pure:** no I/O vocabulary in `ShuttleCore` at all (grep, exit 1); time is
  passed in; nothing sleeps.
- **Message contract is the source of truth:** no divergence found between `v1.md` and the
  implemented wire output. The demo emits `ShuttleStatus` with the exact five field names,
  lowercase states, lane-format `Position`, ISO-8601-`Z` timestamps, and `FaultReason:null` —
  the same shapes M2's broker messages will carry. `State`/`Position` align; nothing to bite at
  M2 from this milestone.
- **No throwaway work / no copied files:** `md5sum` duplicate check prints nothing; the scripted
  test and demo test are real (mutation-proven above).

---

## Explain-it questions — NON-BLOCKING

**The owner waived explain-it re-answers as a gate in review 01; reviews 02 and 03 kept them
non-blocking. Nothing below blocks or lifts the `not yet` verdict — the verdict stands on the
two commented tests alone. Answer these out loud before the re-submit.**

1. Run the two commented tests in your head. What exact exception does each throw, in which
   test, and on which line of `Shuttle.cs` — and why is neither exception the transition under
   test? What does that say about the difference between a *setup* failure and a *guard* under
   test?
2. The two `RaiseFault` tests were commented "because they cause the exceptions they are
   testing for." The acceptance gate, my mutation, and the corrected tests all disagree with
   that reading. Why does `RaiseFault` at M1 accept a call from `Loading`/`Unloading`, and what
   would have to be true of the *design* for the owner's reading to be right?
3. What property of `grep -qE "\.$t\$"` makes it catch the two missing tests while
   `grep -c 'Passed!'` = 4 and the two assert counts all stay green? Which gate is load-bearing
   for "every state transition is covered by a test," and why do the numeric gates miss it?
4. `StartUnload_into_an_occupied_slot_is_rejected` is named for the occupancy guard but throws
   on the state guard. Name the line in `Shuttle.cs` where the test actually dies, and the
   added setup lines that would make it reach the guard its name promises.
5. My mutation test deleted the occupied-slot guard and the *named* rejection test still
   passed. What does that prove about the difference between "a test named X passes" and "X is
   true"? (This is review 02's R1 lesson, revisited in code.)
6. What distinguishes the demo's scenario numbers (`5 s`, `2 s`, `SH-01`) — which record 06 §4
   calls *data* — from a hard-coded timeout AGENTS.md bans, and which project would a
   `TimeSpan.FromSeconds(5)` inside `ShuttleCore` violate, and why?

### Blocks to study (unclear, fragile, or copied-looking)

- `src/ShuttleCore.Tests/StateTransitionTests.cs:111-140` — the commented block. Be able to
  say, out loud, the two exceptions it leaks, the two `Shuttle.cs` lines that throw them, and
  the corrected setup for each. Then delete the comment markers and the reason-block.
- `src/ShuttleCore.Tests/RejectionTests.cs:40-49` — the mis-targeted rejection test. Know the
  state the shuttle is in when `StartUnload` is called and which guard actually fires.
- `src/ShuttleCore/Rack.cs:42-43` — `LaneId`/`SlotId` vs the lesson's `LaneIds`/`SlotIds`
  (consider 3). This is the first time the owner's names have drifted from a *fixed* name in a
  lesson; say which side now has to move.
- `src/ShuttleCore/Shuttle.cs:34-112` — the seven events. Be able to walk the fault path from
  each of the four non-fault states and say which tests cover which row of record 06's table.
- `src/ShuttleCore/Shuttle.cs:49` and `:133-134` (and `Program.cs:27`) — three places the
  travel arithmetic appears: `CompleteMove` calls `TravelTime`, `TravelTime` multiplies
  `DistanceInLanes × MovePerLane` via `TimeSpan.FromTicks`, and the demo recomputes the same
  `ticks × ticks` product to learn when arrival happens. Say which one is the source of truth
  the lesson expects and why the demo's copy is scenario-side, not design-side.
- `src/FleetManager.Tests/StartTests.cs:10,20-21` — R4's indent, R2's unbounded wait. Say why
  cutting and pasting the demo test's timeout pattern here before M2 would close a review that
  is three reviews old.

---

## Revisit list

- **"Every state transition is covered by a test" is a list, not a vibe** (lesson 04
  "Closing M1"). The lesson's name walk is the gate that enforces it — and it went red, naming
  both missing rows. Now you have seen the done-when fail from the failing side.
- **Setup failure vs transition under test.** A test that throws in its own setup does not
  test anything; a test that *passes* for the wrong reason tests the wrong guard (R1). Both
  failure classes appeared in this milestone.
- **Fault is a sink at M1** — reachable from any non-fault state, exits nothing. The two
  restored tests are the loading/unloading entries of that rule.
- **R2/R4**, still due before the M2 lesson; the demo test is the reference pattern to copy.
- Next up: the re-submit (two tests restored + corrected, the mis-targeted rejection fixed),
  then a re-review. On the current evidence the milestone is one small change from close.

---

## End-product check

This is the milestone that made the simulator real: a rack that builds its own truth by
membership, a guarded five-state machine with time passed in, and a demo that honestly moves
one pallet from one slot to another in seven status reports. All of it carries forward —
`ShuttleCore` is the same library M2's broker shell will wrap, `ToWireName()` feeds every
status message after this, and the rack's `DistanceInLanes` is the arithmetic M4's segments
will refine. The pure-logic-away-from-I/O boundary held (compiler-enforced, verified with a
grep of the vocabulary a network would need). The residual risks are named and small: the
coverage gap (blocker), one mis-targeted rejection test (should-fix), and a naming drift on
the rack's read surface (consider). **M1 does not close in this review** — the done-when needs
*every* transition covered, and two are not. Every other acceptance line is green and
mutation-tested, so this is a test-file fix away from closing, not a rebuild.

---

## README

**Not changed.** M0 stays `done`; the M1 row stays `not started`. The flip conditions per
AGENTS.md (spec done-when genuinely met, review passes) are not met — two of the ten
transition rows are uncovered, and the acceptance name walk prints them as missing. When the
re-submit passes, the verification note should record: from `src/`, `dotnet build` exit 0 /
`dotnet test` exit 0 with four `Passed!` lines and **22** in `ShuttleCore.Tests`; the Phase 4
anchored name walk printing nothing (all 23 names); demo exit 0 with 7 status lines and
`00:00:17Z`/`00:00:19Z`; fresh container `docker build`/`docker run` exit 0 with four
`Passed!` lines.

---

## What I changed in this repository

- **Added:** `docs/reviews/04-warehouse-model-and-one-shuttle.md` (this file). Uncommitted — I
  commit only when asked.
- **README.md:** not touched (reason above).
- **`src/`, `docker/`, `docs/lessons/`, `docs/decisions/`:** not touched by me. All
  mutations ran against `/tmp/opencode/rev04` (a `git archive HEAD` copy), which I deleted.
  The corrected-test fixes in the copy were then deleted with it — no copy survives, nothing
  but this review was written in the repo.
- **Cleanup:** no `docker compose` service was started (the acceptance's Phase 6 used only
  `docker build` / `docker run --rm`, which cleans its own container; the `shuttle-fleet-build`
  image tag predates this session — its layers built `CACHED`). `docker ps -a` shows only the
  pre-existing 10-day-old `hello-world` container, left as found. MSBuild build nodes shut down
  and process count back to the pre-session VS Code devkit processes only. `/tmp/opencode`
  empty. `git status --porcelain` at the end of the review: only `docs/reviews/04-warehouse-model-and-one-shuttle.md`.

---

## What the owner must do next

1. **Restore the two fault transition tests** with the corrected setups (place `PLT-001`
   before `StartLoad`; unload into the now-empty source slot before `RaiseFault`), delete the
   `// These are commented out…` reason-block, and re-run `dotnet test` — expect **22, Total:
   22** in `ShuttleCore.Tests` and a silent Phase 4 name walk.
2. **Fix `StartUnload_into_an_occupied_slot_is_rejected`** so it actually reaches the occupied-
   slot guard: complete the load first, then unload into a genuinely occupied slot.
3. Answer the explain-it questions out loud (non-blocking), and let the professor see this
   review so the next lesson can note the `LaneIds`/`SlotIds` naming decision.
4. Re-submit; I will re-run the full block. Nothing in `ShuttleCore` needs to change.

---

# Re-review — 2026-10-09: the fixes, re-verified; M1 stays open

**Appended** on 2026-10-09. The `not yet` verdict and every line above this one stand as written;
nothing above was edited. This section records the owner's fixes at `673bede` and the outcome.

**Re-verified at:** commit `673bede` (`M1: Restore fault transition tests with corrected setups
and fix rejection test for occupied slot`), branch `master`, `git status --porcelain` empty
(exit 0) before I started. `git diff 64fbaa9..673bede --stat` moves exactly three files — the two
test files and this review file (the owner committed it). **`git diff 64fbaa9..673bede -- src/ShuttleCore/
src/ShuttleSim/ src/Contracts/` is empty**: no library or demo code changed, so every mutation
proof written above (the scripted test and demo test are real, `RaiseFault` accepts non-idle
states) still stands unchanged.

**Verdict change: none. `not yet` (M1) stands.**

The prior blocker is **not** cleared and the prior should-fix is **not** fixed — each is a
one-line/two-line test problem, and each is now measured. What is new and good: the anchored name
walk is silent (all 23 names present) and the loading-half of the fault blocker is genuinely
repaired and mutation-proven. What keeps M1 open: the other restored test does not run — it dies
on the clock guard before it reaches its transition — so the suite is **red** (`dotnet test`
exit 1) and `ShuttleCore.Tests` is **21 passed / 22 total**, host and container alike.

- **Blocker (prior #1, still open, now smaller):** `RaiseFault_from_unloading_enters_fault`
  restored but with the wrong timestamp — `RaiseFault(reason, Start)` where the previous event is
  at `Start + Timing.Load`. It throws "clock went backwards" at `Shuttle.cs:117`; the `Fault`
  assertion is never reached. One token fixes it (`Start.Add(Timing.Load)`), and I proved the
  design passes with only that change.
- **Should-fix (prior #2, still open, moved to a different wrong guard):** the occupied-slot test
  now completes the load (good) but unloads into `A01-L01-S01`, the slot the pallet was just
  taken from — **empty** — and at a backwards timestamp. It passes on the *clock* guard; with the
  occupancy guard deleted it still passes; with a valid timestamp it fails ("no exception"), so it
  never tested occupancy at all.
- **Name walk:** silent — criterion 20 of the review above flips to met. All 23 names exist; one
  of the 22 ShuttleCore tests fails.
- New acceptance table below: **27 of 32 met**, five failures all traceable to the two findings.

## Commands re-run, verbatim key output

Run against **this committed tree**, from the directories the block names, exit codes read
directly (the block's `mktemp` capture pattern where output is captured). The block is the one at
lesson 04 lines 586–743.

### Phases 1–3 — all clean, no regression

Phase 1: four project files present (loop silent); `grep -c '<Project Path'` prints **8**;
`ProjectReference … | wc -l` prints **10**; `ShuttleCore.csproj` references nothing (exit 1);
`ShuttleSim` is `Exe` (exit 0); `ShuttleCore` is not (exit 1); `ShuttleSim` never names
`FleetManager` (exit 1); all six `ref` edges present (loop silent); the md5 duplicate check prints
nothing. Phase 2: enum, five wire names, seven events, and the four types all grep exit 0, no
`missing` line. Phase 3: the sleep grep and the `ShuttleCore` duration-literal grep both print
nothing (exit 1); the network-vocabulary grep prints nothing (exit 1); no `Class1.cs`/`UnitTest1.cs`;
`Test1` exit 1; the scoped `Hello, World!` grep exit 1; every `[Fact]` file contains an `Assert`.

### Phase 4 — build green, suite RED

```
$ dotnet build                                      exit 0, 0 Warning(s), 0 Error(s)
$ T=$(mktemp); dotnet test > "$T" 2>&1              exit 1   <-- was exit 0 in the reference
$ grep -c 'Passed!' "$T"                            prints 3  <-- reference: 4
$ grep -c 'No test is available' "$T"               prints 0

  Failed ShuttleCore.Tests.StateTransitionTests.RaiseFault_from_unloading_enters_fault
   Error Message:
    ShuttleCore.ShuttleRejectedException : clock went backwards:
      2026-01-01T00:00:00.0000000Z < 2026-01-01T00:00:02.0000000Z
   Stack Trace:
      at ShuttleCore.Shuttle.EnsureInOrder(DateTime now) in …/ShuttleCore/Shuttle.cs:line 117
      at ShuttleCore.Shuttle.RaiseFault(String reason, DateTime now) in …/ShuttleCore/Shuttle.cs:line 106
      at ShuttleCore.Tests.StateTransitionTests.RaiseFault_from_unloading_enters_fault()
         in …/ShuttleCore.Tests/StateTransitionTests.cs:line 136

Failed! - Failed:     1, Passed:    21, … ShuttleCore.Tests.dll (net10.0)
Passed! - Failed:     0, Passed:     2, … Contracts.Tests.dll   (net10.0)
Passed! - Failed:     0, Passed:     1, … ShuttleSim.Tests.dll  (net10.0)
Passed! - Failed:     0, Passed:     1, … FleetManager.Tests.dll (net10.0)
```

The anchored name walk (`grep -qE "\.$t\$"`, all 23 names) now **prints nothing** — the prior
review's load-bearing gate is satisfied. `Assert.Equal(ShuttleState.` count = **11** (≥10);
`Assert.Throws<ShuttleRejectedException>` count = **6** (≥6). Those two numeric gates are green
even though the suite is red: they count text, not green tests — the same lesson as before, one
layer down.

### Phase 5 — the demo is still exactly right

`dotnet run --project ShuttleSim/ShuttleSim.csproj` exit 0; `grep -c '"ShuttleId"'` prints **7**;
all four states present; `2026-01-01T00:00:17Z` and `…19Z` present; both positions present; line 4
is `"State":"moving"` with `"Position":"A01-L01"`; `"FaultReason":null` present; all scenario-id,
`JsonSerializer`, and `ShuttleStatus` greps satisfied. (Output byte-identical to the review above.)

### Phase 6 — contract untouched; fresh container RED for the same reason

All contract greps exit 0; `git diff 11f82d1..HEAD -- docs/contract/ src/Contracts/` is empty.

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build .   exit 0
$ U=$(mktemp); docker run --rm shuttle-fleet-build > "$U" 2>&1       exit 1   <-- reference: 0
$ grep -c 'Passed!' "$U"                                             prints 3 <-- reference: 4
  Failed ShuttleCore.Tests.StateTransitionTests.RaiseFault_from_unloading_enters_fault
Failed! - Failed:     1, Passed:    21, … ShuttleCore.Tests.dll (net10.0)
```

No compose service was needed or started; the only `docker` actions were this `build` and a
`--rm` `run`.

## Revised acceptance table (re-review)

Same 32 criteria as the first review, re-judged on this tree.

| # | Phase / criterion (lesson wording) | Evidence | Result |
|---|---|---|---|
| 1 | Phase 1: four `.csproj`s exist, loop prints nothing | printed nothing | met |
| 2 | `grep -c '<Project Path'` prints 8 | printed 8 | met |
| 3 | `ProjectReference … \| wc -l` prints 10 | printed 10 | met |
| 4 | `ShuttleCore.csproj` references nothing | exit 1 | met |
| 5 | `ShuttleSim` is `OutputType Exe` | exit 0 | met |
| 6 | `ShuttleCore` is not `Exe` | exit 1 | met |
| 7 | `ShuttleSim` never references `FleetManager` | exit 1 | met |
| 8 | all six new edges present | no `missing edge:` | met |
| 9 | no `.cs` duplicated (md5) | prints nothing | met |
| 10 | Phase 2: enum, wire names, events, types greppable | all exit 0 | met |
| 11 | Phase 3: no `Thread.Sleep`/`Task.Delay` in `src/` | nothing (exit 1) | met |
| 12 | Phase 3: no duration literal in `ShuttleCore` | nothing (exit 1) | met |
| 13 | Phase 3: no network vocabulary in `src/` | nothing (exit 1) | met |
| 14 | Phase 3: no placeholders / `Test1` / template string | nothing | met |
| 15 | Phase 3: every `[Fact]` file has an `Assert` | nothing | met |
| 16 | Phase 4: `dotnet build` exit 0 | exit 0, 0/0 | met |
| 17 | Phase 4: `dotnet test` exit 0 | **exit 1** — one failure | **not met** |
| 18 | Phase 4: `grep -c 'Passed!'` prints 4 | **prints 3** | **not met** |
| 19 | Phase 4: `grep -c 'No test is available'` prints 0 | printed 0 | met |
| 20 | Phase 4: anchored name walk — all 23 names | **prints nothing (was 2 missing)** | **met** |
| 21 | Phase 4: `Assert.Equal(ShuttleState.` ≥ 10 | 11 | met |
| 22 | Phase 4: `Assert.Throws<…RejectedException>` ≥ 6 | 6 | met |
| 23 | Phase 5: demo exit 0, 7 `"ShuttleId"` lines | exit 0, 7 | met |
| 24 | Phase 5: `idle loading moving unloading` all shown | all present | met |
| 25 | Phase 5: `00:00:17Z` and `00:00:19Z` | both present | met |
| 26 | Phase 5: positions; line 4 = moving **and** `A01-L01` | correct | met |
| 27 | Phase 5: `"FaultReason":null` | present | met |
| 28 | Phase 5: scenario ids; `JsonSerializer`/`ShuttleStatus` | all present | met |
| 29 | Phase 6: contract greps and unchanged contract | all exit 0; empty diff | met |
| 30 | Phase 6: `docker build` exit 0, `docker run` exit 0 | build 0, **run 1** | **not met** |
| 31 | Phase 6: container `grep -c 'Passed!'` prints 4 | **prints 3** | **not met** |
| 32 | M0 regression: `dotnet build`/`dotnet test` still green | build 0, **test 1** | **not met** |

**27 of 32 met; criteria 17, 18, 30, 31, 32 fail — all one root cause, the failing
`RaiseFault_from_unloading_enters_fault` test.** Criterion 20 (the gate that failed last time)
now passes. The five misses are not five problems.

## Mutation evidence — the restored tests, and the occupied-slot fix

All against a throwaway copy at `/tmp/opencode/rereview04` (`git archive HEAD`, deleted after);
`src/` was never edited. Baseline on the copy: `Failed: 1, Passed: 21, Total: 22` — identical to
the committed tree.

### The two restored fault tests: one is real, one never runs

**Mutation A — make `RaiseFault` reject every non-`Idle` state** (inserted
`EnsureState(ShuttleState.Idle, "raise fault")` after `EnsureInOrder`; the exact regression record
06 forbids):

```
Failed: 3, Passed: 19, Total: 22
  Failed … RaiseFault_from_moving_enters_fault
  Failed … RaiseFault_from_loading_enters_fault     <-- the restored loading test is real
  Failed … RaiseFault_from_unloading_enters_fault   <-- already red before the mutation
```

The restored **loading** test is genuine: it passes on the honest tree and goes red when the guard
is broken. The restored **unloading** test cannot say anything about the guard because it fails
first, on the clock. To separate the two causes I changed *only* its last line in the copy —
`RaiseFault(reason, Start)` → `RaiseFault(reason, Start.Add(Timing.Load))`, the professor's
corrected setup:

```
$ dotnet test ShuttleCore.Tests/…        (timestamp-only change)     exit 0
Passed! - Failed: 0, Passed: 22, Skipped: 0, Total: 22 - ShuttleCore.Tests.dll
```

With only that one token fixed the copy is the reference's **22/22**, and re-applying Mutation A
turns the unloading test red too (`Failed: 3, Passed: 19`). So the design is fine — every
transition works — and the blocker is the single argument `Start` where `Start.Add(Timing.Load)`
belongs.

### The occupied-slot should-fix is not fixed — two independent measurements

`RejectionTests.cs:41-50` now completes the load (state is `Idle`, not `Loading`), so the old
"dies on the state guard" description no longer applies; but it still does not reach the
occupancy guard, for two reasons.

1. **It dies on the clock guard.** The final call is `StartUnload("A01-L01-S01", Start.AddSeconds(1))`
   — one second *before* `CompleteLoad`'s `Start.Add(Timing.Load)` (two seconds). I captured the
   thrown message by asserting on it in the copy:
   ```
   Assert.Contains() Failure: Sub-string not found
   String:    "clock went backwards: 2026-01-01T00:00:01"···
   Not found: "occupied"
   ```
   **Guard that fires: `EnsureInOrder` (`Shuttle.cs:116-117`), not the occupancy check.**
2. **Deleting the occupancy guard does not make the named test red.** With
   `if (_rack.IsOccupied(slotId)) throw …` (`Shuttle.cs:86`) removed from the copy, the named test
   still passes (`Passed: 1`). That is the R1 pattern review 02 named, still live.
3. **The target slot is empty, so the guard could never fire anyway.** The test unloads into
   `A01-L01-S01` — the very slot `CompleteLoad` just took `PLT-001` out of. With the real guard
   present and a *valid* timestamp, the test fails because nothing throws:
   ```
   Assert.Throws() Failure: No exception was thrown
   Expected: typeof(ShuttleCore.ShuttleRejectedException)
   ```
   The test passes today only because a different guard (the clock) throws first.

**Proven correct shape** (in the copy): place `PLT-002` in `A01-L01-S02`, load from `S01`,
complete the load, then `StartUnload("A01-L01-S02", Start.Add(Timing.Load))` — the target is
genuinely occupied. With the real guard present the named test passes (`Passed: 1`); delete the
occupancy guard and it goes red (`No exception was thrown`). That is a test of the rule its name
promises.

## State × event coverage

| Event | From Idle | From Moving | From Loading | From Unloading | From Fault |
|---|---|---|---|---|---|
| `StartMove` | ✓ | ✗ | ✓ reject | ✗ | ✓ reject |
| `CompleteMove` | ✗ | ✓ | ✗ | ✗ | ✗ |
| `StartLoad` | ✓ | ✗ | ✗ | ✗ | ✗ |
| `CompleteLoad` | ✗ | ✗ | ✓ | ✗ | ✗ |
| `StartUnload` | ✓ | ✗ | ✗ reject (state) | ✗ | ✗ |
| `CompleteUnload` | ✗ | ✗ | ✗ | ✓ | ✗ |
| `RaiseFault` | ✓ | ✓ | **✓ real (mutation-proven)** | **✗ fails on the clock guard — transition not reached** | ✗ |

Of record 06's ten transition rows, **nine are genuinely covered** (up from eight; the loading
fault row is fixed and real) and **one is not** — `RaiseFault` from `Unloading`. The name exists
and the anchored walk sees it, but the test errors before its assertion, so the edge is not
exercised. This is exactly the "a test named X passes" vs "X is true" distinction, now demonstrated
inside a single test: the name walk says all 23 are present while `dotnet test` says one is red.

## Findings (re-review)

### Blocker

**B1 — `RaiseFault_from_unloading_enters_fault` throws in `EnsureInOrder` before `RaiseFault`,
so the suite is red and M1's "every state transition is covered by a test" is not met
(`StateTransitionTests.cs:136`).** Fix: change the argument from `Start` to
`Start.Add(Timing.Load)` (the value used by `StartUnload` one line above). Verified: with only
that change the copy is `Passed: 22, Total: 22`; Mutation A then turns it red. No `ShuttleCore`
change is needed. This is the second consecutive review of the same file; the remaining defect is
a single argument, but the milestone cannot close while `dotnet test` exits 1.

### Should-fix

**S1 — `StartUnload_into_an_occupied_slot_is_rejected` still does not reach the occupancy guard
(R1, review-02 class, third appearance).** It now dies on the clock guard (`Start.AddSeconds(1)` is
before the load's timestamp) and targets `A01-L01-S01`, which is empty after the load. Measured:
deleting the occupancy guard leaves the named test green; a valid timestamp with the guard present
fails with "no exception". Fix: place a second pallet in a *different* slot in the lane
(`rack.Place("A01-L01-S02", "PLT-002")`) and unload into that, at a non-decreasing timestamp. The
corrected shape is proven above: green with the guard, red without it.

### Consider (carried, unchanged, non-blocking)

- **C1 — `Rack.LaneId`/`SlotId` vs the lesson's fixed `LaneIds`/`SlotIds`** (prior consider 3).
  Unchanged; no consumer wired yet.
- **C2 — `RackTests` weaker than record 05's claims** (prior consider 4): the `1, 1, 3` distance
  triple is asserted as `1, 3`, and membership rejection of `A01-L01-S03`/`banana` is not
  asserted. Unchanged, no regression.
- **C3 — R2 and R4 remain due before M2, unchanged.** `StartTests.cs` was not touched by this
  commit (`git diff 11f82d1..HEAD -- src/FleetManager.Tests/StartTests.cs` empty): still an
  unbounded `p.WaitForExit()` (no timeout, R2), the 9-space indent at line 10, and no trailing
  newline (R4). Not this lesson's deliverable; still promised before lesson 05.
- **C4 — cosmetic slips still present, named in the first review:** the `//IDK about this one`
  on `RejectionTests.cs:41` and the blank line at `RejectionTests.cs:9`.

### AGENTS.md compliance (re-checked)

- **Decision logic pure and configuration respected:** Phases 1–3 all clean; `ShuttleCore`
  unchanged by this commit, no `Sleep`, no duration literal, no network vocabulary, references
  nothing.
- **Message contract untouched:** Phase 6 greps exit 0, zero-line contract diff.
- **No throwaway/copied work:** md5 duplicate check silent; `git diff` shows no `src/` change, so
  the prior mutation proofs that the scripted and demo tests are real still hold.
- **Tests pass before a review can pass:** **not satisfied** — `dotnet test` exit 1, container
  exit 1. This alone withholds the verdict.

## README

**Not changed.** M0 stays `done`; the M1 row stays `not started`. The flip conditions per
AGENTS.md are not met: `dotnet test` exits 1, `ShuttleCore.Tests` is 21/22, and the container run
exits 1. When the re-submit passes, the verification note should record (from `src/`):
`dotnet build` exit 0; `dotnet test` exit 0 with four `Passed!` lines and **22** in
`ShuttleCore.Tests`; the Phase 4 anchored name walk printing nothing (all 23 names); the scripted
test leaving `PLT-001` in `A01-L02-S02`; demo exit 0 with 7 status lines and
`00:00:17Z`/`00:00:19Z`; fresh container `docker build`/`docker run` exit 0 with four `Passed!`
lines.

## Cleanup

- No `docker compose` service was started (none needed); the only `docker` actions were
  `docker build` and a `--rm` `docker run`, which cleans its own container. `docker ps -a` shows
  only the pre-existing 10-day-old `hello-world` container, left as found.
- `/tmp/opencode/rereview04` (the `git archive` copy used for every mutation) deleted;
  `/tmp/opencode` is empty.
- MSBuild worker nodes left by this session's builds were killed after `dotnet build-server
  shutdown`; no `dotnet test`, `dotnet run`, test host, or MSBuild node remains.
- `git status --porcelain` after this review: only this file (README untouched).

## What is next

1. **Fix `RaiseFault_from_unloading_enters_fault`:** `RaiseFault(reason, Start.Add(Timing.Load))`.
   One argument. Then `dotnet test` will report four `Passed!` lines and 22/22 in
   `ShuttleCore.Tests`.
2. **Fix `StartUnload_into_an_occupied_slot_is_rejected`:** load and complete the load, place a
   second pallet in a different slot of the same lane, unload into *that* slot at a non-decreasing
   timestamp, keep the `Assert.Throws`. This is the third time the "passes for the wrong reason"
   pattern has surfaced; the corrected shape is proven above.
3. **M1 does not close in this re-review.** Criterion 20 (the prior blocker's gate) is now met and
   the loading fault test is genuinely repaired — real progress — but `dotnet test` is red and one
   transition is still uncovered.
4. **R2/R4** (C3) remain due before the M2 lesson; the reference pattern is `DemoTests.cs`.
5. Re-submit; I will re-run the full block, the anchored name walk, and both mutations.

---

# Re-review (second) — 2026-10-09: both fixes verified, M1 closes

**Appended** on 2026-10-09. Every line above this one stands as written; nothing above was edited.
This is the second re-review of lesson 04 / M1. It records the owner's fix at `cb895a5` and the
outcome.

**Re-verified at:** commit `cb895a5` (`M1: Fix rejection test for unloading into occupied slot and
update fault handling in state transition tests`), on top of `673bede`, branch `master`,
`git status --porcelain` empty (exit 0) before I started. The range moves exactly three files —

```
$ git diff 673bede..cb895a5 --stat
 docs/reviews/04-warehouse-model-and-one-shuttle.md | 322 ++++++++++++++-
 src/ShuttleCore.Tests/RejectionTests.cs            |   5 +-
 src/ShuttleCore.Tests/StateTransitionTests.cs      |   2 +-
```

and **the design code did not move**: `git diff 673bede..cb895a5 -- src/ShuttleCore` prints
nothing (0 lines), so every mutation proof written in the two sections above still stands. The two
source edits are exactly the fixes the last re-review asked for:

- `StateTransitionTests.cs:136` — `RaiseFault(reason, Start)` → `RaiseFault(reason, Start.Add(Timing.Load))`.
- `RejectionTests.cs` — the `//IDK about this one` comment and the trailing blank line are gone,
  `rack.Place("A01-L01-S02", "PLT-002")` is added, and the final call becomes
  `StartUnload("A01-L01-S02", Start.Add(Timing.Load))` — a genuinely occupied slot at a legal
  (non-decreasing) timestamp.

**Verdict change: `not yet` → `pass`. M1 closes.** Both prior findings are cleared and
mutation-proven, the suite is green host and container, and record 06's state×event table is
covered 10/10.

## Commands re-run, verbatim key output

Run against this committed tree, from the directories the block names, exit codes read directly
(the block's `mktemp` capture pattern where output is captured). The block is lesson 04 lines
586–743.

### Phases 1–3 — clean, no regression

Phase 1: four project files present (loop silent); `grep -c '<Project Path'` prints **8**;
`ProjectReference … | wc -l` prints **10**; `ShuttleCore.csproj` references nothing (exit 1);
`ShuttleSim` is `Exe` (exit 0); `ShuttleCore` is not (exit 1); `ShuttleSim` never names
`FleetManager` (exit 1); all six `ref` edges present (loop silent); the md5 duplicate check prints
nothing. Phase 2: enum, five wire names, seven events, and the four types all grep exit 0, no
`missing` line. Phase 3: the sleep grep and the `ShuttleCore` duration-literal grep both print
nothing (exit 1); the network-vocabulary grep prints nothing (exit 1); no `Class1.cs`/`UnitTest1.cs`;
`Test1` exit 1; the scoped `Hello, World!` grep exit 1; every `[Fact]` file contains an `Assert`.
The extra purity grep over `ShuttleCore` (`Sleep|Console|Stopwatch|DateTime.Now|Task.Delay|Process|
Socket|HttpClient|Task.Run`) prints nothing (exit 1).

### Phase 4 — build green, suite GREEN

```
$ dotnet build                                      exit 0, 0 Warning(s), 0 Error(s)
$ T=$(mktemp); dotnet test > "$T" 2>&1              exit 0   <-- reference: 0
$ grep -c 'Passed!' "$T"                            prints 4  <-- reference: 4
$ grep -c 'No test is available' "$T"               prints 0

Passed! - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll (net10.0)
Passed! - Failed: 0, Passed:  2, … Contracts.Tests.dll   (net10.0)
Passed! - Failed: 0, Passed:  1, … ShuttleSim.Tests.dll  (net10.0)
Passed! - Failed: 0, Passed:  1, … FleetManager.Tests.dll (net10.0)
```

The anchored name walk (`grep -qE "\.$t\$"`, all 23 names) **prints nothing** — all 23 present.
`Assert.Equal(ShuttleState.` count = **11** (≥10); `Assert.Throws<ShuttleRejectedException>` count
= **6** (≥6).

### Phase 5 — the demo is unchanged and exactly right

`dotnet run --project ShuttleSim/ShuttleSim.csproj` exit 0; `grep -c '"ShuttleId"'` prints **7**;
all four states present; `2026-01-01T00:00:17Z` and `…19Z` present; both positions present; line 4
is `"State":"moving"` with `"Position":"A01-L01"`; `"FaultReason":null` present; all scenario-id,
`JsonSerializer`, and `ShuttleStatus` greps satisfied. Output byte-identical to lines 118–124 above.

### Phase 6 — contract untouched; fresh container GREEN

All contract greps exit 0; `git diff 11f82d1..HEAD -- docs/contract/ src/Contracts/` is empty.

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build .   exit 0
$ U=$(mktemp); docker run --rm shuttle-fleet-build > "$U" 2>&1       exit 0   <-- reference: 0
$ grep -c 'Passed!' "$U"                                             prints 4
Passed! - … Passed: 22 … ShuttleCore.Tests.dll (net10.0)
```

No compose service was needed or started; the only `docker` actions were this `build` and a `--rm`
`run`.

## Acceptance table (second re-review)

Same 32 criteria as the first review, re-judged on this tree.

| # | Phase / criterion (lesson wording) | Evidence | Result |
|---|---|---|---|
| 1 | Phase 1: four `.csproj`s exist, loop prints nothing | printed nothing | met |
| 2 | `grep -c '<Project Path'` prints 8 | printed 8 | met |
| 3 | `ProjectReference … \| wc -l` prints 10 | printed 10 | met |
| 4 | `ShuttleCore.csproj` references nothing | exit 1 | met |
| 5 | `ShuttleSim` is `OutputType Exe` | exit 0 | met |
| 6 | `ShuttleCore` is not `Exe` | exit 1 | met |
| 7 | `ShuttleSim` never references `FleetManager` | exit 1 | met |
| 8 | all six new edges present | no `missing edge:` | met |
| 9 | no `.cs` duplicated (md5) | prints nothing | met |
| 10 | Phase 2: enum, wire names, events, types greppable | all exit 0 | met |
| 11 | Phase 3: no `Thread.Sleep`/`Task.Delay` in `src/` | nothing (exit 1) | met |
| 12 | Phase 3: no duration literal in `ShuttleCore` | nothing (exit 1) | met |
| 13 | Phase 3: no network vocabulary in `src/` | nothing (exit 1) | met |
| 14 | Phase 3: no placeholders / `Test1` / template string | nothing | met |
| 15 | Phase 3: every `[Fact]` file has an `Assert` | nothing | met |
| 16 | Phase 4: `dotnet build` exit 0 | exit 0, 0/0 | met |
| 17 | Phase 4: `dotnet test` exit 0 | **exit 0** (was 1) | **met** |
| 18 | Phase 4: `grep -c 'Passed!'` prints 4 | **prints 4** (was 3) | **met** |
| 19 | Phase 4: `grep -c 'No test is available'` prints 0 | printed 0 | met |
| 20 | Phase 4: anchored name walk — all 23 names | prints nothing | met |
| 21 | Phase 4: `Assert.Equal(ShuttleState.` ≥ 10 | 11 | met |
| 22 | Phase 4: `Assert.Throws<…RejectedException>` ≥ 6 | 6 | met |
| 23 | Phase 5: demo exit 0, 7 `"ShuttleId"` lines | exit 0, 7 | met |
| 24 | Phase 5: `idle loading moving unloading` all shown | all present | met |
| 25 | Phase 5: `00:00:17Z` and `00:00:19Z` | both present | met |
| 26 | Phase 5: positions; line 4 = moving **and** `A01-L01` | correct | met |
| 27 | Phase 5: `"FaultReason":null` | present | met |
| 28 | Phase 5: scenario ids; `JsonSerializer`/`ShuttleStatus` | all present | met |
| 29 | Phase 6: contract greps and unchanged contract | all exit 0; empty diff | met |
| 30 | Phase 6: `docker build` exit 0, `docker run` exit 0 | build 0, **run 0** (was 1) | **met** |
| 31 | Phase 6: container `grep -c 'Passed!'` prints 4 | **prints 4** (was 3) | **met** |
| 32 | M0 regression: `dotnet build`/`dotnet test` still green | build 0, test 0 | met |

**32 of 32 met.** The five criteria that failed last time (17, 18, 30, 31, 32) were one root cause
— the unloading fault test — and that test now passes. Criterion 20 stays met.

## Mutation evidence — both prior findings fixed and non-vacuous

All against a throwaway copy at `/tmp/opencode/mut04` (`git archive HEAD`, deleted after); `src/`
was never edited in this repo. Baseline on the copy: `Passed: 22, Total: 22` for
`ShuttleCore.Tests`.

### Fix 1 — `RaiseFault_from_unloading_enters_fault` now reaches `RaiseFault` (was the red test)

**Mutation A — make `RaiseFault` reject every non-`Idle` state** (insert `EnsureState(ShuttleState.Idle,
"raise fault")` after `EnsureInOrder`; the exact regression record 06 forbids):

```
Failed: 3, Passed: 19, Total: 22
  Failed … RaiseFault_from_moving_enters_fault
  Failed … RaiseFault_from_loading_enters_fault
  Failed … RaiseFault_from_unloading_enters_fault   <-- the previously-red test now goes red
```

Baseline green for that test on the honest tree plus red under Mutation A means it genuinely
reaches `RaiseFault` while the machine is **`Unloading`** and genuinely asserts
`ShuttleState.Fault`. The one token the last re-review asked for (`Start` →
`Start.Add(Timing.Load)`) is present and the clock guard no longer fires first.

### Fix 2 — `StartUnload_into_an_occupied_slot_is_rejected` now exercises the occupancy guard

The test targets `A01-L01-S02`, which holds `PLT-002` (placed at `RejectionTests.cs:46`), after a
completed load of `PLT-001` from `S01`, at `Start.Add(Timing.Load)` — equal to the previous event's
timestamp, so `EnsureInOrder` passes. Guard order in `StartUnload`: in-order → state (`Idle`, from
`CompleteLoad`) → slot-in-lane → carrying-a-pallet → **occupancy (`Shuttle.cs:86`)**.

1. **Removing the occupancy guard makes the named test go red** — not merely still-passing:
   ```
   Failed ShuttleCore.Tests.RejectionTests.StartUnload_into_an_occupied_slot_is_rejected
    Error Message:
     Assert.Throws() Failure: No exception was thrown
   Failed! - Failed: 1, Passed: 21, Total: 22
   ```
   Only that one test changes; every other test stays green. This is the opposite of the last two
   re-reviews, where deleting the guard left the named test green.
2. **Which guard fires, captured directly** (guard restored, test tweaked in the copy to rethrow the
   caught message): `MESSAGE-CAUGHT: slot A01-L01-S02 is occupied`. That is the occupancy check at
   `Shuttle.cs:86`, not the clock guard at `:117` and not the state guard at `:83`. The target slot
   is genuinely occupied and the timestamp order is legal.

Both fixes are the "a test named X passes" vs "X is true" distinction resolved in favour of the
latter.

## State × event coverage — 10/10

Record 06's ten rows, each matched to a passing test; the two rows that were uncovered two reviews
ago are now genuinely reached (Mutation A above).

| # | Row (record 06) | Test | Evidence |
|---|---|---|---|
| 1 | `StartMove` Idle→Moving | `StartMove_from_idle_enters_moving` | green |
| 2 | `CompleteMove` Moving→Idle | `CompleteMove_from_moving_enters_idle` | green |
| 3 | `StartLoad` Idle→Loading | `StartLoad_from_idle_enters_loading` | green |
| 4 | `CompleteLoad` Loading→Idle | `CompleteLoad_from_loading_enters_idle` | green |
| 5 | `StartUnload` Idle→Unloading | `StartUnload_from_idle_enters_unloading` | green |
| 6 | `CompleteUnload` Unloading→Idle | `CompleteUnload_from_unloading_enters_idle` | green |
| 7 | `RaiseFault` Idle→Fault | `RaiseFault_from_idle_enters_fault` | green |
| 8 | `RaiseFault` Moving→Fault | `RaiseFault_from_moving_enters_fault` | green, red under Mutation A |
| 9 | `RaiseFault` Loading→Fault | `RaiseFault_from_loading_enters_fault` | green, red under Mutation A |
| 10 | `RaiseFault` Unloading→Fault | `RaiseFault_from_unloading_enters_fault` | green, red under Mutation A |

All ten anchored names appear in `dotnet test --list-tests`, all ten pass, and the four `RaiseFault`
rows are mutation-proven to fail when the fault guard is broken. **10/10.**

## Findings (second re-review)

- **B1 (prior Blocker) — cleared.** `RaiseFault_from_unloading_enters_fault` passes at
  `Start.Add(Timing.Load)`; Mutation A proves it real. No `ShuttleCore` change was needed.
- **S1 (prior Should-fix, third appearance) — cleared.** The occupied-slot test targets a
  genuinely occupied slot at a legal timestamp; removing the occupancy guard turns it red and the
  captured message names the occupancy guard. The `//IDK about this one` marker is gone.
- **C1 — `Rack.LaneId`/`SlotId` vs the lesson's fixed `LaneIds`/`SlotIds` (prior consider 3):**
  unchanged, non-blocking, no consumer wired. Carry to the professor; one side should move before
  M7's dashboard.
- **C2 — `RackTests` weaker than record 05's claims (prior consider 4):** unchanged, non-blocking.
- **C3 — R2/R4 remain due before M2.** `src/FleetManager.Tests/StartTests.cs` is still byte-identical
  to `11f82d1` (`git diff 11f82d1..HEAD -- …/StartTests.cs` empty): unbounded `WaitForExit()` (R2),
  the 9-space indent and no trailing newline (R4). Not this lesson's deliverable; the reference
  pattern is `DemoTests.cs`. **Status below.**
- **C4 — cosmetic slips:** the `//IDK about this one` and the blank line at
  `RejectionTests.cs:9` were addressed by the edit (comment removed); one blank line remains at
  `RejectionTests.cs:9` (line 8 is also blank) — cosmetic, non-blocking.

### AGENTS.md compliance (re-checked)

- **Decision logic pure and configuration respected:** Phases 1–3 all clean; `ShuttleCore` was not
  touched by this commit; no `Sleep`, no duration literal, no network vocabulary, references nothing.
- **Message contract untouched:** Phase 6 greps exit 0, zero-line contract diff. The demo's wire
  shape still matches `v1.md`.
- **No hard-coded config:** `ShuttleCore` has no duration literal or `TimeSpan` constructor; the
  demo/tests' `5 s`/`2 s`/`SH-01`/`PLT-001`/`2026-01-01` are scenario data (record 06 §4).
- **No throwaway/copied work:** md5 duplicate check silent; `git diff 673bede..cb895a5 -- src/ShuttleCore`
  empty, so the prior proofs that the scripted and demo tests are real still hold.
- **Tests pass before a review can pass:** **satisfied** — `dotnet test` exit 0, container exit 0.
- **No stray tracked build artifacts:** `git ls-files | grep -E '/(bin|obj)/'` prints nothing.

## README

**Changed:** the M1 row is flipped to `done`, and a verification note for M1 is added under
`### Verification notes`, naming the exact commands. See the next section for the note text.

## Cleanup

- No `docker compose` service was started (none needed); the only `docker` actions were `docker
  build` and a `--rm docker run`, which cleans its own container. `docker ps -a` shows only the
  pre-existing 10-day-old `hello-world` container (`elastic_jemison`), left as found.
- `/tmp/opencode/mut04` (the `git archive` copy used for every mutation) deleted; `/tmp/opencode`
  is empty.
- `dotnet build-server shutdown` run; no `dotnet test`, `dotnet run`, or MSBuild node left.
- `git status --porcelain` after this review: only this file and `README.md`.

## What is next

1. **M1 closes.** The M0 gates still hold; the next lesson is M2 (message contract and transport),
   against record 02.
2. **Carry-forward due before the M2 lesson: R2/R4** — bound the wait in
   `src/FleetManager.Tests/StartTests.cs` with a named timeout constant and `WaitForExit(<timeout>)`
   /`Kill(entireProcessTree: true)` (copy `DemoTests.cs`), fix the 9-space indent and the missing
   trailing newline. Not blocking M1, but the last three reviews have named it; it is now only the
   M2 lesson away from being a should-fix. R2/R4 **still open**.
3. **Carry to the professor (non-blocking):** C1's `LaneIds`/`SlotIds` naming decision, and C2's
   `RackTests` assertions (`1, 1, 3` distance triple; membership rejection of `A01-L01-S03`/
   `banana`).