# Review: Lesson 33 — A real browser dashboard for the shuttle fleet (re-review)

**Milestone:** M7 · **Reviewed:** 2026-10-29 (rev2)
**Verdict:** **pass**

The four blockers from the first review are fixed and verified. Pausing one shuttle no longer empties the queued-task list; the dashboard API tests now exercise the shared production route mapping (mutation checks (a) and (b) go red); an alarm-database save failure is logged and skipped so liveness evaluation continues; and the integration alarm tests use unique ids and clean up their rows (the shared database shows 0 alarm rows after the run). Build, non-integration tests (242), integration tests (6), and the Docker build/test all pass. M7 itself stays in progress: its done-when (a full scenario, including an injected fault, run and recovered from the dashboard alone) is still unverified, and lesson 33 explicitly says the blocked-lane removal and cross-process pause are not yet proven.

---

## Acceptance criteria checklist

| # | Criterion | Result and evidence |
|---|---|---|
| 1 | Frontend builds | **Pass.** `cd src/Dashboard && npm ci && npm run build` → 11 modules transformed; assets written to `src/FleetManager/wwwroot/`. |
| 2 | Backend builds | **Pass.** `cd src && dotnet build ShuttleFleet.slnx -v m` → Build succeeded, 0 warnings, 0 errors. |
| 3 | Non-integration tests | **Pass.** `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → 242 passed, 0 failed (FleetManager 88, FleetCore 77, ShuttleCore 31, TrafficCore 6, Contracts 8, Messaging 10, MqttMessaging 3, MockWms 9, ShuttleSim 10). |
| 4 | Integration tests | **Pass.** With broker and database up: `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"` → 6 passed, 0 failed in FleetManager.Tests. |
| 5 | Docker build and test | **Pass.** `docker build -f docker/Dockerfile.build -t shuttle-fleet-build-rev2 .` succeeded; `docker run --rm shuttle-fleet-build-rev2` → all test projects passed, FleetManager.Tests 88 passed, 0 failed. |
| 6 | `GET /` serves the built page | **Pass by prior live check** (review 33 criterion 6); no code change to static-file handling in this pass. |
| 7 | `GET /api/dashboard/config`, `/shuttles`, `/tasks`, `/alarms`, `POST /api/dashboard/operator-command` | **Pass by shared route mapping.** `DashboardIntegrationTests` now call `DashboardEndpoints.MapDashboardRoutes`, the same mapping `Program.cs` calls; all 7 dashboard tests pass. |
| 8 | R3 configuration validation | **Pass by inspection.** `Program.cs` rejects an empty lane array and a non-positive interval. Empty-string lane ids still pass the length check (see issue 2). |
| 9 | R4 same-origin / no CORS / no browser MQTT | **Pass.** Unchanged from review 33. |
| 10 | R5 contract and existing behavior | **Pass.** `ContractVersion.Current` remains `4`; no contract or WMS route changes. |
| 11 | Mutation check (a): remove the operator publish | **Pass (now red).** In a scratch copy, removing `bus.PublishAsync` from `DashboardEndpoints.MapDashboardRoutes` made `Operator_command_valid_publishes_on_operator_topic` fail (1 failed, 6 passed). |
| 12 | Mutation check (b): accept an unknown command kind | **Pass (now red).** Changing the unknown-kind branch to accept `Pause` made `Operator_command_invalid_kind_is_rejected_and_not_published` fail (2 failed, 5 passed). |
| 13 | Mutation check (e): remove the alarm save call | **Pass (now red).** In a scratch copy, disabling the `SaveAsync` call made `FleetServeLoop_saves_raised_alarm_through_repository` fail (1 failed, 0 passed). |
| 14 | Alarm test cleanup | **Pass.** `docker compose ... psql ... SELECT ... FROM alarms` → `(0 rows)` after the integration run; test debris (`SH-INT`, `SH-INT2`, `TEST-ALARM-*`) is gone. |
| 15 | Owner sign-off | **Present.** `docs/builds/33-browser-dashboard-report.md` ends with `Owner sign-off: 2026-10-29, explained and verified`. |

---

## Top issues (max 5), ranked

