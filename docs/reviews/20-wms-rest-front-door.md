# Review 20 — Lesson 20: the WMS REST front door

**Reviewer:** Reviewer agent
**Commit under review:** `352f5b0` ("M6: Lesson 20 WMS REST front door"), gate fix in `d68b361` ("M6: Lesson 20 and fix bug from previous commit"), cumulative range `99461cb..HEAD`
**Lesson:** [20-wms-rest-front-door.md](../lessons/20-wms-rest-front-door.md) · **Decision:** [record 28](../decisions/28-wms-rest-front-door.md)
**Verdict:** **pass with changes** (no blockers; two should-fixes tied to explicit requirements, three considers)

The code is correct, the build is clean, all 159 non-integration tests pass, and I reproduced the whole
live front door end to end: `201`/`200`/`409`/`400`/`404`/`415`, the Kestrel-URL startup check, and a
clean Ctrl+C shutdown (`rc 0`). The gate test that was red at `99461cb` is fixed. Nothing here loses or
duplicates a pallet. Two requirements are only partially met and should be closed before M6 is called
done: the Ctrl+C path does **not** await both tasks, and the WMS contract page never states the route
paths (and drops the `GET` success row). Details below.

---

## Commands run (exact) and what they showed

| # | Command | Result |
|---|---|---|
| 1 | `git status --short && git log --oneline -6 && git show --stat d68b361 && git show --stat 352f5b0 && git diff 99461cb..HEAD --stat` | tree clean; HEAD `352f5b0`; `d68b361` = lesson/decision docs + the `ShuttleEndpoint.cs` gate fix; `352f5b0` = 13 code/doc files |
| 2 | `dotnet --version` | `10.0.112` |
| 3 | `cd src && dotnet build` | RC 0, **0 Warning(s), 0 Error(s)** |
| 4 | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **RC 0 — Failed: 0, Passed: 159, Total: 159.** FleetCore 35, Contracts 8, TrafficCore 6, ShuttleCore 31, Messaging 10, MqttMessaging 3, ShuttleSim 10, FleetManager 56 |
| 5 | Lesson 20 acceptance greps (19 checks) | **19/19 PASS** (table below) |
| 6 | `cd src && dotnet run --project FleetManager` | RC 0, prints `Fleet Manager Coming Online` |
| 7 | Mutations (a)–(d) in scratch `/tmp/opencode/l20mut` (workspace untouched, scratch **deleted**) | (a)(b)(c) turn red; (d) survives the committed suite but a scratch stress probe catches the race |
| 8 | Own hosting mutations (e)–(g), same scratch | all three survive — see issue 3 |
| 9 | Live `serve` from the built dll + `curl`, in a Python runner that sends SIGINT | 201/200/409/400/404/415; `Application is shutting down...`; **rc 0** |
| 10 | `Kestrel__Endpoints__Http__Url="" dotnet …FleetManager.dll serve` | throws `InvalidOperationException: Kestrel:Endpoints:Http:Url must be set.` at `Program.cs:155`, before MQTT connect |
| 11 | `docker compose up -d broker` … `docker compose down`; `docker ps`; `pgrep -af "FleetManager.dll|mosquitto"` | broker used only for the live run; brought down; no containers, no stray processes |
| 12 | `git status --short` after cleanup | clean — this review edited nothing under `src/` |

**A note on the SIGINT test.** My first attempt sent `kill -INT` to a background job of a non-interactive
shell and the process did not exit; that is a shell artifact (background jobs inherit `SIGINT` ignored),
not an app defect. Re-running with the process spawned by Python (default signal disposition) gave a clean
`Application is shutting down...` and `rc 0`. The owner's composition/clean-shutdown claim is therefore
**verified**, with the caveat in issue 1.

---

## Acceptance criteria

### Lesson 20 requirements

