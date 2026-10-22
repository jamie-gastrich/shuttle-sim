# Hints for assignment 24: the execution slice

Open these only when stuck. One tier at a time. Tier 3 is near-solution — try tier 1 first.

## Tier 1 — nudge

- Start with the smallest piece: `src/FleetCore/OrderSync.cs`. It is a class with two fields, a constructor and a three-line method. It is the piece everything else hangs on.
- The line is called from **five** places, not one. `AssignNextAsync`, `StartAsync`, `CompleteAsync`, `FailAsync`, `RequeueAsync`. The acceptance grep counts them: `grep -c "_orderSync.Note" src/FleetManager/TaskDispatcher.cs` must print `5`. Name the field `_orderSync`.
- The line goes **after** the move and **before** the save. If you cannot say why, re-read the lecture's §4.1 — the WMS must hear about a move even when the database is down.
- The dispatcher takes the line as a **required** argument. You cannot give it a default of `null`, or you would have built the trap this lesson exists to close. The compiler will point at the 13 places that build a dispatcher. That is the feature.
- `TryGet` is not yours to change today — except for one attribute. Add `[NotNullWhen(true)]` to the out parameter and `using System.Diagnostics.CodeAnalysis;`. Do not touch anything else in that file. The acceptance check `git diff --stat "$BASE" -- src/FleetCore/WmsOrderBoard.cs` should show one line changed.
- The picture is a dictionary and a lock. Copy the shape from `FleetServeLoop`: `_knownLock` plus `_knownShuttles`, and `lock (_knownLock)` around the writes.
- A shuttle you have **never heard from** is not idle. If you make `IsIdle` return true for an unknown shuttle, the tick will give work to a shuttle that may not exist, and mutation (h) will survive.
- The tick's order matters: **finish first, then give**. If you give first, a shuttle that just arrived waits a whole beat before it is given anything.
- The tick needs no broker in its tests. Feed it a status by calling `HandleStatusAsync` directly (make it public), and give it a fake `ICommandSender`. The `FakeBus` in `ReliableCommandSenderTests.cs` keeps only **one** handler, so it cannot carry both the status and the ack subscriptions — write your own small double instead, or use `ICommandSender`.
- `ICommandSender` is one line of interface. `ReliableCommandSender.SendAsync(MoveCommandMessage command, CancellationToken cancellationToken)` already has the exact shape — you only add `: ICommandSender` to the class and the interface file.
- The WMS URL check must live in its own tiny class (`WmsEndpoint`), because `Program.cs` is top-level statements and **cannot be unit tested**. A check you cannot test is a check you cannot trust.
- The pusher's catch-all goes **last**, after `HttpRequestException` and after the `OperationCanceledException` filter. C# needs the specific catches first. And it must not swallow a shutdown: filter it with `when (ex is not OperationCanceledException)`.
- In the catch-all, mark the **claimed id** (`orderId`), not `order.OrderId` — `order` may not exist if the failure came from the lookup.
- `serve` keeps the pair in memory. Move `InMemoryFleetTaskRepository` out of the test project into `src/FleetManager/` and change its namespace to `FleetManager`. Keep `SaveCount` — four dispatcher tests read it.
- No literal interval, timeout, topic root, fleet manager id, shuttle id or lane in production code. `ShuttleState.Idle.ToWireName()`, not `"idle"`.
- No logger in the project yet, so `Console.WriteLine` is the house way to write a line. Put the ids in the line: the order id, the shuttle id, the task id.

## Tier 2 — direction

**Where each piece goes:**

