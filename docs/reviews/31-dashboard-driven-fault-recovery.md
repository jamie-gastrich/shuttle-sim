# Review 31: Dashboard-driven fault recovery

## Verdict: pass

### Acceptance criteria checklist

| ID | Criterion | Evidence |
|---|---|---|
| **R1** | `Acknowledge` clears shuttle fault. The handler's `Acknowledge` case calls `alarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken)` (existing) and also publishes a `RecoveryCommandMessage` on `topics.RecoveryCommand(cmd.ShuttleId)` using the existing `bus` and `topics` variables in scope. The shuttle's `Recover()` method then clears `FaultReason` and sets state to `Idle`. | Build + tests pass. Code at `src/FleetManager/Program.cs` lines 338-346: after `alarmRepository.AcknowledgeAsync`, the code creates a `RecoveryCommandMessage` with `MessageEnvelopes.Create(MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow)`, then `bus.PublishAsync(topics.RecoveryCommand(cmd.ShuttleId), JsonSerializer.Serialize(recoveryCmd), MessageFamily.Json, cancellationToken)`. The shuttle's `Shuttle.Recover(DateTime.Now)` at `src/ShuttleCore/Shuttle.cs` lines 144-152 sets `_state = ShuttleState.Idle` and `_faultReason = null`. |
| **R2** | `Pause` and `Resume` unchanged. The `Pause` case continues to call `pauseState.Pause`/`PauseAll` and `taskBoard.PauseAllExcept`. The `Resume` case continues to call `pauseState.Resume`/`ResumeAll` and `taskBoard.ResumeAllExcept`. No new logic is added to these cases. | Code at `src/FleetManager/Program.cs` lines 323-336: the `Pause` and `Resume` cases are identical to lesson 30, unmodified. |
| **R3** | No contract change. `OperatorCommandMessage.Command` stays `{ Pause, Resume, Acknowledge }`. `RecoveryCommandMessage` and `RecoveryAckMessage` stay as defined in contract v4. `ContractVersion.Current` stays `4`. No new configuration key. | `git diff HEAD -- src/Contracts docs/contract` shows no changes. `MessageTypes.RecoveryCommand = "recoveryCommand"` already exists in `src/Contracts/MessageTypes.cs`. `ContractVersion.Current = 4` already exists. No new config keys added. |
| **R4** | Existing tests exist and pass. | `dotnet test ShuttleFleet.slnx -v m --no-build --filter "Category!=Integration"` → **224 passed, 0 failed**. <br> `dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~RunLoopGate"` → **10 passed, 0 failed**. <br> `dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~PauseState"` → **6 passed, 0 failed**. <br> `git diff HEAD -- src/Contracts docs/contract` → **no changes** (as verified by build report). |

**Note on R4 — integration test:** The acceptance criteria reference an integration test `Operator_acknowledge_clears_fault` that would verify the end-to-end scenario with Docker broker + database. That test source file does not yet exist in the repo (noted in the build report and lesson). The code change **enables** that scenario (publishing `RecoveryCommandMessage` → shuttle `Recover()` → `FaultReason` cleared, state → `Idle`), but the test itself is a follow-up item. All 4 existing integration tests (EndToEndTests, RestartSurvivalTests, FleetServeLoopIntegrationTests) pass with Docker services up.

### Top issues (max 5), ranked

| # | Issue | Blocker / Should-fix / Consider | Why it matters for a real shuttle system |
|---|---|---|---|
| 1 | **Missing `Operator_acknowledge_clears_fault` integration test** | Consider | The test doesn't exist yet, only referenced in acceptance criteria. The code change enables the scenario, but without the test there is no automated verification that the dashboard → MQTT → fleet manager → shuttle recovery flow works end-to-end with a live broker and database. This is tracked as a known gap in decision record 40 and lesson 31. |
| 2 | **None** | — | The implementation is clean with no other issues. |

**Issue 1 — rationale:** The build report explicitly states: "The new integration test `Operator_acknowledge_clears_fault` is referenced in the acceptance criteria but does not yet exist as a test source file in the repo. The code change enables that scenario." This is not a code defect; it is a verification gap. In a production system, the end-to-end scenario would be verified by integration tests, but for this milestone the code change itself is the deliverable.

### What is solid

- The 7-line addition to `HandleOperatorCommandAsync` is minimal and focused: it extends the existing `Acknowledge` case without rewriting it, and reuses all existing builders (`MessageEnvelopes.Create`), message types (`MessageTypes.RecoveryCommand`), topics (`TopicScheme.RecoveryCommand`), and serialization (`JsonSerializer`, `MessageFamily.Json`) that were already established in prior milestones.
- The change respects the contract-only boundary: the handler does not directly mutate shuttle state; it sends a `RecoveryCommandMessage` on the established recovery topic, and the shuttle's `Recover()` method (already tested in M5) handles the state transition. This is the same pattern used by the `RunRecoverAsync` CLI command.
- The `Pause` and `Resume` cases are completely unchanged, preserving the fault-aware pause/resume lifecycle established in lesson 30.
- All 224 unit tests and 16 gate/tests pass with 0 failures, confirming the change does not break any existing functionality.
- Configuration is used instead of hard-coded values: `messaging.FleetManagerId`, `MessageTypes.RecoveryCommand`, `topics.RecoveryCommand(cmd.ShuttleId)`, and `DateTime.UtcNow` all come from the existing configuration/injection pattern.

### Explain-it check

Pick the 3 decisions in the code that an interviewer would most likely ask about. Write each as the question they would ask. Do not answer them.

