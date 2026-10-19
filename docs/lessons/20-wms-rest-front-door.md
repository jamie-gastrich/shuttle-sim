# Lesson 20: The WMS front door — REST orders in, validation first

**Milestone:** M6 (WMS integration) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 19: Wiring the recovery round trip](19-recovery-wiring.md) · **Decision:** [record 28](../decisions/28-wms-rest-front-door.md)

**Concepts this assignment requires:**

- [ ] **A second door on the fleet manager.** HTTP for business systems, MQTT for shuttles, and the HTTP door lives inside `serve` in the same process. Taught in [1) The fleet edge: REST in, MQTT down](#1-the-fleet-edge-rest-in-mqtt-down).
- [ ] **The WMS order contract.** The request body, the success body, the error body, the routes, the status codes, and where they are documented. Taught in [2) The WMS order contract](#2-the-wms-order-contract).
- [ ] **Pure rules behind one door.** Validation is a pure function; bad input never reaches fleet state; one small registry holds the orders and owns the one lock. Taught in [3) Pure rules behind one door](#3-pure-rules-behind-one-door).

Three new concepts. That is the cap.

**Not in this lesson:** turning an order into a `FleetTask` (that is the next slice, with the runtime dispatcher and the database wiring); pushing order status updates to the WMS (that is the slice after that, where a slow or dead WMS gets its own record); cancelling orders; load/unload commands on the MQTT wire; order persistence; the dashboard (M7); and **any change to the MQTT contract** — `ContractVersion.Current` stays `4` and `docs/contract/v4.md` is untouched.

**The end-product fact:** today, nothing outside tests can put work into a running fleet manager. There is no task intake at runtime at all — `serve` only watches status and publishes alarms. The WMS door is the first runtime way real work enters the system. Everything after M6 hangs off it.

---

## Where this fits

M6 says: *"A mock WMS submits store and retrieve orders over REST, receives status updates, and can cancel orders. It also sends bad input and replies slowly or not at all. Done when: orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."*

That is three jobs: **take orders in**, **move them through the fleet**, and **tell the WMS what happened**. This lesson is job one only. It is small on purpose: the front door is where "a bad order does not corrupt fleet state" gets decided, and it is the piece every later M6 slice calls.

Everything here reuses patterns you already own. The order types mirror `Contracts` records. The wire names mirror `TaskStateWire`. The registry mirrors `TaskBoard`. The endpoint is a thin shell around pure rules, the same split as `ShuttleEndpoint` around `Shuttle`.

**Forward references.** The next lesson maps an accepted order onto the existing `TaskBoard` and the runtime dispatcher. The lesson after that pushes status to a configured WMS URL with a timeout, so a slow WMS can never sit in the fleet-state path. M7's dashboard calls the same HTTP door. The order state ladder decided here is the shape the WMS will see for the rest of M6.

---

## Gate before you start: M5 has a red test at HEAD

Do this first. It is short, and it is not optional.

I ran the non-integration suite on a clean copy of `src` at commit `99461cb` (the working tree is clean, so the copy is HEAD):

```bash
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
```

Result: **145 passed, 1 failed, 146 total**. The failure is:

```text
[xUnit.net 00:00:00.80] ShuttleEndpointTests.Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing [FAIL]
  Error Message:
   Assert.Single() Failure: The collection was empty
```

I ran `ShuttleSim.Tests` alone three more times: failed 3 of 3. It is not flaky.

The cause is visible in `src/ShuttleSim/ShuttleEndpoint.cs:121-135`. The recovery handler has an `if (_shuttle.State == ShuttleState.Fault) { ... }` branch and **nothing after it**. When the shuttle is not faulted, the handler publishes no ack at all, so the test that expects exactly one ack sees zero.

This is the "already healthy still answers yes" rule from lesson 19, [record 27 §5](../decisions/27-recovery-wiring-and-qos-families.md). The reviewer's pass and README both report `149/149`; that does not reproduce at HEAD. Either the branch was lost in a later commit, or the pass did not cover this test. Either way, the fix is the same.

**What to do:** restore the healthy path so the test passes. The behaviour is already decided and the test names it: not in `Fault` → answer `Accepted: true`, `Reason: null`, change nothing, publish no status. Then tell the reviewer; the milestone table needs a re-check with a command that actually runs.

**Still open from M5, and not fixed by this lesson:**

- The six explain-it questions in [`docs/reviews/19-recovery-wiring.md`](../reviews/19-recovery-wiring.md) (Explain-it check) are still unanswered. Answer them out loud before you move on. Question 2 — *idempotency by precondition* — comes back in this lesson under a new name: a re-sent order.
- The `recover` CLI's health check is still "not yet verified": mutation M-idle survives because no test covers `Program.cs`. Keep it on the board. It does not block this lesson.

---

## Verification table

Everything below was checked today, in this repo or in a scratch copy under `/tmp/opencode`. Scratch copies were deleted afterwards. Nothing was left running.

| Claim | How I checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| Non-integration suite at HEAD | clean copy of `src` in `/tmp/opencode/l20base`; `dotnet build`, then `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0; **145 passed / 1 failed / 146** (FleetCore 22, Contracts 8, TrafficCore 6, ShuttleCore 31, Messaging 10, MqttMessaging 3, FleetManager 56, ShuttleSim 9/10) |
| The one failure is real and repeatable | `dotnet test ShuttleSim.Tests` three times | failed 3 of 3: `Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing` |
| The healthy branch is absent at HEAD | read `src/ShuttleSim/ShuttleEndpoint.cs:121-135`; `git show HEAD:src/ShuttleSim/ShuttleEndpoint.cs` | method ends after the fault branch; no `else`, no second ack call |
| No HTTP code exists in the repo yet | `grep -rn "WebApplication\|HttpClient\|Kestrel\|MapGet\|MapPost\|AddControllers\|HttpListener" src --include=*.cs --include=*.csproj` | no matches |
| The minimal-API template exists | `dotnet new list` | "ASP.NET Core Empty", short name `web` |
| Converting `FleetManager` to the Web SDK builds | copy of `src` in `/tmp/opencode/l20host`; `Sdk="Microsoft.NET.Sdk.Web"`; `dotnet build` | **0 errors**; 6 warnings (`NU1510`) because the three `Microsoft.Extensions.Configuration.*` package refs are now redundant |
| Removing those three package refs builds clean | same copy; removed the three refs; `dotnet build --no-incremental` | **0 warnings, 0 errors** |
| The console CLI still works under the Web SDK | `dotnet run --project FleetManager` (no args); then bad args | RC **0** printing `Fleet Manager Coming Online`; RC **2** printing the usage line |
| The Web SDK copies `appsettings.json` next to the binary | `ls widget-toy/bin/Debug/net10.0/` | `appsettings.json` present |
| Kestrel URL comes from config, and config beats the template's launch URL | widget toy in `/tmp/opencode/widget-toy`, `dotnet run`, read `server.log` | `Overriding address(es) 'http://localhost:5279'. Binding to endpoints defined via IConfiguration...` then `Now listening on: http://127.0.0.1:5088` |
| A pure check maps to real HTTP answers | three `curl` calls against the toy | `201 Created` + `Location: /api/widgets/w-1` + camelCase body; `400 Bad Request`, `Content-Type: application/problem+json`, body shown in the worked example; `415` for a wrong content type |
| The binary folder as content root works from any working directory | set `ContentRootPath = AppContext.BaseDirectory`, then ran the built dll from `/tmp` | `Content root path: .../bin/Debug/net10.0/`, still listening on `5088`, POST → `201` |
| Environment default without a launch profile | toy with `--no-launch-profile` and no `ASPNETCORE_ENVIRONMENT` | `Hosting environment: Production`; the framework's malformed-JSON answer is an empty `400` in Production and a stack trace in Development |
| An HTTP-level test package exists for net10 | `dotnet package search Microsoft.AspNetCore.Mvc.Testing --take 1` | `10.0.13` |
| `FleetManager` has no launch profile | `ls src/FleetManager` | no `Properties/` folder, so no `launchSettings.json` |

Not yet verified: the composition itself — Kestrel and the MQTT serve loop running together in one `FleetManager serve` process, and a clean shutdown of both. Each half is verified; the join is what you build.

---

## Options survey (design choices)

### Survey 1: Where does the HTTP door live?

| | A: inside `FleetManager serve`, Web SDK + minimal API (chosen) | B: a second process, `FleetManager.Api` | C: inside `serve`, raw `HttpListener` |
|---|---|---|---|
| Orders land where the task board is | **yes** — same objects, no crossing | no — needs a second channel (queue or internal HTTP) that does not exist | yes |
| Processes to run and configure | one | two | one |
| HTTP behaviour you get for free | routing, JSON, problem details, content-type checks | same | none — routing, JSON, 415/400 handling are yours to write and defend |
| Real fleet systems | common for a small control service | common at scale | rare, and usually only for a fixed protocol |

Chosen A. Measured: the conversion builds clean and the console commands still behave.

### Survey 2: What does `POST /api/orders` answer?

| | A: `201 Created` + `Location`, `400` problem+json (chosen) | B: `202 Accepted` + the WMS polls | C: `200` with an `{ok, data, error}` envelope |
|---|---|---|---|
| What the WMS knows at answer time | accepted and stored | only that someone read the request | accepted and stored |
| Bad input | visible at once, field by field | hidden until the poll | visible, in a custom shape |
| What WMS integration engineers expect | **yes** — standard REST | used when work is long-running work *we* queue | seen, but every vendor invents their own envelope |
| Cost here | smallest | a status state machine before there is any work to track | a custom shape to document, version, and test |

### Survey 3: Where do the order rules live?

| | A: `FleetCore`, pure types and rules (chosen) | B: inside `FleetManager`, next to the endpoint | C: a new `WmsIntegration` project |
|---|---|---|---|
| Unit-testable without HTTP | **yes** — `FleetCore.Tests` | yes, but mixed with web types | yes |
| New projects to justify | none | none | one |
| Matches the repo rule in `AGENTS.md` (decision logic separated from I/O) | yes | half — logic sits next to I/O | yes, but heavy for one record type |

Full reasons are in [record 28](../decisions/28-wms-rest-front-door.md).

---

## Decision

[Record 28](../decisions/28-wms-rest-front-door.md) settles all of it:

- **A.** The HTTP door lives in `FleetManager serve`. `FleetManager.csproj` moves to `Microsoft.NET.Sdk.Web`; the three now-redundant Configuration package refs come out so the build stays warning-free.
- **B.** The routes, the request body, the success body, the error body, and the status codes are fixed, including the duplicate rule: identical re-submit → `200`, same id with a different body → `409`.
- **C.** The order types, the validator, and the registry live in `FleetCore` as pure code. The endpoint is a thin shell.
- **D.** The shapes are documented in a **new** page, `docs/contract/wms-rest.md`, version **1**. It is not the MQTT contract; `ContractVersion.Current` stays `4`.
- **E.** The order state ladder is fixed (`received`, `in_progress`, `done`, `failed`, `cancelled`); only `received` is reachable in this lesson. The registry is in memory, with a written trigger for moving it to SQL.
- **F.** The listen URL comes from the `Kestrel` config section and is validated at startup. Routes are constants in one place, not config (the reasons are in the record).

Read the record before you write code. Every requirement below is decided there, so you never invent a product choice on your own.

---

## The lecture

Today we are going to learn about **the fleet edge and its two doors**, **the WMS order contract**, and **pure rules behind one door**.

### 1) The fleet edge: REST in, MQTT down

A **door** here means a way into the software from outside. The fleet manager has two.

- **HTTP for the business systems.** The WMS is an office system. It wants to ask a question and get an answer: "here is an order, did you take it?" That is what HTTP request/response is for.
- **MQTT for the shuttles.** A shuttle is a machine on a wireless network. Radio is flaky. Pub/sub with heartbeats and quality-of-service settings fits a moving machine better than a web call.

The spec fixes this split (line 13). It is also how real systems are built: the business layer calls at the edge, and the control layer talks to devices on its own network.

Think of a warehouse office. The WMS clerk phones the control room: "please store pallet PLT-001 in lane A01-L01." The clerk expects a person to say "taken" or "no, that lane is full." The control room then reaches the forklifts by radio. The clerk never talks to a forklift, and a forklift never phones the office.

Now, where does the phone sit? In this project: in the same room as the control desk. That is **option A** from survey 1 — the HTTP server runs **inside the same process** as `serve`.

The first change is the project file. `FleetManager` is a plain console app today:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
```

The Web SDK gives the project the ASP.NET Core shared framework — routing, JSON, problem details — with no package to add:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
```

- `Microsoft.NET.Sdk.Web` is the project type that knows how to host web endpoints. Measured: build 0 errors, and `dotnet run --project FleetManager` with no args still prints `Fleet Manager Coming Online` and exits 0.
- The Web SDK already brings the `Microsoft.Extensions.Configuration.*` assemblies. The three package refs in the csproj become redundant, and the build tells you: `NU1510 ... will not be pruned. Consider removing`. Measured: removing the three refs gives 0 warnings. A reviewer checks warnings, so keep the build clean.
- Nothing about MQTT changes. `OutputType Exe` stays; a web project still runs `Program.cs` from top to bottom.

The second change is startup. `serve` currently does this, in `src/FleetManager/Program.cs` (real code, abbreviated):

```csharp
var loop = new FleetServeLoop(bus, topics, health, evaluator, messaging.FleetManagerId,
    TimeSpan.FromMilliseconds(intervalMs), () => DateTime.UtcNow);
var loopTask = loop.RunAsync(stopping.Token);
// ... waits for Ctrl+C, then cancels the loop
```

The HTTP door is a second task next to `loopTask`. The shape to build:

```csharp
// src/FleetManager/Program.cs (shape to build — the hosting, not the handlers)
var webBuilder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,   // same folder the config reader uses
});
var webApp = webBuilder.Build();
// ... MapPost / MapGet here, using the order types and the board ...
var webTask = webApp.RunAsync(stopping.Token);
// Ctrl+C cancels both; await both in the finally block
```

- `WebApplication.CreateBuilder` builds the web host. The `WebApplicationOptions` are the important part.
- `ContentRootPath = AppContext.BaseDirectory` points the web host at the folder the binary runs from — the same folder the existing `SetBasePath(AppContext.BaseDirectory)` already reads. Measured: with this setting, running the built dll from a different working directory still finds `appsettings.json` and listens where config says.
- `webApp.RunAsync(stopping.Token)` runs Kestrel until the same `stopping` token that stops the loop fires. One Ctrl+C, two things stopping, in the same `finally` that already awaits `loopTask`.

The third change is the listen address. No port in code. An address is a deployment fact, so it belongs in `appsettings.json`:

```json
"Kestrel": {
  "Endpoints": {
    "Http": {
      "Url": "http://127.0.0.1:5080"
    }
  }
}
```

- `Kestrel` is the built-in web server. This config section is the standard way to set its address, and an operator can override it with the environment variable `Kestrel__Endpoints__Http__Url`.
- Measured: this config **overrides** the `applicationUrl` that a template's `launchSettings.json` carries. Kestrel logs `Overriding address(es) ... Binding to endpoints defined via IConfiguration`, then listens on the configured address. Config wins.
- `FleetManager` has no `launchSettings.json` (measured), so `serve` starts in the Production environment. That matters only for the framework's own error pages, which we do not rely on.
- `serve` should check the key the way it checks every other key: read it, and `throw` if it is missing. Otherwise Kestrel silently falls back to a built-in default port, which is exactly the hard-coded fact this repo forbids.

**Why not a second process (survey 1, option B)?** Because an order has to reach the task board, and the task board lives in this process. Two processes would need a third channel between them — a queue or an internal HTTP call — that does not exist and that nobody asked for. One door, one process, until a real reason to split appears.

**Where this is used later.** M7's dashboard is just another HTTP client of the same door. When the order-to-task mapping lands, the endpoint keeps its shape; only what happens after "accepted" changes.

### 2) The WMS order contract

A **contract** here means the agreed shape of what crosses a boundary. The MQTT contract (`docs/contract/v1.md` … `v4.md`) covers the shuttle side. The WMS side needs its own page, because it is a different transport, a different audience, and it changes on its own schedule. That page is `docs/contract/wms-rest.md`, version 1, and **you write it in this lesson** from the tables below. It is not an MQTT version bump: shuttle messages do not change, so `ContractVersion.Current` stays `4`.

The order form, in JSON, that the WMS sends:

```json
{
  "orderId": "ORD-1001",
  "kind": "store",
  "palletId": "PLT-001",
  "laneId": "A01-L01"
}
```

- `orderId` is the WMS's own number for the order. It is the key. The WMS must never reuse it for different work.
- `kind` is `store` or `retrieve`. A store means the pallet goes into the lane. A retrieve means the pallet comes out of it. Later lessons add the load and unload legs.
- `palletId` names the pallet the WMS cares about.
- `laneId` names the lane this order acts on. For a store, that is where the pallet goes. For a retrieve, that is where the pallet sits. When retrieve needs an outbound destination as well, a later record adds a field — decided then, not guessed now.

The machine-shaped JSON, from the existing repo: names in C# are PascalCase, and the framework serializes them camelCase on the wire, exactly like the toy in the worked example. Binding is case-insensitive.

The success body, the order as the fleet sees it:

```json
{
  "orderId": "ORD-1001",
  "kind": "store",
  "palletId": "PLT-001",
  "laneId": "A01-L01",
  "state": "received",
  "receivedAt": "2026-10-19T10:00:00Z"
}
```

- `state` is the order's place in the ladder, decided in record 28 §E: `received` → `in_progress` → `done` or `failed`, and `cancelled` from any live state. Only `received` exists in this lesson.
- `state` and `kind` are lowercase words on the wire, like `TaskStateWire` uses (`queued`, `in_progress`, `done`). Strings, pinned by tests with literals.
- `receivedAt` is when the fleet accepted the order, in UTC. On an identical re-send it stays the first value; a retry does not rewrite history.

The routes and answers, fixed now:

| Call | Success | Failure |
|---|---|---|
| `POST /api/orders` | `201 Created`, `Location: /api/orders/{orderId}`, order body | `400` validation; `409` duplicate id with a different body; `415` wrong content type |
| `GET /api/orders/{orderId}` | `200`, order body | `404` unknown order |
| re-send, identical body | `200`, the existing order body | — |

And the bad-input answers are the framework's standard shape, not one we invent. Measured output from the toy:

```text
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"color":["Color is required."]}}
```

- `application/problem+json` is the standard error body for HTTP APIs (RFC 9457). ASP.NET Core produces it from one call: `Results.ValidationProblem(errors)`.
- `errors` is a map of field name to a list of messages. A WMS client can show a clerk exactly which field is wrong.
- The `404` and `409` answers use the same family (`Results.Problem(...)`) with our own `title`.
- Malformed JSON and a wrong content type never reach our code: the framework answers `400` (measured: empty body in Production, a stack trace only in Development) and `415`. Good — that is bad input handled before our rules even run.

**Why `201`, and why a separate `200` for a retry?** The network loses answers. A WMS that times out on a `201` will send the same order again. If it sent different work with a reused id, that is a real mistake and `409` says so. So:

- First time → `201` and a `Location` header.
- Same id, same four field values → `200` with the stored order. The WMS learns "already done", not "done twice".
- Same id, different field values → `409`. Something is wrong upstream, and the fleet refuses to guess.

**Where this is used later.** The next slice reads orders from this registry and turns accepted ones into tasks. The status push slices reuse the same order body as the payload. The dashboard can read `GET /api/orders/{id}` without any new shape.

### 3) Pure rules behind one door

Two jobs guard the door: a **validator** that decides whether the form is filled in correctly, and a **registry** that is the one place orders live.

The validator is **pure**: no HTTP, no database, no clock, no logging. Same input, same answer, every time. That makes it testable in `FleetCore.Tests` with no server and no broker.

```csharp
// src/FleetCore/WmsOrderValidator.cs (shape to build)
public static Dictionary<string, string[]> Validate(SubmitOrderRequest request)
{
    var errors = new Dictionary<string, string[]>();

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
```

- Every rule runs. The method does not stop at the first failure, so the WMS clerk sees all problems in one answer.
- `request.Kind` is nullable, so a missing `kind` is "unknown", and the same error message covers both "missing" and "not a real kind". One rule, one message.
- The dictionary keys (`orderId`, `kind`, …) are the wire names of the fields. They become the `errors` map in the problem+json body.
- No size limits, no patterns, no trimming. Rules that need a size or a pattern come later with a config value behind them.

The registry is small and boring:

```csharp
// src/FleetCore/WmsOrderBoard.cs (shape to build)
public SubmitResult Submit(SubmitOrderRequest request, DateTime now)
{
    lock (_gate)
    {
        if (_orders.TryGetValue(request.OrderId!, out var existing))
        {
            if (existing == OrderFrom(request, existing.ReceivedAt))   // all four values equal
                return new SubmitResult(SubmitOutcome.AlreadyKnown, existing);
            return new SubmitResult(SubmitOutcome.Conflict, existing);
        }

        var order = OrderFrom(request, now);
        _orders[order.OrderId] = order;
        return new SubmitResult(SubmitOutcome.Created, order);
    }
}
```

- **One lock, inside the board.** Two WMS requests can arrive at the same instant. Without the lock, two threads could both see "not present" and both create the order. The endpoint never locks; the board does, and that is the only place that touches the dictionary.
- **`now` comes in as an argument.** The board never calls `DateTime.UtcNow`; the endpoint passes it. That is what keeps the registry pure enough to unit test with fixed times.
- **The duplicate rule lives here, not in the endpoint.** Identical values → `AlreadyKnown`. Same id, different values → `Conflict`. Read the existing order's `ReceivedAt` for the identical case, so the retry does not rewrite history.
- **`SubmitOutcome` is the answer**: `Created`, `AlreadyKnown`, `Conflict`. The endpoint maps that to status codes and does nothing else clever.

The endpoint is then a thin shell. In words: bind the body, validate, hand to the board, map the outcome.

| After validation | Board outcome | HTTP answer |
|---|---|---|
| errors found | not called | `400` with `Results.ValidationProblem(errors)` |
| no errors | `Created` | `201` with `Results.Created(WmsRoutes.Order(orderId), order)` |
| no errors | `AlreadyKnown` | `200` with the order |
| no errors | `Conflict` | `409` with `Results.Problem(statusCode: 409, title: "Order id already used")` |
| — | `GET`, unknown id | `404` with `Results.Problem(statusCode: 404, title: "Unknown order")` |

- **Validation runs before the board is touched.** A bad order changes nothing, anywhere. That is the first half of M6's done-when, "a bad order does not corrupt fleet state".
- `Results.Created` and `Results.ValidationProblem` are the two helpers you saw measured in the toy. They produce the `Location` header and the problem+json body with no hand-built JSON.
- The registry is in memory. If `serve` restarts, orders are gone. That is written down in record 28 §E with its trigger: when orders start creating tasks, the order row moves next to the task row in SQL. Until then, persistence would be storage for nothing.

**The state ladder, and why only `received` is reachable.** The board stores the order with `state: "received"`. Nothing in this lesson moves it forward, because nothing can: there is no order-to-task mapping yet. The ladder is fixed now so the WMS-facing shape does not shift under the WMS later.

**Where this is used later.** The next slice takes a `received` order and calls the task board, then moves the order to `in_progress` and `done`/`failed` as the task changes. The `cancelled` state lands with the cancel route, after that.

---

## Recap in plain words

- The fleet manager gets a second door: HTTP for the WMS, alongside MQTT for shuttles. The door lives inside `serve`, in one process, with its address in config.
- `FleetManager` becomes a Web SDK project. Kestrel runs as a second task next to the serve loop and stops with the same Ctrl+C.
- The WMS contract is fixed and written down in `docs/contract/wms-rest.md` version 1: `POST /api/orders`, `GET /api/orders/{id}`, `201`/`200`/`400`/`404`/`409`, and the problem+json error shape.
- Validation is pure and reports every problem at once. It runs before anything is stored.
- One small board holds the orders, owns the one lock, and decides `Created` vs `AlreadyKnown` vs `Conflict`. The endpoint only maps outcomes to status codes.

---

## Worked example (toy — NOT the assignment)

This toy shows one mechanism only: **how a pure check becomes real HTTP answers**. It has no orders, no registry, no lock, no MQTT, and no fleet manager. The assignment is the real thing.

Create it and run it:

```bash
cd /tmp/opencode
rm -rf widget-toy
dotnet new web -o widget-toy
# replace Program.cs with the listing below, and add the Kestrel block to appsettings.json
```

`/tmp/opencode/widget-toy/Program.cs` (exactly as I ran it):

```csharp
// TOY - not the assignment. No orders, no registry, no MQTT, no fleet manager.
// It shows one mechanism: a pure check turned into HTTP answers by a minimal API.

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,   // same folder the existing config reader uses
});
var app = builder.Build();

app.MapPost("/api/widgets", (WidgetRequest request) =>
{
    var errors = WidgetValidator.Check(request);
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);          // 400, application/problem+json

    return Results.Created($"/api/widgets/{request.Name}", request);  // 201 + Location
});

app.Run();

public sealed record WidgetRequest(string? Name, string? Color);

public static class WidgetValidator
{
    // Pure: no HTTP, no database, no clock. Same input, same answer.
    public static Dictionary<string, string[]> Check(WidgetRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
            errors["name"] = ["Name is required."];
        if (string.IsNullOrWhiteSpace(request.Color))
            errors["color"] = ["Color is required."];
        return errors;
    }
}
```

Add this block to `/tmp/opencode/widget-toy/appsettings.json` (keep the template's `Logging` and `AllowedHosts` lines):

```json
"Kestrel": {
  "Endpoints": {
    "Http": {
      "Url": "http://127.0.0.1:5088"
    }
  }
}
```

Run it in one terminal:

```bash
dotnet run --project /tmp/opencode/widget-toy
```

Server output (from my run; the launch-settings port is whatever the template generated — mine was 5279, and yours will differ):

```text
Using launch settings from /tmp/opencode/widget-toy/Properties/launchSettings.json...
Building...
warn: Microsoft.AspNetCore.Server.Kestrel[0]
      Overriding address(es) 'http://localhost:5279'. Binding to endpoints defined via IConfiguration and/or UseKestrel() instead.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://127.0.0.1:5088
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Development
info: Microsoft.Hosting.Lifetime[0]
      Content root path: /tmp/opencode/widget-toy/bin/Debug/net10.0/
```

In a second terminal, three requests:

```bash
curl -s -i -X POST http://127.0.0.1:5088/api/widgets -H "Content-Type: application/json" -d '{"name":"w-1","color":"red"}'
curl -s -i -X POST http://127.0.0.1:5088/api/widgets -H "Content-Type: application/json" -d '{"name":"w-2"}'
curl -s -o /dev/null -w "%{http_code}\n" -X POST http://127.0.0.1:5088/api/widgets -d 'name=w-3'
```

Output (mine, verbatim):

```text
HTTP/1.1 201 Created
Content-Type: application/json; charset=utf-8
Date: Mon, 19 Oct 2026 15:30:33 GMT
Server: Kestrel
Location: /api/widgets/w-1
Transfer-Encoding: chunked

{"name":"w-1","color":"red"}---
HTTP/1.1 400 Bad Request
Content-Type: application/problem+json
Date: Mon, 19 Oct 2026 15:30:33 GMT
Server: Kestrel
Transfer-Encoding: chunked

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"color":["Color is required."]}}---
415
```

Stop the server with Ctrl+C in the first terminal.

Three things to notice:

- The `201` echoes the same shape the request sent, camelCase: the framework serialized the record for you.
- The `400` is `application/problem+json` with `errors` keyed by field name. One call produced it.
- `415` came from the framework because the third request sent no JSON content type. No code of ours ran.

---

## Say it out loud

Answer these out loud, the way you would in an interview. No notes.

1. The HTTP door and the MQTT serve loop share one process. What would you have to build if the door lived in a second process, and what does the current split make impossible to do by accident?
2. A WMS retries `POST /api/orders` after a timeout. Trace what the endpoint returns for an identical body, and for the same `orderId` with a changed lane. Why is "identical body" the test that separates a retry from a different order?
3. `WmsOrderValidator` is pure and takes no lock; `WmsOrderBoard.Submit` takes the lock and takes `now` as an argument. What breaks if validation runs after `Submit`, and what breaks if `Submit` calls `DateTime.UtcNow` itself?
4. `/api/orders` is a constant in code, but the listen address is in config. Justify both halves in one sentence each.
5. `GET /api/orders/{id}` for an unknown id returns `404`. A `POST` with an unknown id creates the order. Same id, two different meanings — why is that not a contradiction?

---

## The assignment

Build the WMS front door, per [record 28](../decisions/28-wms-rest-front-door.md). Language: C#. The MQTT contract does **not** change: `ContractVersion.Current` stays `4`.

**First, the gate:** make `ShuttleEndpointTests.Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing` pass again (see the gate section). Do that before the new work, in its own commit.

### Requirements

1. **`src/FleetManager/FleetManager.csproj`** — change `Sdk` to `Microsoft.NET.Sdk.Web`. Remove the three `Microsoft.Extensions.Configuration.*` package references so the build is warning-free. Nothing else in the file changes; the console branches (`serve`, `move`, `recover`, usage) keep working.

2. **`src/FleetCore/`** — the contract types and the pure rules. No HTTP types in this project.
   - `WmsOrderKinds` with `Store = "store"`, `Retrieve = "retrieve"`, and an `IsKnown(string?)` check. Mirror `AlarmCodes`.
   - `WmsOrderStates` with `Received`, `InProgress`, `Done`, `Failed`, `Cancelled` (wire names `received`, `in_progress`, …). Only `Received` is used this lesson; the rest are the fixed ladder from record 28 §E.
   - `WmsRoutes` with `Orders = "/api/orders"` and `Order(orderId)` returning `"/api/orders/{orderId}"`. Constants in one place, like `MessageTypes`. Pin them with literal tests.
   - `SubmitOrderRequest` — a positional record with four nullable strings: `OrderId`, `Kind`, `PalletId`, `LaneId`. This is what the endpoint binds.
   - `WmsOrder` — the stored order: the four values plus `State` and `ReceivedAt`.
   - `WmsOrderValidator.Validate(SubmitOrderRequest)` → `Dictionary<string, string[]>`, every rule evaluated, keys are the wire field names. Exact messages are in record 28 §B.
   - `WmsOrderBoard` — `Submit(SubmitOrderRequest, DateTime now)` → `SubmitResult(SubmitOutcome Outcome, WmsOrder Order)`, and `TryGet(string orderId, out WmsOrder? order)`. One private lock inside; no clocks inside; identical values → `AlreadyKnown` with the **original** `ReceivedAt`; same id, different values → `Conflict`.

3. **`src/FleetManager/`** — a thin endpoint and the hosting.
   - Read and validate `Kestrel:Endpoints:Http:Url` in `RunServeAsync` the way the other keys are validated; throw if missing or blank.
   - Build the web app with `WebApplicationOptions { Args = args, ContentRootPath = AppContext.BaseDirectory }`, map the routes, and run it as a second task next to the serve loop. Ctrl+C must stop both and await both. The loop's behaviour must not change.
   - `MapPost(WmsRoutes.Orders, ...)`: validate → on errors `Results.ValidationProblem(errors)`; else `Submit` → `Created` → `Results.Created(WmsRoutes.Order(orderId), order)`; `AlreadyKnown` → `Results.Ok(order)`; `Conflict` → `Results.Problem(statusCode: 409, title: "Order id already used")`.
   - `MapGet(WmsRoutes.Order("{orderId}"), ...)`: `TryGet` → `Results.Ok(order)` or `Results.Problem(statusCode: 404, title: "Unknown order")`.
   - The endpoint contains no validation rules and no locking. It reads keys, binds, and maps outcomes to results.

4. **`src/FleetManager/appsettings.json`** — add the `Kestrel` section with the address (record 28 §F uses `http://127.0.0.1:5080`). No port literal anywhere in code.

5. **`docs/contract/wms-rest.md`** — new page, header `Version: 1`, containing: the request body, the success body, the problem+json error shape, the route and status-code table, the validation rules table, and one sentence on how it changes (decision record + version bump for this page, never a bump of the MQTT contract). The shapes come from record 28 §B — do not invent fields.

6. **Tests** (pure, no `Integration` trait):
   - `src/FleetCore.Tests/WmsOrderValidatorTests.cs`: a valid order has no errors; `kind` of `"move"` gives only a `kind` error; blank `orderId`, `palletId`, and `laneId` give all three errors in one call; a missing `kind` is an unknown kind.
   - `src/FleetCore.Tests/WmsOrderBoardTests.cs`: first submit → `Created` with `State == "received"`; identical second submit → `AlreadyKnown` with the **same** `ReceivedAt`; same id with a different `laneId` → `Conflict`; unknown id → `TryGet` false.
   - Pin wire names and routes with literals, never constant-vs-itself: `Assert.Equal("store", WmsOrderKinds.Store);`, `Assert.Equal("retrieve", WmsOrderKinds.Retrieve);`, `Assert.Equal("received", WmsOrderStates.Received);`, `Assert.Equal("/api/orders", WmsRoutes.Orders);`, `Assert.Equal("/api/orders/ORD-1", WmsRoutes.Order("ORD-1"));`. A rename must go red.

### Acceptance criteria

```bash
# 1. clean build, no warnings
cd src && dotnet build

# 2. the carry-over gate is repaired and the pure suite is green
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"   # Failed: 0
# measured at HEAD 2026-10-19: 145 passed / 1 failed / 146; after the gate fix: 146, plus your new tests

# 3. the Web SDK conversion and the endpoint pieces exist
grep -q "Microsoft.NET.Sdk.Web" src/FleetManager/FleetManager.csproj
! grep -q "Microsoft.Extensions.Configuration" src/FleetManager/FleetManager.csproj
grep -rq "MapPost" src/FleetManager
grep -rq "Results.ValidationProblem" src/FleetManager
grep -rq "Results.Created" src/FleetManager

# 4. the pure rules live in FleetCore, and the board owns the lock
grep -rq "WmsOrderValidator" src/FleetCore
grep -rq "class WmsOrderBoard" src/FleetCore
grep -q "lock" src/FleetCore/WmsOrderBoard.cs
grep -rq '"/api/orders"' src/FleetCore

# 5. the address is configuration, and it is checked
grep -q "Kestrel" src/FleetManager/appsettings.json
grep -q "Kestrel:Endpoints:Http:Url" src/FleetManager/Program.cs

# 6. the WMS doc exists; the MQTT contract did not move
test -f docs/contract/wms-rest.md
grep -q "Version: 1" docs/contract/wms-rest.md
grep -q "Current = 4" src/Contracts/MessageTypes.cs
git diff --stat docs/contract/v1.md docs/contract/v2.md docs/contract/v3.md docs/contract/v4.md   # prints nothing

# 7. wire names pinned with literals
grep -rq '"store"' src/FleetCore.Tests
grep -rq '"retrieve"' src/FleetCore.Tests
grep -rq '"received"' src/FleetCore.Tests
grep -rq '"/api/orders"' src/FleetCore.Tests

# 8. the CLI paths are unchanged
dotnet run --project src/FleetManager     # rc 0, prints "Fleet Manager Coming Online"
```

Behaviour over HTTP, with the broker up and `serve` running (the stretch goal below shows the setup). Keep the output in your review notes:

- `POST` an order with a missing `laneId` → `400`; then `GET /api/orders/ORD-X` → `404`. The rejected order was **not** stored: validation ran before the board.
- `POST` the same valid order twice → `201` then `200`, and `receivedAt` is the same value both times.
- `POST` the same `orderId` with a changed `laneId` → `409`.

Mutation check before you call it done (each in a scratch copy, never in the workspace):

- (a) Make `WmsOrderValidator.Validate` `return errors;` as soon as the first rule fails (early return) → the all-errors-at-once test must go red.
- (b) Change `WmsRoutes.Orders` to `"/api/order"` → the route literal pin must go red.
- (c) Make an identical re-submit return `Conflict` → the existing-order test must go red.
- (d) Remove the `lock` in `WmsOrderBoard.Submit` and hammer it from two threads. This one may not fail every run; that is the point. Say in your notes what the race would do to the WMS, and why no deterministic unit test catches it easily.

If a mutation survives, the test is decoration; fix the test.

### Stretch goal

Run the door for real, with the broker up, and keep the output in your review notes:

```bash
docker compose -f docker/compose.yml up -d
# terminal 1
dotnet run --project src/FleetManager -- serve
# terminal 2
curl -i -X POST http://127.0.0.1:5080/api/orders -H "Content-Type: application/json" \
  -d '{"orderId":"ORD-1001","kind":"store","palletId":"PLT-001","laneId":"A01-L01"}'
curl -i http://127.0.0.1:5080/api/orders/ORD-1001
docker compose -f docker/compose.yml down
```

Expect `201` then `200`, and `serve` still publishing alarms for shuttles while the order sits in the registry.

**Not in this assignment:** order-to-task mapping, the runtime dispatcher, database wiring, pushing status to the WMS, cancelling orders, load/unload MQTT commands, order persistence, the dashboard, and any edit to `src/Contracts` or `docs/contract/v4.md`.