| File | What it holds |
|---|---|
| `src/FleetCore/OrderSync.cs` (new) | The line. Two dependencies, one method. Pure. |
| `src/FleetCore/WmsOrderBoard.cs` (edit) | One attribute on `TryGet`. Nothing else. |
| `src/FleetManager/TaskDispatcher.cs` (edit) | The line as a required dependency; five calls. |
| `src/FleetManager/InMemoryFleetTaskRepository.cs` (new, moved) | The store `serve` uses. |
| `src/FleetManager/ShuttleRegistry.cs` (new) | The picture. |
| `src/FleetManager/ICommandSender.cs` (new) | The seam that lets the tick be tested with no broker. |
| `src/FleetManager/ReliableCommandSender.cs` (edit) | One word: `: ICommandSender`. Its body does not move. |
| `src/FleetManager/TaskExecutionLoop.cs` (new) | The tick. |
| `src/FleetManager/WmsEndpoint.cs` (new) | The WMS URL check, in a testable place. |
| `src/FleetManager/WmsStatusPusher.cs` (edit) | The catch-all that puts the word back. |
| `src/FleetManager/Program.cs` (edit) | The wiring, the new key, the catch-alls around the awaits. |
| `src/FleetManager/appsettings.json` (edit) | `TaskExecutionIntervalMs`. |

**The line, in order:**

1. `_board.SyncFromTask(taskId)` → the outcome.
2. `_outbox.NoteSync(taskId, outcome)` → the pile marks only when the outcome is `Advanced`.
3. Return the outcome.

**The picture's four members:**

- `Record(ShuttleStatus status)` — store the state, the position and the time, under the lock. A newer arrival overwrites an older one.
- `IsIdle(shuttleId)` — known **and** the state is `ShuttleState.Idle.ToWireName()`.
- `PositionOf(shuttleId)` — the last position, or null.
- `FreeShuttles()` — the idle ones, in id order (ordinal).

**The tick's three steps, in order:**

1. **Finish.** For every shuttle you are working with whose task is `InProgress`: if `IsIdle` **and** `PositionOf == task.ToLane` → `CompleteAsync`, then stop working with that shuttle.
2. **Give.** For each free shuttle in id order that you are not already working with: `AssignNextAsync`. No task → stop. Otherwise remember the task, build the move command, send it.
3. **Start or give back.** Acknowledged → `StartAsync`. Rejected, not answered, or the send threw → `RequeueAsync`, and stop working with that shuttle.

**The tick's four rules again, as a checklist you can tick off:**

- [ ] Finish before give.
- [ ] Position check as well as the idle check.
- [ ] A rejected command requeues; it does not fail the task.
- [ ] An unexpected exception in a tick is logged; the loop continues.

**The wiring in `Program.RunServeAsync`, in this order:**

0. **`Program.cs` is tab-indented.** Match the file. (`appsettings.json` is two spaces.)

1. Read and validate `FleetManager:TaskExecutionIntervalMs` (throw if `< 1`), next to the other `FleetManager` reads.
2. Read and validate `AckTimeoutMs`, `MaxAttempts` and `BackoffMs` (they are already read in `RunMoveAsync`; `serve` needs them too, to build the retry policy).
3. Build, in this order: the task board → the order board → the outbox → the `OrderSync` → the repository → the dispatcher → the registry → the retry policy → the command sender → the execution loop.
4. Subscribe **before** the tick starts: `topics.AllStatuses` → the loop's status handler, and `topics.AllAcks` → `sender.OnAckReceivedAsync`. `RunMoveAsync` already does exactly this — copy it.
5. Start the tick beside `loopTask` and `pushTask`, and await it beside them, with the same two catches.

**The pusher's catch-all, in place:**

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _outbox.MarkPending(orderId);
    Console.WriteLine($"status push: failed {orderId} {ex.GetType().Name}, will retry");
}
```

**Three small traps in the test wiring:**

- The status payload. Copy the helper from `FleetServeLoopTests.cs:38-42` (`StatusJson`) — an envelope with `ContractVersion.Current` and `MessageTypes.ShuttleStatus`, and a `ShuttleStatus(shuttleId, "idle", "A01-L01", sentAt, null)`.
- Every test needs its **own** `TaskBoard`. `TaskBoard.Add` throws `TaskRejectedException("duplicate task id …")` on a repeated id, so two tests that share a board and the same order id will fight.
- The clock. `FleetTask` refuses a time that goes backwards (`clock went backwards`). Use one fixed `DateTime` for the whole test, or a clock that only moves forward.

## Tier 3 — near-solution skeleton

Everything below is a **skeleton with the concept lines marked**. The lines marked `// how to finish` are yours to write. Do not paste this into `src/` and call it done — the point is that you can defend every line.

