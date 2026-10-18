# Decision Record 27: Recovery wiring — who sends, who answers, and with which QoS

**Date:** 2026-10-17
**Milestone:** M5 (Faults and recovery) — lesson 19: wiring the recovery round trip
**Status:** Accepted. Append-only. Builds on [record 26](26-operator-acknowledged-recovery.md) (operator-acknowledged recovery, `Shuttle.Recover`, contract v4), [record 23](23-blocked-path-fault-and-second-alarm.md) (blocked-path fault), [record 22](22-serve-loop-and-alarm-publish.md) (alarm publish path), [record 10](10-command-retry-and-ack-timeout.md) (command retry and ack timeout), [record 09](09-command-acknowledgement-and-idempotency.md) (acknowledgement and idempotency).
**Wire contract:** **no change.** Contract stays `v4`. `docs/contract/v4.md` already documents `recoveryCommand` and `recoveryAck` (lesson 18). `ContractVersion.Current` stays `4`.
**Replaced by:** later records that change how recovery is triggered (dashboard REST, automatic recovery) or what recovery does (releasing occupancy, multi-step recovery). Append-only.

---

## 1. What we are deciding

Record 26 decided *what* recovery is: the operator acknowledges, the fleet manager sends a `recoveryCommand`, the shuttle clears its fault and answers with a `recoveryAck`. Lesson 18 built the two ends that can exist without a wire between them: `Shuttle.Recover` on the shuttle, and the two message kinds in the contract.

Nothing in the running system sends a recovery command. Nothing handles one. Review 18 and review 19 both list this as the next M5 slice: *"the actual recovery wiring (manager sends `recoveryCommand` on operator ack; endpoint calls `Recover`, publishes `recoveryAck` and healthy status)"*.

This record decides:

