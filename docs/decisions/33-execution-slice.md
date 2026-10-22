# Decision Record 33: The execution slice — the fleet manager runs the work

**Date:** 2026-10-22
**Milestone:** M6 (WMS integration) — lesson 24.
**Status:** Accepted. Append-only. Builds on [record 29](29-wms-order-to-task-mapping.md) §3 (task id = order id), §5 (the pair lives in memory, lost together) and §9 (no task execution in `serve` yet); [record 30](30-order-state-ladder.md) §4 (the push trigger rule — "no reason to batch"); [record 31](31-status-push.md) §5 (the pile keys on `Advanced`), §6 (the pile stores ids, the sender reads the word at send time), §8 (the failure shapes) and §10 (the honest limits); [record 32](32-order-status-nullability-contract.md) (the nullability contract of `WmsOrderBoard.TryGet`); [record 22](22-serve-loop-and-alarm-publish.md) (the house loop shape); [record 02](02-message-transport.md) and [record 09](09-command-acknowledgement-and-idempotency.md) (commands and acks); [record 14](14-db-test-gate-and-config.md) (the `Integration` trait gate — this slice adds none).
**Implements:** the first half of M6's done-when — *"orders flow from the WMS to **completed pallets** and back, and a WMS outage or a bad order does not corrupt fleet state."* Lesson 22 built the ladder, lesson 23 built the push. Nothing yet **ran** the work: the WMS could submit an order, and the order's word could be pushed, but no task ever left the queue. This record decides how the work runs, where the executor lives, what the fleet manager remembers, and what happens when a loop dies.
**Wire contract:** **MQTT unchanged. REST unchanged. `ContractVersion.Current` stays `4`.** The move command, the command acknowledgement and the shuttle status already exist and already carry everything the execution path needs: the lane is in the command, the state and the position are in the status, the task id is the order id (record 29 §3) and stays inside the fleet manager. `src/Contracts/`, `docs/contract/v4.md` and `docs/contract/wms-rest.md` do not move.
**Replaced by:** the persistence slice (SQL for the pair, and the restore on start), the cancel slice (the WMS cancels an order), the mock-WMS slice (the WMS that is slow and broken), and the traffic slice (the tick that reserves lanes). Append-only.

---

## 1. What we are deciding

Measured today, before this slice:

```
$ grep -rn "SyncFromTask\|NoteSync" src --include=*.cs | grep -v Tests
src/FleetCore/WmsOrderBoard.cs:49:    public OrderSyncOutcome SyncFromTask(string taskId)
src/FleetCore/WmsStatusOutbox.cs:8:    public void NoteSync(string taskId, OrderSyncOutcome outcome)
```

Two hits, both **definitions**. No production caller. The order board can learn a new word and the pile can hear about it, but nothing in the fleet manager ever calls them, because nothing in the fleet manager ever **runs a task**. The WMS can submit an order today; the order sits `queued` until the heat death of the universe.

This record decides:

- **A.** Where the executor lives — `FleetManager`, `FleetCore`, or a new project.
- **B.** The transition line: where the order learns that a task moved, and in what order.
- **C.** Which moves owe the WMS a word.
- **D.** The picture: how the fleet manager knows what each shuttle is doing.
- **E.** The tick: what the loop does every time it wakes up.
- **F.** Which store `serve` uses.
- **G.** The failure policy: the three layers that stop one bad moment from killing the fleet manager.
- **H.** That the contract does not move.
- **I.** The configuration.
- **J.** What this slice deliberately does not do, and which slice does it instead.

---

## 2. Decision A: the executor lives in `FleetManager`

**Decision A.** The executor — the loop that gives work to shuttles — is a `FleetManager` class. The pure part of the slice (the transition line) is a `FleetCore` class.

Options:

- **A: `FleetManager` for the executor, `FleetCore` for the pure line.** (chosen)
- **B: everything in `FleetCore`.**
- **C: a new project, `FleetExecutor`.**

**Decision A.** Reasons:

1. **The executor needs the wire.** It publishes a move command, it waits for an acknowledgement, and it listens for statuses. `FleetCore` has no broker, no HTTP and no clock of its own — measured today: `grep -rnE "HttpClient|HttpRequest|IMessageBus|MqttMessaging" src/FleetCore --include=*.cs` returns **no matches**. Putting the executor in `FleetCore` would break the rule the spec states in the Constraints section: *"Decision logic is kept separate from input and output (pure functions for task assignment, traffic control, and state machines) so it can be unit tested without a broker or database."*
2. **The spec's own project table says so.** `README.md` calls `FleetManager` the "Manager console" that "talks to broker + Postgres", and calls `FleetCore` the pure "Tasks, `TaskBoard`, repository interface".
3. **Option C buys nothing yet.** A new project would need its own csproj, its own test project, its own reference to `FleetManager`'s types, and it would still talk to the broker. The only gain is a name on the boundary, and the boundary is already drawn by the broker.

**Rejected B, stated fairly.** It would let the whole slice be tested without a broker. But the executor *is* mostly wire: waiting for an acknowledgement and reacting to a status are not pure functions. Putting them in `FleetCore` would either pollute the pure project or leave the pure project holding an interface nothing implements.

**Rejected C.** A separate deployable is a real product decision (a fleet manager that runs tasks as its own service). Nothing in this spec asks for it, and it costs a project boundary before the product has two processes worth separating.

**What would change this:** if the executor ever needs to run in more than one place (for example, a back-office simulator that also runs tasks), then option C earns its keep — one executor type, two hosts.

---

## 3. Decision B: the transition line — one call, right after the move, before the save

**Decision B.** A task's state changes in exactly one place: `TaskDispatcher`. The slice adds a small pure class, `FleetCore.OrderSync`, with one method:

```csharp
public OrderSyncOutcome Note(string taskId)
{
    var outcome = _board.SyncFromTask(taskId);
    _outbox.NoteSync(taskId, outcome);
    return outcome;
}
```

and calls it **after every move**, inside the dispatcher:

```csharp
public async Task StartAsync(string taskId, DateTime now, CancellationToken cancellationToken)
{
    var task = _board.Get(taskId);
    task.Start(now);
    _orderSync.Note(taskId);                      // the line
    await _repository.SaveAsync(task, cancellationToken);
}
```

**The order — move, then line, then save.** Reasons:

1. **The line describes the fleet, not the paperwork.** The task really moved (the shuttle is carrying the pallet). The WMS is owed that news even if the database is down. If the save fails, that is a *storage* problem, and the tick reports it; the pallet still moved.
2. **The line cannot be forgotten.** The dispatcher is the only place a task's state changes, so five call sites cover every move. A caller of the dispatcher cannot skip the line, because the line is inside the dispatcher.
3. **The dependency is required, not optional.** `TaskDispatcher` takes `OrderSync` as a **constructor argument with no default**. Every construction site must supply it — measured today, that is **13** call sites in the test project. The compiler listing them is the feature, not the nuisance: a new caller of the dispatcher cannot forget the line, because there is no dispatcher without one.

**Rejected: a "sync the order" call at each *use* of the dispatcher.** That puts the line in the tick, in five places, in a file that will grow. The tick would own two responsibilities — deciding what to do and telling the WMS — and a future caller of the dispatcher (a test, a tool, a second executor) would silently forget the word.

**Rejected: the dispatcher calls `SyncFromTask` and `NoteSync` directly.** Two dependencies instead of one, and the dispatcher would know about the pile (a transport concern). `OrderSync` keeps the dispatcher talking to one thing: "tell me what happened to this task".

**What would change this:** if the order board ever needed more than one listener (say, an audit log and the pile), `OrderSync` grows a list of listeners rather than becoming two calls in the dispatcher.

---

## 4. Decision C: which moves owe the WMS a word

**Decision C.** The line runs after **all five** moves. Whether a word is *owed* is decided by the ladder, not by the dispatcher. The table, read from `WmsOrderLadder.ForTask` and the lesson-23 facts:

