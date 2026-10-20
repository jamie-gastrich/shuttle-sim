# Decision Record 30: The order state ladder — one pure rule, one locked sync, one push

**Date:** 2026-10-20
**Milestone:** M6 (WMS integration) — lesson 22: keeping `WmsOrder.State` in step with its task's state.
**Status:** Accepted. Append-only. Builds on [record 29](29-wms-order-to-task-mapping.md) §6 (the ladder table), §3 (task id = order id), §5 (where the pair lives), §7 (persistence timing); [record 28](28-wms-rest-front-door.md) §6 and §7 (the order ladder, `WmsOrderStates`, constants vs config); [record 11](11-task-lifecycle-and-queue.md) (the task ladder, guarded transitions, "time and identity are inputs"); [record 12](12-task-blank-inputs-and-exception-types.md) (the two error categories); [record 25](25-reassign-failed-shuttle-work.md) (`Requeue` only from `Assigned`; `Failed` is terminal); [record 02](02-message-transport.md) (REST at the business edge, MQTT to the shuttles).
**Implements:** record 29 §6's ladder line and record 28 §6's order ladder. The table itself does not move: not a supersede, not an erratum. Record 29 pinned *which* word follows *which* task state; this record decides how the code keeps that promise, and every question the table left open (who calls it, what happens on a repeat, on a backwards move, on a task with no order, and what a restart does).
**Wire contract:** **no change.** The MQTT contract stays `v4`; `ContractVersion.Current` stays `4`. `docs/contract/wms-rest.md` also does not change: no field, no route, no status code moves. The page already documents the `state` field on the order (`wms-rest.md:33`) and record 28 §6 already fixed its five words. This slice changes the *value* the field carries over time, not its shape — exactly what the ladder table said it would do.
**Replaced by:** later records that push order status to the WMS, that add a cancel route, that wire task execution into `serve`, or that reopen `Failed`. Append-only.

---

## 1. What we are deciding

M6's done-when is *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."* Lesson 21 got the first half started: an accepted order creates exactly one queued `FleetTask`. The order's own `State` field, though, is written **once** — at intake, to `WmsOrderStates.Received` — and nothing in the codebase ever writes it again. Measured today: `grep -rn "WmsOrderStates\." src --include=*.cs` returns one production site, `WmsOrderBoard.cs:25`, the intake line.

So a pallet can move, a task can finish, and the WMS would still be told `received`. Record 29 §6 wrote the alignment down so the status-push slice would never have to guess. This record makes it real:

- **A.** Where the ladder rule lives, and what it returns.
- **B.** What `WmsOrder.State` is: a stored value you replace, a mutable value you set, or a value derived at read time.
- **C.** How the order board learns that a task moved (the trigger).
- **D.** What one sync does in every situation: first time, twice, backwards, and a task with no order.
- **E.** Whether the task's `FailureReason` reaches the WMS.
- **F.** What this slice deliberately does not do.

Everything here is `FleetCore`: no broker, no database, no HTTP, no clock. The contract does not move.

---

## 2. Decision A: one pure function, `WmsOrderLadder.ForTask`, in its own `FleetCore` file

Options:

- **A: a new static class `WmsOrderLadder` in `src/FleetCore/WmsOrderLadder.cs`, with one method `public static string ForTask(TaskState taskState)`.** (chosen)
- **B: a method on the existing `WmsOrderStates` class.**
- **C: a private method inside `WmsOrderBoard`.**

**Decision A.** The rule is one `switch` over `TaskState`. Each branch returns the matching `WmsOrderStates` constant; an unknown value throws `ArgumentOutOfRangeException`, mirroring `TaskStateWire.ToWireName` (`TaskState.cs:21`).

Three reasons:

1. **Vocabulary and rule are different jobs.** Record 28 §7 put the state *words* in `WmsOrderStates` because they are wire vocabulary — constants, not config. A mapping from one vocabulary to another is decision logic, the same way `TaskStateWire` maps an enum to wire words. Two jobs, two homes.
2. **One file to read, one file to grep, one file to mutate.** The reviewer pins the rule's home with `test -f src/FleetCore/WmsOrderLadder.cs`; the mutation harness has a single target.
3. **The board stays a store with a lock.** Rejected C would put the mapping, the lock, the dictionary, and the intake rule in one class — four jobs, and no way to test the rule without building a board first. Rejected B is the smaller sin: it works, but it grows the vocabulary class into a logic class and leaves a reader unsure whether `WmsOrderStates` is still just the wire words.

