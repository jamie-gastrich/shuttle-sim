# Review 21 — Lesson 21: from an accepted WMS order to a fleet task

**Reviewer:** Reviewer agent
**Commit under review:** `29a81ce` ("M6: Lesson 21 First attempt"), lesson material from `d1cb10f` ("M6: Lesson 21 order-to-task mapping")
**Lesson:** [21-wms-order-to-fleet-task.md](../lessons/21-wms-order-to-fleet-task.md) · **Decision:** [record 29](../decisions/29-wms-order-to-task-mapping.md)
**Verdict:** **pass with changes** (no blockers; the mapping, the intake table, and the shared board are correct and mutation-proven; three test-strength should-fixes and two considers)

The build is clean and the pure suite is green: **RC 0, 0 warnings, 0 errors; Failed: 0, Passed: 165, Total: 165** on the non-integration filter (baseline 159, +6). The mapping lives in `FleetCore`, inside the existing lock, on `Created` only; the endpoint did not change; the task lands on the same `TaskBoard` instance the serve process owns; `ContractVersion.Current` stays `4` and `docs/contract/` is untouched. Every mutation the lesson named as "must go red" goes red, and the lesson's maybe-survive lock mutation survives as predicted. What holds this back from a bare *pass* is three pieces of missing or weakened test evidence the lesson explicitly asked for. None of them corrupt fleet state; all are cheap to close.

---

## Commands run (exact) and what they showed

| # | Command | Result |
|---|---|---|
| 1 | `git status --short && git log --oneline -8 && git diff --stat d1cb10f..HEAD` | tree **clean**; HEAD `29a81ce`; the attempt touched exactly 4 files: `src/FleetCore.Tests/WmsOrderBoardTests.cs` (+67/-4), `src/FleetCore/WmsOrder.cs` (+1/-1), `src/FleetCore/WmsOrderBoard.cs` (+18/-10), `src/FleetManager/Program.cs` (+3/-1) |
| 2 | `dotnet --version` | `10.0.112` |
| 3 | `cd src && dotnet build` | RC 0 — **0 Warning(s), 0 Error(s)** |
| 4 | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **RC 0 — Failed: 0, Passed: 165, Skipped: 0, Total: 165.** Per project: FleetCore 39, FleetManager 56, ShuttleCore 31, Messaging 10, MqttMessaging 3, ShuttleSim 10, Contracts 8, TrafficCore 6. (Baseline 159; **+6** — the four new mapping tests plus two other FleetCore tests already at HEAD.) |
| 5 | Lesson 21 acceptance greps (task checklist, 19 checks) | **16/19 PASS** — see the table below. The 3 failures are the literal-pin checks (checked separately) and `grep CreatedTask src/FleetCore/WmsOrderBoard.cs`. |
| 6 | `git diff --stat 77f3477..HEAD -- docs/contract` and `... 77f3477..HEAD -- src/FleetCore/FleetTask.cs src/FleetCore/TaskBoard.cs src/FleetManager/TaskDispatcher.cs src/FleetManager/RunLoop.cs src/FleetManager/FleetServeLoop.cs` | both **empty** — contract and reviewed pure types untouched |
| 7 | `grep -rnE "Results|HttpContext|IResult" src/FleetCore --include=*.cs` | **no matches** — the reviewed pure types still hold no web type |
| 8 | Mutations (a)–(g) in scratch `/tmp/opencode/l21mut` (workspace untouched; scratch **deleted** afterwards) | see "Mutation evidence" below |
| 9 | `pgrep -af "dotnet run\|FleetManager.dll\|mosquitto"`; `docker compose -f docker/compose.yml ps` | no stray run/broker processes; **no containers**. Nothing was started: the pure suite is the lesson's acceptance gate and needs no broker. |
| 10 | `git status --short` after cleanup | **clean** — this review edited nothing under `src/` |

**Not run / not yet verified:** the `Category=Integration` suite (needs Postgres), and the stretch-goal live `serve` + `curl` run (needs the broker). Both are marked **not yet verified**; the pure gate the lesson names is fully green.

---

## Acceptance criteria

### Lesson 21 assignment requirements