| the task moves to | the task's word | the order's word after the line | the WMS is owed |
|---|---|---|---|
| `Queued` (requeue) | queued | received (unchanged) | nothing |
| `Assigned` | assigned | received (unchanged) | nothing |
| `InProgress` | in_progress | in_progress | one word |
| `Done` | done | done | one word |
| `Failed` | failed | failed | one word |

Reasons:

1. **The ladder already knows the answer.** `WmsOrderLadder.ForTask(taskState)` maps a task state to an order word, and `SyncFromTask` compares it with the word the order already has. `AlreadyThere` means "the word did not move" → `NoteSync` marks nothing (record 31 §5).
2. **`received` is the intake word, and intake already spoke.** The `201` body at intake told the WMS "received". Pushing "received" again would be noise (record 31 §D).
3. **The line never refuses, for the five legal moves.** `SyncFromTask` throws `OrderRejectedException` when the target word is not *above* the current one. The task's own state machine forbids the moves that would do that: `Requeue` is legal only from `Assigned`, so a task that reached `in_progress` can never go back to `received`. Measured from the committed source: `FleetTask.Requeue` requires `State == TaskState.Assigned`; `FleetTask.Complete` requires `InProgress`; `FleetTask.Fail` refuses `Done` and `Failed`.

**What would change this:** a `Cancelled` task state would need a row in this table and a word on the ladder. That is the cancel slice's business.

---

## 5. Decision D: the picture — a registry fed by the status topic

**Decision D.** The fleet manager keeps a `ShuttleRegistry`: for each shuttle it has heard from, the last state word, the last position, and when it last reported. It is fed by the contract's existing `shuttleStatus` message on the existing `AllStatuses` topic.

Options:

- **A: listen to the status topic and keep the last word per shuttle.** (chosen)
- **B: poll each shuttle over REST for its position.**
- **C: add a new "where are you?" message to the contract.**

**Decision A.** Reasons:

1. **The information already flows.** The shuttles publish their state and position on every change (the `ShuttleStatus` record: `ShuttleId`, `State`, `Position`, `Timestamp`, `FaultReason`). The fleet manager already listens to that topic for alarms (`FleetServeLoop`). Keeping the last word costs a dictionary, not a new message.
2. **Option C moves the contract for no reason.** A version bump and a new message type would be needed to learn something the shuttle already says on every move. The spec says changing the contract "needs a decision record and a version bump" — that cost is not worth it.
3. **Option B invents a second way to ask the same question.** Polling means a timer, a per-shuttle request, and a rule for "what if the answer is late". The status topic already has a rule for that: the newest word wins.

**Two subscriptions on one topic are safe.** The alarm loop and the picture both need `AllStatuses`. Measured today against the real broker (mosquitto 2.0 in Docker, probe `/tmp/opencode/l24dup`): two subscriptions to the same filter `shuttle/+/status`, one published status — **the first handler saw 1, the second handler saw 1**. The bus keeps its own list of handlers and calls every matching one (`src/MqttMessaging/MqttMessageBus.cs:84-92`), and the broker delivers one copy per filter. Even if a broker did deliver twice, recording the same status twice changes nothing: the registry keeps the newest word.

**The registry is a dictionary under one lock** — the house shape from `FleetServeLoop` (`_knownLock` plus `_knownShuttles`). A shuttle the fleet manager has never heard from is **not** in the registry, and a shuttle it has not heard from **is not free**: the tick does not give work to a shuttle it knows nothing about.

**What would change this:** if the shuttles ever stopped publishing their position (say, only on change, and the fleet manager missed one), the picture would go stale. The liveness alarm (`FleetServeLoop` + `ShuttleHealth`) already detects a shuttle that stopped talking; the recovery slice will use it to clear the picture.

---

## 6. Decision E: the tick — one timer, three steps

**Decision E.** The executor is one loop over a `PeriodicTimer`, in the house shape of `FleetServeLoop.RunAsync`. Every tick does the same three things:

1. **Finish.** For every shuttle the executor is working with: if the picture says the shuttle is **idle** and its position is the task's lane → `CompleteAsync`.
2. **Give.** For each free shuttle in the picture, in id order: if there is a queued task → `AssignNextAsync(shuttleId)` → build the move command for the task's lane → send it.
3. **Start or give back.** If the shuttle **acknowledged** → `StartAsync` (the task is now in progress). If the shuttle **rejected** the command, or never acknowledged it → `RequeueAsync`, so the task goes back in the queue and another shuttle can try.

Reasons:

1. **The tick is the decision, and the decision is small.** The whole job is "who is free, what is waiting, has anything finished". That is three questions with three answers.
2. **The house shape is already proven.** `FleetServeLoop.RunAsync` is a `PeriodicTimer` in a `while (await timer.WaitForNextTickAsync(token))` loop, disposed when the method ends, with the cancellation exception left to escape to `Program`. Copying it means the fleet manager has one loop shape, not two.
3. **The order of the steps matters.** Finishing first means a shuttle that just arrived is freed *in the same tick* it is noticed, instead of waiting a whole tick. Giving work second means a free shuttle gets work in the same tick it is seen.
4. **A rejected command requeues rather than fails.** The task is still good; the shuttle is not. `FleetTask.Requeue` is legal only from `Assigned`, so requeue is exactly the move that fits.

**The honest limit: the tick waits for the handshake.** The tick awaits the command's acknowledgement, so a shuttle that is slow to answer holds up the tick for the whole fleet. A real system gives each shuttle its own worker. That is the stretch goal of the lesson, not the base. The base is correct and explainable; the limit is written down so nobody is surprised.

**The tick never dies.** An unexpected exception inside a tick is logged and the loop continues with the next tick. One bad task must not stop the fleet.

### E1 — the seam: the tick depends on an interface, not on the bus

The tick sends one command and finds out whether the shuttle said yes. Its tests must not need a broker.

**Decision.** The tick depends on `ICommandSender`, a one-method interface in `src/FleetManager/ICommandSender.cs`:

```csharp
public interface ICommandSender
{
    Task<CommandSendResult> SendAsync(MoveCommandMessage command, CancellationToken cancellationToken);
}
```

`ReliableCommandSender` implements it. Measured from the committed source (`src/FleetManager/ReliableCommandSender.cs:55`): its `SendAsync` already has that exact signature, and it returns a `CommandSendResult` whose `Status` is `Acknowledged`, `Rejected` or `NoAcknowledgement`. So the change to that file is **one word**: `public sealed class ReliableCommandSender : ICommandSender`. Its body does not move.

Options:

- **A: a one-method interface the tick depends on.** (chosen)
- **B: depend on `IMessageBus` and build the retry and the acknowledgement wait inside the tick.**
- **C: depend on the concrete `ReliableCommandSender`.**

**Decision A.** Reasons:

1. **The tick's rules are the most valuable rules in the slice, and their tests must be fast and honest.** Finish, give, start-or-give-back: three rules that deserve three facts with no broker in the way. With the seam, a test writes a fake `ICommandSender` that answers `Acknowledged` and remembers the command it was handed. `AGENTS.md` asks for exactly this split: "decision logic is separated from input and output where practical, so they can be unit tested alone."
2. **Option B copies proven code.** The retry, the acknowledgement wait and the three outcomes are already written and already tested (`ReliableCommandSenderTests`). Moving them into the tick means two copies of one rule, and copies drift.
3. **Option C cannot be faked.** A test cannot hand the tick a real `ReliableCommandSender` without a bus behind it, so option C quietly ends up at option B.

**What would change this:** if the tick ever had to send a second kind of command (a recovery command, say), the interface would grow a second method, and the test doubles would grow with it.

**What would change this:** if the fleet grew to dozens of shuttles, one tick per fleet stops being enough; the tick would become a planner that hands work to per-shuttle workers.