### 3.0 The files, all of them

```
src/FleetCore/OrderSync.cs                        NEW
src/FleetCore/WmsOrderBoard.cs                    EDIT (one attribute)
src/FleetManager/TaskDispatcher.cs                EDIT (the line, five calls)
src/FleetManager/InMemoryFleetTaskRepository.cs   NEW (moved from the test project)
src/FleetManager/ShuttleRegistry.cs               NEW
src/FleetManager/ICommandSender.cs                NEW
src/FleetManager/ReliableCommandSender.cs         EDIT (one word: `: ICommandSender`)
src/FleetManager/TaskExecutionLoop.cs             NEW
src/FleetManager/WmsEndpoint.cs                   NEW
src/FleetManager/WmsStatusPusher.cs               EDIT (the catch-all)
src/FleetManager/Program.cs                       EDIT (the wiring)
src/FleetManager/appsettings.json                 EDIT (one key)
src/FleetManager.Tests/InMemoryFleetTaskRepository.cs   DELETE (moved)
src/FleetManager.Tests/TaskDispatcherTests.cs     EDIT (the new argument)
src/FleetManager.Tests/EndToEndTests.cs           EDIT (the new argument)
src/FleetManager.Tests/RunLoopStressTests.cs      EDIT (the new argument)
src/FleetManager.Tests/RunLoopReassignmentTests.cs EDIT (the new argument)
src/FleetCore.Tests/OrderSyncTests.cs             NEW (facts 1-3)
src/FleetManager.Tests/ShuttleRegistryTests.cs    NEW (fact 6)
src/FleetManager.Tests/TaskExecutionLoopTests.cs  NEW (facts 7-8)
src/FleetManager.Tests/ExecutionSliceTests.cs     NEW (facts 4-5)
src/FleetManager.Tests/WmsStatusPusherTests.cs    EDIT (fact 9)
src/FleetManager.Tests/WmsEndpointTests.cs        NEW (fact 10)
```

### 3.1 The line — `src/FleetCore/OrderSync.cs`

```csharp
namespace FleetCore;

public sealed class OrderSync
{
    private readonly WmsOrderBoard _board;
    private readonly WmsStatusOutbox _outbox;

    public OrderSync(WmsOrderBoard board, WmsStatusOutbox outbox)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    public OrderSyncOutcome Note(string taskId)
    {
        var outcome = _board.SyncFromTask(taskId);   // how to finish: the order learns the new word
        _outbox.NoteSync(taskId, outcome);           // how to finish: the pile hears what is owed
        return outcome;                              // how to finish: the caller can log it
    }
}
```

The three marked lines are the whole idea. `OrderSyncOutcome` already exists (`src/FleetCore/WmsOrder.cs:14`).

### 3.2 The dispatcher — `src/FleetManager/TaskDispatcher.cs`

```csharp
public sealed class TaskDispatcher (TaskBoard board, IFleetTaskRepository repository, OrderSync orderSync)
{
    private readonly TaskBoard _board = board;
    private readonly IFleetTaskRepository _repository = repository;
    private readonly OrderSync _orderSync = orderSync;   // how to finish: required, never null

    public async Task<FleetTask?> AssignNextAsync(string shuttleId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.NextQueued();
        if (task is null)
            return null;

        task.Assign(shuttleId, now);
        _orderSync.Note(task.TaskId);                    // how to finish: the line, before the save
        await _repository.SaveAsync(task, cancellationToken);
        return task;
    }

    // StartAsync, CompleteAsync, FailAsync and RequeueAsync are the same shape:
    //   var task = _board.Get(taskId);
    //   task.<Move>(now);
    //   _orderSync.Note(taskId);                        // how to finish: the line
    //   await _repository.SaveAsync(task, cancellationToken);
}
```

