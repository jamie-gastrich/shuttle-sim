# Lesson 23: Status push — telling the WMS when a word moves

**Milestone:** M6 (WMS integration) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 22: The order state ladder](22-order-state-ladder.md) · **Decision:** [record 31](../decisions/31-status-push.md)

**Concepts this assignment requires:**

- [ ] **A word is owed to the WMS only when the word moved.** The `Advanced` outcome is the send key; silence is a valid answer. Taught in [1) A word is owed only when the word moved](#1-a-word-is-owed-only-when-the-word-moved).
- [ ] **The outbox is a pile of "I owe you" flags.** One flag per order, a lock, claim-then-requeue. Taught in [2) The outbox is a pile of I-owe-you flags](#2-the-outbox-is-a-pile-of-i-owe-you-flags).
- [ ] **The timed sender sends when it can.** A background loop, a client-side timeout, and every failure putting the word back. Taught in [3) The timed sender sends when it can](#3-the-timed-sender-sends-when-it-can).

Three new concepts. That is the cap.

**Not in this lesson:** cancelling an order; wiring the dispatcher into `serve` (so still no production caller for the sync or the note); building the mock-WMS receiver (nothing listens on the new route yet); persistence; load/unload; the dashboard; the deferred door-hardening slice; any MQTT contract change (`ContractVersion.Current` stays `4`, `docs/contract/v4.md` and `src/Contracts/` untouched); and any edit to `WmsOrderLadder` — review 22's issue 4 rides along to the cancel slice, which must open that file anyway.

**The end-product fact:** after lesson 22, the order's `State` follows its task's state — inside the fleet manager's memory. The WMS still hears nothing. Its last direct word was the `201 Created` body at intake, which said `received`. A pallet can finish its whole trip and the WMS would never know. M6 says the WMS **receives status updates**, and its done-when says orders flow *"and back."* This lesson builds the sending half: the rule for what is owed, the pile that remembers, and the loop that delivers — slowly or not at all, without touching fleet state either way.

---

## Where this fits

M6 says: *"orders flow from the WMS to completed pallets **and back**, and a WMS outage or a bad order does not corrupt fleet state."*

Lessons 20 and 21 built the way in (the REST door) and the way down (an accepted order becomes a task). Lesson 22 made the order's own state tell the truth about that task, and returned a three-way outcome — `Advanced`, `AlreadyThere`, `NoOrder` — precisely so a caller could know when the truth *moved*. This lesson is that caller. The execution slice, after this, puts two lines on every task transition; the mock-WMS slice then gives those lines someone to talk to.

**Two measured facts shape this whole lesson.**

There is no outbound HTTP anywhere in the project yet:

```bash
grep -rn "HttpClient" src --include=*.cs
# no matches (exit code 1)
```

So the sender is the first of its kind here: one long-lived `HttpClient`, one loop, one route — and every network behaviour it relies on was measured in scratch before this lesson was written (the verification table below).

And the sync still has no production caller, same as lesson 22:

```bash
grep -rn "SyncFromTask" src --include=*.cs | grep -v Tests
# one hit: the definition, src/FleetCore/WmsOrderBoard.cs:49
```

The tests remain the callers, shaped exactly like the future transition line. Concept 1 fixes the two-line rule the execution slice inherits, so that slice never has to invent it.

**Forward references.** The execution slice adds `SyncFromTask(taskId)` then `NoteSync(taskId, outcome)` on the line after each task transition (record 30 §4, extended by record 31 §5). The mock-WMS slice implements the contract page this lesson bumps to version 2 — it is the receiver this sender exists for. The cancel slice owns `cancelled`, the reverse direction, and the `Rank` alignment. M7's dashboard gets a ready-made tile: `WmsStatusOutbox.PendingCount` — "words the WMS is owed."

---

## Verification table

Everything below was checked today (2026-10-20), in this repo or in scratch copies under `/tmp/opencode`. Scratch copies were deleted afterwards. Nothing was left running.

| Claim | How I checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| Baseline builds clean | fresh copy of `src` in `/tmp/opencode/l23base`; `dotnet build` | **0 Warning(s), 0 Error(s)** |
| Baseline pure suite is green | same copy; `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0; **Failed: 0, Passed: 169, Total: 169** (FleetCore 45, FleetManager 56, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10) |
| No outbound HTTP exists yet | `grep -rn "HttpClient" src --include=*.cs` | no matches (rc 1) |
| No production sync caller | `grep -rn "SyncFromTask" src --include=*.cs \| grep -v Tests` | one hit: `src/FleetCore/WmsOrderBoard.cs:49` |
| The REST page is at v1, no push route | `grep -n "^Version:" docs/contract/wms-rest.md`; `grep -c "order-status" docs/contract/wms-rest.md` | `Version: 1` (line 3); `0` |
| The page's own bump rule | read `docs/contract/wms-rest.md:90-93` | decision record + page version bump; it never bumps the MQTT contract |
| The MQTT contract needs no move | `grep -n "Current = " src/Contracts/MessageTypes.cs`; `grep -n "shuttleStatus" docs/contract/v4.md` | `Current = 4`; v4's only status message is `shuttleStatus` |
| No `Wms` config section | `grep -c '"Wms"' src/FleetManager/appsettings.json` | `0` |
| Route constants live in `WmsRoutes` and get literal pins | read `src/FleetCore/WmsRoutes.cs`; `src/FleetCore.Tests/WmsContractLiteralsTests.cs:9-14` | `Orders` const + `Orders_route` fact — the shape this lesson copies |
| `/api/orders/status` would mean two things | read `src/FleetCore/WmsRoutes.cs:8` | `Order("status")` builds `/api/orders/status` — a literal id on the GET route |
| Default JSON is the wrong body shape | scratch `/tmp/opencode/l23probe`, E4 | default: `{"OrderId":"ORD-1001",…,"State":"in_progress",…}` |
| Web defaults are the documented shape | same run | `{"orderId":"ORD-1001",…,"state":"in_progress",…}` |
| The door already serves that shape | scratch `l23web`, minimal API + `curl` | `{"orderId":"ORD-1001","kind":"store","palletId":"PLT-001","laneId":"A01-L01","state":"received","receivedAt":"2026-10-19T10:00:00Z"}` |
| A `500` reply does **not** throw | scratch `l23fivehundred`: listener returns 500 | `500 reply: NO THROW, status=500 after 58 ms` |
| A slow WMS trips the client timeout | scratch `l23probe`, E1: real listener sleeps 3 s, client timeout 500 ms | `TaskCanceledException after 520 ms; inner=TimeoutException`; server saw `path=/api/order-status contentType=application/json; charset=utf-8` |
| BaseAddress + absolute path resolve cleanly | same run | base `http://127.0.0.1:5099` (no trailing slash) + `/api/order-status` → `http://127.0.0.1:5099/api/order-status` |
| A refused connection is a fast, typed failure | scratch `l23probe`, E2: nothing on the port | `HttpRequestException after 5 ms; inner=SocketException` |
| Tests can fake the network with no sockets | scratch `l23probe`, E3: `HttpMessageHandler` sleeping 3 s | `TaskCanceledException after 501 ms; inner=TimeoutException` |
| The timeout's type is catchable as `OperationCanceledException` | scratch `l23const` | `base of TaskCanceledException = OperationCanceledException` |
| Healthy POST latency, for the timeout default | scratch `l23rtt`: 10 posts to a local listener | first `200`; total 439 ms, **avg 43.9 ms** |
| The pile's lock does real work | scratch `l23lock`: 8 threads × 1000 marks, one claimer, 3 rounds | with the lock: `claimed=8000 expected=8000 PASS` ×3; without: crash RC 134 ×3 (`IndexOutOfRangeException` ×2, `InvalidOperationException: … corrupted its state` ×1) |
| Constant patterns compile (context for review 22's issue 4) | scratch `l23const` | `Rank(received)=0` — a `const string` switch arm builds and runs |
| No catch hides a task refusal today | `grep -rn "catch (TaskRejected" src --include=*.cs` | no matches (rc 1) |
| The house loop shape | read `src/FleetManager/FleetServeLoop.cs:42-44` | `PeriodicTimer` + `while (await timer.WaitForNextTickAsync(ct))` |
| The house shutdown shape | read `src/FleetManager/Program.cs:212-227` | `catch (OperationCanceledException) when (stopping.IsCancellationRequested)` per awaited task |
| `FleetManager.Tests` can reach `FleetCore` | read `src/FleetManager/FleetManager.csproj:8`; existing `FleetManager.Tests` files already use `TaskBoard`/`FleetTask` | the transitive reference is live in the working suite |
| No containers, no stray processes | `docker compose -f docker/compose.yml ps`; `pgrep -af "dotnet run\|FleetManager.dll\|mosquitto"` | empty table; no matches |
| Working tree before writing | `git status --short`, `git log --oneline -3` | clean; HEAD `aa29b3f` |

Not yet verified: the code this lesson asks you to build. That is the assignment.

---

## Options survey (design choices)

### Survey 1: How does the WMS get the words?

| | A: fleet manager POSTs each change (chosen) | B: WMS polls `GET /api/orders/{id}` | C: MQTT topic the WMS subscribes to |
|---|---|---|---|
| Fits the spec's direction ("WMS *receives*") | **yes** | reads as pull | crosses the REST boundary the spec draws |
| Can use the `Advanced` moment | **yes** | no — poll-and-compare | yes |
| Who drives the load | the fleet, per change | the WMS, blind | the broker |
| Contract cost | one section on the REST page | none | a new MQTT message family; v4 moves |
| Slow-WMS risk | bounded by the client timeout (concept 3) | none | broker-side backpressure |

### Survey 2: When does the POST actually run?

| | A: a background loop in `serve` (chosen) | B: await the POST at the transition line | C: fire-and-forget `Task` per mark |
|---|---|---|---|
| A dead WMS slows task work | **no** — two in-memory calls stay on the line | yes — up to the timeout, every transition | no, but see next row |
| Who owns retry and ordering | one loop, one queue | nobody — the line moves on | nobody; unbounded concurrent POSTs |
| Shutdown story | the house cancellation dance | tangled into the dispatcher | unobserved exceptions; `async void` banned |
| Extra concepts | none (the shape is `FleetServeLoop`) | blocking in a hot path | task lifetimes, exception swallowing |

### Survey 3: What does the pile remember?

| | A: one flag per order; the word is read at send time (chosen) | B: the body is stored when marked | C: a FIFO of every event, no coalescing |
|---|---|---|---|
| Newest word wins for free | **yes** — `TryGet` at send | only if every mark overwrites | n/a — every word is sent |
| Memory while the WMS is down | bounded by the order count | bounded, but duplicates order data | grows forever |
| Two sources of truth for the order | **no** — the board stays the only one | yes — a stale copy waits to be sent | yes |
| Matches the contract's "current place, not a history" | **yes** | yes | no |

Full reasons are in [record 31](../decisions/31-status-push.md).

**Confirming against the installed source, not memory.** The two greps, the route shapes, the page's bump rule, the loop and shutdown precedents, and every network behaviour above come from running the code or reading the installed files today. The five scratch programs (`l23probe`, `l23rtt`, `l23const`, `l23web`, `l23fivehundred`, `l23lock`, plus the toy) were built, run, and deleted; their outputs are pasted, not paraphrased.

---

## Decision

[Record 31](../decisions/31-status-push.md) settles all of it:

- **A.** The fleet manager POSTs each state change to the WMS over HTTP. Not polling, not MQTT.
- **B.** `docs/contract/wms-rest.md` goes **version 1 → 2** with one new section, pinned verbatim in record 31 §3. The MQTT contract does not move.
- **C.** The route is `WmsRoutes.OrderStatus = "/api/order-status"`, a pinned constant. The body is the full documented `WmsOrder`, serialized with `JsonSerializerDefaults.Web` — the camelCase shape the door already serves.
- **D.** The send key: `NoteSync(taskId, outcome)` marks the order pending **iff** the outcome is `Advanced`. `AlreadyThere`, `NoOrder`, and intake's own `received` owe nothing.
- **E.** The pile (`WmsStatusOutbox`, `FleetCore`) stores one flag per order id under its own lock. `Claim` takes-and-empties; failures put the flag back; the sender reads the word from the board at send time.
- **F.** One sender (`WmsStatusPusher`, `FleetManager`): `SendPendingAsync` is one testable pass; `RunAsync` is the `PeriodicTimer` loop; one long-lived `HttpClient`.
- **G.** Every failure shape — non-`2xx`, refused, timed out — does the same thing: put the flag back and log. No attempt cap; the interval is the retry spacing.
- **H.** Three config keys: `Wms:BaseUrl`, `Wms:StatusTimeoutMs` (2000), `Wms:StatusIntervalMs` (1000). No URL, timeout, or interval in code.
- **I.** Logging is `Console.WriteLine`, the house style (measured: six sites, zero `ILogger`).

Read the record before you write code. Every requirement below is decided there.

---

## The lecture

Today we are going to learn about **the rule that says a word is owed to the WMS only when the word moved**, **the outbox that remembers what is owed**, and **the timed sender that delivers it when it can**.

### 1) A word is owed only when the word moved

Lesson 22 ended with three outcomes. This is the whole trick of this slice: one of them means *owed*.

```csharp
// the key, from src/FleetCore (record 31 §5 — the shape you will build)
public void NoteSync(string taskId, OrderSyncOutcome outcome)
{
    if (outcome == OrderSyncOutcome.Advanced)
        MarkPending(taskId);
}
```

- `Advanced` means the stored word **just changed**. The ladder climbed. The WMS has an older word (or none — the `201` at intake). So one flag goes on the pile. That is the send key, and nothing else is.
- `AlreadyThere` means the word is already right. The WMS heard it when it moved. Saying it again would turn a quiet machine into a chatterbox. Nothing is marked.
- `NoOrder` means no WMS order stands behind this task — a local move, some future feature. There is no body to send and no party to send it to. Nothing is marked.
- Intake does not push `received` either. The `201 Created` reply *is* the body, with `state: "received"` in it (the routes table, `wms-rest.md:68`). The WMS read the word at the moment it was written. A push a millisecond later would say the same thing. Silence is the correct thank-you.

Read the rule as one sentence: **a word is owed only when the word moved.** Not on a timer, not on a read, not on a repeat. This is what "idempotent" (lesson 05) buys all the way down the stack: a sync that runs twice does not queue two flags, because the second sync returns `AlreadyThere` and `AlreadyThere` queues nothing.

**Where the caller will stand.** Record 30 §4 fixed the transition rule; this slice extends it by one line:

```text
transition the task  ->  SyncFromTask(taskId)  ->  NoteSync(taskId, outcome)  ->  next line of work
```

That is the execution slice's inheritance, written now so it cannot re-decide. Still nothing in `serve` calls either method — the greps in "Where this fits" prove it — so the tests play both roles, exactly like lesson 22's.

**The route, and why it is a constant.** The sender needs a destination path. Contract vocabulary lives in `WmsRoutes` and gets a literal pin — the same treatment `/api/orders` already has:

```csharp
// src/FleetCore/WmsRoutes.cs (real, today — the shape you are extending)
public static class WmsRoutes
{
    public const string Orders = "/api/orders";

    // The route template uses "{orderId}"; the built path uses the value.
    public static string Order(string orderId) => $"{Orders}/{orderId}";
}
```

You add `public const string OrderStatus = "/api/order-status";` beside `Orders`, and one fact beside the existing `Orders_route` test: `Assert.Equal("/api/order-status", WmsRoutes.OrderStatus);`. The literal is the point, same as lesson 22's ladder pins: it fixes what the client depends on, whatever anyone later calls the constant.

Why not `/api/orders/status`? Look at `Order`: it builds `/api/orders/{orderId}`. The string `status` is a legal order id, so that path already means "the order whose id is status" on the GET route. Two meanings for one path is how outages start. A status update is a different kind of thing than an order collection; it gets its own path.

**The body you owe, and the one serializer line that keeps it honest.** The contract page this lesson bumps documents the body: the full `WmsOrder` — `orderId`, `kind`, `palletId`, `laneId`, `state`, `receivedAt` — camelCase, exactly what the door sends in its `201`. Two measurements make the requirement concrete. First, the default serializer is wrong:

```text
E4 default: {"OrderId":"ORD-1001","Kind":"store","PalletId":"PLT-001","LaneId":"A01-L01","State":"in_progress","ReceivedAt":"2026-10-19T10:00:00Z"}
E4 web:     {"orderId":"ORD-1001","kind":"store","palletId":"PLT-001","laneId":"A01-L01","state":"in_progress","receivedAt":"2026-10-19T10:00:00Z"}
```

(Both from a scratch run today on the real `WmsOrder` shape.) PascalCase is a body the contract never promised. Second, the door already serves the Web shape — a scratch minimal API returning the same record answered my `curl` with:

```text
{"orderId":"ORD-1001","kind":"store","palletId":"PLT-001","laneId":"A01-L01","state":"received","receivedAt":"2026-10-19T10:00:00Z"}
```

So the sender serializes with `new JsonSerializerOptions(JsonSerializerDefaults.Web)`. One line, and the push body and the `201` body are the same shape from the same field table.

**Where this is used later.** The execution slice calls `NoteSync` on the line after each sync. The dashboard can show `PendingCount`. The mock-WMS slice implements the route this constant names — its whole job is to be the other end of this line.

### 2) The outbox is a pile of I-owe-you flags

An **outbox** is the pile of messages that are ready to leave but have not left yet. This slice's pile is a small cousin of the real thing: it stores one **flag per order id**, not the body — because the body already lives in the board (lesson 22's rule: hold the board, not the order).

It lives in `FleetCore`, and it owns exactly three moves plus a count:

```csharp
// src/FleetCore/WmsStatusOutbox.cs — the shape, decided in record 31 §6
sealed class WmsStatusOutbox
{
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void NoteSync(string taskId, OrderSyncOutcome outcome)
    {
        if (outcome == OrderSyncOutcome.Advanced)
            MarkPending(taskId);
    }

    public void MarkPending(string orderId)
    {
        lock (_gate) _pending.Add(orderId);   // adding twice = adding once
    }

    public List<string> Claim()
    {
        lock (_gate)
        {
            var ids = _pending.ToList();
            _pending.Clear();
            return ids;
        }
    }

    public int PendingCount { get { lock (_gate) return _pending.Count; } }
}
```

Walk it, four short pieces:

- `MarkPending` takes the lock and adds. A `HashSet` makes the add **idempotent** (lesson 05 again): marking the same order twice leaves one flag. That is what lets a failure simply put the flag back — no counters, no duplicates.
- `Claim` takes the lock, copies the ids out, **clears the pile**, and returns the copy. Take-and-empty means one sender pass owns exactly the flags that existed when it started. Anything marked *during* the pass is next tick's work — nothing can slip between the claim and the send.
- `PendingCount` is a locked count. Tests read it; a future dashboard tile can too.
- The lock is not optional. Two threads touch this set: whatever marks (today an HTTP-adjacent caller in tests; tomorrow the dispatcher thread) and the sender loop. A `HashSet` is not safe for that — measured on this exact shape: with the lock, 3/3 rounds of 8 threads × 1000 marks claimed exactly 8000; without, 3/3 runs crashed (`IndexOutOfRangeException` twice, `InvalidOperationException: … corrupted its state` once). The lock shape has a house precedent — `FleetServeLoop` guards its shuttle set the same way (`_knownLock`, `_knownShuttles`).

**Why a pile at all — why not send inside `NoteSync`?** Because the sender must own its own failures. If `NoteSync` POSTed, the transition line would wait on a network that may be down, and a failure would have nowhere to go but an exception into the fleet path. The pile is the wall between "a word moved" (fast, in-memory, must never block) and "a word goes out" (slow, fallible, retried). Concretely: `Claim` hands the sender a clean batch; every failure path in concept 3 ends with one call — `MarkPending` — and the word is simply owed again.

**Why the sender reads the word at send time.** The flag says "this order owes a word," not *which* word. When the sender picks the flag up, it asks the board: `board.TryGet(orderId, …)` and sends whatever the board says now. If `in_progress` was flagged and the task finished before the send, the WMS is told `done`. Newest truth wins for free, and the pile never holds a stale copy of anything — record 31 §6's rejected alternatives (store the body; queue every event) both break that, and the contract's own words back it up: `state` is the order's *current place in the ladder*, not a history.

One naming note, so the code does not surprise you: `NoteSync`'s parameter is `taskId` — the caller is standing on a transition line holding a task id. `MarkPending` and the pile's contents are `orderId`s. It is the same string (record 29 §3: task id = order id); the two names just follow the string's journey from the task side of the house to the WMS side.

**Where this is used later.** When persistence lands (record 29 §7), "re-mark pending on restore" becomes this class's next record: a restart today drops the pile together with the boards, so there is no half-state to fix — that stops being true the day orders live in SQL.

### 3) The timed sender sends when it can

A **timed loop** wakes on a clock and does one job. The house shape is `PeriodicTimer` — `FleetServeLoop` uses it for alarm evaluation — and the sender copies it:

```csharp
// src/FleetManager/WmsStatusPusher.cs — RunAsync, the loop half
public async Task RunAsync(CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(_interval);
    while (await timer.WaitForNextTickAsync(cancellationToken))
        await SendPendingAsync(cancellationToken);
}
```

- One `PeriodicTimer`, one interval, from config. Every tick means: "try whatever is owed."
- `WaitForNextTickAsync(cancellationToken)` waits, then returns `true`. When the token is cancelled it throws — the same way `FleetServeLoop.RunAsync` stops, and Program catches it the same way (concept 3's wiring, below).
- `SendPendingAsync` is separate and public on purpose: tests call one pass directly with a stub network, no loop, no waiting. That split is the whole testability story.

**One pass, three failure shapes, one behaviour.** Here is the pass, with the branches named. This is the heart of the slice:

```csharp
// SendPendingAsync — the shape (record 31 §8); you write the real one
public async Task SendPendingAsync(CancellationToken cancellationToken)
{
    foreach (var orderId in _outbox.Claim())
    {
        if (!_board.TryGet(orderId, out var order))
        {
            Console.WriteLine($"status push: {orderId} has no order, dropped");
            continue;                       // do not requeue: a phantom id would loop forever
        }

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(order, Json), Encoding.UTF8, "application/json");
            using var response = await _client.PostAsync(WmsRoutes.OrderStatus, content, cancellationToken);
            if (response.IsSuccessStatusCode)
                Console.WriteLine($"status push: {order.OrderId} -> {order.State} delivered");
            else
            {
                _outbox.MarkPending(order.OrderId);
                Console.WriteLine($"status push: {order.OrderId} -> {order.State} rejected with {(int)response.StatusCode}, will retry");
            }
        }
        catch (HttpRequestException)
        {
            _outbox.MarkPending(order.OrderId);
            Console.WriteLine($"status push: {order.OrderId} -> {order.State} failed to send, will retry");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _outbox.MarkPending(order.OrderId);
            Console.WriteLine($"status push: {order.OrderId} -> {order.State} timed out, will retry");
        }
    }
}
```

Every branch above rests on a measurement from today, so none of it is folklore:

- **A rejected reply does not throw.** `PostAsync` hands you the response; you check it yourself. Measured: a listener answering `500` produced `500 reply: NO THROW, status=500 after 58 ms`. That is why the `IsSuccessStatusCode` branch exists at all — miss it and a `5xx` looks like success.
- **A dead WMS fails fast and typed.** Nothing listening on the port: `HttpRequestException after 5 ms; inner=SocketException`. One catch, requeue, move on.
- **A slow WMS trips the client's own timeout.** A listener that sleeps 3 s against a 500 ms client: `TaskCanceledException after 520 ms; inner=TimeoutException`. And `TaskCanceledException` derives from `OperationCanceledException` — measured: `base of TaskCanceledException = OperationCanceledException`. So one catch covers it.
- **That catch needs a filter, because the same exception type also means "we are shutting down."** `when (!cancellationToken.IsCancellationRequested)`: our shutdown cancels the token, the exception must escape `RunAsync` and reach Program's catch. The client's timeout fires while our token is still live — that one is a failed send. Same type, two meanings; the filter tells them apart.
- **The timeout itself lives on the `HttpClient`**, not around the call — `new HttpClient { Timeout = … }` is the mechanism both probes went through. It bounds every request the pusher will ever make.

Note the one deliberate non-requeue: a claimed id whose order is *gone* is dropped with a log line, not put back. A flag can only appear via `Advanced`, which required the order to exist — so a missing order is a bug report, and requeueing it would loop on a ghost forever. Loud and done, not loud and stuck.

**No attempt cap, on purpose.** Commands in this project get `MaxAttempts` (record 06) because a command is an *action*: re-running it needs idempotency and an ack. A status word is *truth*. Giving up on the truth quietly breaks M6's "orders flow … and back." The pile is bounded by the order count in memory; the interval is the spacing; the log line is the alarm. One key does both jobs — `Wms:StatusIntervalMs` is the tick *and* the retry delay. Record 31 §8 names what would change this: a *permanent* rejection class (the realistic one being a WMS that restarted and forgot an order), which deserves attempts plus an alarm, in the slice that also builds the receiver inventing the rejection.

**Now the wiring — config first, then Program.** Three keys, read and validated the way every other key in `RunServeAsync` is (`Program.cs:143-155` is the house shape):

```csharp
// src/FleetManager/Program.cs — inside RunServeAsync, beside the other keys
var wmsBaseUrl = configuration.GetValue<string>("Wms:BaseUrl")
    ?? throw new InvalidOperationException("Wms:BaseUrl is missing.");
if (!Uri.TryCreate(wmsBaseUrl, UriKind.Absolute, out var wmsUri))
    throw new InvalidOperationException("Wms:BaseUrl must be an absolute URL.");
var wmsTimeoutMs = configuration.GetValue<int>("Wms:StatusTimeoutMs");
if (wmsTimeoutMs < 1)
    throw new InvalidOperationException("Wms:StatusTimeoutMs must be positive.");
var wmsIntervalMs = configuration.GetValue<int>("Wms:StatusIntervalMs");
if (wmsIntervalMs < 1)
    throw new InvalidOperationException("Wms:StatusIntervalMs must be positive.");
```

- The URL is checked with `Uri.TryCreate` **at startup** — a bad URL fails before the bus connects, not on the first send mid-loop.
- Then the three objects, right after the board is built (`Program.cs:164-165`):

```csharp
var outbox = new WmsStatusOutbox();
using var wmsClient = new HttpClient { BaseAddress = wmsUri, Timeout = TimeSpan.FromMilliseconds(wmsTimeoutMs) };
var pusher = new WmsStatusPusher(board, outbox, wmsClient, TimeSpan.FromMilliseconds(wmsIntervalMs));
```

- One `HttpClient` for the process — built once, never per send (a client per send burns sockets; the lesson-20 door and this sender share the same .NET habit). `BaseAddress` plus the constant's absolute path resolves to the full URL — measured: `http://127.0.0.1:5099` (no trailing slash) plus `/api/order-status` came out `http://127.0.0.1:5099/api/order-status`.
- Start the loop beside the existing one (`Program.cs:176`), and await it with the house dance (`Program.cs:212-219` is the template):

```csharp
var loopTask = loop.RunAsync(stopping.Token);
var pushTask = pusher.RunAsync(stopping.Token);        // beside it
// …
try { await loopTask; } catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
try { await pushTask; } catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
```

The `pushTask` block mirrors `loopTask`'s, line for line: same exception type, same filter, same comment shape. Ctrl+C cancels the token; `PeriodicTimer` throws; the loop ends; Program's door closes behind it.

**And this is M6's "no corruption" sentence, this slice's share.**

- A **bad order** never reaches the pile — no task, no ladder step, no `Advanced`, nothing owed (lesson 21's intake table still holds).
- A **slow WMS** bounds itself with the client timeout and never touches the transition line, whose new work is two in-memory calls. Lesson 20's promise, kept twice now.
- A **dead WMS** leaves a pile, not a wound. Every failure shape measured above ends in one `MarkPending`; the board, the tasks, and the ladder are untouched. When the WMS returns, the pile drains — newest word per order, because of concept 2's read-at-send rule.
- The **words cannot contradict each other**: the ladder refuses backwards moves (lesson 22), the lock keeps the check-then-write honest (concept 2's probe), and the sender tells the board's current truth.
- A **restart** drops the pair — now a triple — together (record 29 §5, extended by record 31 §11). Nothing half-sent survives; the WMS's view and the fleet's memory rebuild from the same seed: intake's `201` bodies.

---

## Recap in plain words

- A word is owed to the WMS only when the word moved: `NoteSync` marks on `Advanced` and on nothing else. Intake's `received` travels in the `201` body, so it is not pushed.
- The outbox is a pile of one flag per order, under its own lock. Marking is idempotent. `Claim` takes-and-empties. Failures put the flag back.
- The sender reads the word from the board at send time, so the newest truth wins and the pile never holds a stale copy.
- The sender is a `PeriodicTimer` loop with one public pass (`SendPendingAsync`) that tests call directly with a stub network.
- Three failure shapes, one behaviour: non-`2xx`, refused, timed out — all requeue and log. The timeout catch needs a `when` filter because shutdown wears the same exception.
- The timeout lives on the `HttpClient`; the URL, the timeout, and the interval are three config keys, validated at startup.
- Nothing in `serve` moves a task yet, so nothing in `serve` notes a move yet. The execution slice inherits two lines per transition; the receiver slice gives them a destination.
- The contract page goes to version 2 with one section. The MQTT contract does not move.

---

## Worked example (toy — NOT the assignment)

This toy shows one mechanism only: **a word that moved is owed, a failed send goes back on the pile, and the pile's word is read at send time so the newest one wins.** It has no MQTT, no HTTP, no real project types, and no fleet manager. The assignment is the real thing.

Create it and run it:

```bash
cd /tmp/opencode
rm -rf l23toy
dotnet new console -o l23toy
# paste every csharp listing below, in the order they appear, into l23toy/Program.cs
dotnet run --project l23toy
```

The listings are one file read top to bottom — the prose between them is the explanation, not part of the file. `/tmp/opencode/l23toy/Program.cs`, first the flow (exactly as I ran it):

```csharp
// TOY - not the assignment. No MQTT, no HTTP, no real project types.
// It shows one idea: a sync that says Advanced puts the order in an outbound
// pile, a failed send puts it back, and the word is read at send time.

var board = new ToyBoard();
var outbox = new ToyOutbox();
var delivered = new List<string>();

// Intake stored "received". The first sync agrees, so nothing is owed.
board.Submit("ORD-1");
var outcome = board.Sync("ORD-1");
outbox.NoteSync("ORD-1", outcome);
Console.WriteLine($"accept   sync={outcome,-12} owed={outbox.PendingCount}");

// A shuttle starts. The word moved, so the WMS is owed one word.
board.MoveTask("ORD-1", "in_progress");
outcome = board.Sync("ORD-1");
outbox.NoteSync("ORD-1", outcome);
Console.WriteLine($"started  sync={outcome,-12} owed={outbox.PendingCount}");
Tick("tick1", wmsIsUp: false);

// The move finishes while the WMS is still down.
board.MoveTask("ORD-1", "done");
outcome = board.Sync("ORD-1");
outbox.NoteSync("ORD-1", outcome);
Console.WriteLine($"finished sync={outcome,-12} owed={outbox.PendingCount}");
Tick("tick2", wmsIsUp: false);
```

- Each pair of lines is the future transition rule: move the task, sync, note the outcome. Two calls in, two lines out.
- `Tick(…, wmsIsUp: false)` stands in for concept 3's failure branches: the send is attempted and refused, and the flag goes back.

The turn, and the two odd cases:

```csharp
// The WMS comes back.
Tick("tick3", wmsIsUp: true);

// The task does not move again: AlreadyThere owes nothing, the sender stays quiet.
outcome = board.Sync("ORD-1");
outbox.NoteSync("ORD-1", outcome);
Console.WriteLine($"again    sync={outcome,-12} owed={outbox.PendingCount}");
Tick("tick4", wmsIsUp: true);

// A local task with no WMS order behind it: NoOrder owes nothing either.
outbox.NoteSync("LOCAL-7", OrderSyncOutcome.NoOrder);
Console.WriteLine($"local    sync=NoOrder      owed={outbox.PendingCount}");

Console.WriteLine();
Console.WriteLine($"delivered: {(delivered.Count == 0 ? "(nothing)" : string.Join(", ", delivered))}");
```

- One flag was owed across two outages; when the WMS returned, **one** send carried the *newest* word, `done`. `in_progress` was never sent — newest truth wins.
- The local task's `NoOrder` leaves the pile at zero. Silence again.

The sender pass — the toy's `SendPendingAsync`:

```csharp
void Tick(string name, bool wmsIsUp)
{
    var owed = outbox.Claim();
    if (owed.Count == 0)
    {
        Console.WriteLine($"{name}   nothing owed");
        return;
    }

    foreach (var id in owed)
    {
        var word = board.StateOf(id);   // read now: the newest word wins
        if (wmsIsUp)
        {
            delivered.Add($"{id}={word}");
            Console.WriteLine($"{name}   send {id} word={word,-12} -> 200 OK, cleared   owed={outbox.PendingCount}");
        }
        else
        {
            outbox.MarkPending(id);     // a failed send goes back on the pile
            Console.WriteLine($"{name}   send {id} word={word,-12} -> WMS DOWN, put back   owed={outbox.PendingCount}");
        }
    }
}
```

- `Claim` first, then `StateOf` per id — concept 2's read-at-send rule, three lines.
- The down branch is concept 3's whole failure policy in one line: put it back.

Now the pile — the toy's outbox, same shape as the real one:

```csharp
enum OrderSyncOutcome { Advanced, AlreadyThere, NoOrder }

sealed class ToyOutbox
{
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    // The key: only a word that moved is owed to the WMS.
    public void NoteSync(string orderId, OrderSyncOutcome outcome)
    {
        if (outcome == OrderSyncOutcome.Advanced)
            MarkPending(orderId);
    }

    public void MarkPending(string orderId)
    {
        lock (_gate) _pending.Add(orderId);   // adding twice = adding once
    }

    public List<string> Claim()
    {
        lock (_gate)
        {
            var ids = _pending.ToList();
            _pending.Clear();
            return ids;
        }
    }

    public int PendingCount { get { lock (_gate) return _pending.Count; } }
}
```

And the toy board, with lesson 22's sync shortened — the rank refusal is left out, because the toy has no backwards path and that was lesson 22's subject, not this one's:

```csharp
sealed class ToyBoard
{
    private readonly Dictionary<string, (string Task, string Order)> _rows = new(StringComparer.Ordinal);

    public void Submit(string orderId) => _rows[orderId] = ("queued", "received");

    public void MoveTask(string orderId, string taskWord) =>
        _rows[orderId] = (taskWord, _rows[orderId].Order);

    public string StateOf(string orderId) => _rows[orderId].Order;

    public OrderSyncOutcome Sync(string orderId)
    {
        var (task, order) = _rows[orderId];
        var target = task switch
        {
            "queued" or "assigned" => "received",
            "in_progress" => "in_progress",
            "done" => "done",
            "failed" => "failed",
            _ => throw new ArgumentException($"unknown task state {task}"),
        };
        if (target == order) return OrderSyncOutcome.AlreadyThere;
        _rows[orderId] = (task, target);
        return OrderSyncOutcome.Advanced;
    }
}
```

Output, from the run I did:

```text
accept   sync=AlreadyThere owed=0
started  sync=Advanced     owed=1
tick1   send ORD-1 word=in_progress  -> WMS DOWN, put back   owed=1
finished sync=Advanced     owed=1
tick2   send ORD-1 word=done         -> WMS DOWN, put back   owed=1
tick3   send ORD-1 word=done         -> 200 OK, cleared   owed=0
again    sync=AlreadyThere owed=0
tick4   nothing owed
local    sync=NoOrder      owed=0

delivered: ORD-1=done
```

Read the `owed=` column across the outage: `1`, `1`, `1` — the pile holds **one** flag no matter how many words moved, and the WMS's one look at the pile carries the newest word. Read `again`: a repeat sync is still silence.

Now the mutation. Copy the folder, change **one** line — the failure branch drops the flag instead of putting it back:

```csharp
            // MUTATION: the failed send is dropped instead of put back
            Console.WriteLine($"{name}   send {id} word={word,-12} -> WMS DOWN, dropped   owed={outbox.PendingCount}");
```

Output, from that run:

```text
accept   sync=AlreadyThere owed=0
started  sync=Advanced     owed=1
tick1   send ORD-1 word=in_progress  -> WMS DOWN, dropped   owed=0
finished sync=Advanced     owed=1
tick2   send ORD-1 word=done         -> WMS DOWN, dropped   owed=0
tick3   nothing owed
again    sync=AlreadyThere owed=0
tick4   nothing owed
local    sync=NoOrder      owed=0

delivered: (nothing)
```

The WMS went down for two ticks and the fleet forgot, in silence, that it owed two truths. No crash, no error — just a pallet trip the WMS will never hear about. That is why the assignment's mutation (b) must go red.

---

## Say it out loud

Answer these out loud, the way you would in an interview. No notes.

1. A task starts. Trace the whole path out loud — transition, `SyncFromTask`, `NoteSync`, the next tick's send — and name every lock touched along the way, and which data each one protects.
2. The WMS has been down for ten minutes while three orders advanced. What does the pile look like? What does fleet state look like? What does the WMS see the moment it comes back, and why is it one POST per order, not six?
3. Why does the sender read the word at send time instead of storing it when the flag was set? What specific wrongness does that prevent, and which contract sentence backs the choice?
4. `SyncFromTask` returns `AlreadyThere`. What does `NoteSync` do with it, and why is that silence the correct answer — what would the alternative do to the WMS?
5. A send times out. Which exception type arrives (measure it, do not guess), where does the order go, and what does the `when` filter on that catch have to do with Ctrl+C? Then say what M6's done-when would say about dropping the order instead of requeueing it.

---

## The assignment

Build the sending half of the order-status loop, per [record 31](../decisions/31-status-push.md). Language: C#. The MQTT contract does **not** change: `ContractVersion.Current` stays `4`, and `docs/contract/v4.md` is untouched. The REST page does change — exactly as specified below, nowhere else.

### Requirements

1. **`docs/contract/wms-rest.md` — version 1 → 2.** Change `Version: 1` to `Version: 2` and the title's `v1` to `v2`. Add this section, verbatim, before "Changing this contract" (the text is pinned in record 31 §3 — transcribe, do not redesign):

   ```markdown
   ## Order status push (fleet manager → WMS)

   The fleet manager pushes every order state change to the WMS. It POSTs the
   `WmsOrder` body from the table above (same field names, camelCase JSON) to the
   route below, on the WMS's own base URL. The fleet manager's `Wms:BaseUrl`
   setting points at that URL.

   | Route | Body | Content type |
   | --- | --- | --- |
   | `POST /api/order-status` | the `WmsOrder` object | `application/json` |

   | Situation | WMS reply | Fleet manager behaviour |
   | --- | --- | --- |
   | Accepted | any `2xx` | delivered; nothing more to send for that word |
   | Rejected | anything else (4xx, 5xx) | not delivered; the same body is sent again later |
   | Timed out or unreachable | no reply at all | not delivered; the same body is sent again later |

   Notes:

   - At least once: the same body may arrive more than once. A WMS that has
     already applied a word must treat a repeat as a duplicate, not as a new
     event.
   - `state` is the order's current place in the ladder, not a history. A newer
     push for the same `orderId` replaces what came before.
   ```

2. **`src/FleetCore/WmsRoutes.cs`** — `public const string OrderStatus = "/api/order-status";` beside `Orders`. Add one fact to `src/FleetCore.Tests/WmsContractLiteralsTests.cs`, beside `Orders_route`: `Order_status_route_is_pinned` asserting the **literal** string against the constant.

3. **`src/FleetCore/WmsStatusOutbox.cs` (new file)** — the pile, pure `FleetCore` (no HTTP, no web types):
   - `public sealed class WmsStatusOutbox`, a `HashSet<string>` with `StringComparer.Ordinal`, one `object _gate`.
   - `public void NoteSync(string taskId, OrderSyncOutcome outcome)` — marks pending **iff** the outcome is `Advanced`. `AlreadyThere` and `NoOrder` are silent no-ops, not errors.
   - `public void MarkPending(string orderId)` — under the lock; adding twice leaves one flag.
   - `public List<string> Claim()` — under the lock: copy out, clear, return the copy.
   - `public int PendingCount { get; }` — under the lock.

4. **`src/FleetManager/WmsStatusPusher.cs` (new file)** — the sender:
   - `public sealed class WmsStatusPusher` with constructor `(WmsOrderBoard board, WmsStatusOutbox outbox, HttpClient client, TimeSpan interval)`; null guards, and `interval > TimeSpan.Zero` else `ArgumentOutOfRangeException` (the `FleetServeLoop` ctor's shape, `FleetServeLoop.cs:33`).
   - `public async Task SendPendingAsync(CancellationToken cancellationToken)` — one pass: claim; for each id, `TryGet` (missing order → log and `continue`, **no** requeue); serialize with a `static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)`; `PostAsync(WmsRoutes.OrderStatus, …)`; `IsSuccessStatusCode` → delivered log, else `MarkPending` + log; `catch (HttpRequestException)` → `MarkPending` + log; `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` → `MarkPending` + log. One `Console.WriteLine` line per outcome, carrying the order id and the word.
   - `public async Task RunAsync(CancellationToken cancellationToken)` — the `PeriodicTimer` loop, `FleetServeLoop.cs:42-44`'s shape, calling `SendPendingAsync` per tick.
   - No URL, no timeout literal, no interval literal anywhere in this file.

5. **`src/FleetManager/Program.cs`** — inside `RunServeAsync` **only**:
   - Read and validate the three keys exactly as the other keys are read there (`:143-155`'s shape): `Wms:BaseUrl` (missing → throw; not an absolute `Uri` → throw), `Wms:StatusTimeoutMs` (positive), `Wms:StatusIntervalMs` (positive).
   - After the board is built (`:164-165`): the outbox, one `using var` `HttpClient { BaseAddress = wmsUri, Timeout = … }`, the pusher.
   - Start `var pushTask = pusher.RunAsync(stopping.Token);` beside `loopTask` (`:176`); await it after `loopTask` with `catch (OperationCanceledException) when (stopping.IsCancellationRequested)`.
   - `RunMoveAsync` and `RunRecoverAsync` do not change — the acceptance greps below prove it by counting deletions in the file.

6. **`src/FleetManager/appsettings.json`** — add the section:

   ```json
   "Wms": {
     "BaseUrl": "http://127.0.0.1:5090",
     "StatusTimeoutMs": 2000,
     "StatusIntervalMs": 1000
   }
   ```

7. **Do not touch:** `src/FleetCore/FleetTask.cs`, `src/FleetCore/TaskBoard.cs`, `src/FleetCore/WmsOrderStates.cs`, `src/FleetCore/WmsOrderLadder.cs`, `src/FleetCore/WmsOrderBoard.cs`, `src/FleetCore/WmsOrder.cs`, `src/FleetManager/TaskDispatcher.cs`, `src/FleetManager/RunLoop.cs`, `src/FleetManager/FleetServeLoop.cs`, `src/Contracts/`, `docs/contract/v4.md`, and every contract page except `wms-rest.md`.

8. **Tests** (pure, **no** `Integration` trait — the pusher tests need no broker and no sockets: inject a stub `HttpMessageHandler`), in `src/FleetCore.Tests` and `src/FleetManager.Tests`. Keep every existing fact green and add at least these seven:
   - `Order_status_route_is_pinned` (FleetCore) — `Assert.Equal("/api/order-status", WmsRoutes.OrderStatus);`
   - `NoteSync_Advanced_marks_the_order_pending` (FleetCore) — `NoteSync("ORD-1", Advanced)` → `PendingCount` 1 → `Claim` returns exactly `["ORD-1"]` → `PendingCount` 0.
   - `NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing` (FleetCore) — both outcomes → `PendingCount` 0, `Claim` empty.
   - `NoteSync_twice_marks_once` (FleetCore) — `Advanced` twice → `Claim` returns **one** entry.
   - `Push_posts_the_current_word_to_the_status_route` (FleetManager) — submit an order, move its task to `InProgress`, sync (expect `Advanced`), note; stub handler captures the request; `SendPendingAsync`; assert the path is `/api/order-status`, the content type is `application/json`, and the body parsed with `JsonDocument` has `"state"` equal to `"in_progress"` and `"orderId"` equal to the order. The `GetProperty("state")` parse is what kills the PascalCase mutation — a string-contains check would not.
   - `Push_failure_keeps_the_order_pending_for_the_next_pass` (FleetManager) — stub answers `500` on the first request and `200` on the second; first pass: one request, `PendingCount` back to 1; second pass: the second request arrives, `PendingCount` 0.
   - `Push_timeout_keeps_the_order_pending_and_does_not_escape` (FleetManager) — stub handler delays longer than the client's `Timeout` (the E3 shape: e.g. 2000 ms delay, 100 ms timeout); `SendPendingAsync` completes without throwing; `PendingCount` 1.

### Acceptance criteria

```bash
# 1. clean build, no warnings
cd src && dotnet build

# 2. the pure suite is green. Baseline at HEAD is 169;
#    with your seven new facts you land at 176 (or higher), Failed: 0.
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"

# 3. the contract page grew the push section, and only that page moved
grep -q "^Version: 2" docs/contract/wms-rest.md
grep -q "Order status push" docs/contract/wms-rest.md
grep -q "api/order-status" docs/contract/wms-rest.md
test "$(git diff --name-only HEAD -- docs/contract)" = "docs/contract/wms-rest.md"
grep -q "Current = 4" src/Contracts/MessageTypes.cs
git diff --stat HEAD -- src/Contracts docs/contract/v4.md     # must print nothing

# 4. the outbound route is a pinned constant
grep -q "OrderStatus" src/FleetCore/WmsRoutes.cs
grep -rq "Order_status_route_is_pinned" src/FleetCore.Tests --include=*.cs

# 5. the pile lives in FleetCore, pure, with its own lock
test -f src/FleetCore/WmsStatusOutbox.cs
grep -q "NoteSync" src/FleetCore/WmsStatusOutbox.cs
grep -q "lock (_gate)" src/FleetCore/WmsStatusOutbox.cs
! grep -qE "HttpClient|HttpRequest|Results|HttpContext" src/FleetCore/WmsStatusOutbox.cs

# 6. the sender lives in FleetManager; every failure puts the word back; no URL hides in code
test -f src/FleetManager/WmsStatusPusher.cs
grep -q "WmsRoutes.OrderStatus" src/FleetManager/WmsStatusPusher.cs
grep -q "JsonSerializerDefaults.Web" src/FleetManager/WmsStatusPusher.cs
grep -q "MarkPending" src/FleetManager/WmsStatusPusher.cs
grep -q "HttpRequestException" src/FleetManager/WmsStatusPusher.cs
grep -q "OperationCanceledException" src/FleetManager/WmsStatusPusher.cs
! grep -qE "https?://" src/FleetManager/WmsStatusPusher.cs

# 7. serve reads the three keys from config, and only RunServeAsync grew
grep -q "Wms:BaseUrl" src/FleetManager/Program.cs
grep -q "Wms:StatusTimeoutMs" src/FleetManager/Program.cs
grep -q "Wms:StatusIntervalMs" src/FleetManager/Program.cs
grep -q '"Wms"' src/FleetManager/appsettings.json
! grep -q "WmsStatusPusher\|Wms:Status" src/FleetManager/TaskDispatcher.cs \
    src/FleetManager/RunLoop.cs src/FleetManager/FleetServeLoop.cs
git diff --stat HEAD -- src/FleetManager/Program.cs   # reviewer reads: additions only, inside RunServeAsync
test -z "$(git diff HEAD -- src/FleetManager/Program.cs | grep -E '^-[^-]')"   # no line deleted or changed

# 8. the reviewed types did not move
git diff --stat HEAD -- src/FleetCore/FleetTask.cs src/FleetCore/TaskBoard.cs \
  src/FleetCore/WmsOrderStates.cs src/FleetCore/WmsOrderLadder.cs \
  src/FleetCore/WmsOrderBoard.cs src/FleetCore/WmsOrder.cs \
  src/FleetManager/TaskDispatcher.cs src/FleetManager/RunLoop.cs \
  src/FleetManager/FleetServeLoop.cs     # must print nothing

# 9. the seven new facts exist, and the body assertion parses the JSON
grep -rq "NoteSync_Advanced_marks_the_order_pending" src/FleetCore.Tests --include=*.cs
grep -rq "NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing" src/FleetCore.Tests --include=*.cs
grep -rq "NoteSync_twice_marks_once" src/FleetCore.Tests --include=*.cs
grep -rq "Push_posts_the_current_word_to_the_status_route" src/FleetManager.Tests --include=*.cs
grep -rq "Push_failure_keeps_the_order_pending_for_the_next_pass" src/FleetManager.Tests --include=*.cs
grep -rq "Push_timeout_keeps_the_order_pending_and_does_not_escape" src/FleetManager.Tests --include=*.cs
grep -rq 'GetProperty("state")' src/FleetManager.Tests --include=*.cs
```

`HEAD` here means the commit your assignment builds on (the lesson-23 material commit); the reviewer re-bases these comparisons exactly as review 22 did.

**What must not change:** the 169 tests that pass today must all still pass; `ContractVersion.Current` stays `4`; every contract page except `wms-rest.md` is untouched; the reviewed types in grep 8 are untouched; `WmsOrderStates` keeps its five constants; `RunMoveAsync` and `RunRecoverAsync` lose no lines (the deletion-count grep).

### Mutation check

Run each in a scratch copy, never in the workspace. After each mutation, run the full pure suite and note which test went red.

| # | Mutation | Expected | Verdict |
|---|---|---|---|
| a | `NoteSync`: drop the `== Advanced` check — mark on every outcome | `NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing` fails: `PendingCount` is 1, expected 0 | **must go red** |
| b | the sender's failure paths: do **not** call `MarkPending` (all three: non-2xx, refused, timeout) | `Push_failure_keeps_the_order_pending_for_the_next_pass` fails: the second pass finds nothing owed, the second request never arrives | **must go red** |
| c | the sender: remove the `OperationCanceledException` catch (let the timeout escape) | `Push_timeout_…` fails: `SendPendingAsync` throws instead of returning | **must go red** |
| d | the sender: serialize with default `JsonSerializerOptions` (PascalCase), or POST to `WmsRoutes.Orders` | `Push_posts_the_current_word_…` fails: `GetProperty("state")` throws — the key is `"State"` — or the path assert fails | **must go red** |
| e | `Claim`: return the copy **without** clearing | `NoteSync_Advanced_marks_the_order_pending` fails: the second `Claim` returns the same id again; failure test would re-send forever | **must go red** |
| f | `Program.cs`: ignore `Wms:StatusTimeoutMs` — hard-code `TimeSpan.FromSeconds(5)` | the unit tests stay green: they build their own `HttpClient`; only the acceptance grep `grep -q "Wms:StatusTimeoutMs" src/FleetManager/Program.cs` kills it | **may survive** — the grep is the check, on purpose |
| g | the pile: delete `lock (_gate)` from all three methods | the committed suite may stay green — the tests are single-threaded; the stretch probe below is what shows the race | **may survive** — a survey, like lesson 22's (e) |

If a mutation that must go red survives, the test is decoration; fix the test. For (f) and (g), write down in your review notes what each would do to a real WMS — a timeout that stops bounding anything, and a pile that eats flags — then leave the proofs to the grep (f) and the probe (g).

### Stretch goal

Prove the pile's lock does real work, the way lesson 22 proved its board's lock. Write a scratch probe in `/tmp/opencode` (never in this repo): one `WmsStatusOutbox` (or its exact shape), 8 threads each marking 1000 **distinct** ids, and one claimer draining until 8000 ids have been claimed. Assert the claimer saw all 8000. Run it three times with the lock, then delete the locks and run it three times more. Keep the output in your review notes.

What I measured with that shape, three runs each way, on 2026-10-20 (`/tmp/opencode/l23lock`, deleted afterwards):

```text
with the lock — all 3 rounds:
round 1: claimed=8000 expected=8000 PASS
round 2: claimed=8000 expected=8000 PASS
round 3: claimed=8000 expected=8000 PASS

locks removed — all 3 runs died before printing PASS. First lines of each:
run 1: Unhandled exception. System.IndexOutOfRangeException: Index was outside the bounds of the array.
run 2: Unhandled exception. System.IndexOutOfRangeException: Index was outside the bounds of the array.
run 3: Unhandled exception. System.InvalidOperationException: Operations that change non-concurrent
       collections must have exclusive access. A concurrent update was performed on this
       collection and corrupted its state. The collection's state is no longer correct.
(all three exited RC 134; which exception you see first varies by run)
```

**Not in this assignment:** cancel, running tasks inside `serve`, the mock-WMS receiver, persistence, load/unload, the dashboard, the deferred door-hardening slice, any edit to `src/Contracts`, `docs/contract/v4.md`, or `WmsOrderLadder`, and any production call to `SyncFromTask` or `NoteSync` — those two lines belong to the execution slice.

---

## What comes next

- **The execution slice**: wires `TaskDispatcher` into `serve`, and adds the two transition lines — `SyncFromTask(taskId)`, then `NoteSync(taskId, outcome)` (record 30 §4's rule, extended by record 31 §5). M6's *"to completed pallets"* waits on this; the words start flowing the moment it lands. Persistence (record 29 §7) rides with it, and the pile's next record — re-mark on restore — goes with persistence.
- **The mock-WMS receiver**: implements the version-2 page this lesson wrote — one POST route, the documented body, `2xx` means delivered, repeats are duplicates. This is what turns the sender's measured-but-unheard POSTs into real conversations, and closes M6's *"and back."*
- **Cancel**: owns the reverse direction and the `cancelled` word — and inherits review 22's issue 4, because it must open `WmsOrderLadder.cs` anyway: `Rank` has no `cancelled` arm, and `ForTask`'s constants-vs-`Rank`'s-literals alignment comes free with the visit.
- **Door hardening**: still owed before M6 is called done, still deferred by you to its own later slice — this lesson does not touch it.
- **M7**: `PendingCount` is already a thread-safe number the dashboard can show — "words the WMS is owed" — with no extra work.
