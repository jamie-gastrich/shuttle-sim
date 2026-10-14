# Decision Record 21: Alarms with severity — the v3 `alarm` kind, severity taxonomy, and rising-edge detection

**Date:** 2026-10-13
**Milestone:** M5 (Faults and recovery) — lesson 14: alarms with severity.
**Status:** Accepted. Append-only. Supersedes nothing. Links back to records 07, 08, 12, 17, 19, 20.
**Builds on:** [record 17](17-occupancy-and-fault-reservations.md) (faulted shuttles hold reservations; auto-release forbidden), [record 19](19-run-loop-wiring-and-concurrency.md) (`lock` discipline for shared fleet-side state), [record 20](20-liveness-heartbeats-and-timeouts.md) (heartbeats, `lastSeenAt`, timeout detection — this record turns "unresponsive" into a message), [record 07](07-message-contract-v2-envelope-and-topics.md) (envelope, topics, version bump rule), [record 08](08-mqtt-client-qos-and-configuration.md) (QoS from configuration), [record 12](12-task-blank-inputs-and-exception-types.md) (exception-type conventions).
**Replaced by:** a later record that changes the alarm kind, the severity set, the topic, or the edge-detection rule. Append-only.
**Wire contract:** **new version, `v3`.** Adds one message kind, `alarm`. `docs/contract/v2.md` stays exactly as it is; `docs/contract/v3.md` is added. `ContractVersion.Current` becomes `3`.

---

## 1. What we are deciding

Record 20 §8 said the next M5 lesson "can wire the fleet-side status path that calls `RecordSeen` and raise an alarm when `IsUnresponsive` turns true. Record 17 §4 already forbids auto-release on timeout; the alarm is knowledge, not action."

So lesson 13 gave the fleet manager a pure way to *know* a shuttle is unresponsive: `ShuttleHealth.IsUnresponsive(shuttleId, now)`. That knowledge is invisible. Nothing leaves the fleet manager. This lesson gives "unresponsive" a voice: an **alarm** — a message the fleet manager sends when a condition starts, so an operator (M7) and a log (M5) can see it.

This record decides four things:

**A.** How does an alarm travel? A dedicated `alarm` message kind, or an overload of the existing `shuttleStatus`?
**B.** Where does severity live, and what does it look like on the wire?
**C.** When is an alarm sent — on every evaluation where the condition holds, or only when the condition *starts*?
**D.** How much does the contract change now? The alarm kind only, or the alarm and recovery together?

The lesson that follows (15) wires the fleet manager's serve loop: it subscribes to statuses, calls `RecordSeen`, evaluates on a cadence, and publishes the alarm. This record fixes the shapes; the wiring is next.

---

## 2. Decision A: an alarm is a dedicated `alarm` message kind in contract v3

Today the contract carries three kinds: `moveCommand`, `commandAck`, and `shuttleStatus` (`docs/contract/v2.md`). A `shuttleStatus` already has a `FaultReason` field. It is tempting to overload it and skip a version bump.

| | A: a dedicated `alarm` kind (chosen) | B: reuse `shuttleStatus.FaultReason` | C: a `shuttleStatus` plus a `Severity` field |
|---|---|---|---|
| Says "this is an alarm" on the wire | **yes** — its own kind and topic | **no** — a consumer must sniff `FaultReason` | partly — one kind doing two jobs |
| Carries an alarm ever *without* a status snapshot | **yes** | no — every alarm drags a full status along | no |
| Edge vs level is expressible | **yes** — an alarm is an event at a moment | **no** — status is a level, always current | **no** — same problem |
| Cost | one kind, one topic, one version bump, `v3.md` | none now, but the first "raise" has nowhere to go | none now, but the status shape becomes conditional |
| A status consumer sees only statuses | **yes** | yes | **no** — it must now handle severity |

**Decision A: add a dedicated `alarm` kind, contract v3.** An alarm is a *different thing* from a status. A status is a level: "here is where I am right now." An alarm is an event: "this condition just started." Overloading `FaultReason` cannot say "just started" — a consumer sees the current level and cannot tell a fresh fault from an old one without extra state of its own. Rejected: reusing `shuttleStatus.FaultReason` (no event, and a lost link means no status at all — exactly when an alarm is most needed); a `Severity` field on the status (makes the common status case carry a field that is usually null, and still no event).

