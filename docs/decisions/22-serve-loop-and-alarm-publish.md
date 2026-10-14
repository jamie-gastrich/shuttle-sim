# Decision Record 22: The alarm publish path — a fleet-manager serve loop, the `Alarm` transport lane, and a testable publish seam

**Date:** 2026-10-14
**Milestone:** M5 (Faults and recovery) — lesson 15: the alarm publish path (the serve loop).
**Status:** Accepted. Append-only. Supersedes nothing. Links back to records 07, 08, 19, 20, 21.
**Builds on:** [record 21](21-alarms-with-severity.md) (§10 reserves `MessageFamily.Alarm`, `TopicScheme.Alarm`/`AllAlarms`, and `QosOptions.Alarm` for exactly this lesson; §7 lists the loop that must publish), [record 20](20-liveness-heartbeats-and-timeouts.md) (`ShuttleHealth.RecordSeen`/`IsUnresponsive`, the timeout window, and the two-clock seam in §6), [record 19](19-run-loop-wiring-and-concurrency.md) (the `lock` discipline and "decision logic separated from I/O"), [record 08](08-mqtt-client-qos-and-configuration.md) (QoS comes from configuration), [record 07](07-message-contract-v2-envelope-and-topics.md) (the `{root}/{shuttleId}/{kind}` topic scheme).
**Replaced by:** a later record that changes where the serve loop lives, how it chooses shuttles, its cadence, or the alarm's QoS. Append-only.
**Wire contract:** **no change.** `alarm` is already in `docs/contract/v3.md` (kinds table line 23; topic line 40). `ContractVersion.Current` stays `3`. `docs/contract/v2.md` and `v3.md` are not touched.

---

## 1. What we are deciding

Record 21 §10 fixed the target: "Lesson 15 can wire the fleet-manager serve loop with a fixed target: subscribe to `+/status`, call `ShuttleHealth.RecordSeen`, evaluate on a cadence, and publish `AlarmMessage` on `shuttle/{id}/alarm` at `MessageFamily.Alarm` / `QosOptions.Alarm` (QoS 1). No shape is left to guess."

Today that loop does not exist. `src/FleetManager/Program.cs` only runs a one-shot `move` round trip. `ShuttleHealth` and `AlarmEvaluator` are pure and tested, but nothing writes or reads them outside a test. `MessageFamily` has three members, `TopicScheme` has no alarm topic, and `QosOptions` has three QoS values. An alarm can be *computed* but cannot *leave the fleet manager*.

This record decides six things:

**A.** Where does the long-running serve loop live?
**B.** How does the loop know which shuttles to evaluate?
**C.** What makes the evaluation fire, and where does the cadence come from?
**D.** What are the exact transport-lane shapes for the alarm, and does the contract bump?
**E.** How is an `AlarmMessage` built (envelope fields, sender, correlation)?
**F.** What does "publish" mean in tests, so the loop is unit-testable without a broker?

---

## 2. Decision A: a new `FleetServeLoop` class in `FleetManager`, wired from `Program.cs`

The loop must subscribe to a topic, call two pure objects, and publish. Those are I/O. The question is where that I/O-shaped code goes.

| | A: a `FleetServeLoop` class (chosen) | B: fold into `RunLoop` | C: all in `Program.cs` |
|---|---|---|---|
| Unit-testable without a broker | **yes** — pass a fake `IMessageBus` | no — `RunLoop` already carries `Shuttle` and a simulated clock | **no** — top-level statements have no test entry point |
| Keeps the M4 tick loop pure | **yes** | **no** — mixes MQTT into a simulation loop | n/a |
| Reuses the existing `IMessageBus` seam | **yes** — same as `ShuttleEndpoint` | no | no |
| New surface | one class | none, but a mixed responsibility | none, but untestable |

**Decision A: a new `public sealed class FleetServeLoop` in `src/FleetManager/FleetServeLoop.cs`.** It takes an `IMessageBus`, a `TopicScheme`, the two pure objects, the fleet manager id, the cadence, and a clock function. `Program.cs` grows a `serve` command that reads configuration, builds the objects, connects the real bus, and runs the loop until Ctrl+C. `Program.cs` stays the composition root; the loop stays testable.