---

## 7. Decision F: `serve` keeps the pair in memory — the SQL store waits for the persistence slice

**Decision F.** The `serve` path keeps the order and the task **in memory, together**. The `IFleetTaskRepository` seam is honoured: `serve` is given the in-memory implementation, which moves out of the test project into production (`src/FleetManager/InMemoryFleetTaskRepository.cs`, namespace `FleetManager` — the test project still finds it by the enclosing-namespace rule the review verified in lesson 23).

Options:

- **A: the in-memory repository in `serve`; SQL arrives with the persistence slice.** (chosen)
- **B: the PostgreSQL repository in `serve` now.**

**Decision A.** Reasons:

1. **The pair must move together.** Record 29 §5: the order and its task live in the serve process's memory and are lost together. Today the order board has **no** repository — only the task board has one. Option B would persist the task and not the order, and split a pair that is supposed to move as one.
2. **Option B creates a corruption path.** With a SQL task store and no order store, a restart restores the tasks but not the orders. The WMS then re-submits order `ORD-1`; `WmsOrderBoard.Submit` builds a new task with id `ORD-1`; `TaskBoard.Add` sees the restored task with that id and throws `TaskRejectedException("duplicate task id ORD-1")`. The REST door throws on a perfectly good order. That is exactly the "a bad order does not corrupt fleet state" failure M6 forbids — caused by our own half-persistence.
3. **The SQL store already exists and is already proven.** `PostgresFleetTaskRepository` is in production code and the restart-survival proof is in the integration suite (`RestartSurvivalTests`). The slice that wires SQL into `serve` is the **persistence slice**, and it must add the order's table at the same time.

**The named later slice — the persistence slice.** It owns: the order's table beside the task's table, the restore on start, re-marking the words the WMS is owed when a restored task is still owed one, the connection-string configuration key, and the restart-survival proof in `serve` itself (not only in the integration suite). Until that slice lands, the `serve` path forgets everything on restart — and it already did, because the order board was always in memory.

**What would change this:** if M6's cancel slice needs a cancelled order to survive a restart, the persistence slice moves earlier.

---

## 8. Decision G: the failure policy — three layers, and none of them lets a bad moment kill the fleet manager

**Decision G.** Review 23 left this open ("it belongs to the **professor** before the execution slice arms it"). This is that decision. Three layers, each one narrower than the last.

### G1 — the WMS URL must be an HTTP URL, checked before the bus connects

`Uri.TryCreate(value, UriKind.Absolute, out var uri)` accepts `ftp://`, `mailto:` and `file://` (measured in review 23, command 13). `HttpClient.PostAsync` then throws `NotSupportedException` — not an `HttpRequestException`, not an `OperationCanceledException` — so the pusher's catches miss it, the exception escapes `RunAsync`, `Program`'s `await pushTask` does not match it, and the whole process dies. The fix is at startup: after the absolute-URL check, also require `uri.Scheme` to be `Uri.UriSchemeHttp` or `Uri.UriSchemeHttps`, and throw `InvalidOperationException` **before** `await bus.ConnectAsync`.

### G2 — a push pass that throws puts the word back and keeps going

Inside `SendPendingAsync`, one catch-all around the per-order work: catch everything except a shutdown cancellation, `MarkPending` the order id again, and log one line. The truth is never given up on (record 31 §8), and one broken order does not stop the other orders in the same pass. The shutdown cancellation is **not** swallowed: it must escape so the loop stops cleanly.

### G3 — no background task can take the process down

In `Program`, every background task (`loopTask`, `pushTask`, `webTask`, and the new execution task) is awaited under two catches: the existing `catch (OperationCanceledException) when (stopping.IsCancellationRequested)` first, then a catch-all that **logs the failure loudly**. The process stays up, the door stays open, and the operator sees the error instead of a silent exit.

### G4 — the tick never dies

The executor's tick catches an unexpected exception, logs it, and continues with the next tick. A task that cannot move must not stop the fleet.

