# Decision Record 31: Order status push — a word is owed only when the word moved

**Date:** 2026-10-20
**Milestone:** M6 (WMS integration) — lesson 23: pushing order states to the WMS.
**Status:** Accepted. Append-only. Builds on [record 30](30-order-state-ladder.md) §3 (the `Advanced` moment, hold-the-board-not-the-order), §4 (the push trigger rule, "no reason to batch"), §5 (the no-clock condition — deliberately **not** triggered here, see Decision D); [record 29](29-wms-order-to-task-mapping.md) §3 (task id = order id), §5 (the pair lives in memory, lost together), §7 (persistence timing); [record 28](28-wms-rest-front-door.md) §5 (`docs/contract/wms-rest.md` is the REST page, and its own changing rule), §7 (configuration vs constants), and its scope limit "no status push to the WMS"; [record 02](02-message-transport.md) (REST at the business edge, MQTT to the shuttles); [record 12](12-task-blank-inputs-and-exception-types.md) (the two error categories); [record 14](14-db-test-gate-and-config.md) (the `Integration` trait gate — this slice adds none).
**Implements:** the second half of M6's done-when — *"orders flow from the WMS to completed pallets **and back**, and a WMS outage or a bad order does not corrupt fleet state."* Lesson 22 built the ladder and the `Advanced` outcome; this record decides how the fleet manager tells the WMS, what it owes, what it stores, who sends, and what happens when the WMS is slow, refuses, or is gone. Also settles the two review-22 deferrals' design context: the mutation (e)/(f) real-WMS stories are supplied in [the answers file](../lessons/22-order-state-ladder-answers.md) and hang off the decisions below.
**Wire contract:** **MQTT unchanged.** `ContractVersion.Current` stays `4`; `docs/contract/v4.md` and `src/Contracts/` do not move. Measured today: `MessageTypes.cs` contains no WMS message type at all (six constants, all shuttle-facing), and v4's only "status" is `shuttleStatus` — the fleet-manager→WMS hop was never an MQTT hop. **The REST page changes:** `docs/contract/wms-rest.md` goes **version 1 → 2** with a new "Order status push" section, exactly per the page's own rule ("Changing this contract" at `:90-93`: a decision record plus a version bump to this page, and it never bumps the MQTT contract). The full section text is pinned in Decision B below, so the bump is a transcription, not a design task.
**Replaced by:** later records that add cancel, that build the mock-WMS receiver, that persist orders, or that give up on retrying a permanently-rejected word. Append-only.

---

## 1. What we are deciding

After lesson 22 the order's `State` tells the truth — inside the fleet manager's memory. The WMS hears nothing. Its only view is the `201` body at intake (which says `received`) and whatever a `GET /api/orders/{orderId}` returns if it asks. M6 says the WMS **receives status updates**. This record decides:

- **A.** Direction and transport: who pushes what, over what, and why not the alternatives.
- **B.** The contract: the exact new section on `wms-rest.md`, and why the MQTT contract does not move.
- **C.** The route, and the JSON body.
- **D.** The send key: when exactly a word is owed. (The core of the slice.)
- **E.** What the outbound pile stores, and what the sender reads.
- **F.** Who sends, and when.
- **G.** The failure policy: slow, refusing, dead — and what "retry" means here.
- **H.** Configuration: the three keys, their defaults, and why each must be config.
- **I.** Logging, and what this slice deliberately does not do.

Everything decision-logic-shaped is `FleetCore` and pure: the pile has a lock and a `HashSet`, nothing else. The HTTP lives in one `FleetManager` class, behind one loop, with the timeout on the client where measured behaviour says it belongs.

---

## 2. Decision A: the fleet manager POSTs each state change to the WMS over HTTP

Options:

- **A: the fleet manager POSTs the order's body to a route on the WMS, once per state change.** (chosen)
- **B: the WMS polls — `GET /api/orders/{orderId}` on its own schedule.**
- **C: the fleet manager publishes order states to an MQTT topic the WMS subscribes to.**

