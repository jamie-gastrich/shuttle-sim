# Lesson 20 hints — The WMS front door

Open a tier only when you are stuck. Tier 1 is a nudge. Tier 2 is the direction. Tier 3 is nearly the code. Each tier is written to be usable on its own, so you can jump straight to Tier 2 or 3 if you want.

The assignment is in [20-wms-rest-front-door.md](20-wms-rest-front-door.md). The shapes and reasons are in [record 28](../decisions/28-wms-rest-front-door.md). Do not invent fields or status codes; they are already decided.

---

## Tier 1 — Nudges

**The gate (red test at HEAD).** Read `ShuttleEndpoint.HandleRecoveryCommandAsync` and ask: what does the method do when `State` is **not** `Fault`? Nothing leaves it. The test expects exactly one ack. Add the missing branch for the healthy case. Keep the same publish call the fault branch already uses; just change the payload values. Run `dotnet test ShuttleSim.Tests` until it is green.

**The Web SDK move.** One line in `FleetManager.csproj`. Then build. It will tell you (NU1510) which package refs are now redundant — those are the ones to delete. Rebuild: zero warnings.

**The hosting.** You are adding a **second task** next to `loopTask`, in the same method, using the same `stopping.Token`. Look at how `loopTask` is created and awaited; the web task follows the same shape. Set `ContentRootPath` from `AppContext.BaseDirectory` the same way the existing config reader sets its base path.

**The endpoint.** In the handler, in order: validate; if there are errors, return the validation result; otherwise call the board and `switch` on `SubmitOutcome`. Three outcomes, three results. No `if` chains about locking or existence — the board already decided.

**The registry lock.** The dictionary is touched from more than one HTTP request thread. One `lock` around the dictionary access in `Submit`, and the same lock (or a read that is safe) around `TryGet`. Do not lock in the endpoint.

**`now`.** `WmsOrderBoard` must not call `DateTime.UtcNow`. The endpoint passes it in. In tests, pass a fixed `DateTime` and assert `ReceivedAt` on the retry is unchanged.

**Pinned literals.** A test that says `Assert.Equal(WmsOrderKinds.Store, WmsOrderKinds.Store)` proves nothing. Compare against the string `"store"`.

---

## Tier 2 — Direction

**Gate.** The method is method-scoped: `if (fault) { publish ack; return; }` and nothing else. Make it two paths that both publish exactly one ack:

- faulted: `Accepted = false`, a reason, and change state.
- healthy: `Accepted = true`, `Reason = null`, and change nothing.

If you want symmetry, treat `Accepted` and `Reason` as the two values that differ, and keep the rest of the publish call identical.

**Project file.** `Microsoft.NET.Sdk` → `Microsoft.NET.Sdk.Web`. Remove:

```xml
<PackageReference Include="Microsoft.Extensions.Configuration.Binder" Version="..." />
<PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="..." />
<PackageReference Include="Microsoft.Extensions.Configuration.Json" Version="..." />
```

The Web SDK brings these assemblies. Keep `OutputType Exe` and `TargetFramework`.

**Config check.** Find where `serve` already reads config keys and throws on a missing one. Add the Kestrel URL the same way:

```csharp
var webUrl = configuration["Kestrel:Endpoints:Http:Url"]
    ?? throw new InvalidOperationException("Kestrel:Endpoints:Http:Url is not configured.");
```

Read it even though Kestrel reads the same section itself. You are failing fast and loudly instead of silently taking a default port. If the value is present but blank, throw too.

**Hosting skeleton.**

```csharp
var webBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});
var webApp = webBuilder.Build();

webApp.MapPost(WmsRoutes.Orders, (SubmitOrderRequest request, WmsOrderBoard board) => { /* ... */ });
webApp.MapGet(WmsRoutes.Order("{orderId}"), (string orderId, WmsOrderBoard board) => { /* ... */ });

var webTask = webApp.RunAsync(stopping.Token);
```

The handler parameters are injected. `SubmitOrderRequest` is bound from the JSON body. `WmsOrderBoard` must be registered, or pass it by closure (capture the board instance you create in the method) — both work; pick one and be able to defend it.

