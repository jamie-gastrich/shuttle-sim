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
