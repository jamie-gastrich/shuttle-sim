# Lesson 14: Alarms with severity, rising-edge detection, and contract v3

**Milestone:** M5 (Faults and recovery) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 13: Heartbeats, lastSeenAt, and the timeout window](13-heartbeats-and-timeouts.md) · **Decision:** [record 21](../decisions/21-alarms-with-severity.md); builds on [record 20](../decisions/20-liveness-heartbeats-and-timeouts.md), [record 19](../decisions/19-run-loop-wiring-and-concurrency.md), [record 17](../decisions/17-occupancy-and-fault-reservations.md), and [record 07](../decisions/07-message-contract-v2-envelope-and-topics.md)

**Concepts this assignment requires:**

- [ ] **The alarm message and severity** — a dedicated `alarm` wire kind with a `Severity` of `info`, `warning`, or `critical`, and a `Code` naming the fault. An alarm is an *event*, not a status. Taught in [The alarm message and severity](#1-the-alarm-message-and-severity). Fixed by [record 21 §2, §3](../decisions/21-alarms-with-severity.md).
- [ ] **Rising-edge detection (the latch)** — raise one alarm when a condition *starts*, stay quiet while it holds, clear the latch when it ends, and raise again next time. Taught in [Rising-edge detection](#2-rising-edge-detection-the-latch). Fixed by [record 21 §4](../decisions/21-alarms-with-severity.md).
- [ ] **Evolving the contract to v3** — a version bump adds a kind without rewriting the old page; `v2.md` stays, `v3.md` is added. Taught in [Evolving the contract to v3](#3-evolving-the-contract-to-v3). Fixed by [record 21 §5, §6](../decisions/21-alarms-with-severity.md).

Three new concepts. That is the cap.

Five things this assignment needs are **assignment requirements, not concepts**. They change where code lives, what it is named, and how we run it, not the ideas above:

- the v3 contract types in `src/Contracts/` (`Alarm`, `AlarmMessage`, `AlarmSeverities`, `AlarmCodes`, `MessageTypes.Alarm`, `ContractVersion.Current = 3`);
- a pure `AlarmEvaluator` class in `FleetManager` ([record 21 §4, §7](../decisions/21-alarms-with-severity.md));
- the `FleetManager:LostCommunicationSeverity` config key ([record 21 §3](../decisions/21-alarms-with-severity.md));
- `docs/contract/v3.md`, which **you** write (record 21 §7);
- a `lock` inside `AlarmEvaluator`, because the run loop writes the latch while tests read it ([record 21 §4](../decisions/21-alarms-with-severity.md)).

They are specified under [The assignment](#the-assignment).

**Not in this lesson:** wiring the fleet manager's serve loop, subscribing to statuses, publishing alarms, `MessageFamily.Alarm`, `TopicScheme.Alarm`, and `QosOptions.Alarm` (all lesson 15); shuttle-reported faults like `blockedPath` and `palletNotDetected` (a later M5 slice); operator-acknowledged recovery; reassigning a failed shuttle's work; restart reconciliation; auto-release on timeout (forbidden by record 17 §4); WMS orders (M6); the dashboard (M7).

---

## Where this fits

M5's spec line is: *"Heartbeats and timeouts. Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges."*

Lesson 13 built the first half of "lost communication": the shuttle proves it is alive, `ShuttleHealth` records `lastSeenAt`, and a pure rule answers "is this shuttle unresponsive?". That answer lives and dies inside the fleet manager. It is never sent anywhere.

This lesson gives the answer a voice. When a shuttle *becomes* unresponsive, the fleet manager raises an **alarm**: a small message with a severity, so an operator or a log can see it. We build the alarm's shape and the pure decision of *when* to raise it. Lesson 15 wires the loop that actually sends it.

```text
   built (lesson 13)                         this lesson (14)                    next (15)
   ─────────────────                         ────────────────                    ─────────
   ShuttleHealth.IsUnresponsive(id, now) ──► AlarmEvaluator.Evaluate(id, lost) ──► publish AlarmMessage
        a level: true while silent              an edge: one alarm when it              on shuttle/{id}/alarm
                                                starts, quiet while it holds            (MessageFamily.Alarm)
                                                     │
                                                     ▼
                                              Contracts v3:
                                              Alarm(ShuttleId, Severity, Code, Detail?)
```

The difference between a *level* and an *edge* is the heart of this lesson. A level is "is it true right now?". An edge is "did it *just* become true?". `IsUnresponsive` is a level. An alarm is an edge. If we sent an alarm on the level, a shuttle silent for an hour would raise an alarm on every single tick.

M4's done-when is met, and lesson 13 started M5's done-when — *"every injected fault leads to the right alarm and the right recovery"*. This lesson builds the "alarm" half of that sentence for one fault: lost communication. The "right recovery" half is later.

**The honest weak spot.** The sim has no real network. A real shuttle can be silent because of a dropped packet, a slow broker, or a Wi-Fi dead spot — a few seconds of silence that is not a fault. The latch will raise and then clear as the shuttle comes back, which is correct for us but noisy in a real plant; real systems add a debounce or a "confirm twice" rule and tune the timeout window. We do not. Also, a latch in memory is lost when the fleet manager restarts, so a shutdown-and-restart can re-raise an alarm that was already known. Both are written down in [record 21 §8](../decisions/21-alarms-with-severity.md) rather than hidden.

### No hard-coded values

AGENTS.md is flat: *"No hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration."* The severity the fleet manager attaches to lost communication is a value, so it comes from `FleetManager:LostCommunicationSeverity` — not from a literal inside `AlarmEvaluator`. The evaluator takes the severity in its constructor. Test values are test fixtures and stay in the tests. The wire names (`"alarm"`, `"critical"`, `"lostCommunication"`) are contract constants, not tunables — they are the agreed vocabulary, and they live in `src/Contracts/`.

---

## Verification table

Everything below was checked by a command or a file read on **2026-10-13**. The baseline suite ran against a clean copy of `src/` at `/tmp/opencode/l14base` (no `bin/` or `obj/` copied in). The toy ran at `/tmp/opencode/l14toy`. Both scratch directories were deleted afterwards. No file under `src/` was changed.

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no DB | `/tmp/opencode/l14base`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; **90** tests; `Contracts.Tests` = **4**, `FleetManager.Tests` = **21** |
| No alarm/severity code exists yet | read `src/Contracts`, `src/FleetManager`, `src/Messaging` | no `Alarm`, `Severity`, or `AlarmEvaluator` type |
| The contract is v2 with three kinds | read `docs/contract/v2.md` | `Version: 2`; `moveCommand`, `commandAck`, `shuttleStatus`; no `v3.md` |
| `ContractVersion.Current` is 2 today | read `src/Contracts/MessageTypes.cs` | `Current = 2` |
| `Contracts` is a pure data library | read `src/Contracts/Contracts.csproj` | no `PackageReference`; no I/O |
| `FleetManager` already references `Contracts` | read `src/FleetManager/FleetManager.csproj` | `ProjectReference` to `Contracts.csproj` |
| The status message is an envelope plus a body | read `src/Contracts/ShuttleStatusMessage.cs` | `ShuttleStatusMessage(MessageEnvelope Envelope, ShuttleStatus Status)` |
| The status body already uses a wire string | read `docs/contract/v2.md` | `State` is `string` (`idle`, `moving`, …) |
| The envelope has no severity field | read `src/Contracts/MessageEnvelope.cs` | six fields; no severity |
| The lock pattern to copy | read `src/FleetManager/ShuttleHealth.cs` | `object _lock`; one `lock` per public method |
| The endpoint accepts only the current version | read `src/ShuttleSim/ShuttleEndpoint.cs:37` | `envelope.ContractVersion != ContractVersion.Current` → reject |
| Only one test *name* mentions the old version | read `src/Messaging.Tests/MessageEnvelopesTests.cs` | `MessageEnvelopes_generate_a_unique_id_and_version_two` asserts `ContractVersion.Current`; the assertion holds, the name lies |
| `System.Text.Json` writes the new shape as PascalCase | `/tmp/opencode/l14toy`: `dotnet run` | `{"ShuttleId":"SH-02","Severity":"critical","Code":"lostCommunication","Detail":null}` |
| The toy shows the rising edge | `/tmp/opencode/l14toy`: `dotnet run` | `RAISE` SH-03 at `00:00:00`, SH-02 at `00:00:06`; `CLEAR` SH-02 at `00:00:07`; no repeat while latched; exit 0 |

Not yet verified: the v3 types, the version bump, `AlarmEvaluator`, the config key, `docs/contract/v3.md`, and the ten new tests — this assignment specifies them and the reviewer measures them.

---

## Options survey

The spec's learning method asks for options compared before a decision. There are four real choices in this slice.

### Survey 1: How does an alarm travel? (→ [record 21 §2](../decisions/21-alarms-with-severity.md))

| | A: a dedicated `alarm` kind (chosen) | B: reuse `shuttleStatus.FaultReason` | C: `shuttleStatus` plus a `Severity` field |
|---|---|---|---|
| Says "this is an alarm" on the wire | **yes** | **no** — sniff `FaultReason` | partly — one kind, two jobs |
| An alarm with no status snapshot | **yes** | no | no |
| Can express "it just started" | **yes** | **no** — a status is a level | **no** |
| Cost | one kind, one topic, one bump | none now, nowhere to raise later | a usually-null field on every status |

**Decision 1: a dedicated `alarm` kind.** An alarm is an event; a status is a level. Overloading a level cannot express "just started," and a lost link means no status at all — exactly when the alarm is needed. Rejected: reusing `FaultReason`; a `Severity` field on the status.

### Survey 2: Where does severity live, and what type is it? (→ [record 21 §3](../decisions/21-alarms-with-severity.md))

| | A: in the body, a wire string (chosen) | B: on the envelope | C: a C# `enum` |
|---|---|---|---|
| Null on non-alarm messages | **no** — only where it means something | **yes** — null on every command/ack/status | n/a |
| Envelope stays uniform | **yes** | **no** | yes |
| Matches `ShuttleStatus.State` | **yes** — already a wire string | **no** | **no** — serializes as a number |
| Rename safety | one constant | one constant | a rename renumbers the wire |

**Decision 2: `Severity` is a body field, typed `string`, with values in `AlarmSeverities`.** The envelope is shared by every message, so an alarm-only field does not belong there. The status already crosses the wire with `State` as a string; severity follows the same pattern. Rejected: an envelope field; a C# `enum` (numbers on the wire, rename-unsafe).

### Survey 3: When is an alarm sent? (→ [record 21 §4](../decisions/21-alarms-with-severity.md))

| | A: rising edge, latched (chosen) | B: every evaluation | C: level alarm + a clear message |
|---|---|---|---|
| One alarm per fault episode | **yes** | **no** — one per tick | yes |
| A consumer needs extra state | no | no | **yes** — misses the clear → stuck alarming |
| Extra kind / bump | **no** | no | **yes** |
| State kept | one latched set | none | a level map |

**Decision 3: raise only on the rising edge.** A latch remembers which `(shuttleId, code)` pairs are now raised. Rejected: an alarm on every evaluation (a flood); a level alarm plus a separate clear (another kind and a stuck-forever consumer).

### Survey 4: How much does v3 change? (→ [record 21 §5](../decisions/21-alarms-with-severity.md))

| | A: v3 = `alarm` only (chosen) | B: v3 = `alarm` + recovery | C: keep v2, overload status |
|---|---|---|---|
| Shapes built when their slice is | **yes** | **no** — unused until later | n/a |
| Additive vs v2 | **yes** | yes | yes |
| Obeys the contract's own bump rule | **yes** | yes | **no** |

**Decision 4: v3 adds only the `alarm` kind.** Recovery is a later slice; defining its shapes now is work that sits unused — throwaway work. Rejected: alarm and recovery in one bump; keeping v2.

---

## The lecture

Today we are going to learn about three things: **the alarm message and severity**, **rising-edge detection**, and **evolving the contract to v3**.

### 1) The alarm message and severity

An **alarm** is a message that says "a bad condition just started." It is small. It is not a snapshot of the shuttle.

Real-world picture: a warehouse smoke detector. It does not report "the room is currently fine" every second. It stays quiet. When smoke appears, it sounds once, loud. The single beep means "this just began." That is the difference between an alarm and a status.

A **status** is your forklift driver radioing "I am at aisle 3, carrying a pallet." They say it regularly, and it is a picture of right now. An **alarm** is the same driver hitting the emergency button. It happens once, at the moment something goes wrong.

Why do we need a separate kind for this? Because we need to say *when* a fault started, not just that it is present. Later, an operator will look at the dashboard and ask "is this a fresh problem or an old one I already know about?" A status cannot answer that. An event can.

Let's look at the shape. It mirrors the status message you already have: an envelope plus a body.

```csharp
// src/Contracts/AlarmMessage.cs (new)
public sealed record AlarmMessage(MessageEnvelope Envelope, Alarm Alarm);
```

That is the same shape as `ShuttleStatusMessage(MessageEnvelope Envelope, ShuttleStatus Status)`. The envelope is the shared wrapper every message has: version, kind, id, sender, time. The body is what makes an alarm an alarm.

Here is the body.

```csharp
// src/Contracts/Alarm.cs (new)
public sealed record Alarm(
    string ShuttleId,
    string Severity,
    string Code,
    string? Detail
);
```

Line by line:

- `ShuttleId` — which shuttle this is about. Named `ShuttleId`, not `SenderId`, because the fleet manager sends it *about* a shuttle. The envelope's `SenderId` is `fleet-manager`.
- `Severity` — how urgent: `info`, `warning`, or `critical`. More on this in a moment.
- `Code` — which fault, as a short stable name: `lostCommunication`, `blockedPath`, or `palletNotDetected`.
- `Detail` — a human sentence, or nothing (`null`). Optional because the code already says what is wrong.

Now the severity. It is a **closed set of three**. Closed means no other value is allowed.

```csharp
// src/Contracts/AlarmSeverities.cs (new)
public static class AlarmSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";
}
```

Why three? Because three is enough to decide what happens next. `info` goes to the log only. `warning` lights up the dashboard. `critical` stops work and needs a person. With four or five levels, people argue about whether something is "high" or "severe." With two, you cannot separate "worth noting" from "stop the line."

Why a `string` and not a C# `enum`? Two reasons. First, this is a *wire format* — it crosses the network to the shuttle and the dashboard. An `enum` serializes to a number by default, so `critical` would go out as `2`. If someone reorders the enum later, every stored or in-flight message silently changes meaning. Second, the codebase already does this: `ShuttleStatus.State` is a string on the wire (`"idle"`, `"moving"`), with the legal values kept as constants. Severity follows the same pattern instead of inventing a new one.

The codes are a closed set too, naming the faults the spec lists in M5.

```csharp
// src/Contracts/AlarmCodes.cs (new)
public static class AlarmCodes
{
    public const string LostCommunication = "lostCommunication";
    public const string BlockedPath = "blockedPath";
    public const string PalletNotDetected = "palletNotDetected";
}
```

We define all three now so the manager and the shuttle share one vocabulary. But in this slice only `lostCommunication` is actually *raised*. The fleet manager is the only party that can see silence. The shuttle will raise `blockedPath` and `palletNotDetected` when we inject those faults — a later slice. Naming them now avoids a second naming debate later.

**Where this is used later.** The wire name `"alarm"` goes into `MessageTypes.Alarm` and the topic `shuttle/{id}/alarm`. Lesson 15 publishes `AlarmMessage` on that topic. The dashboard (M7) subscribes to `{root}/+/alarm` and renders the severity.

Here is the whole shape on the wire. This is what the fleet manager will send.

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

Notice two things. `ContractVersion` is now **3** — we will come back to that in the third concept. And `Severity` sits in the `Alarm` body, not in the `Envelope`. The envelope is the same wrapper used by commands, acks, and statuses; putting an alarm-only field there would make it `null` on every one of those. Keep a shared field shared.

### 2) Rising-edge detection (the latch)

A **latch** is a memory of what is already raised.

The idea is a road with a pothole. A sign goes up when the pothole first appears and stays up while it is there. You do not put up a brand-new sign every morning. When the pothole is filled, the sign comes down. If the pothole appears again, a new sign goes up.

Why do we need this now? Because `IsUnresponsive` is a **level**: it returns `true` for as long as the shuttle is silent. The run loop evaluates it on a cadence — say, a few times a second. If we raised an alarm every time the level was `true`, a shuttle silent for an hour would send thousands of identical alarms. The operator could not tell the first from the latest. A flood hides the one message that matters.

So the rule is about the **edge**, the moment the condition changes from false to true. Here is the difference, side by side.

```text
  time ──────────────────────────────►
  condition:      F F F T T T T T F F F T T T
                  │       │           │     │
  LEVEL  B says:  . . . A A A A A . . . A A A      ← alarms on every true tick (noisy)
  EDGE  A says:   . . . A . . . . . . . A . .      ← one per episode (quiet)
```

We keep the latch in the pure evaluator. Here is the shape, with the body to come.

```csharp
// src/FleetManager/AlarmEvaluator.cs (new)
using Contracts;

namespace FleetManager;

public sealed class AlarmEvaluator
{
    private readonly string _lostCommunicationSeverity;
    private readonly object _lock = new();
    private readonly HashSet<(string ShuttleId, string Code)> _raised = new();
    // constructor and Evaluate below
}
```

Let's read the fields:

- `_lostCommunicationSeverity` — the severity to attach, handed in at construction. It comes from configuration, never a literal.
- `_lock` — guards the latch. The run loop writes it while tests (and later the status handler) read it. Same discipline as `ShuttleHealth` and `OccupancyMap`.
- `_raised` — the latch itself: a set of `(shuttleId, code)` pairs. A tuple is just a small bundle of values; here it bundles *which shuttle* with *which fault*. Why both? Because SH-01 being lost is a different alarm from SH-02 being lost, and the same shuttle could in principle have two different codes raised at once. The pair is the identity of one alarm.

The constructor validates the severity against the closed set and throws `ArgumentOutOfRangeException`, which is the convention this codebase uses for "right type, wrong value" (record 12).

```csharp
    public AlarmEvaluator(string lostCommunicationSeverity)
    {
        if (lostCommunicationSeverity is not (AlarmSeverities.Info or AlarmSeverities.Warning or AlarmSeverities.Critical))
            throw new ArgumentOutOfRangeException(nameof(lostCommunicationSeverity));
        _lostCommunicationSeverity = lostCommunicationSeverity;
    }
```

Now the one public method. It takes the shuttle id and the current level, and returns zero or one alarm.

```csharp
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
```

Line by line:

- The blank-id guard mirrors `OccupancyMap` and `ShuttleHealth`. A missing shuttle id is a caller bug, not a fleet state.
- `lock (_lock)` — one lock per public method. Everything inside is one atomic step: no other thread can see a half-updated latch.
- `var key = (shuttleId, AlarmCodes.LostCommunication);` — name the pair once, so the "raise" and "clear" branches cannot drift apart.
- `if (lostCommunication && _raised.Add(key))` — this is the rising edge, in one line. `Add` returns `true` only if the pair was *not* already there. So this branch runs only when the condition is true **and** the latch was empty for this pair.
- `return new[] { new Alarm(...) };` — one alarm, with the configured severity. The `Detail` is `null`; the code says enough.
- `if (!lostCommunication) _raised.Remove(key);` — the falling edge. It removes the pair so the *next* true is a fresh edge. It sends nothing: a cleared condition is not an event in this slice. No "all clear" message, no recovery. Clearing only re-arms.
- `return Array.Empty<Alarm>();` — the common case: nothing to say. An empty array means "no new alarm."

Picture the latch as a whiteboard with a list of raised alarms. `Add` succeeds only when you write a new line. The board already has the line, so you write nothing. When the condition clears, you wipe the line off. Next time, it is new again.

**Why the evaluator is pure, and how it can still name `Contracts`.** Lesson 13 told you `ShuttleHealth` must not reference `Contracts`. This file does. That is deliberate. `ShuttleHealth` returned `bool` and `DateTime?` — plain primitives — so it had no reason to touch the contract. `AlarmEvaluator` returns an `Alarm`, which *is* a contract type, and it uses `AlarmCodes`/`AlarmSeverities` for the wire names. `Contracts` is a plain data library: its project file has no packages and does no I/O (I read it). The purity bar is the same one that matters: **no clock, no ids, no I/O.** No `DateTime.UtcNow` (the caller passes the level, so the evaluator never reads a clock), no `Guid.NewGuid`, no `Task.Delay`, no `Thread.Sleep`, no `Npgsql`, no `MQTTnet`, no `Messaging`. That is what keeps it unit-testable with no broker and no database.

**Where this is used later.** Lesson 15 calls `Evaluate(shuttleId, lostCommunication)` once per shuttle per cadence tick, using `ShuttleHealth.IsUnresponsive` as the level input. Every returned `Alarm` becomes an `AlarmMessage` and is published. M7 renders it.

### 3) Evolving the contract to v3

The **contract** is the agreement about message shapes between the fleet manager and the shuttles. It is written down in `docs/contract/`, page per version.

Real-world picture: a parts catalog. When the warehouse adds a new part, you do not tear up the old catalog and rewrite every page. You print a new catalog with a new part number, and keep the old one on the shelf, because some suppliers still order from it.

Why does this matter now? We are about to change a message shape that both sides use. AGENTS.md says: *"The message contract is the source of truth... Changing it needs a decision record and a version bump."* Record 21 is that record. The version bump is the mechanical part, and it is small.

The rule the contract already states, in `docs/contract/v2.md`:

> Changing a message in this file needs a decision record in `docs/decisions/` and a version bump: the new shape gets a new page (`v3.md`) and `v2.md` stays, so a payload written for version 1 & 2 can still be read.

So there are two moves. First, bump the number in code.

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

Second, write the new page `docs/contract/v3.md` and leave `v2.md` exactly as it is.

Why keep `v2.md`? Because a message written under v2 still means what v2 said. If we edited `v2.md` in place, we would lose the only written record of what a v2 message meant. Two pages let a reader who has an old payload still understand it.

Now, an honest limit. *Additive* means v3 only adds a kind — every v2 message is still valid JSON with the same meaning. But the code today accepts only the exact current version:

```csharp
// src/ShuttleSim/ShuttleEndpoint.cs:37
envelope.ContractVersion != ContractVersion.Current ||
```

So when `Current` becomes `3`, a sender using `2` would be rejected. In this repo both sides bump together, so the tests stay green. A real plant would accept a *range* of versions during a rollout, so an old shuttle keeps working while new ones ship. We do not build that now; [record 21 §5](../decisions/21-alarms-with-severity.md) writes it down as a known seam. Saying this plainly is part of the design, not a hidden flaw.

**One more small thing.** A test name gets stale when the version moves. `src/Messaging.Tests/MessageEnvelopesTests.cs:8` is named `MessageEnvelopes_generate_a_unique_id_and_version_two`, but it asserts `ContractVersion.Current` — so it still passes at 3, it just lies about which version. Rename it. The count does not change; only the name.

**Where this is used later.** Lesson 15 publishes the first v3 message. The recovery slice adds its own kinds and gets its own version (or a further v3 kind). The dashboard reads `MessageType` to route.

---

## Worked example

This is a small, complete toy. It is **not** the assignment. It copies the two ideas — the wire shape and the latch — onto made-up types so you can watch the edge happen. It does not touch anything under `src/`.

Type in these two files in a scratch folder (outside the repo, for example `/tmp/opencode/l14toy`).

**`L14Toy.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

**`Program.cs`**

```csharp
// Worked-example toy for lesson 14: the shape of an alarm, and when to raise it.
// This is a TOY, clearly not the assignment. It does not touch anything under src/.
// It copies the ideas; it does not use the real Contracts or FleetManager types.

using System.Text.Json;

// ═══ Part 1: what one alarm looks like when it is sent ═══
var sample = new ToyAlarm("SH-02", "critical", "lostCommunication", null);
Console.WriteLine("=== Part 1: one alarm, as JSON on the wire ===");
Console.WriteLine(JsonSerializer.Serialize(sample));
Console.WriteLine();

// ═══ Part 2: raise a lost-communication alarm once; do not repeat it ═══
// A tiny stand-in for the fleet manager's watch loop. The clock and the
// "silent too long" test live here, in the caller. The latch only decides
// whether this is a new alarm worth sending.
var heartbeat = TimeSpan.FromSeconds(1);
var timeout = TimeSpan.FromSeconds(3);

var shuttles = new[] { "SH-01", "SH-02", "SH-03" };
var lastSeenAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
var latched = new HashSet<string>(StringComparer.Ordinal);
var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

Console.WriteLine("=== Part 2: the latch (raise once, stay quiet, clear, re-raise) ===");
Console.WriteLine($"heartbeat={heartbeat.TotalSeconds}s  timeout={timeout.TotalSeconds}s");
Console.WriteLine();

for (var tick = 0; tick < 10; tick++)
{
    // SH-01 talks every tick. SH-02 talks, then goes silent at tick 3, then
    // comes back at tick 7. SH-03 never talks at all.
    foreach (var shuttleId in shuttles)
    {
        var talks =
            shuttleId == "SH-01" ||
            (shuttleId == "SH-02" && (tick <= 2 || tick >= 7));

        if (talks)
            lastSeenAt[shuttleId] = clock; // stamp on receipt, with our clock
    }

    Console.WriteLine($"{clock:HH:mm:ss}  tick {tick}");

    foreach (var shuttleId in shuttles)
    {
        // The caller decides "is this shuttle silent too long?" — not the latch.
        var silent =
            !lastSeenAt.TryGetValue(shuttleId, out var seen) || clock - seen > timeout;

        if (silent && !latched.Contains(shuttleId))
        {
            // Rising edge: it just became silent, so send one alarm.
            latched.Add(shuttleId);
            var alarm = new ToyAlarm(shuttleId, "critical", "lostCommunication", null);
            Console.WriteLine($"  RAISE  {alarm.ShuttleId} {alarm.Code} severity={alarm.Severity}");
        }
        else if (!silent && latched.Contains(shuttleId))
        {
            // Falling edge: it is healthy again, so clear the latch.
            latched.Remove(shuttleId);
            Console.WriteLine($"  CLEAR  {shuttleId} lostCommunication");
        }
    }

    var active = latched.Count == 0
        ? "(none)"
        : string.Join(", ", latched.OrderBy(id => id, StringComparer.Ordinal));
    Console.WriteLine($"  still latched: {active}");

    clock += heartbeat;
}

// The toy's stand-in for the real Contracts.Alarm record.
record ToyAlarm(string ShuttleId, string Severity, string Code, string? Detail);
```

Run it:

```bash
dotnet run
```

Expected output (this is a real run from `/tmp/opencode/l14toy` on 2026-10-13):

```text
=== Part 1: one alarm, as JSON on the wire ===
{"ShuttleId":"SH-02","Severity":"critical","Code":"lostCommunication","Detail":null}

=== Part 2: the latch (raise once, stay quiet, clear, re-raise) ===
heartbeat=1s  timeout=3s

00:00:00  tick 0
  RAISE  SH-03 lostCommunication severity=critical
  still latched: SH-03
00:00:01  tick 1
  still latched: SH-03
00:00:02  tick 2
  still latched: SH-03
00:00:03  tick 3
  still latched: SH-03
00:00:04  tick 4
  still latched: SH-03
00:00:05  tick 5
  still latched: SH-03
00:00:06  tick 6
  RAISE  SH-02 lostCommunication severity=critical
  still latched: SH-02, SH-03
00:00:07  tick 7
  CLEAR  SH-02 lostCommunication
  still latched: SH-03
00:00:08  tick 8
  still latched: SH-03
00:00:09  tick 9
  still latched: SH-03
```

Read the output like an operator:

- **Part 1** is one alarm as JSON. `Severity` is `"critical"` and it sits *inside* the alarm body, next to `Code`. That is the shape you are about to put in `src/Contracts/`.
- **SH-03 never talks**, so it is silent from tick 0. The latch is empty, so it raises **once** at `00:00:00` and then stays quiet for every later tick — you can see `still latched: SH-03` with no new `RAISE` line. That is the whole point of the edge.
- **SH-02 talks until tick 2**, then goes quiet. It is not yet *past* the 3-second window, so it raises nothing at tick 3, 4, or 5. At `00:00:06`, `now - lastSeen` is 4 seconds, past the window, so it raises.
- **SH-02 comes back at tick 7.** The condition goes false, the latch clears (`CLEAR`), and `SH-02` leaves the board. Notice we did *not* send an "all clear" alarm — clearing only re-arms.
- The `RAISE` at `00:00:06` is the **rising edge** for SH-02, exactly the behavior `AlarmEvaluator.Evaluate` produces with `_raised.Add(key)`.

The toy inlines the latch so you can see it move. The real `AlarmEvaluator` does the same thing in one method, with the lock and the contract types. Map them:

| Toy | Real code |
|---|---|
| `latched.Contains(id)` then `latched.Add(id)` | `_raised.Add((shuttleId, code))` returning `true` |
| `latched.Remove(id)` | `_raised.Remove((shuttleId, code))` |
| `new ToyAlarm(id, "critical", "lostCommunication", null)` | `new Alarm(shuttleId, _lostCommunicationSeverity, AlarmCodes.LostCommunication, null)` |
| the `silent` test with `clock`/`timeout` | `ShuttleHealth.IsUnresponsive(shuttleId, now)`, computed by the caller |

The toy uses a made-up `ToyAlarm` record so it stays independent of the repo. Your job in the assignment is to build the real `Alarm` and the real evaluator, plus the contract page and the tests.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. Why is an alarm a **separate message kind** instead of a `FaultReason` on the existing `shuttleStatus`? What can an event say that a level cannot?
2. Where does `Severity` live — the envelope or the alarm body — and why? Why is it a `string` with constants instead of a C# `enum`?
3. `IsUnresponsive` is a level that stays `true` for a silent shuttle. Why would raising an alarm on that level be wrong, and what exactly does the latch store?
4. In `AlarmEvaluator.Evaluate`, why does the clear branch send nothing? What breaks if you never remove the pair from the latch?
5. Why does the contract get a new `v3.md` page instead of editing `v2.md`? The version bump is *additive* — so why can a real v2 sender still be rejected by our code?

---

## The assignment

Build M5's second slice: **alarms with severity**. The contract gains an `alarm` kind (v3). A pure fleet-side `AlarmEvaluator` turns the level ("this shuttle is unresponsive") into an edge (one alarm when it starts). You also write the `v3.md` page and rename one stale test.

This slice does **not** wire the fleet manager's serve loop, does not subscribe to statuses, and does not publish anything. Those are lesson 15. `AlarmEvaluator` is proven by unit tests, exactly as `ShuttleHealth` was last lesson.

### Requirements

1. **Add the v3 wire vocabulary: `src/Contracts/`.**
   - `src/Contracts/Alarm.cs`: `public sealed record Alarm(string ShuttleId, string Severity, string Code, string? Detail);`
   - `src/Contracts/AlarmMessage.cs`: `public sealed record AlarmMessage(MessageEnvelope Envelope, Alarm Alarm);`
   - `src/Contracts/AlarmSeverities.cs`: `public static class AlarmSeverities` with `public const string Info = "info";`, `Warning = "warning";`, `Critical = "critical";`
   - `src/Contracts/AlarmCodes.cs`: `public static class AlarmCodes` with `public const string LostCommunication = "lostCommunication";`, `BlockedPath = "blockedPath";`, `PalletNotDetected = "palletNotDetected";`
   - `src/Contracts/MessageTypes.cs`: add `public const string Alarm = "alarm";` and change `ContractVersion.Current` to `3`.
   - Do not touch `src/Contracts/MessageEnvelope.cs`, `ShuttleStatus.cs`, `ShuttleStatusMessage.cs`, `MoveCommandMessage.cs`, or `CommandAckMessage.cs`.

2. **Write the contract page: `docs/contract/v3.md`.**
   - Start with `Version: 3`.
   - Document the new kind: a table row `AlarmMessage` | wire name `alarm` | topic kind segment `alarm`, alongside the three kinds carried over from v2.
   - Document `AlarmMessage` (fields `Envelope`, `Alarm`) and `Alarm` (fields `ShuttleId`, `Severity`, `Code`, `Detail`, with types and required-ness), following the table style in `v2.md`.
   - List the allowed `Severity` values (`info`, `warning`, `critical`) and the known `Code` values (`lostCommunication`, `blockedPath`, `palletNotDetected`).
   - Include one JSON example of an `alarm` message with `ContractVersion: 3`, `MessageType: "alarm"`, and a `Severity` of `critical`.
   - State the topic the alarm travels on: `{root}/{shuttleId}/alarm`.
   - Do **not** edit `docs/contract/v2.md`. It stays exactly as it is.

3. **Add the pure evaluator: `src/FleetManager/AlarmEvaluator.cs`.**
   - `public sealed class AlarmEvaluator` with constructor `AlarmEvaluator(string lostCommunicationSeverity)`.
   - The constructor throws `ArgumentOutOfRangeException` when the severity is not one of `AlarmSeverities.Info`, `Warning`, or `Critical`.
   - `public IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication)` — returns the one alarm when `lostCommunication` turns from false to true for `(shuttleId, AlarmCodes.LostCommunication)`, and an empty list otherwise. While the condition stays true it returns an empty list. When the condition turns false it forgets the pair (so a later true raises again).
   - Guard the latch with `private readonly object _lock = new();` and `lock (_lock)` in the public method. Each public method takes the lock once and touches the set directly; no public method calls a locking sibling.
   - Validate a blank `shuttleId` (throw `ArgumentException`), like `OccupancyMap` and `ShuttleHealth`.
   - The alarm's `Severity` is the constructor's severity; its `Code` is `AlarmCodes.LostCommunication`; its `Detail` is `null`.
   - The file must contain **no** `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, or `Messaging`. It **may** reference `Contracts` (that is the one deliberate difference from `ShuttleHealth`).

4. **Configuration, no literals.**
   - `src/FleetManager/appsettings.json`: add `"LostCommunicationSeverity": "critical"` under `FleetManager`. It is read by the wiring in lesson 15; adding the key now keeps the severity out of code.

5. **Add the contract tests: `src/Contracts.Tests/AlarmMessageTests.cs`.** Three tests, with these exact names:
   - `AlarmMessage_survives_a_json_roundtrip` — serialize an `AlarmMessage` with `System.Text.Json`, deserialize it, assert equality.
   - `Alarm_json_keeps_the_agreed_field_names` — the JSON contains `"Envelope"`, `"ShuttleId"`, `"Severity"`, `"Code"`, and `"Detail"`.
   - `Alarm_wire_names_match_the_contract` — `ContractVersion.Current` is `3`; `MessageTypes.Alarm` is `"alarm"`; the three `AlarmSeverities` values and the three `AlarmCodes` values are the agreed strings.
   - These tests are **pure**: no `Integration` trait, no database, no broker.

6. **Add the evaluator tests: `src/FleetManager.Tests/AlarmEvaluatorTests.cs`.** Seven tests, with these exact names:
   - `A_new_lost_communication_condition_raises_one_alarm`
   - `A_condition_that_stays_true_does_not_raise_again`
   - `A_cleared_condition_can_raise_again`
   - `A_healthy_shuttle_raises_nothing`
   - `Two_shuttles_are_latched_independently`
   - `The_configured_severity_is_carried_on_the_alarm`
   - `An_unknown_severity_is_rejected`
   - These tests are **pure**: no `Integration` trait, no database, no broker.

7. **Rename the stale test in `src/Messaging.Tests/MessageEnvelopesTests.cs`.**
   - Rename `MessageEnvelopes_generate_a_unique_id_and_version_two` to `MessageEnvelopes_generate_a_unique_id_and_the_current_version`.
   - Change nothing else in the file — the assertions already use `ContractVersion.Current`. The count does not change.

8. **Do not touch these:** `src/ShuttleCore/`, `src/TrafficCore/`, `src/FleetCore/`, `src/FleetManager/ShuttleHealth.cs`, `src/FleetManager/TaskDispatcher.cs`, `src/FleetManager/Program.cs`, `docs/contract/v2.md`, `docker/Dockerfile.build`.

9. **M5 later items are not present.** No serve loop in `FleetManager/Program.cs`. No subscription to statuses. No publishing. No `MessageFamily.Alarm`, no `TopicScheme.Alarm`, no `QosOptions.Alarm`. No shuttle-reported faults (`blockedPath`, `palletNotDetected`) raised anywhere. No recovery, no acknowledgement, no reassignment, no restart reconciliation, no `Release` on timeout. `AlarmEvaluator` is in-memory only.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are asserted, not admired. Read every line's purpose before you run it.

```bash
# Phase 1 — the new pieces exist, with the pinned names
test -f src/Contracts/Alarm.cs || echo "missing Alarm.cs"
test -f src/Contracts/AlarmMessage.cs || echo "missing AlarmMessage.cs"
test -f src/Contracts/AlarmSeverities.cs || echo "missing AlarmSeverities.cs"
test -f src/Contracts/AlarmCodes.cs || echo "missing AlarmCodes.cs"
test -f src/FleetManager/AlarmEvaluator.cs || echo "missing AlarmEvaluator.cs"
test -f docs/contract/v3.md || echo "missing contract v3.md"
grep -qE 'record Alarm\(' src/Contracts/Alarm.cs
grep -qE 'record AlarmMessage\(' src/Contracts/AlarmMessage.cs
grep -qE 'class AlarmEvaluator' src/FleetManager/AlarmEvaluator.cs
grep -qE 'IReadOnlyList<Alarm> Evaluate\(string shuttleId, bool lostCommunication\)' src/FleetManager/AlarmEvaluator.cs
```

```bash
# Phase 2 — the v3 wire names and the version bump
grep -qE '"alarm"' src/Contracts/MessageTypes.cs
grep -qE 'Current = 3' src/Contracts/MessageTypes.cs
grep -qE '"info"' src/Contracts/AlarmSeverities.cs
grep -qE '"warning"' src/Contracts/AlarmSeverities.cs
grep -qE '"critical"' src/Contracts/AlarmSeverities.cs
grep -qE '"lostCommunication"' src/Contracts/AlarmCodes.cs
grep -qE '"blockedPath"' src/Contracts/AlarmCodes.cs
grep -qE '"palletNotDetected"' src/Contracts/AlarmCodes.cs
```

```bash
# Phase 3 — purity: no clock, no ids, no I/O in the new pure file.
# It MAY reference Contracts (deliberate). It must NOT reference I/O or a broker.
grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' src/FleetManager/AlarmEvaluator.cs   # nothing
grep -rniE 'Npgsql|MQTTnet|Messaging|Postgres' src/FleetManager/AlarmEvaluator.cs                       # nothing
grep -rnE '"A[0-9]{2}-L[0-9]{2}' src/FleetManager/AlarmEvaluator.cs                                     # nothing
grep -qE 'using Contracts;' src/FleetManager/AlarmEvaluator.cs && echo "OK: evaluator names the contract"
```

```bash
# Phase 4 — the latch and the lock are really there
grep -qE 'private readonly object _lock' src/FleetManager/AlarmEvaluator.cs
grep -qE 'lock *\(' src/FleetManager/AlarmEvaluator.cs || echo "FAIL: AlarmEvaluator must guard its latch"
grep -qE 'HashSet<\(' src/FleetManager/AlarmEvaluator.cs || echo "FAIL: the latch must key on (shuttleId, code)"
```

```bash
# Phase 5 — configuration carries the severity, no literal
grep -qE 'LostCommunicationSeverity' src/FleetManager/appsettings.json
grep -qE '"critical"' src/FleetManager/appsettings.json
```

```bash
# Phase 6 — the contract page
grep -qE '^Version: 3$' docs/contract/v3.md
grep -qE 'alarm' docs/contract/v3.md
grep -qE 'shuttle/\{shuttleId\}/alarm|\{root\}/\{shuttleId\}/alarm' docs/contract/v3.md
grep -qE 'critical' docs/contract/v3.md
grep -qE 'lostCommunication' docs/contract/v3.md
for k in moveCommand commandAck shuttleStatus; do grep -qE "\`$k\`" docs/contract/v3.md || echo "v3 missing carried-over kind: $k"; done
```

```bash
# Phase 7 — build, and the pure suite with NO database needed (the container's command).
# The counts are gated, not admired.
cd src
dotnet build
T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed!|Failed!' "$T"
P=$(grep -c 'Passed!' "$T"); F=$(grep -c 'Failed!' "$T")
TOTAL=$(grep -oE 'Passed: +[0-9]+' "$T" | grep -oE '[0-9]+' | awk '{s+=$1} END{print s}')
echo "assemblies-passed=$P assemblies-failed=$F total=$TOTAL"
[ "$RC" -eq 0 ] && [ "$P" -eq 8 ] && [ "$F" -eq 0 ] && [ "$TOTAL" -eq 100 ] \
  && echo "suite gate OK" || echo "FAIL: expected RC 0, 8 Passed!, 0 Failed!, 100 tests"
grep -E 'Passed: +7,.*Contracts\.Tests\.dll' "$T" && echo "Contracts.Tests=7 OK" || echo "FAIL: Contracts.Tests should show 7"
grep -E 'Passed: +28,.*FleetManager\.Tests\.dll' "$T" && echo "FleetManager.Tests=28 OK" || echo "FAIL: FleetManager.Tests should show 28"
rm -f "$T"

# Phase 7b — the new tests are really there, by anchored name
L=$(mktemp); dotnet test ShuttleFleet.slnx --list-tests > "$L" 2>&1
for t in AlarmMessage_survives_a_json_roundtrip Alarm_json_keeps_the_agreed_field_names Alarm_wire_names_match_the_contract A_new_lost_communication_condition_raises_one_alarm A_condition_that_stays_true_does_not_raise_again A_cleared_condition_can_raise_again A_healthy_shuttle_raises_nothing Two_shuttles_are_latched_independently The_configured_severity_is_carried_on_the_alarm An_unknown_severity_is_rejected MessageEnvelopes_generate_a_unique_id_and_the_current_version; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                                             # nothing
grep -qE '\.MessageEnvelopes_generate_a_unique_id_and_version_two$' "$L" && echo "FAIL: stale test name still present"
rm -f "$L"
cd ..
```

```bash
# Phase 8 — no test launches docker
grep -rniE 'docker' src/*.Tests --include='*.cs' && echo "FAIL: docker referenced in a test" || echo "OK: no docker in tests"
```

```bash
# Phase 9 — protected files unchanged; v2 stays v2; no later-M5 scope
git diff --quiet HEAD -- src/ShuttleCore src/TrafficCore src/FleetCore src/FleetManager/ShuttleHealth.cs src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs docs/contract/v2.md docker/Dockerfile.build \
  && echo "OK: core, ShuttleHealth, dispatcher, FleetManager Program, v2 page and Dockerfile unchanged" \
  || echo "FAIL: a protected file changed"
grep -qE '^Version: 2$' docs/contract/v2.md
git diff --quiet HEAD -- src/Contracts/MessageEnvelope.cs src/Contracts/ShuttleStatus.cs src/Contracts/ShuttleStatusMessage.cs src/Contracts/MoveCommandMessage.cs src/Contracts/CommandAckMessage.cs \
  && echo "OK: the carried-over contract shapes are unchanged" \
  || echo "FAIL: a carried-over contract shape changed"
grep -rniE 'MessageFamily\.Alarm|TopicScheme\.Alarm|QosOptions\.Alarm|blockedPath|palletNotDetected' src --include='*.cs' \
  | grep -vE 'src/Contracts/AlarmCodes\.cs' || true
```

**Proof details.** The assignment adds four product files (`Alarm.cs`, `AlarmMessage.cs`, `AlarmSeverities.cs`, `AlarmCodes.cs`), adds one product file (`AlarmEvaluator.cs`), edits two product files (`MessageTypes.cs` gains a constant and the bump; `appsettings.json` gains a key), adds the contract page `docs/contract/v3.md`, adds two test files (three + seven tests), and renames one test. The pure test total goes from **90 to 100**: `Contracts.Tests` gains three (4 → 7), `FleetManager.Tests` gains seven (21 → 28). `Messaging.Tests` stays 8 (one rename). The gate is `RC 0`, 8 `Passed!`, 0 `Failed!`, 100 total.

What the reviewer will mutate to prove the tests are load-bearing:
- remove the `_raised.Add(key)` guard (raise whenever `lostCommunication` is true) → `A_condition_that_stays_true_does_not_raise_again` goes red;
- remove the `_raised.Remove(key)` clear branch → `A_cleared_condition_can_raise_again` goes red;
- key the latch on the shuttle id only instead of `(shuttleId, code)` → no test goes red today (one code), so note this as a deliberate forward-proof, not a load-bearing mutation;
- return the latch's emptied copy on every true → `A_new_lost_communication_condition_raises_one_alarm` goes red;
- hard-code the severity instead of using the constructor's value → `The_configured_severity_is_carried_on_the_alarm` goes red;
- drop the severity validation → `An_unknown_severity_is_rejected` goes red;
- make the `Alarm` record's `Severity` land in the wrong JSON field → `Alarm_json_keeps_the_agreed_field_names` goes red.

### Stretch goal

Make the latch a *forward-proof* for more than one code. Change `Evaluate` to accept the `code` as a parameter as well as the level — for example `Evaluate(string shuttleId, string code, bool active)` — and add a test that raises `lostCommunication` and `blockedPath` for the same shuttle independently, then clears one and shows the other stays latched. Comment that `blockedPath` will be raised by the shuttle, not the manager, so this signature is a rehearsal, not the final shape.

### Not in this assignment

- **Wiring the fleet manager's serve loop** — lesson 15. Today no code subscribes to statuses or publishes an alarm.
- **`MessageFamily.Alarm`, `TopicScheme.Alarm`, `AllAlarms`, `QosOptions.Alarm`** — lesson 15, with the publish path.
- **Shuttle-reported faults** (`blockedPath`, `palletNotDetected`) — a later M5 slice. The codes exist; nothing raises them.
- **Operator-acknowledged recovery and reassignment** — later M5 lessons. A cleared latch sends nothing.
- **Restart reconciliation** — later M5; the latch is in-memory only.
- **Auto-release on timeout** — forbidden (record 17 §4).
- **WMS orders** — M6. **Dashboard** — M7.
- **Version-range compatibility** (accepting v2 and v3 at once) — not built. The endpoint accepts only `ContractVersion.Current`.

---

## Closing

Read the spec's M5 line once more: *"Heartbeats and timeouts. Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges."*

Lesson 13 made "lost communication" **detectable**. This lesson makes it **sayable**. The contract gains an `alarm` kind at v3, with a severity and a code. A pure `AlarmEvaluator` turns the level ("this shuttle is silent") into an edge (one alarm when it starts, silence while it holds, clear and re-raise). We did not wire the send loop, and we did not build recovery — those are the next slices.

Before you call this done, be ready to explain without notes:

- why an alarm is a kind of its own and not a `FaultReason` on a status;
- why `Severity` is a body field and a wire `string`, not an envelope field or an `enum`;
- why the evaluator raises on the edge and what the latch stores, down to the `(shuttleId, code)` pair;
- why the clear branch sends nothing and still matters;
- why v3 adds a page instead of editing v2, and why an additive bump can still reject an old sender.

When the reviewer passes this, M5 has its alarm vocabulary: one fault (lost communication) raises a severity-tagged event, the contract is honestly at v3, and the shapes are frozen for lesson 15 to publish.
