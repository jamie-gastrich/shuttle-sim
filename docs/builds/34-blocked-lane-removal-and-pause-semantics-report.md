# Build report: Lesson 34 — Blocked-lane removal and dispatch-pause semantics

**Summary:** Implemented blocked-lane removal on recovery, corrected dispatch-pause semantics, deduplicated range logic in `Rack.IsRouteBlocked`, ran mutation checks a-d (all went red as required), and attempted M7 stretch scenario (not possible quickly — no serve process running; broker and database are up). Updated `src/ShuttleCore/Rack.cs` and the report.

## Files
- `src/ShuttleCore/Rack.cs` — added `UnmarkBlocked(string laneId)`, `BlockedLanesOnRoute(string fromLane, string toLane)`, and deduplicated `IsRouteBlocked` to delegate to `BlockedLanesOnRoute(...).Any()`.
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

## Mutation checks (run in scratch copy `/tmp/opencode/m34_mutation`)

| # | Mutation applied | Targeted test | Result |
|---|---|---|---|
| a | Removed `UnmarkBlocked` call from `Recover` (commented out in `Shuttle.cs`) | `Recover_clears_the_blocked_lane_that_caused_the_fault` | **Red** (failed) |
| b | Changed `Recover` to unmark every blocked lane (`_rack.LaneId` loop) instead of only recorded ones | `Recover_does_not_clear_an_unrelated_blocked_lane` | **Red** (failed) |
| c | Flipped `PauseAllExcept` back to `owner != shuttleId` | `Pausing_one_shuttle_pauses_only_its_own_assigned_work` | **Red** (failed) |
| d | Dropped `AssignedShuttleId is null` case from `ResumeAllExcept` | `Resuming_one_shuttle_restores_its_paused_work` | **Red** (failed) |

All four mutations produced failing targeted tests as the lesson requires.

## Choices made
- Used `BlockedLanesOnRoute` helper on `Rack` (per hints) as the single source for which lanes are recorded; `UnmarkBlocked` remains the only removal method.
- Recovery unmarks only recorded lanes; non-blocked faults record nothing because `StartMove`'s blocked branch is the only place that fills the list.
- Deduplicated `Rack.IsRouteBlocked` to delegate to `BlockedLanesOnRoute(...).Any()` (review #3). One source of truth for the inclusive-ordinal route check.
- M7 stretch scenario: services available but no active serve process, so full scenario not completed quickly; recorded as not yet verified.
- Kept method names `PauseAllExcept`/`ResumeAllExcept` unchanged per decision 43.

## Study first
1. `src/ShuttleCore/Rack.cs` — `BlockedLanesOnRoute`: ordinal range check over `_blockedLanes`.
2. `src/ShuttleCore/Shuttle.cs` — `StartMove` blocked branch records lanes before `RaiseFault`.
3. `src/ShuttleCore/Shuttle.cs` — `Recover` unmarks recorded lanes then clears state.
4. `src/FleetCore/TaskBoard.cs` — pause/resume conditions and class comment.
5. `src/FleetCore.Tests/TaskBoardPauseTests.cs` — updated expectations, especially resume restoring requeued work.

## Outside scope
- Cross-process pause, alarm history vs current state, mock WMS client (not in this lesson).
- M7 full-recovery stretch scenario: broker (`shuttle-fleet-broker-1`) and database (`shuttle-fleet-database-1`) are running (`docker compose -f docker/compose.yml ps` confirmed both `running`), but no `serve` process is active (verified with `ps aux | grep dotnet`) and no quick injection script is available. The full dashboard sequence (fault → Pause → Acknowledge → Resume) could not be completed quickly; noted as not yet verified. Milestone M7 stays `in progress` pending that run.
- Did not touch `src/Contracts`, `docs/contract`, `src/FleetManager/Program.cs`, or `src/ShuttleSim`.

## M7 full-recovery stretch scenario attempt
- **Services:** `docker compose -f docker/compose.yml ps` shows `broker` (Up 4 seconds) and `database` (Up 3 seconds) running on ports `1883` and `5432`. Confirmed at 2026-10-29 15:55.
- **Attempt 1 (serve process):** `timeout 10 bash -c 'cd src && dotnet run --project FleetManager/FleetManager.csproj -- serve'` produced no stdout, was killed by timeout (exit 124). Log `/tmp/opencode/fleet_manager_serve2.log` is 0 bytes. No active `serve` process exists (`ps aux | grep dotnet` shows only VS Code language-server / MSBuild processes; no `serve` or `ShuttleSim` process).
- **Attempt 2 (any available process/script):** No quick injection script found in repo (`find . -maxdepth 4 \( -name "*.sh" -o -name "*inject*" -o -name "*scenario*" \)` returned nothing). `ShuttleSim` `serve` mode requires `appsettings.json` config and MQTT connection; starting it quickly also hangs.
- **Fault injection attempt:** Could not inject a blocked-lane fault through the dashboard or core because no `serve` loop is active to process `OperatorCommandMessage` and no shuttle endpoint is subscribed.
- **Acknowledge → next status `Idle` with no fault:** Not measured. No active shuttle status stream to observe.
- **Retry succeeds (task completes after recovery, not looping):** Not confirmed. No serve loop to retry the task.
- **Failure cause:** The full M7 scenario requires an active `serve` process (fleet manager loop + shuttle endpoint + MQTT subscription) to drive the sequence `fault → Pause → Acknowledge → Resume`. That process is not running, and no quick start/injection script is available in the repo. The scenario could not be completed quickly; it is recorded as unverified, not as a failure of the built code.

## Stopped early
- M7 full-recovery stretch scenario (fault → Pause → Acknowledge → Resume with timing measurement) was not completed: broker and database services are up, but no `serve` process is running and no scenario injection script is available quickly. Noted in Outside scope; milestone M7 remains `in progress`.

Owner sign-off: 2026-10-29, explained and verified
