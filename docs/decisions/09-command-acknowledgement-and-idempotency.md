# Decision 09: Command acknowledgement and idempotency

**Date:** 2026-10-09
**Milestone:** M2 (Message contract and transport) — how a command is acknowledged, and how a repeated message is kept from running work twice.
**Status:** Accepted. Written for lesson 05, which assigns the round trip and the duplicate guard.
**Supersedes:** none. Record 07 fixed the envelope and the messages; this record fixes what the shuttle *does* when a command arrives, once, twice, or for someone else.
**Replaced by:** the reliability record (lesson 06) that adds **retries, the ack timeout, and the alarm after retries are exhausted**. That record links here; it adds the sender's half of the contract below, it does not change the receiver's.

---

## 1. What we are deciding

The message contract's reliability line: *"A command is acknowledged. If no acknowledgement arrives within a timeout, retry a limited number of times, then raise an alarm. Commands must be idempotent: receiving the same message id twice must not run the work twice. Track recent message ids."* This record decides the **receiving half**:

1. what an acknowledgement means and when it is sent;
2. what happens when the same command arrives twice (at-least-once delivery makes this real — verified on the broker in record 08);
3. what happens when a command is addressed to somebody else, or is malformed or a wrong version.

The **sending half** — resending an unacknowledged command, the timeout, the retry limit, the alarm when retries run out — is lesson 06, record 09's sequel. This record names the seam and stops.

---

## 1a. Survey: how the receiver avoids running work twice

| | A: a bounded cache of recent accepted message ids (chosen) | B: make every operation naturally idempotent | C: ignore duplicates; rely on QoS 2 |
|---|---|---|---|
| Fits this domain | yes — "move to lane X" repeated must report, not re-drive | partly: "move to X" is *nearly* idempotent, but "start a load" is not | no: QoS 2 is per link, not across a reconnect (record 08) |
| Memory bound | a size from config | none needed | none |
| Explains the concept | the cache *is* the lesson | hides the mechanism | hides it behind a setting |
| Works for the ack too | yes — a duplicate is re-acked | n/a | no |

**Decision: `IdempotencyGuard`, a bounded set of recently *accepted* command ids**, capacity from config. Two operations: `IsDuplicate(messageId)` and `MarkCompleted(messageId)`. On capacity overflow the **oldest** id is forgotten (first-in-first-out), because the oldest is the least likely to still be in flight. A duplicate arriving after its id has been evicted *will* run again — that is the honest cost of a bounded cache, and the bound is a configuration choice, not an accident.

---

## 2. Decision: when an acknowledgement is sent, and what it says

```csharp
public sealed record CommandAckMessage(MessageEnvelope Envelope, bool Accepted, string? Reason);
```

The shuttle sends exactly **one** ack per received command, with `CorrelationId` = the command's `MessageId`:

- **Accepted** — the command passed every check (version, kind, recipient, state) and took effect. `Reason` is `null`.
- **Not accepted** — a check failed (the move was rejected by the state machine, or the lane does not exist). `Reason` carries the cause; no work runs.

**The idempotency rule is asymmetric, and that is the decision.** A command is recorded in the guard **only when it was accepted**. Then:

- A duplicate of an **accepted** command is acknowledged again (`Accepted = true`) but **the work does not run a second time**. The manager that never got the first ack still learns the command took effect.
- A duplicate of a **rejected** command is **reprocessed**: it was never recorded, so it runs through the checks again and is rejected again (or accepted, if the world changed). A rejection is not a completed effect, so remembering it would wrongly suppress a retry after the obstruction cleared.

This is the whole of idempotency for M2: *the guard remembers effects, not messages.* The acceptance tests the two branches by name.

---

## 2a. Decision: validation before trust

The receiver validates before it acts, in this order: the envelope declares `ContractVersion.Current`; the `MessageType` is `moveCommand`; the `MoveCommandMessage.ShuttleId` is the shuttle's own id (a command for another shuttle is ignored silently — it arrived on this topic by a subscription mistake, not a fault). Only then does the command reach the state machine, and a `ShuttleRejectedException` becomes an acknowledgement with `Accepted = false`.

A malformed payload (unparseable JSON, a missing envelope) is dropped without an acknowledgement: the receiver cannot trust a message it cannot read enough to correlate, and inventing a reply to garbage would be a second concept. Lesson 06's timeout is how the *sender* notices a command that never got an ack.

---

## 3. Rejected options, and why

- **Acknowledge with a full status instead of a small ack.** Conflates "I received this command" (a link fact, needed fast) with "here is my state" (a periodic fact). A manager correlating acks would have to filter statuses. Rejected.
- **Record rejected commands too.** Would make a duplicate of a rejected command silently re-acknowledge "rejected" without re-checking — hiding the case where the obstruction has since cleared. Rejected; the asymmetry above is deliberate.
- **No bound on the id cache.** An unbounded set is a memory leak with a friendly name. Rejected; the size is configuration.
- **Deduplicate on the fleet-manager side only.** The shuttle is the thing that must not move twice; the dedupe belongs where the effect is. (The manager may *also* dedupe later for its own bookkeeping, but that is M3.)

---

## 4. Verification table

Everything below was checked this session (2026-10-09). The duplicate-delivery and wildcard facts came from a throwaway `eclipse-mosquitto:2.0`; the cache semantics were proven in a throwaway pure-logic reference in `/tmp/opencode/gates` (satisfiable, and mutation-sensitive — FIFO→LIFO, filter typo, and always-duplicate each turned a test red). Both are deleted with the scratch.

| Claim | How checked | Result |
|---|---|---|
| At-least-once delivery really produces duplicates | QoS-1 publish of one message twice; subscriber counts | received twice — dedupe is required, not theoretical |
| A one-level wildcard does not over-match | `shuttle/+/ack` vs `shuttle/SH-01/ack/extra` | deeper topic not received |
| A bounded recent-id cache is satisfiable and mutation-sensitive | reference `IdempotencyGuard`, 3 tests incl. FIFO eviction | 6/6 pass; FIFO→LIFO mutation caught |
| "Duplicate of an accepted command re-acks but does not re-run" is testable without a broker | reference endpoint driven by a fake bus (design) | by construction: the guard is checked before the state machine |

Not yet verified:

- The owner's `ShuttleEndpoint` and guard — this is the assignment.
- Retry, ack timeout, and the retry-exhausted alarm — lesson 06, by design.

---

## 5. What would change this choice

- If commands ever carry a sequence number with ordering guarantees, the cache could shrink to a high-water mark; the spec fixes no such scheme, so the id set stays.
- If the cache size turns out to matter under load, M8's load test measures it; the number is already configuration.
- If rejections should be remembered (a policy change), the asymmetry flips and this record is superseded.

---

## 6. What this record unblocks

- Lesson 05 fixes the ack semantics its acceptance checks: `commandAck` with `Accepted`, correlated by `MessageId`.
- Lesson 06 adds retry and ack timeout on top of a deterministic receiver: it can resend a command knowing a duplicate is safe.
- M3's task tracking can rely on "an accepted acknowledgement means the effect happened once".
