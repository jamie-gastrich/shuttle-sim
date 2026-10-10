# Hints for lesson 05: the message contract and the broker boundary

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- Four new projects, four jobs: `Messaging` (the domain-free bus interface, topics, envelope
  factory, idempotency guard — references **`Contracts` only**), `MqttMessaging` (the only project
  that sees `MQTTnet`), `Messaging.Tests`, `MqttMessaging.Tests`. Ask which of the four is missing
  before you touch any code.
- The **envelope** is one record with six fields, nested as the `Envelope` property of every
  message. `ContractVersion.Current` is `2`; the three wire kinds are `moveCommand`, `commandAck`,
  `shuttleStatus`. `ShuttleStatus` itself does not change — v2 wraps it.
- The **ack** carries `CorrelationId` = the command's `MessageId`. That is the only way the manager
  matches a reply to its cause; an MQTT topic cannot carry it.
- **No topic literal outside `TopicScheme`.** `{root}/{shuttleId}/{kind}`, with the manager
  subscribing `{root}/+/ack` and `{root}/+/status`. The root, host, port, ids, and QoS come from
  `appsettings.json` + environment variables. The acceptance's no-literal/no-sleep greps are scoped
  to **app code** (`--exclude-dir='*.Tests'`): your `TopicSchemeTests` may assert whole topics and
  your in-process broker test may name loopback.
- **QoS 1 is at-least-once**: the broker *really* delivers duplicates (you can see it). That is
  why the idempotency guard is not optional — it is the bill for choosing QoS 1.
- **Remember effects, not messages.** `MarkCompleted` only for an **accepted** command. A duplicate
  accepted command → re-ack, do not move. A rejected command is not recorded → a retry re-checks.
- The endpoint checks the guard **before** it calls the state machine. Order is the whole safety
  argument.
- Do **not** sleep or `Task.Delay` — the shuttle publishes `moving` at `now` and `idle` at
  `now + TravelTimeTo(toLane)` and returns. Lesson 04's "time is an input", now on the wire.
- The manager's one command can be lost if the shuttle has not subscribed yet (no retry yet). The
  acceptance orders the two processes with a short `sleep`; lesson 06 removes it. Do not "fix" it
  by adding retry — that is the next lesson.
- Fix **R2/R4** in `src/FleetManager.Tests/StartTests.cs` first: bounded `WaitForExit(<timeout>)`,
  no bare `WaitForExit()`, no nine-space indent, trailing newline. The M1 demo test is the pattern.

---

## Tier 2 — direction

- Order of work, and why:

  1. `Contracts` — the envelope, the constants, then the three messages. Nothing depends on the
     network; everything else compiles against these.
  2. `Messaging` — `MessageFamily`, `MessageReceived`, `IMessageBus`, `MessagingOptions`,
     `TopicScheme`, `MessageEnvelopes`, `IdempotencyGuard`. Pure; test as you go.
  3. `Messaging.Tests` — topics and the guard first; they need no broker and they are the concepts.
  3. `MqttMessaging` — `MqttQualityOfService`, `MqttMessageBusOptions`, `MqttMessageBus`. Build it
   against the in-process broker as you go.
- Then `ShuttleCore.TravelTimeTo` (one line), then `ShuttleEndpoint` (tested with a fake bus), then
  the two apps, then `docs/contract/v2.md`.
- **Two-step parse.** Deserialize the `Envelope` first (a tiny private record or `JsonDocument`),
  switch on `MessageType`, then deserialize the full message. The kind decides the body type.
- **The project graph is a gate.** `Messaging` may reference `Contracts` and nothing else — not
  `MQTTnet`, not `ShuttleCore`, not the apps. `MqttMessaging` references `Messaging`, `Contracts`,
  `MQTTnet`. `ShuttleCore` still references nothing.
- **The fake bus.** In `Messaging.Tests` and `ShuttleSim.Tests`, write a small `FakeBus :
  IMessageBus` that records published `(Topic, Payload, Family)` and can invoke a stored
  subscription handler. No network, no Docker — that is the entire reason `IMessageBus` exists.
