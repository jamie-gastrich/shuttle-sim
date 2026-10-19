# Decision Record 28: The WMS REST front door — hosting, order shapes, and where the rules live

**Date:** 2026-10-19
**Milestone:** M6 (WMS integration) — lesson 20: the WMS front door
**Status:** Accepted. Append-only. Builds on [record 22](22-serve-loop-and-alarm-publish.md) (the `serve` loop the door joins), [record 11](11-task-lifecycle-and-queue.md) §8 (a pure, in-memory registry with SQL as a later layer), [record 13](13-fleet-state-persistence.md) (fleet state persistence in SQL — the model for moving the registry later), [record 27](27-recovery-wiring-and-qos-families.md) (recovery wiring), [record 14](14-db-test-gate-and-config.md) (config keys and the Integration test gate), [record 02](02-message-transport.md) (MQTT for the shuttle wire, REST for business systems).
**Wire contract:** **no change.** The MQTT contract stays `v4`; `ContractVersion.Current` stays `4` and `docs/contract/v4.md` is untouched. The WMS side gets its **own** page, `docs/contract/wms-rest.md` version 1 (decision D).
**Replaced by:** later M6 records that add order-to-task mapping, status push to the WMS, or cancellation; later records that move orders into SQL. Append-only.

---

## 1. What we are deciding

M6's done-when is *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."* That is three jobs: take an order in, run it through the fleet, tell the WMS what happened. Nothing can submit work to a running fleet manager today — `serve` only observes status and publishes alarms.

This record decides the **front door**: everything about how an order enters the system, and nothing about how it is executed. It decides:

- **A.** Where the HTTP listener lives (same process as `serve`, or its own).
- **B.** The order request/response shapes, the routes, the HTTP status codes, and the duplicate-order rule.
- **C.** Which project holds the order types and rules.
- **D.** Where the WMS shapes are documented, and how the MQTT contract stays out of it.
- **E.** The order state ladder and how long an in-memory registry is acceptable.
- **F.** What is configuration and what is a constant.

Everything below follows the spec's split (line 13): **REST between the WMS and the fleet manager, MQTT between the fleet manager and the shuttles.** The WMS never talks to a shuttle. A shuttle never talks to the WMS.

---

## 2. Decision A: the HTTP door lives inside `FleetManager serve`

Options:

- **A: The Web SDK minimal API hosted inside the `FleetManager` process, started by `serve` alongside the serve loop.** (chosen)
- **B: A second process, `FleetManager.Api`, hosting the REST door.**
- **C: `HttpListener` inside `serve`, hand-rolled.**

**Decision A.** `FleetManager.csproj` becomes `Microsoft.NET.Sdk.Web` and `RunServeAsync` starts Kestrel as a second task next to `FleetServeLoop`, sharing one cancellation token and one shutdown path. Three reasons:

1. **An accepted order must reach the task board, and the task board lives in this process.** Option B would need a third channel between the two processes — a queue, a database table, or an internal HTTP call — that does not exist. That is a new failure mode (what if the queue is down?) bought for nothing.
2. **The Web SDK is already installed and already present in the shared framework.** Measured: `dotnet build` after the SDK switch is 0 errors on its own, and 0 warnings once the three now-redundant `Microsoft.Extensions.Configuration.*` package references are removed (NU1510). No new package, no new tool.
3. **One process to run, one config file, one Ctrl+C.** An operator running a warehouse cell does not want two things to supervise for a service this small.

Rejected C: `HttpListener` means writing routing, JSON binding, content-type checking, and problem-details formatting by hand, all of which the framework hands us and all of which a reviewer will ask us to defend. Measured on the toy: a wrong content type returns `415` and malformed JSON returns `400` **without our code running at all**.

Rejected B is not wrong at scale. It is wrong now, because the thing it buys (independent scaling and deployment of the door) is worth nothing until the door has traffic. A control room of a handful of cells runs one process.

**What would change this:** if the REST door ever needs to stay up while the control loop restarts, or if the door needs its own scaling, split it out then and give the split its own record.

**Environment default.** `FleetManager` has no `launchSettings.json` (measured), so `serve` starts in the Production environment. `ASPNETCORE_ENVIRONMENT` may still be set to `Development` when a developer wants the framework's detailed error pages, but nothing in our code depends on which one is active.

---

## 3. Decision B: routes, shapes, status codes, and the duplicate rule

### Routes

- `POST /api/orders` — submit an order.
- `GET /api/orders/{orderId}` — read one order.

Routes are **constants in `FleetCore/WmsRoutes.cs`**, not configuration.