**Decision A.**

1. **The spec says REST and says "receives."** `spec/shuttle-spec.md` describes the mock WMS ⇄ fleet manager hop as REST (record 02's placement of REST at the business edge), and M6's wording is that the WMS *receives status updates* — the direction is push.
2. **Only push can key on the `Advanced` moment.** Lesson 22 stored the state precisely so a caller could be told "the word just moved." A sender can act on that signal directly. A poller (option B) would have to read, compare, and guess — a trigger wearing a disguise, exactly what record 30 §3 rejected for the board's internal trigger.
3. **Option C crosses a boundary the contract keeps clean.** The WMS is outside the shuttle fleet's message tree. `docs/contract/v4.md` documents six message types, all shuttle↔manager (measured today: `MessageTypes.cs` has no WMS type; v4's only status is `shuttleStatus`). Teaching the WMS to subscribe to fleet topics would move the MQTT contract, add a subscriber lifecycle to the broker story, and hand an external system a firehose it did not ask for.

Rejected B, stated fairly: it is trivially safe against a broken *sender* (there is none), and it needs no outbound port. It pays with a poll loop that cannot use `Advanced`, load that the WMS drives instead of the fleet, and a "status" that is only as fresh as the last poll. It also contradicts M6's "receives."

**What would change this:** a real WMS that refuses inbound connections entirely and only pulls. Then the poll route exists next to `GET /api/orders/{orderId}` unchanged, and the fleet keeps its pile for a pull-completion handshake. Not in this spec.

---

## 3. Decision B: the contract grows one section on the REST page; the MQTT contract does not move

`docs/contract/wms-rest.md` goes from `Version: 1` to `Version: 2` (measured today: it is at `Version: 1`, line 3, with zero occurrences of `order-status`). The new section, pinned here verbatim — the owner transcribes it, nothing more:

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

The page's title line (`# WMS REST contract v1`) follows the version, and the "Changing this contract" section stays as-is.

**Why the body is the full `WmsOrder` and not a slim `{orderId, state}`:** one field table, one type, one serializer. The WMS already knows the full body from the door (the `201` at intake carries it, documented at `wms-rest.md:27-45`). A second, slimmer shape would be a second thing to document, a second thing to version, and a second thing for the mock WMS to parse — for a saving of a few hundred bytes on a wire that carries one message per state change.

**Why the MQTT contract stays `v4`:** the hop was never MQTT. Bumping `ContractVersion.Current` for a REST change would break every shuttle's envelope check for no reason; the REST page's own rule (measured at `wms-rest.md:90-93`) says exactly this: a page bump, and never an MQTT bump.