- **The in-process broker.** `new MqttServerFactory().CreateServerOptionsBuilder()
  .WithDefaultEndpoint().WithDefaultEndpointPort(0).Build()`; after `StartAsync()`, read the bound
  port from `serverOptions.DefaultEndpointOptions.Port` (the options object is updated with the real
  port — `GetDefaultEndpoint()` does **not** exist in MQTTnet 5). Start it in the test, dispose it
  in a `finally` or via `IAsyncLifetime`.
- **Config binding.** `new ConfigurationBuilder().SetDataSource(...)` is not needed; use
  `AddJsonFile("appsettings.json")` then `AddEnvironmentVariables()`, and
  `configuration.GetSection("Messaging").Get<MessagingOptions>()`. Add
  `<None Update="appsettings.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`
  so the file lands beside the dll.
- **The manager bound.** Use a `CancellationTokenSource` with
  `CancelAfter(options.RoundTripTimeoutMs)` and await the messages until the token trips; print,
  then return `0` or `1`. No `Thread.Sleep`, no unbounded `await Task.Delay(Timeout.Infinite)`.
- **The E2E ordering.** Build first, start the shuttle, give it a moment to subscribe, then run the
  manager once. If the manager exits `1`, check the shuttle connected (broker up, same
  `TopicRoot`/`Host`/`Port` on both sides) before suspecting the logic.
- **R2/R4.** Copy the shape from `ShuttleSim.Tests/DemoTests.cs`: a `const int …TimeoutMs`, a
  `WaitForExit(<timeout>)`, and `Kill(entireProcessTree: true)` when it overruns. Then fix the
  indentation and the trailing newline.

---

## Tier 2b — the fixed names the acceptance greps for

- Projects: `Messaging`, `Messaging.Tests`, `MqttMessaging`, `MqttMessaging.Tests`.
- `Contracts`: `MessageEnvelope`, `MessageTypes` (`MoveCommand`, `CommandAck`, `ShuttleStatus`),
  `ContractVersion.Current`, `MoveCommandMessage`, `CommandAckMessage`, `ShuttleStatusMessage`.
- `Messaging`: `MessageFamily`, `MessageReceived`, `IMessageBus`, `MessagingOptions`, `TopicScheme`,
  `MessageEnvelopes`, `IdempotencyGuard`.
- `MqttMessaging`: `MqttQualityOfService`, `MqttMessageBusOptions`, `QosOptions`, `MqttMessageBus`.
- `ShuttleSim`: `ShuttleEndpoint`, and `Shuttle.TravelTimeTo`.
- Test names are listed in the assignment; the anchored `--list-tests` walk checks them.

---

## Tier 3 — near-solution

Templates, not a solution: the shapes and the fixed names come from the records and the assignment;
type every line yourself. Every name the acceptance greps for appears verbatim below.

### Contract v2

```csharp
// src/Contracts/MessageEnvelope.cs
namespace Contracts;

public sealed record MessageEnvelope(
    int ContractVersion,
    string MessageType,
    string MessageId,
    string? CorrelationId,
    string SenderId,
    DateTime SentAt);
```

```csharp
// src/Contracts/MessageTypes.cs
namespace Contracts;

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

```csharp
// src/Contracts/MoveCommandMessage.cs
namespace Contracts;

public sealed record MoveCommandMessage(MessageEnvelope Envelope, string ShuttleId, string ToLane);
```

```csharp
// src/Contracts/CommandAckMessage.cs
namespace Contracts;

public sealed record CommandAckMessage(MessageEnvelope Envelope, bool Accepted, string? Reason);
```

```csharp
// src/Contracts/ShuttleStatusMessage.cs
namespace Contracts;

public sealed record ShuttleStatusMessage(MessageEnvelope Envelope, ShuttleStatus Status);
```

### Messaging

```csharp
// src/Messaging/MessageFamily.cs
namespace Messaging;

