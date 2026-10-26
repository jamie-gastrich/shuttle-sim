# Lesson 27: Order Cancel Slice — Build Report

## Summary

Implemented the M6 cancel slice: WMS order cancel requests now reach the fleet manager front door, cancel transitions a task to `Cancelled`, and the order ladder advances the WMS order state to `cancelled` without drifting from the task state.

## Files Changed

- `src/FleetCore/TaskState.cs` — added `Cancelled` to the task enum and the task wire mapping.
- `src/FleetCore/FleetTask.cs` — added `Cancel(DateTime now)` with the same clock-order guard and rejection pattern as `Fail`.
- `src/FleetCore/WmsOrderLadder.cs` — mapped `TaskState.Cancelled` to `WmsOrderStates.Cancelled` and made the rank ordering allow cancellation to advance past `done`.
- `src/FleetCore/WmsOrderBoard.cs` — added `TryUpdate` helper used to persist the updated order state when the ladder advances.
- `src/FleetCore/WmsRoutes.cs` — added the cancel route builder used by the WMS front door.
- `src/FleetManager/Program.cs` — wired the POST cancel endpoint so it looks up the order and task, cancels the task, syncs the ladder, and updates the order board.
- `src/FleetCore.Tests/TaskLifecycleTests.cs` — added task-lifecycle tests covering cancel transitions and rejection after terminal states.

## Verification Commands

```bash
cd /home/jamie/shuttle-sim/src
dotnet test --nologo --filter "TaskLifecycleTests|WmsOrderLadderTests|WmsContractLiteralsTests|WmsOrderBoardTests"
```

Result: 44 passed, 0 failed.

## Notes

The full FleetManager integration suite still depends on the local Docker-backed MQTT broker and PostgreSQL services; those were not started in this session, so the integration tests remain not yet verified locally. The core cancel logic and state-machine checks were verified directly with the targeted FleetCore test run above.

Owner sign-off: Jamie