Rejected: **folding into `RunLoop`** — `RunLoop` is M4's pure tick loop over `Shuttle` objects with an injected `startTime`; MQTT in it would break its determinism and its tests, and confuse two different "loops". **Putting it in `Program.cs`** — top-level statements cannot be unit tested, so the loop's rising-edge behavior would only be reachable through a broker. **A separate `FleetManager.Service` project** — one class does not need a project; that is structure without a payoff yet.

---

## 3. Decision B: the loop evaluates the shuttle ids it has seen on `+/status`

`AlarmEvaluator.Evaluate(shuttleId, lost)` needs a shuttle id. The loop must have a set of ids to iterate. `ShuttleHealth` alone cannot supply it: `UnresponsiveShuttles(now)` returns only the *stale* ones, and a shuttle that has recovered is exactly the one we must call with `lost = false` to clear the latch.

| | A: ids seen on `+/status` (chosen) | B: a configured roster (`FleetManager:ShuttleIds`) | C: iterate `UnresponsiveShuttles` only |
|---|---|---|---|
| A recovered shuttle clears its latch | **yes** — we still evaluate it | yes | **no** — it is absent from the list, so it never clears; a second silence never re-raises |
| New configuration | **none** | a roster the manager does not have today | none |
| Honest gap | a shuttle that **never** sent a status is not evaluated | none | severe: broken re-raise |

**Decision B: the loop keeps a `HashSet<string>` of shuttle ids it has seen on `+/status`.** The status handler adds an id the first time it hears it. Each cadence tick iterates that set, asks `ShuttleHealth.IsUnresponsive(id, now)`, and calls `AlarmEvaluator.Evaluate(id, lost)`. The set is guarded by a `lock`, because the handler runs on the bus's receive thread and the cadence runs on the timer thread.

Rejected: **a configured roster** — the manager has no shuttle roster today; adding one is a real feature, not wiring, and would need its own decision (see §10). **Iterating `UnresponsiveShuttles` only** — it breaks the clear branch: a shuttle that comes back would never be evaluated, so the latch would stay set and the *next* silence would raise nothing. That is a silent, permanent loss of an alarm, exactly the failure mode M5 must avoid.

**The honest limit.** This loop can only report "lost communication" for a shuttle it has *heard from before*. A shuttle that never comes online at all is invisible to it, because the manager has no roster. That is the correct scope for "lost communication" (the link worked, then stopped). A never-seen shuttle needs a configured roster or the dispatch path to supply ids; that is a later slice, written down here.

---

## 4. Decision C: evaluate on a `PeriodicTimer` cadence, from `FleetManager:AlarmEvaluationIntervalMs`

| | A: a `PeriodicTimer` cadence (chosen) | B: evaluate on every inbound message | C: a `Task.Delay` loop |
|---|---|---|---|
| Fires while a shuttle is silent | **yes** | **no** — a silent shuttle sends nothing, so nothing triggers the check | yes |
| Drift | **no** — `PeriodicTimer` keeps the interval | n/a | **yes** — `Delay` after work drifts |
| Cadence from config | **yes** | n/a | yes |
| Matches the existing producer | **yes** — `HeartbeatPublisher` uses `PeriodicTimer` | no | no |

**Decision C: a `PeriodicTimer` loop at `FleetManager:AlarmEvaluationIntervalMs`** (a positive integer; this project uses `1000`). `Program.cs` reads the key, validates it is positive, and passes `TimeSpan.FromMilliseconds(...)` into the loop. The loop holds no literal.

Rejected: **evaluating on every inbound message** — the whole point of heartbeat detection is that the failure is *silence*; there is no inbound message to trigger the check. **A `Task.Delay` loop** — it drifts and it busy-waits less cleanly than `PeriodicTimer`; the codebase already chose `PeriodicTimer` in `HeartbeatPublisher`.

