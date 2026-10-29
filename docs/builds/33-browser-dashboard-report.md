# Build report 33: browser dashboard

## Summary

Built the Vue/Vite browser dashboard and its Fleet Manager REST bridge. Added
dashboard configuration, a lock-protected `ShuttleRegistry.Snapshot()`, alarm
table creation plus save path in `FleetServeLoop`, static-file serving, and the
five `/api/dashboard/*` routes. Wired the Vue build into Docker via a Node stage.
Verified backend build/tests, frontend build, Docker build/test, and a live
`GET /` returning the built page.

## Files

- `src/Dashboard/` — new Vue/Vite app (`package.json`, `package-lock.json`, `vite.config.js`, `index.html`, `src/main.js`, `src/App.vue`, `README.md`).
- `src/FleetManager/Program.cs` — dashboard config validation, static files, dashboard routes, alarm repository passed to serve loop.
- `src/FleetManager/ShuttleRegistry.cs` — added lock-protected `Snapshot()`.
- `src/FleetManager/FleetServeLoop.cs` — optional `PostgresAlarmRepository`, saves each raised alarm.
- `src/FleetManager/PostgresAlarmRepository.cs` — creates `alarms` table; `EnsureTableAsync` in all methods.
- `src/FleetManager/appsettings.json` — added `Dashboard:LaneIds` and `Dashboard:RefreshIntervalMs`.
- `src/FleetManager/DashboardRoutes.cs` — route constants.
- `src/FleetManager/DashboardOperatorCommandRequest.cs` — request DTO.
- `src/FleetManager/FleetManager.csproj` — copy generated `wwwroot` to output.
- `docker/Dockerfile.build` — Node 22 stage builds frontend before .NET build.
- `.gitignore` — ignore `src/Dashboard/node_modules/` and `src/FleetManager/wwwroot/`.
- `src/FleetManager.Tests/DashboardIntegrationTests.cs` — dashboard API, validation, command publishing tests.
- `src/FleetManager.Tests/AlarmPersistenceTests.cs` — alarm table creation and persistence tests (integration).
- `src/MqttMessaging/MqttMessageBus.cs` — map `MessageFamily.Json` to command QoS (needed for operator topic).
- `docs/builds/33-browser-dashboard-report.md` — this report.

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

Container:

```bash
docker build -f docker/Dockerfile.build -t shuttle-fleet-build .
docker run --rm shuttle-fleet-build
```

## Verification

- `cd src/Dashboard && npm ci && npm run build` → succeeded, 11 modules transformed, assets written to `../FleetManager/wwwroot`.
- `cd src && dotnet build ShuttleFleet.slnx` → Build succeeded, 0 warnings, 0 errors.
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → 86 passed in FleetManager.Tests (plus other projects), 0 failed.
- `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` → succeeded.
- `docker run --rm shuttle-fleet-build` → 86 passed in FleetManager.Tests, 0 failed.
- Integration tests with broker/database up → 6 passed in FleetManager.Tests.
- Live check: `dotnet run --project src/FleetManager -- serve` then `curl http://127.0.0.1:5080/` → 200 with built `index.html`.

## Choices made

- `FleetServeLoop` keeps its existing constructor signature by adding an
  optional `PostgresAlarmRepository?` parameter, so existing tests still compile.
- `ShuttleRegistry.Snapshot()` copies the dictionary under the lock and returns
  an ordered list, matching `PauseState.Snapshot()`.
- Command kind is matched case-sensitively against exactly `Pause`, `Resume`,
  `Acknowledge`.
- Added `MessageFamily.Json` to `MqttMessageBus.ToMqttQos` because the operator
  topic is published/subscribed as `MessageFamily.Json`; without it `serve`
  could not start. This is outside the assignment's file list but required for
  the route to function.

## Study first

1. `src/FleetManager/Program.cs` dashboard routes — how the REST bridge reads
   registry/tasks/alarms and publishes `OperatorCommandMessage`.
2. `src/FleetManager/ShuttleRegistry.cs` `Snapshot()` — lock and copy.
3. `src/FleetManager/FleetServeLoop.cs` — alarm save path in
   `EvaluateAndPublishAsync`.

## Outside scope

- Cross-process pause and verified removal of a blocked lane remain unverified,
  as stated in the lesson.
- `src/MqttMessaging/MqttMessageBus.cs` change noted above; could be split into
  its own decision if reviewers prefer.

## Stopped early

- None.

Owner sign-off: 2026-10-29, explained and verified
