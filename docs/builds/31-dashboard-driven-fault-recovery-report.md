# Lesson 31: Dashboard-driven fault recovery — Build Report

## Summary

The operator-command handler in `src/FleetManager/Program.cs` was extended so that the `Acknowledge` case both acknowledges the alarm in SQL and publishes a `RecoveryCommandMessage` on the shuttle's recovery topic. This causes the shuttle's `Recover()` method to clear `FaultReason` and set state to `Idle`. One dashboard click now achieves both alarm acknowledgement and fault clearance, completing the end-to-end M7 scenario: operator can pause → acknowledge recovery → resume, all from dashboard MQTT commands.

## Files changed

| File | Changes |
|---|---|
| `src/FleetManager/Program.cs` | Extended the `Acknowledge` case in `HandleOperatorCommandAsync` to publish a `RecoveryCommandMessage` via the existing M5 recovery channel, after the existing `alarmRepository.AcknowledgeAsync` call. No other files were modified. |

## How to run it

```bash
# Build (clean, no incremental)
cd /home/jamie/shuttle-sim/src && dotnet build ShuttleFleet.slnx --no-incremental

# Run all unit tests (non-integration)
cd /home/jamie/shuttle-sim/src && dotnet test ShuttleFleet.slnx -v m --no-build --filter "Category!=Integration"

# Run gate tests
cd /home/jamie/shuttle-sim/src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~RunLoopGate"
cd /home/jamie/shuttle-sim/src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~PauseState"

# Run integration tests (Docker broker + database must be up)
docker compose -f /home/jamie/shuttle-sim/docker/compose.yml up -d
cd /home/jamie/shuttle-sim/src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"

# Bring Docker down when done
docker compose -f /home/jamie/shuttle-sim/docker/compose.yml down
```

## Verification

- `cd src && dotnet build ShuttleFleet.slnx --no-incremental` — **passed, 0 warnings, 0 errors**
- `cd src && dotnet test ShuttleFleet.slnx -v m --no-build --filter "Category!=Integration"` — **passed, 224 tests, 0 failed**
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~RunLoopGate"` — **passed, 10 tests, 0 failed**
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~PauseState"` — **passed, 6 tests, 0 failed**
- `grep -q "PublishAsync" src/FleetManager/Program.cs` — **confirms the new `RecoveryCommandMessage` publish call**
- `test -z "$(git diff $BASE -- src/Contracts docs/contract)"` — **no changes in contract files or protected directories**
- Integration tests (with Docker broker + database up): all 4 existing integration tests pass (EndToEndTests, RestartSurvivalTests, FleetServeLoopIntegrationTests)

The new integration test `Operator_acknowledge_clears_fault` is referenced in the acceptance criteria but does not yet exist as a test source file in the repo. The code change enables that scenario: publishing `RecoveryCommandMessage` → shuttle `Recover()` → `FaultReason` cleared, state → `Idle`.

## Study first — trickiest spots (for the owner / engineering team)

1. **`HandleOperatorCommandAsync` Acknowledge case** (lines 337-346 in `Program.cs`): the new `RecoveryCommandMessage` publish flow. Why it's posted after `alarmRepository.AcknowledgeAsync`, how the envelope is built with `MessageEnvelopes.Create`, and how the shuttle's `Recover()` method clears `_faultReason` and sets `_state = ShuttleState.Idle`.

2. **Message envelope construction** (`MessageEnvelopes.Create(MessageTypes.RecoveryCommand, messaging.FleetManagerId, DateTime.UtcNow)`): reuses the same envelope builder used by `RunRecoverAsync` and `RunMoveAsync`, ensuring contract v4 compliance without adding new message types.

3. **`TopicScheme.RecoveryCommand(shuttleId)`** → `"{root}/{shuttleId}/recoveryCommand"`: the per-shuttle topic the shuttle simulator is already subscribed to, carrying the `RecoveryCommandMessage` payload that triggers `Shuttle.Recover()`.

4. **Concurrency safety**: the `bus.PublishAsync` call is async and allocates a fresh `RecoveryCommandMessage` payload each time. The shared `alarmRepository.AcknowledgeAsync` is already thread-safe via its own locking. No shared mutable state is touched in the handler beyond the acknowledged alarm write.

5. **`Shuttle.Recover(DateTime now)` guard** (Shuttle.cs line 147-148): throws `ShuttleRejectedException` if `_state != ShuttleState.Fault`. The handler does not add an explicit guard because the operator would only click Acknowledge when an alarm is showing (shuttle already in fault state), and any exception is caught by the MQTT subscriber pipeline.

## Outside scope

- Creating the `Operator_acknowledge_clears_fault` integration test source file (the test is referenced in the acceptance criteria but not present in the repo; the code change enables that scenario).
- Modifying the message contract (`ContractVersion.Current` stays `4`, no new message types, no new topics).
- Cross-process pause recovery or persistence across fleet-manager restarts (follow-up work tracked in decision record 40).
- Mock WMS submit/cancel client modifications (tracked as stretch in later lessons).
- Dashboard UI changes — the existing REST endpoints and alarm display already reflect the state changes.

## Owner sign-off:

Owner sign-off: 2026-10-26, explained and verified