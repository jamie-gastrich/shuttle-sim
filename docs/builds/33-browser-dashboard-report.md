# Build report 33: browser dashboard

## Summary

Built the Vue/Vite browser dashboard and its Fleet Manager REST bridge: dashboard configuration, a lock-protected `ShuttleRegistry.Snapshot()`, alarm table creation plus save path in `FleetServeLoop`, static-file serving, and the five `/api/dashboard/*` routes. Wired the Vue build into Docker via a Node stage.

This fix pass addresses review 33. Pausing one shuttle no longer hides every queued task. The dashboard API tests now use the same route-mapping code as `Program.cs` instead of a copy. An alarm-database save failure is logged and skipped so liveness evaluation continues for other shuttles. The alarm integration tests now use unique test ids and remove their rows.

## Files

Original build files:

- `src/Dashboard/` — Vue/Vite app (`package.json`, `package-lock.json`, `vite.config.js`, `index.html`, `src/main.js`, `src/App.vue`, `README.md`).
- `src/FleetManager/Program.cs` — dashboard config validation, static files, alarm repository passed to serve loop; now calls the shared dashboard route mapping.
- `src/FleetManager/ShuttleRegistry.cs` — added lock-protected `Snapshot()`.
- `src/FleetManager/FleetServeLoop.cs` — optional alarm repository, saves each raised alarm; now accepts `IAlarmRepository` and isolates save failures.
- `src/FleetManager/PostgresAlarmRepository.cs` — creates `alarms` table; `EnsureTableAsync` in all methods; now implements `IAlarmRepository`.
- `src/FleetManager/appsettings.json` — added `Dashboard:LaneIds` and `Dashboard:RefreshIntervalMs`.
- `src/FleetManager/DashboardRoutes.cs` — route constants.
- `src/FleetManager/DashboardOperatorCommandRequest.cs` — request DTO.
- `src/FleetManager/FleetManager.csproj` — copy generated `wwwroot` to output.
- `docker/Dockerfile.build` — Node 22 stage builds frontend before .NET build.
- `.gitignore` — ignore `src/Dashboard/node_modules/` and `src/FleetManager/wwwroot/`.
- `src/FleetManager.Tests/DashboardIntegrationTests.cs` — rewritten to use the production dashboard route mapping with fake bus and alarm repository.
- `src/FleetManager.Tests/AlarmPersistenceTests.cs` — uses unique test ids, cleans up test alarm rows.
- `src/MqttMessaging/MqttMessageBus.cs` — map `MessageFamily.Json` to command QoS (needed for operator topic; outside the original lesson file list).
- `docs/builds/33-browser-dashboard-report.md` — this report.

Files added or changed in this fix pass:

- `src/FleetManager/DashboardEndpoints.cs` — new shared route mapping used by both `Program.cs` and the dashboard tests.
- `src/FleetManager/IAlarmRepository.cs` — new repository seam so the serve loop and routes can be tested with a fake or failing repository.
- `src/FleetCore/TaskBoard.cs` — `PauseAllExcept` / `ResumeAllExcept` now compare against the task's assigned shuttle, not its task id, and leave unowned queued work dispatchable.
- `src/FleetCore.Tests/TaskBoardPauseTests.cs` — new tests for pause/resume semantics.
- `src/FleetManager.Tests/FleetServeLoopTests.cs` — added a failing-alarm-repository test.
- `src/FleetManager.Tests/TestDatabase.cs` — added `ResetAlarmsAsync` for test-only alarm cleanup.

## How to run it

Frontend:

```bash
cd src/Dashboard
npm ci
npm run build
cd ../..
```

Backend:

```bash
cd src
dotnet build ShuttleFleet.slnx
dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
cd ..
```

Integration tests (broker and database up):

```bash
docker compose -f docker/compose.yml up -d
cd src
dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"
cd ..
docker compose -f docker/compose.yml down
```

Container:

```bash
docker build -f docker/Dockerfile.build -t shuttle-fleet-build .
docker run --rm shuttle-fleet-build
```

## Verification