**Why not configuration:** a route is part of the contract the WMS writes against; moving it silently breaks every client. Config makes it look tunable, which invites someone to tune it in production and break the WMS with no code change and no review. The listen **address** is config (§6) because it is a deployment fact; the path is not. This mirrors `MessageTypes`, whose topic path segments are constants.

### Request shape

```json
{ "orderId": "ORD-1001", "kind": "store", "palletId": "PLT-001", "laneId": "A01-L01" }
```

- Bound to `SubmitOrderRequest(string? OrderId, string? Kind, string? PalletId, string? LaneId)`. Every field is nullable so a missing field is a validation error, not a bind failure — the WMS gets a field-by-field answer instead of a generic 400.
- Property names are PascalCase in C#; the framework serializes camelCase on the wire, the same convention the existing contract messages already use. Binding is case-insensitive (measured in the toy).
- `kind` must be `store` or `retrieve` (`WmsOrderKinds`). No other kind exists and none is reserved.
- `palletId` and `laneId` are opaque strings to the fleet at this stage. This lesson does not validate them against a scene; when locations become a modeled resource, that check lands with the resource and its own record.
- **No outbound lane field yet.** A retrieve eventually needs a destination as well as a source. Adding it now would be a guess; the wire shape is fixed when the retrieve execution slice needs it, with a version bump of this page.

### Success shape

```json
{ "orderId": "ORD-1001", "kind": "store", "palletId": "PLT-001", "laneId": "A01-L01",
  "state": "received", "receivedAt": "2026-10-19T10:00:00Z" }
```

- `state` is the ladder from §5, as a lowercase string, exactly like `TaskStateWire`. Wire strings are pinned by tests with literals.
- `receivedAt` is the fleet's acceptance time in UTC. On a duplicate it stays the **first** value; a retry does not rewrite history.

### Status codes

| Situation | Status | Body |
|---|---|---|
| Order stored, first time | `201 Created` + `Location: /api/orders/{orderId}` | the order |
| Same `orderId`, all four field values identical | `200 OK` | the stored order (original `receivedAt`) |
| Same `orderId`, any field value different | `409 Conflict` | problem+json, title `Order id already used` |
| Validation errors | `400 Bad Request` | problem+json from `Results.ValidationProblem` |
| `GET` unknown id | `404 Not Found` | problem+json, title `Unknown order` |
| Wrong content type | `415` | framework-generated |
| Malformed JSON | `400` | framework-generated |

**Why `201` and not `202`:** option B (202 + poll) is the right answer when the fleet queues work and tells the WMS "I will get to it". This slice does not queue anything — the order is stored synchronously and the answer is known before the response is written. `201` says exactly that. If order intake later becomes asynchronous (a backlog, a rate limit), revisit `202` then.

**Why an envelope was rejected (option C, `{ok, data, error}`):** every vendor invents one, every one is different, and it hides the real status code inside a body field. REST already has status codes. Reviewers and WMS integration engineers read them without a decoder ring.

**Validation rules** (all evaluated, all returned at once):

| Field | Rule | Message |
|---|---|---|
| `orderId` | not null or whitespace | `orderId is required.` |
| `kind` | `store` or `retrieve` | `kind must be 'store' or 'retrieve'.` |
| `palletId` | not null or whitespace | `palletId is required.` |
| `laneId` | not null or whitespace | `laneId is required.` |

The `errors` dictionary is keyed by the **wire** field name, which is what the problem+json `errors` map carries.

### The duplicate rule

At-least-once delivery applies to HTTP too: a WMS whose client times out on a `201` will re-send. There are exactly two kinds of re-send, and they deserve different answers:

- **All four values identical → `200` with the stored order.** This is a retry. The work is already accepted; running it twice must not happen. Answering `200` (not `201`) lets the WMS tell "new" from "already had it" without inspecting anything else.
- **Same `orderId`, any value different → `409`.** Reusing an id for different work is a bug upstream, and the fleet refuses to guess whether the WMS meant to change the order or made a mistake.

**What would change this:** a WMS that legitimately needs to amend a submitted order. That is an `PATCH`/`PUT` route with its own state rules, a deliberate new decision, not a change to this one. The refusal here (409) is what makes "no order id is ever silently reused" true.

---

## 4. Decision C: the order types and rules live in `FleetCore`

Options:

- **A: `FleetCore` — pure types, validator, and registry, with the endpoint as a thin shell.** (chosen)
- **B: inside `FleetManager`, next to the endpoint.**
- **C: a new `WmsIntegration` project.**

