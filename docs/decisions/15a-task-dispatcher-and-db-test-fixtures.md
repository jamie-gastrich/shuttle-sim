# Decision 15a: The dispatcher API and the DB-test fixtures (supplements record 15)

**Date:** 2026-10-11
**Milestone:** M3 (Fleet manager core) — lesson 09: three shuttles finish a queue, state survives a restart.
**Status:** Accepted. This **supplements** [record 15](15-m3-end-to-end-assignment-and-restart.md); it does not replace it and does not rewrite it. Record 15 chose the assignment policy, where the logic lives, the restart-proof scope, and the meaning of "finishes a queue". This record pins the parts record 15 left open so lesson 09 is not writing unguided code: the exact dispatcher shape, the pure-test and integration-test files, the fixture hygiene that fixes review 08's open **C3**, and how the two DB-backed test classes avoid running at the same time.
**Builds on:** [record 15](15-m3-end-to-end-assignment-and-restart.md) (§3: assignment lives in `FleetManager`), [record 13](13-fleet-state-persistence.md) (the repository and the SQL board), [record 14](14-db-test-gate-and-config.md) (the `Integration` trait, the fresh-container filter, the config-driven connection string, DB-backed tests in `FleetManager.Tests`).
**Replaced by:** any later record that changes the dispatcher API, where the M3 tests live, or the DB-test fixture strategy. Append-only; records are never rewritten.
**Wire contract:** unchanged. The dispatcher drives internal state only. No message kinds change; no version bump. `docs/contract/v1.md` and `v2.md` remain as-is.

---

## 1. What we are deciding

Record 15 fixed the *what* (round-robin assignment in the app layer; a process-in-spirit restart proof; "finishes a queue" means three shuttles complete their assigned tasks and the assignable queue empties). It did not fix the *shape*. Lesson 09 needs four concrete things:

1. **The dispatcher class and its exact method names and signatures.** The M3 core is one small class, `TaskDispatcher`, in `src/FleetManager/TaskDispatcher.cs`. It holds the `TaskBoard` it drives and the `IFleetTaskRepository` it saves through, and it persists **every** lifecycle transition. Pinning the names makes the acceptance greps deterministic and makes the class the single thing later milestones call.
2. **Where the two new test files live.** A pure unit-test file (`TaskDispatcherTests`) with an in-memory fake repository, and one DB-backed end-to-end test (`EndToEndTests`) in `FleetManager.Tests`.
3. **DB-test fixture hygiene** so a test can reset its fixture **without first parsing whatever is in the table**. This closes review 08's open consideration **C3**.
4. **DB-test isolation** so the two DB-backed classes never touch the shared `fleet_tasks` table at the same time.

---

## 2. Decision A: the dispatcher API

`TaskDispatcher` is the app-layer owner of the task board. It is the smallest thing that can hand work out and save the result, and it is the thing M4 (traffic gating), M5 (fail and recover), and the deferred app run loop will call. The exact API:

```csharp
// src/FleetManager/TaskDispatcher.cs
namespace FleetManager;

using FleetCore;

public sealed class TaskDispatcher
{
    public TaskDispatcher(TaskBoard board, IFleetTaskRepository repository);

    public Task<FleetTask?> AssignNextAsync(string shuttleId, DateTime now, CancellationToken cancellationToken);
    public Task StartAsync(string taskId, DateTime now, CancellationToken cancellationToken);
    public Task CompleteAsync(string taskId, DateTime now, CancellationToken cancellationToken);
    public Task FailAsync(string taskId, string reason, DateTime now, CancellationToken cancellationToken);
}
```

Rules the file must keep:

- Every method that changes a task calls the repository's `SaveAsync` **after** the guarded transition. The upsert (`ON CONFLICT (task_id) DO UPDATE`) makes a re-save safe.
- The guards stay in `FleetTask`. The dispatcher does not re-check state. If a transition is rejected, `FleetTask` throws and **nothing is saved** (the `SaveAsync` call is after the throw point).
- No clock, no ids, no waiting, no I/O types in the file: no `DateTime.UtcNow`, no `Guid.NewGuid`, no `Task.Delay`, no `Thread.Sleep`, no `Npgsql`, no `MQTTnet`, no `Messaging`, no `Contracts`. Time and ids are inputs; the repository is the only I/O.