- [x] **R1 `FleetManager.csproj` → `Microsoft.NET.Sdk.Web`; three Configuration package refs removed; console branches keep working** — `grep` clean, build 0 warnings, `dotnet run` prints the banner / RC 0.
- [x] **R2 `WmsOrderKinds` / `WmsOrderStates` / `WmsRoutes` constants** — present; `WmsRoutes.Order(orderId)` builds the path (`WmsRoutes.cs:8`).
- [x] **R2 `SubmitOrderRequest` / `WmsOrder` / `SubmitResult` records** — four nullable strings on the request; `WmsOrder` adds `State` + `ReceivedAt` (`WmsOrder.cs`).
- [x] **R2 `WmsOrderValidator.Validate` all-rules-at-once, wire keys, exact messages** — `WmsOrderValidator.cs:7-18`; messages match record 28 §B and the doc table exactly; mutation (a) turns the all-errors test red.
- [x] **R2 `WmsOrderBoard` one lock, no clock, `AlreadyKnown` keeps original `ReceivedAt`, `Conflict` on changed values, `TryGet`** — `WmsOrderBoard.cs:8-37`; mutations (c) red.
- [x] **R3 read + validate `Kestrel:Endpoints:Http:Url`, throw if missing/blank** — `Program.cs:153-155`; live blank-URL run throws at `:155`.
- [x] **R3 web app built with `WebApplicationOptions { Args, ContentRootPath = AppContext.BaseDirectory }`, run as second task, same token, loop behaviour unchanged** — `Program.cs:176-201`; `FleetServeLoop.cs` byte-identical (`git diff` empty).
- [~] **R3 "Ctrl+C must stop both and await both"** — **PARTIAL.** `stopping.Cancel()` stops both and the live process exits `rc 0`, but `await loopTask; await webTask;` share one `try` (`Program.cs:211-215`); `FleetServeLoop.RunAsync` cancels by throwing (`FleetServeLoop.cs:43`), so `await loopTask` throws and the `catch` skips `await webTask`. See issue 1.
- [x] **R3 `MapPost` validate → `Results.ValidationProblem` / `Created` / `Ok` / `Problem(409)`** — `Program.cs:182-195`; live 400/201/200/409 confirmed.
- [x] **R3 `MapGet` `TryGet` → `Ok` / `Problem(404)`** — `Program.cs:197-200`; live 200/404 confirmed.
- [x] **R3 endpoint has no validation rules and no lock** — verified by reading; all rules/locking are in `FleetCore`.
- [x] **R4 `appsettings.json` `Kestrel` section with `http://127.0.0.1:5080`, no port literal in code** — `appsettings.json:22-28`; no port in `Program.cs`.
- [~] **R5 `docs/contract/wms-rest.md` `Version: 1` with request body, success body, error shape, route/status table, validation table, change rule** — **PARTIAL.** All present except the route table never names the route paths and omits the `GET` success (`200`) row. See issue 2.
- [x] **R6 pure tests: validator, board, literal pins** — `WmsOrderValidatorTests` (4), `WmsOrderBoardTests` (4), `WmsContractLiteralsTests` (5); all pass; mutations prove the important ones load-bearing.
- [x] **Contract untouched: `ContractVersion.Current == 4`; `docs/contract/v1..v4.md` unchanged** — `MessageTypes.cs:15`; `git diff --stat … v1.md v2.md v3.md v4.md` prints nothing.
- [x] **Gate fixed in its own commit** — `d68b361` moved the healthy-path ack out of the fault branch (`ShuttleEndpoint.cs:121-135`); ShuttleSim.Tests now **10/10** (was 9/10 red at `99461cb`).

### Build/test gate

- [x] `dotnet build` RC 0, 0 warnings, 0 errors.
- [x] `dotnet test … --filter "Category!=Integration"` RC 0 — **159 passed / 0 failed / 159 total** (baseline at `99461cb` was 145/1/146; +13 new FleetCore tests + the gate test now green).

### Lesson 20 acceptance greps (19/19)

| # | Check | Result |
|---|---|---|
| 3a | `Microsoft.NET.Sdk.Web` in csproj | PASS |
| 3b | no `Microsoft.Extensions.Configuration` package ref | PASS |
| 3c | `MapPost` in `FleetManager` | PASS |
| 3d | `Results.ValidationProblem` | PASS |
| 3e | `Results.Created` | PASS |
| 4a | `WmsOrderValidator` in `FleetCore` | PASS |
| 4b | `class WmsOrderBoard` in `FleetCore` | PASS |
| 4c | `lock` in `WmsOrderBoard.cs` | PASS |
| 4d | `"/api/orders"` literal in `FleetCore` | PASS |
| 5a | `Kestrel` in `appsettings.json` | PASS |
| 5b | `Kestrel:Endpoints:Http:Url` read in `Program.cs` | PASS |
| 6a | `docs/contract/wms-rest.md` exists | PASS |
| 6b | `Version: 1` | PASS |
| 6c | `Current = 4` | PASS |
| 6d | `git diff` over v1–v4 prints nothing | PASS |
| 7a | `"store"` literal in tests | PASS |
| 7b | `"retrieve"` literal in tests | PASS |
| 7c | `"received"` literal in tests | PASS |
| 7d | `"/api/orders"` literal in tests | PASS |