**Decision C (A).** `FleetCore` already holds `FleetTask`, `TaskBoard`, `TaskState`, and `TaskStateWire`: domain types and rules with no broker, no database, and no HTTP. The order types are the same kind of thing. A new project (option C) is a build-file cost and an assembly boundary to justify for four records and two small classes; `FleetCore` is already where this code belongs by the repo's own rule in `AGENTS.md` ("decision logic is separated from input and output where practical") and by the precedent in record 11 §8.

**The split, stated as a rule the reviewer can check:**

- `FleetCore` may not reference any web type. No `Results`, no `HttpContext`, no `IResult`. It returns plain values: a `Dictionary<string,string[]>` from the validator and a `SubmitResult` from the board. That is what makes both unit-testable with no server.
- `FleetManager` knows about HTTP. It maps `SubmitOutcome` → status code and nothing else. If the endpoint ever contains a validation rule or a `lock`, the split has been violated.

Rejected B is not a disaster, just worse: the rules become reachable only through a web host, and the pure tests in `FleetCore.Tests` cannot exist.

### The lock

`WmsOrderBoard` owns the only lock. Two requests can arrive on different threads; without a lock, both can read "not present" and both can create the order. The `TryGetValue` and the `Add` must be in the same critical section, and the endpoint never locks. This is the same "one owner of the mutable state" shape as `TaskBoard`.

### `now` as an argument

`WmsOrderBoard.Submit(request, DateTime now)` takes the time from the caller. Time is an input, like the request. A board that reads the clock itself is not reproducible in a test, and the duplicate test ("the retry keeps the original `receivedAt`") needs two different `now` values to prove anything.

---

## 5. Decision D: the WMS contract is its own page, `docs/contract/wms-rest.md` version 1

Options:

- **A: a new `docs/contract/wms-rest.md`, version 1, separate from the MQTT versions.** (chosen)
- **B: fold the HTTP routes into the next MQTT contract version (`v5.md`).**
- **C: document the WMS API only in the lesson and in code comments.**

**Decision D (A).** The `docs/contract/` pages are the source of truth for what crosses a boundary, and the WMS boundary is real. But it is a **different transport, a different audience, and a different change cadence** from the shuttle wire. Folding REST routes into an MQTT version bump (option B) would tie "we changed the order body" to "we changed something the shuttles must be reflashed for" — two independent decisions welded together. Separate pages mean a `wms-rest.md` bump never touches `ContractVersion.Current`, and an MQTT bump never forces a WMS change.

Rejected C: an undocumented wire shape is exactly the thing a versioned contract page exists to prevent, and M6's whole point is a WMS we can point at a document.

**The page must contain:** the request body, the success body, the problem+json error shape, the route and status-code table, and the validation table from §3, plus one sentence saying that changes to this page need a decision record and a page version bump, and that it is **not** the MQTT contract.

**Measured now:** `docs/contract/` contains only `v1.md`…`v4.md`; there is no `wms-rest.md` yet. This lesson's assignment has the owner create it. `ContractVersion.Current = 4` and `docs/contract/v1.md`…`v4.md` are unchanged (a `git diff --stat` over those four files prints nothing).

---

## 6. Decision E: the order state ladder, and an in-memory registry with a trigger for moving to SQL

**The ladder**, fixed now so the WMS-facing shape does not shift under the WMS later:

`received` → `in_progress` → `done` | `failed`; and `cancelled` reachable from any live state.

- Only `received` is reachable in this lesson, because nothing can execute an order yet. The other four strings exist as constants so the wire vocabulary is fixed and tests can pin them with literals.
- `failed` and `cancelled` are separate on purpose: a fault is the fleet's problem, a cancellation is the WMS's request. A dashboard and an operator will want to tell them apart.

**The registry is in memory** — a `Dictionary<string,WmsOrder>` with one lock. If `serve` restarts, orders are gone.

Options:

- **A: in-memory registry now, SQL when orders become tasks.** (chosen)
- **B: an `orders` table now, alongside the tasks table.**
- **C: no registry — write straight to the task board.**

Rejected B: persistence has a real cost (schema, migrations, a connection-resilience story) and its whole value is surviving a restart or being read by another process. Neither is true yet: nothing reads orders across a restart, and the endpoint is the only reader. Rejected C: the door would either have to run the task machinery, which is not written, or drop orders on the floor.

**The trigger for moving to SQL, written down so it is not forgotten:** when the order-to-task mapping lands (the next slice), the order row is persisted **beside the task row, in the same transaction**, because at that moment "the order was accepted" and "a task exists" become one fact that must not split. That is the same reasoning record 13 used to move the task board to SQL, and record 11 §8 used to keep the queue in memory until SQL was needed. Until then, an in-memory dictionary is the honest amount of storage.

---

## 7. Decision F: configuration vs constants

