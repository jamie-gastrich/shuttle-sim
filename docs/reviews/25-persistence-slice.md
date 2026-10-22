# Review: Lesson 25 — The persistence slice

**Verdict:** pass

**Build/test evidence:** `cd src && dotnet build --no-incremental` → 0 warnings, 0 errors. Pure suite `Category!=Integration` → Failed: 0, Passed: 187, Total: 187 (FleetManager 67). Integration suite with the database up → Failed: 0, Passed: 4, Total: 4 (`RestartSurvivalTests` both facts pass, including `Orders_survive_a_restart_alongside_tasks`). Contract untouched: `ContractVersion.Current = 4`.

## Acceptance criteria

- [x] R1 `IWmsOrderRepository` — four methods mirroring `IFleetTaskRepository`, lives in `FleetCore`, no SQL.
- [x] R2 `WmsOrderBoard.Load` — adds a restored order without creating a task; throws on duplicate id.
- [x] R3 `serve` restores before the bus connects — `LoadAllAsync` on both repos, then populate, then re-mark; bus connects after.
- [x] R4 re-marking — `outbox.MarkPending` for every `InProgress`/`Done`/`Failed` restored task; `Queued`/`Assigned` mark nothing.
- [x] R5 one transaction for the pair — `BeginTransactionAsync` in `TaskDispatcher.SaveAsync` when both repos are PostgreSQL; throws `InvalidOperationException` if no order.
- [x] R6 `serve` uses SQL — `PostgresFleetTaskRepository` + `PostgresWmsOrderRepository`.
- [x] R7 connection string read from `FleetManager:PostgresConnectionString` (with env fallback) and validated non-blank.
- [x] R8 facts — `Orders_survive_a_restart_alongside_tasks` (integration, passes) and `Restoring_an_in_progress_task_remarks_its_word` (unit, passes).
- [x] R9 no forbidden file moved; contract unchanged.

## Top issues (max 5), ranked

1. **consider — `TaskDispatcher` now knows about Npgsql and the two Postgres types.** The transaction branch casts `IFleetTaskRepository`/`IWmsOrderRepository` to their PostgreSQL implementations. That is a small leak of storage detail into the dispatcher, which was previously storage-agnostic. It is the price of a real transaction through the existing seam; acceptable for this slice, but if a third store appears, extract a unit-of-work interface.

2. **consider — `serve` restore runs on the startup thread with no retry.** If PostgreSQL is briefly unavailable at start, `serve` throws and exits instead of retrying. Fine for a simulator; a production manager would retry with backoff. Named, not hidden.

3. **consider — `PostgresWmsOrderRepository.SaveAsync(order, connection, transaction, …)` is a public overload only the dispatcher uses.** It is not part of `IWmsOrderRepository`. That is deliberate (the seam stays storage-free), but the extra public surface is easy to misuse. A comment marking it "for transactional callers only" would help.

## What is solid

- The pair is saved in one transaction; a crash between the two writes is impossible because there is no gap.
- Restore happens before the bus connects, closing the window where a status message could arrive before the boards are populated.
- Re-marking is filtered by task state, so a `Queued` task does not re-push `received`.
- The in-memory repositories remain for unit tests; the dispatcher falls back to separate saves for them, so the 67 unit facts stay green without a database.

## Explain-it check

1. Why must the task and the order be saved in the same transaction? What would a crash between two separate saves look like after a restart?
2. `serve` restores the boards before it connects the bus. What race does that close?
3. After a restart the outbox is empty. Why is marking pending for every in-flight task safe, even if the WMS already heard that word before the crash?

Unclear/copied blocks to study:
- `TaskDispatcher.SaveAsync` — the cast-and-transaction branch vs the fallback branch. Trace which path runs in a unit test vs in `serve`.
- `RestartSurvivalTests.Orders_survive_a_restart_alongside_tasks` — the manual `DELETE FROM wms_orders` and the `GetAsync` call that ensures the table exists first.

## Revisit list

- Transactional seam: a unit-of-work interface if a third store appears.
- Startup retry against PostgreSQL.
- Marking the transactional overloads as internal-only.

## End-product check

This slice closes the restart hole in M6: a process death no longer forgets the pair or the words owed. M6's done-when is now closer — orders flow to completed pallets and back, and a restart does not corrupt state. Still owed: cancel (needs a `Cancelled` state), the mock WMS (slow/gone/500), and traffic control (the tick still does not reserve lanes). Those are named, not hidden.

## Verification of review fixes

Implementer addressed issues 2 and 3; issue 1 left as a design note with an explanatory comment.

- Issue 2 (startup retry): `serve` restore now retries up to three times with a 500 ms delay. Verified by inspection; unit suite still green.
- Issue 3 (public transactional overloads): both `SaveAsync(..., connection, transaction, …)` overloads are now `internal`. Verified by `dotnet build` (0 warnings/errors) and the integration suite (4/4 pass).
- Issue 1 (storage-type leak in `TaskDispatcher`): left as a design note, as the review called it acceptable for this slice. A comment now explains the cast-and-transaction branch.

Re-verified: `cd src && dotnet build --no-incremental` → 0 warnings, 0 errors; `Category!=Integration` → Failed: 0, Passed: 187; `Category=Integration` (database up) → Failed: 0, Passed: 4.

## README status

M6 remains `in progress` (cancel, mock-WMS and traffic slices still owed). No table change.