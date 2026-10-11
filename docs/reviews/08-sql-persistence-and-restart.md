# Review 08: SQL persistence and restart survival (lesson 08, second M3 slice)

**Date:** 2026-10-11
**Reviewed:** commit `009a2cf` (`M3: feat: implement PostgreSQL repository for FleetTask with persistence and retrieval methods` — the owner's implementation), on top of `e2925d0` (lesson 08 + hints + decision record 13) and `a379fd7` (lesson-07 re-review README/review commit). Working tree clean at review start.
**Scope:** lesson 08 against its own assignment **and** [record 13](../decisions/13-fleet-state-persistence.md). M3 stays **in progress** — lesson 09 owns multi-shuttle assignment and the whole-M3 done-when (three shuttles finish a queue, restart end-to-end).
**Contract checked:** `docs/contract/v1.md` `Version: 1`, `docs/contract/v2.md` `Version: 2`, no `v3.md`; no task types in `src/Contracts`. Persistence is internal state only; no wire change.

**Verdict: not yet.**

The persistence design itself is right and the restart proof is real: `IFleetTaskRepository` sits in pure `FleetCore`, the Npgsql implementation sits in `FleetManager`, the schema matches record 13 exactly (wire-name `state`, `created_at ASC, task_id ASC`), hydration is a guarded `internal` constructor exposed only to `FleetManager`, and the test genuinely writes six tasks of five different states to the running Postgres and reloads them into a fresh `TaskBoard`. Three load-bearing mutations (wrong load order, mangled state on load, wrong saved column) all turn that test red. The problem is not the persistence; it is that the new test hard-connects to `127.0.0.1:5432` **unconditionally**, so `dotnet test` fails with no database — and the fresh-container gate (`docker/Dockerfile.build`) runs `dotnet test` in a container with no database. `docker run --rm shuttle-fleet-build` now exits **1**. The spec's definition of done says "Builds in the Docker environment, tests pass"; a failing build/test is an automatic not-yet. M3 stays in progress.

---

## Commands run, verbatim key output

Run from repo root unless stated; exit codes read directly. Mutations ran against a throwaway `git archive HEAD` copy at `/tmp/opencode/rev08`, deleted afterward; no experiment touched `src/`.

### Environment

```
$ dotnet --version                    → 10.0.112
$ docker --version                    → Docker version 29.8.1
$ docker compose -f docker/compose.yml ps
  shuttle-fleet-database-1  postgres:17  Up ...  127.0.0.1:5432->5432/tcp   ← already running at review start
$ docker exec shuttle-fleet-database-1 pg_isready -U shuttle -d shuttle
  /var/run/postgresql:5432 - accepting connections   (exit 0)
```

Note: the `database` service was already up (33 minutes) when the review began — the owner left it running. Closed in [Cleanup](#cleanup).

### Lesson acceptance, Phases 1–3 and 6 (verbatim)

```
# Phase 1
$ test -f src/FleetCore/IFleetTaskRepository.cs                                   exit 0
$ grep -q "InternalsVisibleTo" src/FleetCore/FleetTask.cs                         exit 0
$ grep -q "internal FleetTask" src/FleetCore/FleetTask.cs                         exit 0

# Phase 2
$ grep -rniE 'Npgsql|Postgres|Microsoft.Data.SqlClient' src/FleetCore --include='*.cs' --include='*.csproj'
  (no matches)                                                                    exit 1
$ grep -c 'ProjectReference\|PackageReference' src/FleetCore/FleetCore.csproj     prints 0

# Phase 3
$ grep -rni 'Npgsql' src/FleetManager/FleetManager.csproj | grep PackageReference
  <PackageReference Include="Npgsql" Version="8.0.6" />                            exit 0

# Phase 6
$ grep -qE '^Version: 2$' docs/contract/v2.md                                     exit 0
$ grep -qE '^Version: 1$' docs/contract/v1.md                                     exit 0
$ test ! -f docs/contract/v3.md                                                   exit 0
```

All Phase 1–3 and Phase 6 gates pass.

### Phase 4/5 — build and test, with the database up

```
$ cd src
$ dotnet build                                                                    exit 0, 0 Warning(s), 0 Error(s)
$ dotnet test                                                                     exit 0
Passed!  - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll   (net10.0)
Passed!  - Failed: 0, Passed:  4, … Contracts.Tests.dll     (net10.0)
Passed!  - Failed: 0, Passed:  8, … Messaging.Tests.dll     (net10.0)
Passed!  - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll    (net10.0)
Passed!  - Failed: 0, Passed:  5, … FleetManager.Tests.dll  (net10.0)
Passed!  - Failed: 0, Passed: 19, … FleetCore.Tests.dll     (net10.0)   ← 18 + restart test
7 Passed! lines, 65 tests.

$ dotnet test --list-tests | grep State_survives
FleetCore.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks   FOUND
```

### Phase 4/5 — the same `dotnet test` with the database down

```
$ docker compose -f docker/compose.yml down      (database removed)
$ cd src && dotnet test                                                           exit 1
   Npgsql.NpgsqlException : Failed to connect to 127.0.0.1:5432
   ---- System.Net.Sockets.SocketException : Connection refused
     at Npgsql.NpgsqlConnection.OpenAsync(...)
Passed!  - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll
Passed!  - Failed: 0, Passed:  4, … Contracts.Tests.dll
Passed!  - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll
Passed!  - Failed: 0, Passed:  8, … Messaging.Tests.dll
Passed!  - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll
Failed!  - Failed: 1, Passed: 18, Skipped: 0, Total: 19 - FleetCore.Tests.dll
Passed!  - Failed: 0, Passed:  5, … FleetManager.Tests.dll
```

### Phase 6 — fresh container (the blocker)

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build .                exit 0
$ docker run --rm shuttle-fleet-build                                             exit 1
   Npgsql.NpgsqlException : Failed to connect to 127.0.0.1:5432
   ---- System.Net.Sockets.SocketException : Connection refused
Passed!  - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll
Passed!  - Failed: 0, Passed:  4, … Contracts.Tests.dll
Passed!  - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll
Passed!  - Failed: 0, Passed:  8, … Messaging.Tests.dll
Failed!  - Failed: 1, Passed: 18, Skipped: 0, Total: 19 - FleetCore.Tests.dll
Passed!  - Failed: 0, Passed:  5, … FleetManager.Tests.dll
Passed!  - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll
```

`Dockerfile.build` copies only `global.json` and `src/`, then runs `dotnet test` — no database exists in that container. The DB-backed test is in the default suite, so the M0 gate is red.

### Restart-survival proof, over the real database

The test passed (exit 0, 1/1) and the rows it wrote are real. Read back with `psql`:

```
 task_id      | to_lane |       created_at       |    state    | assigned_shuttle_id | failure_reason
--------------+---------+------------------------+-------------+---------------------+----------------
 task-queued-a    | A01-L01 | 2026-01-01 00:00:00+00 | queued      |                     |
 task-queued-b    | A01-L02 | 2026-01-01 00:01:00+00 | queued      |                     |
 task-assigned    | A01-L03 | 2026-01-01 00:02:00+00 | assigned    | SH-01               |
 task-in-progress | A01-L04 | 2026-01-01 00:03:00+00 | in_progress | SH-02               |
 task-done        | A01-L05 | 2026-01-01 00:04:00+00 | done        | SH-03               |
 task-failed      | A01-L06 | 2026-01-01 00:05:00+00 | failed      |                     | rack empty
(6 rows)
```

### Mutations (throwaway copy, `/tmp/opencode/rev08`)

Baseline of the single restart test in the copy: `Passed! … 1/1` (exit 0).

| Mutation | What changed | Result |
|---|---|---|
| M1 wrong load order | `ORDER BY created_at DESC, task_id ASC` | `Assert.Equal() Failure: Collections differ` — `Failed! 1/1` (exit 1) |
| M2 mangled state on load | `FromWireName`: `"in_progress" => TaskState.Assigned` | `Assert.Equal() Failure: Values differ` — `Failed! 1/1` |
| M3 wrong saved column | `state` parameter bound to `task.ToLane` | `System.ArgumentException : Unknown task state wire name 'A01-L01'` — `Failed! 1/1` |

After restoring the copy: test green again (`Passed! 1/1`). The restart test is load-bearing for ordering, state mapping, and column mapping.

### Regression checks

```
$ dotnet run --project ShuttleSim/ShuttleSim.csproj        exit 0, 7 lines containing "ShuttleId"
$ dotnet run --project FleetManager/FleetManager.csproj    exit 0, "Fleet Manager Coming Online" present
$ grep -n "attempt" src/FleetManager/ReliableCommandSender.cs
  66: Console.Error.WriteLine($"attempt {attempt} of {_policy.MaxAttempts} for command {id}");
$ grep -n "listening\|handled\|published" src/ShuttleSim/Program.cs
  88 LoggingMessageBus; 96 "handled …"; 101 "Shuttle … listening on …"; 153 "published …"
```

M1 demo intact, manager start line intact, review-06 S2 attempt log intact, review-06 L1 serve-mode logging intact. Contract v1/v2 untouched.

---

## Acceptance table

| # | Criterion (lesson / record 13) | Evidence | Result |
|---|---|---|---|
| 1 | Req 1: hydration constructor exists, public ctor unchanged | `FleetTask.cs:79-85`, public ctor still guards blank | met |
| 2 | Req 1: `[assembly: InternalsVisibleTo("FleetManager")]` | `FleetTask.cs:3`; Phase 1 gate exit 0 | met |
| 3 | Req 2: `IFleetTaskRepository` in `FleetCore`, four methods | `IFleetTaskRepository.cs`; Phase 1 gate exit 0 | met |
| 4 | Req 2/6: `FleetCore` has no Npgsql/DB refs | Phase 2 grep exit 1; csproj ref count 0 | met |
| 5 | Req 3: `FleetManager` references Npgsql | `FleetManager.csproj:7` `Npgsql 8.0.6`; Phase 3 exit 0 | met |
| 6 | Req 4: Postgres impl in `FleetManager` | `PostgresFleetTaskRepository.cs`; `ensure` schema matches record 13 | met |
| 7 | Req 4: schema columns/types match record 13 | `CREATE TABLE … fleet_tasks` matches column-for-column; `psql` shows wire-name states | met |
| 8 | Req 4: load ordered `created_at ASC, task_id ASC` | `PostgresFleetTaskRepository.cs:80`; M1 mutation red | met |
| 9 | Req 4: parameterized queries, async, cancellation | `@`-parameters only; `await`/`await using`; ct on every call | met |
| 10 | Req 4: nulls preserved | `DBNull.Value` for null assigned/failure; `IsDBNull` on read; `task-failed` row shows reason, `task-queued` shows NULL | met |
| 11 | Req 4: wire-name mapping both ways | `ToWireName`; `FromWireName`; `psql` shows `in_progress`; M2/M3 red | met |
| 12 | Req 5: restart test persists ≥3 tasks, mixed states | 6 tasks, 5 states; `psql` rows | met |
| 13 | Req 5: new board + reload, fields match | `RestartSurvivalTests.cs:41-66`; test green | met |
| 14 | Req 5: FIFO order preserved, `NextQueued()` correct | `:47-55,68`; M1 red when order flipped | met |
| 15 | Phase 4/5: all suites pass **with DB** | `dotnet test` exit 0, 7 `Passed!`, 65 tests | met |
| 16 | Definition of done: tests pass in the **fresh container** | `docker run --rm shuttle-fleet-build` exit 1 | **NOT MET (B1)** |
| 17 | Phase 6: contract unchanged | v1 `Version: 1`, v2 `Version: 2`, no v3 | met |
| 18 | M3 done-when: state survives restart | restart proof over real DB, mutation-proven | met (this slice) |
| 19 | M3 done-when: queue finished by three shuttles | lesson 09 | not yet |
| 20 | Req 6: pure test project stays pure | `FleetCore.Tests.csproj:23` now references `FleetManager` | **should-fix (S1)** |

**17 met, 1 not met (B1), 1 should-fix (S1), plus the deferred lesson-09 half of M3.** Because a required gate is red, the verdict is not-yet.

---

## Substance checks

### The separation is exactly what record 13 asked for

`IFleetTaskRepository` is a plain interface in `FleetCore` with no DB types. `FleetCore.csproj` has zero `ProjectReference`/`PackageReference`; the Phase 2 purity grep is exit 1. The Npgsql code lives in `FleetManager/PostgresFleetTaskRepository.cs` and takes the connection string by constructor, so the decision logic can be tested with a fake and the app owns config/connection. That is record 13 §4 and AGENTS "decision logic separated from I/O."

### Hydration is a guarded, narrow door

`FleetTask.cs:79` adds the `internal` constructor that accepts the persisted state, and `FleetTask.cs:3` exposes it only to the `FleetManager` assembly. The public constructor is unchanged and still rejects blank ids/lanes. Ordinary code cannot set `State`; the persistence layer can reconstruct saved state; `ReadTask` passes `lastEventAt: null`, matching record 13 §5 ("we do not store `_lastEventAt`"). The test asserts `State`, `AssignedShuttleId`, and `FailureReason` survive, and M2 proves a mis-mapped `state` is caught.

### The restart proof genuinely crosses the DB boundary

`SaveAllAsync` writes through one `NpgsqlConnection` and a transaction, `LoadAllAsync` reads back through a different connection, and the reloaded rows are visible in `psql` with the exact wire names and FIFO order. The assertions compare identity/fields and `NextQueued()`. M1/M2/M3 prove the proof is not vacuous. One nit: the test reuses the **same** `repo` instance for save and load; record 13 §6 says "new `TaskBoard` (or new repository-backed board instance)". Since the repository holds only the connection string and opens a fresh connection per call, the boundary is still crossed and the result is valid — but a fresh repo instance would match the record's wording exactly (C5).

### Npgsql usage against the dotnet-style conventions

Async all the way (`OpenAsync`, `ExecuteReaderAsync`, `ReadAsync`, `ExecuteNonQueryAsync`, `BeginTransactionAsync`, `CommitAsync`), no `.Result`/`.Wait()`; every call takes the caller's `CancellationToken` and passes it to the driver; connections and commands are `await using`; `SaveAllAsync` uses a transaction; null strings go through `DBNull.Value`; all SQL is parameterized (`@task_id`, …) with no string concatenation. `created_at` uses explicit `NpgsqlDbType.TimestampTz`. The only style nits are `AddWithValue` inference for text (safe, but explicit `NpgsqlDbType.Text` avoids surprises), and the repository does no logging. Both are C-level, not blockers.

### The container failure is real and reproducible

Same login on host (DB down) and in the container (no DB at all): `Failed! 1 … FleetCore.Tests.dll` and exit 1. The test is in the default suite with no skip/filter, so `Dockerfile.build`'s `CMD ["dotnet", "test", …]` cannot pass. This is the M0 done-when and the spec definition of done.

---

## Findings

### Top issues, ranked

**1. BLOCKER — the DB-backed test breaks the fresh-container build (B1).**
`FleetCore.Tests.RestartSurvivalTests` opens `Host=127.0.0.1;Port=5432` in `ResetDatabaseAsync` (line 74) and again in the repository, with no guard. `docker run --rm shuttle-fleet-build` exits **1** (`NpgsqlException: Failed to connect to 127.0.0.1:5432`). Why it matters for a real shuttle system: the container is how the project is known to build and test anywhere; a suite that only passes on a developer laptop while a database happens to be up is not a repeatable gate, and every later DB-backed test (lesson 09 assignment persistence, M5 recovery) will inherit the same red default suite. The spec's own definition of done ("Builds in the Docker environment, tests pass") is not met.

**2. SHOULD-FIX — the DB test lives in the pure test project and drags the app in (S1).**
`FleetCore.Tests.csproj:23` adds `ProjectReference ..\FleetManager\FleetManager.csproj`, so `FleetCore.Tests` now transitively depends on `Npgsql` and `MQTTnet`. A pure-domain test project should not need the fleet manager (or a database) to compile or run; the I/O edge belongs with the app in `FleetManager.Tests`, or in a dedicated integration-test project the container build excludes. This is the structural root cause of B1: putting the test where it is forced the reference and forced the DB into the default suite.

**3. SHOULD-FIX — unrelated churn and formatting noise in a persistence commit (S2).**
Commit `009a2cf` also renames `ct` → `cancellationToken` across `src/ShuttleSim/ShuttleEndpoint.cs`, `ShuttleSim.Tests/ShuttleEndpointTests.cs`, and `FleetManager.Tests/ReliableCommandSenderTests.cs` — none of which is lesson-08 work. The rename left `CancellationToken  cancellationToken` (two spaces) in 13 places, including the new `IFleetTaskRepository.cs:5-8`. Why it matters: AGENTS says one concept per commit; mixing a mechanical refactor into a persistence feature makes the diff harder to review and hides accidental behavior changes (e.g. the review-06 `ct` names it renamed). Keep the rename in its own commit and single-space the parameter lists.

**4. CONSIDER — `timestamptz` microsecond precision vs .NET ticks (C1).**
Postgres stores microseconds; `DateTime` ticks are 100 ns. Verified directly: `'…00.1234567+00'::timestamptz` reads back as `…00.123457`. The test only uses whole-second `CreatedAt` values (`RestartSurvivalTests.cs:16-34`), so `Assert.Equal(expected.CreatedAt, actual.CreatedAt)` passes today. But once `CreatedAt` is `DateTime.UtcNow` (lesson 09 and real use), a reloaded task will not be tick-equal. For a fleet system this matters if any code keys on `CreatedAt` equality rather than ordering. Decide now: truncate to microseconds on save, or document that `CreatedAt` is order-only.

**5. CONSIDER — duplicated DDL and a hard-coded port/credentials (C2).**
`RestartSurvivalTests.ResetDatabaseAsync` (lines 77-91) re-declares the same `CREATE TABLE` that `PostgresFleetTaskRepository.CreateTableSql` already creates (`EnsureTableAsync` runs on every save/load). Two copies of the schema drift. Also `RestartSurvivalTests.cs:9` hard-codes `Port=5432;Username=shuttle;Password=shuttle`, so a developer running with `DB_HOST_PORT`/`SHUTTLE_DB_*` overrides (documented in `docker/README.md`) cannot run the test without editing source. Make the connection string an environment/config value with that literal only as a default.

### Lesson defects (for the professor)

**L1 — lesson 08 requirement 5 + Phase 4 do not settle how the fresh-container suite stays green.**
The assignment places the restart-survival test in `FleetCore.Tests` and tells the owner the test "may assume DB is running or start it; acceptance will run in appropriate context", while Phase 4 says "expect all suites pass (7 suites …)". But the M0/AGENTS gate runs `dotnet test` in a container with no database. The lesson never says how the DB test skips, filters, or gets a database there, so its own acceptance is self-inconsistent with the project's definition of done. Fix in the lesson: require the DB test to be opt-in via configuration (connection string from an env var) and skipped/filtered when absent, and say which `dotnet test` invocation the container runs — or place DB tests in a project the container build excludes. This is a lesson gap; the owner still owns the green container per spec DoD.

**L2 (minor) — the connection-string guidance conflicts with the project's config rule.**
The lesson offers `Host=127.0.0.1;Port=5432;Username=shuttle;Password=shuttle;Database=shuttle` as an acceptable literal, but AGENTS says "no hard-coded … sizes; use configuration" and `docker/README.md` documents `DB_HOST_PORT`/`SHUTTLE_DB_*`. Agree a config-based connection string in the lesson.

**Concepts check:** the three required concepts (repository abstraction, persistence shape/mapping, reload/restart survival) are each taught in their own section, and the lesson stays within the 3-concept cap. No concept in the list is untaught. The defect is in the acceptance mechanism, not the teaching.

---

## Explain-it questions — NON-BLOCKING to answer, but required before the next lesson

Answer these out loud or in writing. They do not change the not-yet verdict, which rests on B1.

1. `IFleetTaskRepository` is a type in `FleetCore`, but the only implementation is in `FleetManager`. What does that split buy, and what specifically breaks if `FleetCore` takes a `Npgsql` reference or implements `Save` itself?
2. The `internal FleetTask` constructor sets `State`/`AssignedShuttleId`/`FailureReason` directly and never calls `EnsureInOrder` or the transition methods. Why is that safe for loading saved state, and what exactly does `[assembly: InternalsVisibleTo("FleetManager")]` stop the rest of the codebase from doing?
3. `LoadAllAsync` orders by `created_at ASC, task_id ASC` and the board is rebuilt in that order. Walk through how that makes `NextQueued()` return the correct task after a restart, and say why the `task_id` tie-breaker is there at all.
4. (Study question, see C1) The test asserts equal `CreatedAt` because it uses whole-second values. What does Postgres store for `timestamptz`, and why could a task created with `DateTime.UtcNow` come back with a `CreatedAt` that is not `Equal`?

### Lines or blocks that look unclear, copied, or unexplained

- `src/FleetCore/FleetTask.cs:3` — `[assembly: InternalsVisibleTo("FleetManager")]`. Be able to say what assembly-level attribute it is, what it grants, and why it names only `FleetManager`.
- `src/FleetManager/PostgresFleetTaskRepository.cs:127,133-136` — the `NpgsqlDbType.TimestampTz`/`NpgsqlDbType.Text` calls and `(object?)… ?? DBNull.Value`; say why explicit types matter and why `AddWithValue("task_id", …)` inference is acceptable here.
- `src/FleetCore.Tests/RestartSurvivalTests.cs:72-97` — `ResetDatabaseAsync` duplicates the repository's DDL and opens its connection with no token; say why the duplicate is a drift risk and whether the repo's `EnsureTableAsync` already makes the test's `CREATE TABLE` redundant.
- `src/FleetCore.Tests/RestartSurvivalTests.cs:9` — the hard-coded connection string; say how you would make it config-driven so `DB_HOST_PORT` and credentials work.
- `src/ShuttleSim/ShuttleEndpoint.cs` and the two test fakes — the `ct` → `cancellationToken` rename carried in this commit; be ready to justify why it belongs in a persistence commit.
- `src/FleetCore.Tests/FleetCore.Tests.csproj:23` — the `FleetManager` project reference; say what a pure test project should depend on, and why this reference made the container fail.

---

## Revisit list

- **A test that needs infrastructure is not a unit test.** If it cannot run without a broker/database, it must be opt-in, filtered, or given that infrastructure — "acceptance will run in appropriate context" is not a mechanism. This continues the lesson-06 L2 theme of gates that silently test the wrong environment.
- **Project placement carries design meaning.** Pure domain tests stay pure; I/O edges belong with the app or in an integration project. Adding an app reference to a pure test project is how the container gate broke.
- **Database type fidelity.** `timestamptz` is microsecond precision; .NET `DateTime` is 100 ns. "Same value after reload" is not bit-exact for wall-clock timestamps.
- **One concept per commit.** A mechanical rename is not persistence; keep it separate so a defect can be bisected.
- **Next:** lesson 09 — assignment to several shuttles and the whole-M3 done-when (three shuttles finish a queue, restart end-to-end). The persistence boundary is ready for it.

---

## End-product check

This slice is the second M3 piece and it is the right shape: a domain-owned repository abstraction, an app-owned Npgsql implementation, an explicit row schema with wire-name states and FIFO ordering, and a restart proof that actually touches Postgres and is mutation-proven. Later milestones will lean on exactly this (lesson 09 saves the board between transitions; M5 recovery needs the saved state to show one place per pallet). The risk it introduces now is twofold: the definition of done is red because the new test cannot run in the fresh container, and the test's location has coupled the pure domain test project to the app and the database. If that placement and the skip/filter question are not settled here, every DB-backed test from here on keeps the container red and the "tests pass" claim untrue outside a developer machine. The whole-M3 done-when is still not met: multi-shuttle assignment and the end-to-end three-shuttle/restart proof are lesson 09. **M3 stays in progress.**

---

## README

**Unchanged.** The verdict is not-yet (B1), and AGENTS says the milestone table changes only after a passing review. The M3 row stays `in progress` with the review-07 verification note. On a clean re-review it would remain `in progress` (multi-shuttle assignment is lesson 09), with the persistence evidence added — never `done` at this lesson.

---

## What the owner must do next

1. **Make the fresh container green (B1).** Pick one and state it: (a) move the DB test out of the default suite into a project the container build excludes and have the container run only the pure/unit suites, or (b) keep it in the solution but gate it behind a configured connection string so it is skipped/filtered when no DB is set, or (c) run a database alongside the build. Whatever you pick, `docker build -f docker/Dockerfile.build -t shuttle-fleet-build . && docker run --rm shuttle-fleet-build` must exit 0.
2. **Move the DB-backed test to where I/O tests live (S1).** Drop the `FleetManager` reference from `FleetCore.Tests` so the pure domain suite stays pure. `FleetManager.Tests` already references `FleetManager`.
3. **Make the connection string config-driven (C2)** so `DB_HOST_PORT`/`SHUTTLE_DB_*` work without editing source; keep the local literal only as a default.
4. **Split the unrelated rename into its own commit (S2)** and fix the double-space `CancellationToken  cancellationToken` occurrences.
5. **Decide timestamp precision (C1):** if `CreatedAt` will come from `DateTime.UtcNow`, truncate to microseconds on save or avoid equality comparisons.
6. Answer the explain-it questions out loud; M3 is not closed until the container gate is green.

---

## Cleanup

- `docker compose -f docker/compose.yml down` run after the DB phases; the owner's already-running `database` container stopped and removed; `docker ps` shows no `shuttle-fleet` container and the `shuttle-fleet_default` network is gone.
- Throwaway `/tmp/opencode/rev08` (and its mutation copies) deleted. `/tmp/opencode/mqttinspect` left as found.
- `dotnet build-server shutdown` run. The pre-existing VS Code `ProjectSystem.Server.BuildHost` and MSBuild `nodeReuse` workers were present before the review and are not mine.
- No file under `src/` was modified. `docs/reviews/08-sql-persistence-and-restart.md` is the only file this review adds; `README.md` is untouched because the verdict is not-yet.

---

# Re-review (round 2) — 2026-10-11, commit `0da93e7`

**Date:** 2026-10-11
**Reviewed:** commit `0da93e7` ("M3: feat: update Dockerfile and tests to exclude integration tests from the fresh-container gate") on top of `3ab4a7d` (test moved to `FleetManager.Tests`, config-driven connection string, auto-start helper) and `6929902` (double-space cleanup). Working tree clean, HEAD `0da93e7` at review start.
**The professor's L1/L2 fixes are present and committed:** `0da93e7` carries the lesson/hints edits and [decision record 14](../../decisions/14-db-test-gate-and-config.md) (142 lines) — not just working-copy changes.
**Verdict change: not yet → pass with changes.** Every functional gate from round 1 is green: the fresh container exits 0 with the integration test excluded, the DB test truly ran and asserted against the live database, all six mutations go red, and the pure/non-pure project split is restored. One should-fix remains (dead auto-start scaffolding in the test file) and one lesson defect (Phase 7's docker grep is over-broad) — both small, neither blocking.

---

## Per-finding grades

| Round-1 finding | This round | Evidence |
|---|---|---|
| **B1 (blocker)** — DB-backed test broke the fresh container | **cleared** | `docker run --rm shuttle-fleet-build` exit 0, 7 `Passed!`; reproducible on a `--no-cache` rebuild (`shuttle-fleet-build-fresh`, exit 0, 7 `Passed!`). `FleetManager.Tests` shows **5** tests in the container — the trait-filtered suite excludes only the integration test, which is the record-14 §5 proof that the filter works |
| **S1** — DB test in pure project | **cleared** | `RestartSurvivalTests.cs` lives in `src/FleetManager.Tests/`; `FleetCore.Tests.csproj` references only `FleetCore`; `grep -c 'FleetManager' src/FleetCore.Tests/FleetCore.Tests.csproj` prints 0 |
| **S2** — rename churn + double spaces | **cleared (residue noted)** | no `CancellationToken  x` double spaces anywhere in `src/`; separate whitespace commit `6929902` exists. The `ct`→`cancellationToken` rename itself is still bundled in `009a2cf` history — not worth rewriting history, but the bisection note stands |
| **C1** — `timestamptz` µs vs .NET ticks | **cleared** | `FleetTask` truncates to microsecond precision in **both** constructors (`ToMicrosecondPrecision`, `FleetTask.cs:87-88`). The test deliberately uses `start.AddTicks(7)` (700 ns). Verified: Postgres rounds `…00.0000007+00` up to `…00.000001` when not truncated, and removing the truncation turns the test red (mutation M5) |
| **C2** — duplicated DDL, hard-coded port/credentials | **cleared** | `ResetDatabaseAsync` no longer re-declares DDL (plain `DELETE`; table ensured by the repo first). `BuildConnectionString` follows record 14 §6 exactly: `SHUTTLE_DB_CONNECTION_STRING` whole, else `SHUTTLE_DB_HOST` / `DB_HOST_PORT` / `SHUTTLE_DB_USER` / `SHUTTLE_DB_PASSWORD` / `SHUTTLE_DB_NAME` with local defaults. A non-integer `DB_HOST_PORT` fails fast (verified) |
| **L1 / L2 (lesson defects)** | **cleared** | lesson now requires the trait, the filtered CMD, the config-driven connection string, and `FleetManager.Tests` placement; record 14 documents the mechanism and the two-gate proof |
| **New S3 (should-fix)** — dead auto-start scaffolding | **open** | see Findings |
| **New L3 (lesson defect)** — Phase 7 grep over-broad | **open** | see Findings |
| **New C3 (consider)** — test reads before it resets | **open** | see Findings |

---

## Commands run, verbatim key output

Run from repo root unless stated. Exit codes read directly. Mutations ran against a throwaway `git archive HEAD` copy at `/tmp/opencode/rev08b`, deleted afterward; the DB was stopped before the container runs and started only for the with-DB phases.

### Environment

```
$ dotnet --version        → 10.0.112
$ docker --version        → Docker version 29.8.1
$ docker compose -f docker/compose.yml ps → (nothing up at review start; round-1 note that the owner's container had been left running is now moot)
```

### B1 — the fresh-container gate is green, twice

```
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build .     exit 0 (layers from the owner's rebuild)
$ docker run --rm shuttle-fleet-build                                  exit 0
   Passed!  … 22 … ShuttleCore.Tests.dll       Passed!  … 18 … FleetCore.Tests.dll
   Passed!  …  4 … Contracts.Tests.dll         Passed!  …  5 … ShuttleSim.Tests.dll
   Passed!  …  8 … Messaging.Tests.dll         Passed!  …  5 … FleetManager.Tests.dll
   Passed!  …  2 … MqttMessaging.Tests.dll
$ docker build --no-cache -f docker/Dockerfile.build -t shuttle-fleet-build-fresh .   exit 0
$ docker run --rm shuttle-fleet-build-fresh                             exit 0, same 7 "Passed!" lines
```

A cache-busting rebuild was run so the green container is not the owner's cached layers. No database exists in that container; `FleetManager.Tests` passing 5/6 there is the filter working — had the filter been wrong, the DB test would have run and failed the container (exit 1). This is the record-14 §5 "container half" of the gate.

### The DB test TRULY ran and asserted (not fake green)

```
$ docker compose -f docker/compose.yml up -d database                   exit 0, accepting connections
$ cd src
$ dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration"
   … FleetManager.Tests -> …/FleetManager.Tests.dll                     ← rebuilt now, not a stale binary
   Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks [494 ms]
   Passed: 1
   exit 0
```

The printout is `Passed <name>`, not `Skipped <name>`, and the same log shows the test project being built — so the proof ran on the current code and asserted. Rows read back with `psql` are the real six tasks in FIFO order with wire-name states and preserved nulls:

```
 task_id      | to_lane | created_at | state | assigned_shuttle_id | failure_reason
 task-queued-a    | A01-L01 | 2026-01-01 00:00:00+00 | queued        |                     |
 task-queued-b    | A01-L02 | 2026-01-01 00:01:00+00 | queued        |                     |
 task-assigned    | A01-L03 | 2026-01-01 00:02:00+00 | assigned    | SH-01               |
 task-in-progress | A01-L04 | 2026-01-01 00:03:00+00 | in_progress | SH-02               |
 task-done        | A01-L05 | 2026-01-01 00:04:00+00 | done        | SH-03               |
 task-failed      | A01-L06 | 2026-01-01 00:05:00+00 | failed      |                     | rack empty
(6 rows)
```

### Whole suite with the database up

```
$ cd src && dotnet test ShuttleFleet.slnx -v m                          exit 0
   Passed! 7 lines: 4 / 18 / 8 / 2 / 22 / 6 / 5  = 65 tests
```

### Lesson acceptance phases (verbatim where possible)

Phase 1 (interface, `InternalsVisibleTo`, `internal FleetTask`): all exit 0.
Phase 2 (purity): `grep -rniE 'Npgsql|Postgres|…' src/FleetCore` exit 1 (no match); `grep -c 'ProjectReference\|PackageReference' src/FleetCore/FleetCore.csproj` prints 0; `grep -c 'FleetManager' src/FleetCore.Tests/FleetCore.Tests.csproj` prints 0.
Phase 3: `<PackageReference Include="Npgsql" Version="8.0.6" />` in `FleetManager.csproj`, exit 0.
Phase 4 (pure suite, container command shape, DB down): RC 0, 7 `Passed!`, no `Failed!`.
Phase 5 (DB-test-ran proof): RC 0, exact line `Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks`, `Passed: 1`.
Phase 6 (container): exit 0 (above, including no-cache).
Phase 7: trait present (exit 0); `--filter`/`Category!=Integration` in `docker/Dockerfile.build` (exit 0); **but** `grep -qE 'Process\.Start|"docker"' src/FleetManager.Tests/RestartSurvivalTests.cs` prints `FAIL: test must not start docker` — see L3. The test never starts docker (`Process.Start` is absent; the match is the string `"docker"` inside the dead `FindRepoRoot` helper).
Phase 8 (contract): v1 `Version: 1`, v2 `Version: 2`, no `v3.md`; `grep -rniE 'FleetTask|TaskState' src/Contracts` no match.

### Connection-string configuration (C2, verified behaviorally)

```
$ SHUTTLE_DB_CONNECTION_STRING="Host=127.0.0.1;Port=5432;Username=shuttle;Password=shuttle;Database=shuttle" SHUTTLE_DB_HOST="127.0.0.99" dotnet test FleetManager.Tests/… -v q --filter "Category=Integration"
   exit 0, Passed 1/1        ← the whole-string override wins over a bogus host
$ DB_HOST_PORT="not-a-port" dotnet test … -v m --filter "Category=Integration"
   exit 1 … System.InvalidOperationException : DB_HOST_PORT must be a valid integer.
```

### Regression checks

M1 demo: `dotnet run --project ShuttleSim/ShuttleSim.csproj` exit 0, 7 `ShuttleId` status lines, `PLT-001` moved `A01-L01` → `A02-L02`. Manager start: `dotnet run --project FleetManager/FleetManager.csproj` exit 0 with `Fleet Manager Coming Online`. M2 (review 06) logging intact: `attempt {n} of {max} for command {id}` in `ReliableCommandSender.cs:66`; `listening`/`handled`/`published` lines in `ShuttleSim/Program.cs`. Contract v1/v2 untouched.

### Mutations (throwaway `git archive HEAD` copy, `/tmp/opencode/rev08b`, deleted after)

Baseline of the integration test in the copy: `Passed! 1/1` (exit 0). Each mutation turned it red; the DB was cleaned before each run so a failed mutation could not pollute the next; sources were restored and verified identical to the repo after each mutation.

| Mutation | What changed | Result |
|---|---|---|
| M1 wrong load order | `ORDER BY created_at ASC` → `DESC` | `Assert.Equal() Failure: Collections differ` — red |
| M2 mangled state on load | `"in_progress" => TaskState.Assigned` | `Assert.Equal() Failure: Values differ` — red |
| M3 wrong saved column | `state` bound to `task.ToLane` | `System.ArgumentException : Unknown task state wire name 'A01-L01'` — red |
| M4 field fidelity | `failure_reason` dropped on load | `Assert.Equal() Failure: Strings differ` — red |
| M5 timestamp truncation removed (C1) | `ToMicrosecondPrecision` returns `value` | `Assert.Equal() Failure: Values differ` — red |
| M6 field fidelity | `assigned_shuttle_id` dropped on load | `Assert.Equal() Failure: Strings differ` — red |
| Restore | — | `Passed! 1/1` green again |

The restart proof is load-bearing for ordering, state mapping, column mapping, both nullable-field round trips, and the C1 precision fix — none of it is vacuous.

### Read-before-reset fragility (C3, demonstrated)

Inserted a row with `state='legacy_state'` (a state the current wire mapping cannot parse — the shape of a row from an older schema or a broken run), then ran the **unmutated** test: it fails with `System.ArgumentException : Unknown task state wire name 'legacy_state'.` before its own reset can clear the table. The test's first `LoadAllAsync` exists only to ensure the table exists, but it parses every row first.

---

## Findings

**1. SHOULD-FIX S3 — dead auto-start scaffolding left in the test.** `RestartSurvivalTests.cs:99-131` still contains `EnsureDatabaseIsRunningAsync` (a 20-attempt × 1 s retry poller with hard-coded numbers) and `FindRepoRoot` (walks directories looking for `docker/compose.yml`) — the remains of the removed `StartDatabaseAsync` from the B1 first-fix attempt. Nothing calls either method. They trip the lesson's Phase 7 acceptance grep, which prints `FAIL: test must not start docker` for a test that never starts docker (L3). Delete both methods. Why it matters for a real shuttle system: record 14 §2 rejects auto-starting Docker from a test permanently and explains why; leaving the scaffolding in invites a future developer to re-enable a mechanism that cannot work in the build image and that hides what actually runs in the DB-less gate.

**2. LESSON DEFECT L3 — Phase 7's docker grep tests word presence, not behavior.** `grep -qE 'Process\.Start|"docker"'` matches any quoted literal `"docker"` in the source, including a path string. The check should target real invocations (`Process\.Start`, `docker compose`, `docker run`, or the removed helper names). As written, the acceptance command is red against a correct implementation. The behavior is verified instead by the container gate itself: the test cannot run in the container (no DB), and the with-DB run proves it executes.

**3. CONSIDER C3 — the test reads the table before it clears it.** Reproduced above. This becomes real when lesson 09/M5 start writing different row shapes (e.g. assignment fields) into the same database the developer keeps between lessons. The reset should ensure-then-delete without parsing row contents — e.g. a bare `CREATE TABLE IF NOT EXISTS` plus `DELETE`, or `TRUNCATE`.

**4. CONSIDER C4 (design note) — the precision fix lives in the domain model.** `FleetTask.ToMicrosecondPrecision` truncates in both constructors. It is the right call (single place, invariant holds in memory and in storage, mutation-proven), but it embeds a storage-precision rule in the domain. Write it down as a domain invariant ("`CreatedAt` is UTC at microsecond precision, matching the database") so a later change to `timestamptz` precision or a move to a different store re-triggers the decision. The round-1 worry that `DateTime.UtcNow`-created tasks would not reload tick-equal is resolved: both constructors truncate, so save and load agree.

**Concepts check:** the lesson keeps its 3-concept cap (repository abstraction, persistence shape/mapping, reload/restart survival), each taught in its own section; the test-gate mechanism is explicitly declared an assignment requirement, not a concept, and is fully specified in record 14. No concept in the list is untaught. The only defect is L3's grep.

---

## Explain-it questions — before the next lesson

Round-1 questions, with their status:

1. **Repository split** — still ask. The code now embodies it (`IFleetTaskRepository` in pure `FleetCore`, Npgsql only in `FleetManager`); say what specifically breaks if `FleetCore` takes an Npgsql reference or implements `Save` itself.
2. **Internal hydration + `InternalsVisibleTo`** — still ask; unchanged from round 1, and the attribute now names exactly the assembly that owns the persistence implementation.
3. **Load ordering + `task_id` tie-breaker** — still ask; mutation M1 proves the ordering is load-bearing.
4. **`timestamptz` precision vs ticks** — **now answered in code** (µs truncation, `AddTicks(7)` test, mutation M5). Re-ask as: why does the domain truncate, and what does `start.AddTicks(7)` prove that whole-second values did not?

New:

5. Why is `[Trait("Category","Integration")]` + container filter a two-gate proof instead of a runtime skip? Why would "skipped everywhere" be a fake green?
6. `BuildConnectionString`: walk the precedence and the fail-fast port parse. Why does `SHUTTLE_DB_CONNECTION_STRING` win over the parts?
7. The dead helpers: what were `EnsureDatabaseIsRunningAsync`/`FindRepoRoot` for, why must they go, and what do they do to the Phase 7 grep?
8. The test loads before it resets. Walk the ordering and say why a stale row breaks it (C3).
9. `SaveAllAsync` upserts with `ON CONFLICT (task_id) DO UPDATE` inside one transaction — what does the transaction buy, and what is the in-memory board's state if `CommitAsync` fails?

### Lines or blocks that look unclear, copied, or unexplained

- `src/FleetManager.Tests/RestartSurvivalTests.cs:99-131` — the two unused helpers; be able to tell the B1 first-fix story (record 14 §2) and why they must be deleted.
- `src/FleetCore/FleetTask.cs:87-88` — `ToMicrosecondPrecision` and the `% 10` math; say why both constructors truncate.
- `src/FleetManager/PostgresFleetTaskRepository.cs:28-34` — the `ON CONFLICT` upsert; say why an upsert rather than plain `INSERT` is needed to re-save a board after restart.

---

## Revisit list

- **Integration-test fixture hygiene.** A DB-backed test must be able to reset its own fixture without first parsing whatever is in the table (C3).
- **Delete the scaffolding when a design is rejected.** Record 14 says auto-start is permanently rejected; the half-removed attempt still sits in the file (S3).
- **Acceptance greps must test behavior.** `grep '"docker"'` proves the word appears, not that the test starts Docker (L3).
- Round-1 items still stand: infra-dependent tests are not unit tests; project placement carries design meaning (now fixed); one concept per commit.
- **Next:** lesson 09 — assignment to several shuttles and the whole-M3 done-when (three shuttles finish a queue, restart end-to-end). Its DB-backed tests reuse exactly this trait/filter/two-gate pattern.

---

## End-product check

This slice is now the right shape and the gates are honest: the DB-less container proves the pure suite (7 `Passed!`, integration test excluded), the with-DB phase proves the restart test executed and asserted (`Passed`/`Passed: 1`), psql shows real rows with wire-name states and preserved nulls, and six mutations prove the round trip is load-bearing. What remains is 30 lines of dead code plus one over-broad grep — paper cuts, not structural risk. The structural risk that carries forward is C3 (schema evolution on the developer's persistent database between lessons) and the need to keep the µs-precision invariant documented. The M3 done-when is still not closed: multi-shuttle assignment and the end-to-end three-shuttle/restart proof are lesson 09. **M3 stays in progress.**

---

## README

**Unchanged.** The verdict is **pass with changes** (S3 open), and AGENTS.md changes the milestone table only on a passing review. The M3 row stays `in progress`, which it would also do on a clean pass — lesson 09 owns multi-shuttle assignment and the whole-M3 done-when. When S3 clears, the M3 verification note should be extended with the persistence proof: fresh container exit 0 with `--filter Category!=Integration` (7 `Passed!`), the with-DB run `Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks`, and the six mutations.

---

## What the owner must do next

1. Delete `EnsureDatabaseIsRunningAsync` and `FindRepoRoot` from `src/FleetManager.Tests/RestartSurvivalTests.cs` (S3) and re-run the lesson Phase 7 — it should print `OK: test does not start docker`.
2. Professor: tighten the Phase 7 grep (L3).
3. Optionally fix C3 (ensure-then-delete in the test reset).
4. Answer the explain-it questions out loud.

---

## Cleanup (round 2)

- `docker compose -f docker/compose.yml down` run after the DB phases; `docker ps` shows no shuttle-fleet containers and the `shuttle-fleet_default` network is gone.
- No-cache image `shuttle-fleet-build-fresh` deleted; the pre-existing `shuttle-fleet-build` tag left as found.
- Throwaway `/tmp/opencode/rev08b` and this round's scratch files deleted; `/tmp/opencode/mqttinspect` left as found.
- `dotnet build-server shutdown` run.
- No file under `src/` was modified; the repo working tree is clean. `docs/reviews/08-sql-persistence-and-restart.md` is extended and `README.md` is untouched because the verdict is pass with changes.

---

# Re-review (round 3 — final) — 2026-10-11, commit `6e01777`

**Date:** 2026-10-11
**Reviewed:** commit `6e01777` ("M3: fix: remove unused database helper methods from RestartSurvivalTests") on top of `0da93e7`. HEAD is `6e01777`. The round-2 `src/` change (deleting `EnsureDatabaseIsRunningAsync` / `StartDatabaseAsync` / `FindRepoRoot`) is committed; the lesson L3 fix is not (see caveat).
**Verdict change: pass with changes → pass (clean).** Both open items from round 2 are genuinely cleared: S3 (dead docker-auto-start helpers) is deleted, and L3 (Phase 7 grep false-positives) is fixed. Every functional gate re-ran green and nothing regressed.

> **Git-state caveat — the owner must commit it.** The L3 lesson fix is **uncommitted**. The working tree has the tightened Phase 7 grep, but `HEAD:docs/lessons/08-sql-persistence-and-restart.md` still carries the old `grep -qE 'Process\.Start|"docker"'`. This review ran the gate against the **working tree** (which is what the owner must satisfy) and it passes; for the fix to be real in history the owner must commit `docs/lessons/08-sql-persistence-and-restart.md`. This review did not touch or commit that file.

---

## Per-finding grades (full history)

| Finding | Round | Status now | Evidence |
|---|---|---|---|
| **B1 (blocker)** — DB test broke the fresh container | 1 | **cleared** | `docker run --rm shuttle-fleet-build` exit 0, 7 `Passed!`; `FleetManager.Tests` 5/6 (integration excluded) |
| **S1 (should-fix)** — DB test in the pure test project | 1 | **cleared** | `RestartSurvivalTests.cs` in `src/FleetManager.Tests/`; `grep -c 'FleetManager' src/FleetCore.Tests/FleetCore.Tests.csproj` prints 0 |
| **S2 (should-fix)** — rename churn + double spaces | 1 | **cleared** | no `CancellationToken  x` double spaces; separate `6929902` commit exists |
| **C1 (consider)** — `timestamptz` µs vs .NET ticks | 1 | **cleared** | both `FleetTask` constructors truncate (`ToMicrosecondPrecision`); this round's M5 mutation (remove truncation) goes red |
| **C2 (consider)** — duplicated DDL, hard-coded port/creds | 1 | **cleared** | `ResetDatabaseAsync` is a plain `DELETE`; `BuildConnectionString` follows record 14 §6; whole-string override wins, bad `DB_HOST_PORT` fails fast (both re-verified) |
| **S3 (should-fix)** — dead docker-auto-start helpers | 2 | **cleared (this round)** | `6e01777` deleted both helpers; `grep -nE 'Process\.Start|docker' src/FleetManager.Tests/RestartSurvivalTests.cs` exits 1 (no match); Phase 7 prints `OK: test does not start docker` |
| **L1 / L2 (lesson defects)** | 1 | **cleared** | lesson + record 14 require the trait, filtered CMD, config-driven connection string, `FleetManager.Tests` placement |
| **L3 (lesson defect)** — Phase 7 grep over-broad | 2 | **cleared (this round, uncommitted)** | working-tree pattern passes the current file, does not false-positive on a bare `"docker"` path segment, and catches real regressions (below) |
| **C3 (consider)** — test reads before it resets | 2 | **open (consider)** | reproduced again this round (below); not blocking |
| **C4 (consider, design note)** — µs rule lives in the domain | 2 | **open (documentation ask)** | still no written domain invariant; not blocking |

All four previously-open project findings (B1/S1/S2/C1/C2 and L1/L2/L3) are cleared; the two open items are considerations, not gates.

---

## Commands run, verbatim key output

Run from repo root unless stated; exit codes read directly. The database was down for the container run and started only for the with-DB phases. Mutations ran against a throwaway `git archive HEAD` copy at `/tmp/opencode/rev08c`, deleted afterward; no experiment touched `src/`.

### Environment

```
$ dotnet --version        → 10.0.112
$ docker --version        → Docker version 29.8.1
$ docker compose -f docker/compose.yml ps → (nothing up at review start)
```

### Lesson acceptance, Phases 1–4, 6, 7, 8 (verbatim)

```
# Phase 1
tests -f src/FleetCore/IFleetTaskRepository.cs                                    exit 0
grep -q "InternalsVisibleTo" src/FleetCore/FleetTask.cs                           exit 0
grep -q "internal FleetTask" src/FleetCore/FleetTask.cs                           exit 0
# Phase 2
grep -rniE 'Npgsql|Postgres|Microsoft.Data.SqlClient' src/FleetCore --include='*.cs' --include='*.csproj'
  (no matches)                                                                    exit 1
grep -c 'ProjectReference\|PackageReference' src/FleetCore/FleetCore.csproj       prints 0
grep -c 'FleetManager' src/FleetCore.Tests/FleetCore.Tests.csproj                 prints 0
# Phase 3
grep -rni 'Npgsql' src/FleetManager/FleetManager.csproj | grep PackageReference
  <PackageReference Include="Npgsql" Version="8.0.6" />                           exit 0
# Phase 4  (DB down)
dotnet build                                                                       exit 0, 0 Warning(s), 0 Error(s)
dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"               RC 0
  Passed! … 4 … Contracts.Tests    Passed! … 18 … FleetCore.Tests
  Passed! … 2 … MqttMessaging.Tests Passed! … 8 … Messaging.Tests
  Passed! … 22 … ShuttleCore.Tests Passed! … 5 … ShuttleSim.Tests
  Passed! … 5 … FleetManager.Tests   ← 5/6: the integration test is excluded
  7 "Passed!" lines, no "Failed!"
# Phase 6
docker build -f docker/Dockerfile.build -t shuttle-fleet-build .                   exit 0
docker run --rm shuttle-fleet-build                                                exit 0
  7 "Passed!" lines; FleetManager.Tests 5/6; docker run --rm shuttle-fleet-build which docker → exit 1 (no docker binary)
# Phase 7
grep -qE 'Trait\("Category",\s*"Integration"\)' src/FleetManager.Tests/RestartSurvivalTests.cs        exit 0
grep -qF 'Category!=Integration' docker/Dockerfile.build                             exit 0
grep -qE 'Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))' src/FleetManager.Tests/RestartSurvivalTests.cs
  → OK: test does not start docker
# Phase 8
grep -qE '^Version: 2$' docs/contract/v2.md                                          exit 0
grep -qE '^Version: 1$' docs/contract/v1.md                                          exit 0
test ! -f docs/contract/v3.md                                                        exit 0
grep -rniE 'FleetTask|TaskState' src/Contracts --include='*.cs' --include='*.csproj' no match (exit 1)
```

### Phase 5 — the DB test TRULY ran (with the database up)

```
$ docker compose -f docker/compose.yml up -d database                              exit 0, accepting connections
$ cd src
$ dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration"
   … FleetManager.Tests -> …/FleetManager.Tests.dll                                ← rebuilt now
   Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks [581 ms]
   Passed: 1
   exit 0
```

The printout is `Passed <name>`, not `Skipped <name>`, on a freshly built test binary.

### Rehydrate-field fidelity spot check (psql)

```
 task_id      | to_lane |       created_at       |    state    | assigned |   reason
 task-queued-a    | A01-L01 | 2026-01-01 00:00:00+00 | queued      | <NULL>   | <NULL>
 task-queued-b    | A01-L02 | 2026-01-01 00:01:00+00 | queued      | <NULL>   | <NULL>
 task-assigned    | A01-L03 | 2026-01-01 00:02:00+00 | assigned    | SH-01    | <NULL>
 task-in-progress | A01-L04 | 2026-01-01 00:03:00+00 | in_progress | SH-02    | <NULL>
 task-done        | A01-L05 | 2026-01-01 00:04:00+00 | done        | SH-03    | <NULL>
 task-failed      | A01-L06 | 2026-01-01 00:05:00+00 | failed      | <NULL>   | rack empty
```

FIFO order, wire-name states, and both nullable fields round-trip. `task-queued-a` was created with `start.AddTicks(7)` and reads back `00:00:00` — the µs truncation is doing its job.

### Whole suite with the database up

```
$ cd src && dotnet test ShuttleFleet.slnx -v m                                     exit 0
   Passed! 7 lines: 4 / 8 / 18 / 22 / 2 / 5 / 6 = 65 tests
   (FleetManager.Tests: 6/6 — the integration test included)
```

### L3 — the tightened grep catches regressions and drops the false positive (throwaway copy)

```
new pattern: Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))
new/current-file              → OK (no match)
old pattern on Path.Combine(root, "docker", "compose.yml")  → FAIL (false positive)   ← old L3 bug
new pattern on same path segment                            → OK (no match)
new pattern on Process.Start("docker", "compose up …")      → FAIL (caught, correct)
new pattern on "docker compose up -d database" (string)     → FAIL (caught, correct)
new pattern on "docker-compose up -d database"              → FAIL (caught, correct)
new pattern on Process.Start("docker")                      → FAIL (caught, correct)
```

### Mutations (throwaway `git archive HEAD`, `/tmp/opencode/rev08c`, deleted after)

Baseline green; each mutation red; restore green. The table was truncated before each run so a failed mutation could not poison the next (see C3).

| Mutation | What changed | Result |
|---|---|---|
| M1 wrong load order | `ORDER BY created_at ASC` → `DESC` | **red** (`Failed! 1/1`) |
| M2 mangled state on load | `"in_progress" => TaskState.Assigned` | **red** |
| M3 wrong saved column | `state` bound to `task.ToLane` | **red** |
| M4 field fidelity | `failure_reason` dropped on load | **red** |
| M5 timestamp truncation removed (C1) | `ToMicrosecondPrecision` returns `value` | **red** |
| M6 field fidelity | `assigned_shuttle_id` dropped on load | **red** |
| Restore | — | **green** again |

### Regression checks

- M1 demo: `dotnet run --project ShuttleSim/ShuttleSim.csproj` exit 0, 7 `ShuttleId` status lines; `PLT-001` ends at `A02-L02` (`00:00:19Z` idle).
- Manager start: `dotnet run --project FleetManager/FleetManager.csproj` exit 0, `Fleet Manager Coming Online` present.
- M2 (review 06) logging intact: `attempt {n} of {max} for command {id}` (`ReliableCommandSender.cs:66`); `handled …` (:96), `listening on …` (:101).
- Contract: `v1.md` `Version: 1`, `v2.md` `Version: 2`, no `v3.md`; `src/Contracts` has no `FleetTask`/`TaskState`.
- Connection string (C2): `SHUTTLE_DB_CONNECTION_STRING` whole wins over a bogus `SHUTTLE_DB_HOST` (exit 0); `DB_HOST_PORT=not-a-port` fails fast with `System.InvalidOperationException : DB_HOST_PORT must be a valid integer.`

### C3 reproduced (consider, not blocking)

Leftover rows from the M3 mutation (`state='A01-L01'`), and a deliberately inserted `state='legacy_state'`, both break the test's first `LoadAllAsync` before its own reset can clear the table:

```
System.ArgumentException : Unknown task state wire name 'legacy_state'. (Parameter 'wireName')
```

In the mutation run this showed up as the "restore" check going red until the table was truncated — clean evidence that the read-before-reset ordering is fragile.

---

## Findings

**1. CLEARED — S3 (dead docker-auto-start helpers).** `EnsureDatabaseIsRunningAsync`, `StartDatabaseAsync`, and `FindRepoRoot` are gone from `src/FleetManager.Tests/RestartSurvivalTests.cs` (`6e01777`). No `Process.Start` and no `docker` token remains anywhere in that file, and the lesson's Phase 7 gate prints `OK: test does not start docker`. The file is now 109 lines, all used.

**2. CLEARED — L3 (Phase 7 grep false-positives, uncommitted).** The tightened pattern `Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))` passes the current file, rejects the old false positive (a bare `"docker"` path segment), and still catches every real regression shape. One process note: the fix is only in the working tree — commit the lesson file.

**3. CONSIDER — C3 (read before reset) is still open.** Not a gate, but it is the one real fragility left, and it bites harder once lesson 09/M5 write new row shapes into a developer's persistent database. The reset should ensure-then-delete without parsing row contents.

**4. CONSIDER — C4 (document the µs invariant).** Still a documentation ask: write down "`CreatedAt` is UTC at microsecond precision, matching the database" so a future store/precision change re-triggers the decision.

**Concepts check:** the three required concepts (repository abstraction, persistence shape/mapping, reload/restart survival) are each taught, the list is fully covered, and the lesson stays within the 3-concept cap; the test-gate mechanism is declared an assignment requirement, not a concept, and is specified in record 14. No lesson defect remains.

---

## Explain-it questions — carry into lesson 09

The round-1/2 questions still stand (repository split; internal hydration + `InternalsVisibleTo`; load ordering + `task_id` tie-breaker; why the domain truncates and what `AddTicks(7)` proves; the trait/filter two-gate proof; `BuildConnectionString` precedence; `SaveAllAsync` upsert + transaction). Add:

10. The two dead helpers were deleted, not fixed. Why is auto-starting Docker from the test *permanently* wrong (not just inconvenient) in this project, and what would you use instead if a later DB test needed the database started? (record 14 §7)

### Lines or blocks that look unclear, copied, or unexplained

- `src/FleetManager.Tests/RestartSurvivalTests.cs:14-16` — the `LoadAllAsync` call that exists only to ensure the table, immediately before `ResetDatabaseAsync`; be able to explain why it parses rows first and why that is the C3 risk.
- `src/FleetCore/FleetTask.cs:87-88` — `ToMicrosecondPrecision` and the `% 10` math; say why both constructors truncate.
- `src/FleetManager/PostgresFleetTaskRepository.cs:28-34` — the `ON CONFLICT` upsert; say what a plain `INSERT` would break on re-save.

---

## Revisit list

- **Fixture hygiene (C3):** a DB-backed test must be able to reset its fixture without first parsing whatever is in the table.
- **Write down domain invariants (C4):** the µs precision rule is real; document it where the next change will see it.
- **Acceptance greps must target behavior, not words** — the L3 fix is the worked lesson: over-broad patterns fail correct code and pass wrong code.
- **Commit the lesson fix.** Documentation gates that only exist in a working tree are not gates.
- **Next:** lesson 09 — assignment to several shuttles and the whole-M3 done-when (three shuttles finish a queue, restart end-to-end). Its DB-backed tests reuse this trait/filter/two-gate pattern.

---

## End-product check

This slice is complete and honest for what it claims. The persistence boundary is the right shape (pure `IFleetTaskRepository` in `FleetCore`, Npgsql only in `FleetManager`), the row schema and wire-name mapping match record 13, restart survival is proven against a real database and mutation-proven non-vacuous, and the fresh container is a real DB-less gate rather than a silent skip. For the later milestones this is the carry-forward foundation: lesson 09 saves the board between transitions, and M5 recovery depends on the saved state showing exactly one place for every pallet. The residual risks are C3 (schema evolution on a developer's persistent database) and the still-unwritten µs domain invariant (C4); neither blocks. The M3 done-when is still not met — multi-shuttle assignment and the end-to-end three-shuttle/restart proof are lesson 09. **M3 stays in progress.**

---

## README

**Updated.** The verdict is a clean pass, so the M3 row's verification note is extended with the persistence/restart-survival proof. The state stays **`in progress`** — lesson 09 owns multi-shuttle assignment and the whole-M3 done-when.

---

## What the owner must do next

1. **Commit `docs/lessons/08-sql-persistence-and-restart.md`** so the L3 fix is in history, not just the working tree.
2. Read the explain-it questions out loud (including new #10).
3. Optionally tighten the test reset (C3) and write down the µs invariant (C4).
4. Lesson 09: multi-shuttle assignment and the end-to-end M3 proof.

---

## Cleanup (round 3)

- `docker compose -f docker/compose.yml down` run after the DB phases; `docker ps` shows no shuttle-fleet containers and no `shuttle-fleet_default` network.
- Throwaway `/tmp/opencode/rev08c` and its backups deleted; `/tmp/opencode/mqttinspect` left as found.
- `dotnet build-server shutdown` run.
- The `shuttle-fleet-build:latest` image tag left as found.
- No file under `src/` was modified. `docs/reviews/08-sql-persistence-and-restart.md` is extended and `README.md`'s M3 verification note is updated. The only working-tree change is the professor's uncommitted lesson L3 fix, which this review did not touch.