| Thing | Home | Why |
|---|---|---|
| Listen URL | config, `Kestrel:Endpoints:Http:Url` | deployment fact; a cell's address differs from a laptop's |
| Routes (`/api/orders`, `/api/orders/{id}`) | constants in `WmsRoutes` | part of the client-facing contract; not tunable |
| Order kinds (`store`, `retrieve`) | constants in `WmsOrderKinds` | wire vocabulary |
| Order states | constants in `WmsOrderStates` | wire vocabulary |
| Field names and error messages | code, pinned by tests | wire vocabulary |

**The listen URL must be read and checked at startup.** Kestrel reads the same section itself, but if the key is absent Kestrel falls back to a built-in default port — a hard-coded fact this repo forbids, and a silent one. So `RunServeAsync` reads `Kestrel:Endpoints:Http:Url` the way it reads every other key, and throws if it is missing or blank. Measured: the section overrides a template's `launchSettings.json` `applicationUrl` (Kestrel logs `Overriding address(es) ...`), and a blank value is a startup error, not a surprise port.

**No new timeout or retry key in this slice.** The front door answers synchronously; there is nothing to time out. A WMS *status push* will need a timeout, and it gets its own record.

---

## 8. Scope and limits

- **No order-to-task mapping, no runtime dispatcher, no database wiring.** Accepted orders sit in the registry. The next slice maps them to `FleetTask`s.
- **No status push to the WMS.** How status travels back (push vs poll, and what happens when the WMS is slow or down) is the next record's decision.
- **No cancel route.** `cancelled` exists in the ladder; the route waits for the cancel slice.
- **No load/unload MQTT commands.** Those are wire changes to the shuttle contract, almost certainly a `v5`, decided when the retrieve execution slice needs them.
- **No persistence of orders.**
- **No dashboard.** M7.
- **No change to `src/Contracts`, `docs/contract/v1.md`…`v4.md`, or `ContractVersion.Current`.** It stays `4`.
- **No change to the `serve` loop's behaviour, the `move` path, or the `recover` path.** The web host is a second task in the same method with the same cancellation token.

---

## 9. Verification (measured)

| Claim | How | Result |
|---|---|---|
| SDK | `dotnet --version` | `10.0.112` |
| Baseline suite at HEAD | clean copy of `src` in `/tmp/opencode/l20base`; `dotnet build`, `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0; **145 passed / 1 failed / 146** (FleetCore 22, Contracts 8, TrafficCore 6, ShuttleCore 31, Messaging 10, MqttMessaging 3, FleetManager 56, ShuttleSim 9/10) |
| The one failure is real, not flaky | clean copy `dotnet test ShuttleSim.Tests` ×3 | failed 3 of 3: `Recovery_command_for_a_healthy_shuttle_is_accepted_and_changes_nothing` (the missing healthy branch in `ShuttleEndpoint.cs:121-135`) |
| No HTTP code exists yet | `grep -rn "WebApplication\|HttpClient\|Kestrel\|MapGet\|MapPost\|AddControllers\|HttpListener" src --include=*.cs --include=*.csproj` | no matches |
| No WMS contract page exists | `ls docs/contract` | `v1.md` … `v4.md` only |
| The minimal-API template exists | `dotnet new list` | "ASP.NET Core Empty" / `web` |
| The SDK switch builds | copy in `/tmp/opencode/l20host`, `Sdk="Microsoft.NET.Sdk.Web"`, `dotnet build` | 0 errors; 6 `NU1510` warnings |
| Removing the redundant refs builds clean | same copy minus the three Configuration package refs | 0 warnings, 0 errors |
| Console CLI survives the switch | `dotnet run --project FleetManager` (no args) / bad args | rc 0 `Fleet Manager Coming Online`; rc 2 usage |
| Config overrides the launch profile | toy in `/tmp/opencode/widget-toy`, `dotnet run`, read server log | `Overriding address(es) 'http://localhost:5279' ... Now listening on: http://127.0.0.1:5088` |
| The shapes map to real HTTP | three `curl` calls to the toy | `201` + `Location: /api/widgets/w-1` + camelCase; `400 application/problem+json` with `errors`; `415` |
| Content root at the binary works from any cwd | `ContentRootPath = AppContext.BaseDirectory`, built dll run from `/tmp` | `Content root path: .../bin/Debug/net10.0/`, still `5088`, POST → `201` |
| Environment default | `--no-launch-profile`, no `ASPNETCORE_ENVIRONMENT` | `Hosting environment: Production` |
| HTTP-level test package exists | `dotnet package search Microsoft.AspNetCore.Mvc.Testing --take 1` | `10.0.13` |