**The topic.** `shuttle/{id}/alarm`, following record 07's `{root}/{shuttleId}/{kind}` scheme. A per-shuttle subtree lets the dashboard filter one shuttle's alarms and mirrors `status`. The dashboard will also want all alarms, so the scheme gains `{root}/+/alarm` (like the existing `AllStatuses`) when the transport is wired in lesson 15.

**The QoS.** `Alarm` is `AtLeastOnce` (MQTT QoS 1), from configuration like the other families (record 08). An alarm must not be dropped; a duplicate is acceptable because the latch already stops repeated *sends*, and the dashboard can dedupe by `MessageId` if a retry slips through. QoS 2 (`ExactlyOnce`) is heavier and not worth it for an idempotent-by-`MessageId` event. This family key (`QosOptions.Alarm`) is added in the wiring lesson, not this one.

---

## 3. Decision B: severity is a field in the alarm body, as a wire string from a small constant set

Severity is the alarm's urgency: `info`, `warning`, or `critical`. The question is where it sits and how it is typed.

| | A: in the body, as a wire string (chosen) | B: on the envelope | C: a C# `enum` |
|---|---|---|---|
| Null on messages that are not alarms | **no** — it only appears where it means something | **yes** — `Severity` would be null on every command, ack, and status | n/a |
| A reader of any message sees a uniform envelope | **yes** — the envelope stays what record 07 defined | **no** — the envelope grows a field one kind uses | yes |
| Matches `ShuttleStatus.State` | **yes** — that is already a wire string with a `ToWireName()` | **no** | **no** — enums serialize to numbers by default |
| Rename safety | a constant change, one line | same | a rename renumbers the wire |

**Decision B: `Severity` is a field of the `Alarm` body, typed `string`, with legal values in `AlarmSeverities`.** The envelope is shared by every message (record 07); a field that only alarms use does not belong there. The status already crosses the wire with `State` as a string (`"idle"`, `"moving"`), so a severity string is the same pattern, not a new one. Rejected: an envelope field (pollutes every message); a C# `enum` (its JSON default is a number, and reordering or renaming a member silently changes the wire — the constant-string pattern is what this codebase already uses for `State`).

**The set is three, and it is closed.** `info`, `warning`, `critical`. Three is enough to route: `info` → log only, `warning` → dashboard highlight, `critical` → blocks work and needs attention. More levels invite arguments about whether something is "high" or "severe"; fewer cannot separate "worth noting" from "stop the line."

**The codes are a small closed set too.** `AlarmCodes` names the three faults the spec lists in M5: `lostCommunication`, `blockedPath`, `palletNotDetected`. They are defined now so manager and shuttle share one vocabulary, but only `lostCommunication` is *raised* in this slice.

**Whose alarms, now.** The fleet manager raises `lostCommunication`: it is the only party that can see silence (record 20). The shuttle will raise `blockedPath` and `palletNotDetected` as its own alarms when faults are injected — that is a later M5 slice. Naming all three codes now settles the vocabulary without building the other two paths.

**Configuration, not a literal.** The severity the manager attaches to `lostCommunication` comes from `FleetManager:LostCommunicationSeverity` (AGENTS.md: no literals). Its value in this project is `critical`: a lost shuttle cannot be commanded and holds its lane (record 17 §4), so it stops work. The evaluator takes the severity in its constructor; no file holds a literal severity.

---

## 4. Decision C: alarm on the rising edge only — a latch in the pure evaluator

The liveness rule (`IsUnresponsive`) is a **level**: it is true for as long as the shuttle is silent. If the manager published an alarm every time it evaluated that level, a shuttle silent for an hour would emit an alarm on every tick.

| | A: rising edge, latched (chosen) | B: emit on every evaluation | C: a level alarm plus a separate clear message |
|---|---|---|---|
| One alarm per fault episode | **yes** | **no** — one per tick, forever | yes |
| A consumer reads the truth from one message | **yes** — an alarm always means "just started" | no — the stream is noise | yes |
| Needs a clear message | **no** — the latch clears when the condition goes false | no | **yes** — a second kind, a second version bump |
| State to keep | one set of `(shuttleId, code)` pairs | none | one level map plus the clear path |

