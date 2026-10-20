# Decision Record 29: From an accepted WMS order to a fleet task — mapping, atomic intake, and where the pair lives

**Date:** 2026-10-19
**Milestone:** M6 (WMS integration) — lesson 21: order → `FleetTask` mapping.
**Status:** Accepted. Append-only. Builds on [record 28](28-wms-rest-front-door.md) (the WMS REST front door, `WmsOrder`, `WmsOrderBoard`, the order state ladder), [record 11](11-task-lifecycle-and-queue.md) (`FleetTask`, `TaskBoard`, the five task states, "time and identity are inputs"), [record 13](13-fleet-state-persistence.md) (`IFleetTaskRepository`, the SQL persistence pattern), [record 22](22-serve-loop-and-alarm-publish.md) (the `FleetServeLoop` that `serve` runs), and [record 02](02-message-transport.md) (REST at the business edge, MQTT to the shuttles).
**Wire contract:** **no change.** The MQTT contract stays `v4`; `ContractVersion.Current` stays `4`. The WMS REST page `docs/contract/wms-rest.md` also does not change: no field, route, or status code moves. This slice is internal fleet-manager composition.
**Supersedes:** [record 28 §6 (Decision E)](28-wms-rest-front-door.md)'s *timing* for moving orders to SQL — specifically the sentence "when the order-to-task mapping lands (the next slice), the order row is persisted beside the task row, in the same transaction." Record 29 keeps that destination but moves the *timing* to the execution slice, for the reasons in §7. Record 28 itself is untouched; it stays append-only.
**Replaced by:** later records that wire the runtime dispatcher into `serve`, that persist the order/task pair, that push order status to the WMS, or that add non-WMS task sources. Append-only.

---

## 1. What we are deciding

M6's done-when is *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."* Lesson 20 built the front door: a well-formed order is stored in `WmsOrderBoard` with state `received`. Nothing turns that order into work. The fleet has an accepted order and an empty to-do list.

This record decides the next slice — the **order-to-task mapping** — and every question it raises, so the owner never invents a product choice:

- **A.** Where the mapping code lives, and what triggers it.
- **B.** The task the order becomes: its id, its lane, its time, and how the two WMS kinds (`store`, `retrieve`) are handled.
- **C.** The rule that one accepted order creates exactly one task, and every other outcome creates none.
- **D.** Where the task lands and what a restart does (and therefore what persistence is deferred).
- **E.** How the WMS order's state ladder lines up with the task state ladder, fixed now so later slices do not guess.
- **F.** What this slice must not do to fleet state, tied to the M6 done-when.

Everything here follows the spec's split: **REST between the WMS and the fleet manager, MQTT between the fleet manager and the shuttles.** A task still does not travel on the MQTT wire in this slice. `moveCommand { ShuttleId, ToLane }` remains the only move kind. There is no new message and no version bump.

---

## 2. Decision A: the mapping lives in `FleetCore`, inside `WmsOrderBoard.Submit`, and runs only on `Created`

Options:

- **A: extend `WmsOrderBoard` — the class created in lesson 20 — to own a `TaskBoard` and add a task on `Created`, under its existing lock.** (chosen)
- **B: a new pure `WmsOrderIntake` class in `FleetCore` that composes `WmsOrderBoard` and `TaskBoard`.**
- **C: map in the `Program.cs` endpoint, next to the HTTP mapping.**

**Decision A.** `WmsOrderBoard` gains a `TaskBoard` in its constructor. `Submit(request, now)` keeps its exact duplicate semantics, and on a `Created` outcome — inside the same `lock (_gate)` that guards the order dictionary — it creates one `FleetTask` and adds it to the task board. The HTTP endpoint does not change at all: it still calls `board.Submit(request, DateTime.UtcNow)` and maps the outcome to a status code.

Three reasons:

