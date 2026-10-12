# Review 09: Three shuttles finish a queue; state survives a restart (lesson 09, third M3 slice)

**Date:** 2026-10-12
**Reviewed:** commit `663bf83` (`M3: feat: implement end-to-end tests and task dispatcher functionality`) on top of `ed6e493` (lesson 09 rewrite) and `2c01b3c` (lesson 09 + hints + decisions 15/15a). HEAD `663bf83`; working tree clean at review start.
**Scope:** lesson 09 against its own assignment, [record 15](../decisions/15-m3-end-to-end-assignment-and-restart.md) and its supplement [record 15a](../decisions/15a-task-dispatcher-and-db-test-fixtures.md), on top of [record 11](../decisions/11-task-lifecycle-and-queue.md), [record 13](../decisions/13-fleet-state-persistence.md) and [record 14](../decisions/14-db-test-gate-and-config.md). This is the last piece of M3.
**Contract checked:** `docs/contract/v1.md` `Version: 1`, `docs/contract/v2.md` `Version: 2`, no `v3.md`; `src/Contracts` contains no `FleetTask`/`TaskState` (grep exit 1). A task is internal at M3; no wire change.

**Verdict: pass (clean).**

**This review supersedes the 36-line review of the same name committed in `663bf83`.** That review's verdict was "implementation passes the lesson 09 technical acceptance", with "No implementation findings". It listed the commands it ran in a table with a pass/fail column and posed five explain-it questions, but it recorded no exit codes, no verbatim output, no test counts beyond the aggregate "70 tests across 7 test assemblies", ran no mutations, made no line-level checks against the code, and had no lesson-defect section. Its claims are largely correct; they are simply unbacked. A verdict with no evidence is not a review; every claim below is backed by a command or a file read. The superseded review was also part of the artefact under review (the owner committed it inside the same commit as the code, and it describes "focused completions made at the owner's request"), so its "no findings" line cannot be treated as an independent check.

**Why pass, not pass with changes.** Every one of the seven lesson acceptance phases is green on the committed tree (`663bf83`), the fresh-container gate exits 0 with the two `Integration` tests excluded, both DB-backed tests truly execute against a live PostgreSQL and are mutation-proven non-vacuous, and the M3 done-when *("a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart")* is met end to end. No blocker and no should-fix remain. The findings below are considerations that matter for M4/M5 and the deferred run loop, plus two small lesson-gate precision defects for the professor. M3 is done on the technical gates; the owner still owes the explain-it answers out loud before the milestone is fully closed (noted at the end).

---

## Superseded review

The thin review at this path (committed in `663bf83`, 36 lines) is replaced in full by this document. It is not wrong so much as unbacked: its verification table lists the commands it says it ran and marks each "Passed", and its prose describes the dispatcher, the tests, and the fixtures correctly — but it prints no exit code, no verbatim output, and almost no counts, and it does not point at a single line of the code it is judging. It ran no mutations, so it never showed that any test is load-bearing, and it had no lesson-defect section. It did pose five explain-it questions and correctly refused to flip the milestone table before the owner answered them ("M3 remains **in progress**"). The claims it made that this review can confirm: the pure suite is 70 tests / 7 assemblies, both integration tests run, the dispatcher stays in `FleetManager`, the sentinel reset is used, and the contract is unchanged. What it could not have supported: "No implementation findings", which was asserted without reading the code for failure modes (the five considerations and two lesson defects below). One more thing worth naming: the review also says it "includes the focused completions made at the owner's request", which means the same commit contains both code changes and the review approving them — a reviewer cannot independently approve their own edits.

---

## Commands run, verbatim key output

Run from the repo root unless a phase says `src/`. Exit codes read directly. Mutations ran against a throwaway `git archive HEAD` copy at `/tmp/opencode/rev09`, deleted afterward; no experiment touched `src/`, and no file under `src/` was modified by this review.

### Environment

```
$ dotnet --version        → 10.0.112
$ docker --version        → Docker version 29.8.1, build 4a63305
$ docker compose -f docker/compose.yml ps
  NAME  IMAGE  COMMAND  SERVICE  CREATED  STATUS  PORTS      ← nothing up at review start
$ grep -c '<Project Path' src/ShuttleFleet.slnx  → 14        ← no new project (record 15a §3)
```

### Phase 1 — the dispatcher and its pinned API exist

```
$ test -f src/FleetManager/TaskDispatcher.cs || echo "missing TaskDispatcher.cs"        (no output)
$ grep -qE 'class TaskDispatcher' src/FleetManager/TaskDispatcher.cs                    → class OK
$ grep -qE 'TaskBoard' src/FleetManager/TaskDispatcher.cs                              → TaskBoard OK
$ grep -qE 'IFleetTaskRepository' src/FleetManager/TaskDispatcher.cs                   → IFleetTaskRepository OK
$ for m in AssignNextAsync StartAsync CompleteAsync FailAsync SaveAsync; do
    grep -qE "${m}\s*\(" src/FleetManager/TaskDispatcher.cs || echo "missing method: $m"; done
  (no "missing method" line)                                                            ← all five pins present
```

### Phase 2 — the dispatcher is pure; `FleetCore` stays pure and `FleetCore.Tests` stays pure

```
$ grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' src/FleetManager/TaskDispatcher.cs
  (no matches)                                                                          exit 1
$ grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts' src/FleetManager/TaskDispatcher.cs
  (no matches)                                                                          exit 1
$ grep -rniE 'Npgsql|Postgres|Microsoft.Data.SqlClient' src/FleetCore --include='*.cs' --include='*.csproj'
  (no matches)                                                                          exit 1
$ grep -c 'ProjectReference\|PackageReference' src/FleetCore/FleetCore.csproj           prints 0
$ grep -c 'FleetManager' src/FleetCore.Tests/FleetCore.Tests.csproj                     prints 0   (review 08 S1 stays fixed)
```

### Phase 3 — build and the DB-less pure suite (the container's command)

```
$ cd src && dotnet build
  Build succeeded. 0 Warning(s) 0 Error(s)                                              exit 0
$ dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"                   RC 0
  Passed!  - Failed: 0, Passed:  4, … Contracts.Tests.dll         (net10.0)
  Passed!  - Failed: 0, Passed: 18, … FleetCore.Tests.dll         (net10.0)
  Passed!  - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll       (net10.0)
  Passed!  - Failed: 0, Passed:  8, … Messaging.Tests.dll         (net10.0)
  Passed!  - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll     (net10.0)
  Passed!  - Failed: 0, Passed: 11, … FleetManager.Tests.dll      (net10.0)   ← 5 + 6 new pure tests
  Passed!  - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll        (net10.0)
  7 "Passed!" lines, 0 "Failed!"; 4+18+22+8+2+11+5 = 70 tests.
```

Baseline was 64 pure tests (review 08 round 3); lesson 09 adds the six dispatcher tests and the pure total is **70**, exactly as the lesson predicted.

### Phase 3b — the six prescribed pure tests are really there, by name

```
$ dotnet test ShuttleFleet.slnx --list-tests  → the six names, anchored:
  FleetManager.Tests.TaskDispatcherTests.AssignNextAsync_assigns_the_oldest_queued_task_and_saves_it
  FleetManager.Tests.TaskDispatcherTests.AssignNextAsync_returns_null_when_nothing_is_queued
  FleetManager.Tests.TaskDispatcherTests.A_rejected_transition_throws_and_saves_nothing
  FleetManager.Tests.TaskDispatcherTests.Round_robin_order_assigns_shuttles_in_turn
  FleetManager.Tests.TaskDispatcherTests.Reload_preserves_fifo_order
  FleetManager.Tests.TaskDispatcherTests.FailAsync_persists_the_failure
  (the anchored name walk prints no "missing test:" line)
```

### Phase 4 — with the database up, both trait tests truly run (not skipped)

```
$ docker compose -f docker/compose.yml up -d database                                   exit 0, accepting connections
$ cd src
$ dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m \
    --logger "console;verbosity=detailed" --filter "Category=Integration"               RC 0
  Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks [447 ms]
  Passed FleetManager.Tests.EndToEndTests.Three_shuttles_finish_queue_and_state_survives_restart [150 ms]
  Passed: 2                                                                             ← no "Skipped:" line
$ dotnet test ShuttleFleet.slnx -v m                                                    exit 0
  FleetManager.Tests 13/13 (11 pure + 2 integration); 72 tests total, 7 "Passed!" lines.
```

