# Lesson 08: SQL persistence so fleet state survives a restart

**Milestone:** M3 (Fleet manager core) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 07: The task and its lifecycle (the first M3 piece)](07-task-lifecycle-and-queue.md) · **Decision:** [record 13](../decisions/13-fleet-state-persistence.md) and [record 14](../decisions/14-db-test-gate-and-config.md); builds on [record 11](../decisions/11-task-lifecycle-and-queue.md), [record 12](../decisions/12-task-blank-inputs-and-exception-types.md), [record 03](../decisions/03-database.md), [record 04](../decisions/04-fresh-container-build.md)

**Concepts this assignment requires:**

- [ ] **Repository abstraction (separating I/O from domain)** — define `IFleetTaskRepository` in `FleetCore` so decision logic has no DB calls. Taught in [Repository abstraction](#repository-abstraction-separating-io-from-domain).
- [ ] **Persistence shape and mapping** — table schema, wire names for states, and mapping between `FleetTask` and rows (including FIFO order). Taught in [Persistence shape and mapping](#persistence-shape-and-mapping).
- [ ] **Reload and restart survival** — how to rehydrate a guarded model after restart and prove state survives. Taught in [Reload and restart survival](#reload-and-restart-survival).

Three new concepts. That is the cap.

The **test-gate mechanism** (a `[Trait]` filter so the DB test stays out of the DB-less container) is an **assignment requirement, not a fourth concept**. It is decided in [record 14](../decisions/14-db-test-gate-and-config.md) and specified under [The assignment](#the-assignment). It changes where the test lives and how the container runs it, not what the test proves.

**Not in this lesson:** assignment to several shuttles/policy (later), reassignment (M5), traffic (M4), WMS (M6), dashboard (M7), wire contract changes.

---

## Where this fits

M3’s done-when requires: *"a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart."* Lesson 07 gave the pure model and board. Lesson 08 adds SQL persistence so state survives restart. Lesson 09 adds assignment to several shuttles and completes M3’s end-to-end proof.

The key invariant: **decision logic stays separate from input/output.** `FleetCore` remains pure (no broker, no DB, no network). The repository is an abstraction consumed by the app.

There is a second, harder requirement here. The spec's definition of done says the project *"builds in the Docker environment, tests pass"*, and the M0 gate is `docker run --rm shuttle-fleet-build` in a container with **no database**. The restart-survival test needs a real database, so the test cannot be in the DB-less gate as an ordinary test. This lesson settles that: the test carries an `Integration` trait, the container runs everything **except** that trait, and a separate command run with the database up proves the test really executed. That mechanism is [record 14](../decisions/14-db-test-gate-and-config.md).

---

## Verification table

| Claim | How I checked | Result |
|---|---|---|
| PostgreSQL 17 runs via compose | `docker compose -f docker/compose.yml up -d database; docker compose ps` | started, accepting connections |
| Npgsql connects to compose DB | probe in `/tmp/opencode/npgsqlprobe` wrote/read rows | loaded 2 row(s) correctly |
| `FleetCore` currently has no DB refs | `grep -rniE 'Npgsql|Postgres' src/FleetCore --include='*.cs' --include='*.csproj'` | no matches |
| Existing model is guarded (no state setter) | read `FleetTask.cs`, `TaskBoard.cs` | state is `private set`, only methods change it |
| Contract unchanged | `grep -qE '^Version: 2$' docs/contract/v2.md; test ! -f docs/contract/v3.md` | v2, no v3 |
| Pure board semantics (FIFO) | lesson 07 toy proved `NextQueued()` returns oldest queued | verified earlier |
| The build image has no `docker` binary (so auto-start is impossible) | `docker run --rm shuttle-fleet-build which docker` | exit 1, no path; `which dotnet` prints `/usr/bin/dotnet` |
| xUnit 2.9.3 has no working dynamic skip | read `~/.nuget/packages/xunit.assert/2.9.3/.../xunit.assert.xml` (`SkipException.ForSkip`) | "only works in v3 and later of xUnit.net" |
| `--filter "Category!=Integration"` drops a trait test | scratch xUnit project in `/tmp/opencode/filterprobe` | exit 0, `Passed: 1`, integration test not run |
| `--filter "Category=Integration"` selects only that test | same scratch project | the failing integration test ran; exit 1 |
| The filter works at solution level (the container command shape) | scratch 2-project solution, `dotnet test SolProbe.slnx -v m --filter "Category!=Integration"` | exit 0, both pure tests `Passed` |

Not yet verified: the repository implementation, hydration, restart-survival test, the filtered container rebuild, and the with-DB integration run — the assignment specifies them and the reviewer measures them.

---

## Options survey

### Survey 1: Where does repository abstraction live? (record 13 §4)
| | A: `IFleetTaskRepository` in `FleetCore`; impl in `FleetManager` (chosen) | B: Only in `FleetManager`, no interface | C: New `FleetPersistence` project |
|---|---|---|---|
| Keeps `FleetCore` pure | yes | couples to DB | yes but extra project |
| Testable without DB | yes (mock interface) | harder | yes |
| Matches precedent (`ShuttleCore` pure) | yes | no | yes |

**Decision 1:** Interface in `FleetCore` (domain boundary), implementation in `FleetManager` (app boundary, owns config/connection). Keeps purity and is small.

### Survey 2: How to rehydrate guarded model? (record 13 §5)
| | A: Internal constructor + `InternalsVisibleTo("FleetManager")` (chosen) | B: Replay transition methods | C: Reflection to set private fields |
|---|---|---|---|
| Preserves invariants on new instances | allows loading saved valid state | hard to replay without original event times; can’t call Assign on already-Assigned state | bypasses all guards (dangerous) |
| Explicit and testable | yes | awkward | brittle |
| Matches C# patterns | common | possible but clumsy | discouraged |

**Decision 2:** Add internal hydration constructor to `FleetTask`; add `InternalsVisibleTo("FleetManager")` in `FleetCore`. This lets persistence reconstruct saved state while keeping public API guarded.

### Survey 3: How does the DB test stay out of the DB-less container? (record 14)

The restart-survival test needs a real database. The fresh-container gate has none. There is a real choice here, so we make it explicitly.

| | A: `[Trait("Category","Integration")]` + `--filter` (chosen) | B: runtime skip (early-return when the DB is unreachable) | C: run a database inside the build container |
|---|---|---|---|
| Container exits 0 with no DB | yes | yes | yes |
| Can you tell "ran" from "skipped"? | yes — the with-DB run shows `Passed`, not `Skipped` | **no** — xUnit reports a returned test as `Passed` | yes |
| Does it need a working dynamic skip? | no | **yes, and xUnit 2.9.3 has none** | no |
| Makes a fake green possible? | no | yes | no |
| Extra infrastructure in the build | none | none | a database service |

**Decision 3:** DB-backed tests carry `[Trait("Category", "Integration")]`; the fresh-container command runs everything except that trait. xUnit 2.9.3 has no working dynamic skip (its own docs say it needs v3), so a returned test would be reported as `Passed` while doing nothing — a fake green. The trait filter is visible and honest: the container says exactly what it excludes, and a second command with the database up proves the test ran.

**Where the DB test lives:** `FleetManager.Tests`, not `FleetCore.Tests`. The Postgres code is in `FleetManager`, that test project already references it, and this keeps the pure domain suite pure ([record 13](../decisions/13-fleet-state-persistence.md) §4, review 08 S1).

**Connection string is configuration, not a literal:** build it from `SHUTTLE_DB_CONNECTION_STRING`, or from `SHUTTLE_DB_HOST` + `DB_HOST_PORT` + `SHUTTLE_DB_USER` + `SHUTTLE_DB_PASSWORD` + `SHUTTLE_DB_NAME` with the local values as defaults. A hard-coded literal is rejected — it conflicts with AGENTS.md's "use configuration" rule and `docker/README.md`'s documented overrides.

---

## Lecture

Today we learn three things: **repository abstraction**, **persistence shape/mapping**, and **reload/restart survival**.

### 1) Repository abstraction (separating I/O from domain)

A **repository** is a boundary that hides how data is stored. The domain (`FleetCore`) doesn’t know if storage is memory, Postgres, or a file. The app decides.

Real-world: a repair shop’s ticket system — the front desk logic (“give me next ticket”) shouldn’t care if tickets live in a notebook or a database.

```csharp
// src/FleetCore/IFleetTaskRepository.cs (add)
namespace FleetCore;

public interface IFleetTaskRepository
{
    Task SaveAsync(FleetTask task, CancellationToken ct);
    Task SaveAllAsync(IEnumerable<FleetTask> tasks, CancellationToken ct);
    Task<IEnumerable<FleetTask>> LoadAllAsync(CancellationToken ct);
    Task<FleetTask?> GetAsync(string taskId, CancellationToken ct);
}
```

- Interface lives in `FleetCore` so domain can depend on abstraction. No DB types.
- Implementation lives outside (in `FleetManager`) where `Npgsql` and connection strings live.
- This keeps `FleetCore` pure: no `DateTime.UtcNow`, no GUIDs, no DB refs.

**Why:** tests can mock/fake the repo; logic is unit-testable without a real DB. **Where used later:** assignment loop (lesson 09) saves state after each transition.

---

### 2) Persistence shape and mapping

We persist the task board state. Schema (record 13 §5):

| Column | Type | Meaning |
|---|---|---|
| task_id | text PK | `FleetTask.TaskId` |
| to_lane | text NOT NULL | `FleetTask.ToLane` |
| created_at | timestamptz NOT NULL | `FleetTask.CreatedAt` (UTC) |
| state | text NOT NULL | wire name from `TaskStateWire.ToWireName` |
| assigned_shuttle_id | text NULL | `FleetTask.AssignedShuttleId` |
| failure_reason | text NULL | `FleetTask.FailureReason` |

**State as wire name:** use `queued`, `assigned`, `in_progress`, `done`, `failed` (underscore included). This matches the wire vocabulary and is human-readable.

**Order:** load with `ORDER BY created_at ASC, task_id ASC` to preserve FIFO. The board’s `NextQueued()` returns the oldest queued by enqueue order — `CreatedAt` is the enqueue time.

**Mapping:**
- Save: `state = task.State.ToWireName()`, copy other fields (nulls preserved).
- Load: parse wire name back to `TaskState`. For unknown, treat as error or fail fast.

Hydration constructor (add to `FleetTask`):

```csharp
// in FleetCore/FleetTask.cs
using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("FleetManager")]

public sealed class FleetTask
{
    // existing...
    internal FleetTask(string taskId, string toLane, DateTime createdAt, TaskState state,
        string? assignedShuttleId, string? failureReason, DateTime? lastEventAt)
    {
        TaskId = taskId;
        ToLane = toLane;
        CreatedAt = createdAt;
        State = state;
        AssignedShuttleId = assignedShuttleId;
        FailureReason = failureReason;
        _lastEventAt = lastEventAt;
    }
    // ...
}
```

Why internal + InternalsVisibleTo: keeps public constructor guarded (enforces rules for new tasks), but allows persistence layer (trusted) to reconstruct saved state.

**Pure means pure:** `FleetCore` still has no DB refs. The attribute is metadata only.

---

### 3) Reload and restart survival

**Reload:** read ordered rows, rehydrate each `FleetTask`, add to a fresh `TaskBoard` in that order. This preserves FIFO. The board’s `NextQueued()` will return the oldest queued task by `CreatedAt`.

**Restart survival proof:** create tasks (some queued/assigned/in-progress/done/failed), persist them, then simulate restart by creating **new** instances (`new TaskBoard()`, new repo instance pointing to same DB) and loading. Assert:
- all tasks present with same identity/fields
- order preserved (oldest first by `CreatedAt`)
- `NextQueued()` returns correct task
- state transitions still behave correctly on loaded tasks

This is a process-level restart in spirit. The spec requires state survives restart — this proof demonstrates it.

Real-world: power loss mid-shift; manager restarts and resumes correctly.

**What’s not persisted:** in-flight retry state, command envelopes, connection state (deferred). Tasks and their lifecycle state are persisted.

---

## Worked example (toy, not the assignment)

A tiny toy shows save+load to an in-memory “store” (conceptually like DB). Runs in `/tmp/opencode/persistoy` then cleaned up. **Not the assignment.**

Key idea: domain is pure; repo boundary separates storage.

```csharp
// Toy sketch (conceptual)
var board = new TicketBoard();
board.Add(new Ticket("t1","A01-L01", start)); // queued
// ... mutate ...
// persist
await repo.SaveAllAsync(board.Tasks, ct);
// restart
var board2 = new TicketBoard();
foreach(var t in await repo.LoadAllAsync(ct)) board2.Add(t);
// assert board2 has same state/order
```

The actual assignment uses Postgres via Npgsql.

---

## Say it out loud

1. Why keep `FleetCore` pure (no DB refs)? What bug does the repository abstraction prevent?
2. Why store state as wire name (`in_progress`) not enum? When loading, how do you convert back?
3. Why add an internal hydration constructor instead of making state settable? What does `InternalsVisibleTo` achieve?
4. Why load with `ORDER BY created_at ASC, task_id ASC`? How does this preserve FIFO for `NextQueued()`?
5. What constitutes “restart survival” in the acceptance? Why is creating new instances (not reusing) the right proof?
6. What is NOT persisted now (per record 13)? Why defer it?
7. Why does the DB test carry an `Integration` trait and get filtered out of the fresh container instead of skipping itself when the database is down? How would you prove to a reviewer that the test really runs, and not just skips everywhere? Why can the test not start Docker itself?

---

## The assignment

Build the repository abstraction, Postgres implementation, hydration support, and a restart-survival proof.

### Requirements

1. **Add hydration support to `FleetTask`**: add internal constructor that accepts `(taskId, toLane, createdAt, state, assignedShuttleId, failureReason, lastEventAt)`. Add `[assembly: InternalsVisibleTo("FleetManager")]` in `FleetCore` (or in `FleetTask.cs` with `using System.Runtime.CompilerServices;`). Keep existing public constructor unchanged and guarded.

2. **Add `IFleetTaskRepository` to `FleetCore`**: in `src/FleetCore/IFleetTaskRepository.cs` with the four methods as specified. `FleetCore` must still have no `Npgsql`/DB references.

3. **Add Npgsql to `FleetManager`**: add package reference `Npgsql` (compatible with net10.0). FleetManager can own DB connection/config.

4. **Implement Postgres repository in `FleetManager`**: e.g. `src/FleetManager/PostgresFleetTaskRepository.cs`. Create table `fleet_tasks` if not exists with columns as specified. Map `TaskState` ↔ wire name using `TaskStateWire.ToWireName` and reverse mapping. Load ordered by `created_at ASC, task_id ASC`. Use parameterized queries, async, `CancellationToken`. Handle nulls correctly.

5. **Add the restart-survival test in `FleetManager.Tests`** (not `FleetCore.Tests`; the Postgres code lives in `FleetManager` and the pure domain suite must stay pure). The test is named `State_survives_restart_after_persisting_tasks` and must:
   - carry `[Trait("Category", "Integration")]` so the DB-less container can exclude it ([record 14](../decisions/14-db-test-gate-and-config.md));
   - build its connection string from configuration, never a literal: use `SHUTTLE_DB_CONNECTION_STRING` whole if set, else `SHUTTLE_DB_HOST` (default `127.0.0.1`), `DB_HOST_PORT` (default `5432`), `SHUTTLE_DB_USER`, `SHUTTLE_DB_PASSWORD`, `SHUTTLE_DB_NAME` (defaults `shuttle`). A bad `DB_HOST_PORT` fails fast with a clear message. These names match `docker/README.md` and `docker/compose.yml`;
   - **not** start Docker itself. Do not call `docker`, `docker compose`, or run `Process.Start`. That cannot work: the build image has no `docker` binary (verified — see the verification table). The test assumes the database is reachable and fails loudly if it is not; the with-DB acceptance phase starts the database from the shell;
   - persist several tasks with different states (e.g. queued, assigned, in-progress, done, failed);
   - create a **new** `TaskBoard` and load via the repo;
   - assert all fields match and FIFO order is preserved (oldest queued first).

6. **Keep purity**: `FleetCore` csproj has no `ProjectReference` to DB packages, no `PackageReference` to Npgsql. Only `FleetManager` references Npgsql. `FleetCore.Tests` must not reference `FleetManager` (review 08 S1).

7. **Keep the fresh-container gate green (this is the M0/AGENTS gate).** Change `docker/Dockerfile.build` line 6 so the default command excludes the integration trait:
   ```
   CMD ["dotnet", "test", "src/ShuttleFleet.slnx", "-v", "m", "--filter", "Category!=Integration"]
   ```
   Keep record 04's shape: `RUN` builds, `CMD` tests. No `RUN dotnet test` layer. The `!` is safe because the `CMD` is the JSON exec form, not a shell.

### Acceptance criteria

Run from repo root unless stated. Exit codes read directly. There are **two** proof commands for the DB test: one that shows the DB-less container is green, and one that shows the DB test really ran against a real database.

```bash
# Phase 1 — interface and hydration exist
test -f src/FleetCore/IFleetTaskRepository.cs || echo "missing IFleetTaskRepository.cs"
grep -q "InternalsVisibleTo" src/FleetCore/FleetTask.cs 2>/dev/null || grep -q "InternalsVisibleTo" src/FleetCore/*.cs 2>/dev/null
grep -q "internal FleetTask" src/FleetCore/FleetTask.cs

# Phase 2 — FleetCore purity (no DB refs), and the pure test project stays pure
grep -rniE 'Npgsql|Postgres|Microsoft.Data.SqlClient' src/FleetCore --include='*.cs' --include='*.csproj'  # nothing
grep -c 'ProjectReference\|PackageReference' src/FleetCore/FleetCore.csproj  # prints 0
grep -c 'FleetManager' src/FleetCore.Tests/FleetCore.Tests.csproj  # prints 0 (review 08 S1)

# Phase 3 — FleetManager has Npgsql
grep -rni 'Npgsql' src/FleetManager/FleetManager.csproj 2>&1 | grep PackageReference

# Phase 4 — build, and the pure suite with NO database needed (the container's command)
cd src
dotnet build
T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed!|Failed!' "$T"; rm -f "$T"
# expect RC 0, 7 "Passed!" lines, no "Failed!" — the integration test is excluded

# Phase 5 — PROOF THE DB TEST RAN: with the database up, the trait test executes (not skipped)
cd ..
docker compose -f docker/compose.yml up -d database
cd src
T=$(mktemp); dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed FleetManager\.Tests\.RestartSurvivalTests\.State_survives_restart_after_persisting_tasks|Passed:|Skipped:' "$T"; rm -f "$T"
cd ..
docker compose -f docker/compose.yml down
# expect RC 0 and a line like:
#   Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks [.. ms]
# plus "Passed: 1". A skipped test would print "Skipped <name>" instead, not "Passed <name>".

# Phase 6 — PROOF THE CONTAINER IS GREEN: fresh container, no database, exit 0
docker build -f docker/Dockerfile.build -t shuttle-fleet-build .
docker run --rm shuttle-fleet-build; echo "container RC=$?"
# expect RC 0 and every suite line "Passed!". There is no database in this container, so
# this can only be green if the integration test was excluded. If the filter were wrong,
# the DB test would run and fail (RC 1).

# Phase 7 — the trait and the filtered CMD are really in place
grep -qE 'Trait\("Category",\s*"Integration"\)' src/FleetManager.Tests/RestartSurvivalTests.cs
grep -qF 'Category!=Integration' docker/Dockerfile.build
grep -qE 'Process\.Start|"docker"' src/FleetManager.Tests/RestartSurvivalTests.cs && echo "FAIL: test must not start docker" || echo "OK: test does not start docker"

# Phase 8 — contract unchanged
grep -qE '^Version: 2$' docs/contract/v2.md
test ! -f docs/contract/v3.md
```

**Restart-survival proof details:** the test must persist multiple tasks (e.g. at least 3 with different states including queued and non-queued), load into a new board via the repo, and assert all fields match and FIFO is preserved. The database must be reachable for the with-DB phase (Phase 5); the test assumes it and fails loudly if it is not. The test must **not** start Docker itself — that is impossible in the build image, and the with-DB phase starts the database from the shell.

### Stretch goal
Add `DeleteAsync(taskId, ct)` and a test that deleted tasks don’t reappear on load. Keep scope small.

### Not in this assignment
Assignment policy, multiple shuttles, reassignment (M5), traffic (M4), WMS (M6), dashboard (M7), wire contract changes.

---

## Closing

State survives restart because persistence is explicit and the model is rehydrated cleanly through a narrow boundary. The repository keeps I/O out of `FleetCore`. FIFO order is preserved by ordering on load. The restart-survival proof is concrete and testable against a real DB.

One more thing is settled here so the rest of M3 does not inherit a red build: a test that needs a database follows a clear pattern. It lives with the app (`FleetManager.Tests`), it carries an `Integration` trait, the DB-less container excludes that trait, and a separate with-database run proves the test executed. The connection string comes from configuration, with the local values only as defaults. Later DB-backed tests (lesson 09 assignment persistence, M5 recovery) reuse exactly this pattern.

Next (lesson 09): assignment to several shuttles and finish M3’s done-when with three shuttles finishing a queue and restart survival end-to-end.