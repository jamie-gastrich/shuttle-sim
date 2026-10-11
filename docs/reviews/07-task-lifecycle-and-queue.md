# Review 07: The task record, lifecycle, and pure board (lesson 07, first M3 slice)

**Date:** 2026-10-10
**Reviewed:** commits `4c8cb1a` (`M3: Add lesson 07: Task lifecycle and queue implementation` — docs-only:
lesson 07, its hints, and decision record 11) and `5769d7d` (`M3: feat: add FleetCore project with task
management classes and tests` — the owner's implementation; 8 files, +381/−0). On top of `5f74c20`
(`M2: feat: enhance logging in ReliableCommandSender and Program …` — the lesson-06 L1 / review-06 S2
clear). Branch `master`.
**Working tree at review time:** clean (`git status --porcelain` empty); HEAD `5769d7d`.
**Scope:** lesson 07 against its own assignment **and** the design fixed in
[decision record 11](../decisions/11-task-lifecycle-and-queue.md). This is the **first M3 lesson**;
the M3 done-when (three shuttles finish a queue; fleet state survives a restart) is explicitly
deferred to lessons 08 (assignment + SQL) and 09, so M3 is **not** complete here.
**Contract checked:** `docs/contract/v1.md` untouched (`Version: 1`); `docs/contract/v2.md` untouched
(`Version: 2`); no `v3.md`; no task type in `src/Contracts`. A task is internal at M3 — the wire did
not change.

**Verdict: pass with changes.**

The pure half of the task queue is real and well built. `FleetCore` is a genuinely isolated project
(no `ProjectReference`, no `PackageReference`, no clock, no ids, no wire types), the five states and
their wire names match record 11 §6 exactly, every guard the record fixes is present and is
mutation-proven load-bearing, and the board is a true insertion-ordered FIFO that skips work already
handed out. The build is clean, seven suites pass, the fresh container is green, M1/M2 do not regress,
and the wire contract is untouched. The two M2 post-review fixes this review was asked to clear (the
sender `attempt N of M` log and the shuttle `serve`-mode publish/handle log) are both present and
observed over the real broker.

The changes required before I call this a clean pass are small and confined to a few lines: the
required `Task_wire_names_match_record_11` test does **not test wire names at all** (a mutation that
renames `in_progress` to `inprogress` leaves all 18 tests green — a false green on the one vocabulary
M7/WMS will consume), the "blank" guards accept whitespace-only inputs, and the `ArgumentException`
for a blank id is built with its message and parameter name swapped. None of these break the model
today; all of them should be fixed before lesson 08 builds assignment on top of this shape. Details in
[Findings](#findings).

---

## Commands run, verbatim key output

Exit codes read directly, never through a pipe, except where the lesson's own block pipes or uses
`mktemp`. The acceptance block was run **verbatim from the current lesson file**, from the
directories it names. Mutations ran against throwaway `git archive HEAD` copies under
`/tmp/opencode/rev07`, deleted afterwards; no experiment touched `src/`.

### Phase 1 — the new projects exist and the solution grows by exactly two

```
$ test -f src/FleetCore/FleetCore.csproj            exit 0 (no output)
$ test -f src/FleetCore/FleetTask.cs                exit 0
$ test -f src/FleetCore/TaskBoard.cs                exit 0
$ test -f src/FleetCore/TaskState.cs                exit 0
$ test -f src/FleetCore.Tests/FleetCore.Tests.csproj exit 0
$ grep -c '<Project Path' src/ShuttleFleet.slnx     prints 14
$ grep -q 'ProjectReference Include="..\\FleetCore\\FleetCore.csproj"' \
    src/FleetCore.Tests/FleetCore.Tests.csproj      exit 0
$ grep -c 'ProjectReference\|PackageReference' src/FleetCore/FleetCore.csproj  prints 0
```

All green. `FleetCore.csproj` has only the three SDK property lines — it references nothing, exactly
as record 11 §4 requires.

### Phase 2 — pure means pure

```
$ grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' \
    src/FleetCore --include='*.cs'                  exit 1 (no matches)
$ grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts' src/FleetCore --include='*.cs'  exit 1
$ grep -rniE 'Npgsql|MQTTnet|Postgres' src/FleetCore/FleetCore.csproj  exit 1
$ grep -q 'TaskRejectedException' src/FleetCore/FleetTask.cs            exit 0
$ grep -q 'enum TaskState' src/FleetCore/TaskState.cs                   exit 0
$ grep -q 'NextQueued' src/FleetCore/TaskBoard.cs                       exit 0
```

All green. The purity greps are the heart of record 11 §4 and they hold.

### Phase 3 — from `src/`: build, run the suite, walk the new names

```
$ dotnet build                                      exit 0, 0 Warning(s), 0 Error(s)
$ T=$(mktemp); dotnet test > "$T" 2>&1              exit 0
$ grep -c 'Passed!' "$T"                            prints 7
$ grep -c 'No test is available' "$T"               prints 0
$ dotnet test --list-tests > "$L" 2>&1
$ for t in <the 18 names>; do grep -qE "\.$t\$" "$L" || echo "missing test: $t"; done  (no output)
```

The seven suites, verbatim:

```
Passed!  - Failed: 0, Passed: 18, … FleetCore.Tests.dll   (net10.0)
Passed!  - Failed: 0, Passed:  4, … Contracts.Tests.dll   (net10.0)
Passed!  - Failed: 0, Passed:  8, … Messaging.Tests.dll   (net10.0)
Passed!  - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  5, … FleetManager.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll  (net10.0)
```

**64 tests, seven `Passed!` lines, 0 "No test is available", the anchored 18-name walk silent.** The
`Passed!` count moved **6 → 7** exactly as the lesson says. `ShuttleCore.Tests` is still 22/22.

### Phase 4 — no regression: the M1 demo and the manager's start line still hold

```
$ dotnet run --project ShuttleSim/ShuttleSim.csproj  exit 0
$ grep -c '"ShuttleId"' (demo output)                prints 7
$ dotnet run --project FleetManager/FleetManager.csproj exit 0
$ grep -q 'Fleet Manager Coming Online'              exit 0
```

M1 demo unchanged (7 status lines); the M0 no-argument manager path intact.

### Phase 5 — the contract is untouched: a task is not a wire message

```
$ grep -qE '^Version: 2$' docs/contract/v2.md        exit 0
$ grep -qE '^Version: 1$' docs/contract/v1.md        exit 0
$ test ! -f docs/contract/v3.md                      exit 0 (no v3.md)
$ grep -rniE 'TaskState|FleetTask|TaskBoard' src/Contracts --include='*.cs' \
    --exclude-dir=obj --exclude-dir=bin              exit 1 (no matches)
```

### Mutation checks — the load-bearing invariants (throwaway `git archive HEAD` copy)

Baseline on the copy: `FleetCore.Tests` **18/18 passed**. Then:

```
# M1 — rename the in_progress wire name to "inprogress" (TaskState.cs)
TaskState.InProgress => "inprogress",
$ dotnet test FleetCore.Tests/FleetCore.Tests.csproj
Passed! - Failed: 0, Passed: 18 …        <-- NOTHING goes red  (finding B1)

# M2 — delete the state guard in Complete (if (State != TaskState.InProgress) -> if (false))
Failed FleetCore.Tests.TaskLifecycleTests.Complete_while_assigned_is_rejected
Failed! - Failed: 1, Passed: 17 …
[the timestamp test still passed]

# M3 — make EnsureInOrder never reject (if (_lastEventAt is DateTime previous && false))
Failed FleetCore.Tests.TaskLifecycleTests.A_move_with_a_timestamp_before_the_previous_one_is_rejected
Failed! - Failed: 1, Passed: 17 …

# M4 — make NextQueued return the first task regardless of state (_tasks.FirstOrDefault())
Failed FleetCore.Tests.TaskLifecycleTests.Board_next_queued_skips_assigned_and_finished_tasks
Failed! - Failed: 1, Passed: 17 …
```

M2, M3, and M4 prove the Complete guard, the clock-order guard, and the FIFO state-skip are all
load-bearing. **M1 proves the wire-name test is not** — see finding B1.

### Probes — blank inputs and the `ArgumentException` shape

```
$ PROBE whitespace taskId ctor exception:    NONE (accepted)   <-- "   " accepted as a task id
$ PROBE whitespace toLane ctor exception:    NONE (accepted)
$ PROBE whitespace shuttle assign exception: NONE (accepted)   <-- "   " accepted as a shuttle id
$ PROBE whitespace reason fail exception:    NONE (accepted)
$ PROBE assign whitespace state: Assigned
$ PROBE ctor   exception Message=[TaskId (Parameter 'TaskId cannot be empty.')] ParamName=[TaskId cannot be empty.]
$ PROBE assign exception Message=[shuttleId (Parameter 'ShuttleId cannot be empty.')] ParamName=[ShuttleId cannot be empty.]
```

### Fresh container

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build .   exit 0
$ docker run --rm shuttle-fleet-build                                exit 0
$ grep -c 'Passed!' (container output)                               prints 7   (18/4/8/2/22/5/5)
```

### M2 post-review fixes (broker), verified as asked

Phase 5a — round trip with the shuttle up first:

```
$ docker compose -f docker/compose.yml up -d broker                  exit 0
$ dotnet …/ShuttleSim.dll serve > "$S" 2>&1 &
$ dotnet …/FleetManager.dll move SH-01 A01-L02 > "$M" 2>&1
move exit=0
ack exit=0 ; accepted exit=0 ; moving-in-M exit=0 ; idle-in-M exit=0
S moving count: 1 ; S idle count: 1
```

Manager `$M` (three JSON lines) contains `attempt 1 of 3 for command <id>` on stderr, then a
correlated `commandAck` (`"Accepted":true`), then `moving` and `idle`. Shuttle `$S` now logs its work:

```
Shuttle SH-01 listening on shuttle/SH-01/command
handled shuttle/SH-01/command 9fb79acc-…
published shuttle/SH-01/ack {…"Accepted":true…}
published shuttle/SH-01/status {…"State":"moving"…}
published shuttle/SH-01/status {…"State":"idle"…}
```

Phase 5b — lost command retried (manager first, shuttle second):

```
$ export FleetManager__AckTimeoutMs=1000 FleetManager__MaxAttempts=6 FleetManager__BackoffMs=500
$ dotnet …/FleetManager.dll move SH-01 A01-L02 > "$M2F" 2>&1 &
$ sleep 2 ; dotnet …/ShuttleSim.dll serve > "$S2" 2>&1 &
move exit=0
accepted exit=0 ; attempt-log exit=0 ; attempt lines in manager: 3 ; S2 moving count: 1
```

The first two attempts landed with no subscriber; the third landed after the shuttle came up and was
acked. The attempt log shows **3** lines for the **same** command id, and the shuttle published one
`moving`/`idle` pair. Both fixes (lesson-06 requirement 2 and requirement 8) are real. Lesson-06's doc
fixes are consistent with the code: requirement 2 carries the attempt-log bullet, requirement 8 asks
for the serve-mode log, and Phase 5b's block carries
`grep -qE 'attempt [0-9]+ of [0-9]+' "$M2F"` — now passing. Review 06's S1 (RetryPolicy guards) is also
cleared: `RetryPolicy.cs` puts all three guards in the constructor body and the three tests assert on
construction.

---

## Acceptance table

| # | Lesson criterion | Evidence | Result |
|---|---|---|---|
| 1 | Phase 1: five new files exist | `test -f` × 5, exit 0 | met |
| 2 | Phase 1: solution grows to 14 projects | printed 14 | met |
| 3 | Phase 1: tests reference `FleetCore.csproj` | exit 0 | met |
| 4 | Phase 1: `FleetCore` references nothing | printed 0 | met |
| 5 | Phase 2: no clock / id / sleep in `FleetCore` | exit 1 | met |
| 6 | Phase 2: no `Npgsql`/`MQTTnet`/`Messaging`/`Contracts` | exit 1 | met |
| 7 | Phase 2: named markers present | three exit 0 | met |
| 8 | Phase 3: `dotnet build` exit 0, 0 warnings | exit 0, 0/0 | met |
| 9 | Phase 3: `dotnet test` exit 0 | exit 0 | met |
| 10 | Phase 3: `Passed!` prints 7 | printed 7 | met |
| 11 | Phase 3: `No test is available` prints 0 | printed 0 | met |
| 12 | Phase 3: anchored walk of all 18 names | silent | met |
| 13 | Phase 4: M1 demo exit 0, 7 `"ShuttleId"` lines | exit 0, 7 | met |
| 14 | Phase 4: manager start line | exit 0 | met |
| 15 | Phase 5: `v2.md` `Version: 2`, `v1.md` `Version: 1` | exit 0 / 0 | met |
| 16 | Phase 5: no `v3.md` | exit 0 | met |
| 17 | Phase 5: no task type in `Contracts` | exit 1 | met |
| 18 | Mutation: removing the `Complete` guard goes red | `Complete_while_assigned_is_rejected` FAIL | met |
| 19 | Req 3: five states + `TaskStateWire.ToWireName` | code matches record 11 §6 | met |
| 20 | Req 7: `Task_wire_names_match_record_11` asserts the five names | test asserts `TaskId`/`ToLane`, never `ToWireName` | **NOT met (B1)** |
| 21 | Req 5: constructor rejects blank `taskId`/`toLane` with `ArgumentException` (param name) | rejects `""`; **accepts `"   "`**; `ParamName` is the message text | **partial (S1, S2)** |
| 22 | Req 5: `Assign` rejects a blank `shuttleId` | rejects `""`; accepts `"   "` | partial (S1) |
| 23 | Req 5: `Fail` rejects a blank `reason` | rejects `""`; accepts `"   "` | partial (S1) |
| 24 | Req 5: `now` before the previous move is rejected | mutation-proven (`A_move_with_a_timestamp…` FAIL) | met |
| 25 | Req 5: wrong move throws `TaskRejectedException` naming the state | guards checked; tests assert the type | met |
| 26 | Req 6: board duplicate / lookup / FIFO / skip / Count | mutation-proven FIFO skip; duplicate test | met |
| 27 | Req 8: `FleetCore` is pure | Phase 2 greps | met |
| 28 | Req 7: every `[Fact]` has an `Assert` | all 18 do | met |
| 29 | Fresh container builds and passes | build/run exit 0, 7 `Passed!` | met |
| 30 | M2 L1: shuttle `serve` logs handles/publishes | `$S` `handled`/`published`, moving=1, idle=1 | met |
| 31 | M2 S2/L2: sender logs each attempt; Phase 5b greps it | `attempt 1 of 6 …`, 3 lines, grep exit 0 | met |
| 32 | Review-06 S1: `RetryPolicy` validates on construction | guards in ctor; tests assert construction | met |

**30 of 32 met, 2 partial misses, 1 outright miss (the vacuous wire-name test).** No blocker breaks
the product; B1 is a required test that is present but proves nothing.

---

## Substance checks

### The model follows record 11 §5–§8

`FleetTask` is a guarded, mutable object: `State` is `private set`, every move takes `DateTime now`,
and a private `EnsureInOrder` rejects a backwards clock and allows equal timestamps. `Assign` requires
`Queued`; `Start` requires `Assigned`; `Complete` requires `InProgress`; `Fail` is refused only from
`Done`/`Failed`. The board is a plain `List<FleetTask>` in insertion order, refuses a duplicate
`TaskId`, and `NextQueued()` returns the first task still in `Queued`. Time and identity are inputs:
the Phase 2 greps are clean, so there is no `DateTime.UtcNow` and no `Guid.NewGuid` inside
`FleetCore`.

### The board is genuinely FIFO, and the skip is load-bearing

`Board_hands_out_the_oldest_queued_task_first` returns `task-1` with two queued tasks, and
`Board_next_queued_skips_assigned_and_finished_tasks` returns `task-3` after `task-1` is assigned and
`task-2` is run to `Done`. Mutation M4 (return the first task regardless of state) turns that second
test red, so the state predicate — not just the list order — is what makes the queue advance.

### The decision logic is separated from I/O

`FleetCore` has zero references and no `using` outside `System` (`ImplicitUsings`), which the Phase 1
and Phase 2 gates prove. The tests drive the model with a fixed `new DateTime(2026, 1, 1, …)` base
exactly as `ShuttleCore.Tests` does, so the suite is deterministic and runs in milliseconds.

### The two M2 fixes are real over the broker

See the [broker phase](#phase-5b--lost-command-retried-manager-first-shuttle-second) above: the sender
logs attempts with the shared command id, the shuttle logs each handle and publish with the payload,
and Phase 5b's new grep passes. Lesson-06's requirement 2 bullet, requirement 8, and Phase 5b block are
internally consistent with the code and with the hints.

### One code-quality blemish inside the guarded object

`FleetTask.Fail` assigns `_lastEventAt = now;` **twice** (`FleetTask.cs:65-66`). It is harmless (the
second write wins with the same value), but it is clearly a copy/paste slip and should go. The same
file also keeps five comments copied verbatim from the hints' Tier-3 skeleton
(`// EnsureInOrder; must be Queued; …`), which read like placeholders rather than explanations — the
same "copied artifact" review 06 flagged as C2.

---

## Findings

### Blockers

**B1 — the required wire-name test asserts nothing about wire names.** `Task_wire_names_match_record_11`
(`src/FleetCore.Tests/TaskLifecycleTests.cs:144-154`) builds a task, runs it to `Done`, and then asserts
`TaskId == "task-1"` and `ToLane == "A01-L02"`. It never calls `TaskStateWire.ToWireName`, and no other
test in the repo does (`grep -rn 'TaskStateWire' src` finds only the definition). Mutation M1 proves the
gap: renaming `in_progress` to `inprogress` leaves all 18 tests green. Why it matters for a real shuttle
system: the wire spelling is the one piece of this slice a dashboard (M7) or a WMS (M6) will read, and
record 11 §6 exists specifically so "the dashboard and the WMS do not invent their own spelling later."
The lesson even points at `ShuttleCore.Tests/WireNamesTests.cs` as the pattern to copy. The fix is
mechanical — assert the five names through `ToWireName()`, including the `_` in `in_progress`.

### Should-fix

**S1 — the "blank" guards accept whitespace-only values.** `FleetTask.cs:8,10,29,61` use
`string.IsNullOrEmpty`, which treats `"   "` as valid. The lecture's constructor code and record 11 §5
both use `string.IsNullOrWhiteSpace`, and the requirement says "reject a blank" id/lane/shuttle/reason.
The probe shows all four whitespace inputs are accepted today (a task with id `"   "`, a shuttle with
id `"   "`, and so on). Why it matters: support would see a task with an apparently empty id, and the
next lesson's duplicate-id check and SQL primary key would carry the whitespace. Fix: switch the four
guards to `IsNullOrWhiteSpace`.

**S2 — the `ArgumentException` arguments are swapped, and the constructor uses the property name, not
the parameter name.** `new ArgumentException(nameof(TaskId), "TaskId cannot be empty.")` passes the
property name as the *message* and the sentence as the *paramName*, because the overload is
`(string message, string paramName)`. The probe shows the result:
`Message=[TaskId (Parameter 'TaskId cannot be empty.')] ParamName=[TaskId cannot be empty.]`. The
requirement asks for `ArgumentException` with "parameter name included"; the parameter is `taskId`,
not `TaskId`, and the message is the human sentence. Why it matters: an operator or support engineer
reading a startup failure gets a garbled message, and tooling that keys on `ParamName` gets the wrong
value. Fix: `new ArgumentException("TaskId cannot be empty.", nameof(taskId))` (and drop the property
capital for `taskId`/`toLane`).

**S3 — dead line and copied placeholder comments.** Remove the duplicated `_lastEventAt = now;` in
`Fail` (`FleetTask.cs:66`) and replace the five hints-template comments with sentences that explain
the guards. Cosmetic but it is code an interviewer will read line by line.

### Consider

- **C1 — blank-input guards throw a different exception type from the wrong-state guards.**
  `Assign("")` and `Fail("")` throw `ArgumentException`, while every wrong *state* throws
  `TaskRejectedException`; the lecture shows `TaskRejectedException("shuttle id required")` for the
  blank-input case. The tests only assert the base type behaviour, so both pass. It is worth deciding
  deliberately: lesson 08's assignment loop will likely catch `TaskRejectedException` to map a bad
  assignment to `Fail`, and a blank shuttle id would escape as an `ArgumentException`. Low impact
  (blank ids should not occur in production), but pick one type and say why.
- **C2 — `TaskBoard.TryGet`'s false path and `Get`'s throw path are untested.** Not in the required
  list, but `Get` is the lookup lesson 08 will lean on; one `Assert.Throws<KeyNotFoundException>` would
  cover it.
- **C3 — house style nits:** trailing whitespace at `TaskState.cs:3-4` and
  `TaskLifecycleTests.cs:87,206,209`; none of the five new `.cs` files ends with a newline (the hints
  ask for a trailing newline, though the existing `ShuttleCore` files share the same gap, so this is
  consistent-but-wrong). No exact-nine-space indent; no bare `WaitForExit()`.
- **C4 — `Fail_after_failed` is not tested**, only `Fail_after_done`. Not required, but it is the
  other half of the terminal claim.

### Lesson defects (for the professor)

- **L1 — the acceptance anchor checks test *names*, not test *bodies*.** Phase 3's
  `grep -qE "\\.$t\$"` walk passed for `Task_wire_names_match_record_11` even though the body asserts
  unrelated fields. That is how B1 reached review. The lesson could add a content-level gate (for
  example, a grep for `ToWireName` in the wire-name test) or state the assertion explicitly in
  requirement 7, so a vacuous test cannot satisfy the anchor.
- **L2 — "blank" is not defined, and the blank-input exception type is not fixed.** Requirement 5 says
  "blank" for one constructor case (with `ArgumentException`) but leaves the type open for `Assign`/
  `Fail`, while the lecture shows `TaskRejectedException`. The owner's `IsNullOrEmpty` meets a literal
  reading of "blank is empty" but not the lecture's `IsNullOrWhiteSpace`. Say "whitespace-only counts
  as blank" and name the exception for each of the three blank-input guards. This is a lesson clarity
  gap, not an owner failure on its own.

Concept check: the lesson's three required concepts — the task record, the task lifecycle, and the task
board — are each taught in their own section, and the lesson stays at the 3-concept cap. No gap there.

---

## Explain-it questions — NON-BLOCKING

Answer these out loud before the next lesson.

1. `TaskBoard.NextQueued()` returns the first task whose state is `Queued`, not the first task. Walk
   through what the manager would re-hand out if it returned the first task regardless of state, and
   explain why assigned, in-progress, done, and failed tasks all still have to live on the board at the
   same time.
2. `FleetTask.State` is `private set`, and every move takes `DateTime now` and stores it in a private
   `_lastEventAt`. Why is the time passed in rather than read from `DateTime.UtcNow` inside the move,
   and what does it mean when `Assign` and `Start` are called with the **same** timestamp?
3. `Fail` is allowed from `Queued`, `Assigned`, and `InProgress`, but `Complete` only from
   `InProgress`. Give one real fleet event for each of the three `Fail` entries, and explain what M5
   will have to add to the state machine to re-open a `Failed` task.

### Blocks to study (unclear, fragile, or copied-looking)

- `src/FleetCore.Tests/TaskLifecycleTests.cs:144-154` — the test named
  `Task_wire_names_match_record_11` never calls `ToWireName`; compare it with
  `src/ShuttleCore.Tests/WireNamesTests.cs:8-12`, which is the pattern the lecture told you to copy.
- `src/FleetCore/FleetTask.cs:55-67` — the duplicated `_lastEventAt = now;` and the five comments
  copied from the hints' Tier-3 skeleton.
- `src/FleetCore/FleetTask.cs:8-11,29-30,61-62` — `IsNullOrEmpty` vs `IsNullOrWhiteSpace`, and the
  `ArgumentException(message, paramName)` argument order; be able to say which argument is which.
- `src/FleetCore/TaskBoard.cs:23-24` — the `State == TaskState.Queued` predicate; be able to explain
  why it, not the list order alone, is what skips work already in flight.

---

## Revisit list

- **A test that runs is not a test that checks.** The required wire-name test passed the anchored gate
  while asserting the wrong fields; only a mutation exposed it. Read what a test asserts, not its name.
- **"Blank" in .NET means `IsNullOrWhiteSpace`, not `IsNullOrEmpty`.** Know the difference and why it
  matters for ids that become database keys.
- **Constructor-overload order is easy to get backwards.** `ArgumentException(message, paramName)`
  compiles either way; only reading `ParamName`/`Message` shows the swap.
- **Terminal states are a design choice with an owner.** `Done`/`Failed` are sinks at M3 and M5 adds
  the edge; being able to name that boundary now is what makes lesson 08's mapping to `moveCommand`
  endings easy.
- Next in M3: assignment policy + SQL save/load (lesson 08), then three shuttles finishing a queue and
  restart survival (lesson 09).

---

## End-product check

This is the pure shape every later M3 piece stands on, and it belongs in `FleetCore` beside `ShuttleCore`
for the same reason: the fleet manager's decisions must be testable without a broker or a database. The
lifecycle names the spec chose (queued, assigned, in progress, done, failed) are now one enum with one
wire mapping, so M7's dashboard and M6's WMS have a single spelling to read; the board is the queue the
spec asks for, and it is deliberately memory-only so lesson 08 can add PostgreSQL as a layer instead of
rewriting the model. The risk this slice introduces is small but real: the wire-name vocabulary is
currently protected by no test (B1), and the blank guards and garbled `ArgumentException` (S1/S2) are
the kind of rough edges that get copied into lesson 08's assignment loop. Fix those three lines and the
next lesson starts from a shape that can be trusted. **M3 is in progress, not done — persistence and the
restart proof are still ahead.**

---

## README

**Not changed.** The verdict is **pass with changes**, and AGENTS.md says the milestone table changes
only after a passing review, so M3 stays `not started` here. Clearing **B1** (and, while the file is
open, S1–S3) is a small edit; on the next review I will move M3 to `in progress` with a verification
note recording the commands above and stating plainly that M3 is not complete until lesson 08's SQL
persistence and lesson 09's restart proof land.

---

## What I changed in this repository

- **Added:** `docs/reviews/07-task-lifecycle-and-queue.md` (this file).
- **`README.md`:** not touched.
- **`src/`, `docker/`, `docs/lessons/`, `docs/decisions/`, `docs/contract/`, `spec/`:** not touched.
  Mutations and probes ran against a throwaway `git archive HEAD` copy
  (`/tmp/opencode/rev07`) and were deleted.

---

## What the owner must do next

1. **B1:** make `Task_wire_names_match_record_11` assert all five wire names through
   `TaskStateWire.ToWireName()` — `queued`, `assigned`, `in_progress`, `done`, `failed` — using
   `ShuttleCore.Tests/WireNamesTests.cs` as the model. `dotnet test` from `src/` must stay 7 suites
   green; then repeat the M1 mutation (rename `in_progress` → `inprogress`) and watch it go red.
2. **S1:** change the four blank guards to `string.IsNullOrWhiteSpace`.
3. **S2:** fix the `ArgumentException` argument order and use the parameter names
   (`nameof(taskId)`, `nameof(toLane)`).
4. **S3 + C3:** delete the duplicated `_lastEventAt = now;` in `Fail`, replace the hints-template
   comments, strip the trailing whitespace, and add the trailing newline.
5. **C1:** decide deliberately whether blank-input guards throw `TaskRejectedException` (as the
   lecture shows) or `ArgumentException`, and make the tests assert that type.
6. Answer the three explain-it questions out loud; they are for the next lesson's discussion.

---

## Cleanup and files changed

- **Cleanup:** `/tmp/opencode/rev07` and all review log files deleted; `docker compose -f
  docker/compose.yml down` run after the broker phases (no `shuttle-fleet` network, no live shuttle
  container — `docker ps` shows none); `dotnet build-server shutdown` run; `pgrep` shows no
  `FleetManager`/`ShuttleSim` processes. `/tmp/opencode` holds only the pre-existing `mqttinspect`
  scratch, left as found. The `shuttle-fleet-build` image tag remains (an image, not a process).
- **`git status --porcelain` after this review:** this review file only.