**The cadence and the window are different tunables.** `LivenessTimeoutMs` is how long silence must last before it counts as lost; `AlarmEvaluationIntervalMs` is how often we look. The interval should be smaller than the window so an alarm is prompt, but that is tuning, not a rule the code enforces.

---

## 5. Decision D: the alarm gets a lane — `MessageFamily.Alarm`, `TopicScheme.Alarm`/`AllAlarms`, `QosOptions.Alarm` (QoS 1); no version bump

`MessageFamily` is a small enum that names the *kinds* of traffic a bus carries. `MqttMessageBus` turns a family into a QoS level and, if the family is unknown, throws. So a new kind is not "just a topic" — it is a family, a topic branch, a QoS value, and config, and every one of them must exist before the first alarm can be published.

| | A: a new `Alarm` family on `{root}/{id}/alarm` (chosen) | B: reuse `MessageFamily.Status` | C: a separate alarm topic root |
|---|---|---|---|
| An alarm can have its own QoS | **yes** | **no** — it would inherit the status QoS | yes |
| The bus switch accepts the kind | **yes** | yes, but it lies about the kind | yes |
| M7 can filter all alarms with one wildcard | **yes** — `AllAlarms` | no | yes, but off the shuttle tree |
| Mirrors the existing `status` design | **yes** | n/a | **no** |

**Decision D: add `MessageFamily.Alarm` as the last enum member** (so `Command=0`, `Ack=1`, `Status=2` keep their values), add `TopicScheme.Alarm(shuttleId)` = `{root}/{shuttleId}/alarm` and `TopicScheme.AllAlarms` = `{root}/+/alarm`, and add `QosOptions.Alarm` set to `AtLeastOnce` (MQTT QoS 1) in configuration (`"Alarm": 1`). `MqttMessageBus.ToMqttQos` gains one arm.

**No version bump.** `docs/contract/v3.md` already carries the alarm: the kinds table row `| AlarmMessage | alarm | alarm |` (line 23) and the sentence "Sent by a fleet manager to the bus on topic `{root}/{shuttleId}/alarm`" (line 40). Record 21 already bumped the contract to v3. This lesson uses those shapes; it changes no shape, so `docs/contract/` is untouched and `ContractVersion.Current` stays `3`.

**QoS 1, from configuration.** A lost-communication alarm must not be dropped, so `AtLeastOnce` is right. A duplicate is acceptable: the latch already stops repeated *sends*, and a consumer can dedupe by `MessageId`. `ExactlyOnce` (QoS 2) is heavier and not worth it for an event that is idempotent by id. Record 21 §2 already chose QoS 1; this record only gives it a config key and a switch arm.

Rejected: **reusing `MessageFamily.Status`** — it would route alarms at the status QoS and erase the kind on the bus family dimension. **A separate alarm root** (e.g. `alarm/{id}`) — it breaks the `{root}/{shuttleId}/{kind}` scheme from record 07 and makes per-shuttle filtering harder.

---

## 6. Decision E: `AlarmMessage` is built with a config sender, a null correlation, and the tick's clock

The envelope is shared (record 07). The one interesting choice is the time and the sender.

- `MessageEnvelopes.Create(MessageTypes.Alarm, _fleetManagerId, sentAt)` — `_fleetManagerId` is `MessagingOptions.FleetManagerId` from configuration, never a literal (AGENTS.md). `Create` leaves `CorrelationId` null by default, which is correct: an alarm answers nothing.
- `new AlarmMessage(envelope, alarm)` then `JsonSerializer.Serialize(...)`.
- The topic is `_topics.Alarm(alarm.ShuttleId)`, family `MessageFamily.Alarm`.
- `sentAt` is the **same `now`** the cadence used for the liveness comparison. One tick, one clock value: the envelope time and the decision agree, and a test can inject a fixed clock. Using `DateTime.UtcNow` inside the publish would split the tick across two clocks and make the loop untestable.

