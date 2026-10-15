# Hints for lesson 15: The serve loop and the alarm publish path

**Decision:** [record 22](../decisions/22-serve-loop-and-alarm-publish.md) — a `FleetServeLoop`, the `Alarm` transport lane at QoS 1, and a testable publish seam. No contract bump.

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- **The loop is I/O-shaped code, so it lives in its own class.** `ShuttleHealth` and `AlarmEvaluator` are pure. `FleetServeLoop` is the thing that subscribes, evaluates on a cadence, and publishes. Put it at `src/FleetManager/FleetServeLoop.cs`, next to them. Do not fold it into `RunLoop` and do not put it in `Program.cs`.
- **The seam is `IMessageBus`.** One interface covers subscribe and publish. Depend on `Messaging.IMessageBus`, never on `MqttMessaging.MqttMessageBus`. In tests you pass a fake; `Program.cs` passes the real bus. `ShuttleEndpoint` already does exactly this.
- **The clock is a parameter, not `DateTime.UtcNow`.** Take a `Func<DateTime> clock`. `RunAsync` calls `_clock()` once per tick and hands that `now` to the sweep. The sweep never reads a clock. That is what makes the rising-edge tests pin a fixed time.
- **Evaluate the ids you have heard from, not the stale ones.** Keep a `HashSet<string> _knownShuttles`. The status handler adds an id. The sweep walks that set. Do **not** iterate `_health.UnresponsiveShuttles(now)` — a recovered shuttle is absent from that list, so its latch never clears and a later silence raises nothing.
- **The trigger is a timer, because the failure is silence.** A silent shuttle sends no message, so "evaluate on every inbound message" misses the whole point. Use a `PeriodicTimer` at `FleetManager:AlarmEvaluationIntervalMs`, the same tool `HeartbeatPublisher` uses.
- **The set is shared across two threads.** The bus calls the handler on its receive thread; the timer calls the sweep on the timer thread. Guard the `HashSet` with `_knownLock`. Copy the ids with `ToArray()` under the lock, then evaluate and publish outside it. Never hold the lock across an `await`.
- **A new kind is a lane, not a topic.** Four small things must line up: the enum member, the topic branch plus wildcard, the QoS value plus its bus switch arm, and the config key. Miss any one and the first publish throws.
- **Append the enum member.** `MessageFamily.Alarm` goes **last**, so `Command=0`, `Ack=1`, `Status=2` keep their numbers. `QosOptions.Alarm` goes **last** too.
- **No version bump.** `alarm` is already in `docs/contract/v3.md`. `ContractVersion.Current` stays `3`. Do not touch `docs/contract/` or `src/Contracts/`.
- **Config, never literals.** `"Alarm": 1` in **both** `appsettings.json` files (both build `QosOptions`). `"AlarmEvaluationIntervalMs": 1000` in the manager only. The sender is `Messaging:FleetManagerId`. The severity is `FleetManager:LostCommunicationSeverity`. The loop file names no topic, id, severity, or interval.
- **Name the fake `RecordingBus`.** `internal sealed class FakeBus` already exists in `ReliableCommandSenderTests.cs`. Pick a new name or you get a duplicate-type error.
- **Counts.** Total pure tests go **100 → 108**: `FleetManager.Tests` 28 → **35** (+7 loop tests), `Messaging.Tests` 8 → **9** (+1 topic test). The integration suite goes 2 → **3**. Gate: RC 0, 8 `Passed!`, 0 `Failed!`, 108 total. Two `new QosOptions(...)` call sites in `MqttMessageBusTests.cs` (lines 28 and 74) must gain a fourth value — no new test there.

---

## Tier 2 — direction

Work the eleven requirements in this order. Steps 1–4 make the build go green; steps 5–8 add the tests; steps 9–11 are the guardrails.