Then, in the same `finally` that stops and awaits the loop, also `await webTask`. One Ctrl+C, both awaited. Add a `try`/`catch` around the awaits so an exception in either one does not skip the other.

**Endpoint handlers, in words only** (write the code yourself):

- POST: `var errors = WmsOrderValidator.Validate(request); if (errors.Count > 0) return Results.ValidationProblem(errors);` then `var result = board.Submit(request, DateTime.UtcNow);` then a `switch` on `result.Outcome`.
- `Created` → `Results.Created(WmsRoutes.Order(result.Order.OrderId), result.Order)`.
- `AlreadyKnown` → `Results.Ok(result.Order)`.
- `Conflict` → `Results.Problem(statusCode: 409, title: "Order id already used")`.
- GET: `board.TryGet(orderId, out var order)` → `Results.Ok(order)` else `Results.Problem(statusCode: 404, title: "Unknown order")`.

**Board.** Two fields: a `Dictionary<string, WmsOrder>` (compare by `Ordinal`) and one private `object _gate`. In `Submit`:

1. Take the lock.
2. If the id exists: compare **all four** field values. If all four match, return `AlreadyKnown` with the stored order (so `ReceivedAt` is the original). Otherwise return `Conflict` with the stored order.
3. Otherwise build the order with `State = WmsOrderStates.Received` and `ReceivedAt = now`, store it, return `Created`.

**Equality for the retry.** Do not compare `DateTime`s or `State`; compare only the four input fields. A helper `static bool SameFields(WmsOrder existing, SubmitOrderRequest request)` keeps that rule in one place.

---

## Tier 3 — Near solution

Sketches only. They are shaped to fit the repo, but you still fill in the details, wire the hosting, and write the tests. Do not paste wholesale without reading each line — you must be able to defend it.

**`src/FleetCore/WmsOrderKinds.cs`**

```csharp
public static class WmsOrderKinds
{
    public const string Store = "store";
    public const string Retrieve = "retrieve";

    public static bool IsKnown(string? kind)
        => kind == Store || kind == Retrieve;
}
```

**`src/FleetCore/WmsOrderStates.cs`**

```csharp
public static class WmsOrderStates
{
    public const string Received = "received";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
```

**`src/FleetCore/WmsRoutes.cs`**

```csharp
public static class WmsRoutes
{
    public const string Orders = "/api/orders";

    // The route template uses "{orderId}"; the built path uses the value.
    public static string Order(string orderId) => $"{Orders}/{orderId}";
}
```

Careful: `Order("{orderId}")` is the **template** for `MapGet`, and `Order(orderId)` is the **built path** for `Location`. Same method, two kinds of argument. Say that out loud so you own it.

**`src/FleetCore/WmsOrder.cs`**

```csharp
public sealed record SubmitOrderRequest(string? OrderId, string? Kind, string? PalletId, string? LaneId);

public sealed record WmsOrder(
    string OrderId,
    string Kind,
    string PalletId,
    string LaneId,
    string State,
    DateTime ReceivedAt);

public enum SubmitOutcome { Created, AlreadyKnown, Conflict }

public sealed record SubmitResult(SubmitOutcome Outcome, WmsOrder Order);
```

**`src/FleetCore/WmsOrderValidator.cs`**

```csharp
public static class WmsOrderValidator
{
    public static Dictionary<string, string[]> Validate(SubmitOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(request.OrderId))
            errors["orderId"] = ["orderId is required."];
        if (!WmsOrderKinds.IsKnown(request.Kind))
            errors["kind"] = ["kind must be 'store' or 'retrieve'."];
        if (string.IsNullOrWhiteSpace(request.PalletId))
            errors["palletId"] = ["palletId is required."];
        if (string.IsNullOrWhiteSpace(request.LaneId))
            errors["laneId"] = ["laneId is required."];

        return errors;
    }
}
```

Every `if` is separate on purpose. Do not `return` early; the WMS wants all the problems at once.

**`src/FleetCore/WmsOrderBoard.cs`**

