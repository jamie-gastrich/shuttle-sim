# Build report: Lesson 25 — The persistence slice

Owner sign-off: Jamie

## Summary

Built the persistence slice: an `IWmsOrderRepository` seam in `FleetCore`, a `PostgresWmsOrderRepository` with a `wms_orders` table, a `Load` method on `WmsOrderBoard`, transactional task+order saves in `TaskDispatcher`, and `serve` restore + re-mark of owed words before the bus connects. Unit suite stays green (187 → 188? see counts). Integration tests written but not run (no database available this session).

## Files

New:
- `src/FleetCore/IWmsOrderRepository.cs` — four-method seam mirroring `IFleetTaskRepository`.
- `src/FleetManager/PostgresWmsOrderRepository.cs` — PostgreSQL store for orders.
- `src/FleetManager.Tests` additions in `ExecutionSliceTests.cs` and `RestartSurvivalTests.cs`.

Edited:
- `src/FleetCore/WmsOrderBoard.cs` — added `Load(WmsOrder)`.
- `src/FleetCore/OrderSync.cs` — exposed `Board`.
- `src/FleetManager/PostgresFleetTaskRepository.cs` — added `SaveAsync` overload accepting a connection and transaction, plus `ConnectionString`.
- `src/FleetManager/TaskDispatcher.cs` — optional `IWmsOrderRepository` and connection string; transactional save when both repos are PostgreSQL.
- `src/FleetManager/Program.cs` — read/validate connection string, build both repos, restore both boards, re-mark owed words, pass repos to dispatcher.
- `src/FleetManager/appsettings.json` — `FleetManager:PostgresConnectionString`.

## How to run it

```
cd src
dotnet build --no-incremental
dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# with the database up:
docker compose -f docker/compose.yml up -d
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"
```

## Verification

1. Clean build, no warnings:
   - Command: `cd src && dotnet build --no-incremental`
   - Output: `0 Warning(s), 0 Error(s)`.

2. Pure suite green:
   - Command: `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"`
   - Output: `Failed: 0` across 8 projects; FleetManager 67 passed.

3. Acceptance greps:
   - `test -f src/FleetCore/IWmsOrderRepository.cs` (pass)
   - `grep -q "Load(" src/FleetCore/WmsOrderBoard.cs` (pass)
   - `test -f src/FleetManager/PostgresWmsOrderRepository.cs` (pass)
   - `grep -q "BeginTransactionAsync" src/FleetManager/TaskDispatcher.cs` (pass)
   - `grep -q "LoadAllAsync" src/FleetManager/Program.cs` (pass)

4. Integration tests: not run this session (no database). `Orders_survive_a_restart_alongside_tasks` and the existing `RestartSurvivalTests` are written but unverified — marked "not yet verified".

## Choices made

- Made the order repository and connection string optional constructor parameters on `TaskDispatcher` so the existing unit tests (which use `InMemoryFleetTaskRepository`) keep their three-argument form.
- Transactional save only when both repositories are the PostgreSQL implementations; otherwise save the task (and the order, if a repo is present) separately.
- Re-mark in-flight tasks with `outbox.MarkPending` unconditionally rather than via `SyncFromTask`, because the outbox is empty after a restart and the sync would return `AlreadyThere` for words already in step.

## Study first

1. `src/FleetManager/TaskDispatcher.cs` `SaveAsync` — the transaction branch vs the fallback branch.
2. `src/FleetManager/Program.cs` — restore block before `bus.ConnectAsync`.
3. `src/FleetCore/WmsOrderBoard.cs` `Load` — the one new method.
4. `src/FleetManager/PostgresWmsOrderRepository.cs` — the `wms_orders` table and upsert.

## Outside scope

- Cancel slice, mock-WMS slice, traffic slice.
- Starting/running the database this session.

## Lesson 25 R4 (resolved)

The professor updated R4, §4.3, the worked example, the hints and decision 34 §5 to the `MarkPending`-for-in-flight-tasks rule. The implementation matches.

## Fixes after review

- `PostgresWmsOrderRepository` and `PostgresFleetTaskRepository` transactional `SaveAsync` overloads are now `internal`, shrinking the public surface to the interface only.
- `serve` restore now retries up to three times with a 500 ms delay in case PostgreSQL is still starting.
- Added a comment in `TaskDispatcher.SaveAsync` explaining the cast-and-transaction branch. (Issue 1, the storage-type leak, is left as a design note: the reviewer called it acceptable for this slice.)

## Stopped early

Integration tests not run (no database available). The reviewer should run them with the database up.