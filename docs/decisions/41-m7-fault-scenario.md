# Decision 41: M7 end-to-end fault scenario workflow

**Date:** 2026-10-26
**Decision driver:** M7 done-when criterion — "a full scenario, including an injected fault, is run and recovered from the dashboard alone."

## Status

Chosen: **A. Teach the end-to-end workflow** (no new code; the existing pieces compose together).
Rejected: B. Add a dashboard fault-injection command; C. Add cross-process pause and recovery.

## Reasons

The code already has all the pieces needed for the end-to-end M7 fault scenario:

- **Acknowledge → RecoveryCommandMessage → Shuttle.Recover()** is wired from lesson 31. The `Acknowledge` case in `HandleOperatorCommandAsync` publishes a `RecoveryCommandMessage` on the shuttle's recovery topic, and the shuttle's `Recover()` method clears `FaultReason` and sets state to `Idle`.
- **Pause → PauseState → RunLoopGate** is wired from lesson 30. The shared `PauseState` in `FleetCore` is used by both the gate and the operator-command handler, so dashboard Pause/Resume reaches the movement gate.
- **Operator command topics and message types** are already defined in contract v4: `OperatorCommandMessage` (Pause/Resume/Acknowledge), `RecoveryCommandMessage`, `TopicScheme` topics.

The lesson's job is not to add more code but to teach the student how these existing pieces compose into a coherent dashboard workflow, and how to verify the full scenario runs correctly from start to finish.

Option B (add a dashboard fault-injection command) was rejected because:
- The M7 done-when only requires the operator to recover from a fault that the simulation already introduces (via blocked lanes in `appsettings.json`).
- Adding a dashboard fault-injection command would add unnecessary complexity and a new MQTT topic/handler, deviating from the minimal-change principle.
- Real fleet systems typically have separate test/fault-injection modes, but this sim's M7 scope is operator-led recovery, not fault injection.

Option C (add cross-process pause and recovery) was rejected because:
- It would require MQTT subscribing in a separate process, distributed `PauseState` coordination, and message-loss handling.
- It is a separate M8/M9 slice (cross-process control), not part of M7's "from the dashboard alone" criterion.
- The in-process `PauseState` and recovery channel are already sufficient for the done-when.

## Decision record

- **A.** Teach the end-to-end workflow. The lesson (32) teaches the complete Pause→Acknowledge→Resume sequence as a coordinated dashboard workflow, and how to verify it runs correctly. No new code is needed; the existing pieces compose together. This keeps the change minimal and focused on the student's ability to explain the full flow.
- **B.** `Pause` and `Resume` remain unchanged in their effect on `PauseState` — they only affect the movement gate, not fault state. This was already decided in decision 39 and is confirmed unchanged.
- **C.** No contract change: `OperatorCommandMessage.Command` stays `{ Pause, Resume, Acknowledge }`. The new behavior uses the existing `RecoveryCommandMessage` type already defined in v4.
- **D.** `ContractVersion.Current` stays `4`. No new configuration key.

## What comes next

- **Lesson 32** (this decision) teaches the student the end-to-end workflow and verification steps.
- **After lesson 32**, the owner can run the full M7 scenario from the dashboard and confirm M7's done-when criterion is met.
- **Follow-up**: Cross-process pause and fault-injection dashboard commands are tracked as future slices (M8/M9), not part of this decision.

## References

- Decision 39: Shared pause state in FleetCore (lesson 30)
- Decision 40: Dashboard-driven fault recovery (lesson 31)
- M7 spec: "a full scenario, including an injected fault, is run and recovered from the dashboard alone."
- Lesson 31: `HandleOperatorCommandAsync` Acknowledge case now publishes `RecoveryCommandMessage`
- Lesson 30: `PauseState` in `FleetCore`, shared between gate and handler