public enum MessageFamily { Command, Ack, Status }
```

```csharp
// src/Messaging/MessageReceived.cs
namespace Messaging;

public sealed record MessageReceived(string Topic, string Payload);
```

```csharp
// src/Messaging/IMessageBus.cs
namespace Messaging;

public interface IMessageBus : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken cancellationToken);
    Task SubscribeAsync(string topicFilter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken cancellationToken);
}
```

```csharp
// src/Messaging/MessagingOptions.cs
namespace Messaging;

public sealed record MessagingOptions(
    string Host,
    int Port,
    string ClientId,
    string TopicRoot,
    string FleetManagerId,
    int IdempotencyCacheSize);
```

```csharp
// src/Messaging/TopicScheme.cs
namespace Messaging;

public sealed class TopicScheme
{
    private readonly string _root;

    public TopicScheme(string topicRoot)
    {
        if (string.IsNullOrWhiteSpace(topicRoot))
            throw new ArgumentException("topic root is required", nameof(topicRoot));
        _root = topicRoot.Trim('/');
    }

    public string Command(string shuttleId) => $"{_root}/{shuttleId}/command";
    public string Ack(string shuttleId) => $"{_root}/{shuttleId}/ack";
    public string Status(string shuttleId) => $"{_root}/{shuttleId}/status";
    public string AllAcks => $"{_root}/+/ack";
    public string AllStatuses => $"{_root}/+/status";
}
```

```csharp
// src/Messaging/MessageEnvelopes.cs
using Contracts;

namespace Messaging;

public static class MessageEnvelopes
{
    public static MessageEnvelope Create(string messageType, string senderId, DateTime sentAt,
        string? correlationId = null) =>
        new(ContractVersion.Current, messageType, Guid.NewGuid().ToString(), correlationId, senderId, sentAt);
}
```

```csharp
// src/Messaging/IdempotencyGuard.cs
namespace Messaging;

public sealed class IdempotencyGuard
{
    private readonly int _capacity;
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _completed;

    public IdempotencyGuard(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _completed = new HashSet<string>();
    }

    public bool IsDuplicate(string messageId) => _completed.Contains(messageId);

    public void MarkCompleted(string messageId)
    {
        if (_completed.Add(messageId)) _order.Enqueue(messageId);
        while (_order.Count > _capacity) _completed.Remove(_order.Dequeue());
    }
}
```

The eviction test (`IdempotencyGuard_forgets_the_oldest_id_at_capacity`) is the one to watch: with
`capacity: 2`, marking three ids must forget the **first**, not the last. Run that test after you
type it — it is the difference between a bounded cache and a memory leak with a friendly name.

### MqttMessaging (the adapter)

Check the exact MQTTnet 5 method names — the shapes below are the intent, and your compile step is
the authority.

```csharp
// src/MqttMessaging/MqttQualityOfService.cs
namespace MqttMessaging;

public enum MqttQualityOfService { AtMostOnce = 0, AtLeastOnce = 1, ExactlyOnce = 2 }
```

```csharp
// src/MqttMessaging/MqttMessageBusOptions.cs
namespace MqttMessaging;

public sealed record QosOptions(MqttQualityOfService Command, MqttQualityOfService Ack,
    MqttQualityOfService Status);

public sealed record MqttMessageBusOptions(string Host, int Port, string ClientId, QosOptions Qos);
```

`MqttMessageBus` implements `IMessageBus`. Keep a `Dictionary<MessageFamily, MqttQualityOfService>`
built from the options; map `MessageFamily` to `MQTTnet`'s `MqttQualityOfServiceLevel`; attach
`ApplicationMessageReceivedAsync` to raise your `MessageReceived`; on `SubscribeAsync`, register the
handler in a dictionary keyed by topic filter and `await client.SubscribeAsync(filter, qos)`.

### ShuttleCore addition and ShuttleEndpoint

```csharp
// src/ShuttleCore/Shuttle.cs — add one method
public TimeSpan TravelTimeTo(string lane) =>
    TimeSpan.FromTicks(_rack.DistanceInLanes(_position, lane) * _timing.MovePerLane.Ticks);