- `cd src && dotnet build ShuttleFleet.slnx -v m` → Build succeeded, `0 Warning(s)`, `0 Error(s)`.
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → **242 passed, 0 failed**: Contracts 8, FleetCore 77, Messaging 10, MockWms 9, TrafficCore 6, MqttMessaging 3, ShuttleSim 10, ShuttleCore 31, FleetManager 88.
- With broker and database up: `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"` → **6 passed, 0 failed** in FleetManager.Tests.
- `docker compose -f docker/compose.yml exec -T database psql -U shuttle -d shuttle -c "SELECT shuttle_id, code FROM alarms ORDER BY shuttle_id;"` after the integration run → `(0 rows)`. The test debris (`SH-INT`, `SH-INT2`, `TEST-ALARM-*`) is gone.
- `cd src/Dashboard && npm ci && npm run build` → `11 modules transformed`, `../FleetManager/wwwroot/index.html` and assets written.
- `docker build -f docker/Dockerfile.build -t shuttle-fleet-build-fix .` → succeeded.
- `docker run --rm shuttle-fleet-build-fix` → **242 passed, 0 failed**; FleetManager.Tests 88 passed.
- Live pause check with the real Fleet Manager, broker, and database:

  ```text
  tasks before pause:  [{"taskId":"task-hold",...},{"taskId":"FIXCHECK-001",...}]
  tasks while paused:  [{"taskId":"task-hold",...},{"taskId":"FIXCHECK-001",...}]
  tasks after resume:  [{"taskId":"task-hold",...},{"taskId":"FIXCHECK-001",...}]
  ```

  Pausing `SH-01` no longer empties the queued-task list. The shell wrapper hit its 120-second timeout while waiting during cleanup after printing these results; a follow-up `ps` and `ss -ltnp` showed no FleetManager process and no listener on port 5080.

Mutation checks, run in `/tmp/opencode` against a copy:

- Removed the `bus.PublishAsync` call from the shared production command route → `Operator_command_valid_publishes_on_operator_topic` failed (1 failed, 6 passed).
- Changed the unknown-kind branch to accept `Pause` → `Operator_command_invalid_kind_is_rejected_and_not_published` failed (1 failed, 6 passed).

## Choices made

- **Pause semantics.** For a named shuttle, `PauseAllExcept` now pauses only tasks already assigned to a *different* shuttle. Unassigned queued tasks stay dispatchable, and work owned by the named shuttle is left running. `ResumeAllExcept` clears the marks, including unowned tasks whose old pause mark would otherwise go stale. A blank shuttle id still pauses/resumes every queued task as before. This is the change the review asked for, but it changes the meaning of `PauseAllExcept` described in lessons 26/30 and records 38/39; normally that needs a decision record. I did not edit lessons or decisions.
- **Shared route mapping.** `DashboardEndpoints.MapDashboardRoutes` is called by both `Program.cs` and `DashboardIntegrationTests`. The review explicitly suggested a shared route-mapping method, so the tests no longer re-declare the routes.
- **Repository seam.** Added `IAlarmRepository` so the serve loop can be tested with a repository that throws on save, and the dashboard routes with an in-memory fake. `PostgresAlarmRepository` keeps its existing behaviour.
- **Failure isolation.** `FleetServeLoop` now catches only `SaveAsync` failures, logs them, and continues evaluating other shuttles. Cancellation is still rethrown.
- **Cleanup, not a second database.** Integration alarm tests use `TEST-ALARM-<guid>` ids and delete all `SH-INT%` / `TEST-ALARM-%` rows before and after each test. This removes old test debris without introducing another database or schema.
- **Simpler command parsing.** The production command route now uses a nullable `switch` result instead of the `(OperatorCommandMessage.Command)(-1)` sentinel. Valid and invalid requests behave the same.

## Study first

1. `src/FleetManager/DashboardEndpoints.cs` — the one route mapping used by `Program.cs` and the tests; removing the publish or accepting an unknown kind now fails a test.
2. `src/FleetCore/TaskBoard.cs` `PauseAllExcept` / `ResumeAllExcept` — why an unowned queued task is neither paused nor hidden when one shuttle is paused.
3. `src/FleetManager/FleetServeLoop.cs` `EvaluateAndPublishAsync` — the narrow `try`/`catch` around the alarm save and why the publish happens before it.
4. `src/FleetManager/IAlarmRepository.cs` and `src/FleetManager.Tests/FleetServeLoopTests.cs` — the seam and the test that proves one database failure does not stop the remaining shuttles.
5. `src/FleetManager.Tests/TestDatabase.cs` `ResetAlarmsAsync` and `AlarmPersistenceTests` — how test rows are kept out of the operator's alarm list.

## Outside scope

- Review issue 5 (one alarm row per shuttle/code, so history and per-occurrence acknowledgement are lost) is unchanged.
- Review item 12's config check still accepts a lane list containing an empty string; only the length and refresh interval are validated.
- The dashboard's `Promise.all` refresh and stale-data behaviour are unchanged.
- Cross-process pause and verified removal of a simulated blocked lane remain unproven, as lesson 33 says.
- The `src/MqttMessaging/MqttMessageBus.cs` change is still outside the lesson's original file list.
- The premature README M7 verification paragraph remains; I did not edit the README.

## Stopped early

- None.

Owner sign-off: 2026-10-29, explained and verified
