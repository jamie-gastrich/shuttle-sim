# Lesson 25 hints: The persistence slice

## Tier 1 — nudge

- The order board has no way to receive a restored order today. What method would let one in without going through `Submit`?
- The dispatcher saves a task after every move. Where is the order saved in the same breath?
- After a restart the outbox is empty. Which tasks still owe a word, and how do you ask the ladder?

## Tier 2 — direction

- Add `Load(WmsOrder order)` to `WmsOrderBoard`: put the order into the dictionary, throw if the id already exists. Do not create a task and do not sync.
- In the dispatcher, after the move and before `SaveAsync`, look up the order for the task id. Save both in one transaction: open a connection, begin a transaction, save the task, save the order, commit. If the order is missing, throw.
- In `Program.RunServeAsync`, before `bus.ConnectAsync`, call `LoadAllAsync` on both repositories and populate both boards. Then walk the restored tasks; for each in-flight one (`InProgress`, `Done`, `Failed`) call `outbox.MarkPending`.
- Add `IWmsOrderRepository` to `FleetCore` with the same four methods as `IFleetTaskRepository`. Implement it in `FleetManager` with a `wms_orders` table.

## Tier 3 — near-solution

- `IWmsOrderRepository`:
  ```csharp
  public interface IWmsOrderRepository
  {
      Task SaveAsync(WmsOrder order, CancellationToken cancellationToken);
      Task SaveAllAsync(IEnumerable<WmsOrder> orders, CancellationToken cancellationToken);
      Task<IEnumerable<WmsOrder>> LoadAllAsync(CancellationToken cancellationToken);
      Task<WmsOrder?> GetAsync(string orderId, CancellationToken cancellationToken);
  }
  ```
- `WmsOrderBoard.Load`:
  ```csharp
  public void Load(WmsOrder order)
  {
      lock (_gate)
      {
          if (_orders.ContainsKey(order.OrderId))
              throw new InvalidOperationException($"duplicate order id {order.OrderId}");
          _orders.Add(order.OrderId, order);
      }
  }
  ```
- Re-marking in `Program`:
  ```csharp
  foreach (var task in restoredTasks)
  {
      if (task.State is TaskState.InProgress or TaskState.Done or TaskState.Failed)
          outbox.MarkPending(task.TaskId);
  }
  ```
- The dispatcher's transaction (sketch):
  ```csharp
  await using var connection = new NpgsqlConnection(_connectionString);
  await connection.OpenAsync(cancellationToken);
  await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
  await taskRepository.SaveAsync(task, connection, transaction, cancellationToken);
  await orderRepository.SaveAsync(order, connection, transaction, cancellationToken);
  await transaction.CommitAsync(cancellationToken);
  ```
  Adapt the existing `PostgresFleetTaskRepository` methods to accept a connection and transaction, or add overloads; keep the public `IFleetTaskRepository` signatures unchanged.
filePath">
/home/jamie/shuttle-sim/docs/lessons/25-persistence-slice-hints.md