- [x] **R1 `SubmitResult` gains `CreatedTask`; not named `Task`** — `src/FleetCore/WmsOrder.cs:15` `public sealed record SubmitResult(SubmitOutcome Outcome, WmsOrder Order, FleetTask? CreatedTask);`. Member is not `Task`.
- [x] **R2 `WmsOrderBoard` takes a `TaskBoard`; maps inside the existing lock, on `Created` only** — `WmsOrderBoard.cs:7,9-10` field + ctor with `?? throw new ArgumentNullException`; `:14` `lock (_gate)`; `:28-29` `CreateTask` + `_tasks.Add` after `_orders.Add`; `:19-20` `AlreadyKnown`/`Conflict` return `CreatedTask: null`. The mapping is one small private method `:41-42`.
- [x] **R2 duplicate rule unchanged, mapping one home** — `SameFields` (`:44-47`) compares the four input values only; `CreateTask` (`:41-42`) is the single mapping site.
- [x] **R3 `Program.cs` creates the board and hands it over; handlers unchanged** — `Program.cs:164-165` `var taskBoard = new TaskBoard(); var board = new WmsOrderBoard(taskBoard);`. `MapPost`/`MapGet` unchanged (`git show` confirms only those two lines moved in `Program.cs`).
- [ ] **R4 tests: 4 existing updated + 3 new facts** — **PARTIAL.** The four existing tests construct `new WmsOrderBoard(tasks)`, and all seven are present (`First_submit_creates_exactly_one_queued_task`, `Identical_resubmit_adds_no_second_task`, `Conflict_adds_no_task`, `A_second_order_adds_a_second_task`, plus the retitled old ones). But three assertions the requirement names are weakened or missing — see issues 1–3 and the mutation table:
  - the literal wire pin `Assert.Equal("queued", TaskStateWire.ToWireName(...))` is replaced by `Assert.Equal(TaskState.Queued, result.CreatedTask.State)` (enum vs enum);
  - `A_second_order_adds_a_second_task` asserts only `NotNull` + count, not the second task's id/lane/state;
  - nothing asserts `CreatedTask!.TaskId == result.Order.OrderId` or `CreatedAt == received time`.
- [x] **R5 do not touch `FleetTask.cs`, `TaskBoard.cs`, `TaskDispatcher.cs`, `RunLoop.cs`, `FleetServeLoop.cs`, `Contracts/`, `docs/contract/`** — all diffs empty (commands 6).

### Acceptance greps (task list, 19 checks)

| # | Check | Result |
|---|---|---|
| 1 | `TaskBoard` in `WmsOrderBoard.cs` | PASS |
| 2 | `FleetTask` in `WmsOrderBoard.cs` | PASS |
| 3 | `CreatedTask` in `WmsOrder.cs` | PASS |
| 4 | `CreatedTask` in `WmsOrderBoard.cs` | **FAIL** — the record member exists but the **token `CreatedTask` is never written in the board**; the local is `created` (`:28,31`) and the outcome carries it positionally. The intended `grep` literal is absent. |
| 5 | `new TaskBoard()` in `Program.cs` | PASS |
| 6 | `new WmsOrderBoard(taskBoard)` in `Program.cs` | PASS |
| 7 | no `MapToTask\|CreateTask` in `Program.cs` | PASS |
| 8 | `lock (_gate)` in `WmsOrderBoard.cs` | PASS |
| 9 | `MapPost` in `Program.cs` | PASS |
| 10 | `Current = 4` in `MessageTypes.cs` | PASS |
| 11 | `git diff --stat docs/contract` empty | PASS |
| 12 | reviewed pure types untouched | PASS |
| 13 | no `Results\|HttpContext\|IResult` in `FleetCore` | PASS |
| 14 | `CreateTask` only in `FleetCore` | PASS |
| 15 | validation before `Submit` in the endpoint | PASS |
| 16 | `assert "queued"` literal in `WmsOrderBoardTests.cs` | **FAIL** — no `"queued"` literal in the file (issue 1) |
| 17 | `assert "ORD-1"` literal in `WmsOrderBoardTests.cs` | PASS (`:71`) |
| 18 | `TaskStateWire.ToWireName` pin in `WmsOrderBoardTests.cs` | **FAIL** — absent; `TaskStateWire` is used only in `TaskLifecycleTests.cs` (issue 1) |
| 19 | 4 existing tests updated + 3 new present | PASS/partial (R4) |

**16/19** (checks 4, 16, 18 fail). Check 4 is a grep artifact — the behaviour is correct; checks 16 and 18 are real missing assertions.

### Structural checks (verified against record 29's intake table)