### Mutation evidence (scratch copy `/tmp/opencode/l20mut`, deleted afterwards)

| # | Mutation | Where | Result | Reading |
|---|---|---|---|---|
| a | `Validate` early-returns after first failure | `WmsOrderValidator.cs` | FleetCore **1 failed** — `Blank_required_fields_give_all_three_errors_at_once` | **killed** |
| b | `WmsRoutes.Orders` → `"/api/order"` | `WmsRoutes.cs` | FleetCore **2 failed** — `Orders_route`, `Order_route_builds_the_path` | **killed** |
| c | identical re-submit returns `Conflict` | `WmsOrderBoard.cs` | FleetCore **1 failed** — `Identical_resubmit_is_already_known_and_keeps_the_original_received_at` | **killed** |
| d | `lock (_gate)` → `if (true)` in `Submit` | `WmsOrderBoard.cs` | committed suite **35/35 PASS** | **survives** — no committed test pins the lock |
| d′ | same, plus a scratch 64-thread probe asserting exactly one `Created` | scratch test | probe **FAIL** (more than one `Created`); with the lock restored, the same probe **PASSES** | the race is real and observable, but only with a test the assignment did not ask for |
| e | `AlreadyKnown` → `Results.Created(...)` (duplicate answers `201`) | `Program.cs` | full suite **159/159 PASS** | **survives** — no HTTP test pins the outcome mapping |
| f | disable the `Kestrel:Endpoints:Http:Url` blank check | `Program.cs` | full suite **159/159 PASS** | **survives** — no test pins startup validation |
| g | `MapGet(WmsRoutes.Order("{orderId}"), …)` → `MapGet(WmsRoutes.Orders, …)` | `Program.cs` | full suite **159/159 PASS** | **survives** — no test pins route registration |

The three the lesson named are killed. Mutation (d) is the lesson's acknowledged non-deterministic one;
my scratch probe shows a duplicate-submit race when the lock is removed, so the lock is doing real work —
but the committed suite does not catch it. Mutations (e)–(g) all survive: the entire HTTP host layer is
unpinned. See issue 3.

---

## Top issues, ranked

### 1. SHOULD-FIX — Ctrl+C stops both tasks but does not await the web task
`src/FleetManager/Program.cs:211-215`, `src/FleetManager/FleetServeLoop.cs:43`

```csharp
stopping.Cancel();
try
{
    await loopTask;
    await webTask;     // never reached when await loopTask throws
}
catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
```

`FleetServeLoop.RunAsync` cancels by throwing out of `PeriodicTimer.WaitForNextTickAsync`, so
`await loopTask` throws `OperationCanceledException`, the `catch` swallows it, and `await webTask` is
skipped. Requirement 3 says "Ctrl+C must stop both and **await both**", and the hints warn: "Add a
`try`/`catch` around the awaits so an exception in either one does not skip the other." The live run still
exits `rc 0` (Kestrel was told to stop by the same token), so this is not visible today, but Kestrel's
graceful drain is abandoned and any fault on `webTask` goes unobserved — exactly the kind of shutdown
loose end that cuts an in-flight WMS call short on a real restart. Split the awaits (`Task.WhenAll` in one
`try`, or a `try` per await).

### 2. SHOULD-FIX — the WMS contract page never states the routes and omits `GET` success
`docs/contract/wms-rest.md:64-74`

The "Routes and status codes" table contains only scenarios. The string `/api/orders` appears nowhere in
the page, and the only `/api/orders/{orderId}` is inside the `Location` example. The `GET` success
(`200`, order body) has no row at all. Lesson requirement 5 asks the page to contain "the route and
status-code table", and lesson §2's route table is explicitly `POST /api/orders` → `201` and
`GET /api/orders/{orderId}` → `200`/`404`. A WMS integrator handed this page cannot find the URL to POST
to. Add the two route paths to the table and the missing `GET` `200` row. Worth saying to the professor:
record 28 §3's status table has the same omission, so the record should probably be corrected too.

### 3. CONSIDER — the HTTP door has no automated coverage; every hosting mutation survives
`Program.cs:182-201`, and the absence of any `Microsoft.AspNetCore.Mvc.Testing` reference

