# Lesson 22: The order state ladder

**Milestone:** M6 (WMS integration) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 21: From an accepted order to a fleet task](21-wms-order-to-fleet-task.md) · **Decision:** [record 30](../decisions/30-order-state-ladder.md)

**Concepts this assignment requires:**

- [ ] **The ladder is one plain rule.** Which order word follows which task word, and why the order can only climb. Taught in [1) The ladder is one plain rule](#1-the-ladder-is-one-plain-rule).
- [ ] **The sync applies that rule to one order, inside the board's lock.** What it returns, what it writes, and what it refuses. Taught in [2) The sync applies the rule under the lock](#2-the-sync-applies-the-rule-under-the-lock).
- [ ] **The trigger is a push from whoever moved the task.** Who calls it, when, and why nothing in `serve` calls it yet. Taught in [3) The trigger is a push](#3-the-trigger-is-a-push).

Three new concepts. That is the cap.

**Not in this lesson:** status push to the WMS; cancelling an order; running a task inside `serve` (the dispatcher wiring); persistence; load/unload commands; the dashboard; the deferred door-hardening slice; and **any contract change** — `ContractVersion.Current` stays `4`, and both `docs/contract/v4.md` and `docs/contract/wms-rest.md` are untouched.

**The end-product fact:** after lesson 21, an accepted order makes exactly one queued task, and the order's `State` is set to `received` — once, at intake, and never again. Record 29 §6 wrote down the ladder — the fixed order the two states should move in together — and nothing in the code walks it. So a pallet could move, a task could finish, and the WMS would still be told `received`. This lesson makes the order's state follow its task's state.

---

## Where this fits

M6 says: *"orders flow from the WMS to completed pallets **and back**, and a WMS outage or a bad order does not corrupt fleet state."*

Lesson 20 built the way in. Lesson 21 turned an accepted order into work. This lesson makes the order's own state tell the truth about that work. The slice after this pushes those words to the WMS. After that, the execution slice makes a task actually run — and that is when this ladder gets its first production caller.

**One measured fact shapes this whole lesson.** Nothing in `serve` moves a task today. The class that moves tasks exists, and it drains whatever board you hand it. But `RunServeAsync` never builds one:

```bash
grep -rn "Dispatcher\|RunLoop\|Repository\|AssignNext" src/FleetManager/Program.cs
# no matches (exit code 1)

grep -rn "new TaskDispatcher" src --include=*.cs
# 13 matches, all of them under src/FleetManager.Tests/
```

So an order's task sits `Queued` forever in a running fleet manager. That is not this lesson's bug to fix — wiring the dispatcher into `serve` is the execution slice, and M6 still needs it. It does mean this lesson has **no production caller yet**. The tests are the callers. Concept 3 says exactly where the first real one will go, so the execution slice never has to invent it.

**Forward references.** The status-push slice reads `order.State` and keys each update on this slice's `Advanced` outcome. The execution slice adds `SyncFromTask` to its transition lines. M7's dashboard shows `order.State` through `TryGet`. The cancel slice owns the one reverse direction (order → task) and the `cancelled` word, which this ladder deliberately does not map.

---

## Verification table

Everything below was checked today (2026-10-20), in this repo or in scratch copies under `/tmp/opencode`. Scratch copies were deleted afterwards. Nothing was left running.

| Claim | How I checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| Baseline builds clean | copy of `src` in `/tmp/opencode/l22base`; `dotnet build` | **0 Warning(s), 0 Error(s)** |
| Baseline pure suite is green | same copy; `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0; **Failed: 0, Passed: 163, Total: 163** (FleetCore 39, FleetManager 56, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10) |
| The order's state is written once, at intake | `grep -rn "WmsOrderStates\." src --include=*.cs` | one production line: `src/FleetCore/WmsOrderBoard.cs:25`; plus the literal pin `WmsContractLiteralsTests.cs:7` |
| Nothing in `serve` drains the task board | `grep -rn "Dispatcher\|RunLoop\|Repository\|AssignNext" src/FleetManager/Program.cs` | no matches (rc 1) |
| The dispatcher is built only in tests | `grep -rn "new TaskDispatcher" src --include=*.cs` | 13 hits, all in `src/FleetManager.Tests/` |
| The dispatcher drains any board it is handed | read `src/FleetManager/TaskDispatcher.cs:5,12` | `TaskDispatcher(TaskBoard board, IFleetTaskRepository repository)`; `_board.NextQueued()` |
| `WmsOrder.State` cannot just be assigned | scratch `/tmp/opencode/l22init`: `o.State = "done";` | `error CS8852: Init-only property or indexer 'WmsOrder.State' …` |
| Replacing a stored record works | the toy listing printed below, scratch `/tmp/opencode/l22toy`; `dotnet run --project l22toy` | RC 0; output shown in the worked example |
| All five order words already exist | read `src/FleetCore/WmsOrderStates.cs` | `received`, `in_progress`, `done`, `failed`, `cancelled` |
| One place builds a `WmsOrder` | `grep -rn "new WmsOrder(" src --include=*.cs` | one hit: `src/FleetCore/WmsOrderBoard.cs:23` |
| The task ladder's only backwards edge | read `src/FleetCore/FleetTask.cs:27-98` | `Requeue` only from `Assigned`; `Fail` rejects `Done`/`Failed`; `Assign` only from `Queued`; `Start` only from `Assigned`; `Complete` only from `InProgress` |
| The mapping-switch has a house precedent | read `src/FleetCore/TaskState.cs:14-22` | `TaskStateWire.ToWireName`, ending `_ => throw new ArgumentOutOfRangeException(...)` |
| The REST page already documents `state` | `grep -n "state" docs/contract/wms-rest.md` | field table row at `:33`, example body at `:42` |
| The contract did not move | `grep -n "Current = " src/Contracts/MessageTypes.cs` | `public const int Current = 4;` |
| The ladder toy behaves as specified | the listing printed below, scratch `/tmp/opencode/l22toy`; `dotnet run --project l22toy` | RC 0; output shown in the worked example, verbatim |
| A skipped ladder step is visible in the toy | `/tmp/opencode/l22toy-mut`, the one branch changed; `dotnet run --project l22toy-mut` | RC 0; the `started` line reports `AlreadyThere` instead of `Advanced` |
| The lock does real work | `/tmp/opencode/l22probe`: 8 threads × 2000 orders, each synced to `in_progress` | with the lock: **PASS 3 of 3** (`orders=16000 in_progress=16000`); locks removed (`/tmp/opencode/l22probe-mut`): **unhandled crash 3 of 3** — each run died with `InvalidOperationException: ...corrupted its state.` or `KeyNotFoundException: The given key 'ORD-…' was not present in the dictionary.` (which of the two varies by run) |
| Working tree before writing | `git status --short`, `git log --oneline -3` | clean; HEAD `ea14b8c` |

Not yet verified: the code this lesson asks you to build. That is the assignment.

---

## Options survey (design choices)

### Survey 1: Where does the ladder rule live?

| | A: its own file `src/FleetCore/WmsOrderLadder.cs` (chosen) | B: a method on `WmsOrderStates` | C: a private method inside `WmsOrderBoard` |
|---|---|---|---|
| Words stay separate from rules | **yes** — record 28 §7 says `WmsOrderStates` is vocabulary | no — the vocabulary class grows logic | no |
| Testable without building a board | **yes** | yes | no |
| One file to grep and to mutate | **yes** | yes | buried in a bigger class |
| Jobs in the board | store + lock + intake | store + lock + intake | store + lock + intake + rule |

### Survey 2: What *is* `WmsOrder.State`?

| | A: stored, replaced with `with` under the lock (chosen) | B: a settable property with a guard | C: derived from the task on every read |
|---|---|---|---|
| Type shape and REST body unchanged | **yes** | no — `State` must leave the positional list; measured `CS8852` | yes |
| The push slice gets a "it changed" moment | **yes** — the `Advanced` outcome | yes | **no** — nothing ever changes |
| Can it ever disagree with the task | only if sync is skipped (the refusal catches a backwards write) | same | never |
| Rule has one home | **yes** — in the sync | split between setter and board | n/a |

### Survey 3: How does the board learn that a task moved?

| | A: the mover calls `SyncFromTask` on the next line (chosen) | B: `FleetTask`/`TaskBoard` raise an event | C: the board catches up when someone reads |
|---|---|---|---|
| Reviewed types touched | **none** | `FleetTask` **and** `TaskBoard` | none |
| A call can be forgotten | yes — tests pin each pair, and record 30 fixes the rule now | no | no |
| Extra concepts | none | delegates, handler exceptions, subscription lifetime | writes inside a read path |
| Window where the task is ahead of the order | one line, before any other work | same window (the handler runs after the mutation) | until the next read |

Full reasons are in [record 30](../decisions/30-order-state-ladder.md).

**Confirming against the installed source, not memory.** The five task guards, the `TaskStateWire` switch, the init-only `State`, the single `new WmsOrder(` site, and the two `Program.cs` greps all come from reading and running the code above. `CS8852` came from a scratch project I compiled and deleted.

---

## Decision

[Record 30](../decisions/30-order-state-ladder.md) settles all of it:

- **A.** The rule is `WmsOrderLadder.ForTask(TaskState) -> string`, in its own `FleetCore` file. One branch per task state; returns the `WmsOrderStates` constant; unknown value throws `ArgumentOutOfRangeException`.
- **B.** The order's state is stored. On a real change the board replaces the stored record with `order with { State = target }`, inside its existing lock. The record stays immutable.
- **C.** The trigger is a push: whoever moves a task calls `SyncFromTask(taskId)` on the very next line. No event, no polling, no wiring in `Program.cs`.
- **D.** Four situations, three outcomes (`NoOrder`, `AlreadyThere`, `Advanced`), one refusal (a new `OrderRejectedException`). Rungs: `received` 0, `in_progress` 1, `done` 2, `failed` 2. No clock argument. No failure reason on the wire.
- **E.** The rung numbers live with the rule, as `public static int Rank(string orderState)` on `WmsOrderLadder`. Record 30 §5 fixed the numbers and their home: the rungs and the ladder are one rule, so putting `Rank` in the board would split it. Rejected: a private helper in the board, which hides half the rule inside the store.

Read the record before you write code. Every requirement below is decided there.

---

## The lecture

Today we are going to learn about **the ladder rule that maps a task's state to an order's state**, **the sync that applies that rule under the board's lock**, and **the push that tells the board when to run it**.

### 1) The ladder is one plain rule

A **ladder** is a list of states in a fixed order. You can climb up a ladder. You cannot climb down. The order ladder for this project was fixed twice already: record 28 §6 fixed the order's own words, and record 29 §6 lined them up next to the task's words. This is that table, one more time, because it is the whole rule:

| `FleetTask.State` | `WmsOrder.State` | Plain meaning |
|---|---|---|
| `Queued` | `received` | The fleet has the work; no shuttle started. |
| `Assigned` | `received` | A shuttle is chosen; the move has not begun. |
| `InProgress` | `in_progress` | A shuttle has begun. |
| `Done` | `done` | Finished. |
| `Failed` | `failed` | Could not finish. |

Read it as one sentence: **the order's word follows the task's word.** A store order sits `received` while its task waits. The moment a shuttle starts, the order says `in_progress`. The moment the task finishes, the order says `done`. Each row of that table is one **rung** — one step of the ladder — and concept 2 gives each rung a number so the code can tell "up" from "down".

The words themselves already exist. Here is the real vocabulary file from the repo:

```csharp
// src/FleetCore/WmsOrderStates.cs (real, lines 3-10)
public static class WmsOrderStates
{
    public const string Received = "received";
    public const string InProgress = "in_progress";
    public const string Done = "done";
    public const string Failed = "failed";
    public const string Cancelled = "cancelled";
}
```

- These are the exact strings the WMS sees in `docs/contract/wms-rest.md`.
- They are constants, not settings (record 28 §7). A wire word a client reads must not be tunable in production.
- `Cancelled` is here and stays unused. Cancel is a different slice; see concept 3.

Your rule returns these constants. It never spells the strings itself. That way a renamed constant breaks the build instead of quietly changing the wire.

**This mapping switch already has a house precedent.** Task states travel on the MQTT wire as lowercase words, and one small class does the converting:

```csharp
// src/FleetCore/TaskState.cs (real, lines 14-22)
public static string ToWireName(this TaskState state) => state switch
{
    TaskState.Queued => "queued",
    TaskState.Assigned => "assigned",
    TaskState.InProgress => "in_progress",
    TaskState.Done => "done",
    TaskState.Failed => "failed",
    _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown task state"),
};
```

- One branch per enum member. The compiler cannot warn you about a missing branch, so the `_ =>` arm throws instead of returning a guess.
- Your ladder mirrors that shape: five branches, then `_ => throw new ArgumentOutOfRangeException(...)`.
- Notice it returns **strings**, not enum members. The order's state is a string field by design (record 28 §6), because that is what the WMS body carries.

**Now the part that makes the ladder safe: the task ladder only has one backwards step.** If the task can only go forward, the order can only go forward. That is not a hope; it is written into `FleetTask`'s guards. Two of them matter most:

```csharp
// src/FleetCore/FleetTask.cs (real, lines 40-48)
public void Start(DateTime now)
{
    // EnsureInOrder; must be Assigned; State = InProgress; _lastEventAt = now
    EnsureInOrder(now);
    if (State != TaskState.Assigned)
        throw new TaskRejectedException("Cannot start a task that is not Assigned.");
    State = TaskState.InProgress;
    _lastEventAt = now;
}
```

- A task starts only from `Assigned`. If a shuttle reports "started" for a task nobody assigned, the task refuses the move.
- So `in_progress` is only ever reached by climbing from `received`'s row.

```csharp
// src/FleetCore/FleetTask.cs (real, lines 90-98)
public void Requeue(DateTime now)
{
    EnsureInOrder(now);
    if (State != TaskState.Assigned)
        throw new TaskRejectedException("Cannot requeue a task that is not Assigned.");
    AssignedShuttleId = null;
    State = TaskState.Queued;
    _lastEventAt = now;
}
```

- `Requeue` is the task ladder's only backwards step: `Assigned` back to `Queued`. M5 uses it when a shuttle faults before it moves (record 25).
- Look at the table: `Assigned` and `Queued` sit on the **same** rung, `received`. So the order does not move at all when a task is requeued. The one legal step backwards is invisible to the WMS.

Put the rest of the guards together (I read all five today; they are in the verification table) and the reachable order transitions are exactly four:

| From | To | Triggered by |
|---|---|---|
| `received` | `in_progress` | the task starts |
| `received` | `failed` | the task fails before it starts (route lost, reservation race) |
| `in_progress` | `done` | the task completes |
| `in_progress` | `failed` | the task fails mid-move |

Everything else is "no change". `done` and `failed` are both the top of the ladder: a task cannot move between them (`Fail` rejects a `Done` task), so neither can an order.

**Where this is used later.** The status-push slice reads `order.State` and sends it. The dashboard shows it. Both trust that the word is the one the table above produces.

### 2) The sync applies the rule under the lock

**Sync** is short for *synchronise*: make one thing match another. Here it means: look at the task, work out the order's word, and make the order match. It is **one-way**. The task is the master; the order follows. The order never pushes anything back into the task.

The method goes on the order board, next to `Submit`, and it runs inside the lock the board already owns:

```csharp
// src/FleetCore/WmsOrderBoard.cs (real, lesson 21 — the pattern you are extending)
public SubmitResult Submit(SubmitOrderRequest request, DateTime now)
{
    lock (_gate)
    {
        // check the dictionary, then write it — one atomic step
    }
}
```

- The lock exists because two HTTP threads can call `Submit` at the same instant, and a `Dictionary` is not safe for that.
- The sync writes the same dictionary, so it takes the same lock. Two locks would be two opinions about the same data.
- The endpoint keeps locking nothing. Review 20's question still holds: the lock lives behind the board's methods.

**What the sync does, decided as a table.** These four checks run in this order, inside the lock:

| Situation | Outcome | Write? |
|---|---|---|
| no order with that id | `NoOrder` | no |
| the ladder's word equals the stored word | `AlreadyThere` | no |
| the ladder's word is not above the stored word | throw `OrderRejectedException` | no |
| the ladder's word is above the stored word | `Advanced` | yes |

"Above" means a higher rung. The rungs are `received` 0, `in_progress` 1, `done` 2, `failed` 2. Notice `done` and `failed` share rung 2: neither becomes the other, so a different word at the same rung is refused too. Those numbers live next to the ladder rule, in `WmsOrderLadder.Rank` — the rungs *are* the ladder, so they sit with it (Decision E above).

Three of those rows deserve plain words.

**`AlreadyThere` is the "already there" case, and it is not an error.** Run the sync twice and the second run changes nothing and says so. A method that behaves like that is called **idempotent** (a word you met in lesson 05): run it twice, get the same state as running it once.

Why that matters: the sync will be called at every transition line, and some day a retry or a loop will call it twice. A method that threw or wrote on a repeat would turn a harmless duplicate into a bug.

The same row covers a fresh order too. Intake stored `received`, the task is `Queued`, the ladder says `received`. The sync agrees with intake. Intake is, in a sense, the first sync.

**`NoOrder` covers a task that no order owns.** Record 29 §3 already named this limit: today the task id *is* the order id, but a second task source — say an operator asks for a local move — would create a task with no WMS order behind it. That must not throw. There is no order to update, so there is nothing to do. Note the order of the checks: the order is looked up **first**, so an orderless task returns `NoOrder` without touching anything else.

**The refusal is a backstop, and it protects a promise.** A **backstop** is a safety net behind the normal path: you hope it never catches anything, and it is there so a rare bug fails loudly instead of quietly.

Here is the promise it guards. Once the status-push slice starts talking, the promise to the WMS is "this word only ever moves up." A backwards write would break that promise silently. So the sync refuses instead of writing, and it throws rather than returning an outcome:

- Outcomes are for **untrusted** input. `Conflict` exists because a WMS request body can be wrong; we answer, we do not crash.
- A throw is for **our own** code doing something the domain forbids — the same split record 12 drew between `ArgumentException` (a caller bug) and `TaskRejectedException` (a domain refusal).
- The new type is `OrderRejectedException`, not `TaskRejectedException`. Record 12 §3 says the assignment loop catches `TaskRejectedException` and turns it into a `Fail`. If a ladder desync arrived wearing that name, the loop would swallow our bug as an operational failure.
- It is a **backstop**: measured today, no legal task path can reach it (concept 1's four edges). It fires only if the rule itself is broken — exactly the role `TaskBoard.Add`'s duplicate check played in lesson 21.

**One line writes the change.** `WmsOrder` is an immutable record, so `State` cannot be assigned:

```csharp
// measured today, scratch project: error CS8852
o.State = "done";   // Init-only property or indexer 'WmsOrder.State' can only be assigned …
```

Instead the board replaces the stored entry:

```csharp
// the write, one line, inside lock (_gate)
_orders[taskId] = order with { State = target };
```

- `with` copies the record and changes one field. The type does not change, the constructor does not change, and the REST body keeps the field order `wms-rest.md` documents.
- Rejected alternative, so you know why: giving `WmsOrder` a setter would mean restructuring the record (`State` would have to leave the positional list) and splitting the rule between the setter and the board. Record 30 §3 has the full argument.
- One rule to remember afterwards: **hold the board, not the order.** A copy you grabbed before the sync shows the old word. `TryGet` always reads the stored copy, so readers go through it.

**No clock this time.** The method takes no `now`. Record 11 §5's rule is that time is an *input* when you store a time, and this slice stores none: `WmsOrder` has no "state changed at" field, and the REST page documents only `state` and `receivedAt`. If status push later wants a change time, the field and the `now` parameter arrive together, in that slice's record.

**Where this is used later.** The status-push slice keys "send an update" on the `Advanced` outcome: `AlreadyThere` means nothing to tell the WMS, `Advanced` means the word just moved. That is the whole reason the state is stored instead of derived (survey 2, column C).

### 3) The trigger is a push

A **trigger** is the thing that makes the sync run. There were three candidates (survey 3) and record 30 picked the plain one: **whoever moves the task calls the sync on the very next line.**

```text
the rule, fixed now for the execution slice:
transition the task  ->  SyncFromTask(taskId)  ->  next line of work
```

- The caller does not decide any words. It only says "task X just moved; catch the order up."
- The sync does the deciding, in one place, under one lock.
- Next line, before any other work. The window where the task is ahead of the order is then one line wide, and nothing else can observe it.

**Why not an event on `TaskBoard`?** It would remove the chance of a forgotten call, and it would still run *after* the task mutates — so the window survives anyway. What it buys, it pays for with edits to `FleetTask` and `TaskBoard` (record 29 §9 listed both as untouched), plus delegates, handler exceptions, and a subscription that has to be torn down on shutdown.

There is a deeper cost too: the task model would start carrying a WMS concern, which is the wrong direction for a pure domain type.

**Why not catch up on read?** Then `GET /api/orders/{id}` writes to the dictionary. Reads that mutate surprise readers, they collide with M7's dashboard polling, and the status-push slice still gets no "it changed" moment — it would have to poll and compare, which is a trigger wearing a disguise.

**Honest limit: nothing in `serve` calls this yet.** `RunServeAsync` builds a `TaskBoard` and hands it to the order board (lesson 21, `Program.cs:164-165`), and that is where its interest in tasks ends. No dispatcher, no repository, no run loop — the greps in "Where this fits" prove it. So today:

- the ladder's first row is exercised by intake, which stores `received` itself;
- the tests are the callers: they move a task by hand (`task.Assign`, `task.Start`, …) and then call `SyncFromTask`, exactly as the dispatcher will;
- the **execution slice** adds the first production call, right after each `TaskDispatcher` transition. Record 30 §4 has already written that rule down, so that slice inherits a decision instead of making one.

**This is also why `Program.cs` does not change.** The HTTP door never moves a task. It accepts orders and reads them. A door that started syncing would be reaching past its job — and the acceptance greps below prove it stayed thin.

**And this is M6's "no corruption" sentence, one piece at a time.**

- A **bad order** is refused by the validator before `Submit`, so it never gets an order or a task. Nothing to sync.
- A **WMS outage** cannot touch this code: the sync is in-memory logic with no HTTP call in it. The timeout and the retry for talking to a WMS belong to the status-push slice, with their own config keys.
- A **backwards write** is refused, so the word the WMS has already been told is never contradicted later.
- A **task with no order** returns `NoOrder`, so a future local move cannot crash the loop.
- A **restart** still drops the pair together (record 29 §5). Orders and tasks are in memory, created in one locked step, lost in one restart. There is still no half-state.

---

## Recap in plain words

- The ladder is one rule: the order's word follows the task's word, per record 29 §6's table.
- The words come from `WmsOrderStates`; the rule returns constants, never string literals of its own.
- The task ladder only steps backwards once (`Assigned → Queued`), and both of those share the `received` row — so the order ladder only ever climbs.
- `SyncFromTask` runs inside `lock (_gate)`, checks four things in order, and writes only on `Advanced`.
- `AlreadyThere` is a harmless repeat. `NoOrder` is a task no WMS order owns. A backwards target throws `OrderRejectedException` — a backstop, not a path.
- The write is `order with { State = target }`: records are replaced, not mutated. Hold the board, not the order.
- No clock and no failure reason in this slice: neither has a field to live in yet.
- The trigger is a push from whoever moved the task. Nobody in `serve` moves a task yet, so the tests call it and the execution slice wires it.

---

## Worked example (toy — NOT the assignment)

This toy shows one mechanism only: **the order's state follows the task's state, a repeat says `AlreadyThere`, an orderless task says `NoOrder`, and a backwards target is refused.** It has no MQTT, no HTTP, no real project types, and no fleet manager. The assignment is the real thing.

Create it and run it:

```bash
cd /tmp/opencode
rm -rf l22toy
dotnet new console -o l22toy
# paste every csharp listing below, in the order they appear, into l22toy/Program.cs
dotnet run --project l22toy
```

The listings below are one file read top to bottom — the prose between them is the explanation, not part of the file. `/tmp/opencode/l22toy/Program.cs`, first the flow (exactly as I ran it):

```csharp
// TOY - not the assignment. No MQTT, no HTTP, no real project types.
// It shows one idea: the order's state follows the task's state,
// and a sync that changes nothing says so instead of lying.

var board = new LadderBoard();

// An order arrives. The task is queued; the order is received.
board.Submit("ORD-1", "A01-L01");
Console.WriteLine($"accept     task={board.TaskStateOf("ORD-1"),-12} order={board.OrderStateOf("ORD-1"),-12} sync=-");

// A shuttle is chosen. The task is assigned; the order stays received.
board.Move("ORD-1", "assigned");
Console.WriteLine($"assigned   task={board.TaskStateOf("ORD-1"),-12} order={board.OrderStateOf("ORD-1"),-12} sync={board.Sync("ORD-1")}   <- same row");

// The shuttle starts moving. Now the order moves too.
board.Move("ORD-1", "in_progress");
Console.WriteLine($"started    task={board.TaskStateOf("ORD-1"),-12} order={board.OrderStateOf("ORD-1"),-12} sync={board.Sync("ORD-1")}   <- first real change");

// Sync runs again by mistake. Nothing to do, and it says so.
Console.WriteLine($"again      task={board.TaskStateOf("ORD-1"),-12} order={board.OrderStateOf("ORD-1"),-12} sync={board.Sync("ORD-1")}   <- idempotent");

// The move finishes.
board.Move("ORD-1", "done");
Console.WriteLine($"finished   task={board.TaskStateOf("ORD-1"),-12} order={board.OrderStateOf("ORD-1"),-12} sync={board.Sync("ORD-1")}   <- done");
```

- Each line moves the toy's task by hand and then syncs. That is exactly the shape the execution slice will use.
- The `sync=` column is the return value of the sync — the outcome the push slice will key on.

Then the two odd cases:

```csharp
// A local move task with no WMS order behind it (a future feature).
board.AddLocalTask("LOCAL-7");
Console.WriteLine($"local task sync={board.Sync("LOCAL-7")}   <- no order, not an error");

// A second order walked to in_progress, then the TOY task jumps backwards.
// The real FleetTask refuses that jump; the toy has no guard, so you can see the guard work.
board.Submit("ORD-2", "A02-L02");
board.Move("ORD-2", "in_progress");
board.Sync("ORD-2");
board.Move("ORD-2", "queued");   // only the toy allows this
try
{
    board.Sync("ORD-2");
    Console.WriteLine($"rewound    task={board.TaskStateOf("ORD-2"),-12} order={board.OrderStateOf("ORD-2"),-12} sync=NO THROW   <- bug");
}
catch (OrderRefused e)
{
    Console.WriteLine($"rewound    task={board.TaskStateOf("ORD-2"),-12} order={board.OrderStateOf("ORD-2"),-12} sync=threw {e.GetType().Name}   <- refused");
}

Console.WriteLine();
foreach (var id in new[] { "ORD-1", "ORD-2" })
    Console.WriteLine($"  {id}  task={board.TaskStateOf(id),-12} order={board.OrderStateOf(id)}");

record ToyOrder(string OrderId, string State);
```

- `LOCAL-7` has no order behind it, so `Sync` says `NoOrder`. It does not create an order and it does not throw.
- The `try`/`catch` exists so you can *see* the refusal. The toy's `Move` has no guards, so the task can be pushed backwards — the real `FleetTask` cannot.

Now the board. The ladder first:

```csharp
sealed class LadderBoard
{
    private readonly Dictionary<string, ToyOrder> _orders = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tasks = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    // The ladder: one pure rule, task state -> order state.
    private static string OrderStateFor(string taskState) => taskState switch
    {
        "queued" => "received",
        "assigned" => "received",
        "in_progress" => "in_progress",
        "done" => "done",
        "failed" => "failed",
        _ => throw new ArgumentException($"unknown task state {taskState}"),
    };
```

- The toy writes the strings inline because it has no constants file. The real ladder returns `WmsOrderStates` constants instead.
- Two task states share `received`, exactly as in the table. That is what makes the requeue invisible to the WMS.

The three write methods:

```csharp
    public string Submit(string orderId, string lane)
    {
        lock (_gate)
        {
            _orders.Add(orderId, new ToyOrder(orderId, "received"));
            _tasks[orderId] = "queued";
            return "Created";
        }
    }

    public void AddLocalTask(string taskId)
    {
        lock (_gate) _tasks[taskId] = "queued";
    }

    public void Move(string id, string taskState)
    {
        lock (_gate) _tasks[id] = taskState;
    }
```

- The toy's `Move` stands in for `FleetTask.Assign/Start/Complete/Fail`, which have real guards. `Submit` stands in for lesson 21's intake.

And the sync itself:

```csharp
    // The sync: read the task, map it, compare, then either change or say why not.
    public string Sync(string id)
    {
        lock (_gate)
        {
            if (!_orders.TryGetValue(id, out var order))
                return "NoOrder";

            var target = OrderStateFor(_tasks[id]);
            if (target == order.State)
                return "AlreadyThere";
            if (LadderRank(target) <= LadderRank(order.State))
                throw new OrderRefused("order cannot move backwards");

            _orders[id] = order with { State = target };
            return "Advanced";
        }
    }
```

And the rank helper it leans on:

```csharp
    private static int LadderRank(string state) => state switch
    {
        "received" => 0,
        "in_progress" => 1,
        "done" => 2,
        "failed" => 2,
        _ => -1,
    };
```

And the last two lines of the class:

```csharp
    public string OrderStateOf(string id) => _orders[id].State;
    public string TaskStateOf(string id) => _tasks[id];
}

sealed class OrderRefused(string message) : Exception(message);
```

- Four checks, in the order the decision table says: no order, already there, not above, write.
- `LadderRank` is the toy's version of "is this above that". Equal rung with a different word is refused too, which is why the comparison is `<=` and not `<`.
- `order with { State = target }` is the whole write. The record is replaced, never mutated.

Output, from the run I did:

```text
accept     task=queued       order=received     sync=-
assigned   task=assigned     order=received     sync=AlreadyThere   <- same row
started    task=in_progress  order=received     sync=Advanced   <- first real change
again      task=in_progress  order=in_progress  sync=AlreadyThere   <- idempotent
finished   task=done         order=in_progress  sync=Advanced   <- done
local task sync=NoOrder   <- no order, not an error
rewound    task=queued       order=in_progress  sync=threw OrderRefused   <- refused

  ORD-1  task=done         order=done
  ORD-2  task=queued       order=in_progress
```

Read the `sync=` column. `AlreadyThere` on the assigned line: `Assigned` shares the `received` row, so nothing moved. `Advanced` twice, on the start and the finish. `AlreadyThere` on the repeat. `NoOrder` for the local task. A refusal for the rewound one — and the last two lines show `ORD-2`'s order still `in_progress`, not dragged backwards.

Now the mutation. Change **one** branch of the toy's ladder so it skips a rung:

```csharp
        "in_progress" => "received",   // MUTATION: skip the step
```

Output, from that run:

```text
accept     task=queued       order=received     sync=-
assigned   task=assigned     order=received     sync=AlreadyThere   <- same row
started    task=in_progress  order=received     sync=AlreadyThere   <- first real change
again      task=in_progress  order=received     sync=AlreadyThere   <- idempotent
finished   task=done         order=received     sync=Advanced   <- done
local task sync=NoOrder   <- no order, not an error
rewound    task=queued       order=received     sync=NO THROW   <- bug

  ORD-1  task=done         order=done
  ORD-2  task=queued       order=received
```

The `started` line now says `AlreadyThere`. The shuttle is moving and the order still claims `received`. Nothing crashed — that is what makes this mutation dangerous: it fails in silence, which is exactly why the assignment's tests pin the `in_progress` word with a literal.

---

## Say it out loud

Answer these out loud, the way you would in an interview. No notes.

1. A store order is accepted, then a shuttle is assigned, then the shuttle starts moving. Trace who calls what, and which lock protects the order's `state` by the end.
2. Someone calls `SyncFromTask` twice in a row for the same task. What does each call return, and why is the second one not an error?
3. The task's `Assigned` state maps to `received`, not `in_progress`. What would a WMS misread if assignment moved the order to `in_progress`, and what does that have to do with the requeue path from M5?
4. A sync arrives for a task that has no order behind it. What comes back, why is it not an exception, and what would the code have to do differently if the order lookup ran *first*?
5. Why does a backwards target throw instead of returning an outcome like `Conflict` does — and name the measured `FleetTask` guard that stops that branch from ever firing today.

---

## The assignment

Make the WMS order's state follow its task's state, per [record 30](../decisions/30-order-state-ladder.md). Language: C#. The contract does **not** change: `ContractVersion.Current` stays `4`, and `docs/contract/wms-rest.md` is untouched.

### Requirements

1. **`src/FleetCore/WmsOrderLadder.cs` (new file)** — the rule, one home:
   - `public static class WmsOrderLadder` with `public static string ForTask(TaskState taskState)`.
   - One branch per `TaskState` member, returning the matching `WmsOrderStates` constant, exactly per record 29 §6's table.
   - Unknown value → `throw new ArgumentOutOfRangeException(...)`, mirroring `TaskStateWire.ToWireName`.
   - Pure: no clock, no board, no mutable statics (the only statics are the constant strings you read).

2. **`src/FleetCore/WmsOrder.cs`** — the outcome enum, beside `SubmitOutcome`:
   - `public enum OrderSyncOutcome { Advanced, AlreadyThere, NoOrder }`.

3. **`src/FleetCore/OrderRejectedException.cs` (new file)** — the refusal:
   - `public sealed class OrderRejectedException : Exception` with one `(string message)` constructor, shaped like `TaskRejectedException`.

4. **`src/FleetCore/WmsOrderBoard.cs`** — the sync:
   - `public OrderSyncOutcome SyncFromTask(string taskId)`.
   - The whole method inside the existing `lock (_gate)`.
   - Checks in record 30 §5's order: no order → `NoOrder`; target equals stored → `AlreadyThere`; target not above stored → throw `OrderRejectedException`; otherwise write and return `Advanced`.
   - "Above" uses rungs `received` 0, `in_progress` 1, `done` 2, `failed` 2, through `public static int Rank(string orderState)` on `WmsOrderLadder` itself (Decision E in the Decision section above).
   - The write is `order with { State = target }` into the dictionary. Never assign `State`.
   - No `now` parameter. No failure reason. Do not touch `Submit` or `TryGet`.

5. **`src/FleetManager/Program.cs`** — **no change at all.** No new using, no new line, no sync call. The door does not move tasks.

6. **Do not touch** `src/FleetCore/FleetTask.cs`, `src/FleetCore/TaskBoard.cs`, `src/FleetCore/WmsOrderStates.cs`, `src/FleetManager/TaskDispatcher.cs`, `src/FleetManager/RunLoop.cs`, `src/FleetManager/FleetServeLoop.cs`, `src/Contracts/`, or `docs/contract/`.

7. **Tests** (pure, no `Integration` trait), in `src/FleetCore.Tests` — keep the existing lesson-21 facts green and add at least these six:
   - `Ladder_maps_every_task_state_to_its_order_word` — all five rows, each asserted against a **literal**: `Assert.Equal("in_progress", WmsOrderLadder.ForTask(TaskState.InProgress));` and so on. The literal is the point: it pins the wire word *and* the `WmsOrderStates` constant in one assertion.
   - `Sync_after_accept_says_AlreadyThere_and_changes_nothing` — submit an order, sync its id, expect `AlreadyThere` and `"received"` (kills "advance on the wrong trigger").
   - `Sync_walks_the_order_to_in_progress_then_done` — hand-drive `Assign` → sync (expect `AlreadyThere`, still `"received"`) → `Start` → sync (expect `Advanced`, `"in_progress"`) → `Complete` → sync (expect `Advanced`, `"done"`). That single test kills "skip the rung".
   - `Sync_after_a_requeue_keeps_the_order_received` — `Assign`, `Requeue`, sync: expect `AlreadyThere` and still `"received"`.
   - `Sync_of_a_failed_task_marks_the_order_failed` — hand-drive to a fail and sync: expect `"failed"` (a task that fails before it starts goes `received → failed`; that skip is legal).
   - `Sync_of_a_task_with_no_order_returns_NoOrder` — add a bare `new FleetTask(taskId, lane, now)` to the same `TaskBoard` (no order behind it) and sync its id: expect `NoOrder`, no exception, and no order created. Then sync an id that is in **neither** board: also `NoOrder`. That second assertion is what fails if you look the task up before the order (`_tasks.Get` on an unknown id throws `KeyNotFoundException`).

### Acceptance criteria

```bash
# 1. clean build, no warnings
cd src && dotnet build

# 2. the pure suite is green. Baseline at HEAD is 163;
#    with your six new facts you land at 169 (or higher), Failed: 0.
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"

# 3. the rule lives in FleetCore, in one file, and returns the constants
test -f src/FleetCore/WmsOrderLadder.cs
grep -q "ForTask" src/FleetCore/WmsOrderLadder.cs
grep -q "WmsOrderStates.InProgress" src/FleetCore/WmsOrderLadder.cs
grep -q "WmsOrderLadder.ForTask" src/FleetCore/WmsOrderBoard.cs
grep -q "int Rank" src/FleetCore/WmsOrderLadder.cs
grep -q "WmsOrderLadder.Rank" src/FleetCore/WmsOrderBoard.cs
grep -q "OrderSyncOutcome" src/FleetCore/WmsOrder.cs
test -f src/FleetCore/OrderRejectedException.cs

# 4. the update sits inside the board's lock, and replaces the record
grep -q "SyncFromTask" src/FleetCore/WmsOrderBoard.cs
grep -q "lock (_gate)" src/FleetCore/WmsOrderBoard.cs
grep -q "with { State" src/FleetCore/WmsOrderBoard.cs

# 5. the door and the serve wiring gain no logic
! grep -q "SyncFromTask\|WmsOrderLadder\|OrderSyncOutcome" src/FleetManager/Program.cs
git diff --stat HEAD -- src/FleetManager/Program.cs     # must print nothing

# 6. the reviewed types, the vocabulary, and the wire did not move
git diff --stat HEAD -- src/FleetCore/FleetTask.cs src/FleetCore/TaskBoard.cs \
  src/FleetCore/WmsOrderStates.cs src/FleetManager/TaskDispatcher.cs \
  src/FleetManager/RunLoop.cs src/FleetManager/FleetServeLoop.cs src/Contracts   # must print nothing

# 7. the contract did not move
grep -q "Current = 4" src/Contracts/MessageTypes.cs
git diff --stat HEAD -- docs/contract                    # must print nothing

# 8. the pure split holds
! grep -rnE "Results|HttpContext|IResult" src/FleetCore --include=*.cs

# 9. the ladder tests pin the wire words as literals, not constants against themselves
grep -rq "WmsOrderLadder.ForTask" src/FleetCore.Tests --include=*.cs
grep -rE 'Assert\.Equal\("in_progress", WmsOrderLadder\.ForTask' src/FleetCore.Tests --include=*.cs
grep -rE 'Assert\.Equal\("failed", WmsOrderLadder\.ForTask' src/FleetCore.Tests --include=*.cs
grep -rq "OrderSyncOutcome.NoOrder" src/FleetCore.Tests --include=*.cs
grep -rq "OrderSyncOutcome.AlreadyThere" src/FleetCore.Tests --include=*.cs
grep -rq "Sync_walks_the_order" src/FleetCore.Tests --include=*.cs
```

**What must not change:** the 163 tests that pass today must all still pass; `ContractVersion.Current` stays `4`; `docs/contract/` and `src/Contracts/` get no edits; `Program.cs` gets no edits; `WmsOrderStates` keeps its five constants exactly as they are.

### Mutation check

Run each in a scratch copy, never in the workspace. After each mutation, run the `FleetCore.Tests` suite and note which test went red.

| # | Mutation | Expected | Verdict |
|---|---|---|---|
| a | `WmsOrderLadder`: the `InProgress` branch returns `WmsOrderStates.Received` (skip the rung) | the walk test's `"in_progress"` literal fails — `Advanced` becomes `AlreadyThere` | **must go red** |
| b | `WmsOrderLadder`: the `Queued` branch returns `WmsOrderStates.InProgress` (advance on the wrong trigger) | the accept test expecting `AlreadyThere` + `"received"` fails | **must go red** |
| c | `WmsOrderLadder`: the `Failed` branch returns `WmsOrderStates.Done` | the failed test's `"failed"` literal fails | **must go red** |
| d | `SyncFromTask`: look the task up before the order (`_tasks.Get(taskId)` first), or create a missing order instead of returning | the second half of the no-order test — syncing an id that is in **neither** board — fails with `KeyNotFoundException` instead of returning `NoOrder` | **must go red** |
| e | delete `lock (_gate)` from `SyncFromTask` | the committed suite may stay green — it is single-threaded; the scratch probe below is what shows the race | **may survive** — a survey, not a challenge |
| f | delete the backwards refusal (write the target even when it is not above the stored word) | the committed suite may stay green: no legal task path reaches this branch (measured — `Requeue` only from `Assigned`, and `Queued`/`Assigned` share a rung) | **may survive** — it is a backstop, like `TaskBoard.Add`'s duplicate check |

If a mutation that must go red survives, the test is decoration; fix the test. For (e) and (f), write down in your review notes what each would do to a real WMS — a lost update you can see, and a word moving backwards you cannot unsay — then leave the proofs to the scratch probe (e) and to the ladder test (f).

### Stretch goal

Prove the lock does real work, the same way lesson 21 proved its own lock. Write a scratch probe in `/tmp/opencode` (never in this repo): one board, 8 threads, each thread submitting 2000 of its **own** orders, setting each task to `in_progress`, then calling `SyncFromTask`. Assert that every order ends `in_progress`. Run it three times with the lock, then delete the lock and run it three times more. Keep the output in your review notes.

What I measured with that shape, three runs each way, on 2026-10-20 (`/tmp/opencode/l22probe` and `l22probe-mut`, both deleted afterwards):

```text
with the lock — all 3 runs:
orders=16000 in_progress=16000 PASS
orders=16000 in_progress=16000 PASS
orders=16000 in_progress=16000 PASS

locks removed — all 3 runs died before printing PASS. First lines of each:
Unhandled exception. System.Collections.Generic.KeyNotFoundException: The given key 'ORD-0-21' was not present in the dictionary.
Unhandled exception. System.Collections.Generic.KeyNotFoundException: The given key 'ORD-4-15' was not present in the dictionary.
Unhandled exception. System.Collections.Generic.KeyNotFoundException: The given key 'ORD-2-45' was not present in the dictionary.

(the 2nd and 3rd runs also printed, in other threads:
 System.Collections.Generic.InvalidOperationException: Operations that change non-concurrent
 collections must have exclusive access. A concurrent update was performed on this
 collection and corrupted its state. The collection's state is no longer correct.
 — which exception you see first varies by run)
```

**Not in this assignment:** status push, cancel, running tasks inside `serve`, persistence, load/unload, the dashboard, the deferred door-hardening slice, and any edit to `src/Contracts` or `docs/contract/`.

---

## What comes next

- **Status push** (the next slice): reads `order.State`, keys each send on `Advanced`, and gets its own record with the WMS URL, the timeout key, and the slow/dead-WMS behaviour M6 asks for.
- **The execution slice**: wires `TaskDispatcher` into `serve`, and adds the first production `SyncFromTask` call on the line after each transition (record 30 §4 already fixed that rule). M6's *"to completed pallets"* waits on this.
- **Cancel**: owns the reverse direction (the order tells the task to stop) and the `cancelled` word. The ladder in this lesson deliberately does not map it.
- **Door hardening**: still owed before M6 is called done, still deferred by you to its own later slice — this lesson does not touch it.
