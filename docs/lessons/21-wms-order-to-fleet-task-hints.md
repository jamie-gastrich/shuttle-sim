# Lesson 21 hints — From an accepted order to a fleet task

Open a tier only when you are stuck. Tier 1 is a nudge. Tier 2 is the direction. Tier 3 is nearly the code. Each tier stands on its own, so you can jump straight to Tier 2 or 3.

The assignment is in [21-wms-order-to-fleet-task.md](21-wms-order-to-fleet-task.md). The reasons and the exact shapes are in [record 29](../decisions/29-wms-order-to-task-mapping.md). Do not invent a task id, a mapping site, or a storage rule; they are decided.

Before any of this: answer the review-20 Explain-it questions at the top of the lesson. Question 2 is folded into concept 2, so start there.

---

## Tier 1 — Nudges

**The mapping is one line.** Read `FleetTask`'s constructor and `WmsOrder`. Every `FleetTask` field comes from one `WmsOrder` field. Write the table from concept 1 before you write code; the line writes itself.

**The task board is a new field on the order board.** `WmsOrderBoard` needs a `TaskBoard` to add to. Take it in the constructor. The endpoint creates it and passes it in.

**Only on `Created`.** The `AlreadyKnown` and `Conflict` branches `return` before the mapping. Add the mapping after `_orders.Add(...)` and nowhere else.

**Inside the lock.** The existing `lock (_gate)` already wraps the whole method body. Put the `TaskBoard.Add` inside it. `TaskBoard` is a `List<T>`; two HTTP threads can call `Submit` at once.

**The result carries the task.** Add a third member to `SubmitResult`. Name it `CreatedTask`, not `Task` (`Task` is a .NET type). It is `null` unless this call created the order.

**The tests break at compile time first.** Every `new WmsOrderBoard()` becomes `new WmsOrderBoard(new TaskBoard())`. Fix that, then add the new facts.

**The microsecond thing.** `FleetTask` truncates `CreatedAt`. If a test compares `task.CreatedAt` to `order.ReceivedAt`, compare to `ToMicrosecondPrecision(order.ReceivedAt)` instead, or use an input time that is already on a microsecond boundary.

---

## Tier 2 — Direction

**`src/FleetCore/WmsOrder.cs`.** Extend the positional record:

```csharp
public sealed record SubmitResult(SubmitOutcome Outcome, WmsOrder Order, FleetTask? CreatedTask);
```

Then fix the three `new SubmitResult(...)` sites in `WmsOrderBoard.cs` to pass a third value.

**`src/FleetCore/WmsOrderBoard.cs`.** Add a field and a constructor parameter:

```csharp
private readonly TaskBoard _tasks;

public WmsOrderBoard(TaskBoard tasks)
    => _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
```

In `Submit`, inside the lock:

- The two existing `return new SubmitResult(...)` calls for `AlreadyKnown` and `Conflict` become `new SubmitResult(outcome, existing, null)`.
- The `Created` path gains one line after `_orders.Add(...)`:

```csharp
var created = CreateTask(order);
_tasks.Add(created);
return new SubmitResult(SubmitOutcome.Created, order, created);
```

- Add the small private method so the rule has one home:

```csharp
private static FleetTask CreateTask(WmsOrder order)
    => new(order.OrderId, order.LaneId, order.ReceivedAt);
```

`CreateTask` is pure and takes no clock: the time comes from the order, which took it as an argument. It makes no id: the id is the order's.

**`src/FleetManager/Program.cs`.** In `RunServeAsync`, two lines:

```csharp
var taskBoard = new TaskBoard();
var board = new WmsOrderBoard(taskBoard);
```

Nothing else changes. The `MapPost` handler already calls `board.Submit` and maps the outcome; it does not need the task.

**Tests.** Mirror the four existing facts and add three:

- first submit → `CreatedTask` not null, `TaskId == "ORD-1"`, `ToLane` equals the request lane, `State == TaskState.Queued`, board count 1.
- identical retry → `CreatedTask` null, board count still 1.
- changed lane → `CreatedTask` null, board count still 1.
- two different orders → board count 2.

Keep the `TaskBoard` instance local to each test so the count is easy to assert:

```csharp
var tasks = new TaskBoard();
var board = new WmsOrderBoard(tasks);
```

---