**Decision C: raise only on the rising edge.** A pure `AlarmEvaluator` keeps a latch: a set of `(shuttleId, code)` pairs that are currently raised. When the condition turns true and the pair is not latched, it returns one alarm and latches the pair. While the condition stays true and the pair is latched, it returns nothing. When the condition turns false, it clears the latch, so a later true is new again. Rejected: emit on every evaluation (a flood of duplicates — the operator cannot tell the first alarm from the ten-thousandth); a level alarm plus a clear message (a second kind and a second version bump, and a consumer that misses the clear is stuck "alarming" forever — edge semantics are more forgiving).

**The latch clears but sends nothing yet.** A cleared condition is not itself an event in this slice: no "alarm cleared" message, no recovery command. Clearing only re-arms the latch so the *next* fault raises again. Recovery that an operator acknowledges is a later M5 slice with its own shapes.

**One atomic operation, one lock.** The latch is shared state written from the run loop and read by tests and, later, by the status handler. `AlarmEvaluator` guards it with a `lock`, exactly like `ShuttleHealth` (record 20 §4) and `OccupancyMap` (record 19). Each public method takes the lock once and touches the set directly; no public method calls a locking sibling.

**It references `Contracts`, and that is still pure.** `ShuttleHealth` returned only primitives (`bool`, `DateTime?`), so lesson 13's purity gate forbade `Contracts` in that file. `AlarmEvaluator` is different: it returns the contract's `Alarm` record and uses `AlarmCodes` and `AlarmSeverities` for the wire names. `Contracts` is a plain data library with no package references and no I/O (`src/Contracts/Contracts.csproj` has none — verified). The purity bar this file must meet is "no clock, no ids, no I/O": no `DateTime.UtcNow`, no `Guid.NewGuid`, no `Task.Delay`, no `Thread.Sleep`, no `Npgsql`, no `MQTTnet`, no `Messaging`, no database. Referencing the shared validator-free contract types does not cross that bar.

---

## 5. Decision D: v3 adds the `alarm` kind only; recovery is a later version

| | A: v3 = `alarm` only (chosen) | B: v3 = `alarm` + recovery together | C: keep v2, overload status |
|---|---|---|---|
| Version bumps | one | one | none |
| Each shape is built and tested when its slice is | **yes** | **no** — recovery shapes would sit unused | n/a |
| Only additive vs v2 | **yes** — v2 messages stay valid | yes | yes |
| Answer to `docs/contract/v2.md`'s own rule | a record and a new page | same | **violates** the "no change without a record and bump" rule |

**Decision D: v3 carries the `alarm` kind and nothing else.** Record 20 §6 anticipated "the contract gains an alarm/recovery kind," but the spec's M5 order is faults, *then* alarms, *then* recovery and reassignment. Defining recovery shapes now would be work that sits unused until the recovery slice — exactly the throwaway work AGENTS.md forbids. Rejected: alarm and recovery in one bump (unused shapes waiting for a later slice); keeping v2 and overloading status (violates the contract's own rule and loses the event semantics from Decision A).

**Additive, and honest about compatibility.** v3 only *adds* a kind; every v2 message is still valid JSON and still has the same meaning. But the code today accepts only `ContractVersion == ContractVersion.Current` (`src/ShuttleSim/ShuttleEndpoint.cs:37`). Bumping `Current` to `3` means a v2 sender would be rejected by the endpoint. In this repo both sides bump together, so the suite stays green. A production system would accept a *range* of versions for a migration window; we do not build that now. This is a known sim/reality seam, written down rather than hidden.

---

## 6. The exact v3 shapes

```csharp
// src/Contracts/MessageTypes.cs (change)
public static class MessageTypes
{
    public const string MoveCommand = "moveCommand";
    public const string CommandAck = "commandAck";
    public const string ShuttleStatus = "shuttleStatus";
    public const string Alarm = "alarm";            // new
}

public static class ContractVersion
{
    public const int Current = 3;                   // was 2
}
```

```csharp
// src/Contracts/AlarmSeverities.cs (new)
public static class AlarmSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";
}
```

