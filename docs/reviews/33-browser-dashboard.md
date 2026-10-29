# Review: Lesson 33 — A real browser dashboard for the shuttle fleet

**Milestone:** M7 · **Reviewed:** 2026-10-29
**Verdict:** **not yet**

The browser page and the same-origin HTTP bridge are real and mostly work: the built Vue app is served from the Fleet Manager, the read routes return live fleet data, the command route rejects bad input, and an alarm raised by the serve loop is saved and can be acknowledged from the page. But the dashboard exposes a pause bug that stops every queued task and makes the task list look empty, the API tests do not exercise the production routes, and an alarm-database failure can stop the liveness loop. Those need fixing before this slice passes. M7 itself remains in progress: lesson 33 correctly says that blocked-lane removal and cross-process pause are still unproven.

---

## Acceptance criteria checklist

| # | Criterion | Result and evidence |
|---|---|---|
| 1 | Frontend builds | **Pass.** `cd src/Dashboard && npm ci && npm run build` → 11 modules transformed, assets written to `src/FleetManager/wwwroot/`. `npm audit` reports 1 high (`vite`) and 1 moderate (`esbuild`) dependency advisory. |
| 2 | Backend builds | **Pass.** `dotnet build ShuttleFleet.slnx --no-incremental -v m` → Build succeeded, 0 warnings, 0 errors. |
| 3 | Non-integration tests | **Pass.** `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → 236 passed, 0 failed (FleetManager 86, FleetCore 73, ShuttleCore 31, Messaging 10, ShuttleSim 10, MockWms 9, Contracts 8, TrafficCore 6, MqttMessaging 3). |
| 4 | Integration tests | **Pass.** With broker and database up: `dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"` → 6 passed, 0 failed in FleetManager.Tests. |
| 5 | Docker build and test | **Pass.** `docker build -f docker/Dockerfile.build -t shuttle-fleet-build-review .` succeeded; `docker run --rm shuttle-fleet-build-review` → every test project passed, FleetManager.Tests 86 passed, 0 failed. |
| 6 | `GET /` serves the built page | **Pass.** Live `dotnet ... FleetManager.dll serve` + `curl http://127.0.0.1:5080/` → `200 text/html`, title `Shuttle Fleet Dashboard`; the referenced `/assets/index-D0-1nZn9.js` also returned `200 text/javascript`. |
| 7 | `GET /api/dashboard/config` | **Pass.** Live response: `{"laneIds":["A01-L01","A01-L02","A01-L03","A01-L04"],"refreshIntervalMs":2000}`. |
| 8 | `GET /api/dashboard/shuttles` | **Pass.** After starting `ShuttleSim`: `SH-01` appeared with `state:"idle"`, `position:"A01-L01"`; after `Pause` the `pauseRequested` flag became `true`, and after `Resume` it became `false`. |
| 9 | `GET /api/dashboard/tasks` | **Partial / fails while paused.** Initially it returned `task-hold`; after issuing `Pause` for `SH-01` it returned `[]`; after `Resume` the task reappeared. The task is not gone, but the dashboard hides it and the pause stopped dispatch for every task. See blocker 1. |
| 10 | `GET /api/dashboard/alarms` and alarm persistence | **Pass for the path, with data-quality caveat.** After killing the simulator and waiting past the liveness timeout, an alarm for `SH-01`/`lostCommunication` was saved and returned. Sending `Acknowledge` set `acknowledgedAt`. The route also returned `SH-INT` and `SH-INT2`, which are fake rows left by the integration tests. See issue 4. |
| 11 | `POST /api/dashboard/operator-command` validation | **Pass.** Valid `Pause` returned `200`; `kind:"Dance"` returned `400` with `"kind must be Pause, Resume, or Acknowledge."`; a blank `shuttleId` returned `400` with `"shuttleId and kind are required."` No invalid command was published. |
| 12 | R3 configuration | **Pass by inspection, untested.** `appsettings.json` has `Dashboard:LaneIds` and `Dashboard:RefreshIntervalMs: 2000`; `Program.cs` rejects an empty lane array and a non-positive interval. No test exercises that validation, and a list containing `""` would pass the length check. |
| 13 | R4 same-origin / no CORS / no browser MQTT | **Pass.** The Vue app uses relative `/api/...` URLs and `/assets/...`; no CORS policy was added; the browser bundle contains no MQTT client. |
| 14 | R5 contract and existing behavior | **Pass.** `ContractVersion.Current` remains `4`; `git diff 6620ac1..accc7b8 -- src/Contracts docs/contract docker/compose.yml` is empty. |
| 15 | Mutation check (a): remove the operator publish from the REST route | **Fail.** In a scratch copy I removed the publish from `Program.cs`; `dotnet test ... --filter "FullyQualifiedName~DashboardIntegrationTests"` still passed 6/6. The tests publish from their own copy of the route. |
| 16 | Mutation check (b): accept an unknown command kind | **Fail by the same cause.** The tests re-declare the `kind` switch, so changing the production switch would not fail them. |
| 17 | Mutation checks (c) and (d): missing `index.html` / literal lane list in Vue | **Not automated.** Both were verified only by reading the code and the live page; no test would turn red. |
| 18 | Mutation check (e): remove the alarm save call | **Not re-run.** The integration test constructs `FleetServeLoop` directly and should catch it, but the other dashboard tests do not exercise the actual serve composition. |
| 19 | Owner sign-off | **Present.** `docs/builds/33-browser-dashboard-report.md` ends with `Owner sign-off: 2026-10-29, explained and verified`. |

