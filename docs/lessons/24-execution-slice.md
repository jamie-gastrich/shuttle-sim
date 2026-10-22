# Lesson 24: The execution slice — the fleet manager runs the work

**Milestone:** M6 (WMS integration) · **Language:** C#/.NET · **Prerequisite:** [lesson 23](23-status-push.md)
**Concepts this assignment requires:**

- [ ] **The transition line** — one call, right after every task move, that tells the order and the pile what happened → [§4.1](#41-the-transition-line)
- [ ] **The picture** — the fleet manager's memory of what each shuttle is doing right now → [§4.2](#42-the-picture)
- [ ] **The tick** — the loop that turns the picture into the next move → [§4.3](#43-the-tick)

**Decision record:** [record 33](../decisions/33-execution-slice.md) · **Also read:** [record 32](../decisions/32-order-status-nullability-contract.md)

---

## Where this fits

The spec's M6 says: *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."*

Today the first half of that sentence is not true. The WMS can send an order. The fleet manager can push a word back. But **nothing runs the work**.

Measured today, before this lesson:

```
$ grep -rn "SyncFromTask\|NoteSync" src --include=*.cs | grep -v Tests
src/FleetCore/WmsOrderBoard.cs:49:    public OrderSyncOutcome SyncFromTask(string taskId)
src/FleetCore/WmsStatusOutbox.cs:8:    public void NoteSync(string taskId, OrderSyncOutcome outcome)
```

Two hits. Both are **definitions**. No production code calls them.

So what happens to an order today? The WMS posts it. The fleet manager makes a task. The task waits in the queue. The queue is never drained, because no code assigns the task to a shuttle and no code tells the shuttle to move.

This lesson is the slice that turns the front door into a fleet. The dispatcher goes into `serve`. The task actually moves. The order's word follows the task to the WMS.

This lesson also closes the two items review 23 left open for the professor: the failure policy (#5) and the "newest truth wins" fact (#6). Both land here, because both only matter once something actually runs.

---

## Verification table

Every row was measured on **2026-10-22**, on this tree, in this session.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | The committed tree builds clean | fresh copy of `src` (`git archive HEAD src`) into `/tmp/opencode/l24base`, then `dotnet build` | **0 Warning(s), 0 Error(s)** |
| 2 | The pure suite is green before this lesson | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **Failed: 0, Passed: 176, Skipped: 0, Total: 176** (FleetCore 49, FleetManager 59, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10) |
| 3 | Nothing in production calls the sync yet | `grep -rn "SyncFromTask\|NoteSync" src --include=*.cs \| grep -v Tests` | 2 hits, both definitions |
| 4 | `FleetCore` stays pure | `grep -rnE "HttpClient\|HttpRequest\|IMessageBus\|MqttMessaging" src/FleetCore --include=*.cs` | no matches (exit code 1) |
| 5 | One status topic, two subscriptions, one delivery each | real broker (`docker compose -f docker/compose.yml up -d broker`), probe with two handlers on `shuttle/+/status`, one published status | `first handler saw 1` / `second handler saw 1` |
| 6 | The bus calls **every** matching handler | read the installed source `src/MqttMessaging/MqttMessageBus.cs:84-92` | it copies the subscription list and calls each matching handler |
| 7 | How many places build a dispatcher today | `grep -rn "new TaskDispatcher" src --include=*.cs` | **13** call sites, all in the test project |
| 8 | `TryGet` never hands back a null order when it says `true` | probe against the committed tree | `TryGet(present) -> True, order is null? False` / `TryGet(absent) -> False, order is null? True` |
| 9 | The compiler cannot see that truth | same probe, `dotnet build` | **1 Warning(s)** — `CS8602` — "Dereference of a possibly null reference" |
| 10 | The attribute `[NotNullWhen(true)]` removes the warning at the same call site | scratch copy of `src/FleetCore` with the attribute added, same probe program | **0 Warning(s), 0 Error(s)** — the call site was not changed |
| 11 | The toy below really prints what it says | the toy at `/tmp/opencode/l24toy`, `dotnet run` | the six lines in §6 |

The broker was stopped again (`docker compose -f docker/compose.yml down`) and every scratch directory was deleted afterwards.

---

## Options survey

Three real choices in this slice. Each one has a cost, so each one gets a table.

### Survey 1: where does the executor live?

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. `FleetManager`** (chosen) | The loop that gives work to shuttles is a fleet-manager class. The pure part is `FleetCore`. | Low. It already has the bus, the topics and the config. | A bug in the loop can stop the whole fleet manager. | The fleet manager process owns the link to the vehicles. |
| **B. `FleetCore`** | Everything pure, so the whole slice is testable with no broker. | Medium. The executor needs the wire, so you would have to invent an interface and a fake for everything. | The pure project grows HTTP and broker knowledge, and the "pure" tests need fakes for the wire. | Rare. Decision logic is pure; the loop that drives it is not. |
| **C. A new `FleetExecutor` project** | Its own process, its own deploy. | High. New project, new test project, new references. | Two processes to run, two places to look when a task is lost. | Only when the executor really is a separate service. |

**Decision A.** `FleetCore` is measured pure today (row 4). The executor is mostly wire — waiting for an acknowledgement, reacting to a status — so it does not belong in the pure project. Full reasons: [record 33 §2](../decisions/33-execution-slice.md).

### Survey 2: how does the tick learn where the shuttles are?

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. Listen to the status topic** (chosen) | Keep the last state and position each shuttle reported. | Low. One dictionary under one lock. | If a status message is lost, the picture is stale until the next one. | Vehicles broadcast their state; the controller keeps the latest. |
| **B. Poll each shuttle** | Ask every shuttle "where are you?" on a timer. | Medium. A timer, a request per shuttle, a timeout rule. | Polls are slow and pile up when a shuttle is busy answering. | Used as a fallback, rarely as the main path. |
| **C. Add a "where are you?" message** | A new message type in the contract. | High. A contract change, a version bump, a new page. | The contract grows for information the shuttle already sends. | Only when the vehicles do not already report their position. |

**Decision A.** The shuttles already publish their state and position on every change. The fleet manager already listens to that topic for alarms. Option C would move the contract for information we already receive. Full reasons: [record 33 §5](../decisions/33-execution-slice.md).

### Survey 3: which store does `serve` use?

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. In memory, for both the order and the task** (chosen) | The pair stays together and is lost together on a restart. | Low. The repository seam is already there. | A restart forgets the orders — which is already true today. | A simulator's first cut. Real systems persist. |
| **B. PostgreSQL for the task now** | The tasks go to the database; the orders stay in memory. | Medium. A connection string, a restore on start. | **The pair splits.** After a restart the tasks are back but the orders are not, so the WMS re-submitting an order hits a duplicate task id and the door throws. | Never split the pair. |
| **C. PostgreSQL for both now** | Persist the order too. | High. A new table, a restore, the owed-words rule. | Not wrong — just a whole slice of work on its own. | The right end state. |

**Decision A**, with the honest note that the persistence work is **named and waiting**: the persistence slice owns the SQL store for both boards, the restore on start, and re-marking the words the WMS is owed. Full reasons: [record 33 §7](../decisions/33-execution-slice.md).

---

## Decision

The full decision is [record 33](../decisions/33-execution-slice.md). Here is the short version:

- **A.** The executor lives in `FleetManager`. The pure transition line lives in `FleetCore`.
- **B.** The line is one call, inside the dispatcher, right after the task's move and **before** the save. The dispatcher takes the line as a **required** constructor argument.
- **C.** The line runs after all five moves. Whether a word is owed is the ladder's answer, not the dispatcher's.
- **D.** The picture is a registry fed by the status topic the shuttles already publish on.
- **E.** The tick is one timer with three steps: finish, give, start-or-give-back.
- **E1.** The tick depends on a one-method interface (`ICommandSender`), so its tests need no broker. `ReliableCommandSender` implements it — one word added to its class declaration.
- **F.** `serve` keeps the order and the task in memory, together. The SQL store waits for the persistence slice.
- **G.** Three layers of failure policy: the WMS URL must be http, a broken push pass puts the word back, and no background task can take the process down.
- **H.** The contract does not move. `ContractVersion.Current` stays `4`.
- **I.** One new config key: `FleetManager:TaskExecutionIntervalMs`.

---

## The lecture

Today we are going to learn about **the transition line**, **the picture**, and **the tick**.

### 4.1 The transition line

**What it is.** One line of code. It runs right after a task moves. It does two things:

1. It tells the order what happened to its task.
2. It tells the pile that the WMS is now owed a word.

**Why we need it.** The task and the order are two different things. The task is the work: "move a pallet to lane A01-L07". The order is the WMS's view: "ORD-1 is received, in progress, or done". Two boards, two words.

Today the only way to tell the order that its task moved is for someone to remember to call `SyncFromTask`. If someone forgets, the task says `done` and the WMS hears nothing. Nobody finds out until a pallet is missing and somebody goes looking for it.

The line removes the chance to forget. There is exactly one place in the whole program where a task's state changes: the dispatcher. Five moves live there. The line sits after each one.

**A real-world example.** A warehouse worker scans a pallet onto a shuttle. The scanner always tells the office "pallet PAL-1 is on SH-01". The worker never has to remember it, because it is built into the scan, not into the worker's memory. Our line is that scan. It is part of the move, not a favour.

**The code.** A small pure class in `FleetCore`:

```csharp
namespace FleetCore;

public sealed class OrderSync
{
    private readonly WmsOrderBoard _board;
    private readonly WmsStatusOutbox _outbox;

    public OrderSync(WmsOrderBoard board, WmsStatusOutbox outbox)
    {
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
    }

    public OrderSyncOutcome Note(string taskId)
    {
        var outcome = _board.SyncFromTask(taskId);
        _outbox.NoteSync(taskId, outcome);
        return outcome;
    }
}
```

Line by line:

- `namespace FleetCore;` — it lives with the other pure types. It has no broker, no HTTP and no clock.
- `private readonly WmsOrderBoard _board;` — the board that holds the orders. The word changes there.
- `private readonly WmsStatusOutbox _outbox;` — the pile that remembers what the WMS is owed.
- The constructor: two dependencies, both checked for null. House shape, same as `WmsStatusPusher`'s constructor.
- `public OrderSyncOutcome Note(string taskId)` — one method, one job: report a task's move.
- `var outcome = _board.SyncFromTask(taskId);` — ask the order what it thinks now. It answers `Advanced` (the word moved), `AlreadyThere` (the word did not move) or `NoOrder` (no order for this task).
- `_outbox.NoteSync(taskId, outcome);` — tell the pile. The pile only marks anything when the outcome is `Advanced`. That rule is lesson 23's, and it still holds.
- `return outcome;` — hand the answer back, so the caller can log it.

**And the dispatcher.** The line goes inside it, right after the move:

```csharp
public async Task StartAsync(string taskId, DateTime now, CancellationToken cancellationToken)
{
    var task = _board.Get(taskId);
    task.Start(now);
    _orderSync.Note(taskId);
    await _repository.SaveAsync(task, cancellationToken);
}
```

- `var task = _board.Get(taskId);` — find the task, or throw (house shape, unchanged).
- `task.Start(now);` — **the move**. The task's word becomes `in_progress`.
- `_orderSync.Note(taskId);` — **the line**. The order learns the new word, and the pile hears that the WMS is owed something.
- `await _repository.SaveAsync(task, cancellationToken);` — write it down for a restart.

**Why the line comes before the save.** The task really moved. The shuttle is carrying the pallet. If the database is down, the WMS still deserves to hear that the pallet is moving — and the failed save is a *storage* problem that the tick reports. If the line came after the save, a broken database would also silence the WMS, and two different faults would look the same.

**Why the dispatcher, and not the loop.** The dispatcher is the only place a task's state changes. If the line lived in the loop that decides what to do, then every future caller of the dispatcher (a test, a tool, a second executor) would have to remember it. Inside the dispatcher, nobody can forget.

**And it is required, not optional.** `TaskDispatcher` takes the line as a constructor argument with no default:

```csharp
public sealed class TaskDispatcher(
    TaskBoard board,
    IFleetTaskRepository repository,
    OrderSync orderSync)
```

Measured today: there are **13** places that build a dispatcher, all in the test project. Adding a required argument makes the compiler list all 13. That is not a nuisance — that is the point. A dispatcher without a line cannot be built.

### 4.2 The picture

**What it is.** The fleet manager's memory of what each shuttle is doing right now: the last state it reported, the last place it was, and when it said so.

**Why we need it.** The tick has to answer two questions:

1. Which shuttle is free to take a job right now?
2. Has the shuttle I gave a job to actually finished it?

The fleet manager cannot ask. MQTT is one-way: it publishes and it subscribes. There is no "where are you?" question in the contract, and adding one would mean a version bump for information we already get.

But the shuttles already talk. Every shuttle publishes a status when it changes: its id, its state (`idle`, `moving`, `loading`, `unloading`, `fault`), its position, and the time. So the fleet manager listens to that topic and keeps the last word it heard from each shuttle. That is the picture.

**A real-world example.** A foreman stands at the dock with a whiteboard. Each driver calls in when they start and when they finish. The foreman does not chase anyone. They write down what they hear, and when a truck arrives they look at the board and send the nearest free driver.

**The code.** A dictionary under one lock:

```csharp
public sealed class ShuttleRegistry
{
    private readonly Dictionary<string, ShuttleSnapshot> _shuttles = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void Record(ShuttleStatus status) { /* TODO */ }

    public bool IsIdle(string shuttleId) { /* TODO */ }

    public string? PositionOf(string shuttleId) { /* TODO */ }

    public IReadOnlyList<string> FreeShuttles() { /* TODO */ }
}
```

- `_shuttles` — one entry per shuttle. `StringComparer.Ordinal` is the house rule for ids.
- `_gate` — one lock. Every read and every write takes it. The status handler runs on the bus thread; the tick runs on its own thread. They touch the same dictionary.
- `Record` — store the state, the position and the time that just arrived.
- `IsIdle` — true only if we know this shuttle **and** its last state is `ShuttleState.Idle.ToWireName()`. A shuttle we have never heard from is **not** idle. We do not send work to a shuttle we know nothing about.
- `PositionOf` — the last place we heard about, or null if we never heard.
- `FreeShuttles` — the idle shuttles, in id order. Id order means the same shuttle wins the same race every time, which makes the behaviour easy to explain and easy to test.

**Who fills it.** The status handler. It is the house shape from `FleetServeLoop.HandleStatusAsync`: read the JSON, check the envelope (contract version, message type, a message id, a shuttle id), and only then record it. Copy that shape. It already knows how to say no to a malformed message.

**Two listeners, one topic.** The alarm loop already listens to `AllStatuses`. The picture needs the same topic. Is that safe? Measured today against the real broker: two subscriptions to the same filter, one published status — **the first handler saw 1 and the second handler saw 1**. The bus keeps its own list of handlers and calls every matching one (`MqttMessageBus.cs:84-92`), and the broker delivers one copy per filter. And even if a broker delivered twice, recording the same status twice changes nothing: the picture keeps the newest word.

### 4.3 The tick

**What it is.** One loop on a timer. Every time it wakes up it asks the same three questions and takes the same three steps.

**Why we need it.** The fleet manager is a server. Nobody tells it what to do — it decides. Something has to turn "what we know" into "what we do next", on a regular beat. That something is the tick.

**A real-world example.** A traffic controller at a busy junction. Every few seconds they look: which approach is clear, which is blocked, who is waiting. Then they let one more car through. They do not re-plan the whole city every second. They take one safe step, and then they look again.

**The code.** The house loop shape, from `FleetServeLoop.RunAsync`:

```csharp
public async Task RunAsync(CancellationToken cancellationToken)
{
    using var timer = new PeriodicTimer(_interval);
    while (await timer.WaitForNextTickAsync(cancellationToken))
        await TickAsync(cancellationToken);
}
```

- `new PeriodicTimer(_interval)` — one timer, the interval from config.
- `while (await timer.WaitForNextTickAsync(cancellationToken))` — wait for the next beat. On Ctrl+C the wait throws, and that exception must escape to `Program` (record 33 §G3).
- `await TickAsync(cancellationToken)` — one tick.

And the tick itself:

```csharp
public async Task TickAsync(CancellationToken cancellationToken)
{
    await FinishFinishedWorkAsync(cancellationToken);
    await GiveWorkToFreeShuttlesAsync(cancellationToken);
}
```

**Step 1 — finish.** For every shuttle you are working with: if the picture says the shuttle is **idle** and its position is the task's lane, the work is done:

```csharp
await _dispatcher.CompleteAsync(task.TaskId, _clock(), cancellationToken);
```

Finish first, so a shuttle that has just arrived is free again in the same beat we notice it.

**Step 2 — give.** For each free shuttle, in id order: ask the dispatcher for a queued task. If there is one, send the move command for that task's lane.

**Step 3 — start, or give back.** The shuttle answers the command:

- It **acknowledged** → `StartAsync`. The task is now in progress.
- It **rejected** the command, or never answered → `RequeueAsync`. The task is still good; the shuttle is not. The task goes back in the queue and another shuttle can try.

**And the tick never dies.** An unexpected exception inside a tick is logged, and the loop carries on to the next beat. One bad task must not stop the fleet.

**The seam.** The tick sends one command and finds out whether the shuttle said yes. It must not need a broker to be tested. So the tick depends on a one-method interface, not on the bus:

```csharp
public interface ICommandSender
{
    Task<CommandSendResult> SendAsync(MoveCommandMessage command, CancellationToken cancellationToken);
}
```

`ReliableCommandSender` already has that exact method. It returns a `CommandSendResult`, and its `Status` is one of three words: `Acknowledged`, `Rejected`, or `NoAcknowledgement`. So the only change to that file is one word on the class declaration:

```csharp
public sealed class ReliableCommandSender : ICommandSender
```

The tick's tests then hand the tick a fake sender that answers `Acknowledged` and remembers the command it was given. The test needs no broker at all.

**The honest limit.** The tick waits for the acknowledgement before it does anything else. So a shuttle that is slow to answer holds up the whole fleet for that beat. A real system gives each shuttle its own worker. That is the stretch goal at the end of this lesson, not the base. The base is correct, simple, and explainable.

---

## Recap in plain words

- A task moves in exactly one place: the dispatcher. Right after each of its five moves, one line tells the order the new word and tells the pile the WMS is owed something.
- That line is a required argument of the dispatcher. You cannot build a dispatcher that forgets to say it.
- The picture is the fleet manager's memory of each shuttle: its last state and its last place. The shuttles already tell us; we just keep the newest word.
- The tick wakes up on a timer. It finishes what is finished, gives queued work to a free shuttle, and starts the task when the shuttle says yes.
- A failure never takes the fleet manager down: the WMS URL is checked at startup, a broken push pass puts the word back, and no background task can kill the process.

---

## Worked example (toy — NOT the assignment)

This toy shows the transition line and the "newest truth wins" rule on one order and one task. It has no broker, no HTTP and no shuttles. **It is not the assignment** — the assignment is much bigger.

Create it in a scratch directory (not in `src/`):

```bash
mkdir -p /tmp/opencode/l24toy
cd /tmp/opencode/l24toy
dotnet new console --force -o .
```

Add the project reference to `FleetCore` in `l24toy.csproj`:

```xml
  <ItemGroup>
    <ProjectReference Include="/home/jamie/shuttle-sim/src/FleetCore/FleetCore.csproj" />
  </ItemGroup>
```

Then put this in `Program.cs`:

```csharp
using FleetCore;

// THE TOY for lesson 24. Not the assignment. One order, one task,
// and the line that tells the WMS the order moved.

var tasks = new TaskBoard();
var board = new WmsOrderBoard(tasks);
var pile = new WmsStatusOutbox();

var now = DateTime.UtcNow;
var submit = board.Submit(
    new SubmitOrderRequest("ORD-1", "pallet-move", "PAL-1", "A01-L07"), now);

Console.WriteLine($"order {submit.Order.OrderId} word {submit.Order.State}");
var task = submit.CreatedTask!;
Console.WriteLine($"task {task.State.ToWireName()}");

// THE LINE: after every move, the order learns the new word,
// and the pile hears that the WMS is owed something.
void Note()
{
    var outcome = board.SyncFromTask(task.TaskId);
    pile.NoteSync(task.TaskId, outcome);
    Console.WriteLine($"  task {task.State.ToWireName()}, order word {Word()}, outcome {outcome}, owed {pile.PendingCount}");
}

string Word()
{
    if (!board.TryGet(task.TaskId, out var order) || order is null)
        return "?";
    return order.State;
}

task.Assign("SH-01", now);
Note();
task.Start(now);
Note();
task.Complete(now);
Note();

// THE SENDER reads the word when it sends, not when it was marked.
var claimed = pile.Claim();
Console.WriteLine($"the WMS is owed {claimed.Count} word: {string.Join(", ", claimed)}");
Console.WriteLine($"the word at send time: {Word()}");
```

Run it:

```bash
cd /tmp/opencode/l24toy
dotnet run
```

The output I measured, exactly:

```
order ORD-1 word received
task queued
  task assigned, order word received, outcome AlreadyThere, owed 0
  task in_progress, order word in_progress, outcome Advanced, owed 1
  task done, order word done, outcome Advanced, owed 1
the WMS is owed 1 word: ORD-1
the word at send time: done
```

Read it line by line:

- The order starts at `received` and the task at `queued`.
- **Assign** does not move the word. `AlreadyThere`. Nothing is owed. The WMS already knows: the `201` body said `received`.
- **Start** moves the word to `in_progress`. `Advanced`. The pile now owes one word.
- **Complete** moves the word to `done`. `Advanced` again — but the pile still owes **one** word, because the pile holds order ids, not words.
- When the sender finally goes, it reads the word **at that moment**: `done`. The WMS hears `done`, not `in_progress`. That is "the newest truth wins" — and it is review 23's item #6, now a fact you can point at.

---

## Say it out loud

Answer these out loud, with no notes. Pretend I am the CTO and I do not know the codebase.

1. You added one line inside the dispatcher, after every task move. Why is it in the dispatcher and not in the loop that decides what to do? What would go wrong in a month if it lived in the loop?
2. A task is assigned, starts, and finishes before the sender has pushed anything. How many words does the WMS hear, and what are they? Why is that the right answer?
3. Your tick waits for the shuttle's acknowledgement before it does anything else. A shuttle stops answering. What happens to the rest of the fleet, and what would you change in a real warehouse?
4. The picture says a shuttle is `idle` at `A01-L03`. The task wants `A01-L07`. What does your tick do, and why is the position part of the check?
5. Somebody sets `Wms:BaseUrl` to `ftp://wms.example/api`. What happens, and where do you stop it? Why is that better than catching the problem later?

---

## The assignment

Take the worked example and make it run **for real**, inside the fleet manager, with shuttles.

### The files you will change

| File | New? | What changes |
|---|---|---|
| `src/FleetCore/OrderSync.cs` | **new** | The pure transition line. |
| `src/FleetCore/WmsOrderBoard.cs` | edit | One attribute on `TryGet` ([record 32](../decisions/32-order-status-nullability-contract.md)). Nothing else in this file. |
| `src/FleetManager/TaskDispatcher.cs` | edit | Take the line as a required argument. Call it after all five moves. |
| `src/FleetManager/InMemoryFleetTaskRepository.cs` | **new (moved)** | The store `serve` uses. Move it out of `src/FleetManager.Tests/` and change its namespace to `FleetManager`. The test project still finds it. |
| `src/FleetManager/ShuttleRegistry.cs` | **new** | The picture. |
| `src/FleetManager/ICommandSender.cs` | **new** | A one-method interface so the tick can be tested without a broker. `ReliableCommandSender` implements it (its `SendAsync` already has the right shape). |
| `src/FleetManager/TaskExecutionLoop.cs` | **new** | The tick. |
| `src/FleetManager/WmsEndpoint.cs` | **new** | One static method that turns the configured WMS URL into a `Uri`, and refuses anything that is not http or https. |
| `src/FleetManager/WmsStatusPusher.cs` | edit | The failure policy: a broken pass puts the word back and carries on. |
| `src/FleetManager/Program.cs` | edit | Wire it all into `RunServeAsync`. |
| `src/FleetManager/appsettings.json` | edit | One new key. |
| `src/FleetManager.Tests/TaskDispatcherTests.cs` | edit | The new constructor argument. |
| `src/FleetManager.Tests/EndToEndTests.cs` | edit | The new constructor argument. |
| `src/FleetManager.Tests/RunLoopStressTests.cs` | edit | The new constructor argument. |
| `src/FleetManager.Tests/RunLoopReassignmentTests.cs` | edit | The new constructor argument. |
| `src/FleetCore.Tests/OrderSyncTests.cs` | **new** | Three facts (1–3). |
| `src/FleetManager.Tests/ShuttleRegistryTests.cs` | **new** | One fact (6). |
| `src/FleetManager.Tests/TaskExecutionLoopTests.cs` | **new** | Two facts (7–8). |
| `src/FleetManager.Tests/ExecutionSliceTests.cs` | **new** | Two facts (4–5). |
| `src/FleetManager.Tests/WmsStatusPusherTests.cs` | edit | One fact (9). |
| `src/FleetManager.Tests/WmsEndpointTests.cs` | **new** | One fact (10). |

### Requirements

**R1 — `src/FleetCore/OrderSync.cs` is the pure transition line.**
A public sealed class `OrderSync` with a constructor `(WmsOrderBoard board, WmsStatusOutbox outbox)`, both null-checked, and one public method `Note(string taskId)` that returns `OrderSyncOutcome`. It calls `SyncFromTask` then `NoteSync`, in that order, and returns the outcome. No broker, no HTTP, no clock, no `Console`.

**R2 — `TaskDispatcher` takes the line, and calls it after every move.**
`TaskDispatcher` takes `OrderSync` as a **third required constructor argument** (no default value), stored in a private field named `_orderSync`. Every one of its five methods calls `_orderSync.Note(taskId)` after the task's state changes and **before** `SaveAsync`. `AssignNextAsync` calls it with the task it just assigned. Keep the house shape of the class header — the space before the parenthesis is the house style:

```csharp
public sealed class TaskDispatcher (TaskBoard board, IFleetTaskRepository repository, OrderSync orderSync)
```

**R3 — `WmsOrderBoard.TryGet` says what it promises.**
Add `[NotNullWhen(true)]` to the `out` parameter of `TryGet`, and `using System.Diagnostics.CodeAnalysis;` at the top. Nothing else in that file changes. After this, the pusher's guard may be simplified to `if (!_board.TryGet(orderId, out var order))` — but only in the same change, and only if the cold build stays warning-free.

**R4 — the store `serve` uses.**
Move `InMemoryFleetTaskRepository` from `src/FleetManager.Tests/` to `src/FleetManager/`, namespace `FleetManager`. Delete the test-project copy. The test project keeps using it (the enclosing namespace finds it). `serve` builds one of these and hands it to the dispatcher.

**R5 — the picture.**
`src/FleetManager/ShuttleRegistry.cs`, a public sealed class with a `Dictionary<string, ShuttleSnapshot>` (ordinal comparer) and one lock. It has `Record(ShuttleStatus status)`, `bool IsIdle(string shuttleId)`, `string? PositionOf(string shuttleId)` and `IReadOnlyList<string> FreeShuttles()`. A shuttle it has never heard from is not idle and is not free. `FreeShuttles()` returns the idle shuttles in id order.

**R6 — the tick.**
`src/FleetManager/TaskExecutionLoop.cs`, a public sealed class. It takes the dispatcher, the registry, an `ICommandSender`, the fleet manager id, the tick interval and a clock (`Func<DateTime>`), all null- or range-checked in the house style. It has:
- `Task HandleStatusAsync(MessageReceived message, CancellationToken cancellationToken)` — public, so a test can feed it a status without a broker. It checks the envelope the way `FleetServeLoop.HandleStatusAsync` does, then calls `registry.Record`.
- `Task TickAsync(CancellationToken cancellationToken)` — public. It finishes first, then gives.
- `Task RunAsync(CancellationToken cancellationToken)` — the `PeriodicTimer` loop, house shape.

The tick's rules, exactly:
1. **Finish.** For every shuttle it is working with whose task is `InProgress`: if the picture says the shuttle is idle **and** its position is the task's lane → `CompleteAsync`, and stop working with that shuttle.
2. **Give.** For each free shuttle in id order that it is not already working with: `AssignNextAsync`. If there is no task, stop. Otherwise remember the task, build the move command for the task's lane, and send it.
3. **Start or give back.** If the command was acknowledged → `StartAsync`. If it was rejected, not answered, or the send threw → `RequeueAsync`, and stop working with that shuttle.
4. **Never die.** An unexpected exception inside a tick is logged and the loop continues.

**R7 — the picture is fed and the command is sent.**
In `Program.RunServeAsync`: subscribe the status handler to `topics.AllStatuses` and the command sender's ack handler to `topics.AllAcks`, both **before** the tick starts. Build the move command with `MessageEnvelopes.Create(MessageTypes.MoveCommand, fleetManagerId, now)` and `new MoveCommandMessage(envelope, shuttleId, task.ToLane)`. Two traps: `MessageEnvelopes` lives in the **`Messaging`** project (`src/Messaging/MessageEnvelopes.cs`), not in `Contracts`, so the tick needs `using Messaging;`; and `RunServeAsync` does **not** read `AckTimeoutMs`, `MaxAttempts` or `BackoffMs` today (only `RunMoveAsync` does), so the tick's command sender needs a `RetryPolicy` and those three keys must be read and validated in `serve` too.

**R8 — the failure policy.**
- `WmsEndpoint` has one public static method that returns a `Uri` and throws `InvalidOperationException` when the value is not an absolute URL **or** its scheme is not http or https. `Program` uses it, and the check happens **before** the bus connects.
- `WmsStatusPusher.SendPendingAsync` catches every exception except a shutdown cancellation, puts that order id back on the pile, and logs one line. It does not stop the pass and does not lose the other orders.
- `Program` awaits every background task under the existing `catch (OperationCanceledException) when (stopping.IsCancellationRequested)` **and** a catch-all that logs the failure loudly. The process stays up.
- **How to write the log line.** This project has no logger wired up yet, so `Console.WriteLine` is the house way to write a line. Measured today: `grep -rn "ILogger" src --include=*.cs` → **0 hits**; `Program.cs` has 11 `Console.WriteLine`/`Console.Error.WriteLine` calls and `WmsStatusPusher.cs` has 5. Put the ids in the line — the order id, the shuttle id, the task id — so somebody can find it again later.

**R9 — configuration.**
Add `FleetManager:TaskExecutionIntervalMs` to `appsettings.json` (value `1000`). Read it in `RunServeAsync`, throw `InvalidOperationException` if it is less than 1, and use it as the tick's interval. No interval, timeout, topic root, fleet manager id, shuttle id or lane is written as a literal in code.

**R10 — the facts.** Ten new tests, with these exact names:

| # | Name | Project | What it pins |
|---|---|---|---|
| 1 | `OrderSync_marks_the_word_when_the_task_moved` | FleetCore.Tests | Move a task to `InProgress` → the order's word is `in_progress` and the pile owes one. |
| 2 | `OrderSync_marks_nothing_when_the_word_did_not_move` | FleetCore.Tests | Assign (the word stays `received`) → the pile owes nothing. |
| 3 | `OrderSync_of_a_task_without_an_order_marks_nothing` | FleetCore.Tests | A task with no order → `NoOrder`, and the pile owes nothing. |
| 4 | `Dispatching_a_task_moves_the_order_and_owes_a_word` | FleetManager.Tests | The dispatcher moves the order and the pile owes a word. |
| 5 | `The_newest_word_wins_the_wms_hears_done_not_in_progress` | FleetManager.Tests | Review 23's item #6: mark `in_progress`, then `done`, then send → **one** POST, and its body says `done`. Parse the body with `JsonDocument` and `GetProperty("state")`. |
| 6 | `The_picture_keeps_the_last_state_and_position_of_each_shuttle` | FleetManager.Tests | Record two statuses → the picture holds the newest state and position. |
| 7 | `The_tick_completes_a_task_when_the_shuttle_is_idle_at_the_lane` | FleetManager.Tests | Feed a status (`idle` at the task's lane), tick → the task is `done`. |
| 8 | `The_tick_gives_a_queued_task_to_a_free_shuttle` | FleetManager.Tests | Feed a status (`idle` somewhere), tick → the task is `assigned` and a move command was sent for the task's lane. |
| 9 | `A_push_pass_that_throws_keeps_the_word_and_the_next_order` | FleetManager.Tests | Review 23's item #5: a stub that throws on the first order → that order is still owed, and the second order in the same pass still goes out. |
| 10 | `The_wms_url_must_be_an_http_or_https_url` | FleetManager.Tests | `http://` and `https://` are accepted; `ftp://`, `not-a-url` and a missing value are refused. |

**R11 — what you must not touch.** `src/FleetCore/FleetTask.cs`, `TaskBoard.cs`, `WmsOrder.cs`, `WmsOrderStates.cs`, `WmsOrderLadder.cs`, `WmsStatusOutbox.cs`, `src/FleetManager/RunLoop.cs`, `FleetServeLoop.cs`, `src/Contracts/`, `docs/contract/`. The MQTT contract does not move: `ContractVersion.Current` stays `4`.

There is exactly one exception, and it is one word: `src/FleetManager/ReliableCommandSender.cs` may gain `: ICommandSender` on its class declaration, so the tick can be tested without a broker. **Its body does not move.** If you change anything else in that file, the reviewer will ask why.

### Acceptance criteria

Set the base **once, before your first edit**, so the diffs below mean something:

```bash
BASE=$(git rev-parse HEAD)
```

```bash
# 1. clean build, no warnings. --no-incremental, so this is not a no-op.
cd src && dotnet build --no-incremental

# 2. the pure suite is green. Baseline today is 176; with your ten new facts
#    you land at 186 (or higher), Failed: 0.
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"

# 3. the transition line is pure, and it is the only place the sync is called
test -f src/FleetCore/OrderSync.cs
grep -q "SyncFromTask" src/FleetCore/OrderSync.cs
grep -q "NoteSync" src/FleetCore/OrderSync.cs
! grep -qE "HttpClient|HttpRequest|IMessageBus|MqttMessaging|Console\." src/FleetCore/OrderSync.cs
grep -c "_orderSync.Note" src/FleetManager/TaskDispatcher.cs      # must be 5

# 4. the dispatcher cannot be built without the line
grep -q "OrderSync" src/FleetManager/TaskDispatcher.cs
! grep -qE "OrderSync orderSync = null|OrderSync\? " src/FleetManager/TaskDispatcher.cs

# 5. the nullability contract landed, and nothing else in that file moved
grep -q "NotNullWhen" src/FleetCore/WmsOrderBoard.cs
git diff --stat "$BASE" -- src/FleetCore/WmsOrderBoard.cs   # reviewer reads: one attribute

# 6. the picture
test -f src/FleetManager/ShuttleRegistry.cs
grep -q "lock (" src/FleetManager/ShuttleRegistry.cs
grep -q "FreeShuttles" src/FleetManager/ShuttleRegistry.cs

# 7. the tick
test -f src/FleetManager/TaskExecutionLoop.cs
grep -q "PeriodicTimer" src/FleetManager/TaskExecutionLoop.cs
grep -q "CompleteAsync" src/FleetManager/TaskExecutionLoop.cs
grep -q "AssignNextAsync" src/FleetManager/TaskExecutionLoop.cs
grep -q "StartAsync" src/FleetManager/TaskExecutionLoop.cs
grep -q "RequeueAsync" src/FleetManager/TaskExecutionLoop.cs
test -f src/FleetManager/ICommandSender.cs
grep -q "ICommandSender" src/FleetManager/TaskExecutionLoop.cs

# 8. the picture is fed and the acks are heard, before the tick starts
grep -q "AllStatuses" src/FleetManager/Program.cs
grep -q "AllAcks" src/FleetManager/Program.cs
grep -q "HandleStatusAsync" src/FleetManager/Program.cs

# 9. the failure policy
test -f src/FleetManager/WmsEndpoint.cs
grep -q "Uri.UriSchemeHttp" src/FleetManager/WmsEndpoint.cs
grep -q "Uri.UriSchemeHttps" src/FleetManager/WmsEndpoint.cs
grep -q "WmsEndpoint" src/FleetManager/Program.cs
grep -q "catch (Exception" src/FleetManager/WmsStatusPusher.cs
grep -c "catch (Exception" src/FleetManager/Program.cs        # at least 4: loop, push, web, tick

# 10. the configuration
grep -q "TaskExecutionIntervalMs" src/FleetManager/Program.cs
grep -q '"TaskExecutionIntervalMs"' src/FleetManager/appsettings.json

# 11. the contract did not move
grep -q "Current = 4" src/Contracts/MessageTypes.cs
git diff --stat "$BASE" -- src/Contracts docs/contract    # must print nothing

# 12. the reviewed types did not move
git diff --stat "$BASE" -- src/FleetCore/FleetTask.cs src/FleetCore/TaskBoard.cs \
  src/FleetCore/WmsOrder.cs src/FleetCore/WmsOrderStates.cs src/FleetCore/WmsOrderLadder.cs \
  src/FleetCore/WmsStatusOutbox.cs src/FleetManager/RunLoop.cs \
  src/FleetManager/FleetServeLoop.cs   # must print nothing

# 12b. the sender gained the interface name and nothing else
grep -q ": ICommandSender" src/FleetManager/ReliableCommandSender.cs
git diff --stat "$BASE" -- src/FleetManager/ReliableCommandSender.cs   # one declaration line

# 13. the ten facts exist
grep -rq "OrderSync_marks_the_word_when_the_task_moved" src/FleetCore.Tests --include=*.cs
grep -rq "OrderSync_marks_nothing_when_the_word_did_not_move" src/FleetCore.Tests --include=*.cs
grep -rq "OrderSync_of_a_task_without_an_order_marks_nothing" src/FleetCore.Tests --include=*.cs
grep -rq "Dispatching_a_task_moves_the_order_and_owes_a_word" src/FleetManager.Tests --include=*.cs
grep -rq "The_newest_word_wins_the_wms_hears_done_not_in_progress" src/FleetManager.Tests --include=*.cs
grep -rq "The_picture_keeps_the_last_state_and_position_of_each_shuttle" src/FleetManager.Tests --include=*.cs
grep -rq "The_tick_completes_a_task_when_the_shuttle_is_idle_at_the_lane" src/FleetManager.Tests --include=*.cs
grep -rq "The_tick_gives_a_queued_task_to_a_free_shuttle" src/FleetManager.Tests --include=*.cs
grep -rq "A_push_pass_that_throws_keeps_the_word_and_the_next_order" src/FleetManager.Tests --include=*.cs
grep -rq "The_wms_url_must_be_an_http_or_https_url" src/FleetManager.Tests --include=*.cs
```

### Mutation check

Each row is a change you make **in a scratch copy**, never in the workspace. The column says what must go red.

| # | The change | Must go red |
|---|---|---|
| a | Delete the `_orderSync.Note(taskId);` line from `CompleteAsync` | `Dispatching_a_task_moves_the_order_and_owes_a_word` |
| b | Delete the `_orderSync.Note(taskId);` line from `StartAsync` | `The_newest_word_wins_the_wms_hears_done_not_in_progress` |
| c | In `OrderSync.Note`, drop the `NoteSync` call | `OrderSync_marks_the_word_when_the_task_moved` |
| d | In `OrderSync.Note`, call `NoteSync` for **every** outcome, not only `Advanced` | `OrderSync_marks_nothing_when_the_word_did_not_move` |
| e | Remove `[NotNullWhen(true)]` from `TryGet` and keep the simplified pusher guard | the cold build in criterion 1 (CS8602) |
| f | Remove the catch-all from `SendPendingAsync` | `A_push_pass_that_throws_keeps_the_word_and_the_next_order` |
| g | In `WmsEndpoint`, drop the scheme check | `The_wms_url_must_be_an_http_or_https_url` |
| h | In the picture, treat an unknown shuttle as idle | `The_tick_gives_a_queued_task_to_a_free_shuttle` |
| i | In the tick's finish step, drop the position check | `The_tick_completes_a_task_when_the_shuttle_is_idle_at_the_lane` |

### Stretch goal

**One worker per shuttle.** Right now the tick waits for the acknowledgement, so one slow shuttle holds up the whole fleet for a beat. Change it so each shuttle has its own worker: the tick only hands work out, and the worker for that shuttle does the send, the wait and the start. Then run a test with two shuttles — one that answers straight away and one that never answers — and measure how long the fast shuttle's task takes to finish. Write the measurement down.

---

## What comes next

- **The persistence slice.** The order and the task go into SQL **together**. `serve` restores them on start and re-marks the words the WMS is owed. Until then, `serve` forgets everything on a restart — which it already does today, because the order board has always been in memory.
- **The cancel slice.** The WMS cancels an order. That needs a `Cancelled` task state and a row on the ladder.
- **The mock-WMS slice.** A WMS that is slow, a WMS that is gone, a WMS that answers `500`.
- **The traffic slice.** The tick gives work without asking the occupancy map. With one shuttle that cannot bite. With two shuttles and one lane, it can.
