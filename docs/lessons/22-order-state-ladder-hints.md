# Hints for assignment 22: the order state ladder

Open these only when stuck. One tier at a time. Tier 3 is near the solution — try tier 1 first.

## Tier 1 — nudge

- Start with `src/FleetCore/WmsOrderLadder.cs`. It is a `switch` on `TaskState` that returns a `string`. Copy the shape of `TaskStateWire.ToWireName` in `src/FleetCore/TaskState.cs` (lines 14–22) and change the right-hand side to the `WmsOrderStates` constants.
- The sync goes next to `Submit` in `WmsOrderBoard.cs`, and it starts with `lock (_gate) { ... }` before anything else. If a line inside it touches `_orders`, it is inside that brace.
- The write is one line. `WmsOrder` is a record, and `State` is init-only, so you cannot assign to it. Look up `with` — "copy this record, change one field".
- Which word is "above" which? Use the rungs from record 30 §5: `received` 0, `in_progress` 1, `done` 2, `failed` 2. A small method that turns a word into a number makes the comparison one line — and Decision E in the lesson says it lives on `WmsOrderLadder`, beside `ForTask`.
- The four situations and their outcomes are already written down in record 30 §5's table. The hard part is not deciding — it is putting them in that order.

## Tier 2 — direction

**Where each piece goes:**

- `src/FleetCore/WmsOrderLadder.cs` (new): `public static class WmsOrderLadder` with `public static string ForTask(TaskState taskState)`. Five branches, one per `TaskState`, each returning a `WmsOrderStates` constant. The `_ =>` arm throws `ArgumentOutOfRangeException`.
- `src/FleetCore/WmsOrder.cs`: add `public enum OrderSyncOutcome { Advanced, AlreadyThere, NoOrder }` right under `SubmitOutcome`.
- `src/FleetCore/OrderRejectedException.cs` (new): copy `TaskRejectedException`'s three lines and rename. Same shape: sealed, one `(string message)` constructor.
- `src/FleetCore/WmsOrderBoard.cs`: add `public OrderSyncOutcome SyncFromTask(string taskId)`.

**Order of the four checks inside the lock — this order, not another:**

1. The order lookup. Missing order means "nothing to do": return the no-order outcome, do not throw.
2. The ladder lookup for the task. Notice this comes *after* step 1, so an orderless task never reaches the task lookup.
3. The word comparison. Same word as stored: return the already-there outcome, write nothing.
4. The rank comparison. Target not above stored: throw. Otherwise replace the stored record and return the advanced outcome.

If you check rank before equality, `AlreadyThere` never fires (equal words are also "not above"). If you look the task up first, an id that is in neither board throws `KeyNotFoundException` instead of returning `NoOrder` — order lookup first makes every missing id a clean no-op.

**What each test needs:** drive `FleetTask` by hand with its own clock — `var now = DateTime.UtcNow;` then `task.Assign(shuttleId, now)` / `task.Start(now)` / `task.Complete(now)` / `task.Fail(reason, now)` as each test needs, then `board.SyncFromTask(taskId)`. The task lives on the `TaskBoard` you passed into `WmsOrderBoard`'s constructor, so `taskBoard.Get(orderId)` is how the test finds it. Intake already stores `received`, so the first sync after accept returns `AlreadyThere` with no writes.

**The failure test:** a task that fails before it starts. From `Queued`, call `Fail`, then sync. The ladder maps `Failed` to `failed`, and the rung jumps 0 → 2. That skip is legal.

**The requeue test:** `Assign` then `Requeue` moves the task backwards — but both words sit on the `received` row, so the sync sees the same word and returns `AlreadyThere`. Nothing is written.

## Tier 3 — near-solution skeleton

This is the shape, with the interesting lines left for you to write.

```csharp
// src/FleetCore/WmsOrderLadder.cs
namespace FleetCore;

public static class WmsOrderLadder
{
    public static string ForTask(TaskState taskState) => taskState switch
    {
        // TODO: one line per TaskState member, returning WmsOrderStates.*
        // queued    -> ???
        // assigned  -> ???
        // in_progress -> ???
        // done      -> ???
        // failed    -> ???
        _ => throw new ArgumentOutOfRangeException(nameof(taskState), taskState, "unknown task state"),
    };
}
```

```csharp
// src/FleetCore/WmsOrderBoard.cs — the new method, inside the class
public OrderSyncOutcome SyncFromTask(string taskId)
{
    lock (_gate)
    {
        // 1. no order -> NoOrder (return, do not throw)
        // 2. target = WmsOrderLadder.ForTask(_tasks.Get(taskId).State)
        // 3. target == order.State -> AlreadyThere (no write)
        // 4. WmsOrderLadder.Rank(target) <= WmsOrderLadder.Rank(order.State)
        //      -> throw OrderRejectedException
        // 5. _orders[taskId] = order with { State = target }; return Advanced
    }
}
```

```csharp
// src/FleetCore/WmsOrderLadder.cs — the rank half of the same rule, on the same class
public static int Rank(string orderState) => orderState switch
{
    // TODO: the four rungs from record 30 §5. Unknown word -> ???
};
```

For the tests, each one is the same three steps: arrange a task and its order, act (`task.Start(now)`, then `board.SyncFromTask(taskId)`), assert the order's word with a **literal string** and the outcome with `OrderSyncOutcome.X`. If your assertion is `Assert.Equal(WmsOrderStates.InProgress, WmsOrderLadder.ForTask(TaskState.InProgress));` you have proved only that the two constants agree with each other. Write `"in_progress"` instead.
