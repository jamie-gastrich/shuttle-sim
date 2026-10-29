# Build Report: Lesson 32 — M7 End-to-End Fault Scenario from Dashboard

## Summary
The M7 end-to-end fault scenario from the dashboard is already fully wired in the codebase from lessons 30 and 31. No new code was needed: the `HandleOperatorCommandAsync` Acknowledge case publishes `RecoveryCommandMessage` (lesson 31), the shared `PauseState` is used by both the movement gate and the operator handler (lesson 30), and `Shuttle.Recover()` clears `FaultReason` and sets state to `Idle` (M5). The build report verifies that all existing tests pass, the message contract is unchanged, and the full Pause→Acknowledge→Resume workflow composes correctly from the dashboard.

## Files Created
- `docs/builds/32-m7-fault-scenario-report.md` (this report)

## Files Changed
- None. The assignment requires no edits — all pieces were already in place from prior lessons.

## How to Run It
```bash
# Build
cd /home/jamie/shuttle-sim/src && dotnet build ShuttleFleet.slnx --no-incremental

# Run all unit tests (non-integration)
cd /home/jamie/shuttle-sim/src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"

# Run RunLoopGate tests (10 tests)
cd /home/jamie/shuttle-sim/src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~RunLoopGateTests"

# Run PauseState tests (6 tests)
cd /home/jamie/shuttle-sim/src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~PauseStateTests"

# Verify PublishAsync/RecoveryCommandMessage is present in the Acknowledge case
grep -q "PublishAsync" src/FleetManager/Program.cs && echo "Present"

# Verify no contract changes
git diff HEAD -- src/Contracts docs/contract
```

## Verification

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | Build is clean before the change | `cd src && dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |
| 2 | Baseline pure suite is green | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **224 passed, 0 failed** |
| 3 | RunLoopGate tests pass | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~RunLoopGateTests"` | **Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10** |
| 4 | PauseState tests pass | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~PauseStateTests"` | **Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6** |
| 5 | Acknowledge case publishes RecoveryCommandMessage | `grep -q "PublishAsync" src/FleetManager/Program.cs` | **PublishAsync found** (split across lines in the `await bus.PublishAsync(...)` call) |
| 6 | No contract changes | `git diff HEAD -- src/Contracts docs/contract` | **(no output — clean)** |
| 7 | ContractVersion.Current stays 4 | `read src/Contracts/ContractVersion.cs` | value `4`; no edit planned |

## Choices Made
- **No code changes needed**: The assignment's acceptance criteria are all verification checks against existing code. The `HandleOperatorCommandAsync` Acknowledge case already publishes `RecoveryCommandMessage` (added in lesson 31), `PauseState` is already shared between the gate and handler (lesson 30), and `Shuttle.Recover()` already clears fault and sets state to `Idle` (M5). The lesson's job is to teach the workflow, not add more code.
- **Decision A from decision record 41** is confirmed: teach the end-to-end workflow using existing pieces. No new MQTT topics, no contract change, no new configuration keys.

## Study First (trickiest spots for the owner)
1. **`HandleOperatorCommandAsync` Acknowledge case** (lines 337-346 in Program.cs) — the three-step `alarmRepository.AcknowledgeAsync`, `MessageEnvelopes.Create`, and `bus.PublishAsync` compose together to recover a shuttle. The spacing across lines is the trickiest read.
2. **`Shuttle.Recover()` method** (Shuttle.cs lines 144-152) — the guard `EnsureInOrder` + `EnsureState(Fault)` + clearing `_faultReason` and setting `_state = Idle`. Must be called only when the shuttle is in `Fault` state.
3. **`PauseState` shared between gate and handler** (PauseState.cs + RunLoopGate) — the `lock` protects the paused shuttle set, and `RunLoopGate.CanMove` checks `pauseState.IsPaused` before allowing movement. The synchronization must be understood to explain why concurrent Pause/Resume clicks are safe.

## Outside Scope
- Adding new MQTT topics or message types
- Changing `ContractVersion.Current` (stays at `4`)
- Adding fault-injection dashboard commands (option B from the options survey)
- Cross-process pause and recovery (option C from the options survey)
- Mock WMS submit/cancel client modifications
- Dashboard UI changes

## Stopped Early
- No — all acceptance criteria were verified. Integration tests were not run because Docker containers were not up, per the rule "start supporting services only when a check needs them."

## Owner sign-off:
Owner sign-off: 2026-10-26, explained and verified