---

## Top issues (max 5), ranked

### 1. [blocker] Pausing one shuttle stops every queued task and makes the task queue look empty

Evidence, from the live Fleet Manager:

```text
GET /api/dashboard/tasks before Pause  -> [{"taskId":"task-hold","state":0,...}]
POST Pause {"shuttleId":"SH-01"}        -> 200
GET /api/dashboard/tasks while paused   -> []
POST Resume {"shuttleId":"SH-01"}       -> 200
GET /api/dashboard/tasks after Resume   -> [{"taskId":"task-hold","state":0,...}]
```

Cause: `HandleOperatorCommandAsync` passes a shuttle id to `taskBoard.PauseAllExcept(cmd.ShuttleId)` (`Program.cs` lines 371-372), but `TaskBoard.PauseAllExcept` compares that value to `t.TaskId`, not to the task's assigned shuttle (`src/FleetCore/TaskBoard.cs` lines 32-39). No task id equals `SH-01`, so every task is added to `_pausedTaskIds`. `DashboardRoutes.Tasks` then reads `taskBoard.Queued()` (`Program.cs` lines 335-336), and `Queued()` deliberately hides paused tasks (`TaskBoard.cs` lines 27-28).

Why it matters: in a real fleet, an operator pausing one shuttle would silently stop dispatch for all other shuttles, and the task queue would appear to lose work. That destroys operator trust and can strand pallets. It also means R2's "current queued tasks" is wrong during a pause. This is a cross-lesson defect (lessons 26/30 and records 38/39 describe `PauseAllExcept(shuttleId)` as "pause all except the named shuttle"), so the professor should settle whether pause is per-shuttle dispatch, per-task, or global; the dashboard should then show paused tasks as paused rather than remove them.

### 2. [blocker] The dashboard API tests re-implement the routes instead of testing the production app

`DashboardIntegrationTests.BuildApp` (`src/FleetManager.Tests/DashboardIntegrationTests.cs` lines 15-63) creates a fresh `WebApplication` and re-declares the config, shuttles, tasks, and operator-command handlers. It never calls the route mapping in `Program.cs`.

I proved the consequence in a scratch copy: removing the `bus.PublishAsync(...)` call from the production command route left all six `DashboardIntegrationTests` green. Mutation checks (a) and (b) therefore do not go red. A future edit can break the real dashboard route and the suite will still pass, so the tests give false confidence exactly where operator commands matter. The test should build the actual app (for example `WebApplicationFactory<Program>` or a shared route-mapping method used by both `Program` and the test), with a fake bus/repository injected at the seam.

### 3. [should-fix] An alarm-database failure can stop the liveness/alarm loop

`FleetServeLoop.EvaluateAndPublishAsync` awaits `_alarmRepository.SaveAsync(...)` inside the per-shuttle evaluation loop with no `try`/`catch` (`FleetServeLoop.cs` lines 100-109). `RunAsync` does not catch either. `Program.cs` starts `loopTask` and only awaits it during shutdown, where it logs "the serve loop stopped unexpectedly."

Why it matters: if PostgreSQL is down, slow, or rejects the DDL at the moment an alarm is raised, the entire liveness/alarm loop stops while the Fleet Manager still looks alive to the operator. A stuck shuttle could then go undetected. The publish happens before the save, so the first alarm may escape, but subsequent evaluation stops. Save failures need to be isolated (log, retry/outbox, keep evaluating) and covered by a test with a failing repository.