**Why a catch-all at all, when the three measured shapes are already caught?** Because the three shapes are the ones we *measured*. A fourth shape is a bug we have not met yet, and the difference between "one bug in one order" and "the fleet manager is gone at 03:00" is exactly this catch-all. It is logged loudly, so it is never silent.

**What would change this:** if the operator dashboard (M7) needs to show "the push loop is broken", the catch-all would raise an alarm instead of only logging. Not in this spec.

---

## 9. Decision H: the contract does not move

**Decision H.** `ContractVersion.Current` stays `4`. `src/Contracts/`, `docs/contract/v4.md` and `docs/contract/wms-rest.md` do not move.

Reasons:

1. **Everything the execution path needs is already on the wire.** The move command carries the shuttle id and the lane (`MoveCommandMessage`). The acknowledgement carries the correlation id (`ReliableCommandSender` matches on it). The status carries the state, the position and the timestamp.
2. **The task id never needs to be on the wire.** It is the order id (record 29 §3), and the fleet manager matches an acknowledgement to its task with its own bookkeeping — the command's message id is the correlation id.
3. **A version bump is a promise to every reader of the contract.** The spec says the contract is "the source of truth" and that changing it "needs a decision record and a version bump". This slice has nothing to change.

**Measured today:** `grep -q "Current = 4" src/Contracts/MessageTypes.cs` → `ContractVersion.Current = 4`.

---

## 10. Decision I: configuration

**Decision I.** One new key; the rest are reused.

| Key | Meaning | Default | Where it is validated |
|---|---|---|---|
| `FleetManager:TaskExecutionIntervalMs` | how often the tick wakes up | 1000 | `Program`, before the bus connects (`< 1` → throw) |

Reused, unchanged: `FleetManager:AckTimeoutMs`, `FleetManager:MaxAttempts`, `FleetManager:BackoffMs` (the `RetryPolicy` the command sender already builds), `FleetManager:LivenessTimeoutMs`, `FleetManager:AlarmEvaluationIntervalMs`, `FleetManager:LostCommunicationSeverity`, `FleetManager:BlockedPathSeverity`, `Messaging:*`, `Wms:BaseUrl`, `Wms:StatusTimeoutMs`, `Wms:StatusIntervalMs`.

**No literal in code:** no interval, no timeout, no topic root, no fleet manager id, no lane, no shuttle id. The tick reads its interval from config; the lane comes from the task; the shuttle id comes from the picture.

---

## 11. Decision J: what this slice does not do

| Not in this slice | Why | Who does it |
|---|---|---|
| SQL for the order board, the restore on start, re-marking owed words | the pair must move together (Decision F) | the **persistence slice** |
| The WMS cancelling an order | it needs a `Cancelled` task state and a row on the ladder | the **cancel slice** |
| The mock WMS (slow replies, no replies, bad input) | it is a test fixture, not fleet logic | the **mock-WMS slice** |
| The tick reserving lanes (traffic control) | `TrafficCore`'s occupancy map is not wired into the executor yet; with two shuttles headed for the same lane, this slice can still collide | the **traffic slice** |
| The operator dashboard | M7 | the **dashboard slice** |
| The fleet surviving a restart with work in flight | needs Decision F's persistence | the **persistence slice** |

**The traffic limit is the one to remember.** The tick gives work without asking the occupancy map. With one shuttle it cannot matter. With two shuttles and one lane, it can. It is named, not hidden.

---

## 12. Verification (measured 2026-10-22)