**What would change this:** a second mapping (for example, task state → a *dashboard* colour, or order state → a SQL enum) — if several rules share the same two vocabularies, collect the rules in one `*Ladder`/`*Map` file rather than scattering one method per vocabulary class.

---

## 3. Decision B: the order's state is stored, replaced under the lock with `with`

Options:

- **A: keep `WmsOrder` an immutable record; when the ladder says a new word, store `order with { State = target }` back into the dictionary, inside the board's lock.** (chosen)
- **B: give `WmsOrder` a settable `State` and a guarded `MoveTo` method, like `FleetTask`.**
- **C: do not store the order's state at all — derive it from the task on every read.**

**Decision B (A).** `WmsOrder.State` is a positional record property, so it is `init`-only. Measured today: writing `o.State = "done"` fails to compile with `error CS8852` (scratch `/tmp/opencode/l22init`, deleted afterwards). So option B is not an edit; it is a type restructure — `State` has to leave the positional list, which changes the constructor at `WmsOrderBoard.cs:23` and every construction site, and turns a data snapshot into a half-mutable object.

1. **A stored value gives the push slice a moment of change.** `SyncFromTask` returns `Advanced` on the one call that actually wrote a new word. That is the event status push keys on. Option C (derive at read) cannot produce that moment: nothing ever changes, so nothing ever signals — and a "push on read" is a poll with extra steps.
2. **A record stays a record.** `WmsOrder` is the response body of two REST routes. `with` copies it with one field changed; the type's shape, its constructor, and the documented body (`state` before `receivedAt`) do not move. Option B changes the type.
3. **The rule has one home.** The compare-and-refuse logic lives in `SyncFromTask`, not in a setter on the record and not in the reader. Option B would split it between `WmsOrder.MoveTo` and the board.

Rejected C, stated fairly: it is the strongest consistency story — a derived state cannot desync, and requeue (`Assigned → Queued`) would follow for free. It is rejected because record 29 §6 and record 28 §6 both treat the order's state as a property the *order* carries, `wms-rest.md` documents `state` on the order, and the future SQL order row (record 28 §6 destination, record 29 §7 timing) would need either a join on every read or a column that nothing ever writes. The desync that C prevents is the one Decision D's refusal already catches.

**Staleness, stated as a rule.** After a `with` write, anyone still holding the old copy sees the old word. The board's `TryGet` always returns the stored copy, so the rule is: **hold the board, not the order.** The only code that holds an order reference across a call today is the `POST` handler, and it holds the copy that was current at intake. Any later reader (status push, M7 dashboard) re-reads through `TryGet`.

**What would change this:** a real need for `WmsOrder.MoveTo` — for example a cancel that moves the order without a task behind it. That is the cancel slice's record to write, and it may well add the guarded method then.

---

## 4. Decision C: the trigger is a push — whoever moves the task calls `SyncFromTask` on the next line

Options:

- **A: the code that moves a task calls `WmsOrderBoard.SyncFromTask(taskId)` immediately after the move, before any other work.** (chosen)
- **B: `FleetTask`/`TaskBoard` raise an event on every transition; the order board subscribes.**
- **C: the order board catches up on read — `TryGet` and status push compute the current word from the task each time.**

**Decision A.**

1. **It touches the fewest reviewed types.** Option B means editing `FleetTask` and `TaskBoard`, which record 29 §9 listed as untouched for the intake slice, and which carry M3's whole tested lifecycle. The ladder needs neither a callback nor a subscription.
2. **The window it leaves is one line wide, and it is the same window B leaves anyway.** The task mutates first; the order follows. B's event handler runs *after* the mutation too, so it narrows the window to nothing while adding delegates, handler exceptions, and a subscription lifetime that must not outlive the board in a long-running `serve`.
3. **C buys "cannot be forgotten" at the price of writes during reads.** A `GET /api/orders/{id}` that also mutates the dictionary is surprising, it puts a write inside a read path that M7 will hammer, and it still defers the moment push needs.

Rejected B's real cost is not complexity, it is *ownership*: the task would be pushing WMS words, and `FleetCore`'s task model would grow an integration concern it does not have today.

**The rule, written now so the execution slice cannot re-decide it:** every call site that transitions a `FleetTask` through `TaskDispatcher` (or by calling `FleetTask.Assign/Start/Complete/Fail/Requeue` directly) must call `SyncFromTask(taskId)` on the very next line, before the next piece of work. `SyncFromTask` is cheap and in-memory; there is no reason to batch it.