`FailAsync` also takes a `failureReason` and calls `task.Fail(failureReason, now)`. The line is the same.

### 3.3 The contract — `src/FleetCore/WmsOrderBoard.cs`

```csharp
using System.Diagnostics.CodeAnalysis;

namespace FleetCore;

public sealed class WmsOrderBoard
{
    // ... everything else exactly as it is ...

    public bool TryGet(string orderId, [NotNullWhen(true)] out WmsOrder? order)
    {
        lock (_gate)
            return _orders.TryGetValue(orderId, out order);
    }
}
```

That is the **only** change in this file. After it lands, the pusher's guard may become `if (!_board.TryGet(orderId, out var order))` — in the same change, and only if `dotnet build --no-incremental` stays at zero warnings.

### 3.4 The store — `src/FleetManager/InMemoryFleetTaskRepository.cs`

Move the file out of `src/FleetManager.Tests/`, change `namespace FleetManager.Tests;` to `namespace FleetManager;`, and delete the old copy. Nothing else changes — **keep `SaveCount`**, because four dispatcher tests read it.

The test project finds the class without any `using`, because the enclosing namespace `FleetManager.Tests` sits inside `FleetManager`. The review verified this trick in lesson 23.

### 3.5 The picture — `src/FleetManager/ShuttleRegistry.cs`

```csharp
using Contracts;
using ShuttleCore;

namespace FleetManager;

public sealed class ShuttleRegistry
{
    private readonly Dictionary<string, ShuttleSnapshot> _shuttles = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Record(ShuttleStatus status)
    {
        lock (_gate)
        {
            _shuttles[status.ShuttleId] = new ShuttleSnapshot(
                status.ShuttleId, status.State, status.Position, status.Timestamp);
        }
    }

    public bool IsIdle(string shuttleId)
    {
        lock (_gate)
        {
            // how to finish: known AND the state is ShuttleState.Idle.ToWireName()
        }
    }

    public string? PositionOf(string shuttleId)
    {
        lock (_gate)
        {
            // how to finish: the last position, or null when we never heard
        }
    }

    public IReadOnlyList<string> FreeShuttles()
    {
        lock (_gate)
        {
            // how to finish: the idle shuttles, in id order
        }
    }
}

public sealed record ShuttleSnapshot(
    string ShuttleId, string State, string Position, DateTime SeenAt);
```

`ShuttleState.Idle.ToWireName()` comes from `ShuttleCore`. `FleetManager` already references `ShuttleCore` ✔. Do **not** write the literal `"idle"` in production code.

**Trap:** the lock. `Record` is called from the bus thread; `IsIdle` and `FreeShuttles` are called from the tick's thread. Every one of the four members takes `_gate`.

### 3.6 The seam — `src/FleetManager/ICommandSender.cs`

```csharp
using Contracts;

namespace FleetManager;

public interface ICommandSender
{
    Task<CommandSendResult> SendAsync(MoveCommandMessage command, CancellationToken cancellationToken);
}
```

Then in `src/FleetManager/ReliableCommandSender.cs`, change one line:

```csharp
public sealed class ReliableCommandSender : ICommandSender
```

Its existing `SendAsync` already matches the interface exactly, so there is nothing else to write. R11 allows this one edit: the **body** of that file must not move. Acceptance check 12b reads the diff and expects one declaration line.

### 3.7 The tick — `src/FleetManager/TaskExecutionLoop.cs`