| # | Claim | How | Result |
|---|---|---|---|
| 1 | The committed tree builds clean | fresh copy of `src` (`git archive HEAD src`) in `/tmp/opencode/l24base`, `dotnet build` | **0 Warning(s), 0 Error(s)** |
| 2 | The pure suite is green before the slice | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **Failed: 0, Passed: 176, Skipped: 0, Total: 176** (FleetCore 49, FleetManager 59, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10) |
| 3 | No production caller of the sync exists yet | `grep -rn "SyncFromTask\|NoteSync" src --include=*.cs \| grep -v Tests` | 2 hits, both definitions |
| 4 | `FleetCore` is pure | `grep -rnE "HttpClient\|HttpRequest\|IMessageBus\|MqttMessaging" src/FleetCore --include=*.cs` | no matches (exit 1) |
| 5 | One status topic, two subscriptions, one delivery each | real broker (`docker compose -f docker/compose.yml up -d broker`), probe `/tmp/opencode/l24dup` | `first handler saw 1` / `second handler saw 1` |
| 6 | The bus fans a message out to every matching handler | read `src/MqttMessaging/MqttMessageBus.cs:84-92` | every matching handler is called |
| 7 | The dispatcher has 13 construction sites today | `grep -rn "new TaskDispatcher" src --include=*.cs` | 13 hits, all in the test project |
| 8 | The toy shows the newest truth wins | toy at `/tmp/opencode/l24toy`, `dotnet run` | one word owed; the word at send time is `done` |

The broker was stopped (`docker compose -f docker/compose.yml down`) and every scratch directory was deleted after the measurements.

---

## 13. What this record unblocks

- The lesson can teach three things — the transition line, the picture, the tick — and the owner can build all three in one sitting.
- Review 23's two deferred items are closed by a decision instead of by luck: **#5** (the failure policy) is Decision G, and **#6** ("newest truth wins" pinned by a fact) is the fact the transition line makes possible.
- The nullability trap that review 23 found in the teaching material is closed by [record 32](32-order-status-nullability-contract.md) before the owner writes a line of production code.
- The next slice has a named owner for the things this one left out.

---

## 14. The files this slice adds or changes

So the owner never has to guess what belongs to this slice.

**New production files:**

- `src/FleetCore/OrderSync.cs` — the pure transition line.
- `src/FleetManager/ShuttleRegistry.cs` — the picture.
- `src/FleetManager/ICommandSender.cs` — the seam (Decision E1).
- `src/FleetManager/TaskExecutionLoop.cs` — the tick.
- `src/FleetManager/WmsEndpoint.cs` — the WMS URL check (Decision G1).
- `src/FleetManager/InMemoryFleetTaskRepository.cs` — **moved** out of the test project; namespace `FleetManager`.

**Edited production files:**

- `src/FleetCore/WmsOrderBoard.cs` — one attribute (record 32). Nothing else.
- `src/FleetManager/TaskDispatcher.cs` — the line, five calls, one new required argument.
- `src/FleetManager/WmsStatusPusher.cs` — the catch-all (Decision G2).
- `src/FleetManager/ReliableCommandSender.cs` — one word: `: ICommandSender` (Decision E1).
- `src/FleetManager/Program.cs` — the wiring, the new key, the catch-alls (Decision G3).
- `src/FleetManager/appsettings.json` — one key (Decision I).

**New test files:** `src/FleetCore.Tests/OrderSyncTests.cs`, `src/FleetManager.Tests/ShuttleRegistryTests.cs`, `src/FleetManager.Tests/TaskExecutionLoopTests.cs`, `src/FleetManager.Tests/ExecutionSliceTests.cs`, `src/FleetManager.Tests/WmsEndpointTests.cs`.

**Edited test files:** `src/FleetManager.Tests/TaskDispatcherTests.cs`, `EndToEndTests.cs`, `RunLoopStressTests.cs`, `RunLoopReassignmentTests.cs` (the new constructor argument), and `src/FleetManager.Tests/WmsStatusPusherTests.cs` (one new fact).

**Deleted:** the test project's copy of `InMemoryFleetTaskRepository.cs`.

**Not touched:** `src/Contracts/`, `docs/contract/`, `src/FleetCore/FleetTask.cs`, `TaskBoard.cs`, `WmsOrder.cs`, `WmsOrderStates.cs`, `WmsOrderLadder.cs`, `WmsStatusOutbox.cs`, `src/FleetManager/RunLoop.cs`, `src/FleetManager/FleetServeLoop.cs`.
