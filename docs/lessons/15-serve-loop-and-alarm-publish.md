# Lesson 15: The serve loop and the alarm publish path

**Milestone:** M5 (Faults and recovery) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 14: Alarms with severity, rising-edge detection, and contract v3](14-alarms-with-severity.md) · **Decision:** [record 22](../decisions/22-serve-loop-and-alarm-publish.md); builds on [record 21](../decisions/21-alarms-with-severity.md), [record 20](../decisions/20-liveness-heartbeats-and-timeouts.md), [record 19](../decisions/19-run-loop-wiring-and-concurrency.md), [record 08](../decisions/08-mqtt-client-qos-and-configuration.md), and [record 07](../decisions/07-message-contract-v2-envelope-and-topics.md)

**Concepts this assignment requires:**

- [ ] **The serve loop (a testable I/O shell)** — a long-running loop that subscribes to statuses, evaluates on a cadence, and publishes; it depends on `IMessageBus`, not `MqttMessageBus`, and takes its clock as a function. Taught in [The serve loop](#1-the-serve-loop-a-testable-io-shell). Fixed by [record 22 §2, §4, §6, §8](../decisions/22-serve-loop-and-alarm-publish.md).
- [ ] **The known-shuttle set (who to evaluate)** — the loop evaluates the ids it has heard on `+/status`, so a recovered shuttle is still evaluated and its alarm latch clears; a shuttle that never spoke is invisible. Taught in [The known-shuttle set](#2-the-known-shuttle-set-who-to-evaluate). Fixed by [record 22 §3](../decisions/22-serve-loop-and-alarm-publish.md).
- [ ] **The alarm transport lane (family, topic, QoS)** — a `MessageFamily` is a route plus a QoS binding, so a new kind needs a family value, a topic branch, a QoS value, and a bus switch arm — and no contract bump. Taught in [The alarm transport lane](#3-the-alarm-transport-lane). Fixed by [record 22 §5](../decisions/22-serve-loop-and-alarm-publish.md).

Three new concepts. That is the cap.

Nine things this assignment needs are **assignment requirements, not concepts**. They change where code lives, what it is named, and how we run it, not the ideas above:

- the new `FleetServeLoop` class and its constructor validation ([record 22 §2, §8](../decisions/22-serve-loop-and-alarm-publish.md));
- the `Alarm` QoS value in both `appsettings.json` files and the evaluation interval in the manager's ([record 22 §8](../decisions/22-serve-loop-and-alarm-publish.md));
- the `serve` command in `src/FleetManager/Program.cs` ([record 22 §8](../decisions/22-serve-loop-and-alarm-publish.md));
- the recording fake and the seven pure loop tests ([record 22 §6, §9, §11](../decisions/22-serve-loop-and-alarm-publish.md));
- the real-broker integration test ([record 22 §6, §9](../decisions/22-serve-loop-and-alarm-publish.md));
- the one `TopicScheme` test ([record 22 §9](../decisions/22-serve-loop-and-alarm-publish.md));
- the `FleetManager.Tests` project reference to `MqttMessaging` ([record 22 §9](../decisions/22-serve-loop-and-alarm-publish.md));
- updating the two `QosOptions` call sites in `MqttMessaging.Tests` ([record 22 §11](../decisions/22-serve-loop-and-alarm-publish.md));
- the recording fake's name, which must not collide with the existing `FakeBus` ([record 22 §11](../decisions/22-serve-loop-and-alarm-publish.md)).

They are specified under [The assignment](#the-assignment).

**Not in this lesson:** operator-acknowledged recovery, reassigning a failed shuttle's work, restart reconciliation, and auto-release on timeout (all later M5 or forbidden by record 17 §4); shuttle-reported faults like `blockedPath` and `palletNotDetected` (a later M5 slice); a configured shuttle roster or evaluating a never-seen shuttle ([record 22 §3, §10](../decisions/22-serve-loop-and-alarm-publish.md)); persisting the known set or the latch (in-memory only); WMS orders (M6); the dashboard (M7); and any wire-contract change — `alarm` is already in v3, so `ContractVersion.Current` stays `3` and `docs/contract/` is untouched.

---

## Where this fits

M5's spec line is: *"Heartbeats and timeouts. Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges."*

Lesson 13 made "lost communication" **detectable**: `ShuttleHealth.IsUnresponsive(shuttleId, now)` answers the question. Lesson 14 made it **sayable**: `AlarmEvaluator.Evaluate(shuttleId, lost)` turns the level into one alarm per rising edge, and contract v3 defines the `alarm` message.

But nothing in the running fleet manager ever calls those two objects. `src/FleetManager/Program.cs` today does one thing: a one-shot `move` round trip. The knowledge exists, and the words exist, but no process in the fleet manager ever speaks them. This lesson builds **the serve loop** — the long-running part of the manager that listens, decides, and publishes.

```text
   built (lessons 13 + 14)                     this lesson (15)                          later
   ───────────────────────                     ────────────────                          ─────
   shuttle heartbeat ──►  +/status  ──►  HandleStatusAsync
                                           │  _health.RecordSeen(id, _clock())
                                           │  _knownShuttles.Add(id)   [under lock]
                                           ▼
                                    (every AlarmEvaluationIntervalMs)
                                           │
                             EvaluateAndPublishAsync(now):
                             for each known id:
                               lost = _health.IsUnresponsive(id, now)
                               for alarm in _alarmEvaluator.Evaluate(id, lost):
                                           │
                                           ▼
                                  PublishAlarmAsync(alarm, now)
                                  AlarmMessage on shuttle/{id}/alarm
                                  MessageFamily.Alarm  (QoS 1)  ──►  M7 dashboard: {root}/+/alarm
```

M5's done-when is *"every injected fault leads to the right alarm and the right recovery"*. This lesson finishes the "right alarm" half for one fault: a heartbeat stops, the loop notices on its next tick, and an `AlarmMessage` leaves the manager on `shuttle/{id}/alarm`. The "right recovery" half is still later.

**The honest weak spots**, written down rather than hidden.

- **A shuttle that never spoke is invisible.** The loop only evaluates ids it has heard on `+/status`. A shuttle that never comes online is never seen, so it is never reported lost. The manager has no roster today; adding one is a real feature with its own decision ([record 22 §3, §10](../decisions/22-serve-loop-and-alarm-publish.md)). "Lost communication" is the right scope for "the link worked, then stopped".
- **Two clocks.** `RunLoop` (M4) runs on its simulated `_currentTime`; this serve loop runs on `DateTime.UtcNow`. They do not share a clock domain yet. That is a known seam from [record 20 §6](../decisions/20-liveness-heartbeats-and-timeouts.md) and [record 22 §10](../decisions/22-serve-loop-and-alarm-publish.md).
- **The sim has no real network.** In a real warehouse a status can arrive late, twice, or not at all because of wireless jitter. Here timing is perfect. The `MessageId` on each alarm is what lets a real consumer dedupe a redelivery.

### No hard-coded values

AGENTS.md is flat: *"No hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration."*

- The evaluation cadence comes from `FleetManager:AlarmEvaluationIntervalMs`.
- The liveness window comes from `FleetManager:LivenessTimeoutMs` (added in lesson 13, first read here).
- The severity comes from `FleetManager:LostCommunicationSeverity` (added in lesson 14, first read here).
- The alarm's QoS comes from the `Qos` block in `appsettings.json` via `QosOptions.Alarm` (record 08's rule).
- The sender id comes from `Messaging:FleetManagerId`; the topic root comes from `Messaging:TopicRoot` through `TopicScheme`.
- `FleetServeLoop` itself holds no literal topic, shuttle id, severity, or interval. The wire names (`"alarm"`, `"lostCommunication"`) are contract constants, not tunables, and live in `src/Contracts/`.

Test values are test fixtures and stay in the tests.

---

## Verification table

Everything below was checked by a command or a file read on **2026-10-14**. The baseline ran in place from `src/`. The toy ran at `/tmp/opencode/l15toy`. Both scratch directories are deleted afterwards. No file under `src/` was changed (the build wrote only git-ignored `bin/` and `obj/`).

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline builds clean | `src`: `dotnet build` | exit 0, 0 warnings, 0 errors |
| Baseline pure suite is green with no database | `src`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; **100** tests; `FleetCore.Tests`=18, `Messaging.Tests`=8, `Contracts.Tests`=7, `ShuttleCore.Tests`=25, `MqttMessaging.Tests`=2, `TrafficCore.Tests`=6, `ShuttleSim.Tests`=6, `FleetManager.Tests`=28 |
| The contract already carries `alarm` at v3 | read `docs/contract/v3.md` | kinds row `| AlarmMessage | alarm | alarm |` (line 23); "Sent by a fleet manager to the bus on topic `{root}/{shuttleId}/alarm`" (line 40); `Version: 3` |
| `ContractVersion.Current` is 3 and `MessageTypes.Alarm` exists | read `src/Contracts/MessageTypes.cs` | `Current = 3`; `MessageTypes.Alarm = "alarm"` |
| The alarm shapes already exist | read `src/Contracts/Alarm.cs`, `AlarmMessage.cs` | `Alarm(ShuttleId, Severity, Code, Detail?)`; `AlarmMessage(MessageEnvelope Envelope, Alarm Alarm)` |
| `MessageFamily` has only three members | read `src/Messaging/MessageFamily.cs` | `Command`, `Ack`, `Status`; no `Alarm` |
| `TopicScheme` has no alarm topic | read `src/Messaging/TopicScheme.cs` | `Command`, `Ack`, `Status`, `AllAcks`, `AllStatuses`; no `Alarm`/`AllAlarms` |
| `QosOptions` has three members | read `src/MqttMessaging/MqttMessageBusOptions.cs` | `Command`, `Ack`, `Status` |
| The bus switch throws on an unknown family | read `src/MqttMessaging/MqttMessageBus.cs:102` | `_ => throw new ArgumentOutOfRangeException(nameof(family))` |
| The Qos blocks have three keys today | read `src/FleetManager/appsettings.json:9`, `src/ShuttleSim/appsettings.json:9` | `{ "Command": 1, "Ack": 1, "Status": 1 }` |
| Adding a `QosOptions` member breaks two test call sites | `grep -n 'new QosOptions'` over `src` | `src/MqttMessaging.Tests/MqttMessageBusTests.cs:28,74` |
| There is no serve loop today | read `src/FleetManager/Program.cs` | only the no-arg banner and the one-shot `move` round trip |
| `FleetManager.Tests` cannot see MQTT types today | read `src/FleetManager.Tests/FleetManager.Tests.csproj` | references `FleetManager`, `Contracts`, `Messaging`, `TrafficCore`, `ShuttleCore`; not `MqttMessaging` |
| A `FakeBus` already exists in `FleetManager.Tests` | read `src/FleetManager.Tests/ReliableCommandSenderTests.cs:7` | `internal sealed class FakeBus`; the new fake must use a different name |
| `ShuttleHealth` is unchanged and pure | read `src/FleetManager/ShuttleHealth.cs` | `RecordSeen`/`LastSeenAt`/`IsUnresponsive`/`UnresponsiveShuttles`; no clock or I/O |
| `AlarmEvaluator` is unchanged and pure | read `src/FleetManager/AlarmEvaluator.cs` | `Evaluate(shuttleId, lost)`; one lock; no clock or I/O |
| `IMessageBus` is the existing seam | read `src/Messaging/IMessageBus.cs` | `ConnectAsync`, `PublishAsync`, `SubscribeAsync` |
| The envelope builder defaults correlation to null | read `src/Messaging/MessageEnvelopes.cs` | `Create(messageType, senderId, sentAt, correlationId = null)` |
| `PeriodicTimer` exists and the codebase already uses it | read `src/ShuttleSim/HeartbeatPublisher.cs`; baseline build passed | `PeriodicTimer` in the heartbeat loop; compiles on this SDK |
| The Ctrl+C serve pattern to copy | read `src/ShuttleSim/Program.cs:105-135` | `CancellationTokenSource`, `Console.CancelKeyPress`, await the loop, catch `OperationCanceledException` |
| The toy shows intake → cadence → one alarm per edge | `/tmp/opencode/l15toy`: `dotnet run` | SH-01 `PUBLISH` once at `00:00:06` and silent after; SH-02 `PUBLISH` at `00:00:08`, `CLEAR` at `00:00:09`; SH-03 never evaluated; exit 0 |

Not yet verified: `FleetServeLoop`, the `MessageFamily.Alarm`/`TopicScheme.Alarm`/`QosOptions.Alarm` plumbing, the two config changes, the `serve` command, the recording fake, the seven loop tests, the one `TopicScheme` test, and the integration check — this assignment specifies them and the reviewer measures them.

---

## Options survey

The spec's learning method asks for options compared before a decision. Record 22 already decided all six; the surveys below are the record's, compressed, so you can see *why* before you build.

### Survey 1: Where does the long-running serve loop live? (→ [record 22 §2](../decisions/22-serve-loop-and-alarm-publish.md))

| | A: a new `FleetServeLoop` class (chosen) | B: fold into `RunLoop` | C: all in `Program.cs` |
|---|---|---|---|
| Unit-testable without a broker | **yes** — pass a fake `IMessageBus` | no | **no** — top-level statements have no test entry point |
| Keeps the M4 tick loop pure | **yes** | **no** — mixes MQTT into a simulation loop | n/a |
| Reuses the existing `IMessageBus` seam | **yes** — same as `ShuttleEndpoint` | no | no |

**Decision 1: a new `public sealed class FleetServeLoop`.** `Program.cs` stays the composition root; the loop stays testable. Rejected: folding into `RunLoop` (it is M4's pure tick loop with an injected `startTime`); putting it in `Program.cs` (untestable without a broker).

### Survey 2: How does the loop know which shuttles to evaluate? (→ [record 22 §3](../decisions/22-serve-loop-and-alarm-publish.md))

| | A: ids seen on `+/status` (chosen) | B: a configured roster | C: iterate `UnresponsiveShuttles` only |
|---|---|---|---|
| A recovered shuttle clears its latch | **yes** — we still evaluate it | yes | **no** — absent, so it never clears; a second silence raises nothing |
| New configuration | **none** | a roster the manager does not have | none |
| Honest gap | a never-seen shuttle is not evaluated | none | severe: broken re-raise |

**Decision 2: keep a `HashSet<string>` of ids seen on `+/status`.** The status handler adds an id the first time it hears it. Rejected: a configured roster (a real feature, not wiring); iterating `UnresponsiveShuttles` only (it silently loses the *re-raise* after a recovery).

### Survey 3: What makes the evaluation fire? (→ [record 22 §4](../decisions/22-serve-loop-and-alarm-publish.md))

| | A: a `PeriodicTimer` cadence (chosen) | B: on every inbound message | C: a `Task.Delay` loop |
|---|---|---|---|
| Fires while a shuttle is silent | **yes** | **no** — silence sends no message | yes |
| Drift | **no** — `PeriodicTimer` keeps the interval | n/a | **yes** — `Delay` after work drifts |
| Matches the existing producer | **yes** — `HeartbeatPublisher` | no | no |

**Decision 3: a `PeriodicTimer` loop** at `FleetManager:AlarmEvaluationIntervalMs`. Rejected: on every inbound message (the failure *is* silence); a `Task.Delay` loop (drifts; the codebase already chose `PeriodicTimer`).

### Survey 4: What are the alarm's transport-lane shapes? (→ [record 22 §5](../decisions/22-serve-loop-and-alarm-publish.md))

| | A: a new `Alarm` family on `{root}/{id}/alarm` (chosen) | B: reuse `MessageFamily.Status` | C: a separate alarm topic root |
|---|---|---|---|
| An alarm can have its own QoS | **yes** | **no** — inherits the status QoS | yes |
| The bus switch accepts the kind | **yes** | yes, but it lies | yes |
| M7 can filter all alarms with one wildcard | **yes** — `AllAlarms` | no | yes, but off the shuttle tree |
| Mirrors the existing `status` design | **yes** | n/a | **no** |

**Decision 4: add `MessageFamily.Alarm`, `TopicScheme.Alarm`/`AllAlarms`, `QosOptions.Alarm` (QoS 1), and one bus switch arm.** No version bump — v3 already carries `alarm`. Rejected: reusing `MessageFamily.Status` (wrong QoS, erases the kind); a separate alarm root (breaks record 07's `{root}/{shuttleId}/{kind}` scheme).

### Survey 5: How is the `AlarmMessage` built? (→ [record 22 §6](../decisions/22-serve-loop-and-alarm-publish.md))

| | A: config sender, null correlation, the tick's clock (chosen) | B: `DateTime.UtcNow` inside the publish | C: a literal sender, non-null correlation |
|---|---|---|---|
| Testable with a fixed clock | **yes** | **no** | **yes** |
| Envelope time agrees with the decision | **yes** — one `now` per tick | **no** — two clocks | yes |
| Obeys "no literal names" | **yes** — `Messaging:FleetManagerId` | yes | **no** |

**Decision 5: `MessageEnvelopes.Create(MessageTypes.Alarm, _fleetManagerId, sentAt)`** with `sentAt` the same `now` the sweep used, `CorrelationId` left null (an alarm answers nothing). Rejected: `DateTime.UtcNow` inside the publish (untestable, two clocks); a literal `"fleet-manager"` (forbidden); a non-null correlation (an alarm is not an answer).

### Survey 6: What does "publish" mean in tests? (→ [record 22 §7](../decisions/22-serve-loop-and-alarm-publish.md))

| | A: depend on `IMessageBus` (chosen) | B: depend on `MqttMessageBus` | C: a narrow `IAlarmPublisher` plus the bus |
|---|---|---|---|
| Unit-testable with no broker | **yes** | **no** | yes |
| One dependency for subscribe and publish | **yes** | yes | **no** — two seams |
| Proves the real wire path | via a separate integration test | broker in every test | same as A |

**Decision 6: the loop takes an `IMessageBus`.** A `RecordingBus` fake captures publishes and the subscription; a separate `[Trait("Category","Integration")]` test uses the real bus. Rejected: binding to `MqttMessageBus` (every test needs a broker); two seams (the bus already covers both).

---

## The lecture

Today we are going to learn about three things: **the serve loop**, **the known-shuttle set**, and **the alarm transport lane**.

### 1) The serve loop (a testable I/O shell)

A **serve loop** is a small program that starts, then never stops on its own. It waits for work, does the work, and waits again. It runs until someone stops the process.

Real-world picture: a checkout supervisor at a grocery store. The supervisor does not stand at one till. They walk the floor. Every minute or so they look around for a problem, and when they see one they call the manager. The supervisor is the loop. The "look every minute" is the cadence. The phone call is the publish.

Why do we need one now? Lessons 13 and 14 built two pure objects. `ShuttleHealth` knows who is silent. `AlarmEvaluator` knows when to raise one alarm. But a pure object does nothing on its own. It has no thread, no subscription, and no way to send. Something has to call them, on a timer, forever. That something is the serve loop.

**The seam.** The loop talks to the bus. It must not be glued to the real MQTT bus, or every test would need a broker running. So the loop depends on the interface, `IMessageBus`:

```csharp
// src/Messaging/IMessageBus.cs (existing — do not change)
public interface IMessageBus : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken cancellationToken);
    Task SubscribeAsync(string topicFilter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken cancellationToken);
}
```

A **seam** is a place where you can swap one thing for another. Here the seam is the bus. In production you pass the real `MqttMessageBus`. In a test you pass a fake that records what was published. `ShuttleEndpoint` already uses this exact seam, so the loop follows an established pattern.

**The class and its fields.** Here is the shape. You write the bodies.

```csharp
// src/FleetManager/FleetServeLoop.cs (new)
using System.Text.Json;
using Contracts;
using Messaging;

namespace FleetManager;

public sealed class FleetServeLoop
{
    private readonly IMessageBus _bus;
    private readonly TopicScheme _topics;
    private readonly ShuttleHealth _health;
    private readonly AlarmEvaluator _evaluator;
    private readonly string _fleetManagerId;
    private readonly TimeSpan _evaluationInterval;
    private readonly Func<DateTime> _clock;
    private readonly object _knownLock = new();
    private readonly HashSet<string> _knownShuttles = new(StringComparer.Ordinal);

    // constructor and methods below
}
```

Line by line:

- `IMessageBus _bus` — the seam. One interface covers both subscribe and publish, so there is one injection point.
- `TopicScheme _topics` — builds every topic from the configured root. The loop never writes a topic string by hand.
- `ShuttleHealth _health` — the pure liveness tracker. This loop calls `RecordSeen` and `IsUnresponsive` on it.
- `AlarmEvaluator _evaluator` — the pure rising-edge latch. This loop calls `Evaluate` once per shuttle per tick.
- `string _fleetManagerId` — the envelope's `SenderId`, from `Messaging:FleetManagerId`. A literal here would break AGENTS.md.
- `TimeSpan _evaluationInterval` — how often we look, from configuration.
- `Func<DateTime> _clock` — a function that returns "now". Passing the clock in keeps `DateTime.UtcNow` out of the class and lets a test drive any time it likes.
- `object _knownLock` and `HashSet<string> _knownShuttles` — the known-shuttle set and its guard. We will come back to these in concept 2.

**The constructor validates everything.** The shape:

```csharp
    public FleetServeLoop(IMessageBus bus, TopicScheme topics, ShuttleHealth health,
        AlarmEvaluator evaluator, string fleetManagerId, TimeSpan evaluationInterval, Func<DateTime> clock)
    {
        // validate every reference; reject a blank id and a non-positive interval
    }
```

The rules you write:

- `bus`, `topics`, `health`, `evaluator`, and `clock` must not be null — `ArgumentNullException`, the same as `RunLoop`.
- `fleetManagerId` must not be null or whitespace — `ArgumentException`, the same convention `ShuttleHealth` uses for a blank shuttle id.
- `evaluationInterval` must be greater than `TimeSpan.Zero` — `ArgumentOutOfRangeException`, the same as `HeartbeatPublisher` and `ShuttleHealth`.

A bad value here is a wiring mistake. It should fail loudly at startup, not quietly at 3 a.m.

**Two small methods: subscribe and run.** These are the whole lifecycle, and they are tiny.

```csharp
    public Task SubscribeAsync(CancellationToken cancellationToken) =>
        _bus.SubscribeAsync(_topics.AllStatuses, MessageFamily.Status, HandleStatusAsync, cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await SubscribeAsync(cancellationToken);
        using var timer = new PeriodicTimer(_evaluationInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await EvaluateAndPublishAsync(_clock(), cancellationToken);
    }
```

Line by line:

- `SubscribeAsync` asks the bus for every shuttle's status: `_topics.AllStatuses` is `{root}/+/status`. The `+` is a single-level wildcard, so it matches any one shuttle id. The family is `Status`.
- `RunAsync` subscribes **first**, then starts a `PeriodicTimer` at our interval.
- `await timer.WaitForNextTickAsync(cancellationToken)` waits for the next tick and returns `false` only when the timer is disposed. When the token is cancelled it throws `OperationCanceledException`, which is how the loop stops.
- Each tick calls `EvaluateAndPublishAsync(_clock(), cancellationToken)`. One tick, one clock value. That `now` flows into both the liveness comparison and the published envelope.

Why a `PeriodicTimer` and not "evaluate when a message arrives"? Because the failure we are looking for is **silence**. A silent shuttle sends no message, so there is no message to trigger the check. The clock has to be the trigger.

**The intake handler.** When a status arrives, the loop records it and remembers the shuttle.

```csharp
    public Task HandleStatusAsync(MessageReceived message, CancellationToken cancellationToken)
    {
        // deserialize; accept only a current-version shuttleStatus with a non-blank shuttle id
        // _health.RecordSeen(shuttleId, _clock()); add to _knownShuttles under _knownLock;
        // anything else returns without touching state
    }
```

The rules for the body, mirroring `ShuttleEndpoint.HandleCommandAsync`:

- Wrap the deserialize in `try { ... } catch (JsonException) { return Task.CompletedTask; }`. A malformed payload is not a fleet event.
- Accept only an envelope whose `ContractVersion` equals `ContractVersion.Current` and whose `MessageType` equals `MessageTypes.ShuttleStatus`, with a non-blank `MessageId`.
- Accept only a body with a non-blank `Status.ShuttleId`.
- On a valid status: call `_health.RecordSeen(shuttleId, _clock())`, then, inside `lock (_knownLock)`, add the id to `_knownShuttles`.
- Anything else returns without touching `_health` or the set. The handler returns `Task.CompletedTask` because it does no I/O — it only updates memory.

**The sweep and the publish.** This is the heart of the loop.

```csharp
    public async Task EvaluateAndPublishAsync(DateTime now, CancellationToken cancellationToken)
    {
        // snapshot _knownShuttles under _knownLock
        // for each id: lost = _health.IsUnresponsive(id, now)
        //              foreach alarm in _evaluator.Evaluate(id, lost) -> PublishAlarmAsync
    }

    private Task PublishAlarmAsync(Alarm alarm, DateTime sentAt, CancellationToken cancellationToken)
    {
        var envelope = MessageEnvelopes.Create(MessageTypes.Alarm, _fleetManagerId, sentAt);
        var message = new AlarmMessage(envelope, alarm);
        return _bus.PublishAsync(_topics.Alarm(alarm.ShuttleId), JsonSerializer.Serialize(message),
            MessageFamily.Alarm, cancellationToken);
    }
```

Line by line:

- `EvaluateAndPublishAsync` takes `now`. It never reads a clock itself. That is what makes it testable with a fixed time.
- First, copy the known set under the lock: `string[] ids; lock (_knownLock) { ids = _knownShuttles.ToArray(); }`. We copy so we can evaluate without holding the lock while publishing.
- Then, for each id: `var lost = _health.IsUnresponsive(id, now);`. This is the **level**.
- Then `foreach (var alarm in _evaluator.Evaluate(id, lost)) await PublishAlarmAsync(alarm, now, cancellationToken);`. The evaluator returns zero or one alarm: one on the rising edge, none while the condition holds. This is the **edge**.
- `PublishAlarmAsync` builds the envelope with `MessageEnvelopes.Create(MessageTypes.Alarm, _fleetManagerId, sentAt)`. `Create` sets a fresh `MessageId`, a null `CorrelationId` by default, the sender, and `SentAt`. An alarm answers nothing, so null correlation is correct.
- `sentAt` is the **same `now`** the sweep used. If we called `DateTime.UtcNow` here, the envelope time could disagree with the moment of the decision, and a test could not pin it.
- The topic is `_topics.Alarm(alarm.ShuttleId)` and the family is `MessageFamily.Alarm`. We will define both in concept 3.
- `JsonSerializer.Serialize(message)` uses the same `System.Text.Json` settings as the rest of the code, so field names are PascalCase, exactly as `docs/contract/v3.md` documents.

**Why the public methods are split.** `RunAsync` is the only method that needs a timer. `SubscribeAsync` is the subscribe step alone. `EvaluateAndPublishAsync` is one sweep. A test can call the last two directly, with a fake bus and a fixed clock, and never start a timer or a broker. `RunAsync` stays tiny.

**Where this is used later.** The recovery slice will add a second publish method to this same loop: a clear or acknowledgement message with its own kind ([record 22 §12](../decisions/22-serve-loop-and-alarm-publish.md)). The dashboard (M7) subscribes to `{root}/+/alarm`. The `serve` command in `Program.cs` starts this loop and stops it on Ctrl+C.

### 2) The known-shuttle set (who to evaluate)

The **known-shuttle set** is the list of shuttle ids the manager has heard from. The loop can only evaluate a shuttle on this list.

Real-world picture: a forklift dispatcher's clipboard. The dispatcher writes down each forklift that calls in. At any moment the dispatcher can look at the clipboard and think "which of these is overdue for a call?" A forklift that never called in is not on the clipboard, so the dispatcher cannot call it overdue. The clipboard is the known set.

Why do we need it? `AlarmEvaluator.Evaluate(shuttleId, lost)` needs a shuttle id. Something must give the loop a list of ids to walk. `ShuttleHealth.IsUnresponsive` only answers about one id at a time; `UnresponsiveShuttles(now)` returns only the *stale* ones.

**Why not iterate `UnresponsiveShuttles(now)`?** This is the subtle part. Watch what happens to a shuttle that recovers.

```text
   a shuttle goes silent, then comes back
   ───────────────────────────────────────
   t=0  last without a status                condition: lost = true    latch: empty
   t=1  still silent                          lost = true              Evaluate(id, true)  -> raises, latches
   t=2  a status arrives again                lost = false             Evaluate(id, false) -> clears, sends nothing
   t=3  (a later silence, if we never evaluated it again)
```

- With the **known set**, at t=2 the id is still on our clipboard. We call `Evaluate(id, false)`. That clears the latch. If the shuttle goes silent again later, `Evaluate(id, true)` raises a fresh alarm. Good.
- With **only `UnresponsiveShuttles`**, at t=2 the id is **absent** from that list, because it is responsive. So we never call `Evaluate(id, false)`. The latch stays set. The next silence is not a rising edge anymore, so nothing is raised. The alarm is silently lost forever.

That is exactly the failure M5 must avoid. A lost alarm is worse than a noisy one.

**The honest limit.** A shuttle that never sent a status is not on the clipboard, so it is never evaluated. That is recorded in [record 22 §3](../decisions/22-serve-loop-and-alarm-publish.md) as a known scope boundary. Fixing it needs a configured roster or the dispatch path to supply ids; that is a later slice.

**Why the lock.** Two threads touch this set:

- The bus calls `HandleStatusAsync` on its receive thread and adds ids.
- The timer calls `EvaluateAndPublishAsync` on the timer thread and reads ids.

A plain `HashSet` is not safe for one thread to read while another writes. So the set is guarded by `_knownLock`. The handler takes the lock for the `Add`; the sweep takes it only long enough to copy the ids with `ToArray()`. This is the same discipline as `ShuttleHealth`, `AlarmEvaluator`, and `OccupancyMap` (record 19 §3): one lock per shared object, one atomic operation at a time, and never hold the lock across an `await` on the bus.

**Where this is used later.** When a roster exists, it can seed this set at startup. When dispatch assigns work, the assigned shuttle id could be added to the set too ([record 22 §3, §10](../decisions/22-serve-loop-and-alarm-publish.md)). The edge rule itself does not change.

### 3) The alarm transport lane

A **transport lane** is everything the bus needs to know to carry a kind of message. It is not just a topic string. It is the family, the topic branch, and the QoS level.

Real-world picture: adding a refrigerated truck to a delivery fleet. You do not just paint a new word on the side. You need the right dock (a lane), the right label (a kind), and the right temperature setting (a QoS rule). A new message kind is the same: four small things must line up before the first one can leave.

Why do we need this now? Because `MqttMessageBus` turns a `MessageFamily` into a QoS level with a `switch`, and it **throws** on a family it does not know:

```csharp
// src/MqttMessaging/MqttMessageBus.cs:97-103 (existing)
var qualityOfService = family switch
{
    MessageFamily.Command => _options.Qos.Command,
    MessageFamily.Ack => _options.Qos.Ack,
    MessageFamily.Status => _options.Qos.Status,
    _ => throw new ArgumentOutOfRangeException(nameof(family))
};
```

So `MessageFamily.Alarm` is not optional decoration. Without it, the very first publish throws before the message reaches the broker.

**Edit 1: the family.** Add `Alarm` as the **last** enum member, so the existing values do not move.

```csharp
// src/Messaging/MessageFamily.cs (change) — append so existing values do not move
public enum MessageFamily
{
    Command,
    Ack,
    Status,
    Alarm,
}
```

Why last? Because an enum's members get numbers in order: `Command` is 0, `Ack` is 1, `Status` is 2. If you insert `Alarm` in the middle, `Status` becomes 3, and any stored number changes meaning. Append.

**Edit 2: the topics.** Add a per-shuttle topic and a manager-wide wildcard.

```csharp
// src/Messaging/TopicScheme.cs (change)
public string Alarm(string shuttleId) => $"{_root}/{shuttleId}/alarm";
public string AllAlarms => $"{_root}/+/alarm";
```

`Alarm(shuttleId)` follows the `{root}/{shuttleId}/{kind}` scheme from record 07, exactly like `Status(shuttleId)`. `AllAlarms` is the `+` wildcard the dashboard will use, exactly like the existing `AllStatuses`.

**Edit 3: the QoS binding.** `QosOptions` gains one positional member, and the bus switch gains one arm.

```csharp
// src/MqttMessaging/MqttMessageBusOptions.cs (change) — one new positional member
public sealed record QosOptions(MqttQualityOfService Command, MqttQualityOfService Ack,
    MqttQualityOfService Status, MqttQualityOfService Alarm);
```

```csharp
// src/MqttMessaging/MqttMessageBus.cs (change) — one switch arm
MessageFamily.Alarm => _options.Qos.Alarm,
```

Because `QosOptions` is a positional record, adding a member changes its constructor. That breaks the two call sites in `MqttMessaging.Tests` that build one by hand (`MqttMessageBusTests.cs:28,74`). They must pass a fourth value. The build will tell you if you miss one.

**Why QoS 1 for alarms.** MQTT has three levels. `AtMostOnce` (QoS 0) can drop a message. `AtLeastOnce` (QoS 1) guarantees delivery but can duplicate. `ExactlyOnce` (QoS 2) is heavier machinery. A lost-communication alarm must not be dropped, so QoS 0 is wrong. A duplicate is acceptable: the latch already stops repeated *sends*, and a consumer can dedupe by `MessageId`. So QoS 1 is right. The value comes from configuration (`"Alarm": 1`), not a literal, exactly like the other three families (record 08).

**Edit 4: the configuration.** Both apps declare the new family's QoS, because both build `QosOptions`. Only the manager needs the evaluation cadence.

```jsonc
// both appsettings.json files (change) — the family is config-driven
"Qos": { "Command": 1, "Ack": 1, "Status": 1, "Alarm": 1 }
```

```jsonc
// src/FleetManager/appsettings.json (change) — the cadence, no literal in code
"AlarmEvaluationIntervalMs": 1000
```

**Why no contract bump.** Changing a message shape needs a decision record and a version bump. But we are changing **no shape**. `docs/contract/v3.md` already carries the alarm: the kinds table has the row `| AlarmMessage | alarm | alarm |` (line 23), and the page already says the alarm travels on `{root}/{shuttleId}/alarm` (line 40). Record 21 bumped the contract to v3 for exactly this kind. This lesson uses the shape that is already written down, so `docs/contract/` is untouched and `ContractVersion.Current` stays `3`.

**Where this is used later.** M7's dashboard subscribes to `AllAlarms` and reads the severity in the body. The recovery slice adds a new kind and reuses this same four-edit pattern.

### Recap in plain words

- **The serve loop** is a process that runs until stopped. It subscribes to `{root}/+/status`, evaluates every known shuttle on a timer, and publishes an alarm when the pure evaluator says one is due. It depends on `IMessageBus`, not the real MQTT bus, so it can be tested with no broker. It takes a clock function, so tests can pin time.
- **The known-shuttle set** is the ids the manager has heard from. We evaluate that set, not only the stale shuttles, because a recovered shuttle must still be evaluated with `lost = false` to clear the latch. A never-seen shuttle is invisible — a known limit.
- **The alarm transport lane** is the family (`MessageFamily.Alarm`), the topic (`Alarm`/`AllAlarms`), the QoS (`QosOptions.Alarm`, QoS 1), and the config. All four must exist before the first alarm can leave. No contract bump: v3 already carries `alarm`.

---

## Worked example (toy, not the assignment)

This toy shows the mechanism: **intake → cadence → one alarm per edge**. It stands in for the loop, the health tracker, and the evaluator with plain variables, so you can watch the loop move. It is a **toy**. It does **not** use the real types, it has no MQTT, and it is **not** the assignment. Do not copy it into `src/`.

Everything below ran in `/tmp/opencode/l15toy` and was deleted afterwards. One console project. `L15Toy.csproj` (complete, from `dotnet new console`):

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

`Program.cs` (complete):

```csharp
// Worked-example toy for lesson 15: the serve loop, the known-shuttle set, and
// publishing one alarm per edge. This is a TOY, clearly not the assignment.
// It stands in for FleetServeLoop + ShuttleHealth + AlarmEvaluator without
// using the real types, and it does not touch anything under src/.

using System.Text.Json;

var root = "shuttle";                 // Messaging:TopicRoot
var fleetManagerId = "fleet-manager"; // Messaging:FleetManagerId
var window = TimeSpan.FromSeconds(3); // FleetManager:LivenessTimeoutMs
var cadence = TimeSpan.FromSeconds(1);// FleetManager:AlarmEvaluationIntervalMs

// The serve loop's state: the known-shuttle set (intake), the liveness stamps,
// and the evaluator's latch. In the real code, health and evaluator are objects.
var known = new HashSet<string>(StringComparer.Ordinal);
var lastSeenAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);
var latched = new HashSet<string>(StringComparer.Ordinal);

var all = new[] { "SH-01", "SH-02", "SH-03" };

// SH-01 talks on ticks 0..2, then goes silent. SH-02 talks 0..4, goes silent,
// and comes back at tick 9. SH-03 never talks at all.
bool Talks(string id, int tick) => id switch
{
    "SH-01" => tick <= 2,
    "SH-02" => tick <= 4 || tick >= 9,
    _ => false,
};

var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

Console.WriteLine("=== Serve loop toy: intake -> cadence -> one alarm per edge ===");
Console.WriteLine($"window={window.TotalSeconds}s  cadence={cadence.TotalSeconds}s");
Console.WriteLine();

for (var tick = 0; tick < 12; tick++)
{
    // 1) Intake: a status we hear adds the id to the known set and stamps lastSeenAt.
    foreach (var id in all)
    {
        if (!Talks(id, tick))
            continue;

        known.Add(id);
        lastSeenAt[id] = clock;
        Console.WriteLine($"{clock:HH:mm:ss}  INTAKE   {id} -> known={{{string.Join(",", known.OrderBy(x => x, StringComparer.Ordinal))}}}");
    }

    // 2) Cadence: evaluate every id we have ever heard, with one clock value.
    foreach (var id in known.OrderBy(x => x, StringComparer.Ordinal))
    {
        var lost = !lastSeenAt.TryGetValue(id, out var seen) || clock - seen > window;

        if (lost && !latched.Contains(id))
        {
            latched.Add(id);
            // One tick, one clock: the envelope's SentAt is the same clock the
            // comparison used, and the sender is configuration, not a literal.
            var envelope = new ToyEnvelope(3, "alarm", fleetManagerId, null, clock);
            var alarm = new ToyAlarm(id, "critical", "lostCommunication", null);
            Console.WriteLine($"{clock:HH:mm:ss}  PUBLISH  {root}/{id}/alarm");
            Console.WriteLine($"                    sender={envelope.SenderId} correlation={envelope.CorrelationId ?? "null"} sentAt={envelope.SentAt:HH:mm:ss}");
            Console.WriteLine($"                    {JsonSerializer.Serialize(alarm)}");
        }
        else if (!lost && latched.Contains(id))
        {
            latched.Remove(id);
            Console.WriteLine($"{clock:HH:mm:ss}  CLEAR    {id} (back within the window)");
        }
    }

    clock += cadence;
}

// The toy's stand-ins for the real Contracts shapes.
record ToyEnvelope(int ContractVersion, string MessageType, string SenderId, string? CorrelationId, DateTime SentAt);
record ToyAlarm(string ShuttleId, string Severity, string Code, string? Detail);
```

Run it:

```bash
cd /tmp/opencode/l15toy
dotnet run
```

Observed output (this is a real run from `/tmp/opencode/l15toy` on 2026-10-14):

```text
=== Serve loop toy: intake -> cadence -> one alarm per edge ===
window=3s  cadence=1s

00:00:00  INTAKE   SH-01 -> known={SH-01}
00:00:00  INTAKE   SH-02 -> known={SH-01,SH-02}
00:00:01  INTAKE   SH-01 -> known={SH-01,SH-02}
00:00:01  INTAKE   SH-02 -> known={SH-01,SH-02}
00:00:02  INTAKE   SH-01 -> known={SH-01,SH-02}
00:00:02  INTAKE   SH-02 -> known={SH-01,SH-02}
00:00:03  INTAKE   SH-02 -> known={SH-01,SH-02}
00:00:04  INTAKE   SH-02 -> known={SH-01,SH-02}
00:00:06  PUBLISH  shuttle/SH-01/alarm
                    sender=fleet-manager correlation=null sentAt=00:00:06
                    {"ShuttleId":"SH-01","Severity":"critical","Code":"lostCommunication","Detail":null}
00:00:08  PUBLISH  shuttle/SH-02/alarm
                    sender=fleet-manager correlation=null sentAt=00:00:08
                    {"ShuttleId":"SH-02","Severity":"critical","Code":"lostCommunication","Detail":null}
00:00:09  INTAKE   SH-02 -> known={SH-01,SH-02}
00:00:09  CLEAR    SH-02 (back within the window)
00:00:10  INTAKE   SH-02 -> known={SH-01,SH-02}
00:00:11  INTAKE   SH-02 -> known={SH-01,SH-02}
```

Read the output like an operator:

- **Intake adds ids.** `SH-01` and `SH-02` talk early, so they enter the known set. `SH-03` never talks, so it never appears — it is never evaluated. That is the honest limit from concept 2.
- **One clock per tick.** The `PUBLISH` line prints `sender=fleet-manager correlation=null sentAt=00:00:06`. The sender is configuration, the correlation is null because an alarm answers nothing, and `sentAt` is the **same** `00:00:06` the cadence used. One tick, one clock.
- **SH-01 raises once.** It last spoke at `00:00:02`. The window is 3 seconds. At `00:00:05` it is silent for exactly 3 seconds — not past the window, so nothing. At `00:00:06` it is past, so it publishes. Every later tick is silent (`still latched`), which is why you see no second `PUBLISH`.
- **SH-02 raises, then clears.** It last spoke at `00:00:04`; at `00:00:08` it is past the window, so it publishes. It comes back at `00:00:09`, so `lost` turns false and the latch clears (`CLEAR`). Notice the clear sends nothing; it only re-arms. If `SH-02` went silent again, it would raise a fresh alarm — exactly the behavior that `UnresponsiveShuttles`-only would lose.

The toy inlines the three pieces. The real code splits them the same way:

| Toy | Real code |
|---|---|
| the intake loop that does `known.Add(id)` and `lastSeenAt[id] = clock` | `FleetServeLoop.HandleStatusAsync` → `ShuttleHealth.RecordSeen` + `_knownShuttles.Add` |
| `known.OrderBy(...)` each tick | the snapshot of `_knownShuttles` in `EvaluateAndPublishAsync` |
| the `lost` test with `window` and `clock` | `ShuttleHealth.IsUnresponsive(id, now)` |
| the `latched` set | `AlarmEvaluator._raised` (built in lesson 14) |
| the `PUBLISH` line and the JSON body | `PublishAlarmAsync` → `_bus.PublishAsync(_topics.Alarm(id), ...)` |
| the `CLEAR` line | `AlarmEvaluator.Evaluate(id, false)` returning nothing |

Your assignment is this toy with the real names and the real plumbing: a `FleetServeLoop`, the `Alarm` transport lane, the `serve` command, and the tests.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. Why does `FleetServeLoop` depend on `IMessageBus` instead of `MqttMessageBus`? What would happen to the test suite if you bound it to the real bus?
2. The loop evaluates on a timer, not when a status arrives. Why can it not trigger on an inbound message? What failure would that miss entirely?
3. Why does the loop evaluate its own set of known shuttle ids instead of iterating `UnresponsiveShuttles(now)`? Trace what happens to a recovered shuttle's latch under each choice.
4. In `PublishAlarmAsync`, the envelope's `SentAt` is the `now` passed into the sweep, not `DateTime.UtcNow`. Why does that matter, and which test would go red if you used `DateTime.UtcNow`?
5. Adding `MessageFamily.Alarm` touches four places: the enum, the topic scheme, the QoS options plus the bus switch, and the config. Why can you not just publish on a new topic string? And why does this slice not bump the contract?

---

## The assignment

Build M5's third slice: **the serve loop and the alarm publish path**. The fleet manager gains a long-running `serve` command. On a cadence it evaluates the shuttles it has heard from and publishes an `AlarmMessage` when one becomes unreachable. The alarm travels on a new `Alarm` transport lane at QoS 1. The contract does not change: `alarm` is already v3.

### Requirements

1. **Add the transport lane: `src/Messaging/` and `src/MqttMessaging/`.**
   - `src/Messaging/MessageFamily.cs`: add `Alarm` as the **last** enum member (`Command`, `Ack`, `Status`, `Alarm`). Do not reorder the existing three.
   - `src/Messaging/TopicScheme.cs`: add `public string Alarm(string shuttleId) => $"{_root}/{shuttleId}/alarm";` and `public string AllAlarms => $"{_root}/+/alarm";`.
   - `src/MqttMessaging/MqttMessageBusOptions.cs`: add `MqttQualityOfService Alarm` as the **last** positional member of `QosOptions`.
   - `src/MqttMessaging/MqttMessageBus.cs`: add the switch arm `MessageFamily.Alarm => _options.Qos.Alarm,`.
   - Update the two `new QosOptions(...)` call sites in `src/MqttMessaging.Tests/MqttMessageBusTests.cs` (lines 28 and 74 today) to pass a fourth value. No new test there.

2. **Configuration, no literals.**
   - `src/FleetManager/appsettings.json` and `src/ShuttleSim/appsettings.json`: add `"Alarm": 1` to the `Messaging:Qos` object, so the block reads `{ "Command": 1, "Ack": 1, "Status": 1, "Alarm": 1 }`.
   - `src/FleetManager/appsettings.json`: add `"AlarmEvaluationIntervalMs": 1000` under `FleetManager`. It is a positive integer; the loop takes it as a `TimeSpan`.

3. **Add the serve loop: `src/FleetManager/FleetServeLoop.cs` (new).**
   - `public sealed class FleetServeLoop` with constructor `FleetServeLoop(IMessageBus bus, TopicScheme topics, ShuttleHealth health, AlarmEvaluator evaluator, string fleetManagerId, TimeSpan evaluationInterval, Func<DateTime> clock)`.
   - The constructor validates every reference and throws `ArgumentNullException` for a null one; throws `ArgumentException` for a blank `fleetManagerId`; throws `ArgumentOutOfRangeException` for `evaluationInterval <= TimeSpan.Zero`.
   - `public Task SubscribeAsync(CancellationToken cancellationToken)` — subscribes to `_topics.AllStatuses` with `MessageFamily.Status` and the `HandleStatusAsync` handler.
   - `public async Task RunAsync(CancellationToken cancellationToken)` — awaits `SubscribeAsync`, then loops on a `PeriodicTimer(_evaluationInterval)`, calling `EvaluateAndPublishAsync(_clock(), cancellationToken)` each tick.
   - `public Task HandleStatusAsync(MessageReceived message, CancellationToken cancellationToken)` — deserializes the payload inside `try/catch (JsonException)`; accepts only a `ContractVersion.Current` `shuttleStatus` envelope with a non-blank `MessageId` and a non-blank `Status.ShuttleId`; on a valid status calls `_health.RecordSeen(shuttleId, _clock())` and adds the id to `_knownShuttles` under `_knownLock`; returns `Task.CompletedTask`. On anything else it changes no state. It must not throw on a malformed payload.
   - `public async Task EvaluateAndPublishAsync(DateTime now, CancellationToken cancellationToken)` — snapshots `_knownShuttles` under `_knownLock` into an array; for each id computes `var lost = _health.IsUnresponsive(id, now);` and awaits `PublishAlarmAsync` for every `Alarm` returned by `_evaluator.Evaluate(id, lost)`.
   - `private Task PublishAlarmAsync(Alarm alarm, DateTime sentAt, CancellationToken cancellationToken)` — builds `MessageEnvelopes.Create(MessageTypes.Alarm, _fleetManagerId, sentAt)`, wraps it in `new AlarmMessage(envelope, alarm)`, and publishes `JsonSerializer.Serialize(message)` on `_topics.Alarm(alarm.ShuttleId)` with `MessageFamily.Alarm`.
   - The file must contain no literal topic, shuttle id, severity, sender id, or interval, and no `DateTime.UtcNow` (the clock is injected), no `Guid.NewGuid` (the envelope builder makes ids), and no `Thread.Sleep`. It must not reference `MqttMessaging` or `Npgsql`; `IMessageBus` is the only bus type it names.

4. **Add the `serve` command: `src/FleetManager/Program.cs`.**
   - Keep the no-arg banner and the existing `move` command's behavior exactly as it is.
   - Accept `serve` (one argument). Read `Messaging` (bind `MessagingOptions` and `MqttMessageBusOptions`), `FleetManager:LivenessTimeoutMs`, `FleetManager:AlarmEvaluationIntervalMs`, and `FleetManager:LostCommunicationSeverity`.
   - Validate `LivenessTimeoutMs` and `AlarmEvaluationIntervalMs` are positive integers, like the existing `move` command validates its timeouts.
   - Build `new TopicScheme(messaging.TopicRoot)`, `new ShuttleHealth(TimeSpan.FromMilliseconds(livenessTimeoutMs))`, `new AlarmEvaluator(lostCommunicationSeverity)`, and the real `new MqttMessageBus(mqtt)`; connect the bus; run `new FleetServeLoop(...).RunAsync(stopping.Token)` with `() => DateTime.UtcNow`.
   - Stop on Ctrl+C exactly like `src/ShuttleSim/Program.cs`: a `CancellationTokenSource`, a `Console.CancelKeyPress` handler that cancels and sets a `TaskCompletionSource`, await that task, then cancel and await the loop, catching `OperationCanceledException` when cancelling. Dispose the bus with `await using`. No process is left running.

5. **Add the recording fake and the pure loop tests: `src/FleetManager.Tests/FleetServeLoopTests.cs` (new).**
   - Add `internal sealed class RecordingBus : IMessageBus` (a **new name** — `FakeBus` already exists in `ReliableCommandSenderTests.cs`) that records every `(Topic, Payload, Family)` published, records the last `SubscribeAsync` filter and family, and exposes the captured handler so a test can feed it a status. `PublishAsync` and `SubscribeAsync` return completed tasks; `DisposeAsync` returns a completed `ValueTask`.
   - Seven tests, with these exact names:
     - `The_constructor_rejects_a_blank_fleet_manager_id_and_a_non_positive_interval`
     - `Subscribe_uses_the_all_statuses_filter_and_the_status_family`
     - `A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender`
     - `A_latched_lost_shuttle_publishes_nothing_again`
     - `A_recovered_shuttle_clears_the_latch_and_can_raise_again`
     - `An_invalid_status_message_is_ignored`
     - `Two_shuttles_are_evaluated_independently`
   - Build the loop with a fixed clock (a captured variable the test advances), a `ShuttleHealth` with a small window, an `AlarmEvaluator` with a known severity, and a `TopicScheme("shuttle")`. Feed statuses through the captured handler; drive the sweep by calling `EvaluateAndPublishAsync(now, ...)` directly. No timer, no broker, no database.
   - `A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender` must deserialize the published payload and assert: the topic is `shuttle/SH-01/alarm`; the family is `MessageFamily.Alarm`; the envelope's `MessageType` is `MessageTypes.Alarm`, `ContractVersion` is `ContractVersion.Current`, `SenderId` is the configured fleet manager id, `CorrelationId` is null, and `SentAt` is the `now` passed to the sweep; and the body's `ShuttleId` is the shuttle, `Code` is `AlarmCodes.LostCommunication`, `Severity` is the configured severity, and `Detail` is null.
   - These tests are **pure**: no `Integration` trait, no database, no broker.

6. **Add the integration test: `src/FleetManager.Tests/FleetServeLoopIntegrationTests.cs` (new).**
   - One test, `[Trait("Category", "Integration")]`, named `An_alarm_reaches_the_broker_on_the_shuttle_topic`.
   - Connect the **real** `MqttMessageBus` to the broker, subscribe a listener to `TopicScheme.AllAlarms` with `MessageFamily.Alarm`, run one `FleetServeLoop`: feed a status so the shuttle is known, advance the fixed clock past the window, call `EvaluateAndPublishAsync`, and assert the listener receives one `AlarmMessage` whose body's `Code` is `AlarmCodes.LostCommunication`.
   - Get the broker host and port from configuration or environment (with the same defaults the app uses), not from a literal in the test body. Do not mention or start Docker from the test.
   - This test needs the broker up (`docker compose -f docker/compose.yml up -d broker`). It is excluded by `--filter "Category!=Integration"`.

7. **Add the topic test: `src/Messaging.Tests/TopicSchemeTests.cs`.**
   - Add one test, named `TopicScheme_alarm_topics_use_the_per_shuttle_branch_and_the_wildcard`, asserting `topics.Alarm("SH-01") == "shuttle/SH-01/alarm"` and `topics.AllAlarms == "shuttle/+/alarm"`.

8. **Add the project reference: `src/FleetManager.Tests/FleetManager.Tests.csproj`.**
   - Add `<ProjectReference Include="..\MqttMessaging\MqttMessaging.csproj" />` so the integration test can name `MqttMessageBus` and `MqttMessageBusOptions`. This is the first reference from `FleetManager.Tests` to `MqttMessaging`.

9. **Keep the contract unchanged.**
   - Do not touch `src/Contracts/` or `docs/contract/`. `ContractVersion.Current` stays `3`. Do not add a `v4.md`. The `alarm` kind and topic already exist in `docs/contract/v3.md`.

10. **Do not touch these:** `src/FleetManager/ShuttleHealth.cs`, `src/FleetManager/AlarmEvaluator.cs`, `src/FleetManager/RunLoop.cs`, `src/ShuttleCore/`, `src/TrafficCore/`, `src/FleetCore/`, `docs/contract/v2.md`, `docker/Dockerfile.build`. The `move` command in `Program.cs` keeps its behavior.

11. **M5 later items are not present.** No recovery, no acknowledgement, no reassignment, no restart reconciliation, no `Release` on timeout. No shuttle-reported faults (`blockedPath`, `palletNotDetected`) raised anywhere. No configured shuttle roster; a never-seen shuttle is not evaluated. The known set and the latch are in memory only. No WMS, no dashboard.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are asserted, not admired. Read every line's purpose before you run it.

```bash
# Phase 1 — the new pieces exist, with the pinned names
test -f src/FleetManager/FleetServeLoop.cs || echo "missing FleetServeLoop.cs"
test -f src/FleetManager.Tests/FleetServeLoopTests.cs || echo "missing FleetServeLoopTests.cs"
test -f src/FleetManager.Tests/FleetServeLoopIntegrationTests.cs || echo "missing FleetServeLoopIntegrationTests.cs"
grep -qE 'class FleetServeLoop' src/FleetManager/FleetServeLoop.cs
grep -qE 'IMessageBus' src/FleetManager/FleetServeLoop.cs
grep -qE 'Func<DateTime>' src/FleetManager/FleetServeLoop.cs
for m in SubscribeAsync RunAsync HandleStatusAsync EvaluateAndPublishAsync; do grep -qE "\b$m\b" src/FleetManager/FleetServeLoop.cs || echo "missing FleetServeLoop member: $m"; done
```

```bash
# Phase 2 — the alarm transport lane
grep -qE 'Alarm' src/Messaging/MessageFamily.cs
grep -qE 'Alarm\(string shuttleId\)' src/Messaging/TopicScheme.cs
grep -qE 'AllAlarms' src/Messaging/TopicScheme.cs
grep -qE 'Alarm' src/MqttMessaging/MqttMessageBusOptions.cs
grep -qE 'MessageFamily\.Alarm' src/MqttMessaging/MqttMessageBus.cs
grep -qE 'MessageFamily\.Alarm' src/FleetManager/FleetServeLoop.cs
```

```bash
# Phase 3 — configuration carries the lane and the cadence, no literal
grep -qE '"Alarm": *1' src/FleetManager/appsettings.json
grep -qE '"Alarm": *1' src/ShuttleSim/appsettings.json
grep -qE 'AlarmEvaluationIntervalMs' src/FleetManager/appsettings.json
```

```bash
# Phase 4 — the serve command
grep -qE '"serve"' src/FleetManager/Program.cs
grep -qE 'AlarmEvaluationIntervalMs' src/FleetManager/Program.cs
grep -qE 'LivenessTimeoutMs' src/FleetManager/Program.cs
grep -qE 'LostCommunicationSeverity' src/FleetManager/Program.cs
grep -qE 'FleetServeLoop' src/FleetManager/Program.cs
```

```bash
# Phase 5 — the loop keeps the clock injected and names no I/O bus
grep -rnE 'DateTime\.UtcNow' src/FleetManager/FleetServeLoop.cs   # nothing: the clock is a parameter
grep -rniE 'MqttMessaging|Npgsql|Postgres' src/FleetManager/FleetServeLoop.cs   # nothing
grep -qE 'private readonly object _knownLock' src/FleetManager/FleetServeLoop.cs
grep -qE 'lock *\(_knownLock\)' src/FleetManager/FleetServeLoop.cs || echo "FAIL: the known set must be guarded"
```

```bash
# Phase 6 — the test project can see MQTT types, and the fake has a fresh name
grep -qE 'MqttMessaging' src/FleetManager.Tests/FleetManager.Tests.csproj
grep -qE 'class RecordingBus' src/FleetManager.Tests/FleetServeLoopTests.cs
grep -qE '\[Trait\("Category", "Integration"\)\]' src/FleetManager.Tests/FleetServeLoopIntegrationTests.cs
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
[ "$RC" -eq 0 ] && [ "$P" -eq 8 ] && [ "$F" -eq 0 ] && [ "$TOTAL" -eq 108 ] \
  && echo "suite gate OK" || echo "FAIL: expected RC 0, 8 Passed!, 0 Failed!, 108 tests"
grep -E 'Passed: +35,.*FleetManager\.Tests\.dll' "$T" && echo "FleetManager.Tests=35 OK" || echo "FAIL: FleetManager.Tests should show 35"
grep -E 'Passed: +9,.*Messaging\.Tests\.dll' "$T" && echo "Messaging.Tests=9 OK" || echo "FAIL: Messaging.Tests should show 9"
rm -f "$T"

# Phase 7b — the new tests are really there, by anchored name
L=$(mktemp); dotnet test ShuttleFleet.slnx --list-tests > "$L" 2>&1
for t in The_constructor_rejects_a_blank_fleet_manager_id_and_a_non_positive_interval Subscribe_uses_the_all_statuses_filter_and_the_status_family A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender A_latched_lost_shuttle_publishes_nothing_again A_recovered_shuttle_clears_the_latch_and_can_raise_again An_invalid_status_message_is_ignored Two_shuttles_are_evaluated_independently TopicScheme_alarm_topics_use_the_per_shuttle_branch_and_the_wildcard An_alarm_reaches_the_broker_on_the_shuttle_topic; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                                             # nothing
rm -f "$L"
cd ..
```

```bash
# Phase 8 — no test launches docker
grep -rniE 'docker' src/*.Tests --include='*.cs' && echo "FAIL: docker referenced in a test" || echo "OK: no docker in tests"
```

```bash
# Phase 9 — the contract is untouched, and no later-M5 scope leaks
grep -qE 'Current = 3' src/Contracts/MessageTypes.cs
grep -qE '^Version: 3$' docs/contract/v3.md
grep -qE '^Version: 2$' docs/contract/v2.md
test ! -f docs/contract/v4.md
git diff --quiet HEAD -- src/Contracts docs/contract src/FleetManager/ShuttleHealth.cs src/FleetManager/AlarmEvaluator.cs src/FleetManager/RunLoop.cs src/ShuttleCore src/TrafficCore src/FleetCore docker/Dockerfile.build \
  && echo "OK: contracts, contract pages, pure fleet objects, cores and Dockerfile unchanged" \
  || echo "FAIL: a protected file changed"
grep -rniE 'blockedPath|palletNotDetected' src --include='*.cs' \
  | grep -vE 'src/Contracts/AlarmCodes\.cs|src/[^/]+\.Tests/' || true
```

```bash
# Optional — the real-broker path, needs the broker up.
# docker compose -f docker/compose.yml up -d broker
# cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"   # RC 0, 3 passed
# docker compose -f docker/compose.yml down
```

**Proof details.** The assignment adds one product file (`FleetServeLoop.cs`); edits five product files (`MessageFamily.cs`, `TopicScheme.cs`, `MqttMessageBusOptions.cs`, `MqttMessageBus.cs`, `FleetManager/Program.cs`); edits two `appsettings.json` files; edits the test project file (`FleetManager.Tests.csproj`); adds two test files (`FleetServeLoopTests.cs` with seven tests, `FleetServeLoopIntegrationTests.cs` with one); edits `TopicSchemeTests.cs` (one new test) and `MqttMessageBusTests.cs` (two call-site updates, no new test). The pure test total goes from **100 to 108**: `FleetManager.Tests` gains seven (28 → 35), `Messaging.Tests` gains one (8 → 9). The integration suite goes from 2 to 3. The gate is `RC 0`, 8 `Passed!`, 0 `Failed!`, 108 total.

What the reviewer will mutate to prove the tests are load-bearing:

- bind the loop to a family it does not own — change `_bus.PublishAsync(..., MessageFamily.Alarm, ...)` to `MessageFamily.Status` → `A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender` goes red;
- change `_topics.Alarm(id)` to `_topics.Status(id)` → the same test goes red (wrong topic);
- replace `sentAt` with `DateTime.UtcNow` in `PublishAlarmAsync` → the same test goes red (the envelope time no longer equals the sweep's `now`);
- hard-code the sender instead of `_fleetManagerId` → the same test goes red;
- remove the `_knownShuttles.Add(...)` in `HandleStatusAsync` → `A_lost_shuttle_publishes_one_alarm...`, `A_latched...`, `A_recovered...`, and `Two_shuttles_are_evaluated_independently` go red (nothing is ever evaluated) and `An_invalid_status_message_is_ignored` stays green (it asserts nothing is published);
- iterate `_health.UnresponsiveShuttles(now)` instead of the known set → `A_recovered_shuttle_clears_the_latch_and_can_raise_again` goes red (the recovered shuttle is absent, so the latch never clears);
- accept any version in `HandleStatusAsync` → `An_invalid_status_message_is_ignored` goes red;
- change the subscribe family to `MessageFamily.Status` → `Subscribe_uses_the_all_statuses_filter_and_the_status_family` goes red (if that test also checks the family); make sure it does, or the family arm is unpinned;
- make `EvaluateAndPublishAsync` read `DateTime.UtcNow` instead of using `now` → the fixed-clock tests go red.

### Stretch goal

Give the recovery slice a place to correlate. Add a locked `Dictionary<string, string> _lastAlarmMessageId` and a `public string? LastAlarmMessageId(string shuttleId)` accessor. In `PublishAlarmAsync`, after the publish, record the envelope's `MessageId` for that shuttle. Add one test: after a publish, the accessor returns the same `MessageId` the wire payload carries, and a shuttle with no alarm returns `null`. Comment that the recovery slice will correlate its acknowledgement to this id — this is the "place to publish" that [record 22 §12](../decisions/22-serve-loop-and-alarm-publish.md) names. Do **not** invent a new wire kind; recovery gets its own record and version bump.

### Not in this assignment

- **Recovery, operator acknowledgement, and reassignment** — later M5 slices. The loop has one publish method today.
- **Shuttle-reported faults** (`blockedPath`, `palletNotDetected`) — a later M5 slice. The codes exist; nothing raises them.
- **A configured roster, or evaluating a never-seen shuttle** — [record 22 §3, §10](../decisions/22-serve-loop-and-alarm-publish.md). The known set only holds ids we have heard.
- **Persisting the known set or the latch** — in-memory only; a manager restart forgets both.
- **Auto-release on timeout** — forbidden (record 17 §4). The window passing is knowledge, not action.
- **WMS orders** — M6. **Dashboard** — M7.
- **A contract version bump.** `alarm` is already v3. `docs/contract/` and `src/Contracts/` are untouched.

---

## Closing

Read the spec's M5 line once more: *"Heartbeats and timeouts. Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges."*

Lesson 13 made "lost communication" **detectable**. Lesson 14 made it **sayable**. This lesson makes it **leave the fleet manager**. A long-running `FleetServeLoop` subscribes to every shuttle's status, remembers who it has heard from, and evaluates that set on a cadence from configuration. When a shuttle goes past the liveness window, the pure evaluator returns one alarm and the loop publishes an `AlarmMessage` on `shuttle/{id}/alarm` through a new `Alarm` transport lane at QoS 1. The contract does not move; v3 already carries the kind.

Before you call this done, be ready to explain without notes:

- why the loop depends on `IMessageBus` and takes its clock as a function;
- why it evaluates on a timer, and why "evaluate when a message arrives" cannot work;
- why it evaluates the ids it has heard from, not just the stale ones, and trace the recovered shuttle;
- why the envelope uses the sweep's `now` and a configured sender;
- why adding a family touches four places and why this slice does not bump the contract.

When the reviewer passes this, M5 has its alarm path end to end: a heartbeat stops, the loop notices, and an alarm leaves the manager. The recovery slice that answers it is next.