1. **One lock guards the whole fact.** "The order was accepted" and "a task exists" become one in-memory fact. A `TaskBoard` is a `List<T>` and `List<T>.Add` is not thread-safe, so a second HTTP request for a different order must not mutate it at the same time. Putting the add inside the board's existing `_gate` gives one critical section for the composite operation. A separate coordinator (option B) would either need its own second lock or would call `TaskBoard.Add` outside any lock — the exact race the reference says the endpoint must never own.
2. **One public entry cannot make a half fact.** After this change, the only production caller is the endpoint, and it calls `Submit`. A direct `Submit` on a fresh `WmsOrderBoard` still creates both the order and its task. Rejected option B keeps `WmsOrderBoard.Submit` public and callable on its own, so a caller could store an order with no task — the half-state this slice exists to prevent.
3. **The reviewed tests survive.** The four lesson-20 `WmsOrderBoardTests` only change their construction to `new WmsOrderBoard(new TaskBoard())`; every assertion about `Created`/`AlreadyKnown`/`Conflict`/`ReceivedAt` stays green. No class is renamed and no public behaviour is removed.

Rejected C: the endpoint is the I/O shell. `FleetCore` may not reference a web type (record 28 §4), and the endpoint has no lock, so two concurrent `POST`s for different orders could corrupt the task list. Decision logic stays out of the endpoint.

**The name stays `WmsOrderBoard`.** It is the board where WMS orders are received; making the queued task is part of receiving. A rename to `WmsOrderIntake` touches lesson 20's tests and the reviewer's file references and buys only a nicer noun.

**What would change this:** if a second, non-WMS source of tasks appears in `serve`, or if the composite operation grows beyond "one order, one task" (for example, a task per leg), split the intake into its own class then, with a record.

---

## 3. Decision B: the task's id, lane, and time

The task the order becomes:

```csharp
// shape, inside WmsOrderBoard.Submit on a Created outcome
var task = new FleetTask(order.OrderId, order.LaneId, order.ReceivedAt);
_tasks.Add(task);
```

### Task id = order id

Options:

- **A: `TaskId = OrderId`.** (chosen)
- **B: a derived id, e.g. `"wms-" + OrderId`.**
- **C: a fleet-generated id (GUID or sequence) plus a stored link from order to task.**

**Decision B (A).** One string names the whole saga from the WMS's first `POST` to the final `done`. Reasons:

1. **Traceability is free.** The status-push slice (next) keys its status update on the WMS's `orderId`. If the task's id *is* the order id, there is no second index and no join.
2. **No collision is possible in `serve`.** The order board refuses a second order with the same id (`Conflict`), and the only task source in `serve` is WMS orders. `TaskBoard.Add` additionally rejects a duplicate `TaskId` with `TaskRejectedException` — a second line of defense.
3. **`FleetTask` already accepts a caller-supplied id; no id generation belongs in pure code** (record 11 §5: time and identity are inputs, never calls).

Rejected B: a synthetic prefix is not the WMS's number, so support has to strip it to answer "what happened to `ORD-1001`?" — a transform at every lookup for no gain.
Rejected C: a generated id forces a second store (order → task) and an id source. Identity is free here; do not buy machinery.

**What would change this:** a second task source inside `serve` (for example, a local `move` that creates a task), or two WMS tenants whose order ids can collide. Either makes a namespaced or generated id correct; revisit then.

### Task lane = the order's lane, for both kinds

`ToLane = order.LaneId`. At this slice a task is still record 11's shape: *a shuttle must reach a lane*. A `store` order means the lane the pallet goes to; a `retrieve` order means the lane the pallet sits in. Both are a move to that lane today.

The physical difference — deposit versus pick up — is a load/unload command on the wire, which does **not exist**: the v4 commands are `moveCommand` and `recoveryCommand`. The order keeps its `kind`; the task does not carry it yet. When it arrives (a later slice, almost certainly with a `v5` bump), `FleetTask` gains a field, not a new lifecycle (record 11 §11).

### Task time = the order's `ReceivedAt`

`CreatedAt = order.ReceivedAt`, the instant the fleet accepted the order. The task's FIFO position then matches acceptance order. The caller's `now` and the stored `ReceivedAt` are the same value on a fresh submit, so this is not a second clock.