Not yet verified: the composition — Kestrel and the MQTT serve loop running together under one `serve` and shutting down cleanly — and the implementation itself. The owner builds the door per lesson 20; this record states the design. The `Recovery_command_for_a_healthy_shuttle...` red test at HEAD is a **blocker**, not a design question: lesson 20's assignment repairs it first.

---

## 10. What this unblocks

- Lesson 20's assignment has no unguided product choices: routes, bodies, status codes, the duplicate rule, the project split, the storage limit, and the config boundary are all fixed here.
- The next M6 slice can map an accepted, well-formed `received` order onto the existing `TaskBoard` without inventing a request shape.
- The WMS status-push slice has a fixed order body to send and a fixed `state` vocabulary to send in it.
- M7's dashboard is another client of the same routes and the same bodies.
- The WMS has a document to write against, `docs/contract/wms-rest.md` version 1, that never moves the shuttle wire.

---

## 11. Erratum: the §3 route/status table

**Date:** 2026-10-19. **Appended, not a rewrite.** No line above this one changed. This corrects the `### Status codes` table inside [§3 Decision B](#3-decision-b-routes-shapes-status-codes-and-the-duplicate-rule), which review 20 flagged as incomplete.

**Why an erratum and not a new record.** AGENTS.md says a decision entry is never rewritten, and that a *replaced* decision gets a new entry. Nothing in the design was replaced here: the routes, the bodies, the status codes, and the duplicate rule all stand exactly as decided above. Only the §3 table was an incomplete rendering of that decision — it listed situations but never named the route each situation belongs to, and it had no row for a successful `GET`. A new numbered record would be a decision number spent on a documentation defect, and a reader of §3 might never cross-read it. Recording the correction in the same file keeps it next to the table it fixes. The choice is recorded here so the next agent does not have to guess.

**What was missing, stated plainly.** §3's `### Routes` subsection already names both routes (`POST /api/orders`, `GET /api/orders/{orderId}`), but its `### Status codes` table names neither route and has no row for `GET /api/orders/{orderId}` on a **known** id returning `200` with the order body. The table showed a `200` row, but that row is the **identical re-submit of a `POST`**, not a successful `GET`. So the table was both un-routed and short one outcome. This erratum adds the route to every row and adds the missing `GET` `200` row.

**Corrected, complete route/status table** (the design; unchanged in substance from §3 — only the `Route` column and the `GET` known-id row are new):

| Route | Situation | Status | Body |
|---|---|---|---|
| `POST /api/orders` | Order stored, first time | `201 Created` + `Location: /api/orders/{orderId}` | the order |
| `POST /api/orders` | Same `orderId`, all four field values identical (retry) | `200 OK` | the stored order (original `receivedAt`) |
| `POST /api/orders` | Same `orderId`, any field value different | `409 Conflict` | problem+json, title `Order id already used` |
| `POST /api/orders` | Validation errors | `400 Bad Request` | problem+json from `Results.ValidationProblem` |
| `GET /api/orders/{orderId}` | Known id | `200 OK` | the order |
| `GET /api/orders/{orderId}` | Unknown id | `404 Not Found` | problem+json, title `Unknown order` |
| `POST /api/orders` (a request that binds a body) | Wrong content type | `415` | framework-generated |
| `POST /api/orders` (a request that binds a body) | Malformed JSON | `400` | framework-generated |

**Two rows are POST-only, and that is measured, not assumed.** The `415` and malformed-JSON `400` come from framework body handling; a `GET` carries no bound body, so neither fires on it. Measured in a scratch web toy (`/tmp/opencode`, deleted): `POST` with a JSON content type → `201`; `POST` with malformed JSON → `400`; `POST` with `Content-Type: text/plain` → `415`; `GET` with a wrong `Content-Type` header → `200`; `GET` with a malformed JSON body and a JSON content type → `200`. The old §3 table named neither the route nor this POST-only fact.

**No decision changed, and no version moved.** The duplicate rule (identical → `200`, changed → `409`) is untouched. `ContractVersion.Current` stays `4`; `docs/contract/v1.md`…`v4.md` are untouched. This is a documentation correction to one table, not a wire change, so nothing gets a version bump.

**One related omission remains, and it is the owner's to fix.** The same incomplete table appears in `docs/contract/wms-rest.md` (`## Routes and status codes`), which review 20 issue 2 asks the owner to complete as part of the M6 deliverable. That page is the owner's assignment output and is deliberately **not** edited by this record. Lesson 20's own route table (`POST /api/orders` → `201`; `GET /api/orders/{orderId}` → `200`/`404`; identical re-send → `200`) is already complete and needs no correction.
