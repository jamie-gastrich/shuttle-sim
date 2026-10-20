# Review 22 — Lesson 22: the order state ladder

**Reviewer:** Reviewer agent
**Commit under review:** `3372fe1` ("M6: Lesson 22 First Attempt"), lesson material from `8e24d4f` ("M6: Lesson 22 order state ladder")
**Lesson:** [22-order-state-ladder.md](../lessons/22-order-state-ladder.md) · **Decision:** [record 30](../decisions/30-order-state-ladder.md)
**Verdict:** **pass with changes** (no blockers; record 30's decisions A–E are followed exactly, the build and pure suite are green at 169/169, and every mutation the lesson marked "must go red" is killed — but one explicit requirement assertion is missing (the discarded first `NoOrder` outcome), and two lesson deliverables (the e/f review notes and the stretch-probe output) are absent)

The build is clean and the pure suite is green: **RC 0, 0 warnings, 0 errors; Failed: 0, Passed: 169, Total: 169** on the non-integration filter (baseline 163, +6 — FleetCore 39 → 45, every other project unchanged). The ladder lives in its own `FleetCore` file and returns the `WmsOrderStates` constants; the sync sits inside the board's existing `lock (_gate)`, checks the four situations in record 30 §5's exact order, writes only with `order with { State = target }`, refuses backwards moves with the new `OrderRejectedException`, and takes no clock. `Program.cs`, the reviewed pure types, `src/Contracts`, and `docs/contract/` are untouched. All 22 numbered acceptance greps in lesson 22's sections 3–9 pass. The attempt commit also quietly fixed two cosmetics review 21 flagged (the constructor's stray leading space, the trailing whitespace in `Submit`) without being asked. What holds this back from a bare *pass* is one discarded assertion the requirement explicitly asked for, and the two notes the lesson owed with the mutation table and the stretch goal.

---

## Commands run (exact) and what they showed