```csharp
// src/Contracts/AlarmCodes.cs (new)
public static class AlarmCodes
{
    public const string LostCommunication = "lostCommunication";
    public const string BlockedPath = "blockedPath";
    public const string PalletNotDetected = "palletNotDetected";
}
```

```csharp
// src/Contracts/Alarm.cs (new)
public sealed record Alarm(
    string ShuttleId,
    string Severity,
    string Code,
    string? Detail
);
```

```csharp
// src/Contracts/AlarmMessage.cs (new)
public sealed record AlarmMessage(MessageEnvelope Envelope, Alarm Alarm);
```

The wire shape, on `shuttle/{id}/alarm`:

```json
{
    "Envelope": {
        "ContractVersion": 3,
        "MessageType": "alarm",
        "MessageId": "5b2f0c1e-2a4d-4f0e-9c3a-7d1e6f8a0b21",
        "CorrelationId": null,
        "SenderId": "fleet-manager",
        "SentAt": "2026-10-13T12:00:00Z"
    },
    "Alarm": {
        "ShuttleId": "SH-02",
        "Severity": "critical",
        "Code": "lostCommunication",
        "Detail": null
    }
}
```

**The pure evaluator.**

```csharp
// src/FleetManager/AlarmEvaluator.cs (new)
using Contracts;

namespace FleetManager;

public sealed class AlarmEvaluator
{
    private readonly string _lostCommunicationSeverity;
    private readonly object _lock = new();
    private readonly HashSet<(string ShuttleId, string Code)> _raised = new();

    public AlarmEvaluator(string lostCommunicationSeverity)
    {
        if (lostCommunicationSeverity is not (AlarmSeverities.Info or AlarmSeverities.Warning or AlarmSeverities.Critical))
            throw new ArgumentOutOfRangeException(nameof(lostCommunicationSeverity));
        _lostCommunicationSeverity = lostCommunicationSeverity;
    }

    public IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication)
    {
        if (string.IsNullOrWhiteSpace(shuttleId))
            throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));

        lock (_lock)
        {
            var key = (shuttleId, AlarmCodes.LostCommunication);

            if (lostCommunication && _raised.Add(key))
                return new[] { new Alarm(shuttleId, _lostCommunicationSeverity, AlarmCodes.LostCommunication, null) };

            if (!lostCommunication)
                _raised.Remove(key);

            return Array.Empty<Alarm>();
        }
    }
}
```

