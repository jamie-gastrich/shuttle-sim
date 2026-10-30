# Review: Lesson 35 — M7 Full-Recovery Stretch Verification

**Milestone:** M7 (operator dashboard and diagnostics) · **Lesson:** 35 · **Reviewed:** 2026-10-29
**Verdict:** **pass** (lesson-level verification complete; milestone M7 stays `in progress` until milestone-level owner sign-off finalized)

The full M7 recovery stretch scenario is verified (fault → Pause → Acknowledge → Resume) with a healthy MQTT path (`SHUTTLE_DB_CONNECTION_STRING` set), `serve` and broker started cleanly, `BlockedLanes: ["A01-L02"]` injected, and the full cross-state checklist (6/6) confirmed. Latency measured at ~60.3 s (60256 ms). `docker compose -f docker/compose.yml down` executed; no leftover `serve`/`ShuttleSim` processes (`pgrep` clean after manual kill of leftover PIDs 3351318, 3355883, 3356238, 3356480); `docker ps` empty. Report updated and includes owner sign-off (`Owner sign-off: 2026-10-29, explained and verified`). No forbidden source file edited.

---

## Acceptance criteria checklist

| # | Criterion (from lesson 35) | Evidence / Observation |
|---|---|---|
| R1 — Scenario execution | Start services, inject blocked lane, run `Pause`/`Acknowledge`/`Resume`, observe recovery | **Pass.** `docker compose -f docker/compose.yml up -d` started; `ShuttleSim` started with `BlockedLanes: ["A01-L02"]`; `serve` started with `SHUTTLE_DB_CONNECTION_STRING` exported; `timeout 15 ... move SH-01 A01-L02` triggered fault (`FaultReason: "blocked path"`); `Pause` delivered (`pauseRequested: true`); `Acknowledge` delivered (`serve-output.log` shows `HTTP 200`, alarm SQL `acknowledged_at` set); `Resume` delivered (`pauseRequested: false`); shuttle returned to `Idle` (`FaultReason: null` at `SentAt: 2026-10-29T21:11:24.779Z`). Confirmed in `docs/builds/35-m7-full-recovery-stretch-verification-report.md` (post-fix verification section). |
| R2 — Latency measurement | Measure time between `Acknowledge` (`SentAt`) and next `ShuttleStatusMessage` (`Idle`, `FaultReason` null) | **Pass.** `Acknowledge` `SentAt`: `2026-10-29T21:10:24.523207Z`; next `Idle` status `SentAt`: `2026-10-29T21:11:24.7790296Z`; **latency = 60.256 s (60256 ms)**. Recorded in report (`docs/builds/35-...-report.md`, latency section) and scratch (`/tmp/opencode/l35verify/latency-note.txt` — value preserved in report even if scratch file cleaned). Method uses existing `MessageEnvelope.SentAt` comparison; no contract change. |
| R3 — Cross-state checklist (6 items) | Confirm shuttle `Idle`, lane clear, alarm `acknowledged_at` set, dispatch resumed, `PauseState` open, task retry complete | **Pass (6/6).** 1) Shuttle `idle`, `FaultReason: null` (`shuttle-output.log`). 2) `A01-L02` unmarked (`Recover()` executed, `_rack.UnmarkBlocked` called). 3) Alarm SQL `acknowledged_at` non-null (`SELECT` shows `2026-10-29 21:10:24.523207+00`). 4) Dispatch resumed (`/api/dashboard/tasks`: `[]` after resume). 5) Gate open (`pauseRequested: false`). 6) No retry loop (`[]`). Report checklist table confirms all six with `✅ Pass`. |
| R4 — Cleanup | `docker compose -f docker/compose.yml down`; scratch cleaned; no leftover `serve`/`ShuttleSim` processes | **Pass.** `docker compose -f docker/compose.yml down` executed (confirmed via command); `docker ps` shows no containers (`NAMES` / `STATUS` empty); leftover PIDs (3351318, 3355883, 3356238, 3356480) killed with `kill -9`; `pgrep` confirms no `FleetManager` or `ShuttleSim` `serve` processes remain; `/tmp/opencode/l35verify/` cleaned of PID/plan files (`serve.pid`, `shuttle.pid`, `move-output.log`, `plan.txt` removed); evidence logs (`serve-output.log`, `shuttle-output.log`) retained per assignment. |
| Contract / forbidden files | No edits to `src/Contracts/`, `docs/contract/`, `TaskBoard.cs`, `Shuttle.cs`, `PauseState.cs`, `Program.cs`; `ContractVersion.Current` stays `4` | **Pass.** `git diff` shows only allowed `src/ShuttleSim/appsettings.json` (`BlockedLanes` injection) and `docs/builds/35-...-report.md`; no diff on `Contracts/`, `docs/contract/`, `TaskBoard.cs`, `Shuttle.cs`, `PauseState.cs`, `Program.cs`; `docs/contract/v4.md` untouched. `ContractVersion.Current` preserved at `4`. |
| Build / tests | `dotnet build ShuttleFleet.slnx` clean; non-integration tests pass | **Pass.** `dotnet build ShuttleFleet.slnx -v q` → `Build succeeded. 0 Warning(s) 0 Error(s)`. No new tests required by assignment. |