- [x] **Task created inside the lock, on `Created` only** — `WmsOrderBoard.cs:14-32`; both early returns precede the mapping, `_tasks.Add` is inside `lock (_gate)`.
- [x] **No task on `AlreadyKnown` / `Conflict`** — mutation (a)/(b) prove both paths turn a test red; `CreatedTask` is explicit `null` on both (`:19-20`).
- [x] **Task id = order id; lane = order lane; state `Queued`; time = `order.ReceivedAt`** — `CreateTask` (`:41-42`); mutation (d) proves the lane assertion, mutation (e) the state assertion.
- [x] **Same `TaskBoard` instance the serve process owns** — `Program.cs:164-165` creates one and passes it to the board; the endpoint calls the same `board` (`:189`). (Nothing drains it yet — by design; no dispatcher is wired in `serve`, per record 29 §9.)
- [x] **No lock moved into the endpoint; validation still before `Submit`** — `Program.cs:183-189`: `Validate` first, `board.Submit` after; the handler neither locks nor maps.
- [x] **Contract version and page untouched** — `Current = 4`; `git diff` over `docs/contract` empty.

### Mutation evidence (scratch `/tmp/opencode/l21mut`, deleted afterwards)

Each mutation was applied to a throwaway copy of `src`; the `FleetCore.Tests` suite ran after each. The workspace was never touched.

| # | Mutation | Result | Verdict | Catching test |
|---|---|---|---|---|
| a | map on `AlreadyKnown` (`_tasks.Add(CreateTask(existing))`) | FleetCore **2 failed** (37/39) | **KILLED** | `Identical_resubmit_adds_no_second_task` (and `Identical_resubmit_is_already_known_and_keeps_the_original_received_at`) — `TaskRejectedException: duplicate task id ORD-1`, the backstop firing |
| b | map on `Conflict` (`_tasks.Add(CreateTask(existing))`) | FleetCore **2 failed** (37/39) | **KILLED** | `Conflict_adds_no_task` (and `Same_order_id_with_a_different_lane_is_a_conflict`) — duplicate-id throw |
| c | delete the mapping from `Created` (`CreatedTask` → `null`, no `_tasks.Add`) | FleetCore **4 failed** (35/39) | **KILLED** | `First_submit_creates_exactly_one_queued_task`, `A_second_order_adds_a_second_task`, and both no-task tests (count expectation flips) |
| d | lane from the order id (`new(order.OrderId, order.OrderId, ...)`) | FleetCore **1 failed** (38/39) | **KILLED** | `First_submit_creates_exactly_one_queued_task` (the `ToLane == order.LaneId` assertion) |
| e | `created.Assign("SH-01", now)` after creation | FleetCore **1 failed** (38/39) | **KILLED** | `First_submit_creates_exactly_one_queued_task` (the `TaskState.Queued` assertion) |
| f | remove `lock (_gate)` from `Submit` | committed suite **40/40 PASS** | **SURVIVES** (as the lesson predicted) | none |
| f′ | same, plus a scratch 64-thread `Barrier` probe asserting `taskCount == orderCount` | probe **FAIL on run 2 of 3**; with the lock restored the same probe **PASSES** 3/3 | race is real, non-deterministic | scratch probe only |
| g | `CreateTask` uses `DateTime.UtcNow` instead of `order.ReceivedAt` | committed suite **40/40 PASS** | **SURVIVES** | none |
| g′ | same, plus a scratch probe asserting `CreatedTask.CreatedAt == order.ReceivedAt` | probe **FAIL**; with `order.ReceivedAt` restored, **PASSES** | gap confirmed | scratch probe only |

**Reading.** (a)–(e): every mutation the lesson said "must go red" is killed, and the count assertion is doing the real work — (a)/(b) are caught by the board's own duplicate guard *and* the count, exactly the "rule + backstop" pair record 29 §4 describes. (f): the lock genuinely serialises two `List<T>.Add`s — the scratch probe shows a lost task when it is removed — but no committed test pulls the trigger; the lesson itself labels this "may survive", so this is a survey result, not a student failure. (g): the task's time is **undetected** by the committed suite. The lesson's own acceptance test list made the count the observable and left `CreatedAt` to prose (§ concept 1, "a test that compares them exactly should compare `ToMicrosecondPrecision(order.ReceivedAt)`"), so g surviving is a **lesson-detection gap** as much as an owner omission — see the lesson note below.

---

## Top issues, ranked

### 1. SHOULD-FIX — the wire-name literal pin is gone; the test now compares an enum to itself
`src/FleetCore.Tests/WmsOrderBoardTests.cs:73`

```csharp
Assert.Equal(TaskState.Queued, result.CreatedTask.State);
```

