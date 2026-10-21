# Hints for assignment 23: status push

Open these only when stuck. One tier at a time. Tier 3 is near the solution — try tier 1 first.

## Tier 1 — nudge

- Start with the pile, not the sender: `src/FleetCore/WmsStatusOutbox.cs` is a `HashSet<string>` plus a lock, and its trickiest method is four lines long. Copy the lock shape from `FleetServeLoop`'s `_knownLock`/`_knownShuttles` pair.
- `NoteSync` is one `if`. The outcome enum already exists (`OrderSyncOutcome`, `WmsOrder.cs:14`); the key is `outcome == OrderSyncOutcome.Advanced`. `AlreadyThere` and `NoOrder` do nothing — that is the design, not an omission.
- The route constant is one line beside `Orders` in `WmsRoutes.cs`, and its test is one line beside `Orders_route` in `WmsContractLiteralsTests.cs`. Copy the literal-pin habit from lesson 22: assert the string, not the constant against itself.
- A `500` reply does not throw — measured in the lesson. So the sender needs an `if (response.IsSuccessStatusCode)` you would not otherwise write. Miss it and mutation (d)'s cousin lurks: a rejected word that looks delivered.
- The timeout catch is the one real trap: `TaskCanceledException` derives from `OperationCanceledException`, and our shutdown throws the same family. The `when (!cancellationToken.IsCancellationRequested)` filter is what keeps Ctrl+C working. If you delete the filter, the pusher dies on the first Ctrl+C *or* swallows the first timeout — run both mentally.
- The pusher's tests need no sockets and no broker: construct `new HttpClient(new YourStubHandler()) { Timeout = … }` and point the pusher at it (the E3 probe in the lesson did exactly this). That is why no fact needs the `Integration` trait.
- The contract section text is pinned in record 31 §3 and quoted in the assignment. Transcribe it; do not improve it. Version 1 → 2, and the title's `v1` too.

## Tier 2 — direction

**Where each piece goes:**

- `src/FleetCore/WmsStatusOutbox.cs` (new): the pile. `HashSet<string>` with `StringComparer.Ordinal`, `object _gate`. Four members: `NoteSync`, `MarkPending`, `Claim`, `PendingCount`.
- `src/FleetCore/WmsRoutes.cs`: `public const string OrderStatus = "/api/order-status";` beside `Orders`.
- `src/FleetCore.Tests/WmsContractLiteralsTests.cs`: one new fact, the literal pin.
- `src/FleetCore.Tests/WmsStatusOutboxTests.cs` (new): the three pile facts.
- `src/FleetManager/WmsStatusPusher.cs` (new): ctor + `SendPendingAsync` + `RunAsync`.
- `src/FleetManager.Tests/WmsStatusPusherTests.cs` (new): the three push facts.
- `src/FleetManager/Program.cs`: three keys read + validated, three objects built, `pushTask` started and awaited.
- `src/FleetManager/appsettings.json`: the `Wms` section.
- `docs/contract/wms-rest.md`: version 1 → 2, the pinned section.

**The sender pass, order of branches (this order):**

1. `Claim()` once, outside the per-id work.
2. `TryGet` — no order means log and skip, never requeue (a requeued ghost loops forever).
3. `try`: serialize (`JsonSerializerDefaults.Web`), `PostAsync`, then check `IsSuccessStatusCode`. Success → delivered log. Failure → `MarkPending` + log.
4. `catch (HttpRequestException)` → `MarkPending` + log.
5. `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` → `MarkPending` + log.

**What each pusher test needs:** a stub `HttpMessageHandler` subclass with a queue of canned replies (500, then 200) or a `Task.Delay` for the timeout case. Arrange the world through the real `WmsOrderBoard` — submit, move the task, sync, note — then call `SendPendingAsync` directly. For the body assertion, parse with `JsonDocument` and `GetProperty("state")`: a `Contains("in_progress")` would not kill the PascalCase mutation, and the lesson's mutation table depends on it killing it.

**The two catches and why the filter is not optional:** the client timeout throws `TaskCanceledException` (measured, with a `TimeoutException` inner); our shutdown cancels the token and `PeriodicTimer` throws the same family. `when (!cancellationToken.IsCancellationRequested)` sorts them: timeout → requeue; shutdown → let it fly to Program's `catch`, which already exists for `loopTask` (`Program.cs:212-219`) — copy its shape for `pushTask`.

**Config validation:** copy `Program.cs:143-155`'s pattern for each key. The URL is the odd one: `Uri.TryCreate(value, UriKind.Absolute, out var uri)` must succeed, else throw — startup, not first send.

## Tier 3 — near-solution skeleton

This is the shape, with the interesting lines left for you to write.

```csharp
// src/FleetCore/WmsStatusOutbox.cs
namespace FleetCore;

public sealed class WmsStatusOutbox
{
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void NoteSync(string taskId, OrderSyncOutcome outcome)
    {
        // TODO: mark pending if and only if the outcome means the word moved
    }

    public void MarkPending(string orderId)
    {
        // TODO: under the lock; adding twice leaves one flag
    }

    public List<string> Claim()
    {
        // TODO: under the lock — copy out, clear, return the copy
    }

    public int PendingCount { get { /* TODO: under the lock */ } }
}
```

```csharp
// src/FleetManager/WmsStatusPusher.cs — the members, not the bodies
public sealed class WmsStatusPusher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    // TODO: _board, _outbox, _client, _interval fields; ctor with null guards
    //       and interval > TimeSpan.Zero else ArgumentOutOfRangeException

    public async Task SendPendingAsync(CancellationToken cancellationToken)
    {
        // TODO: claim once; foreach id:
        //   TryGet -> log+continue if missing
        //   try: PostAsync(WmsRoutes.OrderStatus, json content, ct)
        //        2xx -> delivered log; else -> MarkPending + log
        //   catch HttpRequestException -> MarkPending + log
        //   catch OperationCanceledException when (!ct.IsCancellationRequested) -> MarkPending + log
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // TODO: PeriodicTimer(_interval); while WaitForNextTickAsync(ct) -> await SendPendingAsync(ct)
    }
}
```

```csharp
// src/FleetManager/Program.cs — inside RunServeAsync only, three spots
// 1. with the other keys:   Wms:BaseUrl (TryCreate absolute), Wms:StatusTimeoutMs, Wms:StatusIntervalMs
// 2. after the board:       var outbox = new WmsStatusOutbox();
//                           using var wmsClient = new HttpClient { BaseAddress = wmsUri, Timeout = ... };
//                           var pusher = new WmsStatusPusher(board, outbox, wmsClient, ...);
// 3. beside loopTask:       var pushTask = pusher.RunAsync(stopping.Token);
//   after the loopTask await block:
//                           try { await pushTask; }
//                           catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
```

```json
// src/FleetManager/appsettings.json — the new section, beside the others
"Wms": {
  "BaseUrl": "http://127.0.0.1:5090",
  "StatusTimeoutMs": 2000,
  "StatusIntervalMs": 1000
}
```

For the contract, open `docs/contract/wms-rest.md`, set `Version: 2` and the title's `v2`, and paste record 31 §3's section verbatim before "Changing this contract." Then stop: the contract page does not get a second opinion from you — that is what the decision record was for.