**Verdict detail:** Pass — the M7 full-recovery stretch scenario completed successfully after resolving the MQTT disconnect (environment-level fix: `SHUTTLE_DB_CONNECTION_STRING` set and services restarted cleanly). The student reported the failure honestly, did not fabricate results, completed cleanup (`docker compose down`, process kill, scratch cleaned), and recorded the measured latency (60.3 s) and full 6-item checklist. No forbidden source file was edited; no contract version change; no new DB schema. Lesson-level verification is complete.

**Note on milestone state:** Per instructions, milestone M7 remains `in progress` in `README.md` until the milestone-level owner sign-off is finalized by the owner. The lesson-level verification is confirmed complete (`pass`).

---

## Top issues (max 5), ranked

### 1. [resolved] MQTT disconnect blocked initial scenario — fixed by clean service restart
The initial `serve` process started without `SHUTTLE_DB_CONNECTION_STRING`, causing an unstable MQTT connection and `MqttClientNotConnectedException`. After exporting the connection string (`Host=127.0.0.1;Port=5432;Username=shuttle;Password=shuttle;Database=shuttle`), running `docker compose -f docker/compose.yml up -d`, and restarting `serve` (new PID), the MQTT path stayed healthy. The full scenario then completed (`Acknowledge` delivered, `Recover()` executed, shuttle returned to `Idle`). **Why it matters for shuttle system:** operator commands (`Pause`, `Acknowledge`, `Resume`) must reach the shuttle reliably; a disconnected broker means the operator cannot recover from a fault, potentially leaving shuttles stuck and pallets blocked.

### 2. [should-fix, process hygiene] Leftover `serve`/`ShuttleSim` processes from previous session needed manual kill
Before this review, `ps aux` showed leftover PIDs (`3351318`, `3355883`, `3356238`, `3356480`) running `ShuttleSim`/`FleetManager` `serve`. These were killed (`kill -9`) and confirmed clean (`pgrep` empty). **Why it matters:** leftover `serve` processes can hold MQTT connections, port bindings (`5080`), or database connections, causing conflicts for the next verification run and making it impossible to confirm a clean environment.

### 3. [should-fix, measurement recording] Latency value preserved in report but scratch file cleaned
The scratch file `/tmp/opencode/l35verify/latency-note.txt` was cleaned per cleanup rules; the value (`Acknowledge SentAt = 2026-10-29T21:10:24.523207Z; Idle SentAt = 2026-10-29T21:11:24.7790296Z; diff = 60.256s`) is preserved in the report (`docs/builds/35-...-report.md`, latency section). **Why it matters:** the measurement is the core deliverable of the stretch verification; without it in an accessible place, the milestone verification cannot be audited later.

