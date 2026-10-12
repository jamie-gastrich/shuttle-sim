# Hints for lesson 09: Three shuttles finish a queue; state survives a restart

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- **One new class does the dispatching:** `src/FleetManager/TaskDispatcher.cs`, holding a `TaskBoard` and an `IFleetTaskRepository`. It lives in `FleetManager`, not `FleetCore` (record 15 §3). Do not touch `Program.cs`.
- **Exact method names matter** because the acceptance greps them: `AssignNextAsync`, `StartAsync`, `CompleteAsync`, `FailAsync`. Every method ends in `Async` and takes a `CancellationToken`.
- **The guard stays in `FleetTask`.** The dispatcher calls `task.Assign/Start/Complete/Fail`, then `await repository.SaveAsync(task, ct)`. Because the save is last, a refused move throws and saves nothing.
- **No clock, no ids, no I/O types in the dispatcher.** `now` is a parameter. No `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, `Contracts`.
- **Round-robin is a counter and a modulo:** the shuttle for the task at position `i` is `shuttles[i % shuttles.Length]`.
- **Two test files, both in `FleetManager.Tests`:** `TaskDispatcherTests.cs` (pure, in-memory fake, no trait) and `EndToEndTests.cs` (DB-backed, `[Trait("Category","Integration")]`). Never put these in `FleetCore.Tests`.
- **Resolve the mixed-states trap:** the live shift finishes all nine tasks, so nothing is left `InProgress`. Mixed states are lesson 08's test. Here the only non-`Done` task is the one you hold queued (`task-hold`).
- **The restart uses new instances:** a fresh `TaskBoard` and a fresh `PostgresFleetTaskRepository`, then reload with `LoadAllAsync`. The row order is `created_at ASC, task_id ASC`.
- **The DB tests share a table.** Put both classes in one `[Collection("Database")]` and define it with `DisableParallelization = true`, or they race.
- **Reset hygiene (review 08 C3):** ensure the table with `GetAsync` on a sentinel id that no test writes (it parses no rows), then `DELETE FROM fleet_tasks`. Do not use `LoadAllAsync` as the ensure step.
- **Connection string from configuration**, never a literal (record 14 §6). Never start Docker from a test.

---

## Tier 2 — direction

1. **Dispatcher file.** Create `src/FleetManager/TaskDispatcher.cs`, `namespace FleetManager;`, `using FleetCore;`. Constructor takes the board and repository. `AssignNextAsync` uses `board.NextQueued()`; the other three use `board.Get(taskId)`. Each calls the matching `FleetTask` method with `now`, then `await _repository.SaveAsync(task, cancellationToken)`.

2. **Pure tests.** Add `src/FleetManager.Tests/InMemoryFleetTaskRepository.cs` — a list-backed `IFleetTaskRepository` that upserts on `SaveAsync`, returns tasks in insertion order from `LoadAllAsync`, finds by id in `GetAsync`, and exposes a save count. Then `TaskDispatcherTests.cs` with the six named tests. No `[Trait]` here, so they run in the DB-less container.

3. **End-to-end test.** Add `src/FleetManager.Tests/EndToEndTests.cs`. Build nine `FleetTask`s with `CreatedAt = start.AddMinutes(i)` (whole seconds). Drive the round-robin loop: assign, then `StartAsync`, then `CompleteAsync`, advancing a `now` each step. Assert all nine are `Done` with the right `AssignedShuttleId`. Then add and save `task-hold`. Then build a new board + new repository, reload, and assert all ten and `NextQueued() == "task-hold"`.

4. **Shared helper.** Add `src/FleetManager.Tests/TestDatabase.cs` with `BuildConnectionString()` (lift the precedence out of `RestartSurvivalTests`) and `ResetAsync(repo, ct)`. The reset does `await repo.GetAsync(SentinelTaskId, ct);` then opens a connection and runs `DELETE FROM fleet_tasks`. Update `RestartSurvivalTests` to call it.

5. **Collection.** Add `src/FleetManager.Tests/DatabaseCollection.cs` with a `[CollectionDefinition("Database", DisableParallelization = true)]` class. Put `[Collection("Database")]` on both DB test classes.

6. **Do not touch** `src/FleetCore/`, `src/FleetCore.Tests/`, `src/FleetManager/Program.cs`, or `docker/Dockerfile.build`. The filter is already there; the contract stays v2.

7. **Run the phases in order.** Pure suite with no DB first (Phase 3), then the with-DB include run (Phase 4), then the container (Phase 5). The container proves the filter still excludes the two integration tests.

---

## Tier 3 — near-solution skeletons

These are **shapes**, not the solution. You still write the bodies and the tests.

**Dispatcher skeleton**
```csharp
namespace FleetManager;

using FleetCore;

public sealed class TaskDispatcher
{
    private readonly TaskBoard _board;
    private readonly IFleetTaskRepository _repository;

    public TaskDispatcher(TaskBoard board, IFleetTaskRepository repository) { /* store both */ }

    public async Task<FleetTask?> AssignNextAsync(string shuttleId, DateTime now, CancellationToken cancellationToken) { }
    public async Task StartAsync(string taskId, DateTime now, CancellationToken cancellationToken) { }
    public async Task CompleteAsync(string taskId, DateTime now, CancellationToken cancellationToken) { }
    public async Task FailAsync(string taskId, string reason, DateTime now, CancellationToken cancellationToken) { }
}
```

**Round-robin line**
```csharp
var shuttleId = shuttles[i % shuttles.Length];   // i = 0,1,2,3,…  ->  SH-01, SH-02, SH-03, SH-01,…
```

**Reset hygiene shape**
```csharp
public const string SentinelTaskId = "__fixture_probe__";

// ensure the table without parsing any existing row (review 08 C3), then delete
await repository.GetAsync(SentinelTaskId, cancellationToken);
// ... open a connection, then: DELETE FROM fleet_tasks
```

**Collection definition**
```csharp
[CollectionDefinition("Database", DisableParallelization = true)]
public sealed class DatabaseCollection { }
```

**Test names the acceptance walks**
```
AssignNextAsync_assigns_the_oldest_queued_task_and_saves_it
AssignNextAsync_returns_null_when_nothing_is_queued
A_rejected_transition_throws_and_saves_nothing
Round_robin_order_assigns_shuttles_in_turn
Reload_preserves_fifo_order
FailAsync_persists_the_failure
Three_shuttles_finish_queue_and_state_survives_restart
```

**Two-gate commands**
```bash
# DB-less pure suite (the container's command)
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"

# the DB tests truly run (not skipped)
docker compose -f docker/compose.yml up -d database
dotnet test src/FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration"
docker compose -f docker/compose.yml down
```

---

Stuck? These hints cover the key ideas without giving solution code. The acceptance criteria in the lesson file are the authoritative spec.
