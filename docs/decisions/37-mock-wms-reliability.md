# Decision Record 37: The mock WMS and delivery reliability

**Date:** 2026-10-26
**Milestone:** M6 (WMS integration) — lesson 28: a real mock WMS receiver, delivery outcomes, an idempotent ledger, and per-order backoff
**Status:** Accepted. Append-only. Builds on [record 28](../decisions/28-wms-rest-front-door.md) (the WMS front door and `WmsRoutes`), [record 31](../decisions/31-status-push.md) (the status pusher and the outbox), [record 34](../decisions/34-persistence-slice.md) (the board/outbox pair and the serve loop), [record 36](../decisions/36-order-cancel-slice.md) (the `cancelled` word), and [record 01](../decisions/01-language-and-layout.md) (C#/.NET as the project language).
**Implements:** M6's promise: *"A mock WMS submits store and retrieve orders over REST, receives status updates, and can cancel orders. It also sends bad input and replies slowly or not at all."* The last sentence is the gap this record closes. It also serves M6's done-when: *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."*
**Wire contract:** **no change.** `ContractVersion.Current` stays `4`. `docs/contract/v4.md` and `docs/contract/wms-rest.md` are untouched. The WMS REST page stays at version `2`. The mock's `/mock/*` routes are a test harness, not the contract.

---

## 1. What we are deciding

Lesson 23 built the status sender. Lesson 24 wired it into `serve`. But the sender has nobody to talk to:

- `src/FleetManager/appsettings.json` points at `http://127.0.0.1:5090`.
- Nothing in the tree listens on 5090. Measured: `ss -ltn '( sport = :5090 or sport = :5080 )'` returns no listener.
- There is no `MockWms` project in `src/ShuttleFleet.slnx`.

The sender also treats every failure the same. Measured in `src/FleetManager/WmsStatusPusher.cs:38-45`: `response.IsSuccessStatusCode` decides success, and every other status calls `_outbox.MarkPending`. A `404` is retried forever. A `500` is retried on the next one-second tick. A dead WMS gets one request per owed order per tick, with no pause.

That is safe but not reliable. A `404` will never heal. A dead WMS gets hammered. And the fleet manager has never seen the most important failure case: the WMS applied the word, then the reply was late, so the fleet cannot know whether to retry.

This record settles five things:

1. **Where the mock lives** and how it is built (decision A).
2. **How the mock fails on demand** (decision B).
3. **What each reply means** and what the fleet does with it (decisions C and D).
4. **How retries are paced** (decision E).
5. **Configuration and the contract boundary** (decisions F and G).

Everything here is a choice the implementer should not have to make. Lesson 28's assignment points at this record for each requirement.

---

## 2. Decision A: a separate `MockWms` app on port 5090

### Options

- **A: a new ASP.NET Core app, `src/MockWms`, on `http://127.0.0.1:5090`, with its own JSON types and its own ledger.** (chosen)
- **B: a fake `HttpMessageHandler` inside the fleet manager tests.** Low effort, but no real socket, no real timeout, no process to stop, and nothing the owner can demo by hand.
- **C: WMS routes inside `FleetManager`.** Low effort, but the external-system boundary disappears. The fleet manager would call itself, so a WMS outage could never be observed.

### Decision

**A.** The mock is a separate process. That is what makes the failure real: the owner can stop it, slow it, and make it return errors without touching the fleet manager. It listens on the port the fleet manager already points at, so no fleet-side URL changes.

The mock is deliberately dumb:

- It has its own `MockOrder` type. It does **not** reference `FleetCore` or `Contracts`. A shared type would hide a JSON mismatch; the contract test must catch that.
- It stores the ledger in memory. A real WMS has a database; the mock is a test tool.
- It uses the documented route `POST /api/order-status` for the fleet's pushes.

### Why not B or C

- **B** cannot show a timeout or a refused connection. Those are the cases the lesson is about. A fake handler also cannot be stopped mid-run by the owner.
- **C** removes the network path. The fleet manager would always be reachable from itself, so "the WMS is down" would be untestable. It also muddies the front door: one process would serve both the WMS-facing API and the WMS itself.

### What would change this

If the project later runs the mock in a container beside the fleet manager, the location stays the same and only the process boundary moves. If the project decides the mock must survive a restart, its ledger moves to Postgres or a file; that is a new decision.

---

## 3. Decision B: four runtime modes — `ok`, `slow`, `error`, `hang`

### Options

- **A: runtime modes set by `POST /mock/mode`; the same process keeps its ledger.** (chosen)
- **B: a config value plus a restart.** A restart loses the in-memory ledger, which breaks the duplicate story: the WMS applied the word, then the reply was late.
- **C: separate binaries or command-line flags.** More launch commands, and the owner has to stop and start the right one.

### Decision

**A.** The mock has four modes:

| Mode | What the route does | What the fleet sees |
|---|---|---|
| `ok` | Apply the word, answer `200`. | Delivered. |
| `slow` | Apply the word, wait `MockWms:SlowDelayMs`, answer `200`. | A client timeout, then a retry. The retry is a duplicate. |
| `error` | Do **not** apply, answer `500`. | Retry. |
| `hang` | Do **not** apply, wait forever. | A client timeout, then a retry. |

The order of operations is the point:

- `slow` applies **before** it delays. Measured in `/tmp/opencode/l28slow`: the first attempt timed out, the retry got `200`, and the ledger showed `entries=1 applied=1 duplicate=1`. That is the lost-reply case, and it is why the receiver must be idempotent.
- `error` answers **before** it applies. A `500` means "I did not do the work." Retrying is safe.
- `hang` never applies. It models a process that is up but stuck. The client timeout is the only way out.

The mode is read once at the start of a request. A mode change mid-request does not split one request into two behaviours.

The control routes are mock-only:

| Route | Why |
|---|---|
| `GET /mock/state` | Mode, delay, ledger, counters. |
| `POST /mock/mode` | Set `ok`, `slow`, `error`, or `hang`, and optionally the delay. |
| `POST /mock/reset` | Clear the ledger and counters; keep the mode. |

`GET /api/orders/{orderId}` and `GET /api/orders` are the mock's read side. They are useful for a demo and for the stretch goal. They are not the contract either.

`POST /mock/mode` takes `{ "mode": "ok|slow|error|hang", "slowDelayMs": 3000 }`. `slowDelayMs` is **optional**: when it is missing, the current delay stays. The smoke test in lesson 28 sends only `{ "mode": "error" }`, so the route must not treat the missing delay as an error. When it **is** present, it must be positive; zero or a negative value answers `400`. A zero delay would make `slow` behave exactly like `ok` and hide the duplicate case.

### Why not B or C

- **B** cannot show a duplicate after a slow reply, because the restart forgets that the word was applied.
- **C** adds launch complexity without adding a failure the owner cannot already get from A.

### What would change this

If a later slice needs the mode to be set from the dashboard, the route stays and the dashboard calls it. If the mock needs to replay a recorded scenario exactly, the mode moves into a scenario file; that is a new decision.

---

## 4. Decision C: a pure delivery rule with three buckets

### Options

- **A: a pure function, `WmsDeliveryOutcome.FromStatus(int?)`, maps a reply to `Delivered`, `Retry`, or `Permanent`.** (chosen)
- **B: retry every failure forever.** The current behaviour. A dead order is retried at the cap forever, and a `404` never stops.
- **C: drop every failure silently.** The WMS never hears the truth and the operator never knows.

### Decision

**A.** The rule is pure: no `HttpClient`, no clock, no logging.

| Input | Bucket | Why |
|---|---|---|
| `2xx` | `Delivered` | The word is safe. |
| `408` | `Retry` | Request timeout. Temporary. |
| `429` | `Retry` | Too many requests. Temporary. |
| `5xx` | `Retry` | The server failed. It may recover. |
| `null` (no reply) | `Retry` | The network might be back. |
| Other `4xx` | `Permanent` | The request is wrong or the resource is gone. |
| `1xx`, `3xx` | `Retry` | Not a clear success and not a clear bad request. The safe choice is retry. |

The `int?` input is deliberate. Measured with `/tmp/opencode/l28probe` against a real `HttpListener`:

- `500`, `400`, `404`, and `429` do **not** throw. `HttpClient` returns a response, so the pusher must read `response.StatusCode`.
- A 2-second server with a 300 ms client timeout throws `TaskCanceledException` after 308 ms, with an inner `TimeoutException`.
- A refused connection throws `HttpRequestException` after 7 ms, with an inner `SocketException`.
- `TaskCanceledException` is catchable as `OperationCanceledException`.

The catches pass `null` to the rule. That keeps the rule easy to test and easy to explain: the rule only knows status codes, and "no status" is a value.

### Why not B or C

- **B** turns a permanent `4xx` into a noisy loop and hides the operator's clue.
- **C** loses the word and breaks the fleet's promise that the WMS eventually hears the truth.

### What would change this

If the WMS contract later defines a `4xx` that means "retry later" (for example a `425 Too Early` used as a busy signal), the rule adds that code and a test. If the WMS needs `2xx` with a body that says "rejected", the rule grows a body check; that is a contract change, not a silent extension.

---

## 5. Decision D: permanent failures are recorded; retryable ones are re-marked

### Options

- **A: `Delivered` clears the retry state and the failure; `Permanent` records a reason and stops; `Retry` re-marks and schedules.** (chosen)
- **B: record every failure and keep retrying.** The board fills with temporary failures and the operator cannot tell which ones are real.
- **C: log only, no board.** The owner has to read logs to find a rejected word. The M7 dashboard has nothing to show.

### Decision

**A.** Three actions, one per bucket:

- **`Delivered`** — remove the retry state, clear the failure record for that order, and leave the id out of the outbox. `Claim()` already took it out.
- **`Permanent`** — remove the retry state, record `HTTP {code}` in `WmsDeliveryFailures`, and **do not** call `MarkPending`. This word will not be retried.
- **`Retry`** — grow the failure count, set the next attempt time, and call `MarkPending`.

`WmsDeliveryFailures` is a small in-memory board with `Record`, `Clear`, and `Snapshot` (ordered by order id, under a lock). The fleet manager exposes it at `GET /wms/delivery-failures`. The route constant is `WmsRoutes.DeliveryFailures`.

The board is not persisted. A restart forgets permanent failures. That is a known limit, written down in the lesson. The order itself is still readable through `GET /api/orders/{orderId}`, and a later status push that succeeds clears the record.

### Why not B or C

- **B** makes the board useless: a transient `503` would look like a rejected order.
- **C** hides the one failure a human must act on. A permanent `4xx` is an integration bug or a bad order; logs are not a dashboard.

### What would change this

If permanent failures need to survive a restart, they move into the same persistence layer as the order board. That is a diagnostics slice, not this one. If a permanent failure needs an operator action (retry now, edit the order), the board grows a state and a route; that is a new decision.

---

## 6. Decision E: per-order exponential backoff with a ceiling

### Options

- **A: a pure `WmsRetryPolicy` plus one retry state per order in the pusher.** (chosen)
- **B: keep the current fixed tick.** A dead WMS is hit every second for every owed order.
- **C: one timer per order.** Many timers, many lifetimes, easy to leak.

### Decision

**A.** The wait is a pure function of the failure count:

- `DelayFor(1)` is the base delay.
- Each earlier failure doubles the wait.
- The wait is clamped at the max delay.
- `failures < 1` throws.
- The constructor rejects a base of zero or less, and a max smaller than the base.

With a base of 1000 ms and a cap of 30000 ms, the waits are `1000, 2000, 4000, 8000, 16000, 30000` ms. The toy in lesson 28 measured this output.

The pusher owns the state: one `RetryState(int Failures, DateTime NextAttemptAt)` per order id. The loop still ticks every `Wms:StatusIntervalMs`. On each pass:

- Read `now` once.
- Claim the owed ids.
- For each id, if a retry state exists and `now < NextAttemptAt`, call `MarkPending` and skip.
- Otherwise send, then classify the reply.

A success removes the state and the failure. A permanent failure removes the state and records. A retry grows the count and sets `now + DelayFor(failures)`.

The dictionary is owned by the single pusher loop. The outbox keeps its own lock. The retry dictionary is not shared with the web routes, so it needs no lock.

### Why not B or C

- **B** is a hammer, not a retry. It also makes the mock's `error` mode produce one request per order per second forever.
- **C** creates lifetime and cleanup problems the simulator does not need. One loop with a due-time check is easier to explain and easier to test.

### The old constructor

The existing four-argument constructor stays, because existing tests and the current `Program.cs` call it. It delegates to the new constructor with:

- `new WmsRetryPolicy(interval, interval)` — a fixed wait of one tick, which matches the old "try again next pass" behaviour.
- `new WmsDeliveryFailures()` — a private board nothing reads.
- `() => DateTime.UtcNow` — the real clock.

This keeps the old call sites working and makes the new behaviour testable with a fake clock.

### What would change this

If several fleet managers later share one WMS, add jitter so they do not retry on the same tick. If a WMS gives a `Retry-After` header, the policy learns to honour it. Both are new decisions; neither belongs in this slice.

---

## 7. Decision F: configuration and startup validation

### Decision

Add these keys:

| Key | Default | Rule |
|---|---|---|
| `Wms:RetryBaseDelayMs` | `1000` | Must be positive. |
| `Wms:MaxRetryDelayMs` | `30000` | Must be at least the base. |
| `MockWms:SlowDelayMs` | `3000` | Must be positive. |

The fleet manager validates the first two at startup, next to the existing `Wms:StatusTimeoutMs` and `Wms:StatusIntervalMs` checks. A bad value stops `serve` with a clear message. The mock validates its delay at startup and on `POST /mock/mode`.

No timeout, URL, delay, retry count, or port is hard-coded.

**The launch profile gotcha.** `dotnet new web` writes `src/MockWms/Properties/launchSettings.json` with a random port (the installed template used 5153). `dotnet run` applies that profile, so it can override `Kestrel:Endpoints:Http:Url` in `appsettings.json`. Delete the `Properties` folder, or set its URL to `http://127.0.0.1:5090`. This was measured; it is not a guess.

### Why

The fleet manager already treats a bad URL as a startup error (record 31). The retry values deserve the same treatment: a zero base would make the wait meaningless, and a max below the base would make the first wait jump down.

---

## 8. Decision G: the mock's controls are not the contract

### Decision

The WMS contract is the documented `POST /api/order-status` body and the client's rules. The mock's `/mock/*` routes exist only so the owner and the tests can change the failure mode. They are documented in `src/MockWms/README.md`, not in `docs/contract/`.

- `docs/contract/wms-rest.md` stays at version `2`.
- `src/Contracts/` is untouched.
- `ContractVersion.Current` stays `4`.

The mock's ledger rank includes `cancelled` at rank 3, above `done` and `failed` at rank 2. This matches lesson 27's rule that a cancel is a terminal word and must not be replaced by a later `done` or `failed`. The mock has its own rank. It does not change `WmsOrderLadder`, `TaskState`, or the cancel route.

**Lesson 27 dependency:** lesson 27's cancel code is not in the tree today. Measured: `TaskState` has no `Cancelled`, and `WmsOrderLadder` has no `cancelled` rung. This slice does not need them. It accepts the word `cancelled` so it is ready when 27 lands.

### Why

The contract is the promise between the fleet manager and the WMS. A test harness's control route is not part of that promise. Putting it in the contract would force every real WMS to implement `/mock/mode`, which is absurd.

---

## 9. How this slice upholds M6

- **A slow WMS does not corrupt fleet state.** The mock applies the word, the client times out, the fleet retries, and the ledger sees a duplicate. The order does not move twice. Measured: `entries=1 applied=1 duplicate=1`.
- **A dead WMS does not corrupt fleet state.** The word stays in the outbox. The retry state spaces out attempts. The order board is untouched by the failure.
- **A bad order does not corrupt fleet state.** A permanent `4xx` is recorded and removed from the outbox. The fleet does not retry a request that cannot succeed.
- **A late old word does not move an order backward.** The ledger's rank check drops it as stale. The fleet's own ladder has the same rule (record 30).
- **A cancel still wins.** `cancelled` is rank 3, above `done` and `failed`. A later `done` is stale.
- **A restart still loses only what was always in memory.** The order board is persisted (record 34). The retry state and the failure board are not. The lesson says so out loud.

---

## 10. Scope and limits

- **No order submission from the mock.** The stretch goal adds `POST /mock/orders`; it is not part of the required assignment.
- **No cancel client.** Lesson 27 owns the cancel route. This slice only accepts the word.
- **No traffic-control change.** The pusher runs beside the task loop, not inside it.
- **No dashboard.** `GET /wms/delivery-failures` is the data the M7 dashboard will read.
- **No contract change.** MQTT stays `4`; WMS REST stays `2`.
- **No persistence for retry state or failures.** A restart resets the backoff and forgets permanent failures.
- **No jitter.** One fleet manager means no herd. A real multi-manager fleet would add it.
- **No circuit breaker.** The first pass after an outage still sends one request per owed order. Backoff reduces the rate over time, not the first burst.
- **`hang` is sequential.** The pusher sends one request at a time, so in `hang` mode each owed order waits the client timeout in turn. With many orders, one pass can take many timeouts. That is why the pusher loop is separate from the task loop: the fleet keeps moving while the WMS is stuck.

---

## 11. Verification (measured)

Checked on **2026-10-26** in this repo and in scratch copies under `/tmp/opencode`. Scratch copies are deleted afterwards. Nothing was left running.

| Claim | How | Result |
|---|---|---|
| SDK present | `dotnet --version`; `cat global.json` | `10.0.113`; `global.json` asks for `10.0.112` with `latestFeature` |
| Baseline builds clean | copy of `src` in `/tmp/opencode/l28base`; `dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |
| Baseline pure suite is green | same copy; `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **Failed: 0, Passed: 190, Total: 190** (FleetCore 52, FleetManager 70, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10) |
| No mock WMS exists | `grep -n "MockWms" src/ShuttleFleet.slnx`; `ls src` | no matches; no `MockWms` folder |
| Nothing listens on 5090 or 5080 | `ss -ltn '( sport = :5090 or sport = :5080 )'` | header only; no listener |
| 5090 is only a config value | `grep -rn "5090" src --include=*.json --include=*.cs` | `src/FleetManager/appsettings.json:32`, plus a URL parse test |
| The pusher has no retry pacing | read `src/FleetManager/WmsStatusPusher.cs:22-64` | every failure calls `MarkPending`; no delay, attempt count, or next-attempt time |
| The pusher treats all failures the same | read `src/FleetManager/WmsStatusPusher.cs:38-45` | `IsSuccessStatusCode` decides; every other status does the same `MarkPending` |
| A `500` does not throw | scratch `/tmp/opencode/l28probe` against a real `HttpListener` | `/500: no throw, status=500, 5 ms` |
| A `400`, `404`, `429` does not throw | same run | `/400: no throw, status=400, 5 ms`; `/404: no throw, status=404, 1 ms`; `/429: no throw, status=429, 0 ms` |
| A slow WMS trips the client timeout | same run, client timeout 300 ms, server sleeps 2 s | `/slow: TaskCanceledException after 308 ms; inner=TimeoutException` |
| A refused connection is a fast, typed failure | same run, nothing on port 5182 | `refused: HttpRequestException after 7 ms; inner=SocketException` |
| The timeout type is catchable as `OperationCanceledException` | same run | `TaskCanceledException base = OperationCanceledException` |
| A slow WMS that applies before answering causes a duplicate on retry | `/tmp/opencode/l28slow`; apply, sleep 1 s, client timeout 300 ms, retry | first timed out; retry `200`; `ledger entries=1 applied=1 duplicate=1` |
| `dotnet new web` and `dotnet sln add` work on this tree | in `/tmp/opencode/l28base` | `Project 'MockWms/MockWms.csproj' added to the solution.` |
| A test project can reference the web app | same copy: `dotnet new xunit`, `dotnet add reference`, `dotnet test` | `Passed! - Failed: 0, Passed: 1, Total: 1` |
| The toy runs as printed | `/tmp/opencode/l28toy`; `dotnet run` | `ledger: ORD-1=done applied=2 duplicate=1 stale=1`; `200 Delivered`, `400/404 Permanent`, `429/500/no reply Retry`; waits `1000/2000/4000/8000/16000/30000` ms |
| `cancelled` exists as a word | read `src/FleetCore/WmsOrderStates.cs:9` | `public const string Cancelled = "cancelled";` |
| Lesson 27 is not in the tree yet | `grep -n "Cancelled" src/FleetCore/TaskState.cs` | no matches |
| The fleet-side ladder has no `cancelled` rung yet | read `src/FleetCore/WmsOrderLadder.cs:15-22` | `received`, `in_progress`, `done`, `failed`; no `cancelled` |
| The contracts did not move | `grep -n "Current = " src/Contracts/MessageTypes.cs`; `grep -n "^Version:" docs/contract/wms-rest.md docs/contract/v4.md` | MQTT `4`; REST `2`; MQTT page `4` |
| No containers or app processes are running | `docker compose -f docker/compose.yml ps`; `pgrep -af "dotnet run\|FleetManager.dll\|MockWms.dll\|mosquitto"` | empty table; no real process |
| Working tree before writing | `git status --short`; `git log --oneline -3` | two pre-existing `.opencode/agents/*.md` edits; HEAD `e2923c6` |

**Not yet verified:** the `MockWms` app, the backoff map, the failure board, and every test in the assignment. That is the implementer's work. This record states the design; lesson 28's acceptance commands are what prove it.

---

## 12. What this record unblocks

- **Lesson 28's assignment** has no unguided choices: project location, port, mode names, mode order, ledger key, rank order, apply results, reply buckets, retry timing, failure board, route constant, config keys, and the old-constructor behaviour are all fixed here.
- **M7's dashboard** can call `GET /wms/delivery-failures` and show words the WMS refused. It does not re-derive the rules.
- **M8's load test** can flip the mock to `slow` or `error` and measure how the retry rate changes as the backoff grows. The mock's mode route is the knob.
- **The mock's submit stretch goal** can post the documented `SubmitOrderRequest` to the fleet manager's `POST /api/orders` and then watch the same status push come back. That closes the M6 loop: WMS out, fleet work, WMS back.

---

## Appendix: route and type map

| Thing | Where | Kind |
|---|---|---|
| `POST /api/order-status` | mock | contract (documented in `wms-rest.md` v2) |
| `GET /api/orders/{orderId}` | mock | mock read side |
| `GET /api/orders` | mock | mock read side |
| `GET /mock/state` | mock | mock control |
| `POST /mock/mode` | mock | mock control |
| `POST /mock/reset` | mock | mock control |
| `GET /wms/delivery-failures` | fleet manager | fleet diagnostics |
| `WmsDeliveryResult` | `FleetCore` | enum: `Delivered`, `Retry`, `Permanent` |
| `ApplyResult` | `MockWms` | enum: `Applied`, `Duplicate`, `Stale`, `Conflict` |
| `WmsRetryPolicy` | `FleetCore` | pure class |
| `WmsDeliveryFailures` | `FleetCore` | in-memory board with a lock |

The mock's `ApplyResult` and the fleet's `WmsDeliveryResult` are different enums on purpose. They answer different questions: one is "did the receiver change its ledger?", the other is "should the sender try again?". Sharing them would blur the two sides of the wire.