**A real detail to own:** `FleetTask` truncates `CreatedAt` to microsecond precision (`ToMicrosecondPrecision`), while `WmsOrder.ReceivedAt` is stored raw. The two values are the same instant to the microsecond. A test that compares them exactly should compare `ToMicrosecondPrecision`; do not "fix" the truncation — it matches PostgreSQL's microsecond precision (record 13 §5, as lesson 09 explains).

The new task starts `Queued` — `FleetTask`'s constructor sets it, and nothing in this slice moves it.

---

## 4. Decision C: one accepted order creates exactly one task; every other outcome creates none

This is the intake table the owner implements and the reviewer checks:

| `Submit` result | Task added? |
|---|---|
| `Created` (first time) | **exactly one**, `Queued`, id = order id, lane = order lane |
| `AlreadyKnown` (identical retry) | **none** — the first submit already made it |
| `Conflict` (same id, changed values) | **none** |
| validation errors (the endpoint returns `400`) | **none** — `Submit` is never called |

Two protections make "exactly one" true, and it is worth knowing they are different:

- **The mapping runs only on `Created`.** This is the rule. A retry is not new work. A conflict is a bug upstream; the fleet refuses to guess.
- **`TaskBoard.Add` rejects a duplicate id.** This is the backstop. If someone later "fixes" the mapping to run on `AlreadyKnown`, it does not silently duplicate the move — it throws `TaskRejectedException`. Silently doing a move twice is exactly the pallet duplication M5's done-when forbids.

**Why this matters to M6 specifically.** M6's done-when says *"a bad order does not corrupt fleet state."* At the intake, that sentence is this table. A blank field is refused before the board; a retry adds no task; a conflict adds no task. There is no code path that creates a task without a stored, accepted order.

---

## 5. Decision D: where the pair lives, and what a restart does

`RunServeAsync` creates the task board once and hands it to the order board:

```csharp
// shape, in RunServeAsync (the owner writes it)
var taskBoard = new TaskBoard();
var board = new WmsOrderBoard(taskBoard);
```

- The task lands on the **same `TaskBoard` type the fleet already uses** — the board `TaskDispatcher` drains and record 13 persists. The execution slice wires the dispatcher to this instance; it does not invent a new home for tasks.
- Both boards live **in memory, in the `serve` process, and are created together.** If `serve` restarts, both are empty again.

**What a restart with an accepted order does:** the order and its task are both lost. That is consistent, not corrupt. There is never an order without a task or a task without an order, because they are only ever created inside one critical section. Dropping the pair is honest; dropping half of it would be the bug.

**Persistence is deferred to the execution slice.** See §7 for the supersede. In short: nothing in `serve` runs a task yet, so there is nothing to resume; and wiring SQL into `serve` (a connection string, schema creation, load-on-start, save-per-change, and Integration tests) is a concept of its own that would push this lesson past its cap. The persistence machinery already exists (`IFleetTaskRepository`, `PostgresFleetTaskRepository`, record 13); the execution slice only has to compose it.

**A thread-safety note, deliberately left open.** When the dispatcher reads `taskBoard.NextQueued()` from the serve-loop thread while HTTP writes from request threads, the board's thread-safety becomes a real question. This slice's writes are serialized by the board's lock; the read side is the execution slice's decision (funnel writes onto one thread, or make `TaskBoard` thread-safe). This record names the question so it is not discovered as a bug.

**What would change the restart choice:** the execution slice landing; M7 needing to show orders across a restart; or a reviewer requiring restart survival before execution.

---

## 6. Decision E: the order ladder and the task ladder, lined up now

Record 28 §6 fixed the order ladder: `received → in_progress → done | failed`, with `cancelled` from any live state. Record 11 §6–§7 fixed the task ladder: `queued → assigned → in_progress → done | failed`. For the status-push slice to send the right word, the two must line up. That alignment is decided here:

| `WmsOrder.State` | `FleetTask.State` | Plain meaning |
|---|---|---|
| `received` | `Queued` | The fleet has the work; no shuttle has started it. |
| `received` | `Assigned` | A shuttle is chosen; the move has not started. |
| `in_progress` | `InProgress` | A shuttle has begun the move. |
| `done` | `Done` | The work finished. |
| `failed` | `Failed` | The work could not finish. |
| `cancelled` | (no mapping yet) | The WMS asked to stop; the cancel slice owns this row. |