### 1. [should-fix] `PauseAllExcept` semantics changed without a decision record
The fix reinterprets `PauseAllExcept(shuttleId)` so that it pauses only tasks already assigned to a *different* shuttle, leaves unassigned queued tasks dispatchable, and leaves work owned by the named shuttle running (`src/FleetCore/TaskBoard.cs` lines 32-52). That is a defensible reading of the name and it fixes the empty-task-list bug, but it contradicts lesson 26's sentence "stops dispatching new work to the named shuttle while letting others continue", and lessons 26/30 plus records 38/39 describe the method as "pause all except the named shuttle". The professor should settle the intended meaning (per-shuttle dispatch, per-task, or global) and record it; otherwise the next lesson can re-introduce the mismatch. Why it matters: an operator who pauses one shuttle could still see its unassigned queued work dispatched to that shuttle, or could believe other shuttles' work is paused when it is not — a trust problem in a real fleet.

### 2. [should-fix] Dashboard lane configuration still accepts an empty string
`Program.cs` validates that `Dashboard:LaneIds` is non-empty and that the interval is positive, but a list such as `["A01-L01", ""]` passes (`Program.cs` lines 153-158). `DashboardEndpoints.MapDashboardRoutes` then hands that empty string to the Vue map. Why it matters: a typo in config would render a phantom lane and could confuse an operator's rack view; the validation should reject blank entries (or the test should cover it).

### 3. [consider] `IAlarmRepository` depends on the concrete repository's nested record
`IAlarmRepository` declares `SaveAsync(PostgresAlarmRepository.Alarm, ...)` and `LoadAllAsync() ... PostgresAlarmRepository.Alarm` (`src/FleetManager/IAlarmRepository.cs`). The seam works and the failing-repository test proves it, but the interface now references a concrete class in the same project, so a second implementation would have to reuse `PostgresAlarmRepository.Alarm`. Why it matters: small, but it makes the seam less reusable and is the kind of thing a support engineer replacing the alarm store would trip over. Defining an interface-owned `Alarm` record would remove the coupling.

### 4. [consider] `CREATE TABLE IF NOT EXISTS` runs on every read and write
`PostgresAlarmRepository` calls `EnsureTableAsync` in every public method, including every two-second dashboard poll (`PostgresAlarmRepository.cs` lines 29-33 and each method). Why it matters: harmless today, but it puts DDL on the hot path and couples table existence to request traffic; a migration or startup step is the better home for it.

### 5. [consider] Alarms are still current state, not history
One row per `(shuttle_id, code)`, upserted, with the serve loop always passing `null` for `AcknowledgedAt` (`PostgresAlarmRepository.cs` lines 41-45; `FleetServeLoop.cs` line 108). A second occurrence overwrites the row and can clear a prior acknowledgement. Why it matters: fine for today's single current-state list, but M7's diagnostics/health-history goal will need per-occurrence keys and acknowledgement attached to the occurrence.

---

## What is solid

- The shared route mapping is now the single source of truth: `Program.cs` and `DashboardIntegrationTests` both call `DashboardEndpoints.MapDashboardRoutes`, so mutation checks (a) and (b) now go red. The tests are 7 strong, readable cases.
- `FleetServeLoop.EvaluateAndPublishAsync` now wraps only `SaveAsync` in a `try`/`catch` that rethrows cancellation and logs other failures, so one database error no longer silences the liveness loop for every shuttle; `An_alarm_save_failure_does_not_stop_evaluation_for_other_shuttles` proves it.
- `PostgresAlarmRepository` now implements `IAlarmRepository`, and the fake in the tests implements the same interface, so the serve loop and routes can be tested without Postgres.
- Alarm integration tests use `TEST-ALARM-{guid}` ids and `ResetAlarmsAsync` before and after each test; the shared database is clean (`0 rows`) after the run, so operators no longer see phantom test alarms.
- `TaskBoardPauseTests` (4 tests) pin the new pause/resume behavior, including blank-id pause-all and resume-restores-paused-work.
- Owner sign-off is present and dated, and the report's verification commands match the re-run results.

---

## Explain-it check

