# Lesson 09: Three shuttles finish a queue; state survives a restart

**Milestone:** M3 (Fleet manager core) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 08: SQL persistence so fleet state survives a restart](08-sql-persistence-and-restart.md) · **Decision:** [record 15](../decisions/15-m3-end-to-end-assignment-and-restart.md) and its supplement [record 15a](../decisions/15a-task-dispatcher-and-db-test-fixtures.md); builds on [record 11](../decisions/11-task-lifecycle-and-queue.md), [record 13](../decisions/13-fleet-state-persistence.md), [record 14](../decisions/14-db-test-gate-and-config.md), [record 03](../decisions/03-database.md), [record 04](../decisions/04-fresh-container-build.md)

**Concepts this assignment requires:**

- [ ] **The dispatcher** — one app-layer class that owns the task board, pulls the next queued task, hands it to a shuttle, and saves every transition. Taught in [The dispatcher](#1-the-dispatcher). Fixed by [record 15a §2](../decisions/15a-task-dispatcher-and-db-test-fixtures.md).
- [ ] **Round-robin assignment** — which shuttle gets which task when three are free: SH-01, SH-02, SH-03, then back to SH-01. Taught in [Round-robin assignment](#2-round-robin-assignment). Fixed by [record 15 §1](../decisions/15-m3-end-to-end-assignment-and-restart.md).
- [ ] **The end-to-end proof** — a live shift finishes the queue with three shuttles, then a restart reloads the same state from PostgreSQL. Taught in [The end-to-end proof](#3-the-end-to-end-proof). Fixed by [record 15 §1–§3](../decisions/15-m3-end-to-end-assignment-and-restart.md) and [record 15a §3–§5](../decisions/15a-task-dispatcher-and-db-test-fixtures.md).

Three new concepts. That is the cap.

Four things this assignment needs are **assignment requirements, not concepts**. They change where code lives and how the tests run, not the ideas above:

- the `Integration` trait and the two-gate container filter ([record 14](../decisions/14-db-test-gate-and-config.md));
- the config-driven connection string ([record 14 §6](../decisions/14-db-test-gate-and-config.md));
- DB-test fixture hygiene — ensure the table without parsing rows, then delete ([record 15a §4](../decisions/15a-task-dispatcher-and-db-test-fixtures.md)); this closes review 08's open consideration **C3**;
- DB-test isolation — both DB-backed classes join one non-parallel xUnit collection ([record 15a §5](../decisions/15a-task-dispatcher-and-db-test-fixtures.md)).

They are specified under [The assignment](#the-assignment).

**Not in this lesson:** traffic control and lane reservations (M4), fault injection, reassignment, and recovery (M5), WMS orders (M6), the dashboard (M7), mapping a task to a `moveCommand`, wiring a real run loop, and any wire-contract change.

---

## Where this fits

M3's done-when requires: *"a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart."*

Lesson 07 gave the pure model and the queue. Lesson 08 gave SQL persistence so state survives restart. Lesson 09 adds the piece that actually hands work out, and then proves the whole M3 sentence end to end.

| M3 piece | Lesson | What it adds |
|---|---|---|
| The task, its lifecycle, the pure queue | 07 (built) | The shape everything stands on |
| Repository abstraction, SQL save/load | 08 (built) | State that lives in PostgreSQL |
| Assignment to several shuttles; the end-to-end proof | **09 (this)** | The M3 done-when |

The key invariant from lesson 08 still holds: **decision logic stays separate from input and output.** `FleetCore` remains pure — no broker, no database, no network, and no knowledge that shuttles exist. The dispatcher is an **app-layer** class. It names shuttle ids, so it lives in `FleetManager`, not in `FleetCore`. That is the same split as `ShuttleSim` driving `ShuttleCore`.

There is a second, harder requirement. The spec's definition of done says the project *"builds in the Docker environment, tests pass."* The M0 gate is `docker run --rm shuttle-fleet-build` in a container with **no database**. The end-to-end test needs a real database, so it cannot be an ordinary test in that gate. Lesson 08 settled the mechanism: a DB-backed test carries an `Integration` trait, the container runs everything **except** that trait, and a separate command with the database up proves the test really executed (record 14 §5). Lesson 09's test reuses exactly that pattern.

### A note about `Program.cs` (a correction)

`src/FleetManager/Program.cs` today is the **M2 MQTT command demo** — `FleetManager move <shuttleId> <toLane>`. It has no board, no repository, and no run loop, and this lesson does **not** change it. An earlier draft of this lesson told you to "read the assignment loop in `Program.cs`". That was wrong.

The dispatcher is a **new M3 file**, `src/FleetManager/TaskDispatcher.cs`. It is exercised by tests now. Wiring it into a real run loop, and turning a task into a `moveCommand`, is deferred to a later lesson. Saying that out loud matters: a claim that code lives somewhere it does not is exactly the kind of thing a reviewer will catch.

### No hard-coded values

AGENTS.md is flat: *"No hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration."* The database connection string is built from `SHUTTLE_DB_CONNECTION_STRING`, or from `SHUTTLE_DB_HOST` + `DB_HOST_PORT` + `SHUTTLE_DB_USER` + `SHUTTLE_DB_PASSWORD` + `SHUTTLE_DB_NAME` with the local values only as defaults (record 14 §6). The two shuttle ids and the nine task ids in the test are **test fixtures**, not product configuration — they belong in the test, at a known value, so the test is deterministic.

---

## Verification table

Everything below was checked by a command or a file read on **2026-10-11**. The baseline build and suite ran against a throwaway copy of `src/` at `/tmp/opencode/l09verify`; the parallelization behaviour ran in a scratch xUnit project at `/tmp/opencode/parprobe`; the toy ran at `/tmp/opencode/l09toy`. All three are deleted afterwards. No file under `src/` was changed.

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline solution builds clean | copy of `src/`, `dotnet build ShuttleFleet.slnx -v m` | `Build succeeded. 0 Warning(s) 0 Error(s)` |
| Baseline pure suite is green with no DB | copy of `src/`, `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **7** `Passed!` lines, 0 `Failed!`; 22+4+8+2+18+5+5 = **64** tests; `FleetManager.Tests` shows **5** |
| The model is guarded (no state setter) | read `src/FleetCore/FleetTask.cs` | `State` is `private set`; only `Assign`/`Start`/`Complete`/`Fail` change it; wrong-state throws `TaskRejectedException` |
| The board hands out the oldest queued | read `src/FleetCore/TaskBoard.cs`; toy output | `NextQueued()` returns the first task with `State == Queued`; toy shows `task-1` first |
| The repository upserts and orders on load | read `src/FleetManager/PostgresFleetTaskRepository.cs` | `ON CONFLICT (task_id) DO UPDATE`; `ORDER BY created_at ASC, task_id ASC` |
| The existing DB test is config-driven and trait-gated | read `src/FleetManager.Tests/RestartSurvivalTests.cs` | `[Trait("Category","Integration")]`; `BuildConnectionString` uses `SHUTTLE_DB_CONNECTION_STRING` then the `SHUTTLE_DB_*`/`DB_HOST_PORT` parts; reset is a plain `DELETE` |
| No dispatcher exists yet | `grep -rnE 'TaskDispatcher\|AssignNext' src --include='*.cs'` | no matches |
| The contract has no task types | `grep -rniE 'FleetTask\|TaskState' src/Contracts` | no matches |
| The contract version is unchanged | `grep -E '^Version: ' docs/contract/v1.md docs/contract/v2.md`; `test ! -f docs/contract/v3.md` | `Version: 1`, `Version: 2`, no `v3.md` |
| The container build keeps record 04's shape | read `docker/Dockerfile.build` | line 5 `RUN dotnet build …`; line 6 `CMD [… "--filter", "Category!=Integration"]` |
| The solution has 14 projects | `grep -c '<Project Path' src/ShuttleFleet.slnx` | `14` |
| The pure test project stays pure | `grep -c 'FleetManager' src/FleetCore.Tests/FleetCore.Tests.csproj` | `0` |
| xUnit runs two test classes in parallel by default | scratch `/tmp/opencode/parprobe`, two classes each `Thread.Sleep(800)` | both entered at `01:16:50.820`; overlapped |
| A shared `[Collection]` serializes them | same scratch project, `[Collection("Database")]` + `DisableParallelization = true` | `B` exited `01:17:03.515` before `A` entered `01:17:03.520` |
| `CollectionDefinitionAttribute.DisableParallelization` exists in xUnit 2.9.3 | read `~/.nuget/packages/xunit.extensibility.core/2.9.3/lib/netstandard1.1/xunit.core.xml` | member present with docs |
| A toy finishes 9 tasks with 3 shuttles and reloads state | toy at `/tmp/opencode/l09toy`, `dotnet run` | 9 `done` (3 per shuttle, round-robin), `task-hold queued`, after reload `NextQueued -> task-hold`; exit 0 |
| A stale row breaks the old read-before-reset | review 08 round 3 (reproduced there): inserted `state='legacy_state'`, ran the test | `ArgumentException : Unknown task state wire name 'legacy_state'` before the reset could clear it |

Not yet verified: the dispatcher, the two test files, the shared helper, the collection file, and every acceptance phase below — this assignment specifies them and the reviewer measures them. No database was started while writing this lesson.

---

## Options survey

Record 15 already made these choices. Here they are as surveys, so you can defend them.

### Survey 1: Where does the assignment logic live? (→ [record 15 §3](../decisions/15-m3-end-to-end-assignment-and-restart.md))

| | A: in `FleetManager`, consuming `TaskBoard` + `IFleetTaskRepository` (chosen) | B: in `FleetCore` | C: a new `FleetAssignment` project |
|---|---|---|---|
| Keeps `FleetCore` unaware of shuttles | yes | **no** — it would name shuttle ids | yes |
| Testable without a broker or DB | yes (fake repository) | yes, but the core grows a shuttle concept | yes, but indirect |
| Matches the `ShuttleCore`/`ShuttleSim` precedent | yes | breaks it | clean, but more projects |
| New projects | none | none | one |

**Decision 1: the assignment logic (the dispatcher) lives in `FleetManager`.** It is app logic: it talks about shuttles, and shuttles are not the domain's business. `FleetCore` stays the pure task world. Rejected: putting it in `FleetCore` (blurs the I/O boundary the spec requires); a new project (one more seam for three concepts).

### Survey 2: Which shuttle gets which task? (→ [record 15 §1](../decisions/15-m3-end-to-end-assignment-and-restart.md))

| | A: round-robin — SH-01, SH-02, SH-03, SH-01, … (chosen) | B: all tasks to the first free shuttle | C: load-balanced by distance |
|---|---|---|---|
| Deterministic and easy to test | yes — the exact sequence is assertable | yes | no — needs a distance model |
| Fair across shuttles | yes, by construction | no — later shuttles can starve | yes |
| Enough for the M3 done-when | yes | yes, but hidden | yes, but over-specified |
| Builds on what we have | a counter and a modulo | a counter | positions and distances that do not exist yet |

**Decision 2: round-robin.** The spec says *"assign tasks to one shuttle, then several."* Round-robin is the smallest policy that is fair and whose exact result you can assert. Rejected: first-free (starvation, and no clear test); distance-based (needs a position model M4 will add later).

### Survey 3: How do we prove restart survival? (→ [record 15 §2](../decisions/15-m3-end-to-end-assignment-and-restart.md))

| | A: process-in-spirit — new `TaskBoard` + new repository instance + reload from PostgreSQL (chosen) | B: reload from an in-memory copy | C: three real shuttle processes, then restart the manager |
|---|---|---|---|
| Proves the data left the process | yes — it goes through real rows | **no** — nothing left memory | yes |
| Fits one test | yes | yes | no — needs a broker, three processes, and orchestration |
| Matches record 13 §6 wording | yes | no | yes, but far past M3 |

**Decision 3: process-in-spirit restart.** A fresh `TaskBoard` and a fresh `PostgresFleetTaskRepository` reload the same database. The old in-memory objects are thrown away, so survival depends only on what is in PostgreSQL. Three real shuttle processes are out of scope for M3.

### Survey 4: What does "finishes a queue" mean? (→ [record 15 §3](../decisions/15-m3-end-to-end-assignment-and-restart.md))

| | A: three shuttles each complete their assigned tasks and the assignable queue empties (chosen) | B: tasks are dropped after a timeout | C: two shuttles are enough |
|---|---|---|---|
| Matches the spec's words | yes — "finished by three shuttles" | no — dropped is not finished | no — the spec says three |
| Observable in one test | yes — every task is `Done` and `NextQueued()` finds nothing | yes, but it proves giving up | yes, but it changes the claim |

**Decision 4: finished means every task in the shift reached `Done` and the queue of waiting tasks is empty.** Rejected: timeout-cancelled (not finishing); two shuttles (the spec names three).

---

## The lecture

Today we are going to learn about three things: **the dispatcher**, **round-robin assignment**, and **the end-to-end proof**.

### 1) The dispatcher

A **dispatcher** is the part that hands work out. It takes the next waiting task, gives it to a shuttle, and records what happened.

Real-world picture: the shift supervisor at a repair shop's front desk. Tickets wait in a rack. The supervisor pulls the oldest ticket, calls a mechanic by name, and writes "given to Alice" in the log. When Alice starts, the log says "started". When she finishes, the log says "done". The supervisor does not repair cars. The supervisor moves tickets and keeps the log.

The pieces already exist. `TaskBoard` holds the tasks and hands out the oldest queued one. `FleetTask` guards every transition. `IFleetTaskRepository` saves and loads. What was missing is one small class that puts them together, in one place, so the app and the tests use the same code.

```csharp
// src/FleetManager/TaskDispatcher.cs — shape only; you write the body
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

Read the shape.

- The constructor takes the **board** it drives and the **repository** it saves through. The dispatcher **holds** both, so callers do not pass them on every call. This is the same idea as `ReliableCommandSender` holding the bus and the topic scheme.
- `AssignNextAsync` returns the task it assigned, or `null` when nothing is waiting. `Task<FleetTask?>` says "asynchronous, and the answer may be no task".
- `StartAsync`, `CompleteAsync`, and `FailAsync` each take a **task id** and a **time**. The time comes in; the class never reads a clock. That is the record 06 rule you have used since M1: time is an input, so tests are deterministic.
- Every method ends in `Async` and takes a `CancellationToken`. That is the dotnet-style house rule, and the repository already speaks it.
- `FailAsync` is here now because the dispatcher owns the whole lifecycle, and M5's recovery will call it. It is not throwaway.

Inside each method there is the same three-step shape:

```
1. find the task       (AssignNextAsync: board.NextQueued();  others: board.Get(taskId))
2. call the guard      (task.Assign / Start / Complete / Fail — these throw on a bad move)
3. save the task       (await repository.SaveAsync(task, cancellationToken))
```

Two rules make this safe and honest:

**Rule 1: the guard stays in `FleetTask`.** The dispatcher does not repeat the state checks. If `CompleteAsync` is called on a task that is still `Queued`, `FleetTask.Complete` throws `TaskRejectedException`. The error message names the current state. One place decides what moves are legal.

**Rule 2: the save comes after the guard, and a rejected move saves nothing.** The transition either throws at step 2, or the task changed state at step 2 and step 3 runs. There is no path where a refused move writes a row. A pure unit test proves this by counting the fake repository's saves: assign a task, try to assign it again, catch the exception, and assert the save count did not change.

**Purity:** the dispatcher file must contain no `DateTime.UtcNow`, no `Guid.NewGuid`, no `Task.Delay`, no `Thread.Sleep`, and no `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts` usage. Time and ids are inputs. Its only I/O is the repository it was handed. The acceptance greps for this.

**Why save after the move, not before sending a command:** at M3 the "action" is only an in-memory state change inside a single process. Saving right after it is correct. When a later lesson adds the run loop that *sends* a `moveCommand`, the order becomes save-then-send, so a crash between save and send leaves a task that is `InProgress` and can be found again. Write that down now; it is why the repository is the single write point.

Where you will meet this again: the deferred run loop will call `AssignNextAsync` once per idle shuttle; M4 will put a lane check in front of it; M5 will call `FailAsync` and then reassign. The database test calls it with the real Postgres repository; the pure tests call it with a fake.

---

### 2) Round-robin assignment

**Round-robin** means taking turns in a fixed circle. First SH-01, then SH-02, then SH-03, then SH-01 again.

Real-world picture: dealing cards at a table. One card to Alice, one to Bob, one to Cara, then back to Alice. Nobody gets two cards while someone has none.

For nine tasks and three shuttles the pattern is fixed:

| Task | Shuttle |
|---|---|
| task-1 | SH-01 |
| task-2 | SH-02 |
| task-3 | SH-03 |
| task-4 | SH-01 |
| task-5 | SH-02 |
| task-6 | SH-03 |
| task-7 | SH-01 |
| task-8 | SH-02 |
| task-9 | SH-03 |

The rule is a counter and one expression. The shuttle for the task at position `i` is:

```csharp
var shuttleId = shuttles[i % shuttles.Length];   // i starts at 0
```

The **modulo** operator (`%`) gives the remainder. `0 % 3` is `0`, `1 % 3` is `1`, `2 % 3` is `2`, `3 % 3` is `0` again. It wraps the counter around the circle of shuttle ids. This is the whole policy.

Two things make round-robin the right choice for M3.

**It is deterministic.** Every task gets a known shuttle. The test can assert not just "nine tasks are done" but "task-4 was assigned to SH-01". A vague policy would give a vague test.

**It is fair.** No shuttle sits idle while another takes every task. That fairness is the property the spec's "then several" is asking for at this size.

What round-robin does **not** do: it does not know which shuttle is nearest, which one is busy, or which lane is blocked. Those need a model we do not have yet. M4 adds traffic and M5 adds reassignment, and they will replace the choice of *which* shuttle with something smarter. The dispatcher's shape does not change — only the caller that picks `shuttleId` does.

Where you will meet this again: the end-to-end test drives round-robin to get a knowable result. The deferred run loop will pick the shuttle differently (whoever is idle), but it will still call `AssignNextAsync(shuttleId, …)`.

---

### 3) The end-to-end proof

The proof has **two phases** in one test. A live shift, then a restart.

**Phase A — the live shift.** Nine tasks wait. Three shuttles take turns. Each task goes `Queued → Assigned → InProgress → Done`, and every move is saved:

1. `AssignNextAsync(shuttleId, now, ct)` takes the oldest queued task and assigns it. Save.
2. `StartAsync(taskId, now, ct)` moves it to `InProgress`. Save.
3. `CompleteAsync(taskId, now, ct)` moves it to `Done`. Save.
4. Repeat until no task is queued.

At the end, the test asserts three things: all nine tasks are `Done`, each has the `AssignedShuttleId` that round-robin says it should, and `board.NextQueued()` finds nothing — the assignable queue is empty. That is the spec's "a queue of tasks is finished by three shuttles."

**Phase B — the restart.** This follows record 13 §6 exactly. The test throws away the in-memory board, creates a **new** `TaskBoard` and a **new** `PostgresFleetTaskRepository`, and reloads with `LoadAllAsync`. The rows come back in `created_at ASC, task_id ASC` order, so the board is rebuilt in the original order. The test asserts all ten tasks are present with identical fields, that order is preserved, and that `NextQueued()` returns the one task still waiting.

**Now the mixed-states trap.** An earlier draft of this lesson asked for one test that did *both* "tasks in every state (queued, assigned, in-progress, done, failed)" **and** "three shuttles each finish three tasks." Those cannot both be true. If the loop finishes every task, none is left `InProgress`, none is left `Assigned`, and none is `Failed`. The two requirements fight each other.

The fix is to give each test one job:

- **Lesson 08's test** (`RestartSurvivalTests.State_survives_restart_after_persisting_tasks`) already persists one task of each of the five states and proves they round-trip. That is where mixed states are proved. Do not duplicate it here.
- **Lesson 09's test** proves the *lifecycle in motion* and the *restart hand-off*: a queue is finished, and the one task that arrives at the end of the shift is still `Queued` after a restart.

So Phase A ends with all nine `Done`. Then, at the end of the shift, one extra task arrives and stays queued. Call it `task-hold`. It is the reason `NextQueued()` has a real answer after the restart — without it, there would be no queued task to return, and `NextQueued()` would prove nothing.

Real-world picture: the night shift finishes every repair on the board and closes the shop. One new ticket drops in the slot on the way out. A power cut wipes the computer's memory. In the morning, the manager starts a fresh computer, reads the database, and sees every finished ticket still marked done and the one new ticket still waiting at the front. Nothing was lost and nothing ran twice.

**What this proof does not do.** It does not run three real shuttle processes. Record 15 chose the process-in-spirit proof: new in-memory objects, same durable PostgreSQL rows. The shuttles in this test are simulated by the test loop, not by MQTT. Saying that plainly matters — a test that claims to prove three processes would be overclaiming.

**A precision note.** `FleetTask` stores `CreatedAt` in UTC at microsecond precision, matching PostgreSQL (record 13 §5; the `ToMicrosecondPrecision` truncation resolved review 08 C1). Use whole-second `CreatedAt` values in the test so a reloaded row compares `Equal` to the expected value. If you use `DateTime.UtcNow` with 100-nanosecond ticks, the row that comes back is not tick-equal and the equality assert fails.

Where you will meet this again: the deferred run loop will do Phase A for real, against live shuttles, and Phase B every time the manager restarts. The test is a rehearsal of code you will run.

---

## Worked example (toy, not the assignment)

A tiny toy shows the whole idea at a size you can hold in one glance: an in-memory store plays the repository, a mini dispatcher hands out tasks, three shuttles take turns, and a "restart" rebuilds from the store. It is **not** the assignment. The assignment uses the real `TaskDispatcher`, the real `IFleetTaskRepository`, a real PostgreSQL database, and xUnit. Do not copy this into `src/`.

Everything below ran in `/tmp/opencode/l09toy`, compiled against the real `src/FleetCore` source files, and was deleted afterwards. It is one console project. `L09Toy.csproj` (complete):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="/home/jamie/shuttle-sim/src/FleetCore/*.cs" />
  </ItemGroup>

</Project>
```

`Program.cs` (complete):

```csharp
using FleetCore;

// Worked-example toy for lesson 09: the assignment loop, finishing a queue with
// three shuttles, and restart survival — against an in-memory store.
// This is a TOY, clearly not the assignment (the assignment uses Postgres).
// It compiles the real FleetCore sources; nothing under src/ is changed.

// 1) A tiny in-memory store that plays the repository role.
var store = new InMemoryTaskStore();

// 2) A tiny dispatcher: takes the oldest queued task, hands it to a shuttle,
//    and persists every transition through the store.
var board = new TaskBoard();
var dispatcher = new ToyDispatcher(board, store);

// 3) Enqueue nine tasks (task-1..task-9).
var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
for (var i = 1; i <= 9; i++)
    board.Add(new FleetTask($"task-{i}", $"A01-L{i:00}", clock.AddMinutes(i)));

// 4) Three simulated shuttles take the queue in round-robin order and finish it.
var shuttles = new[] { "SH-01", "SH-02", "SH-03" };
var now = clock.AddMinutes(10);
var assigned = 0;
while (board.NextQueued() is not null && assigned < 9)
{
    var shuttleId = shuttles[assigned % shuttles.Length];
    var task = dispatcher.AssignNext(shuttleId, now);
    now = now.AddSeconds(1);
    dispatcher.Start(task!.TaskId, now);
    now = now.AddSeconds(1);
    dispatcher.Complete(task.TaskId, now);
    now = now.AddSeconds(1);
    assigned++;
}

// A refused transition changes nothing and is not saved. (Proves the guard.)
try
{
    dispatcher.Complete("task-1", now);
}
catch (TaskRejectedException exception)
{
    Console.WriteLine($"refused re-complete: {exception.Message}");
}

// 5) A new task arrives at the end of the shift and stays queued for the next one.
var holdTask = new FleetTask("task-hold", "B02-L01", now);
board.Add(holdTask);
dispatcher.Persist(holdTask);

Console.WriteLine($"--- shift over: {board.Count} tasks on the board (row order) ---");
foreach (var t in store.Rows())
    Console.WriteLine($"  {t.TaskId,-9} {t.State.ToWireName(),-11} assigned={t.AssignedShuttleId ?? "-"}");

// 6) "Restart": a brand-new board, reloaded only from the store.
var boardAfterRestart = new TaskBoard();
foreach (var row in store.Rows())
    boardAfterRestart.Add(row);

var next = boardAfterRestart.NextQueued();
Console.WriteLine($"--- after restart: {boardAfterRestart.Count} tasks, NextQueued -> {next?.TaskId ?? "(none)"} ---");
Console.WriteLine($"queue finished by three shuttles: {assigned} tasks, {assigned / 3} per shuttle");

// ---- toy pieces below ----

internal sealed class ToyDispatcher(TaskBoard board, InMemoryTaskStore store)
{
    public FleetTask? AssignNext(string shuttleId, DateTime now)
    {
        var task = board.NextQueued();
        if (task is null)
            return null;
        task.Assign(shuttleId, now);
        store.Save(task);
        return task;
    }

    public void Start(string taskId, DateTime now)
    {
        var task = board.Get(taskId);
        task.Start(now);
        store.Save(task);
    }

    public void Complete(string taskId, DateTime now)
    {
        var task = board.Get(taskId);
        task.Complete(now);
        store.Save(task);
    }

    public void Persist(FleetTask task) => store.Save(task);
}

internal sealed class InMemoryTaskStore
{
    private readonly List<FleetTask> _rows = new();

    public void Save(FleetTask task)
    {
        var index = _rows.FindIndex(existing => existing.TaskId == task.TaskId);
        if (index < 0)
            _rows.Add(task);
        else
            _rows[index] = task;
    }

    public IEnumerable<FleetTask> Rows() => _rows;
}
```

Run it:

```bash
cd /tmp/opencode/l09toy
dotnet run
```

Observed output (exit 0):

```
refused re-complete: Cannot complete a task that is not InProgress.
--- shift over: 10 tasks on the board (row order) ---
  task-1    done        assigned=SH-01
  task-2    done        assigned=SH-02
  task-3    done        assigned=SH-03
  task-4    done        assigned=SH-01
  task-5    done        assigned=SH-02
  task-6    done        assigned=SH-03
  task-7    done        assigned=SH-01
  task-8    done        assigned=SH-02
  task-9    done        assigned=SH-03
  task-hold queued      assigned=-
--- after restart: 10 tasks, NextQueued -> task-hold ---
queue finished by three shuttles: 9 tasks, 3 per shuttle
```

Read the output.

- `refused re-complete: Cannot complete a task that is not InProgress.` — after all nine are done, the toy calls `Complete` again on `task-1`. The guard in `FleetTask` refuses it, and because the save comes after the guard, the store is untouched. That is the "a rejected transition saves nothing" rule in one line.
- `task-1 done assigned=SH-01`, `task-2 done assigned=SH-02`, `task-3 done assigned=SH-03`, `task-4 done assigned=SH-01`, … — the round-robin pattern is visible in the data. The modulo expression picked the shuttle; the row remembered it.
- `task-hold queued assigned=-` — the task that arrived at the end of the shift is still waiting, and it has no shuttle.
- `--- after restart: 10 tasks, NextQueued -> task-hold ---` — the restart built a brand-new board from the store only. Every done task is still done, and the one waiting task is still at the front. That is the whole M3 sentence in one line.
- `queue finished by three shuttles: 9 tasks, 3 per shuttle` — three shuttles, nine tasks, three each.

Your assignment is this toy with the real names and the real plumbing: a `TaskDispatcher` in `FleetManager`, the `IFleetTaskRepository` from lesson 08, a real Postgres database, xUnit tests, and the two-phase end-to-end proof.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. Why does the dispatcher live in `FleetManager` and not in `FleetCore`? What would `FleetCore` have to learn about the world if it moved there, and which record forbids it?
2. `TaskDispatcher` never calls `DateTime.UtcNow`. Where does `now` come from, and what does passing it in buy the tests?
3. A rejected transition throws and saves nothing. Walk the three steps (`find`, `guard`, `save`) and say exactly which step throws and why `SaveAsync` is never reached.
4. Round-robin gives task-4 to SH-01. Show the modulo expression that decides a shuttle for the task at position `i`, and explain why `i % 3` wraps.
5. Why can the end-to-end test not both "finish all nine tasks" and "leave tasks in every state"? Which test proves mixed states instead, and why is that the right split?
6. After the live shift, why is `task-hold` added and saved? What would `NextQueued()` return after the restart if it were not there, and what would the restart proof then show?
7. The restart uses a **new** `TaskBoard` and a **new** repository instance. Why not just keep the old board and claim survival? What does the new instance prove?
8. Two DB-backed test classes both delete from `fleet_tasks`. What goes wrong if they run at the same time, and how does one `[Collection]` fix it?
9. Why does the test reset use `GetAsync` on a sentinel id instead of `LoadAllAsync`? What exact failure does the old way hide (review 08 C3)?

---

## The assignment

Build the M3 dispatcher, its pure tests, and the end-to-end proof that three shuttles finish a queue and the state survives a restart. This is the last piece of M3.

### Requirements

1. **Add `src/FleetManager/TaskDispatcher.cs`.** A `sealed` class with the exact API from [record 15a §2](../decisions/15a-task-dispatcher-and-db-test-fixtures.md):
   - constructor `TaskDispatcher(TaskBoard board, IFleetTaskRepository repository)`;
   - `Task<FleetTask?> AssignNextAsync(string shuttleId, DateTime now, CancellationToken cancellationToken)` — take `board.NextQueued()`; if it is null return null; otherwise call `task.Assign(shuttleId, now)` and `await repository.SaveAsync(task, cancellationToken)`, then return the task;
   - `Task StartAsync(string taskId, DateTime now, CancellationToken cancellationToken)`;
   - `Task CompleteAsync(string taskId, DateTime now, CancellationToken cancellationToken)`;
   - `Task FailAsync(string taskId, string reason, DateTime now, CancellationToken cancellationToken)`.
   The guards stay in `FleetTask`: do **not** re-check state in the dispatcher. A rejected transition must throw and save nothing, so `SaveAsync` is the last thing a successful method does. The file must contain **no** `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, or `Contracts`. The only I/O is the repository.

2. **Add the pure unit tests `src/FleetManager.Tests/TaskDispatcherTests.cs`** (not `FleetCore.Tests`). Use an in-memory fake `IFleetTaskRepository` (add `src/FleetManager.Tests/InMemoryFleetTaskRepository.cs`; it stores tasks in a list, upserts on save, returns them in insertion order on load, and exposes a save count so a test can prove a refused move saved nothing). This file carries **no** `Integration` trait so it runs in the DB-less container. At least these six `[Fact]`s, each with an `Assert`:
   - `AssignNextAsync_assigns_the_oldest_queued_task_and_saves_it`
   - `AssignNextAsync_returns_null_when_nothing_is_queued`
   - `A_rejected_transition_throws_and_saves_nothing`
   - `Round_robin_order_assigns_shuttles_in_turn`
   - `Reload_preserves_fifo_order`
   - `FailAsync_persists_the_failure`
   Use a fixed base time (`new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)`); never `DateTime.UtcNow`.

3. **Add the end-to-end test `src/FleetManager.Tests/EndToEndTests.cs`.** Class `EndToEndTests`; one `[Fact]` named `Three_shuttles_finish_queue_and_state_survives_restart`; carry `[Trait("Category", "Integration")]` and `[Collection("Database")]`. Two phases:
   - **Phase A (the live shift):** create nine tasks `task-1`…`task-9` with monotonic, whole-second `CreatedAt` values; add them to a `TaskBoard`; drive three shuttle ids `SH-01`, `SH-02`, `SH-03` in round-robin; for each assigned task call `StartAsync` then `CompleteAsync` through the dispatcher, so every transition is persisted. Assert all nine are `Done`, each `AssignedShuttleId` matches the round-robin shuttle for its position, and `NextQueued()` finds nothing.
   - **End of shift:** create one more task, `task-hold`, still `Queued`; add it to the board and `SaveAsync` it.
   - **Phase B (the restart):** create a **new** `TaskBoard` and a **new** `PostgresFleetTaskRepository(TestDatabase.BuildConnectionString())`; reload with `LoadAllAsync` in the order it returns; add each task to the new board in that order. Assert all **ten** tasks are present with identical `TaskId`, `ToLane`, `CreatedAt`, `State`, and `AssignedShuttleId`; assert FIFO order is preserved; assert `NextQueued()` returns `task-hold`.
   **Do not also demand mixed states in this test.** Lesson 08's test proves the five states round-trip. This test proves the queue is finished and the waiting task survives.
   Build the connection string from configuration, never a literal. Do **not** start Docker from the test (the build image has no `docker` binary; record 14 §2/§7).

4. **Add the shared test-database helper `src/FleetManager.Tests/TestDatabase.cs`** and refactor `RestartSurvivalTests` to use it. It exposes `BuildConnectionString()` with record 14 §6 precedence, and `ResetAsync(IFleetTaskRepository repository, CancellationToken cancellationToken)` that (a) ensures the table with a repository call that **parses no rows** — `GetAsync` on `SentinelTaskId = "__fixture_probe__"` — and (b) runs `DELETE FROM fleet_tasks`. This closes review 08 **C3**: the old step used `LoadAllAsync`, which parsed every row and blew up on a stale or unknown `state` string before the delete could run. `RestartSurvivalTests`'s behaviour must not change.

5. **Add the DB collection `src/FleetManager.Tests/DatabaseCollection.cs`** with `[CollectionDefinition("Database", DisableParallelization = true)]`, and put `[Collection("Database")]` on **both** `RestartSurvivalTests` and `EndToEndTests`. xUnit runs different test classes in parallel by default; without this, the two classes delete and write the same `fleet_tasks` table at the same time.

6. **Keep `FleetCore` pure and untouched.** Change nothing under `src/FleetCore/` or `src/FleetCore.Tests/`. `FleetCore.Tests` must have no `FleetManager` reference. `FleetCore` still has no DB references and no clock or ids.

7. **Do not change `Program.cs`, `docker/Dockerfile.build`, or the contract.** `Program.cs` stays the M2 MQTT demo; the run loop is deferred. The Dockerfile already builds with `RUN` and tests with the `Category!=Integration` filter; leave it. The contract stays v2 with no `v3.md`.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are asserted, not admired. Read every line's purpose before you run it.

```bash
# Phase 1 — the dispatcher and its API exist, with the pinned names
test -f src/FleetManager/TaskDispatcher.cs || echo "missing TaskDispatcher.cs"
grep -qE 'class TaskDispatcher' src/FleetManager/TaskDispatcher.cs
grep -qE 'TaskBoard' src/FleetManager/TaskDispatcher.cs
grep -qE 'IFleetTaskRepository' src/FleetManager/TaskDispatcher.cs
for m in AssignNextAsync StartAsync CompleteAsync FailAsync SaveAsync; do
  grep -qE "${m}\s*\(" src/FleetManager/TaskDispatcher.cs || echo "missing method: $m"; done   # nothing
```
```bash
# Phase 2 — dispatcher is pure (no clock, ids, waits, I/O types); FleetCore is untouched and pure
grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' src/FleetManager/TaskDispatcher.cs      # nothing
grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts' src/FleetManager/TaskDispatcher.cs                          # nothing
grep -rniE 'Npgsql|Postgres|Microsoft.Data.SqlClient' src/FleetCore --include='*.cs' --include='*.csproj'     # nothing
grep -c 'ProjectReference\|PackageReference' src/FleetCore/FleetCore.csproj                                  # prints 0
grep -c 'FleetManager' src/FleetCore.Tests/FleetCore.Tests.csproj                                            # prints 0 (review 08 S1)
```
```bash
# Phase 3 — build, and the pure suite with NO database needed (the container's command)
cd src
dotnet build
T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed!|Failed!' "$T"; rm -f "$T"
# expect RC 0, 7 "Passed!" lines, no "Failed!" — the integration test is excluded

# Phase 3b — the six new pure tests are really there, by name
L=$(mktemp); dotnet test ShuttleFleet.slnx --list-tests > "$L" 2>&1
for t in AssignNextAsync_assigns_the_oldest_queued_task_and_saves_it \
         AssignNextAsync_returns_null_when_nothing_is_queued \
         A_rejected_transition_throws_and_saves_nothing \
         Round_robin_order_assigns_shuttles_in_turn \
         Reload_preserves_fifo_order \
         FailAsync_persists_the_failure; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                                            # nothing
rm -f "$L"
cd ..
```
```bash
# Phase 4 — PROOF BOTH DB TESTS RAN: with the database up, the trait tests execute (not skipped)
docker compose -f docker/compose.yml up -d database
cd src
T=$(mktemp); dotnet test FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" \
  --filter "Category=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"
grep -E 'Passed FleetManager\.Tests\.(EndToEndTests\.Three_shuttles_finish_queue_and_state_survives_restart|RestartSurvivalTests\.State_survives_restart_after_persisting_tasks)|Passed:|Skipped:|Failed:' "$T"
rm -f "$T"
cd ..
docker compose -f docker/compose.yml down
# expect RC 0 and BOTH lines:
#   Passed FleetManager.Tests.EndToEndTests.Three_shuttles_finish_queue_and_state_survives_restart [.. ms]
#   Passed FleetManager.Tests.RestartSurvivalTests.State_survives_restart_after_persisting_tasks [.. ms]
# plus "Passed: 2". A skipped test prints "Skipped <name>" instead, not "Passed <name>".
```
```bash
# Phase 5 — PROOF THE CONTAINER IS GREEN: fresh container, no database, exit 0
docker build -f docker/Dockerfile.build -t shuttle-fleet-build .
docker run --rm shuttle-fleet-build; echo "container RC=$?"
# expect RC 0 and every suite line "Passed!" (7 lines). There is no database in this container,
# so this can only be green if the two integration tests were excluded. If the filter were wrong,
# they would run and fail (RC 1).
```
```bash
# Phase 6 — trait, collection, config-driven string, no docker binary use, Dockerfile untouched
grep -qE 'Trait\("Category",\s*"Integration"\)' src/FleetManager.Tests/EndToEndTests.cs
grep -q 'Three_shuttles_finish_queue_and_state_survives_restart' src/FleetManager.Tests/EndToEndTests.cs
grep -qE 'Collection\("Database"\)' src/FleetManager.Tests/EndToEndTests.cs
grep -qE 'CollectionDefinition\("Database"' src/FleetManager.Tests/DatabaseCollection.cs
grep -q 'SHUTTLE_DB_CONNECTION_STRING' src/FleetManager.Tests/TestDatabase.cs
grep -qE 'GetAsync' src/FleetManager.Tests/TestDatabase.cs                          # hygiene uses a repo call
grep -qF 'Category!=Integration' docker/Dockerfile.build
grep -q 'RUN dotnet build src/ShuttleFleet.slnx' docker/Dockerfile.build           # record 04 shape kept
# Match only a real docker launch: Process.Start, docker-compose, or a "docker compose/run/start/exec"
# command string. A bare "docker" path segment must NOT trip this (review 08 L3).
grep -qE 'Process\.Start|docker(-compose|[[:space:]]+(compose|run|start|exec))' src/FleetManager.Tests/EndToEndTests.cs \
  && echo "FAIL: test must not start docker" || echo "OK: test does not start docker"
```
```bash
# Phase 7 — contract unchanged
grep -qE '^Version: 2$' docs/contract/v2.md
grep -qE '^Version: 1$' docs/contract/v1.md
test ! -f docs/contract/v3.md
grep -rniE 'FleetTask|TaskState' src/Contracts --include='*.cs' --include='*.csproj'                 # nothing
```

**Proof details.** The assignment adds one product file (`TaskDispatcher.cs`) and five test-side files (`TaskDispatcherTests.cs`, `EndToEndTests.cs`, `InMemoryFleetTaskRepository.cs`, `TestDatabase.cs`, `DatabaseCollection.cs`), plus a small edit to `RestartSurvivalTests.cs`. It adds no new *project*, so the solution stays at 14 projects and the suite stays **7 `Passed!` lines** (that count is test *assemblies*, not tests). `FleetManager.Tests` shows **5 + 6 = 11** in the DB-less run and **13** with the database up. The end-to-end test must persist every transition through the dispatcher, finish all nine tasks, hold one task queued, reload into a fresh board through a fresh repository, and assert both FIFO order and `NextQueued() == task-hold`. If a reviewer removes the save from `CompleteAsync`, the end-to-end test must go red (the done tasks would not be done after reload); if a reviewer deletes the `task-hold` save, the `NextQueued()` assert must go red. Neither test may start Docker; the with-DB phase starts the database from the shell, and the container gate proves the filter still excludes the trait.

### Stretch goal

Prove the **fail path** survives restart too. In the live shift, add one extra task (say `task-blocked`) at the front, assign it to `SH-01`, and call `FailAsync("lane blocked", now, ct)` so it ends `Failed` with a reason. Persist it like the others. After the restart, assert it comes back `Failed` with the same `FailureReason` and `AssignedShuttleId`, and that it does **not** sit in front of `NextQueued()` (a failed task is never handed out). Keep `task-hold` as the one queued task. Accept: all suites stay green and the failure's fields round-trip.

### Not in this assignment

- **Traffic control and lane reservations** — M4. No lane segment is reserved in this proof.
- **Fault injection, reassignment, recovery, alarms** — M5. `Done` and `Failed` stay terminal.
- **Mapping a task to a `moveCommand` and a real run loop** — later. The dispatcher is tested, not wired.
- **WMS orders** — M6. **Dashboard** — M7.
- **Any new wire message.** A task is internal at M3; the contract stays v2.

---

## Closing

Read the spec's M3 line once more: *"A task queue with a clear lifecycle (queued, assigned, in progress, done, failed). Assign tasks to one shuttle, then several. Save state in SQL. Done when: a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart."*

Lessons 07, 08, and 09 now cover every clause. Lesson 07 built the pure task, its guarded lifecycle, and the first-in-first-out board. Lesson 08 put that board in PostgreSQL and proved a restart reloads it. Lesson 09 adds the dispatcher that hands work out, the round-robin policy that decides who gets it, and one end-to-end test that finishes a queue with three shuttles and reloads it after a restart.

Before you call M3 done, be ready to explain without notes:

- why the dispatcher is in `FleetManager` and not `FleetCore`;
- why the guard stays in `FleetTask` and a rejected move saves nothing;
- why round-robin is the right policy at this size, and what M4 and M5 will change about *which* shuttle is chosen;
- why the end-to-end test finishes every task and does **not** also try to leave mixed states — and which test does prove mixed states;
- why the restart uses a fresh board and a fresh repository, and why one held task makes `NextQueued()` mean something;
- why the two DB tests share one non-parallel collection, and why the reset uses a sentinel `GetAsync` before the delete.

When the reviewer passes this, M3 is done and the fleet has a working heart: a durable queue that survives a restart and can be finished by more than one shuttle. Next comes M4: traffic control, so two shuttles never use the same space at the same time.
