# Hints for Lesson 27: The order cancel slice

Three tiers. Open the tier that matches how stuck you are.

## Nudge (tier 1)

You are here because you forgot one detail. Choose the hint that matches:

1. **Cancel command route:** Did you add `WmsRoutes.OrderCancel` to `WmsRoutes.cs`? The constant is `"/wms/orders/cancel"` — it already exists from the lesson 20 scaffold, but you need the POST handler in `Program.cs` to use it.

2. **Task.Cancel guard:** The `EnsureInOrder(now)` check is the same pattern as `Fail` and `Complete`. If you are unsure where to put it, look at `FleetTask.Fail` at line 59‑70 of `FleetCore/FleetTask.cs` — the guard is right after `EnsureInOrder`.

3. **Ladder sync outcome:** `WmsOrderLadder.SyncFromTask` returns `WmsOrderLadder.OrderSyncOutcome.Advanced`, `AlreadyThere`, or `NoOrder`. If the outcome is `Advanced`, you update the order state with `order with { State = "cancelled" }`. If it is not `Advanced`, you do not write.

4. **Wire names:** The `TaskStateWire.ToWireName` and `FromWireName` methods must both include a branch for `TaskState.Cancelled => "cancelled"` and `"cancelled" => TaskState.Cancelled`. If you add it to only one, the round‑trip will fail.

---

## Direction (tier 2)

You need more than a nudge. These hints point you at the right file and the right line.

1. **Add `Cancelled` to the enum:** Open `src/FleetCore/TaskState.cs`. After the `Failed` member (line 9), add `Cancelled,`. Then open `src/FleetCore/TaskStateWire.ToWireName` and add `TaskState.Cancelled => "cancelled",` before the `_ =>` arm. Do the same in `FromWireName` for the `"cancelled"` string.

2. **Add `FleetTask.Cancel` method:** Open `src/FleetCore/FleetTask.cs`. After the `Fail` method (line 69‑70), add a new method:

```csharp
public void Cancel(DateTime now)
{
    EnsureInOrder(now);
    if (State == TaskState.Done || State == TaskState.Failed || State == TaskState.Cancelled)
        throw new TaskRejectedException("Cannot cancel a task that is already Done, Failed, or Cancelled.");
    State = TaskState.Cancelled;
    _lastEventAt = now;
}
```

The `EnsureInOrder` call and the `TaskRejectedException` throw are the same pattern as `Fail`.

3. **Add the cancel POST in `Program.cs`:** Open `src/FleetManager/Program.cs`. After the existing order GET endpoints (after line 281 in the lesson 26 template), add a new `MapPost`:

```csharp
webApp.MapPost(WmsRoutes.OrderCancel("{orderId}"), (string orderId) =>
{
    if (board.TryGet(orderId, out var order) is false)
        return Results.Problem(statusCode: 404, title: "Unknown order");

    var task = _tasks.Get(order.TaskId);
    if (task is null)
        return Results.Problem(statusCode: 404, title: "Task not found for order");

    task.Cancel(now);
    var sync = WmsOrderLadder.SyncFromTask(task.TaskId);
    if (sync == WmsOrderLadder.OrderSyncOutcome.Advanced)
    {
        board.TryUpdate(orderId, order with { State = "cancelled" }, out _);
    }
    return Results.Ok(new { orderId, newState: order.State });
});
```

The `board` and `_tasks` variables are already in scope from the earlier board setup; `now` is the current clock time from the serve loop.

4. **Confirm the ladder mapping exists:** Open `src/FleetCore/WmsOrderLadder.cs`. The `SyncFromTask` method already has a switch on `TaskState` that returns `OrderSyncOutcome`. If `TaskState.Cancelled` is not a case, add it: map it to `OrderSyncOutcome.Advanced` so the order state advances to `cancelled`.

---

## Near-solution (tier 3)

You are this close. These hints show almost the complete solution — use them only if you have been stuck for a while and need to see the full picture to get moving.

1. **Full `TaskState` change:** The enum in `src/FleetCore/TaskState.cs` should look like this after your edit:

```csharp
public enum TaskState 
{ 
    Queued,
    Assigned,
    InProgress,
    Done,
    Failed,
    Paused,
    Cancelled
}
```

And the wire methods:

```csharp
public static string ToWireName(this TaskState state) => state switch
{
    TaskState.Queued => "queued",
    TaskState.Assigned => "assigned",
    TaskState.InProgress => "in_progress",
    TaskState.Done => "done",
    TaskState.Failed => "failed",
    TaskState.Paused => "paused",
    TaskState.Cancelled => "cancelled",
    _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown task state"),
};

public static TaskState FromWireName(string wireName)
{
    ArgumentNullException.ThrowIfNull(wireName);
    return wireName switch
    {
        "queued" => TaskState.Queued,
        "assigned" => TaskState.Assigned,
        "in_progress" => TaskState.InProgress,
        "done" => TaskState.Done,
        "failed" => TaskState.Failed,
        "paused" => TaskState.Paused,
        "cancelled" => TaskState.Cancelled,
        _ => throw new ArgumentException($"Unknown task state wire name '{wireName}'.", nameof(wireName)),
    };
}
```

2. **Full `FleetTask.Cancel` method:** The method in `src/FleetCore/FleetTask.cs` is exactly the block shown in tier 2 hint #2. Place it after `Fail` (line 70) and before the private `EnsureInOrder` (line 72). The class should compile with zero errors after this addition.

3. **Full cancel POST handler:** The handler in `src/FleetManager/Program.cs` is the block shown in tier 2 hint #3. Place it after the last order GET (`MapGet(WmsRoutes.TaskQueue, ...)`) and before the `Recap` section. The handler uses `now` from the closure captured by `RunServeAsync`; if your serve loop passes `now` differently, adjust the variable name but keep the same logic: `task.Cancel(now)` → `SyncFromTask` → `TryUpdate` with `with` expression.

4. **Test skeleton:** A minimal integration test that starts `serve`, posts cancel, and checks the GET endpoint would look roughly like this (do not copy unless you are writing the assignment test):

```csharp
[Fact, OrderCancelTest]
async Task Cancel_endpoint_returns_200_and_updates_state()
{
    var server = new TestServer<Program>(builder =>
    {
        builder.UseEnvironment("Development");
        // config: PostgreSQL connection string, MQTT broker, etc.
    });
    using var client = server.CreateClient();

    // submit an order first (reuse lesson 21 pattern)
    var response = await client.PostAsync("/wms/orders", content);
    response.EnsureSuccessStatusCode();

    // now cancel it
    var cancel = await client.PostAsync("/wms/orders/ORD-1/cancel", null);
    cancel.EnsureSuccessStatusCode();

    // read it back
    var get = await client.GetAsync("/wms/orders/ORD-1");
    get.EnsureSuccessStatusCode();
    var body = await get.Content.ReadAsStringAsync();
    Assert.Contains("\"cancelled\"", body);
}
```

If you see the pattern and can type it in, you are ready to build and run the acceptance criteria.