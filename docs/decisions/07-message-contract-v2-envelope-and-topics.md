# Decision 07: The message contract v2 — envelope, message types, and topics

**Date:** 2026-10-09
**Milestone:** M2 (Message contract and transport) — the shape every M2 message shares, and the topic layout the broker routes on.
**Status:** Accepted. Written for lesson 05, which assigns the v2 contract and the first round trip.
**Supersedes:** none. Record 02 fixed the transport (MQTT + REST) and the broker (`eclipse-mosquitto:2.0`) and explicitly deferred *"topic names, delivery guarantees (QoS), retries, idempotency keys, … the C# client library"* to M2. This record takes up the topic names and the message shapes; record 08 takes the client and QoS; record 09 takes acknowledgement and idempotency. All three link back to record 02, as it asked.
**Replaced by:** any later record that changes the envelope, the topic layout, or a v2 message's fields. A wire change needs a new contract version; the replacement links here and the version goes up.

---

## 1. What we are deciding

M2's spec line: *"Define the commands, status messages, alarms, and recovery messages… Done when: commands, status messages … a round trip."* M1's contract (`v1.md`) has exactly one message, `ShuttleStatus`, with no envelope. This record decides:

1. the **envelope** every v2 message carries, and the version it declares;
2. the **round-trip messages** this lesson puts on the wire — a move command, its acknowledgement, and the status the shuttle reports;
3. the **topic scheme** and who subscribes to what.

Not decided here: alarms and recovery messages (they arrive with the alarm/recovery lesson later in M2, as **contract v3**); retries, ack timeouts and heartbeats (lesson 06, record 09); the MQTT client, QoS numbers and configuration layout (record 08); task assignment (M3).

---

## 2. Survey: how a message carries its metadata

Every message now needs *who sent it, when, which kind it is, and what it is a reply to*. Three shapes:

| | A: an `Envelope` record nested in every message (chosen) | B: the metadata fields flattened into every message | C: metadata only in the topic / transport headers |
|---|---|---|---|
| One place to add a field | the envelope type | every message type, every time | not expressible (MQTT has no reply-to header) |
| Reading the kind before the body | parse the envelope, then the body | parse the whole body first | the topic only — the body is unverifiable |
| Correlation (this ack is for *that* command) | a `CorrelationId` field | a field per message | MQTT has nothing to carry it |
| Contract document | one envelope table plus one table per message | one wide table per message | incomplete |

**Decision 1: a `MessageEnvelope` record nested as the `Envelope` property of every v2 message.**

```csharp
public sealed record MessageEnvelope(
    int ContractVersion,
    string MessageType,
    string MessageId,
    string? CorrelationId,
    string SenderId,
    DateTime SentAt);
```

`ContractVersion` is `2` for everything in this version. `MessageType` is the wire name (`"moveCommand"`, `"commandAck"`, `"shuttleStatus"`), declared as constants in `MessageTypes` so no string is typed twice. `MessageId` is a freshly generated GUID string per message. `CorrelationId` is `null` for a new message and set to the **originating command's `MessageId`** on an acknowledgement — that is what lets the fleet manager match a reply to the command it sent. `SenderId` is `"fleet-manager"` or the shuttle id. `SentAt` is a UTC `DateTime`, serialized by default `System.Text.Json` as ISO-8601 with `Z` (the same shape `ShuttleStatus.Timestamp` already uses).

The nested shape is chosen because it is the only one where a receiver can read *the kind and the version* before it commits to parsing a body, and the only one where a reply can point back at its cause without inventing a transport convention MQTT does not have.

---

## 3. Decision: the round-trip messages

Three records, in `Contracts`. The `ShuttleStatus` **payload is unchanged from v1** — v2 only wraps it in an envelope.

```csharp
public sealed record MoveCommandMessage(MessageEnvelope Envelope, string ShuttleId, string ToLane);
public sealed record CommandAckMessage(MessageEnvelope Envelope, bool Accepted, string? Reason);
public sealed record ShuttleStatusMessage(MessageEnvelope Envelope, ShuttleStatus Status);
```

- **`MoveCommandMessage`** — the fleet manager tells a shuttle to drive to `ToLane`. `ShuttleId` is the target; it must agree with the topic, and the shuttle checks it is the intended recipient. It carries no pallet id: loading and unloading are later; M2 moves the vehicle.
- **`CommandAckMessage`** — the shuttle's reply. `Accepted` says whether the command took effect; `Reason` is a human-readable cause when it did not (a rejected move carries the exception's message). It does **not** repeat the shuttle id: the envelope's `SenderId` is the shuttle, and `CorrelationId` is the command. `Ack` is deliberately a separate message kind, not a "status with a flag", because an acknowledgement answers a specific command and a status does not.
- **`ShuttleStatusMessage`** — wraps the existing `ShuttleStatus`. Keeping the v1 payload byte-for-byte means the M1 demo and its test stay green, and the manager's status parsing is the same type it already knows. The redundancy of a timestamp in both envelope and payload is accepted and named: the envelope timestamp is *when the message was sent*, the payload timestamp is *when the report was made* — they are equal in this sim and may not be later.