**Honest limit, measured.** Today **no production code moves a task at all.** `grep -rn "Dispatcher\|RunLoop\|Repository\|AssignNext" src/FleetManager/Program.cs` returns no matches; `grep -rn "new TaskDispatcher" src --include=*.cs` returns 13 matches, all under `src/FleetManager.Tests/`. The dispatcher class drains whatever `TaskBoard` it is handed (`TaskDispatcher.cs:12` calls `_board.NextQueued()`), but `RunServeAsync` never constructs one, so the order's task sits `Queued` forever. The first production `SyncFromTask` call therefore lands with the **execution slice** — the slice that wires `TaskDispatcher` into `serve` and which M6 still needs for *"to completed pallets"*. This slice builds the rule, the guard, and the seam, and proves them with tests that move a task by hand exactly as the dispatcher will.

**What would change this:** if a transition site could not reach the order board (a second process, or a task source outside `FleetCore`'s boards), the push becomes an event or a queue, and that is a new record.

---

## 5. Decision D: four situations, three outcomes, one refusal

`SyncFromTask(string taskId)` runs entirely inside the board's existing `lock (_gate)` and does exactly this:

| Situation, checked in this order | Outcome | Write? |
|---|---|---|
| no order with that id | `OrderSyncOutcome.NoOrder` | no |
| the ladder's word equals the stored word | `OrderSyncOutcome.AlreadyThere` | no |
| the ladder's word is not **above** the stored word | **throw `OrderRejectedException`** | no |
| the ladder's word is above the stored word | `OrderSyncOutcome.Advanced` | yes — replace the stored record with `order with { State = target }` |

The enum lives in `WmsOrder.cs` beside `SubmitOutcome`; its members are `Advanced`, `AlreadyThere`, `NoOrder`.

### Above means rank

"Above" needs an order, so the four order words get rungs: `received = 0`, `in_progress = 1`, `done = 2`, `failed = 2`. Two consequences worth stating:

- **Skips are allowed.** A task can fail before a shuttle ever starts (`FleetTask.Fail` rejects only `Done` and `Failed`), so `received → failed` is a legal jump of two rungs. An operator sees the same thing: an order that failed at intake never showed as in progress.
- **`done` and `failed` share the top rung, and neither becomes the other.** Both are terminal for an order. Equal rank with a *different* word is refused, so an order can never be rewritten from `done` to `failed` or back.

Rejected alternative: an explicit allowed-edge table (`received→in_progress`, `in_progress→done`, …). It is more precise, but it is a second table to keep in step with the ladder, and the ladder is already the published alignment (record 29 §6). One list of rungs, one switch.

**The rung numbers live with the rule:** `public static int Rank(string orderState)` on `WmsOrderLadder`, beside `ForTask`. Rejected: a private helper inside `WmsOrderBoard`, which would hide half the ladder (the ordering) inside the store while the other half (the mapping) sits in its own file. One rule, one file, one mutation target.

### The refusal throws, and it is a backstop

`OrderRejectedException` is a new sealed type in `src/FleetCore/OrderRejectedException.cs`, shaped like `TaskRejectedException`.

**Why not reuse `TaskRejectedException`:** record 12 §3 says the assignment loop catches `TaskRejectedException` to turn a refused move into a `Fail`. A ladder desync is not a task problem; if it arrived wrapped in the task's exception type, that catch would swallow a bug as an operational failure. `ShuttleRejectedException` (record 06) set the precedent: each domain refusal gets its own name.

**Why throw rather than return an outcome:** outcomes exist where the input is untrusted — the WMS's `Conflict` comes from a request body. `SyncFromTask` is called by our own code, after our own task move. Measured today, this branch **cannot fire** through the public API: `FleetTask.Requeue` runs only from `Assigned`, `Queued` and `Assigned` share the `received` row, and `Done`/`Failed` are terminal (`FleetTask.cs:31,44,53,63,93`). So the refusal is a backstop — the same role `TaskBoard.Add`'s duplicate check played in record 29 §4. A silent no-op would let a desync live forever while the WMS waited for a word that never comes; a returned outcome would hide it in a channel only tests read.

**What would change this:** a sanctioned backwards edge. If a later record reopens `Failed` (record 25 says "`Failed` is terminal *now*") or adds a manual "put this order back" operator action, that edge is added to the ladder deliberately, and the refusal becomes the thing that refuses *everything except the sanctioned edge*. If a caller outside our own code ever reaches this method, revisit whether a refusal should be an exception at all.

### Called twice, called early, called with no order

- **Twice (a task that transitions twice):** the second call returns `AlreadyThere` and writes nothing. Run it ten times, same result. At the task level, a *real* double transition is refused by `FleetTask` itself (`Start` on a non-`Assigned` task throws `TaskRejectedException`), so the order never sees a phantom jump.
- **Early (queued or assigned):** the ladder's first row is `received`, which is what intake already stored. The sync agrees with intake and says `AlreadyThere`. Intake is the first sync; the ladder's row 1 is what makes that true.
- **No order:** `NoOrder`, not an exception. Record 29 §3 named the ceiling — a second, non-WMS task source (a local move from the operator, for example) creates tasks with ids that no order will ever carry. A local move must not crash the fleet loop just because no WMS order stands behind it. There is a second half to this: if an order *does* exist but its task does not, `_tasks.Get` throws `KeyNotFoundException`. That is the pair backstop (record 29 §4's rule applied to the read side) and it must not be swallowed.

### No clock

`SyncFromTask` takes **no `now` parameter**. Record 11 §5's rule is that time is an input when a value is *recorded*; this slice records no time. `WmsOrder` has no "state changed at" field, and `docs/contract/wms-rest.md` documents only `state` and `receivedAt`. Adding an unused `now` would be ceremony that pretends to be a design.

**What would change this:** status push deciding the WMS needs to know *when* the state last changed. That adds a field to the order **and** a `now` parameter in the same record — the pair arrives together, with a `wms-rest.md` version bump.

---

## 6. Decision E: the task's `FailureReason` does not reach the WMS in this slice

`FleetTask.FailureReason` carries an operator-readable string (`record 11 §5`). The order body does not have a place for it: `wms-rest.md`'s field table lists `orderId`, `kind`, `palletId`, `laneId`, `state`, `receivedAt` — no reason field.

**Decision: the order carries the word `failed` and nothing else.** The reason stays on the task, where M5's alarms and M7's dashboard read it.

Rejected: adding `stateReason` to the order now. It changes a documented contract page (which this slice may not touch), and the shape of that field — name, when it is present, whether it repeats on every state — is a status-push question: the slice that actually *sends* the body decides what the body means.

**What would change this:** the status-push record deciding the WMS needs the reason to close an order, with its own `wms-rest.md` version bump and its own row in the field table.

---

## 7. How this slice upholds M6's "a WMS outage or a bad order does not corrupt fleet state"

- **A bad order never gets an order state to corrupt.** Validation refuses it before `Submit` (record 28 §4); lesson 21's intake table means a rejected order creates no task, and no task means nothing for a sync to touch.
- **A WMS outage cannot reach this code.** `SyncFromTask` is pure in-memory logic. It makes no HTTP call, so a slow or dead WMS cannot delay, block, or half-finish a ladder step. The network work — and the timeout key — arrives with the status-push record.
- **A backwards write is refused, not performed.** Once the WMS starts receiving states, the promise is that the word only ever moves forward. The refusal is what makes that promise structural rather than hopeful.
- **A task with no order is a no-op, not a crash.** `NoOrder` keeps a future local task source from taking down the loop.
- **A restart still drops the pair together.** Nothing in this slice changes record 29 §5: both boards are in memory, created together, lost together. There is still no half-state. When persistence lands (record 29 §7, the execution slice), the order row and the task row are written in one transaction — and if a restore ever leaves them disagreeing, the refusal fails loudly instead of quietly rewriting the WMS's history.

---

## 8. Scope and limits

- **No status push.** No HTTP client, no WMS URL config, no timeout key, no outbound call. Next slice, its own record.
- **No cancel.** `WmsOrderStates.Cancelled` stays an unused constant. Cancel is the one place the *order* drives the *task* — the reverse direction — and it needs its own route, its own rule, and its own record. It is separable from this ladder: the ladder is driven only by task transitions.
- **No task execution in `serve`.** No dispatcher wiring, no MQTT command, no repository. The first production `SyncFromTask` call belongs to the execution slice (§4).
- **No persistence.** Orders and tasks stay in memory, per record 29 §7.
- **No new configuration.** No topic, no route, no timeout, no retry count, no size. The state words are constants (record 28 §7), and this slice adds none.
- **No change to `FleetTask`, `TaskBoard`, `TaskDispatcher`, `RunLoop`, `FleetServeLoop`, `WmsOrderStates`, `src/Contracts`, `docs/contract/`, or `ContractVersion.Current` (= 4).**
- **No change to `src/FleetManager/Program.cs`.** The endpoint does not learn about the ladder: it never moves a task (§4), so there is nothing for it to call.

---

## 9. Verification (measured)

Checked today (2026-10-20) in the repo and in scratch copies under `/tmp/opencode` (`l22base`, `l22init`, `l22toy`, `l22toy-mut`, `l22probe`, `l22probe-mut`). All scratch copies are deleted afterwards. Nothing was left running.

| Claim | How | Result |
|---|---|---|
| SDK | `dotnet --version` | `10.0.112` |
| Baseline builds clean | copy of `src` in `/tmp/opencode/l22base`; `dotnet build` | **0 Warning(s), 0 Error(s)** |
| Baseline pure suite is green | same copy; `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0; **Failed 0, Passed 163, Total 163** (FleetCore 39, FleetManager 56, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10) |
| The order's state is written once, at intake | `grep -rn "WmsOrderStates\." src --include=*.cs` | one production site: `src/FleetCore/WmsOrderBoard.cs:25`; plus the literal pin `WmsContractLiteralsTests.cs:7` |
| Nothing in `serve` drains the task board | `grep -rn "Dispatcher\|RunLoop\|Repository\|AssignNext" src/FleetManager/Program.cs` | no matches (rc 1) |
| The dispatcher is constructed only in tests | `grep -rn "new TaskDispatcher" src --include=*.cs` | 13 matches, all under `src/FleetManager.Tests/` |
| The dispatcher drains whatever board it is handed | read `src/FleetManager/TaskDispatcher.cs:5,12` | `TaskDispatcher(TaskBoard board, IFleetTaskRepository repository)`; `_board.NextQueued()` |
| `WmsOrder.State` cannot simply be assigned | scratch `/tmp/opencode/l22init`, `o.State = "done"` | `error CS8852: Init-only property or indexer 'WmsOrder.State' …` |
| `order with { State = … }` compiles and runs | the toy in `/tmp/opencode/l22toy`; `dotnet run` | output shown in the worked example; `dotnet build` → 0 warnings |
| All five order constants already exist | read `src/FleetCore/WmsOrderStates.cs` | `received`, `in_progress`, `done`, `failed`, `cancelled` |
| One construction site for `WmsOrder` | `grep -rn "new WmsOrder(" src --include=*.cs` | one hit: `src/FleetCore/WmsOrderBoard.cs:23` |
| The task ladder has exactly one backwards edge | read `src/FleetCore/FleetTask.cs:27-98` | `Requeue` only from `Assigned`; `Fail` rejects `Done`/`Failed`; `Assign` only from `Queued`; `Start` only from `Assigned`; `Complete` only from `InProgress` → `Queued`/`Assigned` share the `received` row, so **no order-level backwards move is reachable** |
| The unknown-state arm has a house precedent | read `src/FleetCore/TaskState.cs:21` | `_ => throw new ArgumentOutOfRangeException(...)` |
| The REST page already documents `state` | `grep -n "state" docs/contract/wms-rest.md` | field table row at `:33`, example body at `:42` |
| The contract did not move | `grep -n "Current = " src/Contracts/MessageTypes.cs` | `public const int Current = 4;` |
| The ladder toy behaves as specified | `/tmp/opencode/l22toy`; `dotnet run` | output shown in the worked example |
| Skipping a rung is visible in the toy | `/tmp/opencode/l22toy-mut` (`"in_progress" => "received"`), `dotnet run` | the `started` line reports `AlreadyThere` instead of `Advanced`; output shown in the worked example |
| The lock does real work | `/tmp/opencode/l22probe`: 8 threads × 2000 orders, sync each to `in_progress` | with the lock: **PASS 3 of 3** (`orders=16000 in_progress=16000`); locks removed (`l22probe-mut`): **unhandled crash 3 of 3** — each run died with either `InvalidOperationException … corrupted its state.` or `KeyNotFoundException: The given key 'ORD-…' …` (varies by run) |
| Working tree before writing | `git status --short`, `git log --oneline -3` | clean; HEAD `ea14b8c` |

Not yet verified: the owner's implementation of §2–§6 (build, tests, greps, mutations) — that is the assignment. This record states the design.

---

## 10. What this record unblocks

- Lesson 22's assignment has no unguided product choices: where the rule lives, what the state *is*, how the board learns, the four outcomes, the rung order, the rung helper's home (`WmsOrderLadder.Rank`), the exception type, the no-clock rule, and the no-reason rule are all fixed here.
- The **status-push** slice can read `order.State` and trust it, and it can key "send an update" on the `Advanced` outcome. It does not re-derive the ladder.
- The **execution slice** has its wiring rule already written (§4): transition, then `SyncFromTask`, then the next line of work. It does not re-decide the trigger.
- The **cancel** slice knows exactly what it owns: the reverse direction, `WmsOrderStates.Cancelled`, and whatever `FleetTask` needs to stop; it is explicitly *not* this record's ladder row.
- M7's dashboard reads one field that is already correct, through `TryGet`.