### The rows the end-to-end test wrote (read back with `psql`)

This is the real database, not the assertion output. Ten rows, FIFO order, wire-name states, round-robin shuttle ids, and the one held task:

```
  task_id  | to_lane |       created_at       | state  | assigned_shuttle_id | failure_reason
-----------+---------+------------------------+--------+---------------------+----------------
 task-1    | A01-L01 | 2026-01-01 00:00:00+00 | done   | SH-01               |
 task-2    | A01-L02 | 2026-01-01 00:01:00+00 | done   | SH-02               |
 task-3    | A01-L03 | 2026-01-01 00:02:00+00 | done   | SH-03               |
 task-4    | A01-L04 | 2026-01-01 00:03:00+00 | done   | SH-01               |
 task-5    | A01-L05 | 2026-01-01 00:04:00+00 | done   | SH-02               |
 task-6    | A01-L06 | 2026-01-01 00:05:00+00 | done   | SH-03               |
 task-7    | A01-L07 | 2026-01-01 00:06:00+00 | done   | SH-01               |
 task-8    | A01-L08 | 2026-01-01 00:07:00+00 | done   | SH-02               |
 task-9    | A01-L09 | 2026-01-01 00:08:00+00 | done   | SH-03               |
 task-hold | B02-L01 | 2026-01-01 01:00:27+00 | queued |                     |
(10 rows)
```

The `SH-01, SH-02, SH-03, SH-01, …` pattern in the real rows is the round-robin policy, and `task-hold` sits after all nine at whole-second precision. This is exactly what record 15 §1 and §3 describe.

### Phase 5 — fresh container, no database, exit 0

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build .                      exit 0
$ docker run --rm shuttle-fleet-build                                                   exit 0
  Passed!  - Failed: 0, Passed:  4, … Contracts.Tests.dll
  Passed!  - Failed: 0, Passed: 18, … FleetCore.Tests.dll
  Passed!  - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll
  Passed!  - Failed: 0, Passed:  8, … Messaging.Tests.dll
  Passed!  - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll
  Passed!  - Failed: 0, Passed: 11, … FleetManager.Tests.dll      ← integration test excluded
  Passed!  - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll
  7 "Passed!" lines
```

There is no database in this container. `FleetManager.Tests` shows **11**, not 13: the trait filter (`--filter Category!=Integration`, `Dockerfile.build` line 6) excluded the two integration tests. If the filter were wrong, they would run and fail (RC 1). This is the container half of record 14 §5's two-gate proof.

### Phase 6 — trait, collection, config-driven string, sentinel hygiene, no docker start, Dockerfile shape

```
$ grep -qE 'Trait\("Category",\s*"Integration"\)' src/FleetManager.Tests/EndToEndTests.cs   exit 0
$ grep -q 'Three_shuttles_finish_queue_and_state_survives_restart' src/FleetManager.Tests/EndToEndTests.cs  exit 0
$ grep -qE 'Collection\("Database"\)' src/FleetManager.Tests/EndToEndTests.cs               exit 0
$ grep -qE 'CollectionDefinition\("Database"' src/FleetManager.Tests/DatabaseCollection.cs  exit 0
$ grep -qE 'Collection\("Database"\)' src/FleetManager.Tests/RestartSurvivalTests.cs        exit 0  (checked by hand; see L2)
$ grep -q 'SHUTTLE_DB_CONNECTION_STRING' src/FleetManager.Tests/TestDatabase.cs             exit 0
$ grep -qE 'GetAsync' src/FleetManager.Tests/TestDatabase.cs                                exit 0
$ grep -qF 'Category!=Integration' docker/Dockerfile.build                                  exit 0
$ grep -q 'RUN dotnet build src/ShuttleFleet.slnx' docker/Dockerfile.build                  exit 0
$ grep -qE 'Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))' src/FleetManager.Tests/EndToEndTests.cs \
    && echo "FAIL: test must not start docker" || echo "OK: test does not start docker"
  → OK: test does not start docker
```

### Phase 7 — contract unchanged

```
$ grep -qE '^Version: 2$' docs/contract/v2.md      exit 0     ($ grep -qE '^Version: 1$' docs/contract/v1.md  exit 0)
$ test ! -f docs/contract/v3.md                    exit 0
$ grep -rniE 'FleetTask|TaskState' src/Contracts --include='*.cs' --include='*.csproj'   no match, exit 1
```

### Regression checks (M1/M2 stay intact)

```
$ dotnet run --project ShuttleSim/ShuttleSim.csproj (timeout 15)
  … {"ShuttleId":"SH-01","State":"idle","Position":"A02-L02","Timestamp":"2026-01-01T00:00:17Z",…}
  … unloading A02-L02, idle A02-L02 at 00:00:19Z      exit 0, 7 status lines
$ dotnet run --project FleetManager/FleetManager.csproj (timeout 10)
  Fleet Manager Coming Online                          exit 0