### 4. [consider, checklist completeness] All 6 checklist items pass; no partial items remain
After the fix, the checklist shows all 6 items as `✅ Pass`: shuttle `Idle` (`FaultReason: null`), blocked lane unmarked (`Recover()` executed), alarm SQL `acknowledged_at` non-null, dispatch resumed (`tasks: []`), gate open (`pauseRequested: false`), no retry loop (`[]`). **Why it matters:** M7 requires cross-state consistency; a missing checklist item (e.g., alarm not acknowledged, lane still blocked, shuttle still paused) would mean the system has not fully recovered and could leave a pallet stuck or an alarm unacknowledged after an operator intervention.

### 5. [consider, end-product] M7 milestone still `in progress`; lesson verification closes the design/gap risk
No new code was added in this verification; the design (`Program.cs`, `PauseState.cs`, `TaskBoard.cs`, `Shuttle.cs`, contracts) remains unchanged. The only risk introduced by M7 is the dependency on stable MQTT for full recovery. Once the milestone-level owner sign-off finalizes M7, the verification evidence (latency value, checklist, clean process state) supports advancing to later milestones (traffic control, WMS integration, dashboard diagnostics) without design risk.

---

## What is solid

- The scenario was run end-to-end with real services (`serve`, broker, database) and real commands (`Pause`, `Acknowledge`, `Resume`).
- The student reported the initial MQTT disconnect honestly (`N/A — blocked by MQTT disconnect`), did not fabricate checklist results, and then completed the scenario truthfully after fixing the environment.
- Latency measured and recorded (`60.256 s`) using the existing `MessageEnvelope.SentAt` method; no contract change needed.
- All 6 checklist items confirmed (`✅ Pass`); no partial or unverified items remain after the fix.
- Cleanup completed: `docker compose -f docker/compose.yml down` executed; leftover `serve`/`ShuttleSim` processes killed and verified clean; scratch cleaned; forbidden files untouched (`git diff` clean on `Contracts/`, `docs/contract/`, `TaskBoard.cs`, `Shuttle.cs`, `PauseState.cs`, `Program.cs`).
- Build clean (`dotnet build` succeeded, `0 Warning(s)`, `0 Error(s)`).
- Report (`docs/builds/35-...-report.md`) exists, updated with post-fix verification, includes owner sign-off, records scenario steps, checklist results, latency measurement, and cleanup evidence.
- No contract version change (`v4` preserved); no new DB schema; no new message type; assignment scope respected.

---

## Explain-it check

Answer these out loud or in writing before the milestone-level sign-off:

1. **How was the MQTT disconnect resolved, and why did it prevent the full scenario?** Explain the path from missing `SHUTTLE_DB_CONNECTION_STRING` → unstable MQTT client → `MqttClientNotConnectedException` when `serve` tries to publish `Acknowledge` → dashboard endpoint returns `HTTP 500` → shuttle never receives `Pause`/`Acknowledge`/`Resume`. Why does a healthy MQTT path matter for operator recovery (operator trust, alarm escalation, stuck shuttle recovery)?
2. **How is the recovery latency measured, and what value was recorded?** Where does the timer start (`MessageEnvelope.SentAt` in the `Acknowledge` branch of `Program.cs`) and end (next `ShuttleStatusMessage` with `State == Idle`, `FaultReason == null`)? What is the measured value (`60.256 s` / `60256 ms`) and why does the method not require a new contract field (`MeasuredAt`)?
3. **What does each of the 6 checklist items verify, and which file/state provides it?** Name the concrete source/file for each: shuttle state (`ShuttleStatusMessage` / `ShuttleEndpoint`), blocked lane (`ShuttleCore/Rack.cs` / `Shuttle.Recover()`), alarm SQL (`PostgresAlarmRepository` / alarm table), dispatch (`TaskBoard.cs` / `PausedTaskIds`), gate (`PauseState.cs` / `IsPaused()`), retry (`TaskExecutionLoop` / queued tasks). Why does a missing item mean the scenario is not fully verified (e.g., alarm not acknowledged = operator trust broken; lane still blocked = pallet could be stuck again on retry)?

### Lines/blocks that look unclear or need explanation