Lesson R4 is explicit: *"Pin the wire/state name with a literal, not constant-vs-itself: `Assert.Equal("queued", TaskStateWire.ToWireName(result.CreatedTask!.State));`."* As written, the test proves the task's state is the `Queued` enum member, but proves nothing about the string that will eventually travel to the WMS. Record 29 §6 and record 11 map `Queued → "queued"`; the status-push slice keys on that word. If a later edit renamed the enum member or changed `TaskStateWire.ToWireName`, this test would still pass and the WMS would silently get a different word. Replace the assertion with the literal pin the lesson asked for (the hint skeleton at `21-...-hints.md:180` has the exact line). Note that mutation (e) is killed by this assertion, so a correct literal pin still kills (e).

### 2. SHOULD-FIX — `A_second_order_adds_a_second_task` asserts the count but not the second task's shape
`src/FleetCore.Tests/WmsOrderBoardTests.cs:104-115`

The requirement's purpose for the second-order test is *"two different orders land as two tasks."* The test asserts `first.CreatedTask`/`second.CreatedTask` are `NotNull` and `tasks.Count == 2`; it never pins the second task's `TaskId == "ORD-2"` or `ToLane == "A02-L02"`. A mapping that created `ORD-2`'s task with `ORD-1` as its id or lane would pass this test and collide on the board in a way the count alone would not reveal if a third order were involved. In a real fleet this is a pallet going to the wrong lane. Add the two literal assertions for the second task (and ideally its `State`).

### 3. SHOULD-FIX — no test asserts the task id equals the order id, or the created time equals the accepted time
`src/FleetCore.Tests/WmsOrderBoardTests.cs` (absent)