```

`Program.cs` is still the M2 `move` demo (line 10 `Fleet Manager Coming Online`, line 16 `Usage: FleetManager move <shuttleId> <toLane>`); the commit did not touch it. The Dockerfile was not touched (line 5 `RUN dotnet build`, line 6 `CMD … --filter “Category!=Integration”`). The contract was not touched.

### Commit inventory — what `663bf83` actually changes

`git show --stat 663bf83` = 8 files, +385 / −45, and the file list is the whole surface under review. Nothing else in the tree moved.

| File | Lines now | Role in this slice | Kind |
|---|---|---|---|
| `src/FleetManager/TaskDispatcher.cs` | 41 | the new app-layer owner of the board + repository; the only new product code | **product** |
| `src/FleetManager.Tests/TaskDispatcherTests.cs` | 130 | the six pure dispatcher tests | test |
| `src/FleetManager.Tests/InMemoryFleetTaskRepository.cs` | 46 | list-backed fake with `SaveCount` | test double |
| `src/FleetManager.Tests/EndToEndTests.cs` | 76 | the one DB-backed end-to-end test (queue + restart) | test |
| `src/FleetManager.Tests/TestDatabase.cs` | 44 | config-driven connection string + sentinel reset (record 14 §6, record 15a §4) | test fixture |
| `src/FleetManager.Tests/DatabaseCollection.cs` | 6 | non-parallel `Database` collection definition | test fixture |
| `src/FleetManager.Tests/RestartSurvivalTests.cs` | 70 | refactored to use `TestDatabase` and join the collection (behaviour unchanged) | test |
| `docs/reviews/09-three-shuttles-finish-queue.md` | 36 (then) | the thin review this document supersedes | doc |

Line counts are read from the committed blobs (`git show 663bf83:<file>`), so they do not change if the working tree drifts (see the process note under Cleanup). Two observations from the shape alone. First, the only new *product* file is `TaskDispatcher.cs` (41 lines); everything else is tests or fixtures — the code-to-test ratio is heavily test-side, which is right for a slice whose deliverable is "prove M3 end to end", but it means the dispatcher carries a lot of importance for 41 lines. Second, the commit committed its own review document (the thin one) inside the same commit as the code it reviews, which is the root of why that review could not have been an independent check (noted at the top). No `.csproj`, no solution, no `Program.cs`, no Dockerfile, no contract file appears in the stat.

---

## Acceptance criteria

Checked against the lesson's seven numbered Requirements (called R1–R7 here) and its seven acceptance phases. Evidence is a command in this repo or a quoted file/line.

| # | Criterion | Evidence | Result |
|---|---|---|---|
| 1 | R1: `src/FleetManager/TaskDispatcher.cs` exists, `sealed`, pinned ctor and five names | Phase 1 exit 0 for all; file read | met |
| 2 | R1: dispatcher pure — no clock, ids, waits, I/O types | Phase 2 greps exit 1; `TaskDispatcher.cs:1-41` read | met |
| 3 | R1: guard stays in `FleetTask`; save after the guard, rejected move saves nothing | `TaskDispatcher.cs:16-17,24-25,31-32,38-39`; `TaskDispatcherTests.cs:53-59` + mutation M1 | met |
| 4 | R2: `TaskDispatcherTests.cs` + `InMemoryFleetTaskRepository.cs`, six named `[Fact]`s, no `Integration` trait | Phase 3b; Phase 4 excludes them (70 pure) | met |
| 5 | R3: `EndToEndTests.cs`, one `[Fact]`, `Integration` trait + `Database` collection | Phase 6; `EndToEndTests.cs:5-10` | met |
| 6 | R3 Phase A: nine whole-second tasks, round-robin three shuttles, all `Done`, queue empty | `EndToEndTests.cs:17-48`; psql rows; whole-suite run | met |
| 7 | R3: every transition persisted through the dispatcher | dispatcher saves in Assign/Start/Complete; `psql` shows `done`; mutation M1 red | met |
| 8 | R3 end of shift: `task-hold` queued and saved | `EndToEndTests.cs:50-53`; the psql `task-hold`/`queued` row; mutations M3a/M3b red | met |
| 9 | R3 Phase B: new `TaskBoard` + new `PostgresFleetTaskRepository`, reload, ten tasks, identical fields, FIFO, `NextQueued()==task-hold` | `EndToEndTests.cs:55-74`; psql; mutation M2 red | met |
| 10 | R3: connection string from configuration; no docker start | Phase 6; `TestDatabase.cs:9-33` | met |
| 11 | R4: `TestDatabase.cs` with record-14 §6 precedence and fail-fast port | `TestDatabase.cs:11-32`; review-08 C2 re-verified by read | met |
| 12 | R4: reset ensures via sentinel `GetAsync` (parses no rows) then `DELETE` — closes C3 | `TestDatabase.cs:35-43`; mutation M4 red with `LoadAllAsync`, green + row deleted with sentinel | met |
| 13 | R4: `RestartSurvivalTests` refactored, behaviour unchanged | `RestartSurvivalTests.cs:12-14`; Phase 4 `Passed`; same assertions | met |
| 14 | R5: `DatabaseCollection` `[CollectionDefinition("Database", DisableParallelization = true)]`; both classes join | Phase 6; `DatabaseCollection.cs:3`; Phase 4 both tests pass | met |
| 15 | R6: `FleetCore`/`FleetCore.Tests` untouched and pure | Phase 2; commit touches neither | met |
| 16 | R7: `Program.cs`, Dockerfile, contract untouched | regression checks; commit file list | met |
| 17 | Phase 3: build clean, RC 0, 7 `Passed!`, no `Failed!`, 70 tests, `FleetManager` 11 | verbatim above | met |
| 18 | Phase 3b: six test names present exactly | verbatim above | met |
| 19 | Phase 4: both `Passed`, `Passed: 2`, zero skipped, RC 0 | verbatim above | met |
| 20 | Phase 5: fresh container RC 0, 7 `Passed!` | verbatim above | met |
| 21 | Phase 6: trait/collection/config/sentinel/no-docker/Dockerfile | verbatim above | met |
| 22 | Phase 7: contract v1/v2, no v3, no task types in `Contracts` | verbatim above | met |
| 23 | M3 done-when: queue finished by three shuttles | Phase A + psql + whole suite | met |
| 24 | M3 done-when: fleet state survives a fleet manager restart | Phase B + `RestartSurvivalTests`; mutations M1/M2/M5/M6 | met |

**24 met, 0 not met, 0 should-fix.** No gate is red; the verdict is pass.

---

## Spec traceability — the M3 clauses to evidence

The spec's M3 line (`spec/shuttle-spec.md:47-48`) has four clauses and two done-when clauses. Each maps to a command or a file read, not to prose:

| Spec clause (verbatim) | Where it is satisfied | Evidence |
|---|---|---|
| "A task queue with a clear lifecycle (queued, assigned, in progress, done, failed)" | `FleetTask` state machine + `TaskBoard` (lessons 07/08); this slice drives it in motion | `RestartSurvivalTests` round-trips all five states; `EndToEndTests` drives queued→assigned→in-progress→done; `TaskRejectedException` on illegal moves |
| "Assign tasks to one shuttle, then several" | `TaskDispatcher.AssignNextAsync` takes the shuttle id; the caller chooses it | Phase A drives `SH-01/02/03`; `psql` shows the round-robin pattern |
| "Save state in SQL" | `PostgresFleetTaskRepository` upsert, one row per transition | Phase 4's with-DB run; the `psql` readback shows the persisted rows |
| *Done when:* "a queue of tasks is finished by three shuttles" | Phase A finishes all nine tasks, three per shuttle, queue empty | `EndToEndTests.cs:45-48`; the ten-row `psql` table |
| *Done when:* "fleet state survives a fleet manager restart" | Phase B reloads a fresh board + fresh repository from PostgreSQL | `EndToEndTests.cs:55-74`; `RestartSurvivalTests`; mutations M1/M2/M5/M6 |

Every spec clause has an evidence line. Nothing in the M3 done-when rests on a claim that is not reproducible from this repo.

---

## Lesson verification table, resolved

Lesson 09's own verification table (`docs/lessons/09-three-shuttles-finish-queue.md:58-79`) ended with a list of what it could not check: *"Not yet verified: the dispatcher, the two test files, the shared helper, the collection file, and every acceptance phase below — this assignment specifies them and the reviewer measures them."* That is the reviewer's checklist, and here is each item resolved.

| Lesson item "not yet verified" | Resolved by | Result |
|---|---|---|
| the dispatcher | Phase 1 (API), Phase 2 (purity), mutations M1/M6/M7 | present, pure, and its saves are load-bearing |
| `TaskDispatcherTests.cs` | Phase 3 (70 tests), Phase 3b (six names), mutations M6/M7 | six `[Fact]`s, correct names, non-vacuous |
| `EndToEndTests.cs` | Phase 4 (both DB tests run), mutations M1/M2/M3/M5/M7 | one `[Fact]`, trait + collection, load-bearing |
| the shared helper (`TestDatabase.cs`) | Phase 6, Round 2 A/B (precedence + fail-fast), mutation M4/M4′ | matches record 14 §6; closes C3 |
| the collection file (`DatabaseCollection.cs`) | Phase 6, Round 2 C (three clean runs) | defined and joined by both DB classes |
| every acceptance phase | Phases 1–7, verbatim | all seven green, RC 0 |

The lesson's *pre-verified* claims also held up: the baseline was 64 pure tests (it predicted 70 after the six new ones), `FleetTask` is guarded with a `private set` state, and the container's `Category!=Integration` filter is intact. The one thing the lesson could not have seen — whether the two DB tests truly execute rather than skip — is exactly what Phase 4 answered (`Passed: 2`, zero skipped).

---

## Mutation testing (throwaway `git archive HEAD`, `/tmp/opencode/rev09`)

Baseline of the two integration tests in the copy: `Passed! 2/2` (exit 0); baseline pure suite: 70 tests. Each mutation was applied to the committed tree in the copy, run against the live database, then the file restored from a pristine backup, the `fleet_tasks` table cleared, and the run re-verified green before the next mutation. Targets are product files except M3b, which is the lesson's own stated proof ("if a reviewer deletes the `task-hold` save, the `NextQueued()` assert must go red") and is labelled as a test self-mutation.

| # | Mutation | File / line | Result |
|---|---|---|---|
| **M1** | Remove the `SaveAsync` from `CompleteAsync` | `TaskDispatcher.cs:32` | **RED** — `EndToEndTests`: `Assert.Equal() Failure: Values differ — Expected: Done, Actual: InProgress` at `EndToEndTests.cs:70` |
| **M2** | Flip load order `created_at ASC` → `DESC` | `PostgresFleetTaskRepository.cs:80` | **RED** — both integration tests fail (`Failed! 2/2`): FIFO order asserted by e2e and by `RestartSurvivalTests` |
| **M3a** | Repository drops the save for `task-hold` (`if (TaskId=="task-hold") return;`) | `PostgresFleetTaskRepository.cs:43` | **RED** — `Expected: 10, Actual: 9` at `EndToEndTests.cs:61` |
| **M3b** | Delete the test's own `SaveAsync(holdTask, …)` line | `EndToEndTests.cs:53` (test) | **RED** — `Expected: 10, Actual: 9` at `EndToEndTests.cs:61` (count first; `NextQueued()` would then be null) |
| **M4** | Replace the sentinel ensure with `LoadAllAsync`, then plant `state='legacy_state'` | `TestDatabase.cs:37` | **RED** — `System.ArgumentException : Unknown task state wire name 'legacy_state'. (Parameter 'wireName')` before the reset can delete |
| **M4′** | Restore the sentinel `GetAsync`; **same** `legacy_state` row still present | `TestDatabase.cs:37` | **GREEN** — both tests pass; the reset then deletes the stale row (`psql` after: no `stale-probe`) |
| **M5** | Bind `state` to `task.State.ToString()` instead of `ToWireName()` | `PostgresFleetTaskRepository.cs:128` | **RED** — both tests: `Unknown task state wire name 'Queued'` / `'Done'` |
| **M6** | Remove the `SaveAsync` from `FailAsync` | `TaskDispatcher.cs:39` | **RED** — pure `FailAsync_persists_the_failure` fails (1/1 → 0) |
| **M7** | Dispatcher assigns `"SH-99"` instead of the caller's `shuttleId` | `TaskDispatcher.cs:16` | **RED** — pure `Round_robin_order_assigns_shuttles_in_turn` **and** `EndToEndTests` fail |
| — | Restore all | — | **GREEN** — pure suite 7 `Passed!` (70); integration `Passed! 2/2` |

**Read it back.** The end-to-end test is load-bearing for the `Complete` save (M1), load order (M2), the held task on both the product side (M3a) and the test's own save (M3b), state wire mapping (M5) and the shuttle id the dispatcher forwards (M7). The pure suite is load-bearing for the `Fail` save (M6) and the round-robin forwarding (M7). The C3 fixture fix is proven both ways (M4/M4′): the old ensure step explodes on a stale row exactly as review 08 predicted, and the sentinel step shrugs it off and deletes it.

The M4 sequence is the interesting one, because it exercises the *fixture*, not the product:

```
# old ensure step (LoadAllAsync) + a stray state='legacy_state' row
  System.ArgumentException : Unknown task state wire name 'legacy_state'. (Parameter 'wireName')   ← red, C3 reproduced
