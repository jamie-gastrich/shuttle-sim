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
- **Watch the count.** `public int PendingCount { get; }` is a stored number that never changes. It will always read `0`, and every pile fact will fail on its first assert for no visible reason. Write it as a property **with a body** that takes the lock and returns `_pending.Count`. (The lesson's shape shows the body form; the one-liner is the trap.)

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

**Three small traps in the test wiring:**

- The test's `HttpClient` needs a `BaseAddress`. The route constant is a path, not a full URL. Measured: a client with no `BaseAddress` throws `InvalidOperationException: An invalid request URI was provided. Either the request URI must be an absolute URI or BaseAddress must be set.` Any absolute URL works — no socket opens, because the stub handler answers instead.
- The captured content type is `application/json; charset=utf-8` (measured). `Assert.Equal("application/json", ct)` fails. Use `Assert.StartsWith("application/json", ct)` or `Assert.Contains("application/json", ct)`.
- A task reaches `InProgress` in two steps, not one: `Assign` first (it starts `Queued`), then `Start`. `Start` on a `Queued` task throws `TaskRejectedException`.

**The two catches and why the filter is not optional:** the client timeout throws `TaskCanceledException` (measured, with a `TimeoutException` inner); our shutdown cancels the token and `PeriodicTimer` throws the same family. `when (!cancellationToken.IsCancellationRequested)` sorts them: timeout → requeue; shutdown → let it fly to Program's `catch`, which already exists for `loopTask` — copy the shape of that block for `pushTask`.

**Config validation:** copy `Program.cs:143-155`'s pattern for each key. The URL is the odd one: `Uri.TryCreate(value, UriKind.Absolute, out var uri)` must succeed, else throw — startup, not first send.

## Tier 3 — near-solution skeleton

This is the shape, with the interesting lines left for you to write.

Every `TODO` below is yours to write. The lines that carry this lesson's ideas — the `iff` in `NoteSync`, the copy-clear-return in `Claim`, what goes in each catch, the body and the post, the loop shape — are deliberately left as commented steps. You are not pasting this file. You are using it as a map.

The measurements quoted below were taken today in a scratch program under `/tmp/opencode` (deleted afterwards). Nothing was left running.

### 3.0 The files, all seven

| What | Where |
| --- | --- |
| The route constant | `src/FleetCore/WmsRoutes.cs` (one line, beside `Orders`) |
| The pile | `src/FleetCore/WmsStatusOutbox.cs` (new file) |
| The sender | `src/FleetManager/WmsStatusPusher.cs` (new file) |
| The wiring | `src/FleetManager/Program.cs` (inside `RunServeAsync` only) |
| The config | `src/FleetManager/appsettings.json` (the `Wms` section) |
| The contract page | `docs/contract/wms-rest.md` (edited in place) |
| The tests | three files, listed in 3.5 |

### 3.1 The pile — `src/FleetCore/WmsStatusOutbox.cs`

No usings needed. `ImplicitUsings` already brings in `System.Collections.Generic`.

```csharp
// src/FleetCore/WmsStatusOutbox.cs
namespace FleetCore;

public sealed class WmsStatusOutbox
{
    // --- the class shape: one set, one lock. Both are given. ---
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    // --- 1. the send key ---
    public void NoteSync(string taskId, OrderSyncOutcome outcome)
    {
        // TODO: one line. Mark pending if and only if the outcome means the word moved.
        //       Which member of OrderSyncOutcome (src/FleetCore/WmsOrder.cs:14) means that?
        //       The other two outcomes must do nothing at all — no mark, no throw.
    }

    // --- 2. put one flag on the pile ---
    public void MarkPending(string orderId)
    {
        lock (_gate)
        {
            // TODO: one line. Add the id to _pending.
            //       A HashSet makes adding the same id twice leave one flag.
        }
    }

    // --- 3. take the pile, and leave it empty ---
    public List<string> Claim()
    {
        lock (_gate)
        {
            // TODO: three lines, in this order:
            //   1. copy the ids out of _pending into a new List<string>
            //   2. clear _pending
            //   3. return the copy
            // Skip line 2 and the pile never empties. That is mutation (e).
        }
    }

    // --- 4. how many words are owed ---
    // A `{ get; }` auto-property is a stored number that never changes: it always
    // reads 0. Write the property with a body that takes the lock and counts.
    public int PendingCount
    {
        get
        {
            // TODO: under the lock, return _pending.Count
        }
    }
}
```

**How to finish it, in order:** write `NoteSync` first (it is one `if`), then `MarkPending` (one line), then `PendingCount` (one line), then `Claim` (three lines). `Claim` is last because it is the only one with an order to remember.

**One-line proof the lock is real:** the lesson measured 8 threads marking 1000 ids each against one claimer. With the lock, 3 rounds of 3 claimed exactly 8000. Without it, 3 runs of 3 crashed (`IndexOutOfRangeException` twice, `InvalidOperationException: … corrupted its state` once).

### 3.2 The sender — `src/FleetManager/WmsStatusPusher.cs`

Three usings at the top. `System.Net.Http` is already implicit; these three are not.

```csharp
// src/FleetManager/WmsStatusPusher.cs
using System.Text;          // Encoding.UTF8
using System.Text.Json;     // JsonSerializer, JsonSerializerOptions
using FleetCore;            // WmsOrderBoard, WmsStatusOutbox, WmsRoutes

namespace FleetManager;

public sealed class WmsStatusPusher
{
    // --- the four fields ---
    // TODO: declare these four, all readonly, in this order:
    //   private readonly WmsOrderBoard _board;
    //   private readonly WmsStatusOutbox _outbox;
    //   private readonly HttpClient _client;
    //   private readonly TimeSpan _interval;

    // The one serializer for the whole class. Web defaults give camelCase.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public WmsStatusPusher(WmsOrderBoard board, WmsStatusOutbox outbox, HttpClient client, TimeSpan interval)
    {
        // TODO: three null guards and one range guard. House shape: FleetServeLoop.cs:24-34.
        //   _board  = board  ?? throw new ArgumentNullException(nameof(board));
        //   _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        //   _client = client ?? throw new ArgumentNullException(nameof(client));
        //   then the interval: if it is not greater than TimeSpan.Zero, throw
        //   ArgumentOutOfRangeException; otherwise STORE it in _interval.
        //
        // TRAP: `interval = interval > TimeSpan.Zero ? interval : throw ...` throws the
        // value away. The parameter is still not in the field. Assign _interval.
    }

    public async Task SendPendingAsync(CancellationToken cancellationToken)
    {
        // TODO: one claim, outside the per-id work:
        //   var ids = _outbox.Claim();
        //   foreach (var orderId in ids)
        //   {
        //       if (!_board.TryGet(orderId, out var order))
        //       {
        //           // TODO: one log line (Console.WriteLine) saying the id has no order.
        //           //       No MarkPending here. A ghost id would loop forever.
        //           continue;
        //       }
        //
        //       try
        //       {
        //           // TODO: three lines, in this order:
        //           //   1. var body = JsonSerializer.Serialize(order, Json);
        //           //   2. using var content = new StringContent(body, Encoding.UTF8, "application/json");
        //           //   3. using var response = await _client.PostAsync(WmsRoutes.OrderStatus, content, cancellationToken);
        //           // TODO: then branch on response.IsSuccessStatusCode:
        //           //   true  -> one log line: delivered, carrying the order id and the word
        //           //   false -> _outbox.MarkPending(order.OrderId), then one log line with the status code
        //       }
        //       catch (HttpRequestException)
        //       {
        //           // TODO: two things: put the word back on the pile
        //           //       (_outbox.MarkPending(order.OrderId)) and write one log line.
        //       }
        //       catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        //       {
        //           // TODO: the same two things: put the word back, and one log line.
        //       }
        //   }
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        // TODO: three steps, in this order. House shape: FleetServeLoop.cs:42-44.
        //   1. a PeriodicTimer over _interval, disposed when the method ends
        //   2. a while loop that waits for the next tick, passing the token
        //   3. inside the loop, one await SendPendingAsync(cancellationToken)
        //
        // Do NOT wrap this in a try/catch. On Ctrl+C the wait throws, and that
        // exception must escape to Program's catch.
    }
}
```

**How to finish it, in order:** the four fields, then the ctor, then `RunAsync` (three lines), then `SendPendingAsync` (the big one). Do the ctor before anything else, because everything else uses `_interval` and `_client`.

**What each catch means, in one line each:**

- `HttpRequestException` — the WMS is not there. The connection was refused.
- `OperationCanceledException` with the filter — the WMS is there but slow. The client's own timeout fired.
- No filter — our shutdown cancelled the token. Let it fly.

**Two facts the lesson measured, so you trust the branches:**

- A `500` reply does **not** throw. Measured: a listener answering `500` came back in 58 ms with no exception. So `PostAsync` hands you the response and you check `IsSuccessStatusCode` yourself.
- A refused connection fails fast and typed: `HttpRequestException` after 5 ms, inner `SocketException`.

### 3.3 The wiring — `src/FleetManager/Program.cs`

Three spots, all inside `RunServeAsync`. Nothing else in that file changes.

**Spot 1 — the three keys, with the other keys.** House shape: `Program.cs:143-155`.

```csharp
	// Spot 1 — beside the other keys
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

**The Uri conversion line.** `HttpClient.BaseAddress` wants a `Uri`, and config gives you a string.

- If you used `Uri.TryCreate` above, `wmsUri` is already a `Uri`. Nothing to convert.
- If you validated some other way (for example `Uri.IsWellFormedUriString`), you still hold a string. Convert it in one line, after the check:

```csharp
	// the conversion line, when you validated with a string check
	var wmsUri = new Uri(baseUrl);
```

**Spot 2 — the three objects, right after the board is built.** The anchor is the line `var board = new WmsOrderBoard(taskBoard);`.

```csharp
	// Spot 2 — after the board
	var outbox = new WmsStatusOutbox();
	using var wmsClient = new HttpClient
	{
		BaseAddress = wmsUri,
		Timeout = TimeSpan.FromMilliseconds(wmsTimeoutMs),
	};
	var pusher = new WmsStatusPusher(board, outbox, wmsClient, TimeSpan.FromMilliseconds(wmsIntervalMs));
```

One client for the process. Never one per send.

**Spot 3 — start it, then await it.**

```csharp
	// Spot 3a — beside the line that starts the loop
	var loopTask = loop.RunAsync(stopping.Token);
	var pushTask = pusher.RunAsync(stopping.Token);

	// Spot 3b — await it, mirroring the loopTask block line for line.
	// Put it directly after the loopTask block, or after the webTask block. Either place works.
	try
	{
		await pushTask;
	}
	catch (OperationCanceledException) when (stopping.IsCancellationRequested)
	{
		// Expected: the pushTask stopped because we cancelled it.
	}
```

The mirror is exact: same exception type, same `when` filter, same comment shape, same try/catch shape. Copy the `loopTask` block and change two words.

**Why the await matters.** A task you start but never await is a task nobody watches. The cancel exception inside it ends up unobserved instead of handled, and Program does not wait for the pusher to stop before the door closes.

### 3.4 The contract page — `docs/contract/wms-rest.md`

Edit the file where it lives. Do not make a second page.

Three edits, all mechanical:

1. Line 1, the title: `# WMS REST contract v1` → `# WMS REST contract v2`.
2. Line 3, the version: `Version: 1` → `Version: 2`.
3. The new section goes in exactly one place: immediately above the heading `## Changing this contract`, which is the last heading on the page. Keep the `---` separator between the new section and that heading, like every other section on the page.

**The text is pinned.** Copy it from record 31 §3 — `docs/decisions/31-status-push.md`, the block under "Decision B". Do not retype it from memory and do not improve it. The assignment's requirement 1 quotes the same text, so either source works; both must match each other.

**What must not move:** `ContractVersion.Current` stays `4`, `docs/contract/v4.md` is not touched, and no other contract page changes. The acceptance grep `test "$(git diff --name-only HEAD -- docs/contract)" = "docs/contract/wms-rest.md"` checks exactly that.

### 3.5 The seven tests

**Where each one goes:**

| Test name | Project | File |
| --- | --- | --- |
| `Order_status_route_is_pinned` | FleetCore.Tests | `src/FleetCore.Tests/WmsContractLiteralsTests.cs` (existing file, beside `Orders_route`) |
| `NoteSync_Advanced_marks_the_order_pending` | FleetCore.Tests | `src/FleetCore.Tests/WmsStatusOutboxTests.cs` (new file) |
| `NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing` | FleetCore.Tests | same new file |
| `NoteSync_twice_marks_once` | FleetCore.Tests | same new file |
| `Push_posts_the_current_word_to_the_status_route` | FleetManager.Tests | `src/FleetManager.Tests/WmsStatusPusherTests.cs` (new file) |
| `Push_failure_keeps_the_order_pending_for_the_next_pass` | FleetManager.Tests | same new file |
| `Push_timeout_keeps_the_order_pending_and_does_not_escape` | FleetManager.Tests | same new file |

None of them carries an `Integration` trait. They are pure.

#### The stub network (read this once)

`HttpClient` sends every request through an `HttpMessageHandler`. If you build your own handler and hand it to `new HttpClient(handler)`, the client never opens a socket — your handler decides what comes back. So: subclass `HttpMessageHandler`, override `SendAsync`, record what was asked for, and return the reply you want. That is the whole trick, and it is why these three facts need no broker, no listener, and no `Integration` trait.

The skeleton for it (one class, in the test file):

```csharp
// src/FleetManager.Tests/WmsStatusPusherTests.cs — the stub network
using System.Net;
using System.Text.Json;
using FleetCore;

internal sealed class StubHandler : HttpMessageHandler
{
    // --- what the stub was asked for ---
    public List<string> Paths { get; } = new();
    public List<string> ContentTypes { get; } = new();
    public List<string> Bodies { get; } = new();

    // --- how the stub answers ---
    private readonly Queue<HttpStatusCode> _replies = new();
    private readonly TimeSpan _delay;

    public StubHandler(params HttpStatusCode[] replies)
    {
        foreach (var reply in replies) _replies.Enqueue(reply);
    }

    public StubHandler(TimeSpan delay, params HttpStatusCode[] replies)
    {
        _delay = delay;
        foreach (var reply in replies) _replies.Enqueue(reply);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // TODO: record the request: the path, the content type, the body string.
        //       Path:        request.RequestUri!.AbsolutePath
        //       ContentType: request.Content?.Headers.ContentType?.ToString()
        //       Body:        await request.Content!.ReadAsStringAsync(cancellationToken)
        // TODO: if _delay > TimeSpan.Zero, await Task.Delay(_delay, cancellationToken)
        //       — that is the slow WMS the timeout test needs.
        // TODO: return a response: dequeue one reply, or OK when the queue is empty.
        //       A 500 must NOT throw. The sender checks IsSuccessStatusCode itself.
    }
}
```

Measured today with this shape (scratch, deleted afterwards):

```text
seen method=POST path=/api/order-status ct=application/json; charset=utf-8
state=in_progress orderId=ORD-1
first reply=500 success=False requests=1
second reply=200 requests=2
timeout case: TaskCanceledException after 00:00:00.1057471 ms; inner=TimeoutException
  is OperationCanceledException: True
```

#### Test 1 — `Order_status_route_is_pinned`

**File:** `src/FleetCore.Tests/WmsContractLiteralsTests.cs`, beside the existing `Orders_route` fact.

- **Arrange:** nothing. It is a constant.
- **Act:** read `WmsRoutes.OrderStatus`.
- **Assert:** `Assert.Equal("/api/order-status", WmsRoutes.OrderStatus);`
- **Trap:** assert the **literal** string. `Assert.Equal(WmsRoutes.OrderStatus, WmsRoutes.OrderStatus)` always passes and proves nothing.

#### Tests 2, 3, 4 — the three pile facts

**File:** `src/FleetCore.Tests/WmsStatusOutboxTests.cs` (new). `namespace FleetCore.Tests;` and `public class WmsStatusOutboxTests`.

**Test 2 — `NoteSync_Advanced_marks_the_order_pending`**

- **Arrange:** `var outbox = new WmsStatusOutbox();`
- **Act:** `outbox.NoteSync("ORD-1", OrderSyncOutcome.Advanced);`
- **Assert:** `Assert.Equal(1, outbox.PendingCount);`
- **Act again:** `var claimed = outbox.Claim();`
- **Assert:** `Assert.Equal(new[] { "ORD-1" }, claimed);` — exactly one entry, the literal id.
- **Assert:** `Assert.Equal(0, outbox.PendingCount);` — `Claim` leaves the pile empty. This last assert is mutation (e).
- **Trap:** the first assert needs a real count. A `{ get; }` auto-property reads `0` forever, so this test dies on line one.

**Test 3 — `NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing`**

- **Arrange:** a fresh outbox.
- **Act:** `outbox.NoteSync("ORD-1", OrderSyncOutcome.AlreadyThere);` and `outbox.NoteSync("ORD-2", OrderSyncOutcome.NoOrder);`
- **Assert:** `Assert.Equal(0, outbox.PendingCount);`
- **Assert:** `Assert.Empty(outbox.Claim());`
- **Trap:** both outcomes must be silent no-ops — no mark, and no throw. This is the fact mutation (a) must kill.

**Test 4 — `NoteSync_twice_marks_once`**

- **Arrange:** a fresh outbox.
- **Act:** `outbox.NoteSync("ORD-1", OrderSyncOutcome.Advanced);` twice.
- **Assert:** `var claimed = outbox.Claim();` then `Assert.Single(claimed);` and `Assert.Equal("ORD-1", claimed[0]);`
- **Trap:** `Assert.Single` is the shape to reach for — it fails on two entries as loudly as on zero. A pile built on a `List` instead of a `HashSet` gives you two.

#### The world the three push facts arrange

All three push tests set up the same world. Do it once, in a helper method in the test file.

1. `var tasks = new TaskBoard();` and `var board = new WmsOrderBoard(tasks);`
2. Submit the order:
   `board.Submit(new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PLT-001", "A01-L01"), now);`
   The order is now stored with the word `received`, and a `FleetTask` was created in the `TaskBoard`, in the state `queued`.
3. Move that task: `var task = tasks.Get("ORD-1");` then `task.Assign("SH-01", now);` then `task.Start(now);`
   Two steps, not one: `Start` refuses a task that was never assigned. The task is now `InProgress`.
4. `var outcome = board.SyncFromTask("ORD-1");` — this returns `Advanced`, because the ladder maps `InProgress` to `in_progress`, which is one rung above the stored `received`.
5. `var outbox = new WmsStatusOutbox();` then `outbox.NoteSync("ORD-1", outcome);` — the pile now owes one word.
6. Build the client and the pusher:
   `var client = new HttpClient(stub) { BaseAddress = new Uri("http://wms.test"), Timeout = … };`
   `var pusher = new WmsStatusPusher(board, outbox, client, TimeSpan.FromSeconds(1));`

**The clock:** use a fixed one, like `var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);` and step it forward with `now.AddSeconds(1)`. `FleetTask` throws if the clock goes backwards.

#### Test 5 — `Push_posts_the_current_word_to_the_status_route`

**File:** `src/FleetManager.Tests/WmsStatusPusherTests.cs` (new).

- **Arrange:** the world above, with a stub that answers `200`.
- **Act:** `await pusher.SendPendingAsync(CancellationToken.None);`
- **Assert:**
  - one request reached the stub;
  - the path is the literal `/api/order-status`;
  - the content type starts with `application/json` — measured: `application/json; charset=utf-8`, so use `Assert.StartsWith("application/json", ct)` or `Assert.Contains("application/json", ct)`;
  - the body parses: `using var doc = JsonDocument.Parse(body);`
  - `Assert.Equal("in_progress", doc.RootElement.GetProperty("state").GetString());`
  - `Assert.Equal("ORD-1", doc.RootElement.GetProperty("orderId").GetString());`
  - `Assert.Equal(0, outbox.PendingCount);` — delivered, so the pile is empty.
- **Trap:** the parse is what kills the PascalCase mutation, and the reason is the *key*, not the value. Measured today with the real record shape:

  ```text
  web    : {"orderId":"ORD-1","kind":"store","palletId":"PLT-001","laneId":"A01-L01","state":"in_progress","receivedAt":"2026-01-01T00:00:00Z"}
  default: {"OrderId":"ORD-1","Kind":"store","PalletId":"PLT-001","LaneId":"A01-L01","State":"in_progress","ReceivedAt":"2026-01-01T00:00:00Z"}
  web: GetProperty("state") = in_progress
  default: GetProperty("state") threw KeyNotFoundException
  ```

  Both bodies contain the text `in_progress`. Only the key changes. So `Assert.Contains("in_progress", body)` passes on the broken body, and the mutation survives. Parse the JSON and ask for the key by name.

#### Test 6 — `Push_failure_keeps_the_order_pending_for_the_next_pass`

- **Arrange:** the world above, with **one** stub whose queue is `500` then `200`.
- **Act 1:** `await pusher.SendPendingAsync(CancellationToken.None);`
- **Assert 1:** one request reached the stub, and `Assert.Equal(1, outbox.PendingCount);` — the rejected word went back on the pile.
- **Act 2:** `await pusher.SendPendingAsync(CancellationToken.None);`
- **Assert 2:** two requests reached the stub now, and `Assert.Equal(0, outbox.PendingCount);` — the second pass delivered it.
- **Trap:** one stub for both passes. Two stubs, one per pass, and the second pass would find nothing owed and send nothing. Also: a `500` does not throw, so the sender has to look at `IsSuccessStatusCode` itself — if it does not, the pile stays empty and the second pass sends nothing. The second pass sends the same body, because the board still says `in_progress`: nothing moved the task in between.

#### Test 7 — `Push_timeout_keeps_the_order_pending_and_does_not_escape`

- **Arrange:** the world above, with a stub whose delay is **2000 ms**, and a client whose `Timeout` is **100 ms**. The delay must be much bigger than the timeout, so the timeout is what fires.
- **Act:** `await pusher.SendPendingAsync(CancellationToken.None);` — it must return, not throw.
- **Assert:** `Assert.Equal(1, outbox.PendingCount);` — the timed-out word went back on the pile.
- **Trap:** pass `CancellationToken.None`, never a cancelled token. A cancelled token makes `when (!cancellationToken.IsCancellationRequested)` false, so the catch does not fire and the exception escapes — correct for shutdown, wrong here. Measured today: a 2000 ms handler delay against a 100 ms client timeout throws `TaskCanceledException` after about 106 ms, inner `TimeoutException`, and it *is* an `OperationCanceledException`. The pusher does not own the timeout — the client's `Timeout` does, so the test sets it on its own client.

### 3.6 Run it

```bash
cd src
dotnet build
dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
```

The baseline is 169 pure facts. With your seven new facts you land at 176 (or higher), `Failed: 0`.

Then the acceptance greps from the lesson, one by one. The ones that catch the most:

```bash
grep -q "OrderStatus" src/FleetCore/WmsRoutes.cs
grep -q "lock (_gate)" src/FleetCore/WmsStatusOutbox.cs
grep -q "JsonSerializerDefaults.Web" src/FleetManager/WmsStatusPusher.cs
grep -q "MarkPending" src/FleetManager/WmsStatusPusher.cs
grep -rq 'GetProperty("state")' src/FleetManager.Tests --include=*.cs
```

For the contract, edit the page where it lives and stop. That page does not get a second opinion from you — that is what the decision record was for.