```csharp
using System.Text.Json;
using Contracts;
using FleetCore;
using Messaging;

namespace FleetManager;

public sealed class TaskExecutionLoop
{
    private readonly TaskDispatcher _dispatcher;
    private readonly ShuttleRegistry _registry;
    private readonly ICommandSender _sender;
    private readonly string _fleetManagerId;
    private readonly TimeSpan _interval;
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, FleetTask> _working = new(StringComparer.Ordinal);

    public TaskExecutionLoop(TaskDispatcher dispatcher, ShuttleRegistry registry, ICommandSender sender,
        string fleetManagerId, TimeSpan interval, Func<DateTime> clock)
    {
        // how to finish: four null checks, one blank check, one range check
        //   fleetManagerId blank -> ArgumentException
        //   interval <= 0     -> ArgumentOutOfRangeException
    }

    public Task HandleStatusAsync(MessageReceived message, CancellationToken cancellationToken)
    {
        // how to finish: copy the envelope checks from FleetServeLoop.HandleStatusAsync
        //   try to parse ShuttleStatusMessage; a JsonException returns Task.CompletedTask
        //   check envelope.ContractVersion == ContractVersion.Current
        //   check envelope.MessageType == MessageTypes.ShuttleStatus
        //   check envelope.MessageId is not blank and status.Status.ShuttleId is not blank
        //   then _registry.Record(status.Status)
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await FinishFinishedWorkAsync(cancellationToken);      // how to finish: finish first
        await GiveWorkToFreeShuttlesAsync(cancellationToken);   // how to finish: then give
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await TickAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;                                            // how to finish: shutdown must escape
            }
            catch (Exception ex)
            {
                Console.WriteLine($"task execution: tick failed {ex.GetType().Name} {ex.Message}");
            }
        }
    }

    private async Task FinishFinishedWorkAsync(CancellationToken cancellationToken)
    {
        // how to finish, in this order:
        //   1. copy _working so you can remove from it while you walk it
        //   2. for each (shuttleId, task):
        //        if task.State != TaskState.InProgress -> skip
        //        if !_registry.IsIdle(shuttleId) -> skip
        //        if _registry.PositionOf(shuttleId) != task.ToLane -> skip
        //        await _dispatcher.CompleteAsync(task.TaskId, _clock(), cancellationToken)
        //        _working.Remove(shuttleId)
    }

    private async Task GiveWorkToFreeShuttlesAsync(CancellationToken cancellationToken)
    {
        // how to finish, in this order:
        //   for each shuttleId in _registry.FreeShuttles():
        //        if _working.ContainsKey(shuttleId) -> skip
        //        var task = await _dispatcher.AssignNextAsync(shuttleId, _clock(), cancellationToken)
        //        if task is null -> return (no queued task left)
        //        _working[shuttleId] = task
        //        build the command:
        //          var envelope = MessageEnvelopes.Create(MessageTypes.MoveCommand, _fleetManagerId, _clock());
        //          var command = new MoveCommandMessage(envelope, shuttleId, task.ToLane);
        //        try { var result = await _sender.SendAsync(command, cancellationToken); }
        //        catch { await _dispatcher.RequeueAsync(task.TaskId, _clock(), cancellationToken);
        //               _working.Remove(shuttleId); throw; }   // or: log and carry on
        //        if result.Status == CommandSendStatus.Acknowledged
        //              await _dispatcher.StartAsync(task.TaskId, _clock(), cancellationToken);
        //        else
        //              await _dispatcher.RequeueAsync(task.TaskId, _clock(), cancellationToken);
        //              _working.Remove(shuttleId);
    }
}
```

**Two traps in the tick:**

- **`_working` is the tick's own memory.** The `TaskBoard` cannot answer "which task is this shuttle working on" — it has no such method. That is why the tick keeps its own dictionary.
- **The clock moves.** `FleetTask` refuses a time that goes backwards. Call `_clock()` fresh for every transition, never once at the top of the tick and reused.

### 3.8 The WMS URL — `src/FleetManager/WmsEndpoint.cs`

```csharp
namespace FleetManager;

public static class WmsEndpoint
{
    public static Uri Parse(string baseUrl)
    {
        // how to finish, in this order:
        //   1. if the value is null or whitespace -> throw InvalidOperationException
        //   2. if !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) -> throw InvalidOperationException
        //   3. if uri.Scheme is neither Uri.UriSchemeHttp nor Uri.UriSchemeHttps -> throw InvalidOperationException
        //   4. return uri
    }
}
```

