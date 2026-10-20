# Lesson 21: From an accepted order to a fleet task

**Milestone:** M6 (WMS integration) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 20: The WMS front door](20-wms-rest-front-door.md) · **Decision:** [record 29](../decisions/29-wms-order-to-task-mapping.md)

**Concepts this assignment requires:**

- [ ] **A task is the fleet's unit of work, and an order becomes one.** What a `FleetTask` carries, and where each of its values comes from. Taught in [1) A task is the unit of work](#1-a-task-is-the-unit-of-work).
- [ ] **One accepted order creates exactly one task.** The intake table, and why a bad order, a retry, and a conflict all create none. Taught in [2) One accepted order, exactly one task](#2-one-accepted-order-exactly-one-task).
- [ ] **The order and its task live and die together.** Where the task lands, what a restart does, and how the two state ladders line up. Taught in [3) The pair lives and dies together](#3-the-pair-lives-and-dies-together).

Three new concepts. That is the cap.

**Not in this lesson:** running the task (no dispatcher, no MQTT send from `serve`); persisting the order or the task; pushing order status back to the WMS; cancelling an order; load/unload commands; an outbound lane for retrieve; the dashboard (M7); and **any contract change** — `ContractVersion.Current` stays `4`, and both `docs/contract/v4.md` and `docs/contract/wms-rest.md` are untouched.

**The end-product fact:** after lesson 20, the fleet manager can *accept* an order. It cannot act on one. The order sits in `WmsOrderBoard` with state `received`, and the fleet's to-do list is empty. This lesson is the bridge: an accepted order becomes a real `FleetTask` on the board the dispatcher drains. It is the first time the WMS can cause the fleet to plan work.

---

## First: close the loop on review 20

Review 20's Explain-it check is still unanswered. It is short and it is not optional. Answer these **out loud, no notes**, before you write code. Put a short written version in your next review notes so the reviewer can see it.

**The three questions:**

1. `WmsOrderBoard.Submit(request, DateTime now)` takes the clock as an argument, but `WmsOrderValidator.Validate(request)` takes none. Why is time an *input* to the board, and what does the retry test prove *only because* the clock is passed in?
2. `FleetCore` has no `Results`, no `HttpContext`, no `IResult`; the board owns the only lock and the endpoint locks nothing. What breaks if the lock moves into the endpoint, and what breaks if validation runs after `Submit`?
3. A re-sent order with the same four values returns `200`; the same `orderId` with one changed value returns `409`. Why is "identical body" the test that separates a retry from a different order, and what would go wrong for the WMS if the retry returned `201` and the work ran twice?

**The five "unclear or copied" blocks:** the shutdown awaits (now two separate `try`/`catch` blocks at `Program.cs:211-226` after the review follow-up), `WmsOrderBoard.cs:12,19-20` (the `!` on nullable fields), `WmsRoutes.cs:8` (template vs built path), the route table in `docs/contract/wms-rest.md`, and the `BlockedPathSeverity` indentation in `Program.cs:149-152`. Say one sentence about each.

**Question 2 is folded into this lesson.** Concept 2 below adds a second thing to the board's lock and a second thing the endpoint must not do. If you cannot answer question 2, concept 2 will not make sense either. So answer it first, then read on.

---

## Where this fits

M6 says: *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."*

Lesson 20 built the way in. This lesson turns an accepted order into work. The slice after this pushes status back to the WMS. The execution slice wires the runtime dispatcher so a `Queued` task actually moves a shuttle.

The pieces already exist. `FleetTask` is a move task from M3. `TaskBoard` is its queue. `WmsOrderBoard` and `WmsOrder` are from lesson 20. This lesson composes three of them and adds one rule: **an accepted order and a queued task are one fact.**

**Forward references.** The next slice reads each order's state and pushes it to a configured WMS URL. The status-push slice needs the ladder table in concept 3 to know which word to send. The execution slice wires `TaskDispatcher` to the `TaskBoard` created here. M7's dashboard reads the same order board and the same task board.

---

## Verification table

Everything below was checked today (2026-10-19), in this repo or in scratch copies under `/tmp/opencode`. Scratch copies were deleted afterwards. Nothing was left running.

| Claim | How I checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| Baseline builds clean | clean copy of `src` in `/tmp/opencode/l21base`; `dotnet build` | **0 Warning(s), 0 Error(s)** |
| Baseline pure suite is green | same copy; `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0; **159 passed / 0 failed / 159 total** (FleetCore 35, Contracts 8, TrafficCore 6, ShuttleCore 31, Messaging 10, MqttMessaging 3, ShuttleSim 10, FleetManager 56) |
| Nothing maps an order to a task today | `grep -rniE "MapToTask|CreateTask|order.*FleetTask|FleetTask.*order" src --include=*.cs` | no matches |
| `serve` has no task board | `grep -n "FleetTask\|TaskBoard" src/FleetManager/Program.cs` | no matches; the only board is `var board = new WmsOrderBoard();` (line 164) |
| The order board knows no task type | `grep -n "FleetTask\|TaskBoard" src/FleetCore/WmsOrderBoard.cs` | no matches |
| `TaskBoard.Add` rejects a duplicate id | read `src/FleetCore/TaskBoard.cs:6-12` | throws `TaskRejectedException` on a duplicate `TaskId` |
| `FleetTask` takes id, lane, and time as inputs | read `src/FleetCore/FleetTask.cs:10-24` | blank id or lane throws; `State` starts `Queued` |
| `FleetTask` truncates `CreatedAt` to microseconds | read `src/FleetCore/FleetTask.cs:87-88` | `new(value.Ticks - value.Ticks % 10, value.Kind)` |
| The MQTT contract did not move | `grep -n "Current = " src/Contracts/MessageTypes.cs` | `public const int Current = 4;` |
| The toy behaves as specified | `/tmp/opencode/l21toy`; `dotnet run` | output shown in the worked example |
| A mapping on the retry path would duplicate work | `/tmp/opencode/l21toy-mut`; `dotnet run` | the retry reports `AlreadyKnown` but adds a second task |

Not yet verified: the code this lesson asks you to build. That is the assignment.

---

## Options survey (design choices)

### Survey 1: Where does the mapping live?

| | A: inside `WmsOrderBoard.Submit`, on `Created` (chosen) | B: a new `WmsOrderIntake` class that wraps both boards | C: in the `Program.cs` endpoint |
|---|---|---|---|
| One lock for "order stored + task added" | **yes** — the board's existing lock | no — a second lock, or none | no — the endpoint locks nothing |
| A half fact (order with no task) is possible | **no** | yes — `WmsOrderBoard.Submit` stays callable on its own | yes — the endpoint is free to call either |
| Pure and unit-testable | yes | yes | no — `FleetCore` cannot see web types |
| New class to justify | none | one | none |

### Survey 2: What is the task's id?

| | A: the order id (chosen) | B: the order id with a prefix | C: a generated id plus a stored link |
|---|---|---|---|
| Trace a task back to the WMS order | **yes** — same string | needs a prefix strip | needs a lookup |
| Collision risk in `serve` today | none — the only task source is WMS orders, and order ids are unique | none | none |
| Extra machinery | none | a transform at every lookup | an id source and a link store |

### Survey 3: What survives a restart?

| | A: nothing — both boards in memory, together (chosen) | B: persist the order and the task pair now | C: persist the order only |
|---|---|---|---|
| Consistent after a restart | **yes** — the pair drops together | yes | no — a persisted order with no task |
| Something consumes the persisted row | n/a | no — nothing in `serve` runs a task yet | no |
| Concept count for this lesson | 3 | 4 or more | 4 |

Full reasons are in [record 29](../decisions/29-wms-order-to-task-mapping.md).

**Confirming against the installed source, not memory.** The `TaskBoard` duplicate guard, the `FleetTask` constructor, and the microsecond truncation come from reading `src/FleetCore/TaskBoard.cs` and `src/FleetCore/FleetTask.cs`. The absence of any mapping comes from the grep above. The `WmsOrderBoard` shape comes from lesson 20's reviewed code.

---

## Decision

[Record 29](../decisions/29-wms-order-to-task-mapping.md) settles all of it:

- **A.** The mapping lives in `WmsOrderBoard`, inside its existing lock, and runs only on a `Created` outcome. The endpoint does not change.
- **B.** The task is `new FleetTask(order.OrderId, order.LaneId, order.ReceivedAt)`. Task id = order id. Both kinds (`store`, `retrieve`) map to a move to the order's lane. The task starts `Queued`.
- **C.** `Created` → exactly one task. `AlreadyKnown`, `Conflict`, and validation errors → none.
- **D.** `RunServeAsync` creates one `TaskBoard` and hands it to the order board. In memory, together. Persistence is deferred to the execution slice (this supersedes the *timing* in record 28 §6, not its destination).
- **E.** The order ladder and the task ladder are lined up in a fixed table, so the status-push slice never guesses.

Read the record before you write code. Every requirement below is decided there.

---

## The lecture

Today we are going to learn about **turning an order into a task**, **the rule that one order makes exactly one task**, and **where the pair lives and what a restart does**.

### 1) A task is the unit of work

A **task** is one piece of work the fleet must do. In this project a task is a move task: *a shuttle must reach a lane*. That is M3's shape and it has not changed. Here is the real `FleetTask` from the repo, in two small pieces:

```csharp
// src/FleetCore/FleetTask.cs (real, lines 10-24)
public FleetTask(string taskId, string toLane, DateTime createdAt)
{
    if (string.IsNullOrWhiteSpace(taskId))
        throw new ArgumentException("TaskId cannot be empty.", nameof(taskId));
    if (string.IsNullOrWhiteSpace(toLane))
        throw new ArgumentException("ToLane cannot be empty.", nameof(toLane));

    TaskId = taskId;
    ToLane = toLane;
    CreatedAt = ToMicrosecondPrecision(createdAt);
}
public string TaskId { get; }
public string ToLane { get; }
public DateTime CreatedAt { get; }
public TaskState State { get; private set; } = TaskState.Queued;
```

- `TaskId` is the task's name. It must not be blank. The caller supplies it — `FleetTask` never makes one.
- `ToLane` is the work: the lane to reach. It must not be blank either.
- `CreatedAt` is when the work entered the queue. The caller supplies it — `FleetTask` never reads the clock. That is what keeps it testable.
- `State` starts `Queued`. The setter is private; only the event methods move it. This lesson never moves it.

The queue is real too. This is `TaskBoard.Add`:

```csharp
// src/FleetCore/TaskBoard.cs (real, lines 6-12)
public FleetTask Add(FleetTask task)
{
    if (_tasks.Any(existing => existing.TaskId == task.TaskId))
        throw new TaskRejectedException($"duplicate task id {task.TaskId}");
    _tasks.Add(task);
    return task;
}
```

- The board refuses a second task with the same id. It throws instead of silently adding two.
- Remember this line. In concept 2 it becomes the backstop that stops a retry from doing the move twice.

Now the order. Lesson 20 stored it as a `WmsOrder`:

```csharp
// src/FleetCore/WmsOrder.cs (real)
public sealed record WmsOrder(
    string OrderId,
    string Kind,
    string PalletId,
    string LaneId,
    string State,
    DateTime ReceivedAt);
```

The mapping has to answer one question: **which order field goes into which task field?**

| `FleetTask` field | Comes from | Why |
|---|---|---|
| `TaskId` | `order.OrderId` | One string names the whole story. The WMS status-push slice keys on `orderId`. |
| `ToLane` | `order.LaneId` | The lane the order acts on. A store goes there; a retrieve sits there. |
| `CreatedAt` | `order.ReceivedAt` | The task's place in the queue matches the order's acceptance time. |
| `State` | nothing — starts `Queued` | `FleetTask` sets it. Nothing in this slice moves it. |
| `AssignedShuttleId`, `FailureReason` | nothing yet | A shuttle is chosen later; faults come later. |

So the mapping is one line:

```csharp
// shape to build — the mapping itself
new FleetTask(order.OrderId, order.LaneId, order.ReceivedAt)
```

Two things to know about this line.

**The `kind` is not used, on purpose.** A `store` means the pallet goes *into* the lane. A `retrieve` means the pallet comes *out* of it. Today both are "a shuttle must reach this lane." The deposit and the pick-up are not on the wire yet: the v4 contract's only commands are `moveCommand { ShuttleId, ToLane }` and `recoveryCommand`, and neither carries load or unload. The shuttle's state machine has `loading` and `unloading` states, but nothing on the wire tells it which one this order wants. Guessing that shape now would be a guess and a contract version bump. So the order keeps its `kind`, and the task does not carry it. When the wire grows that command, `FleetTask` gains a field — not a new lifecycle.

**`CreatedAt` is truncated to microseconds.** `FleetTask`'s constructor runs `ToMicrosecondPrecision` on the time you pass. `WmsOrder.ReceivedAt` is stored raw. So the two values are the same instant to the microsecond, but not always the same tick count. That is record 13 §5's design — matching PostgreSQL's microsecond precision, as lesson 09 explains — not a bug. A test that compares them exactly should compare `ToMicrosecondPrecision(order.ReceivedAt)`.

The order board is where this line goes. Here is the real method from lesson 20, which you will extend:

```csharp
// src/FleetCore/WmsOrderBoard.cs (real, lesson 20)
public SubmitResult Submit(SubmitOrderRequest request, DateTime now)
{
    lock (_gate)
    {
        if (_orders.TryGetValue(request.OrderId!, out var existing))
        {
            return SameFields(existing, request)
                ? new SubmitResult(SubmitOutcome.AlreadyKnown, existing)
                : new SubmitResult(SubmitOutcome.Conflict, existing);
        }

        var order = new WmsOrder(
            request.OrderId!, request.Kind!, request.PalletId!, request.LaneId!,
            WmsOrderStates.Received, now);

        _orders.Add(order.OrderId, order);
        return new SubmitResult(SubmitOutcome.Created, order);
    }
}
```

- The `lock (_gate)` makes the check-then-add one atomic step. The `TryGetValue` and the `Add` must be in the same critical section, or two threads could both create the order.
- `SameFields` compares the four input values. `State` and `ReceivedAt` are not inputs, so a retry with the same body is `AlreadyKnown`.
- The `!` on `request.OrderId!` is safe only because the endpoint validates first. That is exactly review 20's question 2.

**Where this is used later.** The execution slice hands this `FleetTask` to `TaskDispatcher`, which assigns it to a shuttle and sends a `moveCommand`. The status-push slice reads `order.OrderId` to know which order to update.

### 2) One accepted order, exactly one task

Now the rule that makes this slice safe. Look at the four ways `Submit` can end, and what each one must do to the task board:

| `Submit` result | Task added? |
|---|---|
| `Created` (first time) | **exactly one** |
| `AlreadyKnown` (identical retry) | **none** — the first submit already made it |
| `Conflict` (same id, changed values) | **none** |
| validation errors (the endpoint returns `400`) | **none** — `Submit` is never called |

Think of a paper order at a warehouse desk. The clerk writes it once and files it. If the phone line drops and the customer repeats the same order, the clerk says "already have it" and does **not** write a second work ticket. If the customer reuses the order number for different work, the clerk refuses and does **not** write a ticket. Only the first, correct order makes one ticket.

Here is the shape to build. It is the lesson-20 method with one new step inside the lock:

```csharp
// src/FleetCore/WmsOrderBoard.cs (shape to build — the Created branch only)
var order = new WmsOrder(
    request.OrderId!, request.Kind!, request.PalletId!, request.LaneId!,
    WmsOrderStates.Received, now);
_orders.Add(order.OrderId, order);

var task = new FleetTask(order.OrderId, order.LaneId, order.ReceivedAt);
_tasks.Add(task);

return new SubmitResult(SubmitOutcome.Created, order, task);
```

- **The task is added inside the lock.** `TaskBoard` holds a `List<T>`, and `List<T>.Add` is not thread-safe. Two `POST`s for different orders can arrive on two HTTP threads at the same instant. The lock keeps both `.Add` calls from touching the list at once.
- **`SubmitResult` gains a third value: the created task.** On `Created` it is the task just made. On `AlreadyKnown` and `Conflict` it is `null`, because this call made no task. Do not call it `Task` — that name clashes with `System.Threading.Tasks.Task`. Call it `CreatedTask`.
- **The mapping runs only on `Created`.** The `AlreadyKnown` and `Conflict` branches return before the mapping line. That is the whole rule.

**The second line of defence.** Suppose someone later "improves" the code so the mapping also runs on `AlreadyKnown`. What happens? `TaskBoard.Add` sees the first task with the same id and throws `TaskRejectedException`. The retry fails loudly instead of silently moving the pallet twice. Two protections, two jobs:

- The intake table is the **rule**: a retry is not new work.
- `TaskBoard.Add`'s duplicate check is the **backstop**: a coding mistake cannot turn one order into two moves.

**This is the M6 sentence.** *"a bad order does not corrupt fleet state."* At the intake, that sentence is this table. A blank field is refused before the board ever runs (that is lesson 20's validation, unchanged). A retry adds nothing. A conflict adds nothing. There is no path that makes a task without a stored, accepted order.

**Where this is used later.** The execution slice can trust the invariant: every `Queued` task on the board has exactly one order that points back at it by id. The status-push slice can trust it too: when a task changes, there is exactly one order to update.

### 3) The pair lives and dies together

An order and its task are two views of one fact. So they must share a home. That home is a `TaskBoard` created once in the serve command:

```csharp
// src/FleetManager/Program.cs, RunServeAsync (shape to build)
var taskBoard = new TaskBoard();
var board = new WmsOrderBoard(taskBoard);
```

- One `TaskBoard` is made for the whole process.
- It is handed to the order board. The order board writes to it on `Created`.
- The same instance is what the execution slice will give to `TaskDispatcher`. The board is the fleet's to-do list, and there is only one.

The rest of the endpoint does not change. This is the real POST handler from lesson 20:

```csharp
// src/FleetManager/Program.cs (real, lesson 20, lines 182-195)
webApp.MapPost(WmsRoutes.Orders, (SubmitOrderRequest request) =>
{
    var errors = WmsOrderValidator.Validate(request);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var result = board.Submit(request, DateTime.UtcNow);
    return result.Outcome switch
    {
        SubmitOutcome.Created => Results.Created(WmsRoutes.Order(result.Order.OrderId), result.Order),
        SubmitOutcome.AlreadyKnown => Results.Ok(result.Order),
        _ => Results.Problem(statusCode: 409, title: "Order id already used"),
    };
});
```

- The handler validates, calls the board, and maps the outcome. It does not know a task was made. That is the point: the door stays thin, and the mapping stays in pure `FleetCore`.
- Notice the handler never locks and never maps. Review 20's question 2 is answered by this shape: the lock and the rules live behind `Submit`.

**Now the restart.** Both boards live in the `serve` process's memory. If `serve` restarts, both are empty. The order and its task are both gone. That is **consistent**, not corrupt: there is never an order with no task, or a task with no order, because they are only ever created inside one locked step.

Writing the pair to SQL is a real job: a connection string, a schema, loading on startup, saving after each change, and Integration tests. That job belongs to the execution slice — the first slice with a task worth resuming. Until then, dropping the pair together is the honest amount of storage.

**A question this slice leaves open, on purpose.** When the execution slice reads `taskBoard.NextQueued()` from the serve-loop thread while HTTP writes from request threads, the board's thread-safety becomes a real question. This slice serializes the *writes* with the board's lock. The *read* side is the execution slice's decision. Record 29 names the question so it is not found later as a bug.

**The two ladders, lined up.** One last piece. The order has a state ladder and the task has a different one. The status-push slice needs to know which word to send, so the map is fixed now:

| `WmsOrder.State` | `FleetTask.State` | Plain meaning |
|---|---|---|
| `received` | `Queued` | The fleet has the work; no shuttle has started. |
| `received` | `Assigned` | A shuttle is chosen; the move has not started. |
| `in_progress` | `InProgress` | A shuttle has begun. |
| `done` | `Done` | Finished. |
| `failed` | `Failed` | Could not finish. |
| `cancelled` | (not mapped yet) | The WMS asked to stop; the cancel slice owns the route. |

Only the first row is reachable in this lesson. The order stays `received`; the task is `Queued`. Nothing reads a task's later state yet, because nothing in `serve` runs a task.

**Why the order does not move to `in_progress` at intake.** Because `in_progress` means "a shuttle has begun." If intake set it, the WMS could not tell "accepted, waiting for a shuttle" from "being worked on." Those are different things to an operator, and the status-push slice wants to send the transition when it really happens.

**Where this is used later.** The execution slice drives both ladders together: when it starts a task, it moves the order to `in_progress`; when the task completes, the order goes `done`. The status-push slice sends those words to the WMS.

---

## Recap in plain words

- A `FleetTask` is a move task: an id, a lane, a time, and a state that starts `Queued`.
- An accepted order becomes one task: `new FleetTask(order.OrderId, order.LaneId, order.ReceivedAt)`.
- The mapping lives in `WmsOrderBoard.Submit`, inside the existing lock, and runs only on `Created`.
- `Created` makes exactly one task. A retry, a conflict, and a bad order make none. That is M6's "a bad order does not corrupt fleet state."
- `TaskBoard.Add`'s duplicate check is the backstop if the mapping ever runs twice.
- One `TaskBoard` is created in `serve` and shared. The order and its task live and die together in memory; a restart drops both, never half.
- The order ladder and the task ladder are lined up in a fixed table for the slices to come.

---

## Worked example (toy — NOT the assignment)

This toy shows one mechanism only: **an accepted order becomes exactly one queued task, and a retry, a conflict, or a bad order adds none.** It has no MQTT, no HTTP, no real project types, and no fleet manager. The assignment is the real thing.

Create it and run it:

```bash
cd /tmp/opencode
rm -rf l21toy
dotnet new console -o l21toy
# replace l21toy/Program.cs with the listing below
dotnet run --project l21toy
```

`/tmp/opencode/l21toy/Program.cs` (exactly as I ran it):

```csharp
// TOY - not the assignment. No MQTT, no HTTP, no real project types.
// It shows one idea: an accepted order becomes exactly one queued task,
// and a duplicate, a conflict, or a bad order adds no task.

var intake = new IntakeBoard();

// A store order arrives and is accepted.
var s1 = intake.Submit(new Order("ORD-1", "store", "PLT-001", "A01-L01"));
Console.WriteLine($"ORD-1 first:   {s1}  tasks={intake.TaskCount}");

// The WMS times out and re-sends the same order. That is a retry, not new work.
var s2 = intake.Submit(new Order("ORD-1", "store", "PLT-001", "A01-L01"));
Console.WriteLine($"ORD-1 retry:   {s2}  tasks={intake.TaskCount}");

// Same id, a different lane. The WMS made a mistake.
var s3 = intake.Submit(new Order("ORD-1", "store", "PLT-001", "A02-L02"));
Console.WriteLine($"ORD-1 changed: {s3}  tasks={intake.TaskCount}");

// A retrieve order is new work; it also becomes one queued task.
var s4 = intake.Submit(new Order("ORD-2", "retrieve", "PLT-002", "A02-L02"));
Console.WriteLine($"ORD-2 first:   {s4}  tasks={intake.TaskCount}");

// A bad order (blank lane) never reaches the board.
var bad = intake.Submit(new Order("ORD-3", "store", "PLT-003", ""));
Console.WriteLine($"ORD-3 bad:     {bad}  tasks={intake.TaskCount}");

Console.WriteLine("task board:");
foreach (var task in intake.Tasks)
    Console.WriteLine($"  {task.TaskId} -> {task.ToLane} ({task.State})");

record Order(string OrderId, string Kind, string PalletId, string LaneId);
record Task(string TaskId, string ToLane, string State);

sealed class IntakeBoard
{
    private readonly Dictionary<string, Order> _orders = new(StringComparer.Ordinal);
    private readonly List<Task> _tasks = new();
    private readonly object _gate = new();

    public int TaskCount { get { lock (_gate) return _tasks.Count; } }
    public IReadOnlyList<Task> Tasks { get { lock (_gate) return _tasks.ToArray(); } }

    public string Submit(Order order)
    {
        // A bad order never reaches the board, so it cannot change fleet state.
        if (string.IsNullOrWhiteSpace(order.LaneId))
            return "Rejected (blank laneId)";

        lock (_gate)
        {
            if (_orders.TryGetValue(order.OrderId, out var existing))
            {
                if (existing == order) return "AlreadyKnown";
                return "Conflict";
            }

            _orders.Add(order.OrderId, order);
            _tasks.Add(new Task(order.OrderId, order.LaneId, "queued"));   // one accepted order -> one task
            return "Created";
        }
    }
}
```

Output, from the run I did:

```text
ORD-1 first:   Created  tasks=1
ORD-1 retry:   AlreadyKnown  tasks=1
ORD-1 changed: Conflict  tasks=1
ORD-2 first:   Created  tasks=2
ORD-3 bad:     Rejected (blank laneId)  tasks=2
task board:
  ORD-1 -> A01-L01 (queued)
  ORD-2 -> A02-L02 (queued)
```

Read the `tasks=` numbers. `1` after the first order. Still `1` after the retry. Still `1` after the conflict. `2` after the second order. Still `2` after the bad order. That is the whole lesson in one column.

Now the mutation. Change the toy so the mapping also runs on the retry path. That is one line inside the `AlreadyKnown` branch:

```csharp
if (existing == order) { _tasks.Add(new Task(order.OrderId, order.LaneId, "queued")); return "AlreadyKnown"; }
```

Output, from that run:

```text
ORD-1 first:   Created  tasks=1
ORD-1 retry:   AlreadyKnown  tasks=2
ORD-1 changed: Conflict  tasks=2
ORD-2 first:   Created  tasks=3
ORD-3 bad:     Rejected (blank laneId)  tasks=3
task board:
  ORD-1 -> A01-L01 (queued)
  ORD-1 -> A01-L01 (queued)
  ORD-2 -> A02-L02 (queued)
```

The retry now adds a second `ORD-1`. The toy allows it because its `_tasks` is a plain list. In the real code, `TaskBoard.Add` would throw on the duplicate id instead — which is why the real board is a second line of defence.

---

## Say it out loud

Answer these out loud, the way you would in an interview. No notes.

1. A `POST /api/orders` times out and the WMS sends the identical order again. Trace what happens to the order board and the task board. Then trace the same id with a changed lane. Why must only one of those two make a task?
2. `WmsOrderBoard.Submit` adds the task inside its existing `lock`. What breaks if the `TaskBoard.Add` call moves outside the lock, and what breaks if the mapping runs in the `Program.cs` handler instead? (This is review 20's question 2 with a second thing in the lock.)
3. The task id is the order id. Give two reasons that is safe here, and name the change that would make it unsafe.
4. The order stays `received` when the task is queued, instead of moving to `in_progress`. What would an operator lose if intake set `in_progress` immediately, and what will the status-push slice do with the transition later?
5. `FleetTask.CreatedAt` is truncated to microseconds but `WmsOrder.ReceivedAt` is not. Where does that happen, and why is it not a bug?

---

## The assignment

Turn an accepted WMS order into a real `FleetTask` on the fleet's board, per [record 29](../decisions/29-wms-order-to-task-mapping.md). Language: C#. The contract does **not** change: `ContractVersion.Current` stays `4`, and `docs/contract/wms-rest.md` is untouched.

Do the review-20 answers from the top of this lesson first. Put them in your review notes.

### Requirements

1. **`src/FleetCore/WmsOrder.cs`** — extend `SubmitResult` with the task this call created:
   - `SubmitResult(SubmitOutcome Outcome, WmsOrder Order, FleetTask? CreatedTask)`.
   - `CreatedTask` is non-null only on a `Created` outcome. Do **not** name the member `Task`: that clashes with `System.Threading.Tasks.Task`.

2. **`src/FleetCore/WmsOrderBoard.cs`** — the mapping, inside the existing lock, on `Created` only:
   - Constructor takes a `TaskBoard`: `public WmsOrderBoard(TaskBoard tasks)`.
   - On `Created`: after `_orders.Add(order.OrderId, order)`, create `new FleetTask(order.OrderId, order.LaneId, order.ReceivedAt)`, add it to the task board, and return `Created` with it.
   - On `AlreadyKnown` and `Conflict`: return `CreatedTask: null`. Do not map.
   - Keep the duplicate rule exactly as lesson 20 wrote it (compare the four input values; keep the original `ReceivedAt`).
   - Put the mapping in one small private method (for example `CreateTask(WmsOrder order)`) so the rule has one home.

3. **`src/FleetManager/Program.cs`** — create the board and hand it over, in `RunServeAsync`:
   - `var taskBoard = new TaskBoard();`
   - `var board = new WmsOrderBoard(taskBoard);`
   - The `MapPost`/`MapGet` handlers do **not** change. They still call `Submit` and map the outcome.
   - Do not add a dispatcher, a repository, a connection string, or a task route.

4. **Tests** (pure, no `Integration` trait), in `src/FleetCore.Tests/WmsOrderBoardTests.cs`:
   - Update the four existing tests to construct `new WmsOrderBoard(new TaskBoard())`; their assertions stay the same.
   - `First_submit_creates_exactly_one_queued_task`: after a first submit, `result.CreatedTask` is not null, its `TaskId` is `"ORD-1"`, its `ToLane` is the order's lane, and its `State == TaskState.Queued`; the task board has exactly one task.
   - `Identical_resubmit_adds_no_second_task`: after a first submit and an identical re-submit, `CreatedTask` is null and the task board still has exactly one task.
   - `Conflict_adds_no_task`: after a first submit and a changed-lane submit, `CreatedTask` is null and the task board still has exactly one task.
   - `A_second_order_adds_a_second_task`: two different orders land as two tasks.
   - Pin the wire/state name with a literal, not constant-vs-itself: `Assert.Equal("queued", TaskStateWire.ToWireName(result.CreatedTask!.State));` and `Assert.Equal("ORD-1", result.CreatedTask!.TaskId);`.

5. **Do not touch** `src/FleetCore/FleetTask.cs`, `src/FleetCore/TaskBoard.cs`, `src/FleetManager/TaskDispatcher.cs`, `src/FleetManager/RunLoop.cs`, `src/FleetManager/FleetServeLoop.cs`, `src/Contracts/`, or `docs/contract/`.

### Acceptance criteria

```bash
# 1. clean build, no warnings
cd src && dotnet build

# 2. the pure suite is green. Baseline at HEAD is 159; with your new tests you land above it.
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"   # Failed: 0

# 3. the mapping exists and lives in the right place
grep -q "TaskBoard" src/FleetCore/WmsOrderBoard.cs
grep -q "FleetTask" src/FleetCore/WmsOrderBoard.cs
grep -q "CreatedTask" src/FleetCore/WmsOrder.cs
grep -q "CreatedTask" src/FleetCore/WmsOrderBoard.cs
grep -q "new TaskBoard()" src/FleetManager/Program.cs
grep -q "new WmsOrderBoard(taskBoard)" src/FleetManager/Program.cs

# 4. the mapping runs only on Created, and the endpoint stays thin
! grep -q "MapToTask\|CreateTask" src/FleetManager/Program.cs
grep -q "lock (_gate)" src/FleetCore/WmsOrderBoard.cs
grep -q "MapPost" src/FleetManager/Program.cs

# 5. the contract and the MQTT wire did not move
grep -q "Current = 4" src/Contracts/MessageTypes.cs
git diff --stat docs/contract   # must print nothing

# 6. the reviewed pure types did not move
git diff --stat src/FleetCore/FleetTask.cs src/FleetCore/TaskBoard.cs \
  src/FleetManager/TaskDispatcher.cs src/FleetManager/RunLoop.cs \
  src/FleetManager/FleetServeLoop.cs   # must print nothing

# 7. tests pin the id and the state with literals
grep -rq '"queued"' src/FleetCore.Tests/WmsOrderBoardTests.cs
grep -rq '"ORD-1"' src/FleetCore.Tests/WmsOrderBoardTests.cs
```

### Mutation check

Run each in a scratch copy, never in the workspace. After each mutation, run the
`FleetCore.Tests` suite and note which test went red.

| # | Mutation | Expected | Verdict |
|---|---|---|---|
| a | map on `AlreadyKnown` too (add a task before returning `AlreadyKnown`) | the duplicate test goes red — `TaskBoard.Add` throws on the duplicate id, or the count is 2 | **must go red** |
| b | map on `Conflict` too | the conflict test goes red | **must go red** |
| c | delete the mapping from the `Created` branch | the created test goes red (`CreatedTask` null, count 0) | **must go red** |
| d | map with `new FleetTask(order.OrderId, order.OrderId, ...)` (lane from the id) | the lane assertion goes red | **must go red** |
| e | add `task.Assign("SH-01", now)` after creating the task | the "state is `Queued`" assertion goes red | **must go red** |
| f | remove `lock (_gate)` from `Submit` and hammer it from many threads | the committed suite may stay green; a scratch probe that submits many orders and asserts the task count equals the order count may catch a lost or duplicated add | **may survive** — this is a survey, not a challenge |

If a mutation that must go red survives, the test is decoration; fix the test. For (f), write down what the race would do to the WMS (a lost or doubled task) and why no small deterministic test pins it; then leave the HTTP-level version of that probe to the door-hardening slice (record 29 §11).

### Stretch goal

Run the door for real with the broker up, submit two orders, and confirm both tasks exist by the end of the process. There is no task route yet, so prove it in code: add a temporary log line in `RunServeAsync` after `Submit` when `CreatedTask` is not null, or assert it in a test that drives `board.Submit` and then inspects the same `TaskBoard` instance. Keep the output in your review notes. Do not add a permanent task route; that is M7's job.

```bash
docker compose -f docker/compose.yml up -d
# terminal 1
dotnet run --project src/FleetManager -- serve
# terminal 2
curl -i -X POST http://127.0.0.1:5080/api/orders -H "Content-Type: application/json" \
  -d '{"orderId":"ORD-1001","kind":"store","palletId":"PLT-001","laneId":"A01-L01"}'
curl -i -X POST http://127.0.0.1:5080/api/orders -H "Content-Type: application/json" \
  -d '{"orderId":"ORD-1002","kind":"retrieve","palletId":"PLT-002","laneId":"A02-L02"}'
docker compose -f docker/compose.yml down
```

Expect `201` then `201`, and two accepted orders with two queued tasks in the serve process.

**Not in this assignment:** running a task (the dispatcher, MQTT from `serve`, task state changes), persisting orders or tasks, pushing status to the WMS, cancelling orders, load/unload commands, an outbound lane, the dashboard, and any edit to `src/Contracts` or `docs/contract/`.

---

## Carried forward — do not lose these (record 29 §11)

The door-hardening items review 20 deferred are still open, and they belong in a **door-hardening slice that should land before M6 is called done**:

- **(d)** the duplicate-submit lock race — this slice's lock now also guards `TaskBoard.Add`, so the race has more to prove; a pure stress probe can be written here.
- **(e)** `AlreadyKnown` mapped to `201` instead of `200`.
- **(f)** the blank `Kestrel:Endpoints:Http:Url` startup check.
- **(g)** `MapGet` registered on the wrong route.

All four live in `src/FleetManager/Program.cs` and are invisible to the pure suite. The package that closes them is `Microsoft.AspNetCore.Mvc.Testing` `10.0.13` (measured; see record 28 §9). Review 20's issue 5 (a per-order application log) also belongs there. Name them in your review notes so the next lesson carries them.