**Only the first row is reachable in this slice.** The order stays `received`; the task is `Queued`. Nothing reads a task's later state yet, because nothing executes a task in `serve`.

**Rejected: moving the order to `in_progress` at intake.** Then the WMS could not tell "accepted, waiting for a shuttle" from "a shuttle is working on it"; the two states would mean the same thing, and the status-push slice would have nothing to push at the transition.

---

## 7. Supersede: the timing of order/task persistence (record 28 §6)

Record 28 §6 (Decision E) wrote a trigger: *"when the order-to-task mapping lands (the next slice), the order row is persisted beside the task row, in the same transaction."* The mapping lands here (this record). The persistence does not. That timing was wrong for a reason worth stating, so this record supersedes the timing while keeping the destination.

**Why the timing moves.** (1) A lesson teaches at most three concepts. Mapping plus SQL composition in `serve` is more than three. (2) Nothing in `serve` executes a task, so a persisted row has no consumer: it would be storage for nothing, the same reasoning record 28 §6 itself used to defer persistence in the first place. (3) `serve` opens no database today; introducing one there is its own slice. (4) In-memory-together already satisfies the M6 no-corruption requirement (§8). The pair is never split.

**The destination stands.** When the execution slice makes a task resumable, the order row is persisted beside the task row in the same transaction, as record 28 §6 decided. The repository and the task table already exist (record 13); the execution slice composes them into `serve`.

**Why a supersede and not an erratum.** An erratum fixes a rendering defect in a table. This is a genuine change to *when* a written decision takes effect, so it needs its own record. Record 28 is append-only and untouched; a reader of its §6 is sent here.

---

## 8. What this slice must not do to fleet state (M6 done-when)

> *"a WMS outage or a bad order does not corrupt fleet state."*

- **Bad order.** `Validate` runs before `Submit` (record 28 §4). Blank or unknown fields return `400`, the board is never called, no task is made. Verified by the existing validator and endpoint tests plus this slice's "bad order adds no task" test.
- **WMS outage.** This slice never calls the WMS. Intake is an in-memory synchronous step; a slow or dead WMS client is waiting on its own HTTP call and cannot stall fleet state. Status push (the slice after this) is where a WMS timeout gets its own record and timeout key.
- **Retry.** An identical re-send adds no task (§4). A conflict adds no task.
- **Restart.** The pair drops together; it is never half-present (§5).

---

## 9. Scope and limits

- **No task execution in `serve`.** No dispatcher, no MQTT send, no status-driven task transitions. The task sits `Queued` on the board the dispatcher will drain.
- **No order persistence and no task persistence in `serve`.** Deferred per §7.
- **No status push to the WMS.** The next slice, with its own record and its own timeout key.
- **No cancel route.** `cancelled` remains only a constant; the cancel slice owns the route and the task mapping for it.
- **No load/unload MQTT commands and no retrieve destination.** Those are wire changes, almost certainly a `v5`.
- **No change to `src/Contracts`, `docs/contract/v1.md`…`v4.md`, `docs/contract/wms-rest.md`, or `ContractVersion.Current`.** It stays `4`.
- **No change to `FleetTask`, `TaskBoard`, `TaskDispatcher`, `RunLoop`, or the `FleetServeLoop`.** This slice composes existing pure types; it does not edit them.

---

## 10. Verification (measured)

Checked today (2026-10-19) in the repo and in scratch copies under `/tmp/opencode/l21base` and `/tmp/opencode/l21toy`; the scratch copies are deleted afterwards. Nothing was left running.