The student should be able to answer these out loud before the next lesson. No answers are given here.

1. `DashboardEndpoints.MapDashboardRoutes` now takes `IMessageBus`, `TopicScheme`, `ShuttleRegistry`, `TaskBoard`, `PauseState`, `IAlarmRepository`, lane ids, and the refresh interval. Why is that signature the seam that makes mutation checks (a) and (b) meaningful, instead of the test re-declaring the routes?
2. In `TaskBoard.PauseAllExcept`, an unassigned queued task is neither paused nor hidden when one shuttle is paused. What does the operator see in the task list during a pause, and why did the original code make the list look empty?
3. In `FleetServeLoop.EvaluateAndPublishAsync`, `PublishAlarmAsync` is awaited before `SaveAsync`, and `SaveAsync` is wrapped in a `try`/`catch` that only rethrows cancellation. If PostgreSQL is unavailable, what happens to the alarm that was just published, to evaluation of the remaining shuttles, and to the liveness loop's next tick?

### Blocks that look unclear, copied, or unexplained

- `Program.cs` `HandleOperatorCommandAsync` (lines 326-358): it calls both `taskBoard.PauseAllExcept`/`ResumeAllExcept` and `pauseState.Pause`/`Resume`. The student should be able to explain why both sets are driven together and what drifts if one is skipped.
- `src/FleetCore/TaskBoard.cs` lines 44-52 and 62-69: the pause/resume predicates compare `AssignedShuttleId` to the shuttle id, while the set stores `TaskId`s. Explain the two different key spaces and why a requeue clears the mark only on resume.
- `src/FleetManager/IAlarmRepository.cs` plus `PostgresAlarmRepository.cs`: explain why the interface references the concrete repository's nested `Alarm` record and what would change if a second implementation were added.
- `src/FleetManager.Tests/TestDatabase.cs` `ResetAlarmsAsync`: it deletes only rows whose `shuttle_id` matches `SH-INT%` or `TEST-ALARM-%`. Explain why the cleanup is pattern-based and what happens to a real `SH-01` alarm left by a manual run.
- `src/FleetManager.Tests/DashboardIntegrationTests.cs` `RecordingAlarmRepository`: its `AcknowledgeAsync` mutates the in-memory list in place. Explain why that is enough for the route test and where it diverges from Postgres behavior.

---

## Revisit list

- **Pause semantics and decision records.** Re-read lesson 26, record 38, and record 39 against the new `TaskBoard` implementation; settle and record the intended meaning of `PauseAllExcept(shuttleId)` before the next pause-related change.
- **Configuration validation depth.** Study how to reject blank entries in a list (and add a test), not just an empty list.
- **Seams and concrete types.** Study how an interface can own its own data record so implementations do not depend on each other.
- **DDL and migrations.** Study where table creation belongs (startup/migration) versus per-request code.
- **Alarm modeling.** Current-state row versus event history, per-occurrence keys, and acknowledgement attached to the occurrence.
- **Request cancellation and timeouts.** The dashboard routes still pass `CancellationToken.None`-equivalent tokens; study what happens when the browser disconnects or PostgreSQL hangs.
- **Static files and Docker.** The multi-stage Node-to-.NET copy, `.dockerignore`, and `UseDefaultFiles()` before `UseStaticFiles()`.
- **Frontend dependency audit.** The lockfile still pulls a Vite/esbuild advisory; decide upgrade or record acceptance.

---

## End-product check

This slice completes the browser surface and its HTTP bridge for M7 without changing contract version 4, the WMS routes, or the broker configuration. The fixes make the dashboard trustworthy in the ways the first review demanded: a pause no longer hides the task queue, the API tests now guard the real routes, and a database failure can no longer silence the liveness loop. The remaining risks are smaller and mostly about precision and auditability — pause semantics that still need an explicit decision record, config validation that lets a blank lane through, and alarms modeled as current state rather than history. M7 as a milestone stays in progress: its done-when requires a full scenario, including an injected fault, to be run and recovered from the dashboard alone, and that end-to-end run (blocked-lane removal and cross-process pause included) is still unproven. No README milestone-table change is made here; M7 remains `in progress`.