**Why a class, and not the loop inline in a test:** the same code must run in tests today and in the app run loop, M4, and M5 later. A loop copied into a test proves the test, not the product. This is the "no throwaway work" rule.

**Why `FleetManager` and not `FleetCore`:** the dispatcher names a **shuttle id**, and record 15 §3 keeps `FleetCore` unaware of shuttles. `FleetCore` stays pure; the app layer owns the shuttle concept. This is the same split as `ShuttleSim` driving `ShuttleCore`.

**Rejected:** the assignment loop inline in `Program.cs` or in the test (not carry-forward, and `Program.cs` is the M2 MQTT demo, not the run loop); putting `TaskDispatcher` in `FleetCore` (breaks the purity boundary record 15 §3 protects); a static helper (it would have to re-take the board and repo on every call instead of holding them).

**Why `FailAsync` is in the API now:** the class owns the whole lifecycle, and M5's reassignment needs `Fail`. It is small, it is exercised by one pure unit test, and it is not throwaway work.

---

## 3. Decision B: the test files and where they live

| | A: both in `FleetManager.Tests` (chosen) | B: pure tests in `FleetCore.Tests` | C: a new integration-test project |
|---|---|---|---|
| Can see `TaskDispatcher` | yes — the project references `FleetManager` | no — `FleetCore.Tests` must stay pure | yes |
| Keeps `FleetCore.Tests` a pure domain suite | yes | n/a | yes |
| New projects | none | none, but wrong | one, for two tests |
| Matches record 14 §3 | yes | no | deferred until integration tests grow |

**Decision B: `src/FleetManager.Tests/TaskDispatcherTests.cs` (pure, no DB) and `src/FleetManager.Tests/EndToEndTests.cs` (DB-backed, `Integration` trait).** `TaskDispatcherTests` uses an in-memory fake `IFleetTaskRepository` and carries **no** `Integration` trait, so it runs in the DB-less container. `EndToEndTests` carries `[Trait("Category", "Integration")]`, so the fresh container excludes it and a with-DB run proves it executed (record 14 §5).

The end-to-end test is one class, `EndToEndTests`, with one `[Fact]`:
`Three_shuttles_finish_queue_and_state_survives_restart`.

The pure tests are six behaviors (all in `TaskDispatcherTests`):

- `AssignNextAsync_assigns_the_oldest_queued_task_and_saves_it`
- `AssignNextAsync_returns_null_when_nothing_is_queued`
- `A_rejected_transition_throws_and_saves_nothing`
- `Round_robin_order_assigns_shuttles_in_turn`
- `Reload_preserves_fifo_order`
- `FailAsync_persists_the_failure`

**Rejected:** running the pure dispatcher tests inside the DB-backed test (they would need a database and would leave the DB-less container unable to test the dispatcher); a third test project (record 14 §3 deferred that until integration tests grow past a handful).

---

## 4. Decision C: fixture hygiene — ensure without parsing (closes review 08 C3)

Review 08 C3 found that the restart test's first call, `LoadAllAsync`, exists only to make sure the table exists, but it **parses every row** through `TaskStateWire.FromWireName` before the reset runs. One stale or unknown `state` string (from an older schema or a broken run) throws `ArgumentException: Unknown task state wire name '...'` and the test never reaches its own `DELETE`. That gets worse once lesson 09 and M5 write new row shapes into the same developer database.

**Decision C: the shared reset helper ensures the table with a repository call that parses no rows, then deletes.**

```csharp
// src/FleetManager.Tests/TestDatabase.cs — the shape, not the solution
internal static class TestDatabase
{
    public const string SentinelTaskId = "__fixture_probe__";

    public static string BuildConnectionString();          // record 14 §6 precedence

    public static async Task ResetAsync(IFleetTaskRepository repository, CancellationToken cancellationToken)
    {
        // Ensure the table without parsing any existing row (review 08 C3).
        await repository.GetAsync(SentinelTaskId, cancellationToken);
        // then DELETE FROM fleet_tasks through a fresh connection
    }
}
```

