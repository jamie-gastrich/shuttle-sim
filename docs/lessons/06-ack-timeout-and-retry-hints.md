# Hints for lesson 06: the acknowledgement timeout and the retry that closes M2

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- Three new types, three jobs: `RetryPolicy` (pure, no I/O — `Messaging`), `ReliableCommandSender`
  (the loop that needs a bus — `FleetManager`), and the config that feeds them. Ask which of the
  three is missing before you touch any code.
- **The envelope is built once, before the loop.** `MoveCommandMessage` (and its `MessageEnvelope`)
  are created by `Program.cs` and passed in. The sender serializes the *same object* every attempt.
  If you ever call `MessageEnvelopes.Create` inside the loop, you have made a second command.
- **The wait is one line:** `await waiter.Task.WaitAsync(policy.AckTimeout, cancellationToken)`.
  It throws `TimeoutException` when *this try* ran out, and `OperationCanceledException` when the
  *whole job* was cancelled. Retry only the first.
- **Retry a silence, never a rejection.** An ack with `Accepted = false` is a definitive answer:
  return `Rejected` immediately. Only a missing ack loops.
- **The waiter is correlated by id.** Register it for `command.Envelope.MessageId` once, before the
  loop. `OnAckReceivedAsync` matches `ack.Envelope.CorrelationId` to a waiting id and completes the
  wait; ignore anything it cannot parse or does not recognise.
- **Back off, then try again** — `await Task.Delay(policy.Backoff, cancellationToken)` **between**
  tries, never after the last one. With `Backoff = TimeSpan.Zero` the test runs instantly (measured:
  ~1 ms), so the suite never sleeps.
- The **overall** cap is still `FleetManager:RoundTripTimeoutMs` and it is still a single
  `CancellationTokenSource`. `AckTimeout` bounds one try; the overall bound keeps the whole loop
  finite. Worst case 3×2000 + 2×500 = 7000 ms < 10000 ms.
- **No wire change.** Contract `v2.md` stays `Version: 2`; no `v3.md`. A resend is the *same*
  `moveCommand`; v2 already has a stable `MessageId` and a `CorrelationId`.
- **`FleetManager.Tests.csproj` needs `..\Messaging\Messaging.csproj`** directly, like
  `ShuttleSim.Tests` already has, or the fake bus and `RetryPolicy` are not visible.
- **Fix the config binding first.** `appsettings.json` is copied only if the csproj has the
  `<None Update="appsettings.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>`
  item from lesson 05 — it should already be there. Add `AckTimeoutMs`, `MaxAttempts`, `BackoffMs`
  under `FleetManager` and read them in `Program.cs`.
- **R2/R4 stay fixed.** No bare `WaitForExit()`, no nine-space indent, trailing newline. Your new
  test file follows the same rules.

---

## Tier 2 — direction

- Order of work, and why:

  1. `RetryPolicy` — pure, three fields, three guards. Test it with no bus.
  2. `ReliableCommandSender` — the waiter dictionary, `OnAckReceivedAsync`, then `SendAsync`. Test
     each ending with the fake bus before wiring the app.
  3. `FleetManager.Tests` — add the `Messaging` project reference, then the sender tests. No broker.
  4. `appsettings.json` + `Program.cs` — config, then rewrite `move` to use the sender.
  5. The acceptance gates, then Phase 5a and 5b over the real broker.
- **The waiter.** A `Dictionary<string, TaskCompletionSource<CommandAckMessage>>` keyed by
  `MessageId`, guarded for thread safety (`ConcurrentDictionary` or a `lock`). Register **before**
  the first publish, or a fast ack can arrive before anyone is listening. Remove it in a `finally`.
- **`OnAckReceivedAsync`.** Parse with the lesson-05 two-step pattern: deserialize the envelope,
  check `MessageType == MessageTypes.CommandAck`, then deserialize the body. If
  `Envelope.CorrelationId` is null or not in the dictionary, return without touching anything.
  Otherwise `TrySetResult`. Catch `JsonException` and ignore a malformed payload.
- **`SendAsync`.** Build the waiter, then loop. On ack: `TrySetResult` already happened, so
  `await waiter.Task` returns; branch on `Accepted`. Wrap the wait in `try/catch (TimeoutException)`.
  Do not catch `OperationCanceledException` — let the caller's cancellation stop the loop.
- **The fake bus.** Extend lesson 05's `FakeBus`: record every `PublishAsync`, and expose a way to
  raise an ack (call the stored `OnAckReceivedAsync` with a crafted `CommandAckMessage`). For
  "drop the first ack", count publishes and only craft an ack on publish #2. `AckTimeout` of ~50 ms
  and `Backoff = TimeSpan.Zero` keeps it fast and deterministic.