| Claim | How | Result |
|---|---|---|
| SDK | `dotnet --version` | `10.0.112` |
| Baseline builds clean | clean copy of `src` in `/tmp/opencode/l21base`; `dotnet build` | **0 Warning(s), 0 Error(s)** |
| Baseline pure suite is green | same copy; `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0; **159 passed / 0 failed / 159 total** (FleetCore 35, Contracts 8, TrafficCore 6, ShuttleCore 31, Messaging 10, MqttMessaging 3, ShuttleSim 10, FleetManager 56) |
| No order→task mapping exists anywhere | `grep -rniE "MapToTask|CreateTask|order.*FleetTask|FleetTask.*order" src --include=*.cs` | no matches |
| `serve` has no task board | `grep -n "FleetTask\|TaskBoard" src/FleetManager/Program.cs` | no matches; the only board is `var board = new WmsOrderBoard();` (line 164) |
| `WmsOrderBoard` knows no task type | `grep -n "FleetTask\|TaskBoard" src/FleetCore/WmsOrderBoard.cs` | no matches |
| `TaskBoard.Add` rejects a duplicate id | read `src/FleetCore/TaskBoard.cs:6-12` | `if (_tasks.Any(existing => existing.TaskId == task.TaskId)) throw new TaskRejectedException(...)` |
| `FleetTask` takes id, lane, and time as inputs and starts `Queued` | read `src/FleetCore/FleetTask.cs:10-24` | constructor validates blank id/lane; `State = TaskState.Queued` |
| `FleetTask` truncates `CreatedAt` to microseconds | read `src/FleetCore/FleetTask.cs:87-88` | `new(value.Ticks - value.Ticks % 10, value.Kind)` |
| The contract is untouched | `grep -n "Current = " src/Contracts/MessageTypes.cs` | `public const int Current = 4;` |
| The mapping toy behaves as specified | `/tmp/opencode/l21toy`; `dotnet run` | `Created`→tasks=1, retry `AlreadyKnown`→tasks=1, changed `Conflict`→tasks=1, second order→tasks=2, bad order→tasks stays 2; see the lesson for the exact output |
| A mapping that runs on a retry would duplicate work | `/tmp/opencode/l21toy-mut` (map on `AlreadyKnown`), `dotnet run` | the retry reports `AlreadyKnown` but tasks=2, and the board shows `ORD-1` twice |

Not yet verified: the owner's implementation of §2–§6 — build, tests, greps, and mutations — is the assignment; this record states the design.

---

## 11. Door hardening deferred earlier, and where it belongs

Review 20 deferred four HTTP-host probes to a later "door hardening" slice. They are recorded here so they are not lost. All four live in `src/FleetManager/Program.cs` and are invisible to the pure suite because the HTTP host has no automated test today (review 20 issue 3).

| # | Deferred item | Why it survives today | Where it belongs |
|---|---|---|---|
| d | duplicate-submit lock race in `WmsOrderBoard.Submit` | no committed test pins the lock | a door-hardening slice; this slice's lock now also guards `TaskBoard.Add`, so a pure concurrency probe has more to prove |
| e | `AlreadyKnown` mapped to `201` instead of `200` | no HTTP test pins the outcome mapping | door-hardening slice (`Microsoft.AspNetCore.Mvc.Testing`) |
| f | blank `Kestrel:Endpoints:Http:Url` check removed | no test pins startup validation | door-hardening slice |
| g | `MapGet` registered on the wrong route | no test pins route registration | door-hardening slice |

Also open from review 20 and owned by that slice: the per-order application log (issue 5) and the `Submit`-without-`Validate` guard note (issue 4). The package that closes the HTTP gaps exists for net10 — `Microsoft.AspNetCore.Mvc.Testing` `10.0.13` (measured in record 28 §9) — and record 28 deliberately did not add it.

**This slice adds a pure line of defence, not the HTTP test.** The new "one accepted order, one task" test is pure `FleetCore`; it kills mapping mutations. The HTTP-level pins (e, f, g) still belong to the door-hardening slice, which should land before M6 is called done.

---

## 12. What this record unblocks

- Lesson 21's assignment has no unguided product choices: where the mapping lives, the task id, the lane, the time, the intake table, the restart behaviour, and the ladder alignment are all fixed.
- The execution slice starts from a decided task shape and a decided home: it wires `TaskDispatcher` to the `TaskBoard` created here and sends `moveCommand`s; it does not re-decide the mapping.
- The status-push slice has the ladder table (§6) it needs to choose the word to send.
- M6's "bad order does not corrupt fleet state" has a tested mechanism at the intake (§4, §8).