| # | Command | Result |
|---|---|---|
| 1 | `git log --oneline -5 && git status --short && git diff 8e24d4f..3372fe1 --stat` | HEAD `3372fe1`; tree **clean**; the attempt touched exactly 5 files: `src/FleetCore.Tests/WmsOrderLadderTests.cs` (+103, new), `src/FleetCore/OrderRejectedException.cs` (+8, new), `src/FleetCore/WmsOrder.cs` (+1), `src/FleetCore/WmsOrderBoard.cs` (+27/−4), `src/FleetCore/WmsOrderLadder.cs` (+23, new). Nothing else. |
| 2 | `dotnet --version` | `10.0.112` |
| 3 | `cd src && dotnet build` | RC 0 — **0 Warning(s), 0 Error(s)** |
| 4 | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **RC 0 — Failed: 0, Passed: 169, Skipped: 0, Total: 169.** Per project: FleetCore **45** (was 39, **+6**), FleetManager 56, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10. |
| 5 | Lesson 22 acceptance greps, sections 3–9 (22 checks; `git diff` comparisons made against `8e24d4f`, the pre-assignment lesson commit, since the attempt is committed) | **22/22 PASS** — full table below. |
| 6 | `git diff --stat 8e24d4f..3372fe1 -- src/FleetManager/Program.cs` and `... -- src/FleetCore/FleetTask.cs src/FleetCore/TaskBoard.cs src/FleetCore/WmsOrderStates.cs src/FleetManager/TaskDispatcher.cs src/FleetManager/RunLoop.cs src/FleetManager/FleetServeLoop.cs src/Contracts` and `... -- docs/contract` | all three **empty** — door, serve wiring, reviewed types, vocabulary, and the wire did not move |
| 7 | `grep -c 'SyncFromTask("LOCAL-7")' src/FleetCore.Tests/WmsOrderLadderTests.cs`; `grep -n "^using"` over the five touched files | **1** LOCAL-7 sync call (the reported dead duplicate is gone from the committed state); **no `using` directives** in any touched file — consistent with `ImplicitUsings` + file-scoped namespaces, and no stray using survived. (The removals themselves predate the commit, so what is verified here is the committed result, not the act.) |
| 8 | Mutation spot-check (d) in scratch `/tmp/opencode/l22review` (workspace untouched; scratch **deleted** afterwards) | see "Mutation evidence" below |
| 9 | `git log --format=%B -1 3372fe1`; read of all five touched files | the commit message is the single line "M6: Lesson 22 First Attempt" — **no body, no mutation e/f notes anywhere in the commit** (see follow-up 1) |
| 10 | `pgrep -af "dotnet run\|FleetManager.dll\|mosquitto"`; `docker compose -f docker/compose.yml ps` | no stray run/broker processes (the only pgrep hit was the reviewer's own shell); **no containers**. Nothing was started: the pure suite is this lesson's gate and needs no broker. |
| 11 | `git status --short` after all work | **clean** — this review edited nothing under `src/` |

**Not run / not yet verified:** the `Category!=Integration` filter's complement (the Postgres integration suite) and the stretch-goal 8-thread lock probe as an owner artifact — the professor's own probe output is in the lesson and record 30; the owner's copy of that output is owed (follow-up 2).

---

## Acceptance criteria

### Lesson 22 assignment requirements

- [x] **R1 `src/FleetCore/WmsOrderLadder.cs` (new)** — `public static class WmsOrderLadder` with `public static string ForTask(TaskState)` (`WmsOrderLadder.cs:3,5`); one branch per `TaskState` member returning the matching `WmsOrderStates` constant (`:7-11`); `_ => throw new ArgumentOutOfRangeException(nameof(taskState), taskState, "unknown task state")` (`:12`), mirroring `TaskStateWire.ToWireName`. Pure: no clock, no board, no mutable statics.
- [x] **R2 `OrderSyncOutcome` beside `SubmitOutcome`** — `WmsOrder.cs:14` `public enum OrderSyncOutcome { Advanced, AlreadyThere, NoOrder }`. Member order and names exactly per record 30 §5.
- [x] **R3 `src/FleetCore/OrderRejectedException.cs` (new)** — `public sealed class OrderRejectedException : Exception` with one `(string message)` constructor (`OrderRejectedException.cs:3-8`), shaped like `TaskRejectedException`. Not `TaskRejectedException` — verified by read.
- [x] **R4 `SyncFromTask` on the board** — signature `public OrderSyncOutcome SyncFromTask(string taskId)`, no `now` (`WmsOrderBoard.cs:49`); whole method inside `lock (_gate)` (`:51-65`); checks in record 30 §5's order: no order → `NoOrder` (`:53-54`), target equals stored → `AlreadyThere` (`:57-58`), target not above stored → throw `OrderRejectedException` (`:60-61`), else write and return `Advanced` (`:63-64`); "above" via `WmsOrderLadder.Rank` with rungs 0/1/2/2 (`Ladder.cs:15-22`); the write is `order with { State = target }` (`:63`), never an assignment to `State`; `Submit` and `TryGet` untouched apart from whitespace/indent cleanup.
- [x] **R5 `Program.cs` — no change at all** — no ladder token appears in the file, and the diff against `8e24d4f` is empty (commands 6).
- [x] **R6 do not touch the reviewed types, the vocabulary, or the wire** — `FleetTask.cs`, `TaskBoard.cs`, `WmsOrderStates.cs`, `TaskDispatcher.cs`, `RunLoop.cs`, `FleetServeLoop.cs`, `src/Contracts/`, `docs/contract/` all diff-empty (commands 5–6); `ContractVersion.Current = 4` (grep 7.1).
- [ ] **R7 tests: six new pure facts** — **PARTIAL.** All six are present with the exact required names (`WmsOrderLadderTests.cs:19,29,42,63,77,90`), all green, none skipped, and five of the six match the assignment's assertion spec literally. The sixth (`Sync_of_a_task_with_no_order_returns_NoOrder`) has the right two-part structure but **discards the first sync's return value** (`:96`): it proves "no exception" and (via `TryGet` at `:98-99`) "no order created" for LOCAL-7, and it pins `NoOrder` only for the second, in-neither-board id (`:100`). The requirement says "sync its id: expect `NoOrder`, no exception, and no order created" — the `NoOrder` expectation for the first sync is missing. See issue 1. No lesson-listed mutation survives because of this (mutation d is killed by the second half — spot-checked, below), so this is a strength gap, not decoration.

### Acceptance greps (lesson 22, sections 3–9, 22 checks)

| # | Check | Result |
|---|---|---|
| 3.1 | `test -f src/FleetCore/WmsOrderLadder.cs` | PASS |
| 3.2 | `ForTask` in `WmsOrderLadder.cs` | PASS |
| 3.3 | `WmsOrderStates.InProgress` in `WmsOrderLadder.cs` | PASS |
| 3.4 | `WmsOrderLadder.ForTask` in `WmsOrderBoard.cs` | PASS |
| 3.5 | `int Rank` in `WmsOrderLadder.cs` | PASS |
| 3.6 | `WmsOrderLadder.Rank` in `WmsOrderBoard.cs` | PASS |
| 3.7 | `OrderSyncOutcome` in `WmsOrder.cs` | PASS |
| 3.8 | `test -f src/FleetCore/OrderRejectedException.cs` | PASS |
| 4.1 | `SyncFromTask` in `WmsOrderBoard.cs` | PASS |
| 4.2 | `lock (_gate)` in `WmsOrderBoard.cs` | PASS |
| 4.3 | `with { State` in `WmsOrderBoard.cs` | PASS |
| 5.1 | no `SyncFromTask\|WmsOrderLadder\|OrderSyncOutcome` in `Program.cs` | PASS |
| 5.2 | `git diff --stat 8e24d4f..3372fe1 -- src/FleetManager/Program.cs` empty | PASS |
| 6.1 | reviewed types + `src/Contracts` diff empty | PASS |
| 7.1 | `Current = 4` in `MessageTypes.cs` | PASS |
| 7.2 | `docs/contract` diff empty | PASS |
| 8.1 | no `Results\|HttpContext\|IResult` in `src/FleetCore` | PASS |
| 9.1 | `WmsOrderLadder.ForTask` referenced in tests | PASS |
| 9.2 | `Assert.Equal("in_progress", WmsOrderLadder.ForTask…` literal pin | PASS (`WmsOrderLadderTests.cs:23`) |
| 9.3 | `Assert.Equal("failed", WmsOrderLadder.ForTask…` literal pin | PASS (`:25`) |
| 9.4 | `OrderSyncOutcome.NoOrder` in tests | PASS (`:100`) |
| 9.5 | `OrderSyncOutcome.AlreadyThere` in tests | PASS (`:37,50,72`) |
| 9.6 | `Sync_walks_the_order` in tests | PASS (`:42`) |

**22/22.**

### Record 30 decisions A–E, checked one by one

| Decision | Requirement | Result |
|---|---|---|
| A | `WmsOrderLadder.ForTask(TaskState)` in its own `FleetCore` file; constants returned; unknown → `ArgumentOutOfRangeException` | **PASS** — `WmsOrderLadder.cs:3-13`, pure, mirrors `TaskStateWire.ToWireName`'s `_ =>` arm |
| B | stored state; `order with { State = target }` under the existing lock; record stays immutable | **PASS** — `WmsOrderBoard.cs:63`; `WmsOrder.cs` unchanged apart from the enum line; no setter added |
| C | push trigger; the mover calls `SyncFromTask` on the next line; no events; no `Program.cs` wiring | **PASS** — nothing subscribes, nothing polls, the door is untouched; the tests hand-drive transitions then sync, exactly the dispatcher's future shape |
| D | four checks in the fixed order inside `lock (_gate)`; three outcomes; `OrderRejectedException` (not `TaskRejectedException`); rungs 0/1/2/2 via `Rank` on the ladder; no `now`; no failure reason | **PASS** — `WmsOrderBoard.cs:49-66`; the equality check precedes the rank check exactly per the table; the exception type is the new sealed one |
| E | `public static int Rank(string orderState)` on `WmsOrderLadder` | **PASS** — `WmsOrderLadder.cs:15-22` |

### Mutation evidence

The owner ran all six mutations in scratch; this review accepts that report for (a), (b), (c), (e), (f) — each red-set matches the lesson's "must go red" predictions, and the two survivors match the lesson's "may survive" predictions. This review independently re-ran the one mutation tied to the issue found below:

| # | Mutation | Result | Verdict | Catching test |
|---|---|---|---|---|
| d | `SyncFromTask`: `_tasks.Get(taskId)` before the order lookup (scratch `/tmp/opencode/l22review`, deleted afterwards; workspace untouched) | FleetCore **1 failed** (44/45) | **KILLED** (spot-checked; matches the owner's report) | `Sync_of_a_task_with_no_order_returns_NoOrder` — the second half, syncing `"never-existed"`, hits `KeyNotFoundException` from `_tasks.Get` instead of returning `NoOrder` |
| a, b, c | (owner-run) ladder branches mutated | red on the predicted tests | **KILLED** (accepted from the report; the catching tests' assertions were read and are the ones named) |
| e, f | lock removed / refusal deleted | 45/45 survive | **SURVIVE as predicted** — survey results, not student failures; the lesson predicted both, and the lock's real work is proven by the professor's own 8-thread probe in the lesson's verification table |

**Reading.** Mutation (d) is the interesting one precisely because of issue 1: the first half of the no-order test — the LOCAL-7 sync whose return value is discarded — would *not* catch a task-lookup-first mutation (the task exists, so `_tasks.Get` succeeds and the discarded result is still `NoOrder`). Only the second half, where the id is in neither board, goes red. The lesson predicted exactly this ("the second half of the no-order test … fails"), so the test as written still kills the designated mutation — but the first half is doing less than its name claims.

---

## Top issues, ranked

### 1. SHOULD-FIX — the LOCAL-7 sync's `NoOrder` outcome is never asserted
`src/FleetCore.Tests/WmsOrderLadderTests.cs:96`

```csharp
board.SyncFromTask("LOCAL-7");
```

The test is named `Sync_of_a_task_with_no_order_returns_NoOrder`, and requirement 7 says: *"sync its id: expect `NoOrder`, no exception, and no order created."* The test proves "no exception" (the call returns) and "no order created" (`:98-99`), but the outcome itself is thrown away. A sync that returned `AlreadyThere` (or anything else) for a task with no order behind it — while still not creating an order — would pass this test on its first half. In a real fleet, `NoOrder` is the signal a future caller (the execution loop, once local-move tasks exist) may one day branch on; a test that cannot distinguish `NoOrder` from a silent wrong outcome pins only half the contract. One line fixes it: `Assert.Equal(OrderSyncOutcome.NoOrder, board.SyncFromTask("LOCAL-7"));`. This is a weakened assertion in the same family as review 21's issues 1–3 — no lesson-listed mutation survives because of it (mutation (d) is caught by the second half, spot-checked above), so it is not a blocker, but the requirement asked for it by name.

### 2. FOLLOW-UP (non-blocker) — the mutation e/f review notes the lesson owes are absent
The lesson's mutation-check section ends: *"For (e) and (f), write down in your review notes what each would do to a real WMS — a lost update you can see, and a word moving backwards you cannot unsay."* The commit message is a single line with no body, and no file in the commit contains those notes. The lesson telegraphs the shape of the answers, but the point of the exercise is the owner producing them: for (e), a concrete lost-update story (two syncs interleave on the dictionary, one order's `Advanced` write is overwritten, the WMS is never told the pallet started — a pallet that moved in silence); for (f), the irreversibility story (once status push is live, a backwards word is a statement to the WMS that cannot be unsent — an operator sees `done`, then `in_progress` reappears, and the trust in every earlier update dies). Write both, in your own words, before the status-push lesson — that slice keys its whole send policy on this sync's outcomes.

### 3. FOLLOW-UP (non-blocker) — the stretch-goal probe output is not in any owner artifact
The stretch goal asks for the 8-thread probe with its output kept "in your review notes." No probe, no output, no notes exist in the commit. The lock's real work is already proven — the professor ran the probe (lesson verification table: 3/3 pass with the lock, 3/3 crash without) and mutation (e) surviving the committed suite was predicted — so this is an evidence-attachment gap, not a safety gap. Run the probe once yourself in `/tmp/opencode`, paste the output into your notes (or a future review-response commit to `docs/`), and delete the scratch. You will meet the same shape again in the execution slice, when the dispatcher drains the task board from the serve-loop thread while HTTP still writes.

### 4. CONSIDER — `Rank` matches string literals while `ForTask` returns constants
`src/FleetCore/WmsOrderLadder.cs:17-20`

```csharp
"received" => 0,
"in_progress" => 1,
```

`ForTask` three lines above returns `WmsOrderStates.Received` etc. — the lesson is explicit that the rule "never spells the strings itself," because a renamed constant should break the build. `Rank` spells all four. C# constant patterns would allow `WmsOrderStates.Received => 0`, keeping one vocabulary per file. The current risk is low and fails loudly rather than quietly: a rename would break the build through `ForTask` (and the tests' literal pins) first, and if it somehow reached runtime, the renamed word would hit `Rank`'s `_ =>` arm and throw. But "fails loudly at runtime" is still a worse failure mode than "fails at compile time," and the inconsistency sits in the file that is supposed to *be* the one rule. Worth aligning when the file is next touched.

### 5. NON-BLOCKING NOTES

- `src/FleetCore/WmsOrderBoard.cs:61` — the refusal message `$"The {target} is not above {order.State}"` reads awkwardly at the point a support engineer would see it ("The failed is not above done"). A backstop's log line is read under pressure; consider phrasing it as `Order at {order.State} cannot move to {target}` when the status-push slice next touches this area.
- `src/FleetCore/WmsOrderBoard.cs:10` — the constructor's `=>` continuation now sits at 7 spaces (the stray leading space on the ctor line that review 21 flagged **was** fixed — thank you — but the continuation kept its dedented shape). House continuation is 8.
- `src/FleetCore/WmsOrder.cs:16` and `WmsOrderBoard.cs:67` — both files still end without a trailing newline (carried from before; review 21 noted `WmsOrder.cs`). One keystroke whenever they are next edited.
- The order-without-task backstop (`_tasks.Get` throwing `KeyNotFoundException` when an order exists but its task does not, record 30 §5: *"it must not be swallowed"*) is correctly left uncaught in the code but has no test. That is per the lesson's spec — none of the six facts ask for it — but when persistence lands, that throw is what will reveal a half-restored pair; a one-fact test of it would be cheap insurance in the execution slice.
- The `ArgumentOutOfRangeException` arms of both ladder methods are unpinned by tests (no fact passes a bogus `TaskState` or order word). Not required by the lesson; noted so the student knows the `_ =>` arms are currently only eyeball-verified against `TaskStateWire.ToWireName`'s precedent.

---

## What is solid

- **Record 30 is followed to the letter.** Every decision point — the file, the signature, the check order inside the lock, `Rank`'s home, the `with` write, the exception type, the absent `now`, the untouched door — matches. Nothing had to be inferred; the decision record did its job and the code did not deviate by a token.
- **The six tests are the right six tests, almost literally as specified.** Literal wire pins on all five ladder rows; the walk test hand-drives `Assign → Start → Complete` with an outcome-and-word assertion at each step; the requeue test pins the invisibility of the task ladder's one backwards edge; the failed test exercises the legal `received → failed` skip. The test file mirrors the house style of `WmsOrderBoardTests.cs` line for line (same `Start`, same `Request` helper shape, behaviour-named facts, file-scoped namespace, no usings).
- **The mutation table is honest.** The four must-kill mutations kill (one spot-checked independently), and the two survivors are exactly the two the lesson predicted would survive — including the refusal deletion, which survives because no legal task path can reach it, precisely as record 30 measured. A suite that kills what it should and cannot kill what it should not is a suite that matches its design.
- **The pure split held, again.** No web types in `FleetCore`; the sync is in-memory logic with no clock and no network; the contract version and `docs/contract/` are untouched. A WMS outage cannot reach this code by construction.
- **Unprompted cleanup.** The attempt fixed review 21's constructor-indent and trailing-whitespace notes inside the same commit, without touching behaviour — `Submit`'s logic is byte-identical apart from whitespace.

---

## Explain-it check

The owner must answer these out loud, no notes, before the status-push or execution lesson. They go deeper than the lesson's own "Say it out loud" section, which the owner should also be able to answer cold.

1. **(Required — check order.)** In `SyncFromTask`, the order lookup runs before `_tasks.Get`. Mutation (d), which moves the task lookup first, is killed *only* by the second half of the no-order test — the id that is in neither board. Walk through what `_tasks.Get("LOCAL-7")` does in the mutated code and explain why the first half of the test stays green, then name the exception a local-move task id would throw at the fleet loop if the two lines were ever swapped in production.
2. **(Required — literals vs constants.)** `Ladder_maps_every_task_state_to_its_order_word` asserts `Assert.Equal("in_progress", WmsOrderLadder.ForTask(TaskState.InProgress))`. Why does comparing the return to the literal `WmsOrderStates.InProgress` pin strictly less — what silent wire change survives a constant-vs-itself assertion that the literal catches — and then answer the follow-up: `Rank` matches on literals (`"received" => 0`) while `ForTask` returns constants. Defend or condemn that inconsistency in one sentence each.
3. **(Deeper than lesson Q3.)** `Assigned` maps to `received`, and the requeue test proves the order does not move when a task goes `Assigned → Queued`. Say out loud what the WMS would have been told on the M5 fault-before-movement path if `Assigned` mapped to `in_progress` instead — trace the requeue, the sync, and the status-push send that would follow — and why sharing one rung is what makes the task ladder's only legal backwards step invisible.
4. **(Deeper than lesson Q5.)** The backwards refusal throws, and mutation (f) — deleting it — survives 45/45. Using only the public `FleetTask` API, name a sequence that reaches the refused branch (record 30 says none exists — prove it in words via the five guards). Then explain why the exception is `OrderRejectedException` and not `TaskRejectedException`: what would record 12's assignment-loop catch do to a ladder desync wearing the task's name, and what would the WMS wait for forever as a result?
5. **(The outcome split.)** `SyncFromTask` returns `Advanced` or `AlreadyThere`, and record 30 §3 says the status-push slice keys "send an update" on that split. Explain what information a `void` sync (or one returning a single `bool`) would lose, and why the state being *stored and replaced* (`order with { … }`) rather than derived from the task on every read is what makes the `Advanced` moment exist at all.
6. **(The trigger seam.)** Nothing in `serve` calls `SyncFromTask` yet. Where will the first production call go, on which line relative to the task transition, and what breaks — concretely, in the WMS's view — if the execution slice batches the syncs instead of calling on the very next line?

**Lines and blocks that look unclear, copied, or unexplained — study these:**

- `src/FleetCore.Tests/WmsOrderLadderTests.cs:96` — `board.SyncFromTask("LOCAL-7");` with the result discarded, in a test whose name says the call "returns NoOrder." Where did that line come from, and what would you change it to? (Issue 1.)
- `src/FleetCore/WmsOrderLadder.cs:17-20` — the literal arms of `Rank`. Why do these four lines spell the words when `ForTask` refuses to? (Issue 4.)
- `src/FleetCore/WmsOrderBoard.cs:60-61` — `if (WmsOrderLadder.Rank(target) <= WmsOrderLadder.Rank(order.State))`. Why `<=` and not `<` — what specific pair of words does `<` alone fail to refuse, and which M5 path makes that pair reachable?
- `src/FleetCore/WmsOrderBoard.cs:10` — `       => _tasks = tasks ?? throw …` at 7 spaces. What is the house continuation width, and why does this shape keep surviving edits? (Copy-adapt residue, fourth review running — the leading-space variant is fixed, the continuation variant is not.)
- `src/FleetCore.Tests/WmsOrderLadderTests.cs:11-16` — the `StateOf` helper asserts, then reads `order.State` on line 15. Why is the compiler satisfied that `order` is non-null there, and what would break if the `Assert.NotNull` line were deleted?

---

## Revisit list

- **A discarded return value proves only "did not throw."** Review 21's literal-pin lesson, one step further: here it was the *outcome enum* that went unpinned. Say out loud which of the three requirement-listed expectations ("NoOrder, no exception, no order created") each line of the no-order test actually pins. (Issue 1.)
- **Mutation notes are a deliverable, not a formality.** Mutations (e) and (f) surviving was predicted and is fine; inheriting the professor's one-line prediction silently is not. Write the two real-WMS stories in your own words — a lost update you can see, and a word moving backwards you cannot unsay. (Issue 2.)
- **Constant patterns exist.** `WmsOrderStates.Received => 0` compiles. Vocabulary should have one spelling per file; know when a `switch` arm can be a constant pattern. (Issue 4.)
- **Rule vs backstop, third lesson running.** `AlreadyThere` is the rule (idempotence, called at every transition); `OrderRejectedException` is the backstop (a loud bug report, unreachable through legal paths). Which slice would ever *catch* each? (Mutation (f) is the answer to why the backstop cannot be a returned outcome.)
- **The pair backstop on the read side.** An order whose task has vanished throws `KeyNotFoundException` out of `SyncFromTask` — record 30 §5 says it must not be swallowed, and nothing tests it. When persistence lands, that throw is the tripwire for a half-restored pair. Know where it surfaces.
- **Door hardening is still owed before M6 is done** (carried from reviews 20/21: the duplicate-submit HTTP race, `AlreadyKnown → 201`, the blank Kestrel-URL check, the wrong `MapGet` route), alongside status push and cancel.

---

## End-product check

This slice installs the seam M6's second half depends on: the order's stored `State` now tells the truth about its task, and the `Advanced`/`AlreadyThere`/`NoOrder` outcomes are exactly the three facts the status-push slice will key its send policy on and the execution slice will produce at every transition line. Structurally it adds no new risk — pure logic, one existing lock, no wire change, no clock, and a refusal that fails loudly if the ladder is ever broken rather than letting the WMS wait on a word that never comes. The risks it surfaces are small and honest: one test half that pins less than its name claims (issue 1, one line), two evidence artifacts the lesson asked for and did not get (issues 2–3), and the standing fact that `SyncFromTask` has no production caller yet — its first real integration is the execution slice, where record 30 §4's "transition, sync, next line" rule will be enforced only by discipline and by the tests in this file, which are shaped exactly like that future call site. Close issue 1, write the e/f notes, run the probe once, and this piece is inherited as-is.

---

## README

**Not updated.** The verdict is *pass with changes*: no blockers, but one requirement-literal assertion is missing and two lesson deliverables (mutation e/f notes, stretch-probe output) are absent, so per AGENTS.md the M6 row stays `in progress` and nothing in `README.md` was touched — verified with `git status --short` (clean) after writing this file. M6 is not close to done: status push, cancel, task execution in `serve`, WMS-outage behaviour, and the door-hardening slice are all still owed.