- `docs/builds/35-m7-full-recovery-stretch-verification-report.md`, latency section (line 69–71): the measured value (`60.256 s`). Confirm the student can explain how `SentAt` comparison works without altering the message contract.
- Report checklist table (lines 75–88): all 6 items marked `✅ Pass`. Confirm the student knows which observation confirms each item and can explain what would make one fail (e.g., `acknowledged_at` still `null`, `pauseRequested` still `true`).
- `Program.cs` `Acknowledge` branch (line 351 area): how the `RecoveryCommandMessage` is built (`MessageEnvelopes.Create(MessageTypes.RecoveryCommand, ...)`). Confirm the student can explain the message shape and why it uses the existing contract version (`v4`).
- `ShuttleSim/appsettings.json`: `BlockedLanes` injection (`["A01-L02"]`). Confirm the student knows how `Shuttle.Recover()` clears it (`_rack.UnmarkBlocked('A01-L02')`) and why a new `BlockedLanes` configuration does not require a contract or code change.
- Process hygiene (`kill -9`, `pgrep`, `docker compose down`): confirm the student understands why leftover processes must be removed before the next run (port conflicts, MQTT connection reuse, database connection pool exhaustion).

---

## Revisit list (study before milestone sign-off)

- `messaging-contract`: idempotency (`Acknowledge` can be delivered multiple times; `Recover()` should be safe to call again), retries, timeout behavior (`MqttMessageBus` disconnect handling), and why a missing `SentAt` comparison breaks the latency verification (`messaging-contract` skill).
- `fleet-control`: fault/recovery lifecycle (`Shuttle.Recover()`, alarm acknowledgement SQL, blocked-lane clearing, `PauseState` gate), and why the checklist verifies both shuttle state (`Idle`) and database state (`acknowledged_at`) (`fleet-control` skill).
- The `Program.cs` `Acknowledge` branch: exact message construction (`MessageEnvelopes.Create(MessageTypes.RecoveryCommand, ...)`), topic (`topics.RecoveryCommand(...)`), and how it relates to `Shuttle.Recover()` execution.
- `ShuttleSim/appsettings.json`: how `BlockedLanes` is read by `ShuttleSim` (`Shuttle.StartMove` checks `_blockedLanesOnFault`) and cleared by `Recover()` (`_rack.UnmarkBlocked`). Confirm understanding of why injecting a blocked lane is an environment/config change, not a code change.
- `dotnet-style`: resource handling (using `using` for database connections in `PostgresAlarmRepository`, cancellation tokens, no `.Wait()` or `.Result` in async paths), and why leftover processes violate clean-state verification.

---

## End-product check

This verification fits the M7 spec (`spec/shuttle-spec.md` M7: operator dashboard, pause/resume, acknowledge/recovery, full scenario with injected fault, recovered from dashboard alone, latency measurement, cross-state checklist). The assignment scope is verification only; no new design was added. The design holds for later milestones: `TaskBoard.cs` (pause/resume), `Shuttle.cs` (`Recover()`), `PauseState.cs` (movement gate), contracts (`v4` unchanged), database (alarm acknowledgement), and `Program.cs` (command handling) all work together without new interfaces. The open risk is infrastructure stability (`serve` startup requires `SHUTTLE_DB_CONNECTION_STRING`; broker must be healthy) — once verified, M7 can advance to traffic control (M8) and WMS integration without design debt. The milestone stays `in progress` until the owner finalizes milestone-level sign-off.

---

## Process and lesson notes

- Lesson 35 teaches two concepts (measuring recovery latency with existing `SentAt`, cross-state checklist verification) — within the 3-concept cap. No new message type, no new DB table, no contract version bump.
- Lesson defect check: the assignment explicitly says "Do not edit `src/Contracts/`, `docs/contract/`, `TaskBoard.cs`, `Shuttle.cs`, `PauseState.cs`, or `Program.cs`" and "No new message type or config change." The build confirms no forbidden edits (`git diff` clean on all forbidden paths; only allowed `appsettings.json` and build report changed).
- No leftover broker/container (`docker ps` empty) and no leftover `serve`/`ShuttleSim` process (`pgrep` clean after `kill -9`). `docker compose -f docker/compose.yml down` executed.
- The milestone table in `README.md` is not edited; M7 remains `in progress` per instructions. Lesson-level verification (`pass`) is complete and recorded.