### 4. [should-fix] Integration tests leave fake alarms in the shared database, and the dashboard shows them

The live alarm route returned these rows before any simulator fault of its own:

```json
[
  {"shuttleId":"SH-INT","severity":"critical","code":"lostCommunication",...},
  {"shuttleId":"SH-INT2","severity":"critical","code":"lostCommunication",...}
]
```

`AlarmPersistenceTests` inserts `SH-INT` and `SH-INT2` and never deletes them. Because the integration tests use the same database as the operator workflow, the dashboard presents phantom shuttles and alarms. In a real system this is a trust and support problem: the operator cannot tell test debris from a live fault. Use an isolated test database/schema or clean up the rows after each test, and make the test IDs obviously non-production.

### 5. [consider] The alarm table stores one row per shuttle/code, so history is lost and re-raising clears acknowledgement

The table has `PRIMARY KEY (shuttle_id, code)` and `SaveAsync` upserts with `acknowledged_at = @acknowledgedAt` (`PostgresAlarmRepository.cs` lines 7-16 and 41-45). The serve loop always passes `null` for `AcknowledgedAt` (`FleetServeLoop.cs` line 106). A second occurrence of the same fault therefore overwrites the previous row, moves `raised_at` forward, and can quietly clear a previous acknowledgement.

For today's single current-state list this is tolerable, but M7's diagnostics/health-history goal and any audit use will need occurrence history. Decide now whether alarms are current state or events; if events, use a per-occurrence key and keep acknowledgement attached to the occurrence.

---

## What is solid

- The Vue build is genuinely wired into the deployment: the Docker Node stage builds it and `COPY --from=frontend` puts it where the .NET app serves it. I verified `GET /` and its JavaScript asset live.
- `ShuttleRegistry.Snapshot()` copies the dictionary under the existing lock and returns an ordered list, so a status update cannot mutate the list while the route enumerates it.
- The command route maps the string `kind` explicitly, rejects unknown values and blank shuttle ids with `400`, publishes on the existing operator topic, and returns a body that says "accepted for publishing", not "completed".
- The alarm save path works end to end: a real liveness alarm was saved, returned by the dashboard route, and updated by `Acknowledge`.
- The page clearly labels the Fleet Manager's pause as a "pause requested" and shows loading, sending, and error states.
- Contract version 4, contract documents, and Docker service/port configuration are unchanged. The operator workflow still goes through the existing topic and handler.

---

## Explain-it check

The student should be able to answer these out loud before the next lesson. No answers are given here.

1. `DashboardIntegrationTests.BuildApp` creates its own copies of the dashboard routes. Why does that make the tests less trustworthy, and what would you change so that removing the publish call from the real `Program.cs` route makes a test fail?
2. When the dashboard sends `Pause` for `SH-01`, why does the handler call both `taskBoard.PauseAllExcept(...)` and `pauseState.Pause("SH-01")`? What actually happens to a queued task assigned to `SH-02`, and what does the operator see in the task list?
3. In `FleetServeLoop.EvaluateAndPublishAsync`, why is `SaveAsync` awaited directly inside the loop? If PostgreSQL is unavailable at that moment, what happens to alarm evaluation for every shuttle, and how would you make the save failure harmless to the liveness loop?

### Blocks that look unclear, copied, or unexplained

- `Program.cs` lines 344-352: using `(OperatorCommandMessage.Command)(-1)` as a sentinel and testing `(int)kind < 0`. It works, but an interviewer will ask why not `Enum.TryParse` or a switch that returns a nullable value. The student should be able to defend or simplify it.
- `Program.cs` line 372 plus `TaskBoard.cs` lines 32-39: the parameter named `shuttleId` is compared with `TaskId`. This looks like a copy of an idea from lesson 26 that no longer matches its name or documented behavior.
- `PostgresAlarmRepository.cs` lines 29-33 and every public method: `CREATE TABLE IF NOT EXISTS` runs on every read and write, including every two-second dashboard poll. Explain why, and whether that belongs in startup/migration instead.
- `FleetServeLoop.cs` lines 20-36: the optional concrete `PostgresAlarmRepository` constructor parameter. Why does the loop depend on a concrete repository, and why is the parameter optional?
- `MqttMessaging/MqttMessageBus.cs` line 104: the new `MessageFamily.Json` mapping is outside the lesson's file list. The build report says `serve` could not start without it; the student should be able to explain the throw in `ToMqttQos` and why the operator subscription needs this family.
- `src/Dashboard/src/App.vue` `refresh()`: `Promise.all` means one failing endpoint prevents shuttle, task, and alarm data from all updating, while old values remain on screen. Explain what the operator sees and whether stale data needs an age/freshness marker.
- `src/FleetManager/FleetManager.csproj` and `docker/Dockerfile.build`: explain the path from Vite's `outDir` to `wwwroot` in the build output and why `UseDefaultFiles()` must come before `UseStaticFiles()`.

