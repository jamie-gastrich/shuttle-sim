# Hints for Lesson 28: The mock WMS and delivery reliability

Three tiers. Open the tier that matches how stuck you are.

## Nudge (tier 1)

You are here because you forgot one detail. Choose the hint that matches.

1. **The mock listens on the wrong port.** `dotnet new web` writes `Properties/launchSettings.json` with a random port. That file wins over `appsettings.json` when you run `dotnet run`. Delete the `Properties` folder, or change its URL to `http://127.0.0.1:5090`. Then check with `ss -ltn '( sport = :5090 )'` while the mock runs.

2. **`slow` mode must apply the word before it waits.** The order of the two steps is the whole lesson. Apply first, then `await Task.Delay(...)`, then answer `200`. If you delay first, the retry is not a duplicate and you have not modelled the lost reply.

3. **`error` and `hang` must not apply the word.** In `error` mode, answer `500` before the ledger is touched. In `hang` mode, do not touch the ledger at all. Only `ok` and `slow` call `Apply`.

4. **A permanent failure must not go back in the outbox.** `WmsStatusOutbox.Claim()` already took the id out. `Delivered` and `Permanent` leave it out. Only `Retry` calls `MarkPending`.

5. **The old test now fails for the right reason.** `Push_failure_keeps_the_order_pending_for_the_next_pass` calls `SendPendingAsync` twice with no clock movement. The second call is now "not due" and skips the send. Pass a fake clock and advance it by the base delay between the two calls.

6. **The ledger checks identity before it checks rank.** A different `kind`, `palletId`, or `laneId` for the same order id is a `Conflict`, even if the state word looks newer. The order id means the same work, or it is an error.

7. **`cancelled` is rank 3, not rank 2.** `done` and `failed` share rank 2. `cancelled` sits above both terminal words. A later `done` for a cancelled order is stale.

---

## Direction (tier 2)

You need more than a nudge. These hints point you at the right file and the right shape.

1. **Scaffold the two projects from `src`.** Do not hand-write the project files.

   ```bash
   cd src
   dotnet new web -o MockWms -f net10.0
   dotnet new xunit -o MockWms.Tests -f net10.0
   dotnet add MockWms.Tests/MockWms.Tests.csproj reference MockWms/MockWms.csproj
   dotnet sln ShuttleFleet.slnx add MockWms/MockWms.csproj
   dotnet sln ShuttleFleet.slnx add MockWms.Tests/MockWms.Tests.csproj
   rm -rf MockWms/Properties
   ```

   `MockWms` references nothing else. It must not reference `FleetCore` or `Contracts`.

2. **`MockOrder` is the mock's own copy of the body.** Give it the seven fields from `docs/contract/wms-rest.md`: `orderId`, `kind`, `palletId`, `laneId`, `state`, `receivedAt`. Add a `TryCreate` or a validation method that rejects a missing id, an unknown kind, an unknown state, and a bad lane id with `400`. Do not reuse the fleet manager's record.

3. **`MockWmsLedger.Apply` has four answers.** Put a `lock` around the whole check-and-write. Look up by `orderId`. Check identity, then same word, then rank, in that order. The toy's `ToyLedger.Put` in the lesson has the same shape; the real one adds `Conflict` and counters.

4. **`MockWmsControl` is one small locked object.** It holds the current mode and the slow delay. `POST /mock/mode` parses the body, rejects an unknown mode with `400`, updates the mode, and updates the delay only when `slowDelayMs` is present. The push route reads the mode once at the start of the request, so a mode change mid-request does not split one request into two behaviours.

5. **The push route reads the mode once.** A switch over `ok`, `slow`, `error`, `hang` is easier to defend than four `if` statements. In `slow`, call `Apply`, then `Task.Delay(delay, cancellationToken)`, then return `200` with the apply result. In `hang`, `await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)` and never return.

6. **`WmsDeliveryOutcome` is a pure function in `FleetCore`.** No `HttpClient`, no logging, no clock. Its input is `int?`. `null` means "no reply". Follow §1 of the lesson exactly, including the final `return Retry` for `1xx` and `3xx`.

7. **`WmsRetryPolicy` is also pure.** Constructor takes `TimeSpan baseDelay, TimeSpan maxDelay`. Reject `baseDelay <= 0`, and `maxDelay < baseDelay`. `DelayFor(int failures)` throws for `failures < 1`, starts at the base, doubles for each earlier failure, and clamps with `Math.Min`.

