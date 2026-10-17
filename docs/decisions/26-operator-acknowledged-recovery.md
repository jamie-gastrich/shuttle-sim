# Decision Record 26: Operator-acknowledged recovery (recovery command and fault clearance)

**Date:** 2026-10-17
**Milestone:** M5 (Faults and recovery) — lesson 18: operator-acknowledged recovery
**Status:** Accepted. Append-only. Builds on [record 23](23-blocked-path-fault-and-second-alarm.md) (blocked-path fault), [record 25](25-reassign-failed-shuttle-work.md) (reassignment of unstarted work), [record 22](22-serve-loop-and-alarm-publish.md) (alarm publish path), [record 21](21-alarms-with-severity.md) (alarms with severity), [record 17](17-occupancy-and-fault-reservations.md) (faulted shuttles hold occupancy).
**Wire contract:** **new version, `v4`.** Adds message kinds for recovery commands/acks. `docs/contract/v3.md` stays; add `docs/contract/v4.md`. `ContractVersion.Current` becomes `4`. This is the first contract change since v3 (alarm). 
**Replaced by:** later records that change recovery semantics (auto-recovery, multi-shuttle recovery, or clearing lost-communication differently). Append-only.

---

## 1. What we are deciding

M5 says: *"Recovery that an operator acknowledges. Done when: every injected fault leads to the right alarm and the right recovery..."*

Lesson 16 gave us a shuttle-detected fault (`blockedPath`) that puts the shuttle in `Fault` state with `FaultReason="blocked path"`, and the manager publishes a `blockedPath` alarm. Lesson 17 reassigns unstarted work when a shuttle faults at `StartMove`. But there is no way to clear the fault. The shuttle stays in `Fault`; the alarm latch behavior is already tested (recovery clears the latch when status becomes healthy), but nothing in the system produces a "healthy" status from a faulted shuttle except manual state changes.

This record decides:
- **A.** How recovery is triggered (operator acknowledges alarm → recovery command).
- **B.** How the shuttle exits `Fault` state (new transition/method).
- **C.** The wire contract for recovery (v4), keeping it minimal and separate from alarms.
- **D.** Scope: focus on shuttle-detected faults (blockedPath) first; lostCommunication recovery is a separate slice.

---

## 2. Decision A: Operator acknowledges alarm, triggering recovery command

Recovery is operator-acknowledged per the spec. The alarm is already published by the fleet manager. The operator (dashboard, M7) acknowledges the alarm; that causes the fleet manager to issue a recovery command to the affected shuttle.

Options:
- A: Fleet manager sends `RecoveryCommand` (manager→shuttle) on operator ack. Shuttle responds with ack; clears fault. (chosen)
- B: Shuttle can self-recover when condition clears (no operator ack). Violates "operator acknowledges".
- C: Recovery is embedded in status. Not a command/ack.

**Decision A:** Operator acknowledgement results in a `RecoveryCommandMessage` from fleet manager to shuttle (`{root}/{shuttleId}/recoveryCommand` topic branch) with correlation/ack semantics like move commands. This preserves the command/ack pattern and keeps the contract explicit.

Rejected B (no operator ack) and C (no clear trigger).

---

## 3. Decision B: Shuttle clears fault via recovery transition

Shuttle is in `Fault` state. From Fault, other operations throw. We need a way out.

Options:
- A: Add `Recover(DateTime now)` method. Preconditions: in Fault. Effect: set state to `Idle`, clear `FaultReason`, update `_lastEventAt`. (chosen)
- B: Allow `RaiseFault` to be "cleared" differently; or allow transition from Fault to Idle on any command. Confusing.
- C: Shuttle auto-recovers when the blocking condition is gone (check Rack on next tick). No operator involvement — violates A.

**Decision B:** `Shuttle.Recover(now)` transitions from `Fault` to `Idle`, sets `FaultReason = null`, preserves position and carried pallet (if any). The fault latch in manager clears when shuttle reports `State != Fault` (healthy status) — this matches existing test behavior ("A recovered shuttle clears the latch"). The method throws `ShuttleRejectedException` if not in Fault state.

Rejected C. This keeps recovery explicit and operator-acknowledged.

---

## 4. Decision C: Contract v4 adds recovery message kinds

We need minimal new message kinds. Following the existing envelope pattern (v2/v3), add:
- `RecoveryCommandMessage` (manager→shuttle): targets shuttleId, maybe reason/ack context
- `RecoveryAckMessage` (shuttle→manager): mirrors command ack pattern (Accepted, Reason)

Wire names: `recoveryCommand`, `recoveryAck`. Topics: command-style under shuttle branch (e.g. `{root}/{shuttleId}/recoveryCommand`, ack on `{root}/{shuttleId}/recoveryAck`); manager subscribes to `+/recoveryAck`.

`ContractVersion.Current` becomes 4. `docs/contract/v4.md` documents these kinds; v3 unchanged.

This is a small, additive change. No change to existing kinds.

---

## 5. Scope and limits

- Focus on shuttle-detected faults (blockedPath) first. LostCommunication recovery semantics differ and come later.
- Recovery does not automatically release reservations except as already decided; the shuttle's physical position/occupancy remains as-is (per record 17).
- No dashboard implementation (M7) — just the message shapes and the mechanism so the system can be exercised.
- No auto-recovery on condition change; operator must acknowledge.
- Reassignment already handled for unstarted work; mid-move fault recovery is not in this slice.

---

## 6. Verification (measured)

Baseline: pure suite RC 0, 8 assemblies Passed!, 0 Failed!, 134 tests total (as of review 17). No recovery code exists. After this slice, contract becomes v4, new message kinds exist, Shuttle gains Recover, new tests added (contract + shuttle + manager wiring as needed). The fault latch clearing behavior is already present in tests; this enables the real path.

Not yet verified in code: the actual implementation details (the student will build per lesson). This record states the design.

---

## 7. What would change this choice

- LostCommunication recovery needs different handling (shuttle may reconnect with stale state) → new record.
- Mid-move fault recovery requires pallet state reconciliation → new record.
- Auto-recovery when obstacle clears becomes desired → revisit operator-ack requirement.
- Need for recovery reason/details or multi-step recovery → contract extension (still v4+ with new record).

---

## 8. What this unblocks

- Lesson 18 can teach recovery with no unguided product choices.
- M5 moves closer to "right recovery" for blockedPath.
- Enables end-to-end recovery flow once operator ack path exists.
- Sets contract pattern for future recovery kinds.