**What would change this:** a WMS that wants an acknowledgement per push with its own vocabulary, or a change-time field on the order (record 30 §5's condition — the field and the `now` parameter arrive together, in the same record and the same page bump; this slice adds neither).

---

## 4. Decision C: the route is a pinned constant; the body is the documented order, camelCase

The route lives beside the door's routes, as a constant, because it is contract vocabulary — the same rule record 28 applied to `WmsOrders`'s `/api/orders`:

```csharp
// src/FleetCore/WmsRoutes.cs — the shape, decided here
public const string OrderStatus = "/api/order-status";
```

It gets a literal pin in `WmsContractLiteralsTests`, next to the existing `Orders_route` fact — a route a client depends on is pinned the same way a wire word is.

**Why `/api/order-status` and not `/api/orders/status`:** `/api/orders/{orderId}` is already a live route (`WmsRoutes.Order` builds `/api/orders/{orderId}`, measured at `WmsRoutes.cs:8`), so `/api/orders/status` would parse as an order whose id is the string `status` on the GET route. Two meanings for one path is how outages start. The status push is a different resource kind — an update stream, not a collection — so it gets its own path.

**Why camelCase JSON, and how:** the documented body is camelCase (measured: the door's minimal API serves `{"orderId": …}`, and `wms-rest.md`'s example is camelCase). The sender serializes with `JsonSerializerOptions(JsonSerializerDefaults.Web)`. Measured today in scratch: the default serializer produces PascalCase (`{"OrderId":…}`) and the Web defaults produce the documented shape (`{"orderId":…,"state":…}`) — so the options object is not cosmetic; without it the push is a body the contract never promised.

**What would change this:** the WMS asking for a different media type or a versioned path (`/api/v2/order-status`) — a page bump and a constant edit, nothing else.

---

## 5. Decision D: a word is owed only when the word moved — `NoteSync` keys on `Advanced`

This is the rule the whole slice hangs on.

```csharp
// the key, decided here (FleetCore, pure)
public void NoteSync(string taskId, OrderSyncOutcome outcome)
{
    if (outcome == OrderSyncOutcome.Advanced)
        MarkPending(taskId);
}
```

- **`Advanced` means the stored word just changed.** Lesson 22's outcome is the send key, exactly as record 30 §3 promised: one true value, one meaning — *owed*.
- **`AlreadyThere` owes nothing.** The WMS already has that word (it was pushed when the word moved, or carried in the `201`). Pushing it again would turn idempotence into chatter.
- **`NoOrder` owes nothing.** There is no order, so there is no body to send and no WMS party to send it to. A local-move task must not queue anything.
- **Intake does not push `received`.** The `201 Created` reply already carries the full body with `state: "received"` (the routes table at `wms-rest.md:68`). The WMS read the word at the moment it was written; a push a millisecond later would say the same thing again. Silence is the correct acknowledgement.
- **The parameter is named `taskId`** because the caller holds a task id at the transition line (record 30 §4's rule: transition, sync, next line — and now, note). The ids are the same string (record 29 §3); the pile's other methods name it `orderId`, because that is what the sender looks up and what the WMS sees. One string, two shoes.

**No clock, and the record-30 condition stays unmet.** `WmsOrder` gains no "changed at" field, so this slice adds no `now` anywhere. Record 30 §5's condition — *status push wanting a change time adds the field and the parameter together, with a page bump* — is deliberately not triggered. A change-time field is a contract question for the slice that can name what the WMS does with it.

**What would change this:** the WMS asking for a delivery confirmation per order (an app-level ack distinct from the HTTP `2xx`), or a "send `received` too" rule for receivers that lost the `201`. Both are receiver-side questions for the receiver's slice.

---

## 6. Decision E: the pile stores one flag per order; the sender reads the word at send time

The outbound pile — named `WmsStatusOutbox`, in `FleetCore` — is a `HashSet<string>` of order ids under its own lock, with three moves:

| Method | Does |
|---|---|
| `MarkPending(orderId)` | `lock` + `Add` — adding twice is adding once |
| `Claim()` | `lock`, copy the ids out, clear the set, return the copy |
| `PendingCount` | `lock`, the count (a dashboard tile and a test hook) |

**Flag, not body.** The pile holds *ids only*. The sender reads the order through `board.TryGet` at send time — record 30 §3's rule, *hold the board, not the order*, applied to the wire: the word that goes out is the newest word the board has. If `in_progress` is marked and `done` lands before the send, the WMS is told `done`. Intermediate words may be skipped; that is correct, because the contract documents `state` as the order's *current place*, not a history (Decision B's notes).

Rejected: storing the body at mark time. It duplicates order data outside the board (the exact staleness rule record 30 §3 forbids), and it buys nothing — the board read is in-memory and cheaper than the copy.

Rejected: a FIFO of every event with no coalescing. The WMS would receive the full story including superseded words, the pile would grow without bound during an outage (one entry per transition, forever), and ordered redelivery would need per-message attempt tracking. The contract's "current place" wording rules this out on its own.

**Claim-then-requeue is the handoff.** `Claim` empties the pile and hands the ids to one sender pass. A failed send puts the id back (`MarkPending`). A mark that lands *during* a send is safe: the pile was emptied before the send, so the new mark is simply the next tick's work — no update is lost between claim and requeue. The pile is bounded by the number of orders in memory, which is bounded by the fleet's day.

**The lock is part of the ladder's promise, not a performance detail.** Measured today, on this exact shape (8 threads marking, one claimer draining, 3 runs each way): with the lock, **3/3 rounds claimed exactly 8000 ids**; without, **3/3 runs crashed** (`IndexOutOfRangeException` twice, `InvalidOperationException: … corrupted its state` once; RC 134). The full story — including how an unlocked check-then-write lets the order word move backwards on its own — is in the [answers file](../lessons/22-order-state-ladder-answers.md), Block 3.

**What would change this:** persistence (record 29 §7). A restart today drops the pile with everything else, in step with the orders — no half-state. When orders go to SQL, "re-mark pending on restore" (or a dirty flag on the row) becomes this pile's next record.

---

## 7. Decision F: one background loop in `serve`, shaped like `FleetServeLoop`

The sender is a `FleetManager` class, `WmsStatusPusher`, with two public methods:

- `SendPendingAsync(ct)` — one testable pass: claim, send each word, requeue failures.
- `RunAsync(ct)` — the loop: `PeriodicTimer` at the configured interval, one `SendPendingAsync` per tick.

`PeriodicTimer` + `WaitForNextTickAsync` is the house shape — measured at `FleetServeLoop.cs:42-44` and `HeartbeatPublisher.cs:16`. One `HttpClient`, built once in `RunServeAsync`, owned by the pusher. The loop starts beside `loopTask` (`Program.cs:176`) and is awaited with the same cancellation dance: `catch (OperationCanceledException) when (stopping.IsCancellationRequested)` (`Program.cs:212-219` is the precedent).

Rejected: awaiting the POST at the transition line. Every task transition would wait up to the timeout for a WMS that may be down — lesson 20's forward promise was that a slow business edge does not sit in the fleet path, and M6's outage sentence is about exactly this. The transition line's total new work stays two in-memory calls (`SyncFromTask`, `NoteSync`).

Rejected: fire-and-forget `Task.Run` per mark. Unbounded concurrent POSTs, no ordering, unobserved exceptions (and `async void` is forbidden house-wide — dotnet-style). A loop owns its retry, its interval, and its shutdown.

**What would change this:** a second outbound business hop (say, a PLC bridge) needing its own cadence — then the loop shape generalises into a small "outbound worker" convention, which is a record, not an edit.

---

## 8. Decision G: every failure puts the word back; nothing is ever given up on

Three failure shapes, one behaviour: **requeue and log.**

| Shape | What actually happens (measured today) | The sender does |
|---|---|---|
| WMS answers `5xx`/`4xx` | `PostAsync` **does not throw** — it returns the response (measured: a `500` came back in 58 ms, no exception) | check `IsSuccessStatusCode`; not success → `MarkPending` + log |
| WMS refuses the connection | `HttpRequestException`, ~**5 ms**, inner `SocketException` | catch, `MarkPending` + log |
| WMS is up but slow | client timeout fires: `TaskCanceledException` (inner `TimeoutException`) after the configured timeout (measured: 501 ms with a 500 ms client) — and `TaskCanceledException` **derives from** `OperationCanceledException` (measured) | catch `OperationCanceledException` **when the pusher's own token is not cancelled** → `MarkPending` + log |

The `when (!cancellationToken.IsCancellationRequested)` filter is the whole trick: one exception type, two meanings. Our shutdown cancels the token — that exception must propagate out of `RunAsync` to Program's catch. The *client's* timeout fires while our token is live — that one is a failed send, put the word back. Measured both ways; the filter is not decoration.

**No attempt cap.** Commands get `MaxAttempts` (record 06's retry policy) because a command is an *action* — re-running it needs idempotency and an ack story. A status word is *truth*. Giving up on the truth silently breaks M6's "orders flow … and back." The pile is bounded by the order count, the interval is the spacing, and the log line is the alarm. One key, not two: `Wms:StatusIntervalMs` doubles as the retry delay.

**The timeout lives on the `HttpClient`,** not around the call: `new HttpClient { Timeout = … }` is the measured mechanism (both probes above went through a client timeout). It bounds every request the pusher makes, including future ones.

**What would change this:** a permanent-rejection class from the WMS — the realistic one being a `404` after the WMS restarts and loses its memory of an order the fleet is still finishing. Then attempts-per-word plus an alarm, decided in the slice that also builds the mock-WMS receiver (which is the component that will invent the rejection in the first place).

---

## 9. Decision H: three configuration keys, validated at startup

```json
"Wms": {
  "BaseUrl": "http://127.0.0.1:5090",
  "StatusTimeoutMs": 2000,
  "StatusIntervalMs": 1000
}
```

- **`Wms:BaseUrl`** — where the WMS listens. Absolute URL, validated at startup with `Uri.TryCreate(..., UriKind.Absolute, ...)`; a missing or relative value fails before the bus connects. Why `5090` as the default: `5080` is the fleet manager's own door (record 28), and `5090` leaves the block free for the future mock-WMS receiver. It is a default in config, not a value in code.
- **`Wms:StatusTimeoutMs`** — the client timeout. Default `2000`. Measured today: a healthy loopback POST averaged **43.9 ms** (10 posts, total 439 ms), so 2000 ms is roughly 45× headroom; and a stuck send bounds itself to that number instead of holding a socket forever.
- **`Wms:StatusIntervalMs`** — the tick, and the retry spacing. Default `1000`, the same rhythm as `FleetManager:AlarmEvaluationIntervalMs` (`appsettings.json:20`). A dead WMS gets one attempt per second per owed order — a modest log rate, a bounded pile.

All three are read in `RunServeAsync` with the house validation shape (`GetValue` + `throw new InvalidOperationException(...)`, the pattern at `Program.cs:143-155`). AGENTS.md's rule stands: no hard-coded URL, timeout, retry count, or interval anywhere.

---

## 10. Decision I: logging, and the honest limits

**Logging is `Console.WriteLine`, one line per outcome.** Measured today: six `Console.WriteLine` sites in `FleetManager`, zero `ILogger` references anywhere in `src/`. The pusher follows the house, not the wish: one line when a word is delivered, one per failure shape with the order id, the word, and the reason. What would change this: a repo-wide logging decision — one that touches the six existing sites too, and therefore gets its own record. (The `dotnet-style` skill prefers the built-in logging abstraction; adopting it here alone would make the pusher the only structured logger in a console-printing codebase, which is a worse seam than matching.)

**Honest limits of this slice, stated for the review:**

- **No production trigger yet.** `NoteSync` has no caller in `serve`, exactly like lesson 22's `SyncFromTask` — measured today: the only non-test `SyncFromTask` reference is its own definition at `WmsOrderBoard.cs:49`. The execution slice adds both calls on the transition lines (record 30 §4's rule, extended by Decision D). The tests are the callers, shaped like that future line.
- **No receiver.** Nothing in the repo listens on `/api/order-status`; the first *live* push needs the mock-WMS slice (M6's remaining piece). This slice ships the sender, the pile, and the contract for the receiver to implement against.
- **No cancel, no door hardening, no persistence, no `WmsOrder` shape change, no `WmsOrderLadder`/`Rank` edit** (review 22's issue 4 — the literal arms and the missing `cancelled` arm — rides along to the cancel slice, which must touch that file anyway).
- **No alarm on a long outage.** The pile and the logs are the signal for now. An operator-facing alarm is the stretch goal of the assignment, and a product decision if it graduates.

---

## 11. How this slice upholds M6's "a WMS outage or a bad order does not corrupt fleet state"

- **A bad order never reaches the pile.** It is refused at the door (record 28 §4, lesson 21's intake table); no task, no ladder step, no `Advanced`, nothing owed.
- **A slow WMS cannot stall the fleet path.** The transition line's new work is two in-memory calls; the POSTs happen on a separate loop with a client-side timeout (Decision F/G — and lesson 20's promise, now kept twice).
- **A dead WMS leaves a pile, not a corruption.** Failure puts the word back; the board, the tasks, and the ladder are untouched by every network shape measured above. When the WMS returns, the pile drains — newest word per order.
- **The words cannot contradict each other.** The ladder refuses backwards moves (lesson 22), the lock keeps the check-then-write honest (Decision E's probe), and the sender reads the board's current word at send time — the stream stays a rising line.
- **A restart still drops the pair together** — now with the pile in the same bag (record 29 §5). Nothing half-sent survives; the WMS's view and the fleet's memory are rebuilt together, from intake, with `201` bodies as the seed.

---

## 12. Verification (measured)

Checked today (2026-10-20), in this repo and in scratch copies under `/tmp/opencode` (`l23base`, `l23probe`, `l23rtt`, `l23const`, `l23web`, `l23fivehundred`, `l23notnull`, `l23lock`, `l23toy`, `l23toy-mut`). All scratch is deleted afterwards. Nothing was left running.

| Claim | How | Result |
|---|---|---|
| SDK | `dotnet --version` | `10.0.112` |
| Baseline builds clean | fresh copy of `src` in `/tmp/opencode/l23base`; `dotnet build` | **0 Warning(s), 0 Error(s)** |
| Baseline pure suite is green | same copy; `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0; **Failed 0, Passed 169, Total 169** (FleetCore 45, FleetManager 56, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10) |
| No outbound HTTP exists yet | `grep -rn "HttpClient" src --include=*.cs` | no matches (rc 1) |
| No production `SyncFromTask` caller | `grep -rn "SyncFromTask" src --include=*.cs \| grep -v Tests` | one hit: the definition, `WmsOrderBoard.cs:49` |
| No `Wms` config section | `grep -c '"Wms"' src/FleetManager/appsettings.json` | `0` |
| The REST page is at v1, no push route | `grep -n "^Version:" docs/contract/wms-rest.md`; `grep -c "order-status" docs/contract/wms-rest.md` | `Version: 1` (line 3); `0` |
| The page's own bump rule | read `docs/contract/wms-rest.md:90-93` | decision record + page bump; never an MQTT bump |
| MQTT contract untouched need | `grep -n "Current = " src/Contracts/MessageTypes.cs`; `grep -n "shuttleStatus" docs/contract/v4.md` | `Current = 4`; v4's only status is `shuttleStatus` |
| Route constant home + pin precedent | read `src/FleetCore/WmsRoutes.cs`; `src/FleetCore.Tests/WmsContractLiteralsTests.cs:9-14` | `Orders` const + route facts, the shape this slice copies |
| `/api/orders/status` would collide | read `WmsRoutes.cs:8` | `Order(id)` builds `/api/orders/{id}` — a literal `status` is a valid id on that route |
| Default JSON is the wrong shape; Web is right | scratch `/tmp/opencode/l23probe` E4 | default: `{"OrderId":…}`; Web: `{"orderId":…,"state":"in_progress"}` |
| The door already serves camelCase | scratch `l23web`, minimal API + `curl` | `{"orderId":"ORD-1001",…,"state":"received",…}` |
| A `500` reply does not throw | scratch `l23fivehundred` | `500 reply: NO THROW, status=500 after 58 ms` |
| A slow WMS trips the client timeout | scratch `l23probe` E1 (real listener, 3 s sleep, 500 ms client) | `TaskCanceledException after 520 ms; inner=TimeoutException`; server saw `path=/api/order-status contentType=application/json; charset=utf-8` |
| BaseAddress + absolute path resolve | same run | `http://127.0.0.1:5099` + `/api/order-status` → `http://127.0.0.1:5099/api/order-status` |
| A refused connection is a fast `HttpRequestException` | scratch `l23probe` E2 (nothing listening) | `HttpRequestException after 5 ms; inner=SocketException` |
| Stub-handler timeout, no sockets needed | scratch `l23probe` E3 (`HttpMessageHandler` sleeping 3 s) | `TaskCanceledException after 501 ms; inner=TimeoutException` |
| `TaskCanceledException` derives from `OperationCanceledException` | scratch `l23const` | `base of TaskCanceledException = OperationCanceledException` |
| Healthy POST latency | scratch `l23rtt`, 10 posts, local listener | first `200`; total 439 ms, **avg 43.9 ms** |
| Constant patterns compile (issue-4 context) | scratch `l23const` | `Rank(received)=0` — `Words.Received => 0` builds and runs |
| `Assert.NotNull` carries `[NotNull]` | scratch `l23notnull`, xunit 2.9.3 | with assert: **no warning**; without: `warning CS8602` (build RC 0, 1 Warning — no warnings-as-errors in the repo) |
| The pile's lock does real work | scratch `l23lock`: 8 threads × 1000 marks, one claimer, 3 rounds | with lock: `claimed=8000 expected=8000 PASS` ×3; without: crash RC 134 ×3 (`IndexOutOfRangeException` ×2, `InvalidOperationException: … corrupted its state` ×1) |
| No catch hides a task refusal anywhere | `grep -rn "catch (TaskRejected" src --include=*.cs` | no matches (rc 1) |
| House loop shape | read `src/FleetManager/FleetServeLoop.cs:42-44`, `src/ShuttleSim/HeartbeatPublisher.cs:16` | `PeriodicTimer` + `WaitForNextTickAsync` |
| House config-validation shape | read `src/FleetManager/Program.cs:143-155` | `GetValue` + `InvalidOperationException` per key |
| House shutdown shape | read `src/FleetManager/Program.cs:212-227` | `catch (OperationCanceledException) when (stopping.IsCancellationRequested)` per task |
| No `ILogger`, no warnings-as-errors | `grep -rn "ILogger" src …`; `grep -rn "TreatWarnings" src --include=*.csproj` | rc 1 / rc 1 |
| `FleetManager.Tests` can reach `FleetCore` | read `src/FleetManager/FleetManager.csproj:8` (refs FleetCore); existing `FleetManager.Tests` files using `TaskBoard`/`FleetTask` | transitive reference is live in the working suite |
| No containers, no stray processes | `docker compose -f docker/compose.yml ps`; `pgrep -af "dotnet run\|FleetManager.dll\|mosquitto"` | empty table; no matches |
| Working tree before writing | `git status --short`, `git log --oneline -3` | clean; HEAD `aa29b3f` |

Not yet verified: the owner's implementation of Decisions C–I (build, tests, greps, mutations) — that is the assignment. This record states the design.

---

## 13. What this record unblocks

- Lesson 23's assignment has no unguided product choices: the transport, the contract text, the route, the body shape, the send key, the pile's storage, the loop shape, the failure policy, the config keys, and the logging rule are all fixed here.
- The **mock-WMS receiver** (M6's remaining slice) has its contract to implement against: one POST route, the documented body, `2xx` means delivered, repeats are duplicates.
- The **execution slice** inherits two lines per transition instead of one: `SyncFromTask(taskId)` then `NoteSync(taskId, outcome)` — record 30 §4's rule, extended, with the batching question already answered (answers file, Block 1 item 6).
- The **cancel** slice still owns `cancelled` and the reverse direction — and now also the `Rank`/`ForTask` alignment review 22 flagged, because it must open `WmsOrderLadder.cs` anyway.
- **M7's dashboard** has a second field to show: `WmsStatusOutbox.PendingCount` — "words the WMS is owed" — already testable, already thread-safe.