Mutations (e), (f) and (g) — wrong duplicate mapping, no Kestrel-URL check, broken `MapGet` registration —
all leave **159/159** green. The literal pins in `WmsContractLiteralsTests` protect the *constants*, but
nothing protects that `MapPost`/`MapGet` actually use them or that outcomes map to the right status codes.
The lesson scoped the assignment's tests to pure `FleetCore` and made live behaviour a stretch goal, and
record 28 notes the test package exists (`10.0.13`) without adding it — so this is a lesson-design choice,
not a violation. Still: nothing turns red if the route text is mistyped, and the one thing M6 must not do
is hand the WMS a door that silently moved. A single `WebApplicationFactory` test per outcome would close
it, and the door will only grow. Read this together with mutation (d): the whole new layer is
"verified today, unguarded tomorrow".

### 4. CONSIDER — `WmsOrderBoard.Submit` is only safe because the caller validates first
`src/FleetCore/WmsOrderBoard.cs:12,19-20`

The board dereferences `request.OrderId!`, `request.Kind!`, `request.PalletId!`, `request.LaneId!`. With
the endpoint as the sole caller and validation always first, that holds. A direct call to `Submit` with
`OrderId == null` would `TryGetValue(null)` (throws on `Dictionary` with `Ordinal`) and, past that, store
`null` into non-nullable `WmsOrder` fields. The hints ask the owner to note this dependency; there is no
comment in the code and no test that a blank id cannot reach the board. Cheap hardening: guard in `Submit`
or keep the board `internal` to `FleetCore`'s assembly.

### 5. CONSIDER — no application-level log for an accepted or rejected order
`src/FleetManager/Program.cs:182-200`

The framework logs the request line and status code, but nothing in our code logs the `orderId` and the
outcome in our own words. When the WMS says "we sent `ORD-1001` and got a timeout", a support engineer
sees the route and status but not the order id or whether it was created, deduplicated, or refused.
AGENTS.md calls for logs that let a support engineer find a root cause. One structured line per outcome is
enough at this slice.

**Non-blocking notes**

- `WmsOrderValidator` builds `new Dictionary<string, string[]>()` with no comparer; the hint preferred
  `StringComparer.Ordinal`. Default string equality is already ordinal, so this is style only.
- `_ => Results.Problem(statusCode: 409, …)` (`Program.cs:193`) catches any future `SubmitOutcome`. An
  explicit `SubmitOutcome.Conflict =>` would fail to compile the day a fourth outcome is added, which is
  safer for a state-ladder type.
- `Program.cs:151-152` — the `BlockedPathSeverity` continuation is not indented under its assignment,
  unlike the other keys. Cosmetic; a reminder of the copy-adapt residue review 19 flagged.
- `Program.cs:176-180` passes `Args = args` (i.e. `["serve"]`) to the web builder. It is harmless here
  (verified live), and lesson §1 shows exactly this shape; but it is accidental coupling to the
  command-line provider. A one-line comment would stop the next reader wondering.

---

## What is solid

- **The gate is fixed in its own commit, and only the gate.** `d68b361` moves the accepted ack out of the
  fault branch so the healthy path answers once with no status publish; the fault path still recovers and
  publishes status. ShuttleSim.Tests is 10/10 and nothing else moved.
- **The pure split is exactly what the record describes.** `FleetCore` contains no web type (grep for
  `Results|HttpContext|IResult` is empty); the validator is pure and reports every error at once;
  `WmsOrderBoard` owns the only lock and takes `now` as an argument; the endpoint only maps
  `SubmitOutcome` to a result. Each of the three named mutations kills its target test.
- **The duplicate rule is implemented as decided**: all four input values compared (not `State`/time),
  the stored order and its original `ReceivedAt` returned on a retry, `409` on a changed body. Mutation
  (c) proves the retry test is load-bearing.
- **The contract page is otherwise accurate**: `Version: 1`, request body, success body with `state` in
  both the table and the example, the problem+json shape, and validation messages that match
  `WmsOrderValidator` character for character; the change rule states it never bumps the MQTT contract.
- **The MQTT contract did not move**: `ContractVersion.Current == 4`, `docs/contract/v1..v4.md` untouched,
  `FleetServeLoop`/`Messaging`/`Contracts`/`ShuttleSim` untouched.
- **The live door works end to end**: 201 with `Location`, 200 on the identical re-send, 409 on a changed
  lane, 400 problem+json on a missing field (and the rejected order is not stored — the follow-up `GET`
  is 404), 404 on unknown, 415 on a wrong content type; blank `Kestrel:…:Url` fails fast at startup; a
  clean SIGINT exits `rc 0`.

---