# sentinel ensure step (GetAsync("__fixture_probe__")) + the SAME stray row
  Passed!  - Failed: 0, Passed: 2, … FleetManager.Tests.dll                                        ← green
  after the run: SELECT task_id, state FROM fleet_tasks;  →  stale-probe is gone
```

---

## Substance checks

### The dispatcher is the shape record 15a §2 pinned, and the guard/save order is real

`TaskDispatcher.cs` is 41 lines. The constructor takes the board and the repository and holds them (`:5-8`). Every mutating method is the same three steps — find (`AssignNextAsync` uses `_board.NextQueued()` at `:12`; the others use `_board.Get(taskId)` at `:23,30,37`), guard (`task.Assign/Start/Complete/Fail` at `:16,24,31,38`), save (`await _repository.SaveAsync(task, cancellationToken)` at `:17,25,32,39`). The save is the last statement in each method, so a rejected transition throws at the guard and never reaches the save. `AssignNextAsync` returns `null` before touching the repository when nothing is queued (`:13-14`), and the pure test proves that path saves nothing (`TaskDispatcherTests.cs:39-40`, `SaveCount == 0`).

The purity boundary holds: no `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep` (Phase 2, exit 1), and no `Npgsql`/`MQTTnet`/`Messaging`/`Contracts` token (Phase 2, exit 1). `now` is a parameter on all four methods; time is an input (record 06), so the tests are deterministic. `FailAsync` is implemented (`:35-40`) and its persistence is guarded by the pure test (M6).

### Test quality — the six pure tests have real, non-vacuous assertions

- `AssignNextAsync_assigns_the_oldest_queued_task_and_saves_it` (`:10-27`) seeds two tasks at different times, asserts the first is chosen (`:23`), the shuttle is stored (`:24`), exactly one save happened (`:25`), and the repository can fetch it (`:26`). Removing the save or reordering the board would fail it.
- `AssignNextAsync_returns_null_when_nothing_is_queued` (`:30-41`) asserts `null` **and** `SaveCount == 0`, so the empty path cannot silently write.
- `A_rejected_transition_throws_and_saves_nothing` (`:44-60`) assigns, snapshots the save count (`:53`), asserts `CompleteAsync` throws `TaskRejectedException` from `Assigned` (`:55-56`), then asserts the save count did not change (`:58`) and the state is still `Assigned` (`:59`). This is the "a refused move writes nothing" rule, tested directly.
- `Round_robin_order_assigns_shuttles_in_turn` (`:63-86`) sees the SH-01/02/03 pattern — see the caveat in Findings 3.
- `Reload_preserves_fifo_order` (`:89-110`) saves three tasks, rebuilds a board, and asserts the count, the id sequence, and `NextQueued() == task-1`.
- `FailAsync_persists_the_failure` (`:113-129`) asserts the repository now holds a `Failed` task with the reason and `SaveCount == 1` (M6 proves it is load-bearing).

`InMemoryFleetTaskRepository` is a list-backed double with **upsert** semantics (`:36-45` finds by id, replaces in place, else appends), so insertion order survives re-saves; `LoadAllAsync` returns list order (`:17-20`); `GetAsync` returns by id (`:11-15`); and `SaveCount` increments per saved task (`:44`), which is the counter the "saves nothing" tests depend on.

### The end-to-end test proves the whole M3 sentence, and it genuinely crosses the DB boundary

Phase A (`EndToEndTests.cs:17-43`) builds nine tasks with monotonic **whole-second** `CreatedAt` values, drives `SH-01/02/03` by `index % 3`, and calls `AssignNextAsync` → `StartAsync` → `CompleteAsync` through the dispatcher, so all 27 transitions plus the held task are persisted. It asserts all nine are `Done` (`:45`), each `AssignedShuttleId` matches round-robin (`:46-47`), and `NextQueued()` is null — the assignable queue is empty (`:48`). The `psql` readback above is the independent witness. Phase B (`:55-74`) constructs a **new** `PostgresFleetTaskRepository` and a **new** `TaskBoard`, reloads with `LoadAllAsync`, and asserts ten rows, the id sequence, and all five fields per task including `CreatedAt` equality (`:69`) and `NextQueued() == task-hold` (`:74`). The whole-second values are deliberate: `FleetTask` truncates to microseconds (`FleetTask.cs:87-88`), so `Assert.Equal` on `DateTime` holds (review 08 C1 stays resolved).

### `TestDatabase` matches record 14 §6 and closes C3 without parsing rows

`BuildConnectionString` (`:9-33`) returns the whole `SHUTTLE_DB_CONNECTION_STRING` when set, otherwise assembles from `SHUTTLE_DB_HOST`/`SHUTTLE_DB_USER`/`SHUTTLE_DB_PASSWORD`/`SHUTTLE_DB_NAME` with local defaults, and parses `DB_HOST_PORT` with a fail-fast `InvalidOperationException` (`:26-27`). `ResetAsync` (`:35-43`) calls `repository.GetAsync(SentinelTaskId, …)` first — every repository call ensures the table, and a missing sentinel id returns no rows, so no `state` string is parsed — then opens a fresh connection and runs `DELETE FROM fleet_tasks`. Mutation M4 shows the sentinel form survives a `legacy_state` row that the old `LoadAllAsync` form choked on. This is exactly the fix record 15a §4 promised.

### `DatabaseCollection` and the commit scope

`DatabaseCollection.cs:3` is `[CollectionDefinition("Database", DisableParallelization = true)]`, and both `RestartSurvivalTests` (`:6`) and `EndToEndTests` (`:6`) carry `[Collection("Database")]`; both ran green together in Phase 4. The commit `663bf83` touches only the seven `src/` files the lesson names plus the review doc — no `Program.cs`, no `FleetCore`, no Dockerfile, no contract, no `.csproj`, no solution change (14 projects before and after). There is no review-08-S2-style churn.

### Purity boundaries unchanged

`FleetCore.csproj` has 0 `ProjectReference`/`PackageReference`; `FleetCore.Tests.csproj` has no `FleetManager` token; both Phase 2 greps are exit 1. `FleetCore` and `FleetCore.Tests` are untouched by the commit.

### Close reading of `TaskDispatcher.cs` (the only product file, 41 lines)

The whole product deliverable of this slice is 34 non-blank lines. Read it against what each line is *for* and what would break it:

```csharp
 5: public sealed class TaskDispatcher (TaskBoard board, IFleetTaskRepository repository)
 7:     private readonly TaskBoard _board = board;
 8:     private readonly IFleetTaskRepository _repository = repository;