Two facts record 29 §3 treats as load-bearing are unpinned:
- **Task id = order id** (record 29 §3, option A — the whole traceability story): the first-submit test hard-codes `"ORD-1"` as the request id, so it cannot distinguish "task id = order id" from "task id is a constant". One line, `Assert.Equal(result.Order.OrderId, result.CreatedTask.TaskId);`, with a non-`ORD-1` order in the second-order test, closes it.
- **CreatedAt = order.ReceivedAt** (record 29 §3): **mutation (g) survives** — `CreateTask` could read `DateTime.UtcNow` and the suite stays green. `FleetTask` truncates to microseconds, so the pin should compare against `ToMicrosecondPrecision(order.ReceivedAt)` or use a microsecond-boundary `Start` (the lesson's `Start` already is one). Without it, a task's FIFO position could stop matching WMS acceptance order and nothing would notice.

This is where the lesson itself is thin: the acceptance test list names the count as the observable for id and time, but never asks for a time assertion, which is why (g) has no committed catcher. Worth telling the professor.

### 4. CONSIDER — the time the task carries is not observable, and the empty-`OrderId` ceiling
Two smaller notes, both pre-existing but now more relevant:

- **Detection gap on `CreatedAt` (mutation g).** Beyond the missing assertion in issue 3, the pure suite has no other guard on the task's clock. `CreateTask` taking `order.ReceivedAt` rather than reading a clock is exactly the "time is an input" rule from record 11/13; it deserves a test, not just a comment.
- **`TaskId = OrderId` collides the day a non-WMS task source appears in `serve`.** Record 29 §3 names this ceiling ("a second source of tasks… makes a generated id correct; revisit then"). Also `Send` parity: done-when for M6 is "a bad order does not corrupt fleet state", and a blank `orderId` is refused by `Validate` before `Submit`, so the id-based mapping is safe today. No action now — but when the dispatch slice adds a local move, the id scheme needs a record, and the test that pins `TaskId == OrderId` (issue 3) will be the one that has to change.

**Non-blocking notes**

- `WmsOrderBoard.cs:9` — the constructor's indentation has a stray extra leading space (` public WmsOrderBoard(...)`). Cosmetic, but it recurs; see the revisit list.
- `WmsOrderBoard.cs:27,30` and `WmsOrderBoardTests.cs:111` — trailing whitespace on blank lines the editor added. Harmless.
- `WmsOrderBoardTests.cs:73` — `result.CreatedTask.State` is read without `!` after a `NotNull` assert; nullable-analysis is satisfied by the assert, so it compiles clean, but a reader may wonder. Using `result.CreatedTask!.State` matches the hint skeleton and reads better.
- `WmsOrder.cs:15` — the file still ends with no newline (carried from before); a one-line fix whenever the file is next touched.
- The endpoint still maps any unknown `SubmitOutcome` to `409` via `_ =>` (`Program.cs:193`); review 20 issue 4's note about an explicit `Conflict` arm stands, unaffected by this slice.

---

## What is solid

- **The mapping is exactly where record 29 put it.** `WmsOrderBoard.Submit` owns one lock and one composite fact: the order dictionary entry and the task are written in the same critical section. The endpoint gained two lines and no logic — `git show 29a81ce` proves the `MapPost`/`MapGet` bodies are byte-identical, and `Program.cs` contains no `CreateTask`/`MapToTask`.
- **The intake table is real and tested.** Mutations (a), (b), (c) each turn a test red: a retry and a conflict add no task, a first submit adds exactly one. The `TaskBoard.Add` duplicate guard works as the second line of defence — when the mapping is forced onto `AlreadyKnown`, the test fails loudly with `TaskRejectedException` instead of silently queueing a second move.
- **The task values are the right values.** `CreateTask(order) => new(order.OrderId, order.LaneId, order.ReceivedAt)` is one pure line with no clock and no id source; mutations (d) and (e) prove the lane and state assertions are load-bearing.
- **The pure split held.** `FleetCore` still contains no `Results`, `HttpContext`, or `IResult`; the board takes `now` as an argument; the concurrency probe shows the lock is doing real work on the shared `List<T>`.
- **Nothing collateral moved.** `FleetTask`, `TaskBoard`, `TaskDispatcher`, `RunLoop`, `FleetServeLoop`, `Contracts/`, and `docs/contract/` are all untouched, and `ContractVersion.Current == 4`.
- **6 new tests, all green, none skipped,** with the four lesson-20 tests intact in meaning (only their construction changed).

---

## Explain-it check

The owner must answer these out loud, with no notes, before the door-hardening or status-push lesson. Do not read the answer back from the code.

**Review 20's questions (still open — the lesson gates this assignment on them).** These were asked at `docs/reviews/20-wms-rest-front-door.md:220-249`. **They are not recorded anywhere in this attempt.** Commit `29a81ce` adds no notes file and no comment answering them; carried forward verbatim:

1. `WmsOrderBoard.Submit(request, DateTime now)` takes the clock as an argument while `WmsOrderValidator.Validate(request)` takes none, and the endpoint supplies `DateTime.UtcNow`. Why is time an *input* to the board, and what does `Identical_resubmit_is_already_known_and_keeps_the_original_received_at` prove *only because* the clock is passed in?
2. `FleetCore` contains no `Results`, no `HttpContext`, no `IResult`; `WmsOrderBoard` owns the only lock and the endpoint locks nothing. What breaks if the lock moves into the endpoint, and what breaks if validation runs after `Submit`?
3. A re-sent order with the same four values returns `200`; the same `orderId` with one changed value returns `409`. Why is "identical body" the test that separates a retry from a different order, and what would go wrong for the WMS if the retry returned `201` and the work ran twice?

**Review 20's five "unclear or copied" blocks** (also unanswered; say one sentence about each): the shutdown awaits at `Program.cs:211-226` (now two `try`/`catch` blocks), `WmsOrderBoard.cs:12,19-20` (the `!` on nullable fields), `WmsRoutes.cs:8` (template vs built path), the route table in `docs/contract/wms-rest.md`, and `BlockedPathSeverity` indentation at `Program.cs:151-152`.

**The three new decisions an interviewer would ask about this slice:**

1. The mapping (`_tasks.Add(CreateTask(order))`) sits inside `WmsOrderBoard`'s existing `lock (_gate)`, not in the endpoint and not in a new intake class. What breaks if the `TaskBoard.Add` moves outside that lock, and why is `FleetCore` (not `Program.cs`) the only place it can live?
2. A retry (`AlreadyKnown`) and a conflict both return before `CreateTask` is reached, but `TaskBoard.Add` *also* rejects a duplicate id. Why are both needed — what is the "rule" and what is the "backstop", and what would a retry that quietly queued a second move do to a pallet on a real rack?
3. `SubmitResult.CreatedTask` is non-null only on `Created`, and the task id is the order id (not a prefix, not a generated GUID). Give the two reasons record 29 says this is safe in `serve` today, and name the change that would make it unsafe.

**Lines and blocks that look unclear, copied, or unexplained — study these:**

- `src/FleetCore.Tests/WmsOrderBoardTests.cs:73` — `Assert.Equal(TaskState.Queued, result.CreatedTask.State)`. Why does comparing an enum to an enum not pin the wire vocabulary, and what would `Assert.Equal("queued", TaskStateWire.ToWireName(result.CreatedTask!.State))` catch that this line cannot? (Issue 1.)
- `src/FleetCore/WmsOrderBoard.cs:9` — the stray leading space on `public WmsOrderBoard`. Where did this constructor come from, and why does the same indentation shape keep recurring across reviews? (Copy-adapt residue, carried from reviews 19 and 20.)
- `src/FleetCore/WmsOrderBoard.cs:28-31` — why is the local named `created` and not `createdTask`, when `SubmitResult`'s member is `CreatedTask`? Does the grep in acceptance criterion 4 care about the name? (Check 4 failure — a naming inconsistency, not a bug.)
- `src/FleetCore.Tests/WmsOrderBoardTests.cs:104-109` — the blank lines at `111` and the unused `first`/`second` beyond `NotNull`. Is every value the test reads actually asserted?
- `src/FleetCore/WmsOrder.cs:3` — `SubmitOrderRequest` keeps four nullable strings while `WmsOrder` below it has non-nullable fields. Where does the nullable→non-nullable transition happen, and which line would break if `Submit` were ever called without `Validate` first? (Review 20 issue 4; the `!`s at `WmsOrderBoard.cs:16,24`.)

---

## Revisit list

- **Pin the vocabulary, not the enum.** A wire name is a string contract; `"queued"` and `TaskState.Queued` are not the same assertion. Know when a test must use the literal and when it may use the enum. (Issue 1, and this is the second lesson running that a literal pin was required.)
- **Count assertions prove cardinality, not identity.** `Count == 2` says two rows exist; it does not say they point at the right lanes and times. Say out loud which mutation a count assertion can and cannot kill. (Issues 2–3.)
- **Time as an input, again.** The board takes `now`; `FleetTask` takes `createdAt`; `CreateTask` must take the *order's* time, not read a clock. Mutation (g) shows no test pins it. Record 11 §5 / record 13 §5 are the background.
- **Rule vs. backstop.** The `Created`-only rule and `TaskBoard.Add`'s duplicate guard are two protections with two different jobs; mutation (a) demonstrates both at once. Say which one you would keep if you could keep only one, and why.
- **The id scheme has a ceiling.** `TaskId = OrderId` is correct only while WMS orders are the sole task source in `serve`. Name the trigger (a local move task, or two tenants) that forces a namespaced id, per record 29 §3.
- **Copy-adapt residue** (carried from reviews 19 and 20): stray indentation and continuation shapes that survive edits usually mark a pasted block. The constructor indent at `WmsOrderBoard.cs:9` is this round's example.
- **Door hardening is still owed before M6 is done:** the duplicate-submit race over HTTP, `AlreadyKnown → 201`, the blank Kestrel-URL check, and `MapGet` on the wrong route (record 29 §11, review 20 issues 3–5). A pure stress probe can now cover the lock race (mutation f′ is the shape); the HTTP pins need `Microsoft.AspNetCore.Mvc.Testing`.

---

## End-product check

This slice is the bridge M6 exists for: the WMS can now cause fleet work. REST at the business edge, an in-memory order/task pair in pure `FleetCore`, MQTT untouched — the same split the spec calls for, and the same `TaskBoard` the M3 dispatcher will drain and record 13 will persist. The risk it introduces is precisely the risk record 29 §11 names: the new composite write is guarded by a lock that the committed suite does not test, and the task's time is not asserted at all, so a regression in either would be found only at runtime. Neither is a design flaw — the shape is right — but both are the kind of gap that becomes inherited once the dispatch and status-push slices start reading this task. The next slice adds a *reader* of the board (`TaskDispatcher` on the serve-loop thread) while HTTP still writes from request threads; that is the moment the read-side thread-safety question record 29 §5 deliberately left open becomes live. Close issues 1–3 while they are three assertions, and land the door-hardening slice before M6 is called done.

---

## README

**Not updated.** The verdict is *pass with changes*: no blockers, but three test-strength should-fixes remain (plus two considers), so per AGENTS.md the M6 row stays `in progress`. The current M6 row also references review 20's outcomes; it is left exactly as it is, and nothing in `README.md` changed — verified with `git status --short` (clean) after writing this file. M6 is **not** marked done: order → queued task is one input half of M6; order status back to the WMS, cancellation, slow/dead-WMS behaviour, and the door-hardening slice are still owed.