Wire names live in one place:

```csharp
public static class MessageTypes
{
    public const string MoveCommand = "moveCommand";
    public const string CommandAck = "commandAck";
    public const string ShuttleStatus = "shuttleStatus";
}

public static class ContractVersion
{
    public const int Current = 2;
}
```

---

## 4. Survey: the topic scheme

Topics are how MQTT routes. The contract's own skill asks for *"one topic branch per shuttle with separate sub-topics for commands, status, and alarms"* and *"no hard-coded topic strings"*.

| | A: `{root}/{shuttleId}/{kind}` (chosen) | B: `{root}/{kind}/{shuttleId}` | C: one flat topic per kind with the id in the body |
|---|---|---|---|
| Subscribe to one shuttle's commands | `{root}/SH-01/command` | `{root}/command/SH-01` | impossible without reading every body |
| The manager listens to all shuttles' acks | `{root}/+/ack` | `{root}/{kind}/+` | `{root}/ack` (all shuttles, unfilterable) |
| "Which shuttle is this?" | the second level | the third | only the body |
| Add an alarm later | `{root}/{id}/alarm` | `{root}/{kind}` | mixes every shuttle on one topic |

**Decision 2:** the scheme is **`{TopicRoot}/{shuttleId}/{kind}`**, with `kind` one of `command`, `ack`, `status`. The fleet manager subscribes to `{root}/+/ack` and `{root}/+/status`; a shuttle subscribes to its own `{root}/{shuttleId}/command` exactly. The `+` wildcard matches exactly one topic level — verified against a real Mosquitto 2.0 this session: `shuttle/+/ack` matched `shuttle/SH-01/ack` and did **not** match `shuttle/SH-01/ack/extra`.

Every topic string is built by one type, `TopicScheme`, from the configured root. No topic literal appears in decision code.

---

## 5. Rejected options, and why

- **Flattened metadata (survey B).** Every new envelope field would have to be added to every message type and every contract table; the first field someone forgets is a message the receiver cannot version. Rejected.
- **Metadata in the topic (survey C).** MQTT topics can carry a kind and a shuttle id, but not a message id, a correlation id, or a timestamp. The correlation an acknowledgement needs has nowhere to live. The topic still carries the kind and the shuttle — as a *routing* convenience, with the payload as the source of truth. Rejected as the sole carrier.
- **A single `Message` type with a `Kind` enum (not surveyed above).** Tempting for compactness, but it collapses three payload shapes into one and loses the compiler's help that `MoveCommandMessage` has a `ToLane` and `CommandAckMessage` has an `Accepted`. Rejected: the same reason record 06 chose an enum over free strings, one level up.

---

## 6. Verification table

Everything below was checked by running a command this session (2026-10-09). The JSON round trip and the two-step parse ran in `/tmp/opencode/m2-verify/Scratch`, a throwaway project; the broker checks ran against a throwaway `eclipse-mosquitto:2.0` container started from `/tmp/opencode/m2-verify/compose.yml`. Both are deleted with the scratch. Read-only commands were run against this repo.

| Claim | How checked | Result |
|---|---|---|
| The envelope and the three messages round-trip through `System.Text.Json` default options | serialize then deserialize `MoveCommandMessage`/`CommandAckMessage`/`ShuttleStatusMessage`; compare fields | fields survive, PascalCase names preserved |
| The receiver can read the kind before the body | parse the `Envelope`, switch on `MessageType`, then parse the concrete type | works for all three kinds |
| An acknowledgement can be matched to its command | set `CorrelationId` = the command's `MessageId`, compare on receive | matched |
| `shuttle/+/ack` matches one level only | subscribe `shuttle/+/ack`; publish to `shuttle/SH-01/ack` and `shuttle/SH-01/ack/extra` | first received, second not (count stayed 1) |
| `v1.md` is untouched by v2 | `grep -qE '^Version: 1$' docs/contract/v1.md` | exit 0; the owner writes `v2.md` |

Not yet verified:

- The owner's contract code and `docs/contract/v2.md` — this record is the specification for them.
- Alarm and recovery messages — contract v3, a later M2 lesson.
- Anything that needs two live processes: that is the lesson's Phase 5 round trip.

---

## 7. What would change this choice

- If the fleet grows past what a single `+` level expresses (per aisle, per zone), the topic scheme gains a level — a new record and a version bump.
- If message size matters (real embedded controllers), the envelope may be trimmed to fields the receiver uses; the sim has no such pressure.
- If alarm/recovery messages need metadata the envelope lacks (severity, operator id), that is a v3 envelope question; the envelope is where such a field goes.

---

## 8. What this record unblocks

- Lesson 05 can fix the exact record names, the wire names, and the topic shapes its acceptance greps for.
- The fleet manager knows how to subscribe (`{root}/+/ack`, `{root}/+/status`) and how to match an ack to a command (`CorrelationId`).
- Every later M2 message (alarm, recovery, heartbeat) is "the envelope plus a new body", with a new version to declare it.