---

## Revisit list

- **Test seams and the composition root.** Testing a copy of the routes is not testing the app. Study `WebApplicationFactory<Program>` or a shared `MapDashboardRoutes` function, and how to inject a fake bus and repository at the seam.
- **Pause semantics.** Re-read `TaskBoard`, `PauseState`, `RunLoopGate`, and the operator handler. Decide what "pause one shuttle" means for dispatch, movement, and queued tasks; the current code mixes task ids and shuttle ids.
- **Failure isolation around database I/O in loops.** Study why the task execution loop catches per-tick exceptions and the serve loop does not after this change; apply the same pattern or an outbox/retry.
- **Alarm data modeling and migration.** Current-state row versus event history, keys, acknowledgement per occurrence, and where `CREATE TABLE` belongs.
- **Request cancellation and timeouts.** The dashboard routes pass `CancellationToken.None`; study what happens when the browser disconnects or PostgreSQL hangs.
- **Static files and Docker.** The multi-stage Node-to-.NET copy, `.dockerignore` (currently `node_modules` is not excluded from the Docker context), and serving `index.html` from the app's content root.
- **Frontend dependency audit.** The lockfile pulls a Vite/esbuild advisory; decide whether to upgrade Vite (and re-verify the build) or record why the development-server advisory is acceptable for a local dashboard.

---

## Process and lesson notes

- **README was changed before review.** The current README contains an M7 verification paragraph that says lesson 33's review is "to be recorded" and repeats the build report's claims. Per `AGENTS.md`, the implementer should not mark milestone state or add reviewer verification notes; only the reviewer updates the milestone table, and only after a passing review. I left README unchanged because this review is not a pass. The owner should remove or correct that premature paragraph.
- **The lesson's file list is missing `src/MqttMessaging/MqttMessageBus.cs`.** The operator subscription uses `MessageFamily.Json`, and without a branch in `ToMqttQos` the `serve` command throws while subscribing. The implementer found this and documented it in the build report, but the next version of the lesson should include that file or explicitly state the required mapping. This is a lesson defect, not an implementer failure.
- **The `PauseAllExcept(shuttleId)` contract is wrong in the lessons and decisions.** Lessons 26 and 30, record 38, and record 39 all describe it as "pause all except the named shuttle", but the implementation compares the argument to `TaskId`. The professor should correct this with an explicit decision before asking for per-shuttle pause behavior; otherwise the student is being asked to make a dashboard control work against a method that cannot express the requirement.
- **M7's done-when is not met and should not be marked done.** Lesson 33 itself says the blocked-lane removal and cross-process pause are unverified. Review 32's claim that the full dashboard-only scenario was already proven is inconsistent with the fact that no browser dashboard existed at that point; the M7 record should stay "in progress".
- **Concept limit is respected.** The lesson lists exactly three new concepts (browser client/static build, HTTP boundary, polling/operator feedback) and teaches all three. Alarm persistence is presented as the data source for the HTTP boundary rather than a fourth concept; that is acceptable, though the table-creation and save-path work could have been called out in the file list more clearly.

---

## End-product check

This slice adds the missing browser surface and an HTTP boundary that keeps MQTT out of the browser, and it does so without changing contract version 4, the WMS routes, or the broker configuration. It fits the M7 spec structurally. The risk is in behavior, not architecture: the pause control currently stops every queued task and hides the queue, the dashboard can show stale fleet data while one read fails, and the new alarm save can silence the very liveness loop that detects stuck shuttles. Those are operator-trust and safety problems in a real shuttle system. M7 stays in progress until the pause semantics are settled, the tests exercise the real routes, and alarm persistence is isolated from the liveness loop; the full blocked-lane, dashboard-alone recovery scenario still needs its own verified run.
