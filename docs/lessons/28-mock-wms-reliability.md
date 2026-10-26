# Lesson 28: The mock WMS and delivery reliability

**Milestone:** M6 (WMS integration) · **Language:** C#/.NET 10 · **Prerequisite:** [lesson 27](27-order-cancel-slice.md)
**Concepts this assignment requires:**

- [ ] **Delivery outcomes** — every status push ends as delivered, retry, or permanent → [§1](#1-delivery-outcomes)
- [ ] **The idempotent receiver** — the mock WMS applies each word once; a repeated push is safe → [§2](#2-the-idempotent-receiver)
- [ ] **Backoff with a ceiling** — retry pacing per order, reset on success → [§3](#3-backoff-with-a-ceiling)

Three new concepts. That is the cap.

**Not in this lesson:** order submission from the mock WMS (stretch goal), the cancel client, traffic control, the dashboard, any change to `src/Contracts/`, any change to `docs/contract/`, and any change to the task state machine or the order ladder. The MQTT contract stays `4`. The WMS REST contract stays `2`.

---

## Where this fits

The spec's M6 says:

> *"A mock WMS submits store and retrieve orders over REST, receives status updates, and can cancel orders. It also sends bad input and replies slowly or not at all."*

Lesson 20 built the front door. Lesson 21 turned an order into a task. Lesson 23 built the status sender. Lesson 24 wired the sender into `serve`. Lesson 25 saved the pair. Lesson 27 adds cancel.

But the sender has nobody to talk to. The fleet manager points at `http://127.0.0.1:5090` (measured in `appsettings.json`), and nothing listens there. The sender also treats every failure the same: a `500`, a `400`, a timeout — all go back on the pile and try again on the next tick. That is safe, but it is not reliable. A `404` will never heal. A dead WMS gets a request every second for every owed order.

This slice builds the missing half and makes the sending side act on what the WMS says:

1. **A small `MockWms` app** that receives the documented `POST /api/order-status`, keeps a ledger, and can be switched into slow, error, or hang mode.
2. **A delivery rule** that reads the reply and chooses one of three actions: done, retry, or give up and record.
3. **A backoff rule** that waits longer after each failure, up to a ceiling, and resets after success.

**Forward references.** The M7 dashboard can read `GET /wms/delivery-failures` to show words the WMS refused. The M8 load test can flip the mock into `slow` or `error` mode and measure the retry rate. The M6 done-when proof gets a real receiver that can be slow, dead, or angry.

---

## Verification table

Everything below was checked on **2026-10-26**, in this repo or in scratch copies under `/tmp/opencode`. Scratch copies are deleted afterwards. Nothing was left running.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | SDK present | `dotnet --version`; `cat global.json` | `10.0.113`; `global.json` asks for `10.0.112` with `latestFeature` |
| 2 | Baseline builds clean | copy of `src` in `/tmp/opencode/l28base`; `dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |
| 3 | Baseline pure suite is green | same copy; `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **Failed: 0, Passed: 190, Total: 190** (FleetCore 52, FleetManager 70, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10) |
| 4 | No mock WMS exists | `grep -n "MockWms" src/ShuttleFleet.slnx`; `ls src` | no matches; no `MockWms` folder |
| 5 | Nothing listens on 5090 | `ss -ltn '( sport = :5090 or sport = :5080 )'` | header only; no listener |
| 6 | 5090 is only a config value | `grep -rn "5090" src --include=*.json --include=*.cs` | `src/FleetManager/appsettings.json:32`, plus a URL parse test. No server code. |
| 7 | The pusher has no retry pacing | read `src/FleetManager/WmsStatusPusher.cs:22-64` | every failure calls `MarkPending`; no delay, no attempt count, no next-attempt time |
| 8 | The pusher treats all failures the same | read `src/FleetManager/WmsStatusPusher.cs:38-45` | `IsSuccessStatusCode` decides; all other statuses do the same `MarkPending` |
| 9 | A `500` does not throw | scratch `/tmp/opencode/l28probe` against a real `HttpListener` | `/500: no throw, status=500, 5 ms` |
| 10 | A `400`, `404`, `429` does not throw | same run | `/400: no throw, status=400, 5 ms`; `/404: no throw, status=404, 1 ms`; `/429: no throw, status=429, 0 ms` |
| 11 | A slow WMS trips the client timeout | same run, client timeout 300 ms, server sleeps 2 s | `/slow: TaskCanceledException after 308 ms; inner=TimeoutException` |
| 12 | A refused connection is a fast, typed failure | same run, nothing on port 5182 | `refused: HttpRequestException after 7 ms; inner=SocketException` |
| 13 | The timeout type is catchable as `OperationCanceledException` | same run | `TaskCanceledException base = OperationCanceledException` |
| 14 | A slow WMS that applies before it answers causes a duplicate on retry | scratch `/tmp/opencode/l28slow`; server applies, sleeps 1 s, client timeout 300 ms, then retry | `first: TaskCanceledException after the 300 ms client timeout`; `retry: status=200`; `ledger entries=1 applied=1 duplicate=1` |
| 15 | `dotnet new web` and `dotnet sln add` work on this tree | in `/tmp/opencode/l28base`: `dotnet new web -o MockWms -f net10.0`; `dotnet sln ShuttleFleet.slnx add MockWms/MockWms.csproj` | `Project 'MockWms/MockWms.csproj' added to the solution.` |
| 16 | A test project can reference the web app | same copy: `dotnet new xunit -o MockWms.Tests -f net10.0`; `dotnet add ... reference ...`; `dotnet test` | `Passed! - Failed: 0, Passed: 1, Total: 1` |
| 17 | The toy runs as printed | scratch `/tmp/opencode/l28toy`; `dotnet run` | see the worked example output below |
| 18 | `cancelled` exists as a word | read `src/FleetCore/WmsOrderStates.cs:9` | `public const string Cancelled = "cancelled";` |
| 19 | Lesson 27 is not in the tree yet | `grep -n "Cancelled" src/FleetCore/TaskState.cs` | no matches |
| 20 | The fleet-side ladder has no `cancelled` rung yet | read `src/FleetCore/WmsOrderLadder.cs:15-22` | `received`, `in_progress`, `done`, `failed`; no `cancelled` |
| 21 | The contracts did not move | `grep -n "Current = " src/Contracts/MessageTypes.cs`; `grep -n "^Version:" docs/contract/wms-rest.md docs/contract/v4.md` | MQTT `4`; REST `2`; MQTT page `4` |
| 22 | No containers or app processes are running | `docker compose -f docker/compose.yml ps`; `pgrep -af "dotnet run\|FleetManager.dll\|MockWms.dll\|mosquitto"` | empty table; no real process |
| 23 | Working tree before writing | `git status --short`; `git log --oneline -3` | two pre-existing `.opencode/agents/*.md` edits; HEAD `e2923c6` |

**Not yet verified:** the `MockWms` app, the backoff map, the failure board, and the tests this lesson asks you to build. That is the assignment. Lesson 27's cancel code is also not in the tree yet; this slice does not depend on it, but the mock accepts the word `cancelled` so it is ready when 27 lands.

---

## Options survey

### Survey 1: Where does the mock WMS live?

| Option | What it means | Complexity | How it fails | How real systems do it |
|---|---|---|---|---|
| **A. A new `MockWms` app** (chosen) | A separate ASP.NET Core process on port 5090. It has its own JSON types and its own ledger. | Medium. One new app and one test project. | A typo in the JSON field names breaks the push; the contract test catches it. | Test doubles for external systems are separate processes or containers, so the network path is real. |
| **B. A fake `HttpMessageHandler` inside the tests** | The existing `StubHandler` pretends to be the WMS. | Low. No new project. | No real HTTP, no real timeout, no way to stop the WMS. It cannot be demoed by the owner. | Unit tests use fakes; end-to-end tests use a real stub service. |
| **C. Add WMS routes to `FleetManager`** | The fleet manager talks to itself on another route. | Low, but wrong. | There is no network failure to observe. The "external system" boundary disappears. | Never. The WMS is outside the fleet. |

**Decision A.** The mock is the receiver the fleet manager has been missing since lesson 23. It must be a separate process so the owner can stop it, slow it, and make it angry.

### Survey 2: How does the mock fail?

| Option | What it means | Complexity | How it fails | How real systems do it |
|---|---|---|---|---|
| **A. Runtime modes** (chosen) | `POST /mock/mode` sets `ok`, `slow`, `error`, or `hang`. The same process keeps its ledger. | Low. One small control class and one route. | A mode change during a request is read once at the start of that request. | Test harnesses expose fault switches so a scenario can change mid-run. |
| **B. Config plus restart** | `MockWms:Mode` in `appsettings.json`; change it and restart. | Low. | A restart loses the in-memory ledger, so the duplicate story is harder to show. | Some test rigs do this, but they persist the ledger. |
| **C. Separate binaries or flags** | `MockWms --fail` and `MockWms --slow`. | Medium. More launch commands. | The owner has to stop and start the right binary. | Rare outside small tools. |

**Decision A.** The ledger has to survive the mode change. That is the whole point of the duplicate lesson: the WMS applied the word, then it was slow, then the fleet retried.

### Survey 3: What does a `4xx` reply mean?

| Option | What it means | Complexity | How it fails | How real systems do it |
|---|---|---|---|---|
| **A. Permanent, except `408` and `429`** (chosen) | `400`, `404`, `409` will not get better by retrying. Record it. | Low. One pure function. | A WMS that uses `400` for a temporary problem would be misread. That is a contract mistake. | HTTP clients usually treat most `4xx` as permanent and `5xx`/timeouts as transient. |
| **B. Retry every failure forever** | The current behaviour. | Low. | A dead order is retried at the cap forever. The log fills with the same failure. | Some systems do this for truth, but they add an alarm. |
| **C. Drop every failure silently** | No retry, no record. | Low. | The WMS never hears the truth and the operator never knows. | Never. |

**Decision A.** A `4xx` that is not `408` or `429` is a "this request will not work" answer. The fleet stops, records it, and moves on.

### Survey 4: How are retries paced?

| Option | What it means | Complexity | How it fails | How real systems do it |
|---|---|---|---|---|
| **A. Per-order exponential backoff with a cap** (chosen) | Each order has a failure count and a next-attempt time. The wait doubles: base, 2×, 4×, … max. | Medium. One pure policy and one dictionary in the pusher. | The first pass still sends one request per owed order. | Common: exponential backoff, often with jitter. |
| **B. Fixed interval** | The current tick is the retry spacing. | Low. | A dead WMS is hit every second for every order. | Simple systems do this for a small fleet. |
| **C. A separate timer per order** | Every owed order gets its own scheduled task. | High. Many timers, many lifetimes. | Easy to leak tasks and lose errors. | Rare for a small simulator. |

**Decision A.** The loop still ticks every `Wms:StatusIntervalMs`. The per-order next-attempt time decides whether a word goes out on that tick.

**Confirming the tools exist.** I ran `dotnet new web -f net10.0`, `dotnet new xunit -f net10.0`, `dotnet add reference`, `dotnet sln add`, and `dotnet test` in `/tmp/opencode/l28base`. The installed SDK is `10.0.113`. No new NuGet package is needed for the mock.

---

## Decision

[Record 37](../decisions/37-mock-wms-reliability.md) settles all of it. Short version:

- **A.** A new `MockWms` app, separate process, own JSON types, listens on 5090 from config. No shared `FleetCore` code.
- **B.** Runtime modes `ok`, `slow`, `error`, `hang`. `slow` applies the word, then waits. `error` returns `500` before applying. `hang` never applies.
- **C.** A pure `WmsDeliveryOutcome.FromStatus` maps a reply to `Delivered`, `Retry`, or `Permanent`. `2xx` delivered; `408`, `429`, `5xx`, no reply retry; other `4xx` permanent.
- **D.** A permanent failure is recorded in `WmsDeliveryFailures` and is not put back in the outbox. A retryable failure is put back and given a next-attempt time.
- **E.** A pure `WmsRetryPolicy.DelayFor(failures)` doubles the wait from a base to a cap. A success resets the order's failure count and clears its failure record.
- **F.** New config keys: `Wms:RetryBaseDelayMs` (1000), `Wms:MaxRetryDelayMs` (30000), `MockWms:SlowDelayMs` (3000). All validated at startup.
- **G.** The mock's control and read routes are mock-only. They are not the WMS contract. `docs/contract/wms-rest.md` stays at version 2. The MQTT contract stays at version 4.

Read the record before you write code. Every requirement below is decided there.

---

## The lecture

Today we are going to learn about **delivery outcomes**, **the idempotent receiver**, and **backoff with a ceiling**.

### 1 Delivery outcomes

**What it is.** Every status push ends in one of three buckets. The bucket tells the fleet what to do next.

| Bucket | What the WMS said | What the fleet does |
|---|---|---|
| **Delivered** | `2xx` | The word is safe. Remove it from the outbox. Clear any old failure. Reset the backoff. |
| **Retry** | `408`, `429`, `5xx`, or no reply | The problem might heal. Put the word back. Wait longer before the next try. |
| **Permanent** | Any other `4xx` | The request will not work. Record it. Stop retrying this word. |

**Why we need it.** A `500` and a `404` look similar on the wire. They mean opposite things. A `500` says "I am broken, come back." A `404` says "there is no such order, and there never will be." If the fleet treats both as retry, a `404` becomes a noisy loop that never ends. If the fleet treats both as done, a `500` loses the word.

**A real-world example.** You call a warehouse office. A busy signal means call again later. A wrong number means stop calling that number. The phone gives you a signal; the signal decides your next move.

**The code.** The rule is a pure function. It takes a status code, or `null` when there was no reply.

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

Line by line:

- `int? statusCode` — the `?` means the value may be missing. A timeout or a refused connection has no HTTP status. The caller passes `null` for that case.
- `if (statusCode is null) return Retry` — no reply means "try again." The network might be back.
- `code is >= 200 and < 300` — the `2xx` family means success.
- `code is 408 or 429` — `408` is "request timeout"; `429` is "too many requests." Both are temporary. They are `4xx`, but they do not belong in the permanent bucket.
- `code >= 500` — the server failed. Retry.
- `code >= 400` — the remaining `4xx` codes mean the request is wrong or the resource is gone. Permanent.
- `return Retry` — anything else (`1xx`, `3xx`) is not a clear success or a clear bad request. The safe choice is retry. A misconfigured redirect should not silently drop the truth.

**Why "no reply" is a value and not an exception.** The pusher catches network and timeout exceptions. The pure rule should not know about `HttpClient`. Passing `null` keeps the rule easy to test and easy to read.

**What the pusher does with each bucket.** The reply is read after `PostAsync`. Remember from the measured table: `500`, `400`, `404`, and `429` do **not** throw. `HttpClient` returns a response. The pusher must read `response.StatusCode`.

```csharp
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
```

- `Delivered` — the word is safe. Remove the retry state. Clear any old failure.
- `Permanent` — remove the retry state, record why, and **do not** put the word back. The next pass will not see it.
- `Retry` — schedule the next attempt. The next concept explains the wait.

A network exception and a client timeout are also retry cases. The current code already catches them. Keep that shape. The only new part is the `null` status for those catches.

**Where the failure goes.** `WmsDeliveryFailures` is a small in-memory board. It stores one reason per order id. The fleet manager exposes it at `GET /wms/delivery-failures`. The M7 dashboard can show it later. A later success for the same order clears the record. The board is not persisted; a restart forgets it. That is a known limit, not a hidden one.

**Forward reference.** The cancel slice's `cancelled` word is just another state in the body. This rule does not care which word moved. If the WMS answers `409` because it does not know the order, the word is recorded and not retried.

### 2 The idempotent receiver

**What it is.** At-least-once delivery means the fleet may send the same body more than once. The receiver must apply it once. That property is called **idempotent**: doing the same thing twice has the same effect as doing it once.

**Why we need it.** The measured slow case is the reason. The WMS applied the word, then took too long to answer. The fleet's client timed out. The fleet retried. If the WMS simply inserted a row each time, the order would appear twice. If it simply overwrote, a late old word could move the order backward.

The fleet cannot know whether the WMS applied the word before the timeout. The safe rule is: **the receiver must handle a repeat.**

**A real-world example.** A clerk hands you a receipt. If you hand the same receipt back, the clerk does not charge you twice. The order number is the key. The paper can arrive twice; the charge happens once.

**The mock's ledger.** The mock keeps one entry per `orderId`. It does not use the HTTP request id, because the status body has no request id. The order id is the identity that both systems already share.

```csharp
public ApplyResult Apply(MockOrder order)
{
    lock (_gate)
    {
        if (_orders.TryGetValue(order.OrderId, out var old))
        {
            if (old.Kind != order.Kind || old.PalletId != order.PalletId || old.LaneId != order.LaneId)
                return ApplyResult.Conflict;
            if (old.State == order.State)
                return ApplyResult.Duplicate;
            if (Rank(order.State) <= Rank(old.State))
                return ApplyResult.Stale;
        }

        _orders[order.OrderId] = order;
        return ApplyResult.Applied;
    }
}
```

Line by line:

- `lock (_gate)` — the ledger is shared. One HTTP request per call, but several can run at once. The lock makes the check-and-write one step.
- `_orders.TryGetValue(order.OrderId, out var old)` — look up the existing entry by the order id. This is the key.
- The identity check — the same order id must mean the same work. A different `kind`, `palletId`, or `laneId` is a conflict, not an update. This mirrors the front door's rule that an order id is never reused for different work.
- `old.State == order.State` — the exact same word. This is a duplicate. The ledger does not change. The route answers `200`. The fleet can stop retrying.
- `Rank(order.State) <= Rank(old.State)` — the new word is not higher. It is stale. The ledger does not change. The route answers `200`, and the mock counts a stale push. The fleet should not send an older word; the count is there to catch a bug.
- `_orders[order.OrderId] = order` — a new order or a higher word. Replace the entry. One ledger row per order.
- `return Applied` — the word moved.

**The rank.** The mock has its own small ladder. It does not share `FleetCore`, because the mock is a separate system.

```csharp
private static int Rank(string state) => state switch
{
    "received" => 0,
    "in_progress" => 1,
    "done" => 2,
    "failed" => 2,
    "cancelled" => 3,
    _ => throw new ArgumentException($"unknown state '{state}'"),
};
```

- `received` is rank 0. It is the intake word. The fleet does not push it, but the mock accepts it.
- `in_progress` is rank 1.
- `done` and `failed` are rank 2. Both are terminal outcomes.
- `cancelled` is rank 3. It is above both terminal outcomes. This matches lesson 27's rule that a cancel is a terminal word and must not be replaced by a later `done` or `failed`.
- An unknown word throws. The mock should not invent a rank for a word the contract does not define.

The identity check runs before the rank. That is why a `done` → `failed` change is not "same rank, duplicate." It is a different word at the same rank, so it is stale. The ledger keeps the first terminal word.

**The modes.** The mock has four modes, set at runtime:

| Mode | What the route does | What the fleet sees |
|---|---|---|
| `ok` | Apply the word, answer `200`. | Delivered. |
| `slow` | Apply the word, wait `SlowDelayMs`, answer `200`. | A client timeout. Then a retry. The retry is a duplicate. |
| `error` | Do not apply, answer `500`. | Retry. |
| `hang` | Do not apply, wait forever. | A client timeout. Then a retry. |

`slow` is the important one. It models the real case where the WMS applied the word but the reply was lost or late. The measured probe shows the result: `ledger entries=1 applied=1 duplicate=1`. One order, one word, one duplicate. That is idempotency in action.

`POST /mock/mode` can change the mode alone. The slow delay is optional. If the delay is missing, the current delay stays. If it is present, it must be positive.

`hang` models a WMS that is up but not answering. The fleet's client timeout is the only way out. It must not hold the whole fleet; the pusher loop is separate from the task loop.

**The mock's routes.** The mock serves the documented push route and a few mock-only routes:

| Route | Why |
|---|---|
| `POST /api/order-status` | The documented fleet-manager → WMS push. The body is the `WmsOrder` shape from `wms-rest.md` v2. |
| `GET /api/orders/{orderId}` | Read one entry from the ledger. The WMS's own read side. |
| `GET /api/orders` | Read all entries. Useful for a demo. |
| `GET /mock/state` | Mode, delay, ledger, and counters. Mock-only. |
| `POST /mock/mode` | Set `ok`, `slow`, `error`, or `hang`. Mock-only. |
| `POST /mock/reset` | Clear the ledger. Mock-only. |

The `/mock/*` routes are **not** the WMS contract. They are the test harness's controls. The mock's README documents them. The contract page `docs/contract/wms-rest.md` stays at version 2.

**A gotcha: the launch profile.** `dotnet new web` writes `Properties/launchSettings.json` with a random port (the template used 5153). If you run `dotnet run`, that profile can override the `Kestrel` URL in `appsettings.json`. Delete the `Properties` folder, or set its URL to `http://127.0.0.1:5090`. The fleet manager's `Wms:BaseUrl` points at 5090.

**Forward reference.** The M7 dashboard can read the mock's ledger during a demo. The M8 load test can flip the mock to `error` and measure how the fleet's retry rate falls as the backoff grows.

### 3 Backoff with a ceiling

**What it is.** After a retryable failure, the fleet waits before trying that order again. The wait grows each time: base, 2×, 4×, and so on, up to a maximum. A success resets the count to zero.

**Why we need it.** A dead WMS should not get one request per owed order every second. That is a hammer, not a retry. The WMS may be restarting or overloaded. Backoff gives it room. It also keeps the logs and the CPU bounded. The ceiling stops the wait from growing forever. The reset means a recovered WMS gets the next word promptly.

**A real-world example.** You call a busy office. You try, wait a minute, try again, wait two minutes, then four. After a while you call every half hour, not every second. When someone answers, you go back to normal.

**The policy.** The wait is a pure function of the failure count. `1` means the first failure.

```csharp
public TimeSpan DelayFor(int failures)
{
    if (failures < 1)
        throw new ArgumentOutOfRangeException(nameof(failures));

    var ticks = BaseDelay.Ticks;
    for (var i = 1; i < failures && ticks < MaxDelay.Ticks; i++)
        ticks = Math.Min(ticks * 2, MaxDelay.Ticks);

    return TimeSpan.FromTicks(ticks);
}
```

Line by line:

- `failures < 1` — a zero or negative count has no meaning. Throw.
- `ticks = BaseDelay.Ticks` — the first wait is the base delay. One `TimeSpan` tick is 100 nanoseconds; using ticks keeps the doubling exact.
- The loop runs once per earlier failure. It stops early when the wait reaches the cap.
- `Math.Min(ticks * 2, MaxDelay.Ticks)` — double the wait, but never pass the cap.
- `TimeSpan.FromTicks(ticks)` — turn the number back into a `TimeSpan`.
- The constructor rejects a base delay of zero or less, and a cap smaller than the base. A cap smaller than the base would make the first wait jump down, which is confusing.

With a base of 1000 ms and a cap of 30000 ms, the toy prints:

```text
attempt 1: wait 1000 ms
attempt 2: wait 2000 ms
attempt 3: wait 4000 ms
attempt 4: wait 8000 ms
attempt 5: wait 16000 ms
attempt 6: wait 30000 ms
```

**The per-order state.** The policy is pure. The pusher owns the state. The state is one small record per order id:

```csharp
private sealed record RetryState(int Failures, DateTime NextAttemptAt);
private readonly Dictionary<string, RetryState> _retry = new(StringComparer.Ordinal);
```

- `Failures` is the number of failures in a row for this order.
- `NextAttemptAt` is the first time the order may be sent again.
- The dictionary is owned by the single pusher loop. The outbox has its own lock. This dictionary is not shared with the web routes, so it needs no lock.

**The pass.** The pusher still ticks every `Wms:StatusIntervalMs`. On each pass it claims the owed ids and checks each one:

```csharp
var now = _now();
foreach (var orderId in ids)
{
    if (_retry.TryGetValue(orderId, out var state) && now < state.NextAttemptAt)
    {
        _outbox.MarkPending(orderId);
        continue;
    }

    // send the current word, then classify the reply
}
```

- `var now = _now()` — read the clock once per pass. Every order in the pass compares to the same time.
- `_retry.TryGetValue` — is this order in a wait?
- `now < state.NextAttemptAt` — not due yet. Put the id back in the outbox and skip. The word is still owed; it is just not time.
- The send path runs only when the order is due, or when it has no retry state.

**The failure update.** A retryable failure grows the count and sets the next time.

```csharp
var failures = _retry.TryGetValue(orderId, out var state) ? state.Failures + 1 : 1;
var delay = _retryPolicy.DelayFor(failures);
_retry[orderId] = new RetryState(failures, now + delay);
_outbox.MarkPending(orderId);
Console.WriteLine($"retry {orderId} in {delay.TotalMilliseconds:0} ms");
```

- The first failure has `failures = 1` and waits the base delay.
- The next failure has `failures = 2` and waits twice the base.
- The wait is added to `now`, the time this pass started.
- The id goes back in the outbox. The next pass will see it, but the retry map says "not yet."

**The success update.** A delivered word resets everything.

```csharp
_retry.Remove(orderId);
_failures.Clear(orderId);
_outbox claims already removed the id, so no MarkPending here
```

- Remove the retry state. The next word for this order starts at the base delay if it ever fails.
- Clear the failure record. The order has recovered.

**The permanent update.** A permanent failure also removes the retry state, but records a reason and does **not** re-mark.

```csharp
_retry.Remove(orderId);
_failures.Record(orderId, $"HTTP {(int)response.StatusCode}");
// no _outbox.MarkPending: this word will not be retried
```

**Why the cap matters.** Without a cap, the wait would double past hours and days. The word would be effectively lost, and the operator would see nothing. The cap keeps trying at a steady, slow rate. The failure record is the clue that something is wrong.

**The tick and the wait.** The loop ticks every `StatusIntervalMs` (1000 by default). The retry base is also 1000 by default. A due time between two ticks is rounded up to the next tick. If the base is smaller than the tick, the real wait is the tick. The lesson's defaults line up. If you change one, check the other.

**Honest limits.**

- The retry state is in memory. A restart resets the backoff. The restored in-flight tasks are re-marked by `serve`, so the words are not lost, but the wait starts over.
- The failure board is in memory. A restart forgets permanent failures. The order board is persisted; the WMS can still read the order through the fleet manager's `GET /api/orders/{orderId}`. Persisting the failure board is a later diagnostics slice.
- There is no jitter. One fleet manager in the simulator means no herd. A real fleet with several managers would add a small random spread so they do not all retry on the same tick.
- The first pass after an outage still sends one request per owed order. The backoff reduces the rate over time, not the first burst. A real system might add a circuit breaker. That is a larger change, not this slice.
- The mock's ledger is in memory. A mock restart loses it. A real WMS has a database. The fleet's retry after a mock restart would look like a new order. The mock is a test tool, not a durable WMS.

---

## Recap in plain words

- **Delivery outcomes** — read the reply. `2xx` means done. `5xx`, `408`, `429`, or no reply means try again later. Other `4xx` means stop and record it.
- **The idempotent receiver** — the fleet may send the same body twice. The mock keys the ledger by `orderId`. The same word is a duplicate. An older word is stale. A different pallet for the same order is a conflict. Only a higher word replaces the entry.
- **Backoff with a ceiling** — after a failure, wait longer: base, 2×, 4×, up to a cap. A success resets the wait. The loop still ticks; the per-order next time decides when to send.

---

## Worked example (toy — NOT the assignment)

This toy shows the three ideas on one screen: what a reply means, how a ledger handles duplicates, and how the wait grows. **It is not the assignment.** It is a plain console app with no HTTP and no mock WMS.

Create it:

```bash
mkdir -p /tmp/opencode/l28toy
cd /tmp/opencode/l28toy
dotnet new console --force -o .
```

Put this in `Program.cs`. It is split into three parts so it is easy to read. Put the parts in order, in the same file.

**Part 1 — the story:**

```csharp
using System;

var ledger = new ToyLedger();

Console.WriteLine("-- duplicate-safe apply --");
Console.WriteLine($"put in_progress -> {ledger.Put("ORD-1", "in_progress")}");
Console.WriteLine($"put in_progress -> {ledger.Put("ORD-1", "in_progress")}");
Console.WriteLine($"put done        -> {ledger.Put("ORD-1", "done")}");
Console.WriteLine($"put in_progress -> {ledger.Put("ORD-1", "in_progress")}");
Console.WriteLine($"ledger: ORD-1={ledger.StateOf("ORD-1")} " +
                  $"applied={ledger.Count("applied")} " +
                  $"duplicate={ledger.Count("duplicate")} " +
                  $"stale={ledger.Count("stale")}");

Console.WriteLine();
Console.WriteLine("-- what a reply means --");
foreach (var code in new int?[] { 200, 400, 404, 429, 500, null })
    Console.WriteLine($"status {code?.ToString() ?? "no reply"} -> {Classify(code)}");

Console.WriteLine();
Console.WriteLine("-- retry spacing: base 1000 ms, cap 30000 ms --");
for (var attempt = 1; attempt <= 6; attempt++)
    Console.WriteLine($"attempt {attempt}: wait {DelayMs(attempt, 1000, 30000)} ms");
```

**Part 2 — the two pure rules:**

```csharp
static Delivery Classify(int? statusCode)
{
    if (statusCode is null) return Delivery.Retry;
    var code = statusCode.Value;
    if (code is >= 200 and < 300) return Delivery.Delivered;
    if (code is 408 or 429) return Delivery.Retry;
    if (code >= 500) return Delivery.Retry;
    if (code >= 400) return Delivery.Permanent;
    return Delivery.Retry;
}

static int DelayMs(int attempt, int baseMs, int maxMs)
{
    var delay = Math.Min(baseMs, maxMs);
    for (var i = 1; i < attempt; i++)
        delay = Math.Min(delay * 2, maxMs);
    return delay;
}

enum Delivery { Delivered, Retry, Permanent }
```

**Part 3 — the toy ledger:**

```csharp
enum ApplyResult { Applied, Duplicate, Stale }

sealed class ToyLedger
{
    private readonly Dictionary<string, (string State, int Rank)> _orders = new();
    private readonly Dictionary<string, int> _counts = new()
    {
        ["applied"] = 0,
        ["duplicate"] = 0,
        ["stale"] = 0,
    };

    public ApplyResult Put(string orderId, string state)
    {
        if (_orders.TryGetValue(orderId, out var old))
        {
            if (old.State == state)
            {
                _counts["duplicate"]++;
                return ApplyResult.Duplicate;
            }
            if (Rank(state) <= old.Rank)
            {
                _counts["stale"]++;
                return ApplyResult.Stale;
            }
        }

        _orders[orderId] = (state, Rank(state));
        _counts["applied"]++;
        return ApplyResult.Applied;
    }

    public string StateOf(string orderId) => _orders[orderId].State;
    public int Count(string name) => _counts[name];

    private static int Rank(string state) => state switch
    {
        "received" => 0,
        "in_progress" => 1,
        "done" => 2,
        "failed" => 2,
        "cancelled" => 3,
        _ => throw new ArgumentException($"unknown state '{state}'"),
    };
}
```

Run it:

```bash
cd /tmp/opencode/l28toy
dotnet run
```

I ran it this session. The output was:

```text
-- duplicate-safe apply --
put in_progress -> Applied
put in_progress -> Duplicate
put done        -> Applied
put in_progress -> Stale
ledger: ORD-1=done applied=2 duplicate=1 stale=1

-- what a reply means --
status 200 -> Delivered
status 400 -> Permanent
status 404 -> Permanent
status 429 -> Retry
status 500 -> Retry
status no reply -> Retry

-- retry spacing: base 1000 ms, cap 30000 ms --
attempt 1: wait 1000 ms
attempt 2: wait 2000 ms
attempt 3: wait 4000 ms
attempt 4: wait 8000 ms
attempt 5: wait 16000 ms
attempt 6: wait 30000 ms
```

Read it line by line:

- `put in_progress -> Applied` — the first word for ORD-1. The ledger has one entry.
- `put in_progress -> Duplicate` — the same word again. The ledger does not change. This is what the fleet's retry after a timeout looks like.
- `put done -> Applied` — a higher word. The entry changes to `done`.
- `put in_progress -> Stale` — an older word. The entry stays `done`. A late retry cannot move it backward.
- `applied=2 duplicate=1 stale=1` — two changes, one repeat, one late old word.
- `status 200 -> Delivered` — success.
- `status 400 -> Permanent`, `status 404 -> Permanent` — the request will not heal.
- `status 429 -> Retry`, `status 500 -> Retry` — temporary.
- `status no reply -> Retry` — the timeout or dead WMS case.
- The six waits show the doubling and the cap. Attempt 6 is `30000`, not `32000`.

The toy uses a tuple and a dictionary. Your assignment uses a lock, an ASP.NET Core app, and real HTTP. The rules are the same.

---

## Say it out loud

Answer these out loud, with no notes.

1. The WMS answers `500`. What does the fleet do? The WMS answers `409`. What is different, and why?
2. A slow WMS applies the word, then waits three seconds. The fleet's client times out at two seconds and retries. What does the mock's ledger show, and why is that safe?
3. Why does the wait double? What is the cap for? What would happen without a cap?
4. The retry map is keyed by `orderId`, not by the HTTP request. Why? What happens when the order's word moves from `in_progress` to `done` while the WMS is down?
5. A permanent `4xx` is recorded and not put back in the outbox. Where does the operator see it, and what is lost when the fleet manager restarts?

---

## The assignment

Take the worked example and make it run **for real**, as the `MockWms` app and the reliable status pusher in `serve`.

### The files you will change or add

| File | New? | What changes |
|---|---|---|
| `src/MockWms/MockOrder.cs` | new | The mock's own JSON body type and its `TryCreate` validation. No reference to `FleetCore`. |
| `src/MockWms/MockWmsLedger.cs` | new | The ledger: one entry per `orderId`, a lock, the four apply results, counters, and a snapshot. |
| `src/MockWms/MockWmsControl.cs` | new | The runtime mode (`ok`, `slow`, `error`, `hang`), the slow delay, and a lock. |
| `src/MockWms/Program.cs` | new | The minimal API routes from the lecture. |
| `src/MockWms/appsettings.json` | new | `Kestrel` on `http://127.0.0.1:5090`, `MockWms:SlowDelayMs` = 3000. |
| `src/MockWms/README.md` | new | The route table, the mode table, how to run it, and the "not the contract" note. |
| `src/MockWms.Tests/MockWmsLedgerTests.cs` | new | The ledger facts. |
| `src/MockWms.Tests/MockWmsControlTests.cs` | new | The mode parse and the slow-delay update. |
| `src/FleetCore/WmsDeliveryOutcome.cs` | new | The pure classification rule. |
| `src/FleetCore/WmsRetryPolicy.cs` | new | The pure backoff rule. |
| `src/FleetCore/WmsDeliveryFailures.cs` | new | The in-memory failure board. |
| `src/FleetCore.Tests/WmsDeliveryOutcomeTests.cs` | new | The classification facts. |
| `src/FleetCore.Tests/WmsRetryPolicyTests.cs` | new | The backoff facts. |
| `src/FleetCore/WmsRoutes.cs` | edit | Add `DeliveryFailures = "/wms/delivery-failures"`. |
| `src/FleetManager/WmsStatusPusher.cs` | edit | Add the classification, the per-order retry map, the failure board, and the two constructors. |
| `src/FleetManager/Program.cs` | edit | Read `Wms:RetryBaseDelayMs` and `Wms:MaxRetryDelayMs`, build the policy and the failure board, pass them to the pusher, and map the failures route. |
| `src/FleetManager/appsettings.json` | edit | Add the two retry keys. |
| `src/FleetManager.Tests/WmsStatusPusherTests.cs` | edit | Use a fixed clock for the retry tests; add the new facts. |
| `src/ShuttleFleet.slnx` | edit | Add `MockWms` and `MockWms.Tests`. |

Scaffold the projects with the CLI, not by hand:

```bash
cd src
dotnet new web -o MockWms -f net10.0
dotnet new xunit -o MockWms.Tests -f net10.0
dotnet add MockWms.Tests/MockWms.Tests.csproj reference MockWms/MockWms.csproj
dotnet sln ShuttleFleet.slnx add MockWms/MockWms.csproj
dotnet sln ShuttleFleet.slnx add MockWms.Tests/MockWms.Tests.csproj
```

Delete `src/MockWms/Properties/launchSettings.json` (or set its URL to `http://127.0.0.1:5090`). The template's random port will override the config otherwise.

### Requirements

**R1 — the mock listens where the fleet manager points.** `MockWms` reads `Kestrel:Endpoints:Http:Url` from `appsettings.json` and listens on `http://127.0.0.1:5090`. The URL is not in code. The mock has its own `MockOrder` type; it does not reference `FleetCore` or `Contracts`.

**R2 — the push route and the four modes.** `POST /api/order-status` accepts the documented `WmsOrder` body (camelCase). In `ok` mode it applies the word and answers `200`. In `slow` mode it applies the word, waits `MockWms:SlowDelayMs`, then answers `200`. In `error` mode it answers `500` **without applying**. In `hang` mode it waits forever **without applying**. A missing or invalid field answers `400`.

**R3 — the ledger is idempotent.** A new order or a strictly higher word is `Applied`. The exact same word is `Duplicate`. An older word, or a different terminal word at the same rank, is `Stale`. A different `kind`, `palletId`, or `laneId` for the same `orderId` is `Conflict` and answers `409`. The rank is `received=0`, `in_progress=1`, `done=2`, `failed=2`, `cancelled=3`. The ledger has one lock and one entry per order id. It keeps counters for applied, duplicate, stale, and conflict.

**R4 — the mock's read and control routes.** `GET /api/orders/{orderId}` returns the entry or `404`. `GET /api/orders` returns all entries in id order. `GET /mock/state` returns the mode, the slow delay, the entries, and the counters. `POST /mock/mode` accepts `{ "mode": "ok|slow|error|hang", "slowDelayMs": 3000 }` and returns the new state. `slowDelayMs` is optional: when it is missing, the current delay stays. When it is present it must be positive. `POST /mock/reset` clears the ledger and counters, keeps the mode. The `/mock/*` routes are not the contract.

**R5 — the pure delivery rule.** `FleetCore.WmsDeliveryOutcome.FromStatus(int?)` returns `Delivered`, `Retry`, or `Permanent` exactly as §1 says. `null` is `Retry`. `408` and `429` are `Retry`. Other `4xx` are `Permanent`. `5xx` are `Retry`.

**R6 — the pure backoff rule.** `FleetCore.WmsRetryPolicy` is built from a base delay and a max delay. `DelayFor(failures)` returns the base for `1`, doubles for each earlier failure, and clamps at the max. The constructor rejects a base of zero or less, and a max smaller than the base. `DelayFor(0)` throws.

**R7 — the pusher uses the rule.** `WmsStatusPusher` reads the reply. `Delivered` removes the retry state and clears the failure. `Permanent` removes the retry state, records the failure, and does not re-mark. `Retry` (including a network error, a client timeout, and an unexpected exception) re-marks and schedules the next attempt. The existing `OperationCanceledException when (!cancellationToken.IsCancellationRequested)` filter stays: our shutdown must propagate; the client's timeout must not.

**R8 — per-order pacing.** The pusher keeps one retry state per order id. On each pass, an order that is not due is put back in the outbox and skipped. A due order is sent. A success resets the order. A permanent failure removes the state. The dictionary is owned by the single pusher loop; the outbox keeps its own lock.

**R9 — the failure board.** `FleetCore.WmsDeliveryFailures` stores one reason per order id under a lock. It has `Record`, `Clear`, and `Snapshot` (ordered by order id). The fleet manager maps `GET /wms/delivery-failures` and returns the snapshot. `WmsRoutes.DeliveryFailures` is the route constant.

**R10 — configuration.** Add `Wms:RetryBaseDelayMs` (default 1000) and `Wms:MaxRetryDelayMs` (default 30000). Validate both at startup: base > 0, max >= base. The mock validates `MockWms:SlowDelayMs` > 0. No timeout, URL, delay, or retry count is hard-coded.

**R11 — the facts.** At least these tests exist and pass:

- `WmsDeliveryOutcomeTests`: `A_2xx_reply_is_delivered`, `A_500_reply_is_retry`, `A_408_and_429_are_retry`, `A_400_and_404_and_409_are_permanent`, `No_reply_is_retry`.
- `WmsRetryPolicyTests`: `The_first_failure_waits_the_base_delay`, `The_delay_doubles_each_failure`, `The_delay_stops_at_the_cap`, `A_zero_failure_count_is_rejected`.
- `MockWmsLedgerTests`: `A_first_word_is_applied`, `The_same_word_twice_is_a_duplicate`, `An_older_word_does_not_replace_a_newer_one`, `A_different_pallet_for_the_same_order_is_a_conflict`, `Cancelled_is_above_done`, `A_done_word_is_not_replaced_by_failed`.
- `MockWmsControlTests`: `An_unknown_mode_is_rejected`, `A_missing_slow_delay_keeps_the_current_delay`.
- `WmsStatusPusherTests`: `A_500_reply_is_retried_after_the_first_wait`, `A_permanent_4xx_is_recorded_and_not_retried`, `A_successful_retry_clears_the_backoff_and_the_failure`.

Update the existing `Push_failure_keeps_the_order_pending_for_the_next_pass` test to use a fixed clock. It currently calls `SendPendingAsync` twice in a row; the second call is now "not due" and would skip the `200`. Advance the clock by the base delay between the calls.

**R12 — what you must not touch.** `src/Contracts/`, `docs/contract/`, `ContractVersion.Current` (stays `4`), `WmsOrderBoard`, `WmsStatusOutbox`, `TaskState`, `WmsOrderLadder`, and the cancel route from lesson 27. The mock has its own rank; it does not change the fleet's ladder. The WMS REST page stays at version 2.

### Acceptance criteria

```bash
BASE=$(git rev-parse HEAD)
cd src && dotnet build --no-incremental
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
test -f src/FleetCore/WmsDeliveryOutcome.cs
test -f src/FleetCore/WmsRetryPolicy.cs
test -f src/FleetCore/WmsDeliveryFailures.cs
test -f src/MockWms/MockWmsLedger.cs
test -f src/MockWms/Program.cs
test -f src/MockWms.Tests/MockWmsLedgerTests.cs
grep -q "MockWms" src/ShuttleFleet.slnx
grep -q "MaxRetryDelayMs" src/FleetManager/appsettings.json
grep -q "WmsDeliveryOutcome.FromStatus" src/FleetManager/WmsStatusPusher.cs
grep -q "DelayFor" src/FleetManager/WmsStatusPusher.cs
grep -q "WmsRoutes.DeliveryFailures" src/FleetManager/Program.cs
grep -q "WmsDeliveryFailures" src/FleetManager/Program.cs
test -z "$(git diff --name-only "$BASE" -- src/Contracts docs/contract)"
```

All twenty test names in R11 must appear in `dotnet test --list-tests` and pass.

Manual smoke test. Start the mock, push the same body twice, check the counters, flip to `error`, and stop it:

```bash
cd src
dotnet run --project MockWms/MockWms.csproj > /tmp/mockwms.log 2>&1 &
MOCK=$!
sleep 2
BODY='{"orderId":"ORD-1","kind":"store","palletId":"PLT-1","laneId":"A01-L01","state":"in_progress","receivedAt":"2026-01-01T00:00:00Z"}'
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://127.0.0.1:5090/api/order-status -H 'Content-Type: application/json' -d "$BODY"
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://127.0.0.1:5090/api/order-status -H 'Content-Type: application/json' -d "$BODY"
curl -s http://127.0.0.1:5090/mock/state
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://127.0.0.1:5090/mock/mode -H 'Content-Type: application/json' -d '{"mode":"error"}'
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://127.0.0.1:5090/api/order-status -H 'Content-Type: application/json' -d "$BODY"
curl -s -o /dev/null -w '%{http_code}\n' -X POST http://127.0.0.1:5090/mock/mode -H 'Content-Type: application/json' -d '{"mode":"ok"}'
kill $MOCK
```

Expected: `200`, `200`, the state shows one entry and `duplicate=1`, `200` for the mode set, `500` for the push in error mode, `200` for the mode set back. The exact JSON field names are yours; the counters and the ledger entry are the facts. Do not leave the mock running.

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Make `FromStatus` send `400` to `Retry` | `A_400_and_404_and_409_are_permanent` |
| b | Make `DelayFor` return the base every time | `The_delay_doubles_each_failure` |
| c | Remove the cap from `DelayFor` | `The_delay_stops_at_the_cap` |
| d | Make the ledger overwrite on an older word | `An_older_word_does_not_replace_a_newer_one` |
| e | Make the ledger compare only rank, so `done` and `failed` are a duplicate | `A_done_word_is_not_replaced_by_failed` |
| f | Re-mark the word on a permanent `4xx` | `A_permanent_4xx_is_recorded_and_not_retried` |
| g | Remove the "not due" check | `A_500_reply_is_retried_after_the_first_wait` |
| h | Do not clear the retry state or the failure on success | `A_successful_retry_clears_the_backoff_and_the_failure` |

### Stretch goal

**Give the mock a submit client.** Add `POST /mock/orders` that takes the `SubmitOrderRequest` shape and POSTs it to the fleet manager's `POST /api/orders` (config `FleetManager:BaseUrl`, default `http://127.0.0.1:5080`). Add `GET /mock/orders` to list what the mock submitted. Then run the full loop: mock submits, fleet assigns, shuttle moves, fleet pushes `in_progress` and `done` back to the mock. Measure the time from submit to the mock's `done` ledger entry. Write it down. This is real M6 work, not a toy.

### What comes next

- **The traffic-control-under-dashboard slice (M7).** The operator's pause must stop shuttle movement, not only task dispatch. The tick must consult the traffic gate and the pause flag.
- **The mock's submit and cancel client.** The stretch goal above, or the next slice.
- **M8 stretch.** OPC UA or Modbus TCP gateway, Python scenario runner, or a load test with measured limits. The mock's `slow` and `error` modes are ready for the load test.

---

## What I assumed about what you know

I could not ask you in person, so I assumed you are comfortable with:

- C# records, enums, switch expressions, dictionaries, and `lock`.
- ASP.NET Core minimal APIs (`WebApplication.CreateBuilder`, `MapPost`, `MapGet`, `Results.Ok`, `Results.Problem`, `Results.ValidationProblem`).
- `HttpClient`, `PostAsync`, `HttpResponseMessage.StatusCode`, and client timeouts.
- `PeriodicTimer`, `CancellationToken`, and `async`/`await`.
- `System.Text.Json` with the web defaults (camelCase).
- xunit and `dotnet new` / `dotnet sln`.

The fleet ideas in this lesson — at-least-once delivery, an idempotent receiver, and exponential backoff — are taught from first principles. If any of them is new, say so before you build, and we will walk through it before the assignment.

I also assumed lesson 27 is the next thing you build. Lesson 27's cancel code is not in the tree today (measured: no `Cancelled` in `TaskState`). This slice does not need it. The mock's rank includes `cancelled` so it is ready when 27 lands.