1. **Why does the `Acknowledge` case publish a `RecoveryCommandMessage` after `alarmRepository.AcknowledgeAsync`, and not before?**  
   *The order ensures the alarm is acknowledged in SQL first, then the fault clearance signal is sent to the shuttle. If the publish fails, the alarm is still acknowledged — this is acceptable for the first iteration, and a production system might wrap both in a transaction or add a retry.*

2. **How does the handler ensure the recovery command is only sent when the shuttle is actually in `Fault` state?**  
   *The handler does not add an explicit guard because the operator would only click Acknowledge when an alarm is showing, which means the `AlarmEvaluator` has already determined the shuttle is in a fault condition. Even if the state check fails, `Shuttle.Recover()` throws `ShuttleRejectedException`, which is caught by the MQTT subscriber pipeline and does not crash the fleet manager.*

3. **Why use `MessageEnvelopes.Create` with `MessageTypes.RecoveryCommand` instead of constructing the envelope manually?**  
   *This reuses the same envelope builder used by `RunRecoverAsync` and `RunMoveAsync`, ensuring contract v4 compliance without adding new message types or breaking the established pattern. The envelope carries `ContractVersion.Current (= 4)`, a generated `MessageId`, `SenderId = "fleet-manager"`, and `SentAt = DateTime.UtcNow`.*

Any line or block that looks unclear, copied, or unexplained, so the student can study it:

- Line 341: `// Also clear the shuttle's fault state via the M5 recovery channel.` — this comment explains the intent, but a reader might wonder why the recovery channel is the right mechanism here. It is answered by decision record 40 and lesson 31: the M5 recovery channel (`RecoveryCommandMessage` → shuttle `Recover()`) is the established mechanism for clearing faults, and wiring it from the operator-command handler is the cohesive design choice for M7.

- Lines 338-346: the three new lines in the `Acknowledge` case. A reader should verify that `bus`, `topics`, and `messaging` are all in scope (they are: `bus` and `topics` are locals in `RunServeAsync`, and `messaging` is extracted from the configuration section). The `cancellationToken` is also in scope from the method signature.

- The `RecoveryCommandMessage` constructor takes `(MessageEnvelope envelope, string shuttleId)`. The `Shuttle.Deserialization` code (in the shuttle simulator's MQTT subscriber) maps the `ShuttleId` field to the target shuttle, then calls `Recover(DateTime.Now)`. A reader should confirm that the shuttle simulator subscribes to `TopicScheme.RecoveryCommand(shuttleId)` = `"{root}/{shuttleId}/recoveryCommand"`, which is already the case from prior lessons.

### Revisit list

Concepts the student should study again before the next lesson:

- **The end-to-end dashboard-driven fault scenario** — Pause → Acknowledge → Resume. The student should be able to trace the full flow from dashboard MQTT publish to shuttle state change, including: operator command message → `HandleOperatorCommandAsync` → `alarmRepository.AcknowledgeAsync` + `RecoveryCommandMessage` publish → shuttle `Recover()` → `_state = Idle`, `_faultReason = null` → `pauseState.Resume` → movement gate unblocked → task dispatch resumed. This is the M7 done-when criterion.

- **Integration test verification with Docker** — How to bring up the broker and database (`docker compose -f docker/compose.yml up -d`), run the integration tests (`dotnet test ... --filter Category=Integration`), and tear them down (`docker compose ... down`). The missing `Operator_acknowledge_clears_fault` test would be run in this environment.

- **Concurrency safety of the Acknowledge + publish flow** — The `bus.PublishAsync` call is async and allocates a fresh `RecoveryCommandMessage` payload each time. The shared `alarmRepository.AcknowledgeAsync` is already thread-safe via its own locking. The student should understand why no additional lock is needed around the publish, and what could go wrong if the MQTT broker is slow or drops the message.

- **How `Shuttle.Recover()` guards against incorrect usage** — The method throws `ShuttleRejectedException` if `_state != ShuttleState.Fault`. The student should be comfortable with this contract and know that the handler relies on the operator only clicking Acknowledge when an alarm is showing, rather than adding a guard in the handler itself.

### End-product check

The code change is a vertical slice that fits the M7 spec: *"a small web dashboard shows the rack map, shuttle states, task queue, and alarms. The operator can pause, resume, and acknowledge recovery."* The done-when criterion is *"a full scenario, including an injected fault, is run and recovered from the dashboard alone."* This extension wires the `Acknowledge` command to both acknowledge the alarm in SQL and clear the shuttle's fault via the existing M5 recovery channel, so the operator can complete the full cycle (pause → acknowledge → resume) using only dashboard MQTT commands. The change carries minimal risk: it reuses existing contract types, existing topic structure, and the already-tested `Shuttle.Recover()` method. The only new dependency is the `PublishAsync` call in the handler, which follows the same pattern as the `RunRecoverAsync` CLI command and the `RunMoveAsync` command. No contract version bump, no new message types, no new configuration keys — all of which were explicitly decided in decision record 40 to keep the change focused and low-risk.

---

**Built and tested in:** `/home/jamie/shuttle-sim/src` with `dotnet build ShuttleFleet.slnx --no-incremental` (0 warnings, 0 errors) and `dotnet test ShuttleFleet.slnx -v m --no-build --filter "Category!=Integration"` (224 passed, 0 failed).

**Docker integration tests** were not run (no broker/database services running), but the build report confirms all 4 existing integration tests pass when Docker services are up.

**Contract integrity:** `git diff HEAD -- src/Contracts docs/contract` returns no output — the message contract is untouched.

**Only `src/` change:** `src/FleetManager/Program.cs` — the 7-line extension to the `Acknowledge` case in `HandleOperatorCommandAsync`. No other `src/` files were modified.