- **The rewrite in `Program.cs`.** Same subscriptions as lesson 05; add the ack route. Build the
  command + envelope **once**. Create the `ReliableCommandSender`, `await SendAsync`, then branch on
  `Status`. On `Acknowledged`, wait for a status from that shuttle under the overall token. On
  `Rejected`/`NoAcknowledgement`, stderr + `return 1`. Keep the no-arg path first so M0 stays green.
- **Phase 5b ordering.** Start the manager *before* the shuttle, with short `AckTimeout` and more
  `MaxAttempts` via environment (`FleetManager__AckTimeoutMs=1000`, `FleetManager__MaxAttempts=6`).
  The first tries land with no listener and are lost; the shuttle comes up and a retry lands. Log
  each attempt (`attempt 2 of 6`) so the log proves the resend. If it passes but you did not see the
  shuffled retry in the log, lower `AckTimeout` or raise the pre-shuttle `sleep` and rerun.
- **Phase 5a no-sleep.** Lesson 05 needed `sleep 3` to let the shuttle subscribe. The retry removes
  that need: start the shuttle, run the manager immediately, and the sender's resends cover the
  subscription gap. If it passes *without* retry (one publish), that is fine too — the deterministic
  retry proof is Phase 3.

---

## Tier 2b — the fixed names the acceptance greps for

- `Messaging`: `RetryPolicy` (`MaxAttempts`, `AckTimeout`, `Backoff`) — `public sealed record`.
- `FleetManager`: `ReliableCommandSender`, `CommandSendStatus` (`Acknowledged`, `Rejected`,
  `NoAcknowledgement`), `CommandSendResult` (`Status`, `Attempts`, `Acknowledgement`).
- `FleetManager` config keys: `AckTimeoutMs`, `MaxAttempts`, `BackoffMs`, plus existing
  `RoundTripTimeoutMs`.
- Test names are listed in the assignment; the anchored `--list-tests` walk checks them.
- The exact grep strings: `sealed record RetryPolicy`, `enum CommandSendStatus`,
  `record CommandSendResult`, `policy.Backoff`, and the `ProjectReference` to `Messaging.csproj`.

---

## Tier 3 — near-solution

Templates, not a solution: the shapes and the fixed names come from record 10 and the assignment;
type every line yourself. Every name the acceptance greps for appears verbatim below.

### The policy

```csharp
// src/Messaging/RetryPolicy.cs
namespace Messaging;

public sealed record RetryPolicy(int MaxAttempts, TimeSpan AckTimeout, TimeSpan Backoff)
{
    public RetryPolicy
    {
        if (MaxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        if (AckTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(AckTimeout));
        if (Backoff < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Backoff));
    }
}
```

### The sender

```csharp
// src/FleetManager/ReliableCommandSender.cs — shape; you write the parsing and the dictionary
using System.Collections.Concurrent;
using System.Text.Json;
using Contracts;
using Messaging;

namespace FleetManager;

public enum CommandSendStatus { Acknowledged, Rejected, NoAcknowledgement }

public sealed record CommandSendResult(
    CommandSendStatus Status, int Attempts, CommandAckMessage? Acknowledgement);

public sealed class ReliableCommandSender
{
    private readonly IMessageBus _bus;
    private readonly TopicScheme _topics;
    private readonly RetryPolicy _policy;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandAckMessage>> _waiters = new();

    public ReliableCommandSender(IMessageBus bus, TopicScheme topics, RetryPolicy policy)
    {
        _bus = bus;
        _topics = topics;
        _policy = policy;
    }

    public Task OnAckReceivedAsync(MessageReceived message, CancellationToken cancellationToken)
    {
        // 1. parse envelope; not CommandAck -> return. Malformed -> return.
        // 2. deserialize CommandAckMessage; CorrelationId null or not in _waiters -> return.
        // 3. _waiters[id].TrySetResult(ack);
        return Task.CompletedTask;
    }

    public async Task<CommandSendResult> SendAsync(MoveCommandMessage command, CancellationToken cancellationToken)
    {
        var id = command.Envelope.MessageId;
        var waiter = new TaskCompletionSource<CommandAckMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters[id] = waiter;

        try
        {
            var payload = JsonSerializer.Serialize(command); // same bytes every attempt
            for (var attempt = 1; attempt <= _policy.MaxAttempts; attempt++)
            {
                await _bus.PublishAsync(_topics.Command(command.ShuttleId), payload, MessageFamily.Command, cancellationToken);
                try
                {
                    var ack = await waiter.Task.WaitAsync(_policy.AckTimeout, cancellationToken);
                    return ack.Accepted
                        ? new CommandSendResult(CommandSendStatus.Acknowledged, attempt, ack)
                        : new CommandSendResult(CommandSendStatus.Rejected, attempt, ack);
                }
                catch (TimeoutException)
                {
                    if (attempt < _policy.MaxAttempts)
                        await Task.Delay(_policy.Backoff, cancellationToken);
                }
            }
            return new CommandSendResult(CommandSendStatus.NoAcknowledgement, _policy.MaxAttempts, null);
        }
        finally
        {
            _waiters.TryRemove(id, out _);
        }
    }
}
```