```

- `sealed` is right: nothing should subclass the owner of the board, and sealing documents that.
- The explicit `_board`/`_repository` fields are **redundant in C# 12** — a class primary constructor's parameters can be used directly in methods — but assigning them to `readonly` `_camelCase` fields is a defensible house-style choice (the `dotnet-style` convention wants `_camelCase` private fields, and it makes the capture explicit). Be ready to explain why both exist; an interviewer will ask.

```csharp
10:     public async Task<FleetTask?> AssignNextAsync(string shuttleId, DateTime now, CancellationToken cancellationToken)
12:         var task = _board.NextQueued();
13:         if (task is null)
14:             return null;
16:         task.Assign(shuttleId, now);
17:         await _repository.SaveAsync(task, cancellationToken);
18:         return task;
```

- `Task<FleetTask?>` is the honest signature: "asynchronous, and the answer may be no task". Returning `null` at `:14` happens **before** any repository call, so the empty path costs nothing and writes nothing (mutation-proven by the `SaveCount == 0` test).
- `:16-17` are the load-bearing pair, and they are the slice's central design decision: the guard (`FleetTask.Assign`) throws on an illegal move, so the save at `:17` is unreachable on rejection. This is what makes "a refused move writes nothing" true rather than hoped. The reverse order would be a bug.
- `:16` mutates the in-memory board *before* the `await` at `:17`. That ordering is the root of Finding 1: if the save throws or the token cancels between the two lines, the board is ahead of the database. The lesson says the order becomes save-then-send when a command is actually sent; here there is no send yet, so this is the earliest the divergence could bite.

```csharp
21:     public async Task StartAsync(string taskId, DateTime now, CancellationToken cancellationToken)
23:         var task = _board.Get(taskId);
24:         task.Start(now);
25:         await _repository.SaveAsync(task, cancellationToken);
28/30/31/32:  CompleteAsync      (same three steps)
35/37/38/39:  FailAsync          (same three steps, `failureReason`)
```

- The three lifecycle transitions (`StartAsync`, `CompleteAsync`, `FailAsync`) are the same `find → guard → save` shape repeated; the duplication is deliberate and readable at four short methods. If M4/M5 add a pre-condition (a lane check) or a post-step (an outbox write) they will be tempted to extract a private helper — do it then, not preemptively.
- `FailAsync` (`:35`) is present now even though nothing calls it in this slice. Lesson 09 justifies it: the dispatcher owns the whole lifecycle and M5's recovery will call it. Its persistence is proven by the pure test (M6). Keeping it is the "no throwaway work" rule in action.
- No line logs anything. Every transition is invisible outside the database; see the Configuration/logging dimension.

The file has no clock (all `now` are parameters), no `Guid.NewGuid` (ids are caller-supplied), no `Task.Delay`/`Thread.Sleep`, and no `Npgsql`/`MQTTnet`/`Messaging`/`Contracts` token (Phase 2, exit 1). That is why the same class is exercised by a pure in-memory test and a live-PostgreSQL test without changing a character.

---

## Round 2 — independent re-verification (same commit, 2026-10-12)

Round 1 established that every lesson phase is green and that the tests are load-bearing. Round 2 re-runs the claims a review should not take on trust, and specifically the things the lesson's own verification table left to "the reviewer": the precedence and fail-fast behaviour of the now-centralised connection-string helper (record 14 §6), the *reason* the no-docker-start requirement exists (record 14 §2), the stability of the shared-table collection under repeated runs, and the exact whole-suite counts. All were run on the pristine `663bf83` tree, database up where the test needs it.

```
# A. whole SHUTTLE_DB_CONNECTION_STRING must win over a bogus SHUTTLE_DB_HOST (record 14 §6)
$ SHUTTLE_DB_CONNECTION_STRING="Host=127.0.0.1;Port=5432;Username=shuttle;Password=shuttle;Database=shuttle" \
  SHUTTLE_DB_HOST="127.0.0.99" \
  dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v q --filter "Category=Integration"
  Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 369 ms - FleetManager.Tests.dll (net10.0)   RC 0
  ← the bogus host is ignored; the whole string is used verbatim

# B. a malformed DB_HOST_PORT must fail fast, not fall back to 5432 (record 14 §6)
$ DB_HOST_PORT="not-a-port" \
  dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --filter "Category=Integration"
  System.InvalidOperationException : DB_HOST_PORT must be a valid integer.
    Failed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks   RC 1
  ← both DB tests fail loudly on the misconfiguration instead of silently using the wrong port

# C. the shared Database collection must serialise and stay stable across repeated runs
$ for i in 1 2 3; do dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v q --filter "Category=Integration" | tail -1; done
  Passed!  - Failed: 0, Passed: 2, … 567 ms
  Passed!  - Failed: 0, Passed: 2, … 489 ms
  Passed!  - Failed: 0, Passed: 2, … 513 ms
  ← three clean runs back to back; reset + non-parallel collection hold

# D. the build image has no docker binary — why "the test must not start docker" is not a style rule
$ docker run --rm shuttle-fleet-build which docker        RC 1   (no docker)
$ docker run --rm shuttle-fleet-build which dotnet        RC 0   (/usr/bin/dotnet)
  ← a test that shelled out to `docker` would fail in the container even if it passed on the host