8. **The pusher gets a new constructor and keeps the old one.** The new one takes the board, outbox, client, interval, retry policy, failure board, and a clock `Func<DateTime>`. Keep the four-argument constructor for the existing tests and pass `new WmsRetryPolicy(interval, interval)`, a new `WmsDeliveryFailures()`, and `() => DateTime.UtcNow`. A fixed wait of one interval matches the old "try again next tick" behaviour.

9. **The pusher's pass has three branches.** Read `now` once before the loop. Read the reply once. Then `Delivered`, `Permanent`, or `Retry`. Network and timeout catches pass `null` to the rule. Keep the existing `OperationCanceledException when (!cancellationToken.IsCancellationRequested)` filter: our shutdown must still propagate.

10. **`Program.cs` builds the policy and the failure board once.** Read `Wms:RetryBaseDelayMs` and `Wms:MaxRetryDelayMs`, validate them, construct the policy and the board, pass both into the pusher, and map `GET /wms/delivery-failures` to `failures.Snapshot()`. Add `DeliveryFailures` to `WmsRoutes` rather than typing the path twice.

11. **The tests need a clock.** `WmsStatusPusher` takes `Func<DateTime> now`. In tests, hold a `DateTime` variable, pass `() => clock`, and move `clock` forward between calls. Without that, you cannot prove a wait happened.

---

## Near-solution (tier 3)

You are this close. These hints show almost the complete shape — use them only if you have been stuck for a while and need to see the full picture to get moving. Type them in and adapt the names; do not paste a whole file without reading it.

1. **The ledger shape.**

   ```csharp
   public enum ApplyResult { Applied, Duplicate, Stale, Conflict }

   public sealed class MockWmsLedger
   {
       private readonly object _gate = new();
       private readonly Dictionary<string, MockOrder> _orders = new(StringComparer.Ordinal);
       private int _applied, _duplicate, _stale, _conflict;

       public ApplyResult Apply(MockOrder order)
       {
           lock (_gate)
           {
               if (_orders.TryGetValue(order.OrderId, out var old))
               {
                   if (old.Kind != order.Kind || old.PalletId != order.PalletId || old.LaneId != order.LaneId)
                   {
                       _conflict++;
                       return ApplyResult.Conflict;
                   }
                   if (old.State == order.State)
                   {
                       _duplicate++;
                       return ApplyResult.Duplicate;
                   }
                   if (Rank(order.State) <= Rank(old.State))
                   {
                       _stale++;
                       return ApplyResult.Stale;
                   }
               }

               _orders[order.OrderId] = order;
               _applied++;
               return ApplyResult.Applied;
           }
       }

       public IReadOnlyList<MockOrder> Snapshot()
       {
           lock (_gate)
               return _orders.Values.OrderBy(o => o.OrderId, StringComparer.Ordinal).ToList();
       }

       // Rank: received=0, in_progress=1, done=2, failed=2, cancelled=3.
   }
   ```

   Add a `Counts()` record or method that returns all four counters for `GET /mock/state`. Do not expose the dictionary itself.

2. **The delivery rule, exactly as the lecture states it.**

   ```csharp
   public enum WmsDeliveryResult { Delivered, Retry, Permanent }

   public static class WmsDeliveryOutcome
   {
       public static WmsDeliveryResult FromStatus(int? statusCode)
       {
           if (statusCode is null) return WmsDeliveryResult.Retry;
           var code = statusCode.Value;
           if (code is >= 200 and < 300) return WmsDeliveryResult.Delivered;
           if (code is 408 or 429) return WmsDeliveryResult.Retry;
           if (code >= 500) return WmsDeliveryResult.Retry;
           if (code >= 400) return WmsDeliveryResult.Permanent;
           return WmsDeliveryResult.Retry;
       }
   }
   ```

3. **The backoff rule.**

   ```csharp
   public sealed class WmsRetryPolicy
   {
       private readonly TimeSpan _baseDelay;
       private readonly TimeSpan _maxDelay;

       public WmsRetryPolicy(TimeSpan baseDelay, TimeSpan maxDelay)
       {
           if (baseDelay <= TimeSpan.Zero)
               throw new ArgumentOutOfRangeException(nameof(baseDelay));
           if (maxDelay < baseDelay)
               throw new ArgumentOutOfRangeException(nameof(maxDelay));
           _baseDelay = baseDelay;
           _maxDelay = maxDelay;
       }

       public TimeSpan DelayFor(int failures)
       {
           if (failures < 1)
               throw new ArgumentOutOfRangeException(nameof(failures));

           var ticks = _baseDelay.Ticks;
           for (var i = 1; i < failures && ticks < _maxDelay.Ticks; i++)
               ticks = Math.Min(ticks * 2, _maxDelay.Ticks);

           return TimeSpan.FromTicks(ticks);
       }
   }
   ```

