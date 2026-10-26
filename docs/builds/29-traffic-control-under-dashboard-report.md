# Lesson 29: Traffic control under the dashboard — Build Report

## Summary

Added per-shuttle pause state to `RunLoopGate`. A paused shuttle is denied new moves, and an in-progress move is skipped by the run loop until the shuttle is resumed. Its position, occupied lane, and transit reservation remain unchanged while paused.

The pause state is independent of `TaskBoard`. No message contract, configuration, dashboard, or serve-loop wiring was changed.

## Files changed

| File | Changes |
|---|---|
| `src/FleetManager/RunLoopGate.cs` | Added ordinal pause tracking, per-shuttle and all-shuttle pause/resume helpers, and the pause check before occupancy and traffic checks. |
| `src/FleetManager/RunLoop.cs` | Skips paused moving shuttles in the move-completion loop. |
| `src/FleetManager.Tests/RunLoopGateTests.cs` | Added tests for paused movement denial, resumed movement permission, and pause/resume-all behavior. |
| `src/FleetManager.Tests/RunLoopStressTests.cs` | Added a real run-loop test proving a paused shuttle holds its state, position, occupancy, and reservation until resumed. |

## Verification

- `dotnet test src/FleetManager.Tests/FleetManager.Tests.csproj --filter "FullyQualifiedName~RunLoopGate|FullyQualifiedName~Paused_shuttle_does_not_complete_its_move"` — passed, 9 tests after the latest review follow-up.
- `dotnet build src/ShuttleFleet.slnx --no-incremental` — passed, 0 warnings and 0 errors.
- `dotnet test src/ShuttleFleet.slnx -v m --filter "Category!=Integration"` — passed, 224 tests, 0 failed after the latest review follow-up.
- Assignment source checks for `IsPaused`, `Pause`, and the move-completion pause check — passed.
- Contract scope check (`git diff -- src/Contracts docs/contract`) — passed; no contract changes.
- `appsettings.json` was not changed. `ContractVersion.Current` was not changed.

## Review follow-up

- `CanMove_returns_false_when_paused` records a clear occupied lane before pausing and confirms the same route is allowed after resume.
- The paused-move test now uses a one-second-per-lane travel time and pauses for five ticks, longer than the two-second route. Without the completion-loop pause check, the move would complete during the paused interval.
- `CommitMove` now refuses a paused shuttle before reserving any route; a test verifies no reservation is created.
- The review's concern about connecting separate pause sets to dashboard/serve-loop commands and deciding whether paused simulation time counts toward travel remains out of scope for this lesson and requires a follow-up decision.
- `dotnet test src/FleetManager.Tests/FleetManager.Tests.csproj --filter "FullyQualifiedName~RunLoopGate|FullyQualifiedName~Paused_shuttle_does_not_complete_its_move"` — passed, 10 tests after the prior follow-up, before removing the vacuous case.
- `dotnet build src/ShuttleFleet.slnx --no-incremental` — passed, 0 warnings and 0 errors after the follow-up.
- `dotnet test src/ShuttleFleet.slnx -v m --filter "Category!=Integration"` — passed, 225 tests, 0 failed after the prior follow-up, before removing the vacuous case.
- `git diff --check` — passed.
- Mutation check: temporarily removing the `CanMove` pause guard made `CanMove_returns_false_when_paused` fail; temporarily removing the move-completion pause guard made `Paused_shuttle_does_not_complete_its_move` fail. Both guards were restored, then the 10 focused tests passed again.
- Removed the no-occupancy test because its false result could come from the missing-lane branch even if the pause guard were removed; the main paused test remains mutation-sensitive.

## Study first

- `src/FleetManager/RunLoopGate.cs` — why the pause check runs before reading occupancy, and how the paused-id set makes pause/resume idempotent.
- `src/FleetManager/RunLoop.cs` — why a paused moving shuttle is skipped before `CompleteMove`, occupancy updates, and reservation release.
- `src/FleetManager.Tests/RunLoopStressTests.cs` — how the test proves both that the shuttle remains paused with its reservation held and that resuming lets the same task finish.

## Owner review gate

Owner sign-off: 2026-10-26, explained and verified