The constructor validates the severity against the closed set and throws `ArgumentOutOfRangeException` (record 12's convention for a bad value of a valid type). The single public method takes the lock once; `HashSet.Add` returns `false` when the pair is already latched, so a held condition raises nothing.

---

## 7. Where the new code lives

- **`src/Contracts/`** — the wire vocabulary: `Alarm`, `AlarmMessage`, `AlarmSeverities`, `AlarmCodes`, the new `MessageTypes.Alarm`, and `ContractVersion.Current = 3`. Crosses the wire; both the manager and the shuttle compile against it.
- **`src/FleetManager/AlarmEvaluator.cs`** — the pure rising-edge decision. It lives in `FleetManager` next to `ShuttleHealth` and `OccupancyMap` for the same reason: fleet-side runtime state, not `FleetCore`. `FleetCore` stays the pure task core.
- **`src/FleetManager/appsettings.json`** — `FleetManager:LostCommunicationSeverity`. Read by the wiring in lesson 15; the evaluator takes it in its constructor.
- **`docs/contract/v3.md`** — the owner writes it: version 3, the `alarm` kind and its topic, the `Alarm` body table, the severity and code sets, and the JSON example. `docs/contract/v2.md` is not touched.

Not in this record: `MessageFamily.Alarm`, `TopicScheme.Alarm`/`AllAlarms`, and `QosOptions.Alarm` — transport plumbing for the publish path, added with the serve loop in lesson 15.

---

## 8. What would change these choices

- **The dashboard or WMS wants to subscribe to alarms** → the topic scheme and `AllAlarms` become load-bearing; revisit the filter shape, not the kind.
- **A fourth severity appears** (for example an `emergency`) → reopen Decision B; a closed set of three is a choice, not a law.
- **The shuttle starts reporting its own faults** (`blockedPath`, `palletNotDetected`) → the codes already exist; the question becomes whether the shuttle or the manager stamps the severity, and whether a shuttle alarm and a manager alarm share one kind. Revisit A/B with a new record.
- **Recovery and operator acknowledgement** → v4, or a further v3 kind the moment that slice is built. Reopen Decision D then.
- **Alarms must survive a fleet-manager restart** (a latch in memory is lost on restart) → the latch becomes persisted state. Reopen Decision C's storage location; the edge rule itself stays.
- **Version-range compatibility is needed** for a rolling upgrade → `ContractVersion` stops being a single `Current` and the endpoint accepts a range. This touches record 07 as well.

---

## 9. Verification table (measured)

Everything below was checked on **2026-10-13** by a command or a file read. The baseline suite ran against a clean copy of `src/` at `/tmp/opencode/l14base` (no `bin/`/`obj/` copied in). The toy ran at `/tmp/opencode/l14toy`. Both scratch directories were deleted afterwards. No file under `src/` was changed.

| Claim | How checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no DB | `/tmp/opencode/l14base`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; `4+25+8+6+18+2+6+21` = **90** tests; `Contracts.Tests` = **4**, `FleetManager.Tests` = **21** |
| The current contract is v2 with three kinds | read `docs/contract/v2.md` | `Version: 2`; `moveCommand`, `commandAck`, `shuttleStatus`; no `v3.md` |
| `ContractVersion.Current` is 2 today | read `src/Contracts/MessageTypes.cs` | `Current = 2`; three `MessageTypes` constants |
| No alarm/severity code exists yet | read `src/Contracts`, `src/FleetManager`, `src/Messaging` | no `Alarm`, `Severity`, or `AlarmEvaluator` type anywhere |
| `Contracts` is a pure data library | read `src/Contracts/Contracts.csproj` | no `PackageReference`; no I/O |
| `FleetManager` already references `Contracts` | read `src/FleetManager/FleetManager.csproj` | `ProjectReference` to `Contracts.csproj` |
| The status message pairs an envelope with a body | read `src/Contracts/ShuttleStatusMessage.cs` | `ShuttleStatusMessage(MessageEnvelope Envelope, ShuttleStatus Status)` |
| The status body uses a wire string for state | read `docs/contract/v2.md`, `src/Contracts/ShuttleStatus.cs` | `State` is `string`; values `idle`/`moving`/`loading`/`unloading`/`fault` |
| The envelope has no severity field | read `src/Contracts/MessageEnvelope.cs` | six fields; no severity |
| Simplest lock pattern to copy | read `src/FleetManager/ShuttleHealth.cs` | `object _lock`; one `lock` per public method; no nested public calls |
| Bumping `Current` touches only one stale test *name* | read `src/Messaging.Tests/MessageEnvelopesTests.cs` | `..._and_version_two` asserts `ContractVersion.Current`; the assertion holds at 3, only the name lies |
| `System.Text.Json` round-trips the new shape as PascalCase | `/tmp/opencode/l14toy`, `dotnet run` | `{"ShuttleId":"SH-02","Severity":"critical","Code":"lostCommunication","Detail":null}` |
| The toy shows the rising edge | `/tmp/opencode/l14toy`, `dotnet run` | `RAISE` at `00:00:00` (SH-03) and `00:00:06` (SH-02), `CLEAR` at `00:00:07` (SH-02); no repeat while latched; exit 0 |

Not yet verified: the `Alarm`/`AlarmMessage`/`AlarmSeverities`/`AlarmCodes` types, the version bump, `AlarmEvaluator`, the config key, `docs/contract/v3.md`, and the ten new tests — this assignment specifies them and the reviewer measures them.

---

## 10. What this record unblocks

- Lesson 14 can assign the v3 shapes, the pure `AlarmEvaluator`, the config key, and the tests, with no unguided product choices.
- Lesson 15 can wire the fleet-manager serve loop with a fixed target: subscribe to `+/status`, call `ShuttleHealth.RecordSeen`, evaluate on a cadence, and publish `AlarmMessage` on `shuttle/{id}/alarm` at `MessageFamily.Alarm` / `QosOptions.Alarm` (QoS 1). No shape is left to guess.
- The recovery slice gets its own version bump, with the edge rule and the code vocabulary already settled.
- M7's dashboard has a message to render with severity: `alarm` on `{root}/+/alarm`.