## Explain-it check

Answer these out loud, no notes, before the next lesson. Do not read an answer back from the code.

**The three decisions an interviewer would most likely ask about:**

1. `WmsOrderBoard.Submit(request, DateTime now)` takes the clock as an argument while
   `WmsOrderValidator.Validate(request)` takes none, and the endpoint supplies `DateTime.UtcNow`. Why is
   time an *input* to the board rather than something the board reads itself, and what does
   `Identical_resubmit_is_already_known_and_keeps_the_original_received_at` prove *only because* the
   clock is passed in?
2. `FleetCore` contains no `Results`, no `HttpContext`, no `IResult`; `WmsOrderBoard` owns the only lock
   and the endpoint does none of the validation or locking. What breaks if the lock moves into the
   endpoint, and what breaks if validation runs after `Submit`?
3. A re-sent order whose four values are identical returns `200` with the stored order; the same
   `orderId` with one changed value returns `409`. Why is "identical body" the test that separates a
   retry from a different order, and what would go wrong for the WMS if the retry returned `201` and the
   work ran a second time?

**Lines and blocks that look unclear, copied, or unexplained — study these:**

- `Program.cs:211-215` — `await loopTask; await webTask;` in one `try`. On Ctrl+C, which await throws
  first, which one is skipped because of it, and what does `FleetServeLoop.RunAsync` actually do when its
  token is cancelled?
- `WmsOrderBoard.cs:12,19-20` — `request.OrderId!`, `Kind!`, `PalletId!`, `LaneId!`. What guarantees each
  is non-null, and what happens if someone calls `Submit` without calling `Validate` first?
- `WmsRoutes.cs:8` — `Order(string)`. Which call site passes the *template* `"{orderId}"` and which
  passes a real id? What would a reader mistaking one for the other produce?
- `docs/contract/wms-rest.md:64-74` — where in this page does a WMS client learn the URL to POST to, and
  which `GET` outcome has no row?
- `src/FleetManager/Program.cs:149-152` — the indentation of the `BlockedPathSeverity` continuation.
  Where does this line come from, and why does the same shape recur across reviews?

---

## Revisit list

- **Awaiting several tasks on shutdown.** One `try` around two `await`s means the first thrown exception
  skips the second. Know `Task.WhenAll` vs per-await `try`/`catch`, and which your service needs.
- **Idempotency by content vs. by id.** Why a re-send must be compared on the four *inputs* only, why the
  derived `State`/`ReceivedAt` are excluded, and why `200` (not `201`) is the honest answer to a retry.
- **Where a clock may live.** The board takes `now`; the endpoint reads the wall clock. Say why that
  boundary makes the duplicate test reproducible.
- **A status table is not a route table.** An integration document must name the URLs. (And check whether
  record 28 §3 needs the same correction.)
- **Pin the wiring, not only the constants.** Constant literals prove the vocabulary; they prove nothing
  about `MapPost`/`MapGet`/outcome mapping. Mutation (e)–(g) are the demonstration.
- **Copy-adapt residue** (carried from review 19): indentation and continuation shapes that survive
  several edits are usually a sign of a block that was pasted, not written.

---

## End-product check

This is the first runtime way work enters the fleet manager, and it lands where the spec says it should:
REST at the business edge, MQTT down to the shuttles, one process, one cancellation token, the order rules
in pure `FleetCore` beside `TaskBoard`. The shapes, the duplicate rule, and the in-memory registry with a
written SQL trigger all match record 28, and the MQTT contract is untouched. The risk this slice introduces
is concentrated in the new edge: the HTTP host and its mappings have no automated test, and the shutdown
path stops Kestrel without awaiting it. The next slices (order → `FleetTask` mapping, then status push to
a configured WMS URL) will sit on this door and on this shutdown path, so both are worth locking down now,
while they are two small fixes rather than an inherited pattern. No design change is needed: the split,
the types, the lock, and the endpoint shell are all the right shape.

---

## README

**Not updated.** The verdict is *pass with changes*: no blockers, but two requirements are only partially
met (issue 1 "await both", issue 2 route table), so per AGENTS.md the M6 milestone row stays `not started`
and nothing in `README.md` changed — verified with `git status --short` (clean) after writing this file.
Flag for the professor, not this review: the README's milestone table already says **M5 done** while the
M5 verification note still opens "**M5 (in progress)**" — those two lines disagree and one should be
reconciled when M5's record is next touched.

---

# README update — 2026-10-19 (M5 done, M6 in progress)

