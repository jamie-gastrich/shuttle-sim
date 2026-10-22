# Decision Record 34: The persistence slice — the pair moves together

**Date:** 2026-10-23
**Milestone:** M6 (WMS integration) — lesson 25.
**Status:** Accepted. Append-only. Builds on [record 29](29-wms-order-to-task-mapping.md) §5 (the pair lives in memory, lost together), [record 33](33-execution-slice.md) §7 (serve keeps the pair in memory; SQL waits for this slice) and §11 (this slice owns SQL for both boards). **Implements:** the persistence half of M6's done-when — orders and tasks survive a restart, and the WMS words owed survive with them.
**Wire contract:** MQTT unchanged. REST unchanged. `ContractVersion.Current` stays `4`. `src/Contracts/`, `docs/contract/` do not move.

---

## 1. What we are deciding

After lesson 24 the fleet manager can run work, but `serve` keeps the order board and the task board in memory. A restart forgets both. That is acceptable only while the slice is new. M6's done-when says a WMS outage or a bad order must not corrupt fleet state, and a restart in the middle of a task is the sharpest version of that.

This record decides:
- **A.** Where the order board is stored — beside the task, or somewhere else.
- **B.** The transaction — one write or two.
- **C.** Restore on start — rebuild memory from the tables.
- **D.** Re-marking owed words — which words the WMS is still owed after a restart.
- **E.** Whether `serve` switches to SQL now or stays in memory.

## 2. Decision A: the order board gets its own table, next to the task table

**Decision A.** A second table, `wms_orders`, beside `fleet_tasks`. One row per order, columns mirroring `WmsOrder`: `order_id`, `kind`, `pallet_id`, `lane_id`, `state`, `received_at`.

Reasons:
1. **The pair is already two things.** `WmsOrder` and `FleetTask` are different shapes with different lifecycles. Forcing them into one table would either duplicate columns or split a row, and a half-written row is worse than two tables.
2. **The task table already works and is tested.** `PostgresFleetTaskRepository` and `RestartSurvivalTests` prove save and load for tasks. Adding a parallel repository for orders reuses that proof instead of rewriting it.
3. **The repository seam already exists.** `IFleetTaskRepository` is the shape. An `IWmsOrderRepository` with the same four methods (`SaveAsync`, `SaveAllAsync`, `LoadAllAsync`, `GetAsync`) keeps the two boards symmetric.

Rejected: a single joined table with nullable order columns. It would make every task row carry order data it does not need, and a task created without an order (the cancel slice, tools, tests) would have to fill the order columns with nulls.

## 3. Decision B: one transaction for the pair

**Decision B.** When `serve` saves after a move, it saves the task and the order in **one transaction**. Either both land or neither does.

Reasons:
1. **The pair must move together (record 29 §5).** If the task is saved as `in_progress` and the order row is lost, a restart shows a task in progress with no order, and the WMS is never told. That is exactly the corruption M6 forbids.
2. **PostgreSQL gives us this for free.** `BeginTransactionAsync`, two `SaveTaskAsync`/`SaveOrderAsync` calls, one `CommitAsync`. The existing `SaveAllAsync` already does this for a list of tasks; the pair is the same idea with two tables.
3. **The alternative is worse.** Two separate saves means a crash between them leaves the pair split, and recovery code would have to guess which one is right.

Rejected: saving the task now and the order "soon". There is no "soon" in a crash.

## 4. Decision C: restore on start rebuilds both boards

**Decision C.** On `serve` start, before the bus connects, load every task and every order from SQL into the in-memory boards. The task board is built task by task (as `RestartSurvivalTests` already does). The order board gets a matching load path.

Reasons:
1. **The in-memory boards are the working copy.** The executor, the tick and the pusher all read from them. Loading once at start keeps every hot path free of SQL.
2. **One place to restore.** `RunServeAsync` already builds the boards. Loading there means the rest of the program sees a fully populated fleet manager and needs no "is it loaded yet" checks.
3. **The order board needs a load seam.** `WmsOrderBoard` has no `LoadAll` today. Adding one (or a constructor that takes loaded orders) is the smallest change that keeps the board's locking honest.

## 5. Decision D: re-mark the words the WMS is owed

**Decision D.** After restoring, for every task that is `InProgress`, `Done` or `Failed`, call `outbox.MarkPending(taskId)`. An `Assigned` or `Queued` task marks nothing.

Reasons:
1. **The outbox was in memory and is empty after a restart.** We cannot know which words were already pushed, so the safe reconstruction is: every in-flight task owes at least one word. Re-pushing is harmless because the sender reads the current word at send time (record 31 §6) and the WMS handles a repeated word.
2. **Filtering by task state is the right cut.** `InProgress`, `Done` and `Failed` are the states in which the WMS has not yet heard a final word it can act on. `Queued` and `Assigned` mean the WMS already heard `received` at intake.
3. **No special-case code.** Marking pending is one call per in-flight task; no guessing about push history.

Rejected: re-marking every task. That would re-push `received`, which the WMS already got at intake, and would make a restart noisy.

## 6. Decision E: serve switches to SQL now

**Decision E.** `serve` uses `PostgresFleetTaskRepository` and the new order repository instead of the in-memory ones. The in-memory repository stays (it is still used by tests and by the unit-level facts).

Reasons:
1. **The repository seam was built for this.** `TaskDispatcher` takes `IFleetTaskRepository`. Swapping the implementation in `Program` is one line, plus the restore.
2. **The proof already exists.** `RestartSurvivalTests` loads tasks from SQL and rebuilds a board. The slice extends that proof to orders and to `serve` itself.
3. **The honest limit.** With SQL in `serve`, a restart no longer forgets. But the WMS still may be down; the outbox still retries. Persistence does not fix a broken WMS, it fixes a broken process.

## 7. What this slice does not do

| Not in this slice | Why | Who does it |
|---|---|---|
| Cancelling an order | needs a `Cancelled` task state and a row on the ladder | the cancel slice |
| The mock WMS (slow, gone, 500) | test fixture, not fleet logic | the mock-WMS slice |
| The tick reserving lanes | occupancy map not wired into the executor | the traffic slice |
| Re-marking words across a WMS that was down at restart | the outbox already retries; nothing new needed | — |

## 8. Verification (to be measured before teaching)

- `PostgresFleetTaskRepository` exists and `RestartSurvivalTests` passes (measured: 3 integration tests in `RestartSurvivalTests`? verify with `dotnet test --filter "Category=Integration"`).
- `WmsOrder` has 6 fields; `WmsOrderBoard` has no load path today (verify with grep).
- `IFleetTaskRepository` has 4 methods (verified from source).
- `WmsStatusOutbox` has `MarkPending` and `Claim` (verified from source).
filePath">
/home/jamie/shuttle-sim/docs/decisions/34-persistence-slice.md