4. **The pusher's send branch.**

   ```csharp
   var now = _now();
   var ids = _outbox.Claim();
   foreach (var orderId in ids)
   {
       if (!_board.TryGet(orderId, out var order) || order is null)
       {
           Console.WriteLine($"no order for {orderId}");
           continue;
       }

       if (_retry.TryGetValue(orderId, out var state) && now < state.NextAttemptAt)
       {
           _outbox.MarkPending(orderId);
           continue;
       }

       try
       {
           var body = JsonSerializer.Serialize(order, Json);
           using var content = new StringContent(body, Encoding.UTF8, "application/json");
           using var response = await _client.PostAsync(WmsRoutes.OrderStatus, content, cancellationToken);
           var outcome = WmsDeliveryOutcome.FromStatus((int)response.StatusCode);

           if (outcome == WmsDeliveryResult.Delivered)
           {
               _retry.Remove(orderId);
               _failures.Clear(orderId);
               Console.WriteLine($"pushed {orderId} {order.State}");
           }
           else if (outcome == WmsDeliveryResult.Permanent)
           {
               _retry.Remove(orderId);
               _failures.Record(orderId, $"HTTP {(int)response.StatusCode}");
               Console.WriteLine($"rejected permanently {orderId} {order.State}");
           }
           else
           {
               ScheduleRetry(orderId, now);
           }
       }
       catch (HttpRequestException)
       {
           ScheduleRetry(orderId, now);
       }
       catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
       {
           ScheduleRetry(orderId, now);
       }
       catch (Exception ex) when (ex is not OperationCanceledException)
       {
           ScheduleRetry(orderId, now);
       }
   }

   void ScheduleRetry(string orderId, DateTime now)
   {
       var failures = _retry.TryGetValue(orderId, out var state) ? state.Failures + 1 : 1;
       var delay = _retryPolicy.DelayFor(failures);
       _retry[orderId] = new RetryState(failures, now + delay);
       _outbox.MarkPending(orderId);
       Console.WriteLine($"retry {orderId} in {delay.TotalMilliseconds:0} ms");
   }
   ```

   A local function can capture the fields. If a local function makes the class hard to read, make `ScheduleRetry` a private method and pass `now`.

5. **The mode route.**

   ```csharp
   app.MapPost("/mock/mode", (ModeRequest request) =>
   {
       if (!MockWmsControl.IsMode(request.Mode))
           return Results.Problem(statusCode: 400, title: "Unknown mode");

       if (request.SlowDelayMs is <= 0)
           return Results.Problem(statusCode: 400, title: "slowDelayMs must be positive");

       control.Set(request.Mode,
           request.SlowDelayMs is int ms ? TimeSpan.FromMilliseconds(ms) : null);
       return Results.Ok(control.Snapshot());
   });
   ```

   `SlowDelayMs` is nullable. When it is missing, `Set` keeps the current delay. The lesson's smoke test sends only `{"mode":"error"}`, so a missing delay must not be an error. A zero delay would make `slow` behave like `ok`.

6. **Three pusher tests, with a fake clock.**

   ```csharp
   var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
   var policy = new WmsRetryPolicy(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(30));
   var failures = new WmsDeliveryFailures();
   var pusher = new WmsStatusPusher(board, outbox, client, TimeSpan.FromSeconds(1), policy, failures, () => clock);

   await pusher.SendPendingAsync(CancellationToken.None);   // 500 → wait 1 s
   await pusher.SendPendingAsync(CancellationToken.None);   // not due → nothing sent
   clock = clock.AddSeconds(1);
   await pusher.SendPendingAsync(CancellationToken.None);   // sent again
   ```

   The stub handler counts calls. That count is the fact the test asserts, not a log line.

7. **Acceptance order.** Build first, then run the pure tests, then start the mock by hand and do the two-curl smoke test from the lesson. Do not start the mock inside the test run if another test already uses port 5090. If you add an integration test, give it its own port through config.

If you see the shape and can type it in, you are ready to build and run the acceptance criteria.