The messages should name the setting, the way `Program.cs` already does: `"Wms:BaseUrl must be an absolute URL."`, `"Wms:BaseUrl must be an http or https URL."`.

**Why this is its own class:** `Program.cs` is top-level statements. There is no method for a test to call. A check you cannot test is a check you cannot trust.

### 3.9 The failure policy — `src/FleetManager/WmsStatusPusher.cs`

Inside `SendPendingAsync`, after the existing three catches, add the catch-all. It must be **last**:

```csharp
catch (Exception ex) when (ex is not OperationCanceledException)
{
    _outbox.MarkPending(orderId);                                    // the truth is not given up on
    Console.WriteLine($"status push: failed {orderId} {ex.GetType().Name}, will retry");
}
```

Two things to get right:

- The filter. `OperationCanceledException` means "we are shutting down" when the token is cancelled. That one must escape so the loop stops. A *timeout* is already caught by the existing `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)`.
- The id. Use `orderId` (the claimed id), not `order.OrderId`.

### 3.10 The wiring — `src/FleetManager/Program.cs` (inside `RunServeAsync` only)

```csharp
// 1. the new key, next to the other FleetManager reads
var tickIntervalMs = configuration.GetValue<int>("FleetManager:TaskExecutionIntervalMs");
if (tickIntervalMs < 1)
    throw new InvalidOperationException("FleetManager:TaskExecutionIntervalMs must be positive.");

// 2. the retry policy needs these three; serve does not read them yet
var ackTimeoutMs = configuration.GetValue<int>("FleetManager:AckTimeoutMs");
if (ackTimeoutMs < 1)
    throw new InvalidOperationException("FleetManager:AckTimeoutMs must be positive.");
var maxAttempts = configuration.GetValue<int>("FleetManager:MaxAttempts");
if (maxAttempts < 1)
    throw new InvalidOperationException("FleetManager:MaxAttempts must be positive.");
var backoffMs = configuration.GetValue<int>("FleetManager:BackoffMs");
if (backoffMs < 0)
    throw new InvalidOperationException("FleetManager:BackoffMs must not be negative.");

// 3. the objects, in this order
var taskBoard = new TaskBoard();
var board = new WmsOrderBoard(taskBoard);
var outbox = new WmsStatusOutbox();
var orderSync = new OrderSync(board, outbox);
var repository = new InMemoryFleetTaskRepository();
var dispatcher = new TaskDispatcher(taskBoard, repository, orderSync);
var registry = new ShuttleRegistry();
var retry = new RetryPolicyExtensions.RetryPolicy(
    maxAttempts, TimeSpan.FromMilliseconds(ackTimeoutMs), TimeSpan.FromMilliseconds(backoffMs));
var sender = new ReliableCommandSender(bus, topics, retry);
var execution = new TaskExecutionLoop(dispatcher, registry, sender, messaging.FleetManagerId,
    TimeSpan.FromMilliseconds(tickIntervalMs), () => DateTime.UtcNow);

// 4. the WMS URL, before the bus connects
var wmsUri = WmsEndpoint.Parse(wmsBaseUrl);

// 5. the subscriptions, before the tick starts
await bus.SubscribeAsync(topics.AllStatuses, MessageFamily.Status, execution.HandleStatusAsync, CancellationToken.None);
await bus.SubscribeAsync(topics.AllAcks, MessageFamily.Ack, sender.OnAckReceivedAsync, CancellationToken.None);

// 6. start the tick beside the other two loops
var executionTask = execution.RunAsync(stopping.Token);
```

And every await gains a second catch:

```csharp
try
{
    await executionTask;
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested)
{
    // Expected: the tick stopped because we cancelled it.
}
catch (Exception ex)
{
    Console.WriteLine($"fleet manager: the task execution loop stopped unexpectedly: {ex.Message}");
}
```

Do the same for `loopTask`, `pushTask` and `webTask`. The acceptance check wants **at least four** `catch (Exception` in `Program.cs`.

