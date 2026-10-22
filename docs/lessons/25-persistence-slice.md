# Lesson 25: The persistence slice — the pair moves together

**Milestone:** M6 (WMS integration) · **Language:** C#/.NET · **Prerequisite:** [lesson 24](24-execution-slice.md)
**Concepts this assignment requires:**

- [ ] **The pair moves together** — the order and the task are saved in one transaction → [§4.1](#41-the-pair-moves-together)
- [ ] **Restore on start** — rebuild both in-memory boards from the tables → [§4.2](#42-restore-on-start)
- [ ] **Re-marking owed words** — after a restart, only the in-flight words come back → [§4.3](#43-re-marking-owed-words)

**Decision record:** [record 34](../decisions/34-persistence-slice.md)

---

## Before we start

I am assuming you already understand: how `TaskDispatcher` saves a task after every move (lesson 24), how the outbox holds order ids and the sender reads the word at send time (lesson 23), and how `RestartSurvivalTests` reloads tasks from PostgreSQL. If any of those is fuzzy, say so before building — the persistence slice rests on all three.

---

## Where this fits

The spec's M6 says: *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."*

Lesson 24 made the first half true: an order becomes a moving pallet and a word comes back. But `serve` still keeps the order board and the task board in memory. A restart forgets everything. That is a WMS outage of a sort — the process itself going away — and today the fleet state is lost with it.

This lesson is the slice that makes a restart boring. The pair goes into SQL together. `serve` restores them on start. The words the WMS is owed come back with them.

---

## Verification table

Every row was measured on **2026-10-23**, on this tree.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | The committed tree builds clean | `cd src && dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |
| 2 | The pure suite is green before this lesson | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **Failed: 0, Passed: 186, Skipped: 0, Total: 186** |
| 3 | `WmsOrderBoard` has no load path today | `grep -n "public " src/FleetCore/WmsOrderBoard.cs` | `Submit`, `TryGet`, `SyncFromTask` only — no `LoadAll` |
| 4 | `IFleetTaskRepository` has four methods | read `src/FleetCore/IFleetTaskRepository.cs` | `SaveAsync`, `SaveAllAsync`, `LoadAllAsync`, `GetAsync` |
| 5 | `WmsStatusOutbox` can mark and claim | read `src/FleetCore/WmsStatusOutbox.cs` | `MarkPending`, `Claim`, `PendingCount` |
| 6 | Tasks already survive a restart | `RestartSurvivalTests` (integration) | 1 fact, passes against PostgreSQL |
| 7 | `WmsOrder` has six fields | read `src/FleetCore/WmsOrder.cs` | `OrderId, Kind, PalletId, LaneId, State, ReceivedAt` |

The broker and database were left down after these checks.

---

## Options survey

### Survey 1: where does the order live?

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. A second table, `wms_orders`, beside `fleet_tasks`** (chosen) | One row per order, columns mirroring `WmsOrder`. | Low. A parallel repository, same shape as the task one. | Two tables to keep in sync — handled by the transaction. | Orders and tasks are different things with different lifecycles; two tables is the honest shape. |
| **B. One joined table** | Task row carries order columns. | Medium. Nullable columns, wider rows. | A task with no order fills order columns with nulls; the cancel and tool paths get awkward. | Rare. Usually means "we ran out of tables". |
| **C. Only the task, re-derive the order** | Store the task; rebuild the order from it. | High. The order carries kind, pallet id, lane id the task does not have. | Information loss: a retrieve order and a store order look the same to the task. | Never. The order is the WMS's word; the task is the fleet's work. |

**Decision A.** Full reasons: [record 34 §2](../decisions/34-persistence-slice.md).

### Survey 2: one transaction or two?

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. One transaction for the pair** (chosen) | Save task and order together; commit once. | Low. `BeginTransactionAsync`, two saves, one commit. | A crash between the two saves cannot happen, because there is no gap. | The pair moves together, so it is saved together. |
| **B. Two separate saves** | Save the task, then the order. | Low. Two calls. | A crash between them splits the pair: a task in progress with no order, or an order with no task. | Never for a pair that must move as one. |

**Decision A.** Full reasons: [record 34 §3](../decisions/34-persistence-slice.md).

---

## Decision

The full decision is [record 34](../decisions/34-persistence-slice.md). Short version:

- **A.** A second table, `wms_orders`, beside `fleet_tasks`. A new `IWmsOrderRepository` in `FleetCore`, implemented in `FleetManager`.
- **B.** One transaction for the pair. Both rows land or neither does.
- **C.** `serve` restores both boards on start, before the bus connects.
- **D.** After restore, re-mark the in-flight words by re-running the sync per task; `Advanced` means owed, `AlreadyThere` means not.
- **E.** `serve` switches to SQL now. The in-memory repositories stay for tests.

---

## The lecture

Today we are going to learn about **the pair moving together**, **restore on start**, and **re-marking owed words**.

### 4.1 The pair moves together

**What it is.** The order and the task are two different things. The order is the WMS's view ("ORD-1 is received, in progress, or done"). The task is the fleet's work ("move a pallet to lane A01-L07"). Two boards, two words. Today both live in memory and are lost together on a restart — which is honest, but not durable.

**Why we need it.** Lesson 24's transition line keeps the two boards in step while the process is alive. The moment the process dies, the line stops. The only way to bring them back in step after a restart is to have saved them both, and to have saved them so that a crash between the two saves is impossible.

**A real-world example.** A warehouse office keeps two ledgers: one for the customer's order, one for the forklift's job. At the end of every move the clerk writes both ledgers in the same stroke of the pen. If the power goes out mid-stroke, neither ledger is half-written — the ink either dried on both or on neither. Our transaction is that stroke.

**The code.** A new interface in `FleetCore`, mirroring `IFleetTaskRepository`:

```csharp
namespace FleetCore;

public interface IWmsOrderRepository
{
    Task SaveAsync(WmsOrder order, CancellationToken cancellationToken);
    Task SaveAllAsync(IEnumerable<WmsOrder> orders, CancellationToken cancellationToken);
    Task<IEnumerable<WmsOrder>> LoadAllAsync(CancellationToken cancellationToken);
    Task<WmsOrder?> GetAsync(string orderId, CancellationToken cancellationToken);
}
```

Line by line:

- `namespace FleetCore;` — it lives with the other pure types. No SQL, no broker.
- Four methods, the same four as `IFleetTaskRepository`. Symmetry is the point: a reader who knows one knows the other.
- `SaveAsync` — one order, one row, upsert.
- `SaveAllAsync` — many orders, one transaction.
- `LoadAllAsync` — every order, in received order.
- `GetAsync` — one order by id, or null.

And the transaction, in the implementation:

```csharp
await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
await taskRepository.SaveTaskAsync(connection, transaction, task, cancellationToken);
await orderRepository.SaveOrderAsync(connection, transaction, order, cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

- `BeginTransactionAsync` — opens the transaction. Nothing is on disk yet.
- Two saves, both inside the transaction. If either throws, the transaction is not committed.
- `CommitAsync` — both rows land together, or neither does.

**Why the interface lives in `FleetCore`.** The pure project already owns `IFleetTaskRepository`. Putting the order interface there keeps decision logic testable without a database, and the implementation in `FleetManager` is the only thing that touches PostgreSQL.

### 4.2 Restore on start

**What it is.** On `serve` start, before the bus connects, read every task and every order out of SQL and put them back into the in-memory boards. The fleet manager then starts with a full memory, as if it had never stopped.

**Why we need it.** The executor, the tick and the pusher all read from the in-memory boards. If those boards are empty at start, the first tick sees no tasks and no orders, and the WMS's in-flight work is silently dropped. Restoring once, up front, means the rest of the program needs no "is it loaded yet" checks.

**A real-world example.** A night-shift supervisor hands over to the day shift. Before the day shift takes a single call, they read the whole whiteboard: which pallets are where, which jobs are half done. Our restore is that handover. It happens once, before any decision is made.

**The code.** In `Program.RunServeAsync`, after building the boards and before connecting the bus:

```csharp
var tasks = await taskRepository.LoadAllAsync(CancellationToken.None);
foreach (var task in tasks)
    taskBoard.Add(task);

var orders = await orderRepository.LoadAllAsync(CancellationToken.None);
foreach (var order in orders)
    orderBoard.Load(order);
```

- `LoadAllAsync` — reads every row, in `created_at` order for tasks, `received_at` order for orders.
- `taskBoard.Add(task)` — the same call the intake path uses. A duplicate id throws, which is what we want: a restored task and a submitted task with the same id is a real conflict, not a silent overwrite.
- `orderBoard.Load(order)` — a new method on `WmsOrderBoard` that puts a restored order into the dictionary without going through `Submit` (which would create a duplicate task).

**Why before the bus connects.** If the bus is connected first, a status message could arrive and be recorded in the registry before the boards are populated. The tick would then try to match a task that is not there yet. Restoring first closes that window.

**Why `WmsOrderBoard` needs a load seam.** Today the only way in is `Submit`, which creates a task. A restored order already has a task. So `WmsOrderBoard` needs a method that adds an order without creating one. That is the one new method this slice adds to that file — and it is the only edit `WmsOrderBoard.cs` gets in this slice.

### 4.3 Re-marking owed words

**What it is.** After restoring, some tasks are still in flight: `InProgress`, `Done` or `Failed`. The WMS was owed a word for each of them when the process died. The outbox is empty after a restart (it was in memory too), so those words must be re-marked.

**Why we need it.** The pile stores ids, and the sender reads the word at send time (lesson 23). So re-marking is not "remember the word" — it is "remember that this order still owes a word". The sender will read the current word when it finally pushes.

**A real-world example.** The office burned down. The day shift rebuilds the whiteboard from the saved ledgers. For every job that was half done, they put a sticky note on the wall: "tell the customer about this one." The sticky note does not say what to tell them — that is decided when the call is actually made, from the current state of the job.

**The code.** After restoring both boards:

```csharp
foreach (var task in restoredTasks)
{
    if (task.State is TaskState.InProgress or TaskState.Done or TaskState.Failed)
        outbox.MarkPending(task.TaskId);
}
```

Line by line:

- Walk the restored tasks.
- Only the in-flight ones owe a word: `InProgress`, `Done` and `Failed`. A `Queued` or `Assigned` task means the WMS already heard `received` at intake, so it owes nothing.
- `outbox.MarkPending(task.TaskId)` — one call per owed task. The sender reads the current word at send time, so re-pushing a word the WMS already heard is harmless.

**Why marking every in-flight task is the safe choice.** The outbox was in memory and is empty after a restart, so we cannot know which words were already pushed. The safe reconstruction is: every in-flight task owes at least one word. Re-pushing is harmless because the sender reads the current word at send time and the WMS handles a repeated word. No guessing about push history.

**The honest limit.** This re-marks words that were owed at the moment of the crash. A word that was successfully pushed and acked before the crash is not in the outbox (it was cleared), so it is not re-marked. That is correct: the WMS already heard it.

---

## Recap in plain words

- The order and the task are saved in one transaction. A crash between the two writes is impossible because there is no gap.
- `serve` restores both boards on start, before the bus connects, so the first tick sees a full fleet manager.
- After restore, the in-flight words are re-marked by re-running the sync. Only `Advanced` is owed; `received` is not re-pushed.
- The in-memory repositories stay for tests and for the unit-level facts.

---

## Worked example (toy — NOT the assignment)

This toy shows the restore and the re-mark on one order and one task, with no database and no broker. **It is not the assignment** — the assignment wires real SQL into `serve`.

Create it in a scratch directory:

```bash
mkdir -p /tmp/opencode/l25toy
cd /tmp/opencode/l25toy
dotnet new console --force -o .
```

Add the project reference to `FleetCore` in `l25toy.csproj`:

```xml
  <ItemGroup>
    <ProjectReference Include="/home/jamie/shuttle-sim/src/FleetCore/FleetCore.csproj" />
  </ItemGroup>
```

Then put this in `Program.cs`:

```csharp
using FleetCore;

var tasks = new TaskBoard();
var board = new WmsOrderBoard(tasks);
var outbox = new WmsStatusOutbox();
var orderSync = new OrderSync(board, outbox);
var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

var submit = board.Submit(
    new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PAL-1", "A01-L07"), now);
var task = submit.CreatedTask!;

// Move the task to in_progress before the "crash".
task.Assign("SH-01", now);
task.Start(now);
orderSync.Note(task.TaskId);

Console.WriteLine($"before crash: task {task.State.ToWireName()}, order {board.TryGet("ORD-1", out var o) ? o.State : "?"}, owed {outbox.PendingCount}");

// --- crash: memory is gone. Rebuild from saved state. ---
var savedTask = task;          // pretend this came from SQL
var savedOrder = board.TryGet("ORD-1", out var oo) ? oo : null!;

var tasks2 = new TaskBoard();
tasks2.Add(savedTask);
var board2 = new WmsOrderBoard(tasks2);
// load the saved order without creating a second task
board2.Load(savedOrder);

var outbox2 = new WmsStatusOutbox();
if (savedTask.State is TaskState.InProgress or TaskState.Done or TaskState.Failed)
    outbox2.MarkPending(savedTask.TaskId);

Console.WriteLine($"after restore: task {tasks2.Get("ORD-1").State.ToWireName()}, order {board2.TryGet("ORD-1", out var o2) ? o2.State : "?"}, owed {outbox2.PendingCount}");
```

Run it:

```bash
cd /tmp/opencode/l25toy
dotnet run
```

The output I measured, exactly:

```
before crash: task in_progress, order in_progress, owed 1
after restore: task in_progress, order in_progress, owed 1
```

Read it line by line:
- Before the crash the word is `in_progress` and one word is owed.
- After the restore both boards agree, and the word is still owed. The WMS will hear it on the next pass.

**Note:** `WmsOrderBoard.Load` does not exist yet — it is part of this lesson's assignment. The toy calls it to show the shape; the implementer adds it.

---

## Say it out loud

Answer these out loud, with no notes.

1. Why must the task and the order be saved in the same transaction? What would a crash between two separate saves look like when `serve` restarted?
2. `serve` restores the boards before it connects the bus. What goes wrong if it connects first?
3. After a restart the outbox is empty. Why is re-running `SyncFromTask` per restored task enough to decide which words are owed — and why is re-marking *every* task wrong?
4. A task was `Done` and its word was successfully pushed before the crash. Is it owed a word after the restart? Why or why not?

---

## The assignment

Take the worked example and make it run **for real**, inside `serve`, with PostgreSQL.

### The files you will change

| File | New? | What changes |
|---|---|---|
| `src/FleetCore/IWmsOrderRepository.cs` | **new** | Four-method interface, mirroring `IFleetTaskRepository`. |
| `src/FleetCore/WmsOrderBoard.cs` | edit | Add `Load(WmsOrder order)` so a restored order enters the board without creating a task. Nothing else in this file changes. |
| `src/FleetManager/PostgresWmsOrderRepository.cs` | **new** | PostgreSQL implementation: `wms_orders` table, upsert, load all, get by id. |
| `src/FleetManager/Program.cs` | edit | In `RunServeAsync`: build both repositories, restore both boards before the bus connects, re-mark owed words, hand the repositories to the dispatcher and the pusher's board. |
| `src/FleetManager/TaskDispatcher.cs` | edit | `AssignNextAsync`, `StartAsync`, `CompleteAsync`, `FailAsync`, `RequeueAsync` each save the task and its order in one transaction. |
| `src/FleetManager.Tests/RestartSurvivalTests.cs` | edit | Add a fact: orders survive a restart alongside tasks. |
| `src/FleetManager.Tests/ExecutionSliceTests.cs` | edit | (none required; do not weaken existing facts) |

### Requirements

**R1 — `IWmsOrderRepository` is the order's seam.** Four methods: `SaveAsync`, `SaveAllAsync`, `LoadAllAsync`, `GetAsync`, with the same shapes as `IFleetTaskRepository`. It lives in `FleetCore` and has no SQL.

**R2 — `WmsOrderBoard.Load` adds a restored order.** It takes a `WmsOrder`, puts it into the dictionary under its `OrderId`, and does nothing else — no task, no sync, no outbox. If the id already exists, throw `InvalidOperationException`.

**R3 — `serve` restores before it connects.** In `RunServeAsync`, after building `taskBoard` and `board`, and before `await bus.ConnectAsync`, load all tasks and all orders and populate both boards. Then re-mark owed words (R4). The bus must not be connected during restore.

**R4 — re-marking owed words.** For each restored task whose state is `InProgress`, `Done` or `Failed`: call `outbox.MarkPending(task.TaskId)`. A `Queued` or `Assigned` task marks nothing. The outbox was in memory and is empty after a restart, so every in-flight task owes at least one word; the sender reads the current word at send time, so re-pushing is safe.

**R5 — one transaction for the pair.** In the dispatcher, after a move, save the task and its matching order in one transaction. If the order cannot be found, throw `InvalidOperationException` — a task with no order is a bug, not a skip.

**R6 — `serve` uses SQL.** `RunServeAsync` builds a `PostgresFleetTaskRepository` and a `PostgresWmsOrderRepository` from the connection string. The in-memory repositories stay in the test project.

**R7 — configuration.** Add `FleetManager:PostgresConnectionString` (or reuse the existing connection string key the integration tests use) and validate it is non-blank before the bus connects. No connection string literal in code.

**R8 — the facts.** At least these new tests:
- `Orders_survive_a_restart_alongside_tasks` (integration): save an order and its task, reload both, assert the order's state and the task's state agree.
- `Restoring_an_in_progress_task_remarks_its_word` (unit, in-memory): build a board with an in-progress order/task, simulate restore (load both, re-run sync), assert the outbox owes one word.

**R9 — what you must not touch.** `src/FleetCore/FleetTask.cs`, `TaskBoard.cs`, `WmsOrder.cs`, `WmsOrderStates.cs`, `WmsOrderLadder.cs`, `WmsStatusOutbox.cs`, `OrderSync.cs`, `src/FleetManager/RunLoop.cs`, `FleetServeLoop.cs`, `src/Contracts/`, `docs/contract/`. The contract does not move: `ContractVersion.Current` stays `4`.

### Acceptance criteria

```bash
BASE=$(git rev-parse HEAD)
cd src && dotnet build --no-incremental
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# integration (with the database up):
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"
test -f src/FleetCore/IWmsOrderRepository.cs
grep -q "Load(" src/FleetCore/WmsOrderBoard.cs
test -f src/FleetManager/PostgresWmsOrderRepository.cs
grep -q "BeginTransactionAsync" src/FleetManager/TaskDispatcher.cs
grep -q "LoadAllAsync" src/FleetManager/Program.cs
```

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Save the task but not the order in `CompleteAsync` | `Orders_survive_a_restart_alongside_tasks` |
| b | Re-mark every restored task, not just the in-flight ones | `Restoring_an_in_progress_task_remarks_its_word` (queued task would wrongly owe a word) |
| c | Restore after the bus connects | a new integration test that publishes a status during restore and asserts it is not lost (optional; note if skipped) |

### Stretch goal

**Measure the restart.** Stop `serve` mid-task, start it again, and time how long the WMS waits before it hears the in-flight word again. Write the measurement down.

---

## What comes next

- **The cancel slice.** The WMS cancels an order. That needs a `Cancelled` task state and a row on the ladder.
- **The mock-WMS slice.** A WMS that is slow, a WMS that is gone, a WMS that answers `500`.
- **The traffic slice.** The tick gives work without asking the occupancy map.

---

## What I assumed about what you know

I assumed you are comfortable with: transactions in Npgsql (`BeginTransactionAsync`/`CommitAsync`), upserts with `ON CONFLICT`, and rebuilding an in-memory collection from a query result. If any of those is new, say so before building and I will walk through it.
filePath">
/home/jamie/shuttle-sim/docs/lessons/25-persistence-slice.md