## Tier 3 — Near-solution skeleton

Sketches only. They are shaped to fit the repo, but you still write the tests and prove the behaviour. Read every line before you use it.

**`SubmitResult`:**

```csharp
public sealed record SubmitResult(SubmitOutcome Outcome, WmsOrder Order, FleetTask? CreatedTask);
```

**`WmsOrderBoard` (the parts that change):**

```csharp
public sealed class WmsOrderBoard
{
    private readonly Dictionary<string, WmsOrder> _orders = new(StringComparer.Ordinal);
    private readonly TaskBoard _tasks;
    private readonly object _gate = new();

    public WmsOrderBoard(TaskBoard tasks)
        => _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

    public SubmitResult Submit(SubmitOrderRequest request, DateTime now)
    {
        lock (_gate)
        {
            if (_orders.TryGetValue(request.OrderId!, out var existing))
            {
                return SameFields(existing, request)
                    ? new SubmitResult(SubmitOutcome.AlreadyKnown, existing, null)
                    : new SubmitResult(SubmitOutcome.Conflict, existing, null);
            }

            var order = new WmsOrder(
                request.OrderId!, request.Kind!, request.PalletId!, request.LaneId!,
                WmsOrderStates.Received, now);

            _orders.Add(order.OrderId, order);

            var created = CreateTask(order);   // the mapping: order -> task, one line
            _tasks.Add(created);

            return new SubmitResult(SubmitOutcome.Created, order, created);
        }
    }

    public bool TryGet(string orderId, out WmsOrder? order)
    {
        lock (_gate)
            return _orders.TryGetValue(orderId, out order);
    }

    private static FleetTask CreateTask(WmsOrder order)
        => new(order.OrderId, order.LaneId, order.ReceivedAt);

    private static bool SameFields(WmsOrder existing, SubmitOrderRequest request)
        => existing.Kind == request.Kind
        && existing.PalletId == request.PalletId
        && existing.LaneId == request.LaneId;
}
```

Do **not** add the mapping call to the `AlreadyKnown` or `Conflict` branches. If you do, `TaskBoard.Add` throws a duplicate-id exception — which is the backstop doing its job, but it is still the wrong code.

**`RunServeAsync` (the two lines):**

```csharp
var taskBoard = new TaskBoard();
var board = new WmsOrderBoard(taskBoard);
```

**Test skeleton (one fact, so you can see the shape):**

```csharp
[Fact]
public void First_submit_creates_exactly_one_queued_task()
{
    var tasks = new TaskBoard();
    var board = new WmsOrderBoard(tasks);
    var request = new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PLT-001", "A01-L01");

    var result = board.Submit(request, Start);

    Assert.Equal(SubmitOutcome.Created, result.Outcome);
    Assert.NotNull(result.CreatedTask);
    Assert.Equal("ORD-1", result.CreatedTask!.TaskId);
    Assert.Equal("A01-L01", result.CreatedTask!.ToLane);
    Assert.Equal("queued", TaskStateWire.ToWireName(result.CreatedTask!.State));
    Assert.Equal(1, tasks.Count);
}
```

Write the retry, conflict, and second-order facts yourself. Each one asserts both the outcome and the **task count**, because the count is the thing this lesson protects.

---

## Tier 3b — If something will not go green

- **All four lesson-20 tests fail to compile.** They still say `new WmsOrderBoard()`. Add `new TaskBoard()`.
- **The retry test throws `TaskRejectedException: duplicate task id ORD-1`.** The mapping is running on the `AlreadyKnown` path. Move it back inside the `Created` branch only.
- **`Assert.NotNull(result.CreatedTask)` fails on a first submit.** The `Created` return still passes two arguments, or you added the task to a different `TaskBoard` than the one the test holds. Use the same instance.
- **A task-count assertion fails after a conflict.** The mapping is running on the `Conflict` path, or `Submit` mutated the order dictionary before returning `Conflict`. `Submit` must return without adding on a conflict.
- **A time-equality assertion fails.** `FleetTask` truncated `CreatedAt`. Compare `ToMicrosecondPrecision(order.ReceivedAt)`, or pass an input time that is already a whole number of microseconds.
- **A mutation that must go red does not.** Your test asserts the outcome but not the task board. Add the count assertion; the count is what tells a retry from new work.