1. **The transport lane (won't build until all four land).**
   - `MessageFamily.cs`: append `Alarm`.
   - `TopicScheme.cs`: add `public string Alarm(string shuttleId) => $"{_root}/{shuttleId}/alarm";` and `public string AllAlarms => $"{_root}/+/alarm";`.
   - `MqttMessageBusOptions.cs`: append `MqttQualityOfService Alarm` as the last positional member of `QosOptions`.
   - `MqttMessageBus.cs`: add the switch arm `MessageFamily.Alarm => _options.Qos.Alarm,`.
   - Then fix the two `new QosOptions(...)` call sites. Let the compiler list them; it will.

2. **Configuration.**
   - Both `appsettings.json` files: `"Qos": { "Command": 1, "Ack": 1, "Status": 1, "Alarm": 1 }`.
   - `src/FleetManager/appsettings.json`: add `"AlarmEvaluationIntervalMs": 1000` under `FleetManager`, next to `LivenessTimeoutMs`.

3. **`FleetServeLoop.cs` — fields, constructor, subscribe, run.**
   - Fields and constructor exactly as the lesson spells them. Validate `bus`/`topics`/`health`/`evaluator`/`clock` for null (`ArgumentNullException`); `fleetManagerId` for blank (`ArgumentException`); `evaluationInterval <= TimeSpan.Zero` (`ArgumentOutOfRangeException`).
   - `SubscribeAsync` is one line: subscribe `_topics.AllStatuses` with `MessageFamily.Status` and `HandleStatusAsync`.
   - `RunAsync`: `await SubscribeAsync`, then `using var timer = new PeriodicTimer(_evaluationInterval); while (await timer.WaitForNextTickAsync(cancellationToken)) await EvaluateAndPublishAsync(_clock(), cancellationToken);`.

4. **`FleetServeLoop.cs` — intake and sweep.**
   - `HandleStatusAsync`: deserialize inside `try/catch (JsonException)`. Check the envelope's `ContractVersion == ContractVersion.Current`, `MessageType == MessageTypes.ShuttleStatus`, non-blank `MessageId`, and non-blank `Status.ShuttleId`. On pass: `_health.RecordSeen(id, _clock())`, then add to `_knownShuttles` under `lock (_knownLock)`. Return `Task.CompletedTask`.
   - `EvaluateAndPublishAsync(now, ...)`: copy the set under the lock into an array; for each id, `var lost = _health.IsUnresponsive(id, now);`, then `foreach (var alarm in _evaluator.Evaluate(id, lost)) await PublishAlarmAsync(alarm, now, cancellationToken);`.
   - `PublishAlarmAsync`: `MessageEnvelopes.Create(MessageTypes.Alarm, _fleetManagerId, sentAt)`, wrap in `new AlarmMessage(envelope, alarm)`, publish `JsonSerializer.Serialize(message)` on `_topics.Alarm(alarm.ShuttleId)` with `MessageFamily.Alarm`. Note `sentAt` is the parameter, **not** `DateTime.UtcNow`.

5. **The `serve` command in `Program.cs`.**
   - Keep the no-arg banner and the whole `move` path exactly as they are; add a branch for `args.Length == 1 && args[0] == "serve"`.
   - Read `Messaging` (bind both option records), `FleetManager:LivenessTimeoutMs`, `FleetManager:AlarmEvaluationIntervalMs`, `FleetManager:LostCommunicationSeverity`. Validate the two `...Ms` values are positive, like `move` does.
   - Build `TopicScheme`, `ShuttleHealth`, `AlarmEvaluator`, and the real `MqttMessageBus`; connect; run the loop with `() => DateTime.UtcNow`; stop on Ctrl+C.
   - Copy the Ctrl+C block from `src/ShuttleSim/Program.cs:105-135`: a `CancellationTokenSource`, a `Console.CancelKeyPress` handler that cancels and completes a `TaskCompletionSource`, `await stopped.Task`, unsubscribe the handler, cancel, then `await` the loop and swallow `OperationCanceledException` when cancelling. Use `await using` on the bus.

6. **`FleetServeLoopTests.cs` — the fake and the harness.**
   - `internal sealed class RecordingBus : IMessageBus`. Record `(Topic, Payload, Family)` for every publish; record the last subscribe filter and family; keep the captured handler in a field so a test can call it. `ConnectAsync`/`PublishAsync`/`SubscribeAsync` return `Task.CompletedTask`; `DisposeAsync` returns `ValueTask.CompletedTask`.
   - Harness: a `DateTime now = Start` captured variable, a `clock = () => now`, `new ShuttleHealth(TimeSpan.FromSeconds(3))`, `new AlarmEvaluator(AlarmSeverities.Critical)`, `new TopicScheme("shuttle")`, and `new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager", TimeSpan.FromSeconds(1), clock)`.
   - Feed a status by building a `ShuttleStatusMessage` with a current-version envelope and calling the captured handler. Drive the sweep by calling `EvaluateAndPublishAsync(now, CancellationToken.None)` directly — no timer.

7. **`FleetServeLoopTests.cs` — the seven test names.** Use the exact names from the lesson so Phase 7b finds them:
   - `The_constructor_rejects_a_blank_fleet_manager_id_and_a_non_positive_interval`
   - `Subscribe_uses_the_all_statuses_filter_and_the_status_family`
   - `A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender`
   - `A_latched_lost_shuttle_publishes_nothing_again`
   - `A_recovered_shuttle_clears_the_latch_and_can_raise_again`
   - `An_invalid_status_message_is_ignored`
   - `Two_shuttles_are_evaluated_independently`

8. **`FleetServeLoopIntegrationTests.cs`, `TopicSchemeTests.cs`, and the project reference.**
   - Add `<ProjectReference Include="..\MqttMessaging\MqttMessaging.csproj" />` to `FleetManager.Tests.csproj`.
   - Integration test: one `[Trait("Category", "Integration")]` test. Real `MqttMessageBus`, listener on `AllAlarms` + `MessageFamily.Alarm`, feed a status, advance the clock past the window, `EvaluateAndPublishAsync`, assert one `AlarmMessage` with `Code == AlarmCodes.LostCommunication`. Read host/port from config or environment, not a literal.
   - `TopicSchemeTests.cs`: `TopicScheme_alarm_topics_use_the_per_shuttle_branch_and_the_wildcard` asserts `Alarm("SH-01") == "shuttle/SH-01/alarm"` and `AllAlarms == "shuttle/+/alarm"`.

9. **Do not touch:** `src/Contracts/`, `docs/contract/`, `ShuttleHealth.cs`, `AlarmEvaluator.cs`, `RunLoop.cs`, `src/ShuttleCore/`, `src/TrafficCore/`, `src/FleetCore/`, `docker/Dockerfile.build`.

10. **No later-M5 scope.** No recovery, no ack, no reassignment, no restart reconciliation, no auto-release, no `blockedPath`/`palletNotDetected` raised anywhere, no roster, no WMS, no dashboard.

11. **Run the phases.** Phase 7 is the gate: `cd src && dotnet build`, then the filtered test run. Read `RC`, the `Passed!` count, and the total. They must be `0`, `8`, `0`, and `108`.

---

## Tier 3 — near-solution shapes

These are shapes, not the solution. You still write the tests, the `serve` wiring, and any small details.

**`MessageFamily.cs`**
```csharp
public enum MessageFamily
{
    Command,
    Ack,
    Status,
    Alarm,
}
```

**`TopicScheme.cs` additions**
```csharp
public string Alarm(string shuttleId) => $"{_root}/{shuttleId}/alarm";
public string AllAlarms => $"{_root}/+/alarm";
```

**`MqttMessageBusOptions.cs` (the record)**
```csharp
public sealed record QosOptions(MqttQualityOfService Command, MqttQualityOfService Ack,
    MqttQualityOfService Status, MqttQualityOfService Alarm);
```

**`FleetServeLoop.cs` (shape only)**
```csharp
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
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _topics = topics ?? throw new ArgumentNullException(nameof(topics));
        _health = health ?? throw new ArgumentNullException(nameof(health));
        _evaluator = evaluator ?? throw new ArgumentNullException(nameof(evaluator));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (string.IsNullOrWhiteSpace(fleetManagerId))
            throw new ArgumentException("A fleet manager id is required.", nameof(fleetManagerId));
        if (evaluationInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(evaluationInterval));
        _fleetManagerId = fleetManagerId;
        _evaluationInterval = evaluationInterval;
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
        ShuttleStatusMessage? status;
        try
        {
            status = JsonSerializer.Deserialize<ShuttleStatusMessage>(message.Payload);
        }
        catch (JsonException)
        {
            return Task.CompletedTask;
        }

        if (status?.Envelope is not { } envelope
            || envelope.ContractVersion != ContractVersion.Current
            || envelope.MessageType != MessageTypes.ShuttleStatus
            || string.IsNullOrWhiteSpace(envelope.MessageId)
            || string.IsNullOrWhiteSpace(status.Status?.ShuttleId))
        {
            return Task.CompletedTask;
        }

        var shuttleId = status.Status.ShuttleId;
        _health.RecordSeen(shuttleId, _clock());
        lock (_knownLock)
        {
            _knownShuttles.Add(shuttleId);
        }
        return Task.CompletedTask;
    }

    public async Task EvaluateAndPublishAsync(DateTime now, CancellationToken cancellationToken)
    {
        string[] ids;
        lock (_knownLock)
        {
            ids = _knownShuttles.ToArray();
        }

        foreach (var id in ids)
        {
            var lost = _health.IsUnresponsive(id, now);
            foreach (var alarm in _evaluator.Evaluate(id, lost))
                await PublishAlarmAsync(alarm, now, cancellationToken);
        }
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

**The `serve` command skeleton (inside `Program.cs`)**
```csharp
if (args.Length == 1 && args[0] == "serve")
    return await RunServeAsync();

async Task<int> RunServeAsync()
{
    var configuration = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
        .AddEnvironmentVariables()
        .Build();

    var messagingSection = configuration.GetSection("Messaging");
    var messaging = messagingSection.Get<MessagingOptions>()
        ?? throw new InvalidOperationException("Messaging configuration is missing.");
    var mqtt = messagingSection.Get<MqttMessageBusOptions>()
        ?? throw new InvalidOperationException("MQTT configuration is missing.");

    var livenessMs = configuration.GetValue<int>("FleetManager:LivenessTimeoutMs");
    if (livenessMs < 1)
        throw new InvalidOperationException("FleetManager:LivenessTimeoutMs must be positive.");
    var intervalMs = configuration.GetValue<int>("FleetManager:AlarmEvaluationIntervalMs");
    if (intervalMs < 1)
        throw new InvalidOperationException("FleetManager:AlarmEvaluationIntervalMs must be positive.");
    var severity = configuration.GetValue<string>("FleetManager:LostCommunicationSeverity")
        ?? throw new InvalidOperationException("FleetManager:LostCommunicationSeverity is missing.");

    var topics = new TopicScheme(messaging.TopicRoot);
    var health = new ShuttleHealth(TimeSpan.FromMilliseconds(livenessMs));
    var evaluator = new AlarmEvaluator(severity);
    await using var bus = new MqttMessageBus(mqtt);
    await bus.ConnectAsync(CancellationToken.None);
    var loop = new FleetServeLoop(bus, topics, health, evaluator, messaging.FleetManagerId,
        TimeSpan.FromMilliseconds(intervalMs), () => DateTime.UtcNow);

    Console.WriteLine($"Fleet manager serving on {topics.AllStatuses}");
    using var stopping = new CancellationTokenSource();
    var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        stopped.TrySetResult();
    };
    Console.CancelKeyPress += cancelHandler;
    var loopTask = loop.RunAsync(stopping.Token);
    try
    {
        await stopped.Task;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }
    stopping.Cancel();
    try
    {
        await loopTask;
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
    {
        // Expected: the loop stopped because we cancelled it.
    }
    return 0;
}
```

**`RecordingBus` (shape only)**
```csharp
internal sealed class RecordingBus : IMessageBus
{
    public List<(string Topic, string Payload, MessageFamily Family)> Published { get; } = new();
    public string? LastFilter { get; private set; }
    public MessageFamily? LastFamily { get; private set; }
    public Func<MessageReceived, CancellationToken, Task>? Handler { get; private set; }

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken cancellationToken)
    {
        Published.Add((topic, payload, family));
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string filter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        LastFilter = filter;
        LastFamily = family;
        Handler = handler;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

**A status fixture the handler accepts (verified shapes)**
```csharp
// ShuttleStatus(ShuttleId, State, Position, Timestamp, FaultReason?)
// MessageEnvelope(ContractVersion, MessageType, MessageId, CorrelationId, SenderId, SentAt)
static string StatusJson(string shuttleId, DateTime sentAt) => JsonSerializer.Serialize(
    new ShuttleStatusMessage(
        new MessageEnvelope(ContractVersion.Current, MessageTypes.ShuttleStatus,
            Guid.NewGuid().ToString(), null, shuttleId, sentAt),
        new ShuttleStatus(shuttleId, "idle", "A01-L01", sentAt, null)));
```
Both constructors above were read from `src/Contracts/ShuttleStatus.cs` and `src/Contracts/MessageEnvelope.cs` on 2026-10-14. `ShuttleStatusMessage` is `(MessageEnvelope Envelope, ShuttleStatus Status)`.

**Which mutation each test catches** (from the lesson's list):

- change `MessageFamily.Alarm` to `MessageFamily.Status` on the publish → `A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender` goes red;
- change `_topics.Alarm(id)` to `_topics.Status(id)` → same test goes red (wrong topic);
- replace `sentAt` with `DateTime.UtcNow` → same test goes red (envelope time no longer equals the sweep's `now`);
- hard-code the sender → same test goes red;
- delete `_knownShuttles.Add(...)` → four tests go red, `An_invalid_status_message_is_ignored` stays green;
- iterate `_health.UnresponsiveShuttles(now)` instead of the known set → `A_recovered_shuttle_clears_the_latch_and_can_raise_again` goes red;
- accept any version in `HandleStatusAsync` → `An_invalid_status_message_is_ignored` goes red;
- change the subscribe family → `Subscribe_uses_the_all_statuses_filter_and_the_status_family` goes red (make sure that test checks the family, or the arm is unpinned);
- make the sweep read `DateTime.UtcNow` instead of its `now` parameter → the fixed-clock tests go red.

---

## Worked example (teaching artifact — not the assignment)

This section shows the full working shape of the two tests you are stuck on. It is a teaching artifact on paper. You still write your own tests. The code below was compiled and run in a scratch project on 2026-10-14. Both tests passed. The shapes match the Tier 3 shapes above; no Tier 3 shape needed correcting.

### The corrected pure-test harness

**Root cause (a): `_knownShuttles` is only populated by `HandleStatusAsync`.**

`health.IsUnresponsive("SH-01", ...)` is a query. It asks a question. It changes no state. It does not add `"SH-01"` to `_knownShuttles`. The sweep iterates `_knownShuttles`. An empty set means nothing is published.

The only way an id enters `_knownShuttles` is through `HandleStatusAsync`. That method is the bus's receive handler. A pure test has no bus, so you must call the captured handler yourself.

**Root cause (b): the clock must line up.**

`HandleStatusAsync` calls `_health.RecordSeen(shuttleId, _clock())`. The sweep calls `_health.IsUnresponsive(id, now)`. Both must read the same clock. The test captures `now` in a variable and hands `() => now` to the loop. When the test advances `now`, both the record and the sweep see the new value.

Your current code sweeps with `now = Start`. But the status was never fed through the handler, so `_knownShuttles` is empty. Even if it were not empty, `Start` is not past the window. The sweep time must be past the window.

**The corrected test:**

```csharp
[Fact]
public async Task A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender()
{
    var bus = new RecordingBus();
    var topics = new TopicScheme("shuttle");
    var health = new ShuttleHealth(TimeSpan.FromSeconds(3));
    var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);

    var now = Start;
    var loop = new FleetServeLoop(bus, topics, health, evaluator, "fleet-manager",
        TimeSpan.FromSeconds(1), () => now);

    await loop.SubscribeAsync(CancellationToken.None);
    await bus.Handler!(new MessageReceived(topics.Status("SH-01"), StatusJson("SH-01", now)),
        CancellationToken.None);

    now = Start.AddSeconds(4);
    await loop.EvaluateAndPublishAsync(now, CancellationToken.None);

    Assert.Single(bus.Published);
    var (topic, payload, family) = bus.Published[0];
    Assert.Equal("shuttle/SH-01/alarm", topic);
    Assert.Equal(MessageFamily.Alarm, family);
    var alarm = JsonSerializer.Deserialize<AlarmMessage>(payload);
    Assert.NotNull(alarm);
    Assert.Equal(ContractVersion.Current, alarm!.Envelope.ContractVersion);
    Assert.Equal(MessageTypes.Alarm, alarm.Envelope.MessageType);
    Assert.Equal("fleet-manager", alarm.Envelope.SenderId);
    Assert.Null(alarm.Envelope.CorrelationId);
    Assert.Equal(now, alarm.Envelope.SentAt);
    Assert.Equal("SH-01", alarm.Alarm.ShuttleId);
    Assert.Equal(AlarmCodes.LostCommunication, alarm.Alarm.Code);
    Assert.Equal(AlarmSeverities.Critical, alarm.Alarm.Severity);
    Assert.Null(alarm.Alarm.Detail);
}
```

**What each part does:**

- `var now = Start;` — a captured variable. The test advances it. It is not a literal in the assertions.
- `() => now` — the clock. Both `RecordSeen` and the sweep read this.
- `await loop.SubscribeAsync(...)` — captures the handler in `bus.Handler`.
- `await bus.Handler!(...)` — feeds a status through the handler. This is the only way to add `"SH-01"` to `_knownShuttles` in a pure test. The `!` is a null-forgiving operator: `Handler` is nullable until `SubscribeAsync` sets it.
- `now = Start.AddSeconds(4)` — advances past the 3-second window. `IsUnresponsive` now returns `true`.
- `await loop.EvaluateAndPublishAsync(now, ...)` — one sweep. The evaluator's latch is empty, so it returns one alarm.
- The assertions check the full envelope and body, exactly as requirement 5 lists.

The `StatusJson` helper is the Tier 3 fixture. It builds a `ShuttleStatusMessage` with a current-version envelope.

### The integration test, fully worked

Your current file is a bare method at namespace level. It has no class, no `[Fact]`, no `async Task`. It will not compile or discover. Here is the full shape:

```csharp
using System.Text.Json;
using Contracts;
using Messaging;
using MqttMessaging;
using MQTTnet;

namespace FleetManager.Tests;

[Trait("Category", "Integration")]
public class FleetServeLoopIntegrationTests
{
    [Fact]
    public async Task An_alarm_reaches_the_broker_on_the_shuttle_topic()
    {
        var host = Environment.GetEnvironmentVariable("MQTT_HOST") ?? "127.0.0.1";
        var port = int.Parse(Environment.GetEnvironmentVariable("MQTT_HOST_PORT") ?? "1883");

        var listenerOptions = new MqttMessageBusOptions(host, port, "fleet-manager-test-listener",
            new QosOptions(MqttQualityOfService.AtLeastOnce, MqttQualityOfService.AtLeastOnce,
                MqttQualityOfService.AtLeastOnce, MqttQualityOfService.AtLeastOnce));
        var loopOptions = new MqttMessageBusOptions(host, port, "fleet-manager-test-loop",
            new QosOptions(MqttQualityOfService.AtLeastOnce, MqttQualityOfService.AtLeastOnce,
                MqttQualityOfService.AtLeastOnce, MqttQualityOfService.AtLeastOnce));

        var topics = new TopicScheme("shuttle");

        await using var listener = new MqttMessageBus(listenerOptions);
        await using var loopBus = new MqttMessageBus(loopOptions);
        await listener.ConnectAsync(CancellationToken.None);
        await loopBus.ConnectAsync(CancellationToken.None);

        var received = new TaskCompletionSource<AlarmMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        await listener.SubscribeAsync(topics.AllAlarms, MessageFamily.Alarm, (message, _) =>
        {
            received.TrySetResult(JsonSerializer.Deserialize<AlarmMessage>(message.Payload)!);
            return Task.CompletedTask;
        }, CancellationToken.None);

        var health = new ShuttleHealth(TimeSpan.FromSeconds(3));
        var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var loop = new FleetServeLoop(loopBus, topics, health, evaluator, "fleet-manager",
            TimeSpan.FromSeconds(1), () => now);

        await loop.HandleStatusAsync(new MessageReceived(topics.Status("SH-01"),
            StatusJson("SH-01", now)), CancellationToken.None);

        now = now.AddSeconds(4);
        await loop.EvaluateAndPublishAsync(now, CancellationToken.None);

        var alarm = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(AlarmCodes.LostCommunication, alarm.Alarm.Code);
    }

    static string StatusJson(string shuttleId, DateTime sentAt) => JsonSerializer.Serialize(
        new ShuttleStatusMessage(
            new MessageEnvelope(ContractVersion.Current, MessageTypes.ShuttleStatus,
                Guid.NewGuid().ToString(), null, shuttleId, sentAt),
            new ShuttleStatus(shuttleId, "idle", "A01-L01", sentAt, null)));
}
```

**What each part does and why:**

- **Two `MqttMessageBus` instances with distinct ClientIds.** MQTT gives each connection a ClientId. If two clients use the same ClientId, the broker disconnects the older one. The listener and the loop bus are two connections, so they need two ClientIds. `"fleet-manager-test-listener"` and `"fleet-manager-test-loop"` are test fixtures, not production literals. The existing `MqttMessageBusTests` uses the same pattern.

- **Host and port from environment, not a literal.** The test reads `MQTT_HOST` and `MQTT_HOST_PORT` with defaults `127.0.0.1` and `1883`. These are the same defaults the app's `appsettings.json` uses. The `docker/compose.yml` broker maps `MQTT_HOST_PORT` to the host port. This matches the `TestDatabase.BuildConnectionString` pattern in the same test project.

- **Subscribe before publish.** The broker does not retain alarm messages. The `MqttMessageBus` publish does not set the retain flag, and the mosquitto config does not enable it. A subscriber that subscribes after the publish misses the message. The listener subscribes to `topics.AllAlarms` with `MessageFamily.Alarm` before the loop publishes.

- **Four-member `QosOptions`.** The record has four positional members: `Command`, `Ack`, `Status`, `Alarm`. All four are `MqttQualityOfService.AtLeastOnce` (QoS 1). The alarm publishes at `QosOptions.Alarm`. This matches the existing `MqttMessageBusTests` pattern.

- **`await loop.HandleStatusAsync(...)` directly.** No wire status is needed. The test calls the loop's public method with a `MessageReceived` wrapping the status JSON. This adds `"SH-01"` to the loop's `_knownShuttles` and records the seen time.

- **Advance the fixed clock past the window.** The `ShuttleHealth` window is 3 seconds. The test sweeps at `Start.AddSeconds(4)`. `IsUnresponsive` returns `true`.

- **Await with a timeout.** `received.Task.WaitAsync(TimeSpan.FromSeconds(5))` waits for the listener's handler to fire. If the alarm never arrives, the test fails with a timeout rather than hanging.

- **`await using` disposes both buses.** `MqttMessageBus.DisposeAsync` disconnects from the broker. `await using` ensures both buses are disposed even if the test fails.

- **The `[Trait("Category", "Integration")]` attribute.** The pure test run excludes this test with `--filter "Category!=Integration"`. The test needs the broker up. Start it with `docker compose -f docker/compose.yml up -d broker`. The test does not mention or start Docker.

### Why this test is worth it

The pure tests prove the decision composition: the loop calls the right pure objects with the right inputs and publishes the right shape. But the pure tests use a fake bus. They cannot prove the alarm actually leaves the process.

The integration test proves the transport lane. It connects the real `MqttMessageBus` to the broker. It subscribes a listener on the wildcard topic. It asserts the alarm arrives on the wire with the right `Code`. This is the only test that would catch a break in the `MessageFamily.Alarm` to QoS binding, the topic branch, or the serialize-and-publish path.

The mutation list above is the proof. The pure tests catch mutations in the decision logic: wrong family, wrong topic, wrong clock, hard-coded sender. The integration test catches a mutation that breaks the transport itself. For example, if the `MessageFamily.Alarm` arm were deleted from the bus switch, the pure tests would still pass (they use a fake), but the integration test would fail with an `ArgumentOutOfRangeException` on the publish.

### Verification

The code in this section was compiled and run in a scratch xUnit project on 2026-10-14. Both tests passed:

- `A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender` — passed.
- `An_alarm_reaches_the_broker_on_the_shuttle_topic` — passed (against an in-process MQTTnet server; the docker-broker run itself is not yet verified in this environment).

Your current broken approach was also verified: calling `health.IsUnresponsive(...)` directly and sweeping with `now = Start` publishes nothing. This confirms both root causes.

Not yet verified: the integration test against the docker Mosquitto broker. Docker is not available in the environment where this was verified. The test shape is identical; only the host/port source differs (environment variables pointing at the in-process server vs. the docker broker).

---

## Final sanity check

- `FleetManager.Tests` shows **35**, `Messaging.Tests` shows **9**; total pure **108**; 8 `Passed!`, 0 `Failed!`, RC 0.
- The filtered run needs **no** database or broker (Phase 7). The integration test is excluded.
- `grep -nE 'DateTime\.UtcNow|MqttMessaging|Npgsql' src/FleetManager/FleetServeLoop.cs` prints nothing.
- `grep -nE 'lock *\(_knownLock\)' src/FleetManager/FleetServeLoop.cs` finds the guard.
- `src/Contracts/MessageTypes.cs` still says `Current = 3`; `docs/contract/v3.md` says `Version: 3`; no `v4.md`.
- `git diff --quiet HEAD -- src/Contracts docs/contract src/FleetManager/ShuttleHealth.cs src/FleetManager/AlarmEvaluator.cs src/FleetManager/RunLoop.cs` exits 0.
- `grep -rniE 'docker' src/*.Tests --include='*.cs'` prints nothing.
- Ctrl+C stops `serve` cleanly. No process is left running.
