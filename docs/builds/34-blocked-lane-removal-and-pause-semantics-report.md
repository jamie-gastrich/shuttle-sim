# Build report: Lesson 34 — Blocked-lane removal and dispatch-pause semantics

**Summary:** Implemented blocked-lane removal on recovery and corrected dispatch-pause semantics. `Rack` now supports `UnmarkBlocked` and `BlockedLanesOnRoute`; `Shuttle` records blocked lanes on a faulted move and unmarks them in `Recover`. `TaskBoard.PauseAllExcept`/`ResumeAllExcept` now pause the named shuttle's own work. Added/updated unit tests; all non-integration tests pass.

## Files
- `src/ShuttleCore/Rack.cs` — added `UnmarkBlocked(string laneId)` and `BlockedLanesOnRoute(string fromLane, string toLane)`.
- `src/ShuttleCore/Shuttle.cs` — added `_blockedLanesOnFault`; record blocked lanes in `StartMove`; unmark in `Recover`.
- `src/FleetCore/TaskBoard.cs` — changed pause/resume conditions; updated class comment.
- `src/ShuttleCore.Tests/RackTests.cs` — added two `UnmarkBlocked` tests.
- `src/ShuttleCore.Tests/RecoveryTests.cs` — added three recovery tests.
- `src/FleetCore.Tests/TaskBoardPauseTests.cs` — renamed/updated tests per R4.

## How to run it
```
cd src && dotnet build ShuttleFleet.slnx
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~Rack"
cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~TaskBoardPause"
```

## Verification
- `cd src && dotnet build ShuttleFleet.slnx` → Build succeeded, 0 warnings, 0 errors.
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → 247 passed, 0 failed (FleetCore 77, TrafficCore 6, ShuttleCore 36, Contracts 8, Messaging 10, MqttMessaging 3, MockWms 9, ShuttleSim 10, FleetManager 88).
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~Rack"` → 11 passed, 0 failed.
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~TaskBoardPause"` → 4 passed, 0 failed.
- `grep -q "UnmarkBlocked" src/ShuttleCore/Rack.cs` and `src/ShuttleCore/Shuttle.cs` → OK.
- `git diff $BASE -- src/Contracts docs/contract src/FleetManager/Program.cs src/ShuttleSim` → empty (no changes).
- `ContractVersion.Current` remains 4.

## Choices made
- Used `BlockedLanesOnRoute` helper on `Rack` (per hints) as the single source for which lanes are recorded; `UnmarkBlocked` remains the only removal method.
- Recovery unmarks only recorded lanes; non-blocked faults record nothing because `StartMove`'s blocked branch is the only place that fills the list.
- Kept method names `PauseAllExcept`/`ResumeAllExcept` unchanged per decision 43.

## Study first
1. `src/ShuttleCore/Rack.cs` — `BlockedLanesOnRoute`: ordinal range check over `_blockedLanes`.
2. `src/ShuttleCore/Shuttle.cs` — `StartMove` blocked branch records lanes before `RaiseFault`.
3. `src/ShuttleCore/Shuttle.cs` — `Recover` unmarks recorded lanes then clears state.
4. `src/FleetCore/TaskBoard.cs` — pause/resume conditions and class comment.
5. `src/FleetCore.Tests/TaskBoardPauseTests.cs` — updated expectations, especially resume restoring requeued work.

## Outside scope
- Cross-process pause, alarm history vs current state, mock WMS client (not in this lesson).
- Did not touch `src/Contracts`, `docs/contract`, `src/FleetManager/Program.cs`, or `src/ShuttleSim`.

## Stopped early
- None.

Owner sign-off:
