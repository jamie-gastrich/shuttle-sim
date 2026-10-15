# src/FleetCore

Tasks and the task board. Pure: a task is data plus a state machine, and the board holds them. No broker, no database, no clocks hard-coded.

## Depends on

- Nothing. (Persistence is expressed as an interface only — `IFleetTaskRepository` lives here, the `Npgsql` implementation lives in `FleetManager`.)

## Used by

- `FleetManager` (assignment, dispatch, persistence).
- `FleetCore.Tests`.

## Overview

FleetCore owns the task as data: what needs doing, which shuttle, current state. It is pure — no broker, no database, no clock.

```text
  FleetManager (TaskDispatcher, RunLoop)
      │
      │  board.Add(task)                 → enqueue a new FleetTask
      │  board.NextQueued()              → the next task to assign
      │  task.Assign(shuttleId, now)     → Queued     → Assigned
      │  task.Start(now)                 → Assigned   → InProgress
      │  task.Complete(now)              → InProgress → Done
      │  task.Fail(reason, now)          → Failed
      ▼
  FleetManager persists through IFleetTaskRepository (the Postgres impl lives
  in FleetManager, not here). On startup the board is rebuilt from the
  database so tasks survive a restart.
```

Every method takes `now` and uses the supplied `TaskId` — nothing here reads the
clock or generates ids, which keeps the module deterministic and database-free
for unit tests. Illegal transitions throw `TaskRejectedException`. See record 11
for the task lifecycle. The WMS (milestone 6) will create tasks through this
same board.

## The files

| File | Where it fits |
|---|---|
| `FleetTask.cs` | One task and its state machine: `Assign` → `Start` → `Complete`, or `Fail`. |
| `TaskState.cs` | The enum `Queued/Assigned/InProgress/Done/Failed` and the wire names. |
| `TaskBoard.cs` | The in-memory collection: `Add`, `TryGet`, `Get`, `NextQueued`, `Count`. |
| `IFleetTaskRepository.cs` | Save/load contract; the Postgres implementation lives in FleetManager. |
| `TaskRejectedException.cs` | Thrown on an illegal task transition (e.g. completing a queued task). |