**Trap:** the WMS URL check must happen **before** `await bus.ConnectAsync`. The review in lesson 23 found the old check accepted `ftp://`, `mailto:` and `file://`.

**Trap:** `topics` and `bus` must exist before you build the sender. `RunServeAsync` already builds both.

### 3.11 The configuration — `src/FleetManager/appsettings.json`

Add one key to the existing `FleetManager` section:

```json
"TaskExecutionIntervalMs": 1000
```

Keep the trailing comma on the line above it correct, and keep the file's two-space indentation.

### 3.12 The ten tests

**The world they arrange.** Almost every test needs the same four things:

```csharp
var tasks = new TaskBoard();
var board = new WmsOrderBoard(tasks);
var outbox = new WmsStatusOutbox();
var orderSync = new OrderSync(board, outbox);
var repository = new InMemoryFleetTaskRepository();
var dispatcher = new TaskDispatcher(tasks, repository, orderSync);
```

Then an order:

```csharp
var submit = board.Submit(new SubmitOrderRequest("ORD-1", "pallet-move", "PAL-1", "A01-L07"), now);
var task = submit.CreatedTask!;
```

**Fact 1 — `OrderSync_marks_the_word_when_the_task_moved`** (`src/FleetCore.Tests/OrderSyncTests.cs`)

- Arrange: the world above.
- Act: `task.Start(now)` — the task is `Assigned`? No: a fresh task is `Queued`, and `Start` needs `Assigned`. So **assign first**: `task.Assign("SH-01", now);` then `task.Start(now);` then `var outcome = orderSync.Note(task.TaskId);`
- Assert: `outcome` is `OrderSyncOutcome.Advanced`; `outbox.PendingCount` is `1`; and the board's word is the one you expect — `board.TryGet("ORD-1", out var order)` then `order.State` equals `WmsOrderStates.InProgress`.

**Fact 2 — `OrderSync_marks_nothing_when_the_word_did_not_move`** (same file)

- Arrange: the world above.
- Act: `task.Assign("SH-01", now); var outcome = orderSync.Note(task.TaskId);`
- Assert: `outcome` is `OrderSyncOutcome.AlreadyThere`; `outbox.PendingCount` is `0`.

**Fact 3 — `OrderSync_of_a_task_without_an_order_marks_nothing`** (same file)

- Arrange: a `TaskBoard` with a task that has **no** order. Build it by hand: `var task = new FleetTask("TASK-9", "A01-L07", now); tasks.Add(task);` — `FleetTask`'s constructor is public ✔.
- Act: `var outcome = orderSync.Note("TASK-9");`
- Assert: `outcome` is `OrderSyncOutcome.NoOrder`; `outbox.PendingCount` is `0`.

**Fact 4 — `Dispatching_a_task_moves_the_order_and_owes_a_word`** (`src/FleetManager.Tests/ExecutionSliceTests.cs`)

- Arrange: the world above, with an order submitted.
- Act: `await dispatcher.AssignNextAsync("SH-01", now, CancellationToken.None);` then `await dispatcher.StartAsync("ORD-1", now, CancellationToken.None);`
- Assert: the task's state is `TaskState.InProgress`; the order's word is `WmsOrderStates.InProgress`; `outbox.PendingCount` is `1`.

**Fact 5 — `The_newest_word_wins_the_wms_hears_done_not_in_progress`** (`src/FleetManager.Tests/ExecutionSliceTests.cs`)

This is review 23's item #6. It needs the pusher, so copy the stub handler from `src/FleetManager.Tests/WmsStatusPusherTests.cs` (the `StubHandler` that captures requests).

- Arrange: the world above, plus `new WmsStatusPusher(board, outbox, new HttpClient(new StubHandler()), TimeSpan.FromSeconds(1))`.
- Act: assign, start, **then complete** — all three before any push. Then `await pusher.SendPendingAsync(CancellationToken.None);`
- Assert: the stub saw **exactly one** request; parse its body with `JsonDocument` and `GetProperty("state")` and assert it equals `"done"`; and `GetProperty("orderId")` equals `"ORD-1"`.
- **Trap:** a string-contains check (`body.Contains("done")`) does not kill mutation (d) in lesson 23. Parse the JSON.