`GetAsync(SentinelTaskId, ...)` calls the repository's private `EnsureTableAsync` (every repository method does) and then runs `SELECT ... WHERE task_id = @task_id`. For a sentinel id no test writes, the reader returns no rows, so `ReadTask` never runs and no wire name is parsed. The `DELETE` then clears everything, including any stale row. Both DB-backed classes use this helper; `RestartSurvivalTests` is refactored to call it and its behavior is unchanged.

**Rejected:** keeping `LoadAllAsync` as the ensure step (this is exactly C3); putting `CREATE TABLE` back in the test (review 08 C2 removed the duplicated DDL because the repository already ensures the table); a public `EnsureTableAsync` on the repository (widens the domain interface for a test-only need).

---

## 5. Decision D: DB-test isolation — one collection, not two parallel classes

xUnit 2.9.3 runs each test **class** as its own collection and runs collections in parallel. Measured this session: two classes each took 800 ms and overlapped exactly (both entered at `01:16:50.820`). Two DB-backed classes that both `DELETE` and write the same `fleet_tasks` table would race.

**Decision D: both DB-backed classes join one collection, and that collection is defined as non-parallel.**

```csharp
// src/FleetManager.Tests/DatabaseCollection.cs
using Xunit;

namespace FleetManager.Tests;

[CollectionDefinition("Database", DisableParallelization = true)]
public sealed class DatabaseCollection
{
}
```

Both `RestartSurvivalTests` and `EndToEndTests` carry `[Collection("Database")]`. Measured: with the shared collection the two classes ran one after the other (`B` finished at `01:17:03.515` before `A` entered at `01:17:03.520`). `CollectionDefinitionAttribute.DisableParallelization` is present in the installed xUnit 2.9.3 (`xunit.core.xml`).

**Rejected:** giving each test its own table name (changes the schema and the repository for a test concern); relying on the `--filter` run to serialize them (a filter selects tests, it does not order them); `xunit.runner.json` global `parallelizeTestCollections: false` (turns off parallelism for the whole assembly, including pure tests that do not need it).

---

## 6. What would change these choices

- If the dispatcher grows past assignment (for example M4 lane reservation), split traffic decisions into their own class; do not let `TaskDispatcher` become a catch-all.
- If integration tests grow past a handful, move them into their own project (record 14 §3) and give that project its own two-gate command. New record.
- If the schema gains a migrations step, the test reset can call the migrator instead of a sentinel `GetAsync`. New record.
- If the run loop is added (a later lesson), wire `TaskDispatcher` into it there; this record deliberately leaves `Program.cs` untouched.

---

## 7. Verification table (measured)

Experiments ran in `/tmp/opencode`; no file under `src/` was changed.

| Claim | How checked | Result |
|---|---|---|
| xUnit runs two test classes in parallel by default | scratch `/tmp/opencode/parprobe`, two classes, each `Thread.Sleep(800)` and log entry/exit | both entered at `01:16:50.820`; ran overlapped |
| A shared `[Collection]` serializes them | same scratch project, both classes in `[Collection("Database")]`, definition `DisableParallelization = true` | `B` exited `01:17:03.515` before `A` entered `01:17:03.520`; ran one after the other |
| `CollectionDefinitionAttribute.DisableParallelization` exists in xUnit 2.9.3 | read `~/.nuget/packages/xunit.extensibility.core/2.9.3/lib/netstandard1.1/xunit.core.xml` | member present with docs |
| The repository ensures the table in every call | read `src/FleetManager/PostgresFleetTaskRepository.cs` (`EnsureTableAsync` at the top of `SaveAsync`/`LoadAllAsync`/`GetAsync`) | confirmed |
| `GetAsync` on a missing id parses no row | read `GetAsync`: `reader.ReadAsync` returns false, `ReadTask` not called | confirmed |

**Not yet verified:** the dispatcher file, the tests, the shared helper, the collection file, and the acceptance phases — lesson 09 specifies them and the reviewer measures them.

---

## 8. What this record unblocks

- Lesson 09 can specify a dispatcher with fixed names and signatures, two test files with fixed names, one hygienic reset helper, and one DB collection — no unguided choices for the owner.
- Review 08's C3 (fixture hygiene) and the parallel-DB-test hazard are closed in the same lesson that first needs them.
- M4, M5, and the deferred run loop all call the same `TaskDispatcher`; nothing here is throwaway.