`TaskCreationOptions.RunContinuationsAsynchronously` matters: it stops the ack-handling thread from
running the rest of `SendAsync` inline, which is how a completion source deadlocks a test.

### The fake bus for the sender tests

```csharp
// src/FleetManager.Tests/ReliableCommandSenderTests.cs — a fake, not the assignment's solution
internal sealed class FakeBus : IMessageBus
{
    public List<(string Topic, string Payload, MessageFamily Family)> Published { get; } = new();
    private Func<MessageReceived, CancellationToken, Task>? _handler;

    public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;

    public Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken ct)
    {
        Published.Add((topic, payload, family));
        return Task.CompletedTask;
    }

    public Task SubscribeAsync(string filter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken ct)
    {
        _handler = handler;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

For the "lost first ack" test, the test itself counts `Published.Count` after each publish (with a
short `AckTimeout`) and calls `sender.OnAckReceivedAsync(...)` with a crafted accepted
`CommandAckMessage` whose `CorrelationId` is the command id — only on the second publish. Assert
`Attempts == 2` and that both published payloads deserialize to the **same** `Envelope.MessageId`.
Be aware the test needs the sender to have subscribed, or call `OnAckReceivedAsync` directly as the
handler.

### appsettings.json

```jsonc
"FleetManager": {
  "RoundTripTimeoutMs": 10000,
  "AckTimeoutMs": 2000,
  "MaxAttempts": 3,
  "BackoffMs": 500
}
```

### The `Program.cs` rewrite, sketched

```csharp
// after building the bus and topics:
var retry = new RetryPolicy(
    Math.Max(1, config.GetValue<int>("FleetManager:MaxAttempts")),
    TimeSpan.FromMilliseconds(config.GetValue<int>("FleetManager:AckTimeoutMs")),
    TimeSpan.FromMilliseconds(Math.Max(0, config.GetValue<int>("FleetManager:BackoffMs"))));
var sender = new ReliableCommandSender(bus, topics, retry);

await bus.SubscribeAsync(topics.AllAcks, MessageFamily.Ack, sender.OnAckReceivedAsync, ct);
// ...status subscription as lesson 05...

using var overall = CancellationTokenSource.CreateLinkedTokenSource(ct);
overall.CancelAfter(roundTripTimeoutMs);

var command = new MoveCommandMessage(
    MessageEnvelopes.Create(MessageTypes.MoveCommand, fleetManagerId, sentAt), shuttleId, toLane); // ONCE
var result = await sender.SendAsync(command, overall.Token);
// branch: Acknowledged -> wait status under overall.Token, print, exit 0/1
//         Rejected      -> stderr reason, exit 1
//         NoAcknowledgement -> stderr "no ack after N attempts", exit 1
```

### Acceptance snippet you will retype (Phase 5a)

```bash
# From the repo root, after `docker compose up -d broker` and `cd src && dotnet build && cd ..`
dotnet src/ShuttleSim/bin/Debug/net10.0/ShuttleSim.dll serve > /tmp/shuttle.log 2>&1 &
dotnet src/FleetManager/bin/Debug/net10.0/FleetManager.dll move SH-01 A01-L02; echo "exit=$?"
kill %1
docker compose -f docker/compose.yml down
```

Expected: `exit=0`, manager output contains `"MessageType":"commandAck"`, `"Accepted":true`,
`"State":"moving"`, `"State":"idle"`, and the shuttle log has exactly one `"State":"moving"`.

---

What is deliberately *not* given here: the ack-parsing body, the four sender-test bodies, the
`Program.cs` rewrite in full, the exact `Task.WaitAsync` overload you will compile against (the
compiler and the .NET docs are the authority), and the Phase 5b log-format greps. The fixed facts
the acceptance greps for — the record and enum names, the config keys, the `policy.Backoff` call
site, the `Messaging` project reference, and the untouched `v2.md` — are all above. Type every line
yourself and be able to explain, out loud, why the envelope is built once, why only a silence is
retried, and why `Messaging` stays free of `MQTTnet`.
