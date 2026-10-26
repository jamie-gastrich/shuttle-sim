# Decision 40: Dashboard-driven fault recovery (lesson 31)

**Date:** 2026-10-26

## Status: adopted

### Choice: `Acknowledge` command extends to clear shuttle fault

The handler's `Acknowledge` case in `HandleOperatorCommandAsync` now also publishes a `RecoveryCommandMessage` on the shuttle's recovery topic. The shuttle's `Recover()` method clears `FaultReason` and sets state to `Idle`. One dashboard click both acknowledges the alarm in SQL and clears the fault that caused it.

### Rejected options

| Option | Why rejected |
|---|---|
| **B. `Acknowledge` only marks alarm in SQL; separate `Recovery` command needed** | Leaves the M7 scenario incomplete — the operator would need two dashboard clicks (acknowledge + separate recovery), and the fault might persist across the two clicks. The done-when requires "recovered from the dashboard alone" in a single flow. |
| **C. `Acknowledge` clears fault via direct shuttle state mutation** | Breaks the contract-only boundary. The handler must not directly access shuttle internals (`_faultReason`, `_state`). The established mechanism for clearing faults is the `RecoveryCommandMessage` → shuttle `Recover()` channel, already tested and proven in M5. |
| **D. Keep `OperatorCommandMessage.Command` as `{ Pause, Resume, Acknowledge }` without extending `Acknowledge`** | The scenario would not work end-to-end from the dashboard. The fault would persist after acknowledgement, and a subsequent `Resume` would not fully recover the shuttle. This was the gap that lesson 31 was directed to close. |

### What would change the choice

- If the M7 done-when criterion shifts to require operator-driven recovery across a fleet-manager restart, the `PauseState` and `alarms` table would need to survive restart, and the decision would be revisited.
- If the contract version must bump (e.g., a new `RecoveryKind` enum value added to `OperatorCommandMessage.Command`), this decision would be replaced with a new one with a version bump.
- If a simpler mechanism is discovered (e.g., the shuttle clears its own fault on the next status update without an explicit recovery command), the `Acknowledge` extension could be scaled back.

### File changes

- `src/FleetManager/Program.cs` — extended `HandleOperatorCommandAsync` Acknowledge case with two additional lines (publish `RecoveryCommandMessage`). No other files changed.
- `docs/lessons/31-dashboard-fault-recovery.md` — lesson content teaching the chosen approach.
- `docs/lessons/31-dashboard-fault-recovery-hints.md` — three-tier hints for the student.
- `docs/decisions/40-dashboard-fault-recovery.md` — this decision record.

### Traceability

- **M7 done-when:** "a full scenario, including an injected fault, is run and recovered from the dashboard alone."
- **Lesson 30:** shared `PauseState` — enabler for dashboard pause reaching the movement gate.
- **M5:** `Shuttle.Recover()`, `RecoveryCommandMessage`, `RecoveryAckMessage` — existing recovery channel.
- **Decision 39:** shared pause state in `FleetCore` — prior M7 enabler, no contract change.
- **Contract version:** stays `4`; no new message types; only wiring existing types together.