Rejected: **`DateTime.UtcNow` inside the publish** — untestable and inconsistent with the evaluation time; **sender as the literal `"fleet-manager"`** — a hard-coded name, forbidden; **a non-null `CorrelationId`** — an alarm is not an answer to anything yet.

---

## 7. Decision F: the loop depends only on `Messaging.IMessageBus`; tests use a recording fake, plus one real-broker integration check

| | A: depend on `IMessageBus` (chosen) | B: depend on `MqttMessageBus` | C: a narrow `IAlarmPublisher` plus the bus |
|---|---|---|---|
| Unit-testable with no broker | **yes** | **no** | yes |
| One dependency for subscribe and publish | **yes** | yes | **no** — two seams |
| Matches existing code (`ShuttleEndpoint`) | **yes** | no | no |
| Proves the real wire path | via a separate integration test | only with a broker in every test | same as A |

**Decision F: `FleetServeLoop` takes an `IMessageBus` and a `TopicScheme`.** A `RecordingBus` fake in `FleetManager.Tests` captures every `(topic, payload, family)` published and the subscription filter. The pure tests call the loop's public methods directly — `HandleStatusAsync`, `EvaluateAndPublishAsync`, `SubscribeAsync` — so no timer and no broker are involved. A separate `[Trait("Category", "Integration")]` test connects the **real** `MqttMessageBus` to the broker and asserts an alarm appears on the wire. The split is: the fake proves the decision composition; the integration test proves the transport lane.

Rejected: **binding the loop to `MqttMessageBus`** — every test would need a broker, which the pure suite must not require. **A narrow `IAlarmPublisher` plus the bus** — two injection points for one loop; the bus already covers both needs, and `ShuttleEndpoint` set the precedent.

**Why the loop's public methods are split.** `RunAsync` subscribes and drives the timer; `SubscribeAsync` is the subscribe step alone; `EvaluateAndPublishAsync` is one evaluation sweep. Splitting them lets the tests drive the mechanism deterministically and keep `RunAsync` tiny.

---

## 8. The exact shapes

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

```csharp
// src/Messaging/TopicScheme.cs (change)
public string Alarm(string shuttleId) => $"{_root}/{shuttleId}/alarm";
public string AllAlarms => $"{_root}/+/alarm";
```

```csharp
// src/MqttMessaging/MqttMessageBusOptions.cs (change) — one new positional member
public sealed record QosOptions(MqttQualityOfService Command, MqttQualityOfService Ack,
    MqttQualityOfService Status, MqttQualityOfService Alarm);
```

```csharp
// src/MqttMessaging/MqttMessageBus.cs (change) — one switch arm
MessageFamily.Alarm => _options.Qos.Alarm,
```

```jsonc
// both appsettings.json files (change) — the family is config-driven
"Qos": { "Command": 1, "Ack": 1, "Status": 1, "Alarm": 1 }
```

```jsonc
// src/FleetManager/appsettings.json (change) — the cadence, no literal in code
"AlarmEvaluationIntervalMs": 1000
```