**Fact 6 — `The_picture_keeps_the_last_state_and_position_of_each_shuttle`** (`src/FleetManager.Tests/ShuttleRegistryTests.cs`)

- Arrange: `var registry = new ShuttleRegistry();`
- Act: `registry.Record(new ShuttleStatus("SH-01", "moving", "A01-L03", now, null));` then `registry.Record(new ShuttleStatus("SH-01", "idle", "A01-L07", now, null));`
- Assert: `registry.IsIdle("SH-01")` is true; `registry.PositionOf("SH-01")` equals `"A01-L07"`; `registry.FreeShuttles()` contains `"SH-01"`.
- One more assertion worth having: `registry.IsIdle("SH-99")` is **false** for a shuttle it never heard.

**Fact 7 — `The_tick_completes_a_task_when_the_shuttle_is_idle_at_the_lane`** (`src/FleetManager.Tests/TaskExecutionLoopTests.cs`)

- Arrange: the world above; a fake `ICommandSender` that returns `new CommandSendResult(CommandSendStatus.Acknowledged, 1, null)`; the registry; and the tick.
- Act: feed a status — `await tick.HandleStatusAsync(new MessageReceived("shuttle/SH-01/status", StatusJson("SH-01", "idle", "A01-L07", now)), CancellationToken.None);` — then `await tick.TickAsync(CancellationToken.None);`
- Assert: the task's state is `TaskState.Done`.
- **Trap:** the status must say the shuttle is idle **at the task's lane** (`A01-L07`). If the status says `A01-L03`, the task must stay `InProgress`. Write that second case as part of the same test, or as a second assert.

**Fact 8 — `The_tick_gives_a_queued_task_to_a_free_shuttle`** (same file)

- Arrange: the world above, the fake sender (record the commands it is given), the registry, the tick.
- Act: feed a status for `SH-01` (`idle`, `A01-L01`), then `await tick.TickAsync(CancellationToken.None);`
- Assert: the task's state is `TaskState.InProgress` (the fake acknowledged it); the fake received one command; the command's `ToLane` equals `"A01-L07"` (the task's lane); the command's `ShuttleId` equals `"SH-01"`.
- **Trap:** a shuttle the tick has never heard about must get **no** work. Add that case.

**Fact 9 — `A_push_pass_that_throws_keeps_the_word_and_the_next_order`** (`src/FleetManager.Tests/WmsStatusPusherTests.cs`)

- Arrange: two orders, both owed a word. A stub handler that **throws** on the first request and answers `200` on the second.
- Act: `await pusher.SendPendingAsync(CancellationToken.None);`
- Assert: the first order is still owed (`outbox.PendingCount` is `1`, and it is the first order's id); the second order **did** go out (the stub saw the second request).

**Fact 10 — `The_wms_url_must_be_an_http_or_https_url`** (`src/FleetManager.Tests/WmsEndpointTests.cs`)

- Arrange: nothing.
- Act and assert: `WmsEndpoint.Parse("http://127.0.0.1:5090")` returns a `Uri`; `WmsEndpoint.Parse("https://wms.example/api")` returns a `Uri`; `Assert.Throws<InvalidOperationException>(() => WmsEndpoint.Parse("ftp://wms.example/api"));` and the same for `"not-a-url"` and `""`.

### 3.13 Run it

```bash
cd src && dotnet build --no-incremental
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
```

Then the acceptance criteria from the lesson, one numbered block at a time. If a grep fails, read the lesson's requirement again — every grep is a sentence in the requirement, not a magic incantation.

**When you are done, clean up:**

```bash
cd src && dotnet clean
```

and check that nothing is left running:

```bash
pgrep -af "dotnet run|FleetManager.dll|mosquitto|postgres"
docker compose -f docker/compose.yml ps
```