**Reviewer follow-up on the _pass with changes_ verdict.** This section records the build/test gate and
the README changes made after the review above. No existing review text was modified.

## Commands run and results

| # | Command | Result |
|---|---|---|
| 1 | `dotnet --version` | `10.0.112` |
| 2 | `cd src && dotnet build` | RC 0 — **0 Warning(s), 0 Error(s)** |
| 3 | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0 — **Failed: 0, Passed: 159, Total: 159** (FleetCore 35, Contracts 8, Messaging 10, MqttMessaging 3, ShuttleCore 31, TrafficCore 6, ShuttleSim 10, FleetManager 56) |
| 4 | `git diff -- src/FleetManager/Program.cs` | only the working-tree shutdown fix under `src/` (the two `try`/`catch` split); nothing else in `src/` moved |
| 5 | `pgrep -af "FleetManager.dll\|dotnet run\|mosquitto"`; `docker ps` | no stray processes; no containers running |

**Not run:** `Category=Integration` (needs Postgres, per the follow-up's instruction to use the
non-integration filter) and the live SIGINT/Kestrel run (needs a broker). Should-fix #1's fix was
verified by inspection of the two `try`/`catch` blocks, not by a live Ctrl+C.

## Should-fix #1 verification

**Fixed.** `src/FleetManager/Program.cs:211-226` now awaits `loopTask` and `webTask` in **separate**
`try`/`catch` blocks. On Ctrl+C, `stopping.Cancel()` makes `await loopTask` throw
`OperationCanceledException`; the first filter catches it and control reaches the **second** `try`,
which awaits `webTask`. The exception can no longer skip the web-task await.

```csharp
try { await loopTask; }
catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
try { await webTask; }
catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
```

**Residual (not a blocker):** a *non-cancellation* fault from `loopTask` — any exception that does not
match the `when (stopping.IsCancellationRequested)` filter, e.g. an `InvalidOperationException` — still
propagates past the second `try`, so `webTask` would go unawaited in that case. The lesson's requirement
was the Ctrl+C path specifically, which is now correct; worth noting for the deferred "door hardening"
slice.

## README changes (milestone table + the M5 status marker only)

- M5 row: `| M5 | Faults and recovery | done | Verified: \`dotnet test ShuttleFleet.slnx\` (149/149 passed) |`
  → `| M5 | Faults and recovery | done | Verified: \`dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"\` (159/159 passed) |`
- M6 row: `| M6 | WMS integration | not started |`
  → `| M6 | WMS integration | in progress | Reviewed: docs/reviews/20-wms-rest-front-door.md (pass with changes); record 28; lesson 20. Open should-fix: docs/contract/wms-rest.md route/status table; deferred considers (d/e/f/g). |`
- M5 verification note heading (README line 147): `**M5** (in progress)` → `**M5** (done)`.

The M5 note body still carries an older 138-test count and a "Not yet done: … the recovery wiring"
sentence written before lesson 19 landed. That prose was left untouched per the follow-up's
"do not touch other prose" limit; the professor should reconcile it if the note is next edited.

## Still open

- **Should-fix #2 — appears already fixed in the working tree (uncommitted, not yet re-reviewed).**
  Review 20 recorded `docs/contract/wms-rest.md` as missing the route paths and the
  `GET /api/orders/{orderId}` → `200` row. The current (uncommitted) working tree already contains that
  fix: the "Routes and status codes" table gained a `Route` column (`POST /api/orders` on the POST rows,
  `GET /api/orders/{orderId}` on the GET rows) and explicit `GET` `200 OK` and `GET` `404 Not Found`
  rows. Decision record 28 §11 carries the same corrected table. **Discrepancy flagged:** this follow-up
  was told should-fix #2 was still open, so the content-level request is in fact met — but the change is
  uncommitted and has not been through a re-review, so it is *fixed-in-tree, not yet verified by a
  review*. The M6 row in `README.md` therefore does not call it out as open.
- **Deferred considers (d/e/f/g)** — (d) duplicate-submit lock race, (e) `AlreadyKnown` → `201` mapping,
  (f) blank-Kestrel-URL startup check, (g) broken `MapGet` registration. Each survives the committed
  suite; the owner has moved them to a later "door hardening" slice. Issues 3 and 4 above also remain
  open (the HTTP host has no automated coverage; no per-order application log).
- **Explain-it answers** — the three questions and the five "unclear/copied" blocks in the Explain-it
  check above are still unanswered; none were answered in this follow-up.