- **A.** How the operator's acknowledgement enters the system today.
- **B.** What the manager-side sender looks like (new class vs. generalizing the move sender).
- **C.** Which delivery-guarantee label (`MessageFamily`, explained in §4) each recovery message uses, in both directions.
- **D.** What the shuttle answers when a recovery command arrives more than once, or when there is nothing to recover.
- **E.** A documentation-ordering convention for future contract versions (fold-in from review 19's consider item).

---

## 2. Decision A: the operator's entry point is a `recover` CLI command

The fleet manager already has a one-shot operator path: `FleetManager move <shuttleId> <toLane>` connects, sends one command, waits, and exits. Recovery gets the same shape: `FleetManager recover <shuttleId>`.

Options:

- **A: A one-shot `recover` CLI command, mirroring `move`.** (chosen)
- **B: An alarm-acknowledgement path inside `FleetManager serve`.** The serve loop would need an "operator acked alarm X on shuttle Y" input. No such input, message, or concept exists yet; building one means inventing M7's operator interface inside M5.
- **C: A REST endpoint on the fleet manager now.** The spec puts REST order intake in M6 and the dashboard in M7. Building it now re-orders the milestones and pulls database and HTTP work into an M5 slice.

**Decision A.** `FleetManager recover <shuttleId>` is an operator saying, out loud, "I have dealt with the blocked path; send the recovery." The command builds a `RecoveryCommandMessage`, publishes it, waits for the `recoveryAck`, then waits for a healthy status from that shuttle, using the same configuration keys as `move` (`RoundTripTimeoutMs`, `AckTimeoutMs`, `MaxAttempts`, `BackoffMs`). When M7 builds the dashboard, its REST route calls the same sender; the CLI does not get thrown away, it stays as the operator's terminal tool.

Rejected B and C: both are real designs, but both are dashboard work. M5's done-when proof ("every injected fault leads to the right alarm and the right recovery") needs a trigger an operator can run today; a CLI command is the smallest honest one.

---

## 3. Decision B: a separate `RecoverySender`, mirroring `ReliableCommandSender`

`ReliableCommandSender` is typed to move commands: `SendAsync(MoveCommandMessage)` publishes on the command topic with `MessageFamily.Command`, and `OnAckReceivedAsync` accepts only `MessageTypes.CommandAck`. Recovery needs the same retry-and-correlation machinery with different details: a different topic branch, a different family label (the label that picks a delivery guarantee — defined in §4), and different command and ack kinds.

Options:

- **A: A new `RecoverySender` class that mirrors `ReliableCommandSender`'s shape.** (chosen)
- **B: Generalize `ReliableCommandSender` into one correlated-command sender** that takes topic, family, payload, and expected ack kind per call.
- **C: A shared base class or helper holding the retry loop**, with two thin subclasses.

**Decision B.** A separate class. Reasons, in order:

1. The move path is green, reviewed twice, and mutation-tested (reviews 10, 17, 18). Touching it inside a fault slice puts two risks in one commit: "did the new fault behaviour break?" and "did the refactor break the old behaviour?".
2. Each sender stays small enough to explain out loud in one sitting — the whole class is one retry loop and one ack waiter.
3. Two copies of a ~40-line loop is a known, accepted cost. The usual rule for duplication is three, not two: the first copy is a fluke, the second is a pattern, the third is a design. When a third correlated command kind arrives (pause/resume for M7, load/unload for the pallet work), the shared core gets extracted then, in its own slice with its own record.

Rejected B and C: both are the right *long-term* shape and both are wrong *now* — they trade a small, certain duplication cost for a refactor risk in the middle of M5. `ReliableCommandSender`'s public surface and its four tests stay exactly as they are.

**What would change this:** a third correlated command kind, or a bug found in the retry loop that must be fixed in both copies in the same commit.

---

## 4. Decision C: the recovery *command* uses the Recovery family, the recovery *ack* uses the Ack family

`MessageFamily` is not on the wire. It is a local label the bus uses to pick a QoS level (MQTT quality of service — the broker's delivery guarantee) from configuration. `QosOptions` has four knobs: `Command`, `Ack`, `Status`, `Alarm`. Lesson 18 added `MessageFamily.Recovery`, mapped to `_options.Qos.Command`.

Options for the two recovery directions:

- **A: Command direction `MessageFamily.Recovery` (Command QoS); ack direction `MessageFamily.Ack` (Ack QoS).** (chosen)
- **B: Both directions `MessageFamily.Recovery`.**
- **C: A fifth `RecoveryAck` field in `QosOptions`.**

**Decision C (A).** The family follows the *role* of the message, not the flow it belongs to. A recovery command is a command; it rides the Recovery family, which is already mapped to Command QoS and already pinned by the test `Publish_with_Recovery_family_uses_correct_QoS`. A recovery ack is an acknowledgement; it rides the Ack family, exactly like `commandAck`, and gets whatever reliability the operator configured for acks. Subscriptions mirror the publish side: the manager subscribes `AllRecoveryAcks` with `MessageFamily.Ack`; the shuttle subscribes the per-shuttle `recoveryCommand` topic with `MessageFamily.Recovery`.

Rejected B: an ack whose reliability silently follows the command knob. If an operator later lowers `Command` to `AtMostOnce` to make move traffic cheap, recovery acks become lossy too — and a lost ack makes the manager re-send a command the shuttle has already run. Acks should keep ack reliability without the operator having to know which flow they came from.

Rejected C: correct but heavy. A fifth config knob exists forever and is tuned almost never — recovery messages are rare (one per fault). Adding a knob is a cost; it needs a reason beyond tidiness.

**What would change this:** if recovery ever needs a reliability class of its own (for example, a hard requirement that recovery acks are ExactlyOnce regardless of what move acks do), add a `QosOptions` field then, with the requirement quoted in a new record.

**Pinning.** The command direction is already pinned by the in-process-broker QoS test. This slice adds the ack-direction pin: the endpoint tests assert the `recoveryAck` publish carries `MessageFamily.Ack` on the fake bus, and the sender tests assert the `recoveryCommand` publish carries `MessageFamily.Recovery`. Review 19's leftover ("the `MessageFamily.Recovery => Qos.Command` arm is pinned by no test") is already closed by the existing test; this slice keeps both directions honest at the *call site*, not only inside the bus.

---

## 5. Decision D: a recovery that already happened still answers "yes"

At-least-once delivery means the same command can arrive twice: the manager re-sends when an ack is lost, and the broker may deliver a duplicate. Operators also press buttons twice. So the endpoint will see recovery commands for shuttles that have nothing to recover.

Options for "shuttle is not in `Fault` when a recovery command arrives":

- **A: Answer `Accepted: true`, `Reason: null`, change nothing.** (chosen)
- **B: Answer `Accepted: false` with the `Recover` rejection ("cannot recover while idle").**
- **C: Stay silent — no ack at all.**

**Decision D (A).** The shuttle answers success whenever the operator's goal already holds: the shuttle is not faulted. Three reasons:

1. **It is true.** Recovery's whole purpose is "make this shuttle not faulted". That state already holds.
2. **The wire contract says so.** `v4.md` documents `Reason` as "Explanation for why the command was rejected; null when accepted". A success carries no reason string. Reporting "already idle" as a rejection would also invent a failure mode the operator has to learn.
3. **Silence is worse than either.** No ack makes the manager retry to exhaustion and report `NoAcknowledgement` — the operator is told recovery failed when it succeeded, and a later M7 dashboard loop would keep retrying forever.

**Idempotency by precondition, not by list.** The move handler keeps a message-id list (`IdempotencyGuard`) because `StartMove` is not safe to run twice. The recovery handler needs no list: `Recover` only acts from `Fault`, so every later delivery is a no-op that still answers yes. That is idempotent-by-precondition — at-least-once delivery in, exactly-one effect out. `Accepted: false` survives only as the defensive answer when `Recover` itself throws an unexpected `ShuttleRejectedException` (a real error, not a duplicate).

**What would change this:** if recovery becomes multi-step (for example, "home the shuttle to its start lane, then clear the fault"), the handler stops being a single guarded transition and needs message-id tracking or step identifiers again — a new record at that point.

---

## 6. Decision E: contract documents keep their section order (fold-in from review 19)

Review 19 noted, as a consider-level item, that `docs/contract/v4.md` reordered the `AlarmMessage`/`Alarm` sections relative to `v3.md` (they moved from before `MoveCommandMessage` to after `ShuttleStatus`), so a `diff v3.md v4.md` shows a 100-line block move on top of the real additions. No record ever captured that ordering choice.

**Decision E.** From now on: **a new contract version keeps the previous version's section order; new sections are appended just before "Changing this contract"; existing sections are never reordered or removed** (mark one deprecated in place if it must go). A version-to-version diff then shows only real changes — new kinds, changed fields — which is what a controls engineer reading the contract history needs.

Measured (2026-10-17): `grep -n "^## " docs/contract/v3.md` gives `MessageEnvelope, AlarmMessage, Alarm, MoveCommandMessage, CommandAckMessage, ShuttleStatusMessage, ShuttleStatus, Changing this contract`; `v4.md` gives `MessageEnvelope, MoveCommandMessage, CommandAckMessage, ShuttleStatusMessage, ShuttleStatus, AlarmMessage, Alarm, RecoveryCommandMessage, RecoveryAckMessage, Changing this contract`. The two Alarm sections moved. `v3.md` is untouched and stays frozen; the rule applies going forward, so `v5.md` copies `v4.md`'s order and appends.

Rejected: "most important first" or alphabetical order — both reshuffle every version and make diffs unreadable.

**What would change this:** if the contract ever outgrows one page per version, split into one page per message kind with an index; ordering then stops being the diff tool and this rule can retire.

---

## 7. Scope and limits

- No contract change: `ContractVersion.Current` stays `4`, `docs/contract/` is untouched. The kinds, topics, and shapes already exist.
- No `palletNotDetected`. It needs a load or unload command on the wire, which does not exist (`moveCommand` is the only move kind) — record 23 already predicted this; it is a later slice with its own record and almost certainly a contract bump.
- No release of occupancy after recovery. Record 17 and record 26 §5 keep the shuttle's physical position occupied; the traffic controller is not in the MQTT path.
- No mid-move recovery, no lost-communication recovery, no dashboard, no alarm-acknowledgement storage. The CLI *is* the operator for now.
- No change to `ReliableCommandSender`, `FleetServeLoop`, `AlarmEvaluator`, or the `move` path.

---

## 8. Verification (measured)

| Claim | How | Result |
|---|---|---|
| SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite | clean copy of `src` in `/tmp/opencode/l19base`; `dotnet build`, `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, 0 warnings, 0 errors; 8 `Passed!`, 0 `Failed!`, **139** tests (FleetManager 52, ShuttleCore 31, FleetCore 22, Messaging 10, Contracts 8, ShuttleSim 7, TrafficCore 6, MqttMessaging 3) |
| No recovery wiring exists | `grep -rn "HandleRecovery\|RecoverySender" src --include=*.cs` | no matches outside constants, topics, and tests |
| Endpoint handles move commands only | `src/ShuttleSim/ShuttleEndpoint.cs:38` | `envelope.MessageType != MessageTypes.MoveCommand` → return |
| Shuttle subscribes to one command topic | `src/ShuttleSim/Program.cs:95` | single `SubscribeAsync(topics.Command(shuttleId), MessageFamily.Command, ...)` |
| Manager CLI has `serve` and `move` only | `src/FleetManager/Program.cs:14-23` | two branches |
| Delivery-guarantee knobs are four | `src/MqttMessaging/MqttMessageBusOptions.cs` | `QosOptions(Command, Ack, Status, Alarm)`; `Recovery => _options.Qos.Command` at `MqttMessageBus.cs:103` |
| v4 ordering drift vs v3 | `grep -n "^## " docs/contract/v3.md docs/contract/v4.md` | Alarm sections moved after `ShuttleStatus` in v4 (table in §6) |

Not yet verified in code: the implementation itself. The owner builds it per lesson 19; this record states the design.

---

## 9. What this unblocks

- Lesson 19's assignment has no unguided product choices.
- M5's done-when proof gets its operator trigger: inject a blocked lane, watch the `blockedPath` alarm, run `FleetManager recover SH-01`, watch the shuttle return to idle.
- M7's dashboard has a sender to call and a CLI to compare against.
- The contract-doc ordering rule stops the next version bump from making a noisy diff.