```csharp
public sealed class WmsOrderBoard
{
    private readonly Dictionary<string, WmsOrder> _orders = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public SubmitResult Submit(SubmitOrderRequest request, DateTime now)
    {
        lock (_gate)
        {
            if (_orders.TryGetValue(request.OrderId!, out var existing))
            {
                return SameFields(existing, request)
                    ? new SubmitResult(SubmitOutcome.AlreadyKnown, existing)
                    : new SubmitResult(SubmitOutcome.Conflict, existing);
            }

            var order = new WmsOrder(
                request.OrderId!, request.Kind!, request.PalletId!, request.LaneId!,
                WmsOrderStates.Received, now);

            _orders.Add(order.OrderId, order);
            return new SubmitResult(SubmitOutcome.Created, order);
        }
    }

    public bool TryGet(string orderId, out WmsOrder? order)
    {
        lock (_gate)
            return _orders.TryGetValue(orderId, out order);
    }

    private static bool SameFields(WmsOrder existing, SubmitOrderRequest request)
        => existing.Kind == request.Kind
        && existing.PalletId == request.PalletId
        && existing.LaneId == request.LaneId;
}
```

The `!` on `request.OrderId!` is safe **only because validation ran first**. Note that dependency in a comment; it is exactly the kind of thing an interviewer will ask about ("what if someone calls `Submit` without validating?"). The honest answer: the board is not a public API, the endpoint is the only caller, and the guard is the doc comment plus a test that a validated-but-blank id cannot reach here.

**Endpoint (inside `Program.cs`, sketch):**

```csharp
webApp.MapPost(WmsRoutes.Orders, (SubmitOrderRequest request) =>
{
    var errors = WmsOrderValidator.Validate(request);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    var result = board.Submit(request, DateTime.UtcNow);
    return result.Outcome switch
    {
        SubmitOutcome.Created => Results.Created(WmsRoutes.Order(result.Order.OrderId), result.Order),
        SubmitOutcome.AlreadyKnown => Results.Ok(result.Order),
        _ => Results.Problem(statusCode: 409, title: "Order id already used"),
    };
});

webApp.MapGet(WmsRoutes.Order("{orderId}"), (string orderId) =>
    board.TryGet(orderId, out var order)
        ? Results.Ok(order)
        : Results.Problem(statusCode: 404, title: "Unknown order"));
```

`board` is the instance you created in `RunServeAsync`. If you register it in DI instead, do that consistently and be ready to explain why.

---

## Tier 3b — The gate fix (near solution)

`src/ShuttleSim/ShuttleEndpoint.cs`, inside `HandleRecoveryCommandAsync`. The fault path already publishes an ack with the existing `RecoveryAcknowledgeAsync` helper and then returns. What is missing is everything the shuttle should say when it is **not** faulted.

The test is the spec: exactly one published entry, family `MessageFamily.Ack`, `Accepted == true`, `Reason == null`, `CorrelationId` unchanged (`"recovery-healthy"`), shuttle still `Idle` at `A01-L01`, and **no** `MessageFamily.Status` publish. So the healthy path is a single call to the same helper, and nothing else:

```csharp
// after the existing fault branch (which already returns):
await RecoveryAcknowledgeAsync(command, accepted: true, reason: null, now, cancellationToken);
```

Do **not** call `PublishStatusAsync` here — the test asserts there is no Status publish, because nothing changed. Do not invent a second ack helper.

Then run `dotnet test ShuttleSim.Tests` and confirm the healthy-shuttle test is green.

---

## Tier 3c — If a test will not go green

- **Problem+json body is empty in tests.** The Development/Production difference is real. The framework hides the malformed-JSON detail in Production. Do not assert on a stack trace.
- **`Location` header missing.** `Results.Created` needs the full path. Check you passed `WmsRoutes.Order(result.Order.OrderId)` and not just the id.
- **Retry returns `409` instead of `200`.** Your `SameFields` is comparing something that changes — usually `State` or `ReceivedAt`. Compare the four inputs only.
- **`ReceivedAt` changes on retry.** You rebuilt the order instead of returning the stored one. Return `existing`.
- **Two threads both create the same order under load.** The lock is missing or outside the dictionary access. The `TryGetValue` and the `Add` must be in the same lock scope.
