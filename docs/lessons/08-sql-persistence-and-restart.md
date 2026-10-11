# Lesson 08: SQL persistence so fleet state survives a restart

**Milestone:** M3 (Fleet manager core) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 07: The task and its lifecycle (the first M3 piece)](07-task-lifecycle-and-queue.md) · **Decision:** [record 13](../decisions/13-fleet-state-persistence.md); builds on [record 11](../decisions/11-task-lifecycle-and-queue.md), [record 12](../decisions/12-task-blank-inputs-and-exception-types.md), [record 03](../decisions/03-database.md)

**Concepts this assignment requires:**

- [ ] **Repository abstraction (separating I/O from domain)** — define `IFleetTaskRepository` in `FleetCore` so decision logic has no DB calls. Taught in [Repository abstraction](#repository-abstraction-separating-io-from-domain).
- [ ] **Persistence shape and mapping** — table schema, wire names for states, and mapping between `FleetTask` and rows (including FIFO order). Taught in [Persistence shape and mapping](#persistence-shape-and-mapping).
- [ ] **Reload and restart survival** — how to rehydrate a guarded model after restart and prove state survives. Taught in [Reload and restart survival](#reload-and-restart-survival).

Three new concepts. That is the cap.

**Not in this lesson:** assignment to several shuttles/policy (later), reassignment (M5), traffic (M4), WMS (M6), dashboard (M7), wire contract changes.

---

## Where this fits

M3’s done-when requires: *"a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart."* Lesson 07 gave the pure model and board. Lesson 08 adds SQL persistence so state survives restart. Lesson 09 adds assignment to several shuttles and completes M3’s end-to-end proof.

The key invariant: **decision logic stays separate from input/output.** `FleetCore` remains pure (no broker, no DB, no network). The repository is an abstraction consumed by the app.

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

Not yet verified: repository implementation, hydration, restart-survival test — assignment specifies them.

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

---

## The assignment

Build the repository abstraction, Postgres implementation, hydration support, and a restart-survival proof.

### Requirements

1. **Add hydration support to `FleetTask`**: add internal constructor that accepts `(taskId, toLane, createdAt, state, assignedShuttleId, failureReason, lastEventAt)`. Add `[assembly: InternalsVisibleTo("FleetManager")]` in `FleetCore` (or in `FleetTask.cs` with `using System.Runtime.CompilerServices;`). Keep existing public constructor unchanged and guarded.

2. **Add `IFleetTaskRepository` to `FleetCore`**: in `src/FleetCore/IFleetTaskRepository.cs` with the four methods as specified. `FleetCore` must still have no `Npgsql`/DB references.

3. **Add Npgsql to `FleetManager`**: add package reference `Npgsql` (compatible with net10.0). FleetManager can own DB connection/config.

4. **Implement Postgres repository in `FleetManager`**: e.g. `src/FleetManager/PostgresFleetTaskRepository.cs`. Create table `fleet_tasks` if not exists with columns as specified. Map `TaskState` ↔ wire name using `TaskStateWire.ToWireName` and reverse mapping. Load ordered by `created_at ASC, task_id ASC`. Use parameterized queries, async, `CancellationToken`. Handle nulls correctly.

5. **Add restart-survival test**: in `FleetCore.Tests` (or a new test that uses real DB) add a test `State_survives_restart_after_persisting_tasks` that:
- connects to Postgres (use connection string from config or `Host=127.0.0.1;Port=5432;Username=shuttle;Password=shuttle;Database=shuttle`)
- persists several tasks with different states (e.g. queued, assigned, in-progress, done, failed)
- creates new `TaskBoard` and loads via repo
- asserts all fields match and FIFO order preserved (oldest queued first)
- uses `docker compose -f docker/compose.yml up -d database` context as needed (test may assume DB is running or start it; acceptance will run in appropriate context)

6. **Keep purity**: `FleetCore` csproj has no `ProjectReference` to DB packages, no `PackageReference` to Npgsql. Only FleetManager references Npgsql.

### Acceptance criteria

Run from repo root unless stated. Exit codes read directly.

```bash
# Phase 1 — interface and hydration exist
test -f src/FleetCore/IFleetTaskRepository.cs || echo "missing IFleetTaskRepository.cs"
grep -q "InternalsVisibleTo" src/FleetCore/FleetTask.cs 2>/dev/null || grep -q "InternalsVisibleTo" src/FleetCore/*.cs 2>/dev/null
grep -q "internal FleetTask" src/FleetCore/FleetTask.cs

# Phase 2 — FleetCore purity (no DB refs)
grep -rniE 'Npgsql|Postgres|Microsoft.Data.SqlClient' src/FleetCore --include='*.cs' --include='*.csproj'  # nothing
grep -c 'ProjectReference\|PackageReference' src/FleetCore/FleetCore.csproj  # prints 0

# Phase 3 — FleetManager has Npgsql
grep -rni 'Npgsql' src/FleetManager/FleetManager.csproj 2>&1 | grep PackageReference

# Phase 4 — build and test
cd src
dotnet build
T=$(mktemp); dotnet test > "$T" 2>&1; RC=$?; echo "---RC:$RC---"; tail -15 "$T"; rm -f "$T"
# expect all suites pass (7 suites, FleetCore.Tests includes restart-survival test)

# Phase 5 — restart-survival test exists and is meaningful
L=$(mktemp); dotnet test --list-tests > "$L" 2>&1
grep -q "State_survives_restart_after_persisting_tasks" "$L" && echo "FOUND" || echo "MISSING"
rm -f "$L"

# Phase 6 — contract unchanged
grep -qE '^Version: 2$' ../docs/contract/v2.md
test ! -f ../docs/contract/v3.md
```

**Restart-survival proof details:** the test must persist multiple tasks (e.g. at least 3 with different states including queued and non-queued), load into new board via repo, assert all fields match and FIFO preserved. The DB must be accessible (docker database running). 

### Stretch goal
Add `DeleteAsync(taskId, ct)` and a test that deleted tasks don’t reappear on load. Keep scope small.

### Not in this assignment
Assignment policy, multiple shuttles, reassignment (M5), traffic (M4), WMS (M6), dashboard (M7), wire contract changes.

---

## Closing

State survives restart because persistence is explicit and the model is rehydrated cleanly through a narrow boundary. The repository keeps I/O out of `FleetCore`. FIFO order is preserved by ordering on load. The restart-survival proof is concrete and testable against a real DB.

Next (lesson 09): assignment to several shuttles and finish M3’s done-when with three shuttles finishing a queue and restart survival end-to-end.