The serve loop, in shape (the owner writes it):

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

    public FleetServeLoop(IMessageBus bus, TopicScheme topics, ShuttleHealth health,
        AlarmEvaluator evaluator, string fleetManagerId, TimeSpan evaluationInterval, Func<DateTime> clock)
    {
        // validate every reference; reject a blank id and a non-positive interval
    }

    public Task SubscribeAsync(CancellationToken cancellationToken) =>
        _bus.SubscribeAsync(_topics.AllStatuses, MessageFamily.Status, HandleStatusAsync, cancellationToken);

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await SubscribeAsync(cancellationToken);
        using var timer = new PeriodicTimer(_evaluationInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await EvaluateAndPublishAsync(_clock(), cancellationToken);
    }

    public Task HandleStatusAsync(MessageReceived message, CancellationToken cancellationToken)
    {
        // deserialize; accept only a current-version shuttleStatus with a non-blank shuttle id
        // _health.RecordSeen(shuttleId, _clock()); add to _knownShuttles under _knownLock;
        // anything else returns without touching state
    }

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
}
```

`Program.cs` grows a `serve` command: read `Messaging`, `FleetManager:LivenessTimeoutMs`, `FleetManager:AlarmEvaluationIntervalMs`, and `FleetManager:LostCommunicationSeverity`; build `TopicScheme`, `ShuttleHealth`, `AlarmEvaluator`, and the real `MqttMessageBus`; connect; run the loop with `() => DateTime.UtcNow`; stop on Ctrl+C exactly like `ShuttleSim/Program.cs`. The existing `move` command keeps its behavior.

---

## 9. Where the new code lives

- **`src/Messaging/MessageFamily.cs`, `TopicScheme.cs`** — the transport vocabulary. Both sides compile against it.
- **`src/MqttMessaging/MqttMessageBusOptions.cs`, `MqttMessageBus.cs`** — the QoS binding and the family switch.
- **`src/FleetManager/FleetServeLoop.cs`** — the I/O-shaped serve loop. It lives in `FleetManager` next to `ShuttleHealth`, `AlarmEvaluator`, and `RunLoop`.
- **`src/FleetManager/Program.cs`** — the composition root's new `serve` command.
- **`src/FleetManager/appsettings.json`, `src/ShuttleSim/appsettings.json`** — the `Alarm` QoS and the evaluation interval.
- **`src/FleetManager.Tests/FleetServeLoopTests.cs`** — pure tests with a recording fake bus.
- **`src/FleetManager.Tests/FleetServeLoopIntegrationTests.cs`** — the real-broker check.
- **`src/Messaging.Tests/TopicSchemeTests.cs`** — one test for the new topics.
- **`src/FleetManager.Tests/FleetManager.Tests.csproj`** — a `ProjectReference` to `MqttMessaging` for the integration test.

Not changed: `src/Contracts/`, `docs/contract/`, `src/FleetManager/ShuttleHealth.cs`, `src/FleetManager/AlarmEvaluator.cs`, `src/FleetManager/RunLoop.cs`, `src/ShuttleCore/`, `src/TrafficCore/`, `src/FleetCore/`, `docker/Dockerfile.build`.

---

## 10. What would change these choices

- **The fleet manager becomes multi-instance** → the known-shuttle set and the alarm latch live in one process's memory. They must be shared (database or cache). Revisit B and record 21 §8 together.
- **A shuttle roster exists** (configured, or supplied by the dispatch path) → a never-seen shuttle becomes evaluable and the "never seen" branch of `ShuttleHealth` becomes reachable in the running system. Reopen B; the edge rule does not change.
- **Recovery and operator acknowledgement arrive** → they add a clear/ack message on the wire. That is a contract version bump (v4, or a further v3 kind) and a new publish method in the loop. Reopen D and F then.
- **The run loop and the serve loop must share one clock** → today `RunLoop` uses its simulated `_currentTime` and the serve loop uses `DateTime.UtcNow` (record 20 §6). A single clock domain is a later slice. Revisit only the clock input, not the cadence rule.
- **The dashboard wants a different alarm stream** → revisit the topic and `AllAlarms`, not the family or the QoS.
- **The real-broker integration test proves flaky or docker is unavailable in CI** → switch to an in-process broker (`MQTTnet.Server`) so the check needs no external service.
- **`AlarmEvaluationIntervalMs` must be derived** (for example, half the window) → change who computes it, not where it is read.

---

## 11. Verification table

Everything below was checked on **2026-10-14** by a command or a file read. The baseline suite ran against a clean copy of `src/` at `/tmp/opencode/l15base` (no `bin/` or `obj/` copied in). The toy ran at `/tmp/opencode/l15toy`. Both scratch directories are deleted afterwards. No file under `src/` was changed.

| Claim | How checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no database | `/tmp/opencode/l15base`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; **100** tests; `FleetManager.Tests` = **28**, `Messaging.Tests` = **8**, `MqttMessaging.Tests` = **2**, `Contracts.Tests` = **7**, `ShuttleCore.Tests` = **25**, `ShuttleSim.Tests` = **6**, `TrafficCore.Tests` = **6**, `FleetCore.Tests` = **18** |
| The contract already carries `alarm` at v3 | read `docs/contract/v3.md` | kinds row `| AlarmMessage | alarm | alarm |` (line 23); "Sent by a fleet manager to the bus on topic `{root}/{shuttleId}/alarm`" (line 40); `Version: 3` |
| `ContractVersion.Current` is 3 | read `src/Contracts/MessageTypes.cs` | `Current = 3`; `MessageTypes.Alarm = "alarm"` |
| The alarm shapes already exist | read `src/Contracts/Alarm.cs`, `AlarmMessage.cs` | `Alarm(ShuttleId, Severity, Code, Detail?)`; `AlarmMessage(MessageEnvelope Envelope, Alarm Alarm)` |
| `MessageFamily` has only three members | read `src/Messaging/MessageFamily.cs` | `Command`, `Ack`, `Status`; no `Alarm` |
| `TopicScheme` has no alarm topic | read `src/Messaging/TopicScheme.cs` | `Command`, `Ack`, `Status`, `AllAcks`, `AllStatuses`; no `Alarm`/`AllAlarms` |
| `QosOptions` has three members | read `src/MqttMessaging/MqttMessageBusOptions.cs` | `Command`, `Ack`, `Status` |
| The bus switch throws on an unknown family | read `src/MqttMessaging/MqttMessageBus.cs` | `_ => throw new ArgumentOutOfRangeException(nameof(family))` |
| The Qos blocks have three keys today | read `src/FleetManager/appsettings.json:9`, `src/ShuttleSim/appsettings.json:9` | `{ "Command": 1, "Ack": 1, "Status": 1 }` |
| Adding a `QosOptions` member breaks two test call sites | read `src/MqttMessaging.Tests/MqttMessageBusTests.cs:28,74` | two `new QosOptions(AtLeastOnce, AtLeastOnce, AtLeastOnce)` |
| There is no serve loop today | read `src/FleetManager/Program.cs` | only the no-arg banner and the one-shot `move` round trip |
| `FleetManager.Tests` cannot see MQTT types today | read `src/FleetManager.Tests/FleetManager.Tests.csproj` | references `FleetManager`, `Contracts`, `Messaging`, `TrafficCore`, `ShuttleCore`; not `MqttMessaging` |
| A `FakeBus` already exists in `FleetManager.Tests` | read `src/FleetManager.Tests/ReliableCommandSenderTests.cs:7` | `internal sealed class FakeBus`; the new fake must use a different name |
| `ShuttleHealth` is unchanged and pure | read `src/FleetManager/ShuttleHealth.cs` | `RecordSeen`/`IsUnresponsive`; no clock or I/O |
| `AlarmEvaluator` is unchanged and pure | read `src/FleetManager/AlarmEvaluator.cs` | `Evaluate(shuttleId, lost)`; one lock; no clock or I/O |
| The toy shows intake → cadence → one alarm per edge | `/tmp/opencode/l15toy`: `dotnet run` | SH-01 `PUBLISH` once at `00:00:06` and silent after; SH-02 `PUBLISH` at `00:00:08`, `CLEAR` at `00:00:09`; exit 0 |

Not yet verified: `FleetServeLoop`, the `MessageFamily.Alarm`/`TopicScheme.Alarm`/`QosOptions.Alarm` plumbing, the two config changes, the `serve` command, the recording fake, the seven new tests, and the integration check — this assignment specifies them and the reviewer measures them.

---

## 12. What this record unblocks

- Lesson 15 can assign a `FleetServeLoop`, the `Alarm` transport lane, the `serve` command, and the tests, with no unguided product choice.
- M5's "lost communication" becomes end-to-end: a heartbeat stops, the loop notices, and an alarm leaves the manager on `shuttle/{id}/alarm`.
- M7's dashboard has a real stream to subscribe to: `{root}/+/alarm`, at QoS 1, with severity in the body.
- The recovery slice gets a place to publish its own message: the same loop, a new publish method, a new kind.