```

Read it back:

- **A** is the precedence rule working as promised: the whole connection string is authoritative, so a developer's stray `SHUTTLE_DB_HOST` cannot silently point the suite at another database. This is the same behaviour review 08 verified on `RestartSurvivalTests` before the helper existed; it now lives in `TestDatabase.BuildConnectionString` (`:11-14`) and the owner's centralised version preserves it.
- **B** is the fail-fast rule: a bad `DB_HOST_PORT` throws `InvalidOperationException` before any connection is attempted, and the tests fail rather than quietly defaulting to 5432. The message (`must be a valid integer`) is the one record 14 §6 pins.
- **C** is the isolation requirement made empirical: three consecutive runs of the two DB tests, each of which drops and recreates rows in the one shared `fleet_tasks` table, stay green. If the `[Collection("Database")]` / `DisableParallelization = true` pairing were missing, the reset of one test would race the other's rows; repeated runs are exactly where that shows up. One run proves little; three in a row is the cheap version of "not a fluke".
- **D** closes the loop on the no-docker-start requirement: it is not a style preference. The build container has `dotnet` but no `docker` binary, so any `Process.Start("docker", …)` in a test is a guaranteed container failure. That is why record 14 §2 puts DB start-up outside the tests, and why the Phase 6 no-docker grep (and its L1 widening) is a real gate.

Whole-suite counts re-confirmed in Round 2: DB-less `dotnet test ShuttleFleet.slnx --filter "Category!=Integration"` = 7 `Passed!` lines, 70 tests, `FleetManager.Tests` 11; with the database up, `FleetManager.Tests` = 13 and the suite = 72. These match Round 1 and the lesson's "Proof details".

### Grade of the superseded review, claim by claim

Every substantive claim the thin review made, checked against this document:

| Claim in the thin review | Verdict now | Where |
|---|---|---|
| build passes | correct but unsupported | Phase 3 (RC 0, 0 warnings/errors) |
| "70 tests across 7 test assemblies" | correct | Phase 3 counted (`4+18+22+8+2+11+5`) |
| both integration tests run | correct but unsupported | Phase 4 (`Passed: 2`, zero skipped) |
| container green | correct but unsupported | Phase 5 (RC 0, 7 `Passed!`) |
| "No implementation findings" | **not supportable as written** | 5 considerations + 2 lesson defects exist; none is a blocker, but "none" was asserted without reading the code |
| "M3 remains in progress" | superseded | done-when met; README row updated to `done` |

So the thin review was not *wrong* on the facts it stated; it was unbacked, and it under-reported. A review that lists zero findings and shows zero commands is indistinguishable from a guess, which is why this document replaces it rather than appending to it.

---

## Findings (top issues, ranked)

**1. CONSIDER — the dispatcher is single-caller only, and it mutates the board before it saves.** `TaskDispatcher` has no lock and no single-owner discipline. `AssignNextAsync` reads `NextQueued()`, mutates in memory (`task.Assign`), then awaits the save. Two concurrent callers (the deferred run loop handling two MQTT messages, or M4's traffic gate plus an assignment) can interleave: both read the same head task, and the second `Assign` throws `TaskRejectedException` — safe (no duplicate assignment) but noisy, and the caller must treat the throw as "someone else took it". Worse for durability: if `SaveAsync` throws (database down), the in-memory board is already ahead of the database, and nothing rolls it back (`TaskDispatcher.cs:31-32`). Why it matters for a real shuttle system: the fleet manager will be concurrent once the run loop lands, and M5 recovery depends on the saved state showing exactly one place for every pallet. Recommendation for the run-loop lesson: own the board from one dispatch loop (or a `SemaphoreSlim`) and reconcile the board from the database on startup; keep the repository the single write point, as the lesson already says.

**2. CONSIDER — the in-memory fake stores task references, so the pure tests prove a save *happened*, not that the *post-transition snapshot* was written.** `InMemoryFleetTaskRepository.Upsert` (`:36-45`) puts the same `FleetTask` object into its list. `FailAsync_persists_the_failure` fetches that same reference and asserts its state — which would also pass if the dispatcher saved *before* mutating, because the object mutates after the save anyway. The end-to-end test over the real `PostgresFleetTaskRepository` does serialize the row and does prove the post-transition state round-trips (M1 is exactly that), so the product is covered. But the pure fake is a weaker double than it looks. If you want the pure suite to catch a save-before-mutate regression, have the fake snapshot the task's fields on `SaveAsync` (or count saves with a captured copy) rather than storing the live object. Worth an explain-it answer; not a gate at M3.

**3. CONSIDER — the round-robin test computes its expected value with the same expression it uses to choose the shuttle.** `TaskDispatcherTests.cs:76-84` picks `shuttleIds[index % shuttleIds.Length]` to call the dispatcher and then asserts `shuttleIds[index % shuttleIds.Length]` equals the result. The assertion is not vacuous — mutation M7 (dispatcher stores `"SH-99"`) turns it red — but it proves the dispatcher forwards the argument, not that the policy produces `SH-01, SH-02, SH-03, SH-01, …` independently. Since record 15 makes the policy caller-side, that is acceptable, but a stronger test hard-codes the expected sequence (`"SH-01","SH-02","SH-03","SH-01",…`) or derives it by a different means, so a change to the formula cannot move both sides together. The end-to-end test does assert the exact per-position shuttle, which partly covers this.

**4. CONSIDER — `TestDatabase.ResetAsync` re-derives the connection string for the `DELETE` instead of using the repository's.** `:39` calls `BuildConnectionString()` again to open the connection that deletes the rows, rather than taking the connection string (or the repository's connection) as an input. Today every caller builds both from the same `BuildConnectionString()` value, so they cannot diverge — but the helper's contract silently assumes that, and a future caller that constructs a repository against a different database would ensure/delete against the wrong one. Minor; a cleaner shape takes the connection string once, or resets through a repository method.

**5. CONSIDER (style) — a primary-constructor spacing nit and a parameter name that drifts from the pinned signature.** `TaskDispatcher.cs:5` writes `public sealed class TaskDispatcher (TaskBoard board, …)` — a space before `(` that C# allows but formatters remove. `FailAsync` names its second parameter `failureReason` (`:35`) where lesson 09 R1 and record 15a §2 pin `reason`. Neither is a behavioural problem — the acceptance greps only method presence, and the parameter is positional — but the pinned API is the contract later lessons call, so keep the spelling or deliberately amend the record. See also the study list.

---

## Review dimensions (the "what to judge" pass)

The findings above are the issues; this is the systematic scan behind them, one line per required dimension, so nothing was skipped by only looking at what stood out.

**Messaging — N/A this slice, and deliberately so.** Lesson 09 introduces no wire message: `docs/contract/v1.md`/`v2.md` are `Version: 1`/`Version: 2`, there is no `v3.md`, and `grep -rniE 'FleetTask|TaskState' src/Contracts` is empty (Phase 7). That is correct, not an omission — record 11 keeps a task internal at M3, and mapping a task to a `moveCommand` is explicitly deferred (lesson 09 "Not in this assignment"). The `TaskDispatcher` has no `MQTTnet`/`Messaging` token (Phase 2), so it cannot accidentally send. When the deferred run loop does map a task to a command, the messaging-contract rules (envelope version, `messageId`, correlated ack, retry with the *same* id) apply to *that* code, not to this one. There is nothing to get wrong here yet, and nothing was smuggled in.

**State — explicit and durable, with one ordering weakness.** The lifecycle is a real state machine: `FleetTask.State` is `private set` and only `Assign/Start/Complete/Fail` move it, throwing `TaskRejectedException` on an illegal move (lesson 09 §1; verified in review 08 and in the toy). Every transition is persisted, and the restart proof reloads through a *new* board and a *new* repository, so survival depends only on rows. Writes are per-task upserts (`ON CONFLICT (task_id) DO UPDATE`), so a mid-shift crash leaves every task at its last *complete* transition — there is no half-written row and no cross-task partial state, because each task is written independently. The weakness is the in-memory board being mutated *before* the save (`TaskDispatcher.cs:31-32`): if the save throws, the board is ahead of the database (Finding 1). This is the exact divergence M5 recovery must not inherit. There is no transaction spanning multiple rows, which is fine while tasks are independent — but M4's lane reservation plus a task write will be the first case where two writes must move together, and that is where an explicit ordering or a transaction has to appear.

**Concurrency — correct async plumbing, no shared-state protection.** `async`/`await` is used correctly throughout; every method returns `Task`/`Task<T>`, the `CancellationToken` is threaded to `await _repository.SaveAsync(task, cancellationToken)` (`:17,25,32,39`), and nothing blocks. But `TaskDispatcher` has no lock and assumes one caller at a time (Finding 1). Two callers would both read `NextQueued()`; the second `Assign` throws rather than double-assigning, which is safe but relies on all callers treating the throw as "someone else took it". `TaskBoard` and the fake list are not thread-safe; the two DB tests are protected by the non-parallel collection (Phase 4, Round 2-C), but that protects the *tests*, not the product. A subtler race is cancellation-after-mutation: cancel the `CancellationToken` between `:31` and `:32` and the row is never written, leaving the same board/database split as a failed save. There is no timer in this slice, so message-vs-timer races are a later concern — but the run loop is the first thing that will call this concurrently, and the fix belongs in that lesson, not a retro-fit here.

**Separation — strong, and it is the point of the slice.** The dispatcher is app-layer (`FleetManager`), holds `IFleetTaskRepository`, names shuttle ids, and never touches `FleetCore`'s purity: `FleetCore.csproj` has 0 `ProjectReference`/`PackageReference`, `FleetCore.Tests` has no `FleetManager` token (Phase 2), and the commit touches neither. Decision logic is injectable: the pure tests drive the dispatcher with a fake repository, no broker and no database. One thing to be honest about: the *assignment policy* (round-robin) is not in the product at all — record 15 makes the caller own `shuttleId`, so the dispatcher only forwards it (which is why Finding 3's test can only prove forwarding). That is a deliberate separation, and it is exactly where M4's traffic gate and M5's reassignment will plug in without changing `TaskDispatcher`'s shape. The lesson says so ("only the caller that picks `shuttleId` does").

**Configuration and logging — configuration good; logging absent.** No hard-coded connection string: the tests read `SHUTTLE_DB_CONNECTION_STRING` or the `SHUTTLE_DB_*`/`DB_HOST_PORT` parts (Phase 6; Round 2-A/B). The three shuttle ids and nine task ids are *test fixtures* with fixed values so the test is deterministic, which the lesson defends and which is correct — they are not product configuration. What is missing is logging: `TaskDispatcher` writes nothing, and no transition is logged anywhere. At M3 the tests are the witness, so this is not a gate, but for a real fleet manager a support engineer reconstructing a pallet's path needs "task X assigned to SH-0n at T", "task X done at T", and (M5) "task X failed: reason". Consider adding structured transition logging in the run-loop lesson; the dispatcher is the natural place, and a logged `(taskId, shuttleId, from, to, now)` line costs little and pays back in support effort.

**Failure behavior — FailAsync is real and tested; the DB-down path is unexplored by design.** `FailAsync` exists, persists (M6 turns its absence red), and keeps `Failed` terminal (record 11). The lesson's stretch goal — prove a *failed* task survives restart — was not attempted; optional, and `RestartSurvivalTests` already proves the `Failed` state round-trips, so the gap is small. The database-down case is not tested: if Postgres is unreachable, `SaveAsync` throws and the exception propagates out of the dispatcher to the caller (there is no catch, which is right — swallowing it would hide the split). The board is left ahead (Finding 1). Broker-down and WMS-down do not arise in this slice because neither is wired. So the failure surface here is "the save can throw and the caller must decide", and the decision is deferred to M5 recovery. That is acceptable for M3; it is written down here so M5 does not discover it as a surprise.

**Language quality — idiomatic, with three cosmetic items.** `dotnet-style` reads clean: `sealed` class, primary constructor, `readonly` fields, `Async` suffixes, `CancellationToken` on every async method, nullable `Task<FleetTask?>` for "may be no task", the guard exception left to propagate rather than caught-and-rewrapped. Naming is right (`AssignNextAsync`, `_board`, `_repository`). The items are cosmetic: the space before `(` in the primary constructor (`TaskDispatcher.cs:5`), the `failureReason`-vs-`reason` parameter name (`:35`), and the missing trailing newline on every file this commit touches (Finding 5 and the study list). The fake repository's `Upsert` is a linear scan — correct and fine for a test double at ten rows, not a performance concern. No `IDisposable` to leak: the dispatcher holds no unmanaged resource, and the repository's connections are opened and closed inside each call.

---

## Lesson defects (for the professor)

Lesson 09 and record 15a are buildable as written: the pinned names are consistent between the lesson, the hints, and record 15a (the only drift is the unenforced `reason` parameter name, Finding 5); every phase ran to the expected exit code; the counts (70 pure, `FleetManager` 11 DB-less / 13 with DB) matched the lesson's "Proof details"; the two-gate mechanism reuses record 14 exactly. The three concepts are each taught in their own section, the four assignment requirements are declared as requirements rather than concepts, and nothing in the concept list is untaught. The defects below are gate-precision nits — fix them so the acceptance command tests the behaviour it names, per review 08 L3.

**L1 — the Phase 6 no-docker grep only scans `EndToEndTests.cs`, but this lesson added a second test-side helper.** Phase 6 runs the (correctly tightened) pattern `Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))` against `src/FleetManager.Tests/EndToEndTests.cs` only. Lesson 09 introduces `TestDatabase.cs`, a new test-side file that opens connections — the exact class of place where review 08's S3 ("dead docker-auto-start scaffolding in the test helper") lived. A `Process.Start("docker", …)` in `TestDatabase.cs` would pass the gate. Widen the check to `src/FleetManager.Tests/*.cs` (or at least `EndToEndTests.cs` **and** `TestDatabase.cs`).

**L2 — the Phase 6 hygiene checks test word presence, not the requirement.** `grep -qE 'GetAsync' TestDatabase.cs` passes if `GetAsync` appears anywhere; the requirement is "ensure through the sentinel id, and do **not** use `LoadAllAsync` as the ensure step". A stronger gate checks `SentinelTaskId` is the argument and that `LoadAllAsync` is absent from `ResetAsync`. Likewise, the collection gate checks `[Collection("Database")]` on `EndToEndTests.cs` and the definition, but not on `RestartSurvivalTests.cs` — yet *both* classes joining the collection is the whole isolation requirement (the owner did add it, so this is a gate gap, not a failure). And Phase 3 says "expect … 7 `Passed!` lines" in a comment but does not grep the counts (64→70, `FleetManager` 5→11), so a regression that dropped tests while staying green would not be caught. Tighten the three greps.

**Concepts check.** Required concepts: the dispatcher, round-robin assignment, the end-to-end proof — three, each with its own section, within the cap. Assignment requirements: the `Integration` trait + two-gate filter, the config-driven connection string, the sentinel fixture hygiene (closes review 08 C3), and the non-parallel DB collection — all four declared as requirements and specified. No concept in the list is untaught, and the lesson stayed inside its limit. The lesson's own "Verification table" correctly marked the implementation "not yet verified" and left it to the reviewer.

---

## Explain-it questions — required before the milestone is fully closed

These do not change the pass verdict (all gates are green), but M3 is not fully closed until you can answer them out loud. They mix "why", "what would break", and "study the code". **The three an interviewer is most likely to ask are Q1 (where the dispatcher lives), Q3 (mutate-then-save and the failed save) and Q5 (the reference-holding fake); questions 2, 4, 6, 7 and 8 are additional study questions.** None is answered here on purpose.

1. `TaskDispatcher` lives in `FleetManager`, not `FleetCore` (`TaskDispatcher.cs:1-5`). What would `FleetCore` have to learn about the world if the dispatcher moved there, and which decision record forbids it? How is this the same split as `ShuttleSim` driving `ShuttleCore`?
2. Walk the three steps in `CompleteAsync` (`TaskDispatcher.cs:28-33`). Which line throws on a task that is still `Queued`, and why is `SaveAsync` never reached? Then read `TaskDispatcherTests.cs:53-59` and explain exactly what the save-count assertion proves that a state assertion alone would not.
3. In `CompleteAsync` the board is mutated at `:31` and saved at `:32`. If the database is down and the save throws, what is the board's state, what is the database's state, and what must the deferred run loop do about it? The lesson says the order "becomes save-then-send" later — why, and what does that have to do with the repository being the single write point?
4. `Round_robin_order_assigns_shuttles_in_turn` (`TaskDispatcherTests.cs:63-86`) computes the expected shuttle with `index % shuttleIds.Length` on both sides. In what sense is the assertion still real, and how would you rewrite it so the expectation is independent of the formula? (Mutation M7 is the clue.)
5. `InMemoryFleetTaskRepository.Upsert` (`:36-45`) stores the live `FleetTask` reference. Why can `FailAsync_persists_the_failure` pass even if the dispatcher saved *before* mutating? What would you change in the fake to make it prove the post-transition snapshot was written, and why does the end-to-end test not have this weakness?
6. The restart uses a **new** `PostgresFleetTaskRepository` and a **new** `TaskBoard` (`EndToEndTests.cs:55-59`). Why does reusing the old board prove nothing, and what specifically does the fresh instance force the data through? Then explain why `task-hold` is added (`:50-53`) and what `NextQueued()` would return without it.
7. `TestDatabase.ResetAsync` calls `GetAsync(SentinelTaskId, …)` before the `DELETE` (`:35-43`). Why does the sentinel `GetAsync` parse no rows, and what exact failure did the old `LoadAllAsync` ensure step produce when a `state='legacy_state'` row was present (mutation M4)? Why is `LoadAllAsync` the wrong ensure step?
8. Both DB classes carry `[Collection("Database")]` and the collection is defined `DisableParallelization = true` (`DatabaseCollection.cs:3`). What do the two classes do to the same table, what goes wrong if xUnit runs them in parallel, and why is a per-class table name or a global `xunit.runner.json` setting a worse fix?

### Lines or blocks that look unclear, copied, or unexplained

- `src/FleetManager/TaskDispatcher.cs:5-8` — the primary-constructor syntax with a space before `(` and the explicit `_board`/`_repository` fields. Be ready to say whether the fields are redundant with the primary-constructor parameters and why you kept them `readonly`.
- `src/FleetManager/TaskDispatcher.cs:35` — `failureReason` where the pinned API says `reason`. Say why the name differs and whether the public contract cares.
- `src/FleetManager.Tests/TaskDispatcherTests.cs:76-84` — the expected/actual both use `index % shuttleIds.Length`; see question 4.
- `src/FleetManager.Tests/InMemoryFleetTaskRepository.cs:36-45` — `Upsert` stores the reference; see question 5.
- `src/FleetManager.Tests/TestDatabase.cs:39` — the `DELETE` connection re-derives `BuildConnectionString()` rather than using the repository's connection; say when those could differ (Finding 4).
- `src/FleetManager.Tests/EndToEndTests.cs:50` — the held task is created at `now` (`start.AddHours(1).AddSeconds(27)`), i.e. after all nine tasks; explain why its `CreatedAt` position matters to the reload and to `NextQueued()`.
- All seven files this commit touches end without a trailing newline (`git show 663bf83:<file> | wc -l` is one less than the line count from `git show … | grep -c ''`). That is an editor/`.editorconfig` habit, not a logic issue; a formatter pass would fix it, as it would the `TaskDispatcher (…)` spacing.

---

## Revisit list

- **Concurrent access to the dispatcher and the board.** Protect shared state with a single owner (one dispatch loop) or a lock, and decide what a duplicate claim should return instead of throwing. This is fleet-control's "protect shared state; prefer a single owner" and the run loop will force it.
- **Save ordering and reconciliation.** Mutate-then-save leaves the board ahead of the database when the save fails; the run loop must save-then-act and reconcile from the database on startup. Record 15a already flags save-then-send — make it concrete in the run-loop lesson.
- **Test doubles should snapshot.** A persistence fake that stores live references cannot catch save-before-mutate. Make the fake copy fields on save.
- **Independent expected values in tests.** Do not compute the expected result with the same expression that drove the actual call; hard-code or derive it differently.
- **Fixture hygiene (C3) is closed here — keep it.** The sentinel-ensure-then-delete pattern is now the template for every DB-backed test. Do not reintroduce `LoadAllAsync` as an ensure step.
- **Next:** M4 traffic control — reserve lane segments before `AssignNextAsync` hands out a task, and write the deadlock rule down in a decision record. The dispatcher's shape should not change.

---

## End-product check

This slice is the working heart of M3 and it is the right shape. `TaskDispatcher` is the app-layer owner of the pure `FleetCore` board: it names shuttles, holds the repository, persists every lifecycle transition after the guard, and never reads a clock or generates an id. The end-to-end test does the real thing — nine tasks finished by three shuttles in round-robin, every state change written to PostgreSQL, then a fresh board and a fresh repository rebuilding all ten rows from the database with FIFO order and the one held task still queued — and the nine mutation rows (seven mutations plus the M4/M4′ control pair) prove the test is not paper. The fresh container is an honest DB-less gate: it excludes the two `Integration` tests by trait and exits 0, while a with-DB run proves they truly executed. M3's done-when is met end to end. The risk this slice carries forward is concurrency: the dispatcher is not safe to call from more than one place at once, and its in-memory board can outrun a failed save. That is fine for the single-caller test loop the lesson pins, but the deferred run loop and M4's traffic gate are the first things that will call it concurrently, and the in-memory-vs-database divergence is exactly what M5 recovery must not inherit. Get the single-owner discipline and the save ordering right in the run-loop lesson, before M5 depends on it.

---

## What later milestones will demand of this slice

The reviewer's job is to avoid approving something that will not hold up. Here is each upcoming milestone's first ask of this code, and whether this slice is ready.

| Milestone | First ask of this code | Ready? |
|---|---|---|
| deferred run loop | call `AssignNextAsync` once per idle shuttle, map a task to a `moveCommand`, save before send | shape holds; the **save-then-send** reorder and the single-owner rule are the two things to add here (Findings 1–2) |
| M4 — traffic control | reserve lane segments *before* `AssignNextAsync` hands out a task; two shuttles must not share space | the assignment policy is already caller-side, so the gate plugs in without touching `TaskDispatcher`; but the dispatcher is not concurrency-safe, so two shuttles in flight is the first caller that can trip Finding 1 |
| M5 — faults and recovery | call `FailAsync`, reassign, reconcile from the database on restart, guarantee exactly one place per pallet | `FailAsync` is present and tested; the board-ahead-of-database divergence (Finding 1) is exactly what "exactly one place per pallet" must not inherit; multi-record changes (task + slot) will need a transaction (fleet-control "Persistence") |
| M6 — WMS integration | tasks arrive from the WMS, often duplicated or late; idempotent task creation | not this slice, but the internal task model does not leak into the contract (Phase 7), so the WMS can map onto it; idempotency keys are a contract concern, not a `FleetTask` one |
| M7 — dashboard | read task/fleet state, show a pallet's path, tune thresholds | `TaskDispatcher` logs nothing, so a dashboard/support engineer has only the database rows; structured transition logging belongs in the run-loop lesson |
| persistence/restart (already M3) | rebuild in-memory state from saved state | Phase B proves it for this slice; the reconcile-vs-shuttles-report step is M5's |

Nothing here is a blocker for M3; the table exists so the run-loop lesson is written knowing what it must add.

---

## README

**Updated.** The verdict is a clean pass and the M3 done-when is met (three shuttles finish a queue, state survives a restart), so the M3 row moves from `in progress` to **`done`**, with the verifying commands noted under the table. No other row or section of `README.md` was touched.

---

## What the owner must do next

1. Answer the explain-it questions out loud before M3 is treated as fully closed — the three an interviewer would ask are Q1, Q3 and Q5. Question 3 (save failure and board/database divergence) and question 5 (the reference-holding fake) are the ones they will push hardest on.
2. Carry Findings 1 and 2 into the run-loop/M4 lesson: give the board a single owner and make save ordering explicit; consider snapshots in the fake.
3. No code changes are required to close M3; the five findings are considerations for later slices, not defects in this one.
4. Professor: tighten the Phase 6 no-docker grep to cover `TestDatabase.cs`, check the sentinel argument (not just the word `GetAsync`), gate `[Collection("Database")]` on both DB classes, and grep the test counts (L1/L2).

---

## Reproduce this review

The non-mutating gates are cheap to re-run and every claim above comes from them. From the repo root, against `663bf83`:

```bash
# toolchain
dotnet --version                       # expect 10.0.112
docker --version                       # expect Docker 29.x

# DB-less path (what the build container runs)
cd src
dotnet build                                                     # expect exit 0, 0 warnings, 0 errors
dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
                                                                 # expect RC 0, 7 "Passed!" lines, 70 tests, FleetManager 11
dotnet test ShuttleFleet.slnx --list-tests | grep -E 'FailAsync_persists_the_failure|Round_robin_order_assigns_shuttles_in_turn'
                                                                 # expect both names present

# with-DB path (the two integration tests really execute)
cd ..
docker compose -f docker/compose.yml up -d database
cd src
dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m \
  --logger "console;verbosity=detailed" --filter "Category=Integration"
                                                                 # expect RC 0, "Passed: 2", zero skipped
cd ..
docker compose -f docker/compose.yml down

# fresh container (no database, integration excluded)
docker build -f docker/Dockerfile.build -t shuttle-fleet-build .
docker run --rm shuttle-fleet-build                              # expect RC 0, 7 "Passed!" lines, FleetManager 11
```

The mutation table is not reproduced by a one-liner: it requires a throwaway `git archive HEAD` copy, applying each mutation by hand, running the DB-backed test against a live database, then restoring and re-running. Section "Mutation testing" records the file and line of each mutation and the exact assertion that fired, so any of them can be replayed. Do **not** apply mutations in the working repo.

The config-precedence and fail-fast checks from Round 2 are the two environment overrides in that section (`SHUTTLE_DB_CONNECTION_STRING` wins; a bad `DB_HOST_PORT` throws). They are safe to re-run against the same database.

---

## Cleanup

- `docker compose -f docker/compose.yml down` run after the mutation phase; the database container and the `shuttle-fleet_default` network are removed (`docker ps` shows no `shuttle-fleet` container).
- Throwaway `/tmp/opencode/rev09` and `/tmp/opencode/rev09-pristine` deleted; `/tmp/opencode/mqttinspect` left as found.
- `dotnet build-server shutdown` run (compiler and MSBuild servers shut down).
- No file under `src/` was modified by this review. `docs/reviews/09-three-shuttles-finish-queue.md` and the M3 row/verification note in `README.md` are the only files changed.
- **Process note — an uncommitted working-tree change appeared during the review, not made by me.** At review start `git status` was clean at `663bf83`. During the review, `src/FleetManager.Tests/TaskDispatcherTests.cs` gained an uncommitted edit removing five `// Arrange` comment lines (the committed file has five; the working tree now has none). The change is behaviour-neutral — the test count and results are identical (70 pure tests, 7 `Passed!` on the committed tree in the throwaway copy) — but it is a `src/` edit this review did not make and did not revert, because a reviewer must not touch `src/`. All gates above were run against the committed tree (`663bf83`) and re-verified in the throwaway copy, so the verdict is unaffected. The owner should decide whether the working-tree edit is theirs, and commit or discard it deliberately.