```

```csharp
// src/ShuttleSim/ShuttleEndpoint.cs — shape; you write the parsing and the publish calls
public sealed class ShuttleEndpoint
{
    public ShuttleEndpoint(Shuttle shuttle, IMessageBus bus, TopicScheme topics,
        string shuttleId, IdempotencyGuard guard) { /* store fields */ }

    public async Task HandleCommandAsync(string payload, DateTime now, CancellationToken ct)
    {
        // 1. parse envelope; wrong version / wrong kind -> return, publish nothing
        // 2. deserialize MoveCommandMessage; not our ShuttleId -> return
        // 3. if (_guard.IsDuplicate(command.Envelope.MessageId)) { await Ack(accepted: true, null); return; }
        // 4. try StartMove(command.ToLane, now) catch ShuttleRejectedException -> Ack(false, ex.Message)
        // 5. _guard.MarkCompleted(id); Ack(true, null);
        //    Status("moving", now);
        //    var arrival = now + _shuttle.TravelTimeTo(command.ToLane);
        //    _shuttle.CompleteMove(arrival);
        //    Status("idle", arrival);
    }
}
```

The arrival status uses `now + TravelTimeTo(toLane)` computed **after** `StartMove` — position does
not change until arrival, so the distance is still from the origin.

### A fake bus for the endpoint tests

```csharp
// src/ShuttleSim.Tests or Messaging.Tests
internal sealed class FakeBus : IMessageBus
{
    public List<(string Topic, string Payload, MessageFamily Family)> Published { get; } = new();
    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
    public Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken ct)
    { Published.Add((topic, payload, family)); return Task.CompletedTask; }
    public Task SubscribeAsync(string filter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken ct) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

Then a duplicate test is: publish the same `MoveCommandMessage` payload twice into
`HandleCommandAsync` and assert the shuttle is idle at the destination and exactly **one** pair of
statuses was published (or that `Published` has one `moving`).

### appsettings.json (both apps)

```jsonc
{
  "Messaging": {
    "Host": "127.0.0.1",
    "Port": 1883,
    "ClientId": "fleet-manager",
    "TopicRoot": "shuttle",
    "FleetManagerId": "fleet-manager",
    "IdempotencyCacheSize": 1000,
    "Qos": { "Command": 1, "Ack": 1, "Status": 1 }
  },
  "Shuttle": { "Id": "SH-01", "StartLane": "A01-L01" },
  "FleetManager": { "RoundTripTimeoutMs": 10000 }
}
```

### Acceptance snippet you will retype

```bash
# From the repo root, after `docker compose up -d broker`
cd src && dotnet build ShuttleFleet.slnx >/dev/null 2>&1 && cd ..
dotnet src/ShuttleSim/bin/Debug/net10.0/ShuttleSim.dll serve > /tmp/shuttle.log 2>&1 &
sleep 3
dotnet src/FleetManager/bin/Debug/net10.0/FleetManager.dll move SH-01 A01-L02; echo "exit=$?"
```

Expected: `exit=0`, and the manager's output contains `"MessageType":"commandAck"`,
`"Accepted":true`, `"State":"moving"`, and `"State":"idle"`. Then `kill` the shuttle and
`docker compose -f docker/compose.yml down`.

---

What is deliberately *not* given here: the bodies of the thirteen named tests beyond the fake-bus
shape, the MQTTnet call sites (your compiler and the library docs are the authority), the parsing
code, the reconnection concerns (lesson 06+), the manager's `move` implementation, and
`docs/contract/v2.md` — that document is yours to write. The fixed facts the acceptance greps for —
the four projects and their edges, the record and constant names, the three wire kinds, the
`TopicScheme` shapes, the bounded `IdempotencyGuard`, the endpoint's ack-and-status behaviour, and
the six `Passed!` lines — are all above. Type every line yourself and be able to explain, out loud,
why QoS 1 forces the guard and why `ShuttleCore` still references nothing.
