# Decision 11: The task model, its lifecycle, and the pure task board

**Date:** 2026-10-10
**Milestone:** M3 (Fleet manager core) — what a *task* is, the five states in its lifecycle, the guarded transitions between them, and where the task queue lives.
**Status:** Accepted. Written for lesson 07, the first M3 lesson, which assigns the pure task model and its in-memory board.
**Supersedes:** none. It builds on [record 03](03-database.md) (PostgreSQL is the server M3 will persist to) and [record 06](06-shuttle-state-machine-and-timing.md) (the shuttle state machine and "time is an input"). It hands the SQL layer to the next M3 record, which links back here.
**Replaced by:** any later record that changes the task states, the transition rules, the task record's shape, or where the queue lives. The persistence record will link here; a change to the five states named in the spec is a milestone-level event.
**Wire contract:** unchanged. A task does not travel on the wire at M3. The only executable command the v2 contract carries is `moveCommand { ShuttleId, ToLane }`, and M3 maps a task to that command in a later lesson. There is no new message kind and no version bump. `docs/contract/v2.md` stays `Version: 2`.

---

## 1. What we are deciding

The spec's M3 says:

> *"A task queue with a clear lifecycle (queued, assigned, in progress, done, failed). Assign tasks to one shuttle, then several. Save state in SQL. Done when: a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart."*

This record decides the **pure half** of that, so no part of the task model is left to guesswork:

1. **what a task is at M3** — the unit of work the fleet manager hands out;
2. **the task record's shape** — the fields a task carries;
3. **the five states** and their exact wire names (the spec names them in prose; the code needs one spelling);
4. **the transition rules and guards** — which event is legal from which state, and how a wrong call is refused;
5. **where the task queue lives** — a pure, in-memory project, or mixed with the database and the broker;
6. **what is deferred** — SQL persistence, assignment policy, reassignment, and the WMS order.

Not decided here: the SQL schema, the driver, and the restart behaviour (the next M3 record); which shuttle gets which task, and load balancing (the assignment lesson); reassigning a failed shuttle's work (M5); traffic reservations (M4); WMS orders (M6).

---

## 2. Why this is the first M3 piece

M0-M2 built a working seam: a fleet manager and a shuttle, speaking a versioned contract over MQTT, with commands that are not silently lost. What is missing is **work**: the manager has nothing to hand out and nothing to track. M3 gives it a queue of tasks with a lifecycle.

The spec keeps decision logic separate from I/O: *"Decision logic is kept separate from input and output (pure functions for task assignment, traffic control, and state machines) so it can be unit tested without a broker or database."* So the task model and its lifecycle come **first**, as pure code, exactly the way the shuttle state machine (record 06) came before the shuttle spoke on the wire.

The order is deliberate:

| M3 piece | Lesson | Why here |
|---|---|---|
| The task model, lifecycle, and pure queue | **07 (this record)** | The shape every later piece stands on |
| Assignment to a shuttle, and the SQL save/load | 08 | Wraps the pure model with a policy and a store |
| Three shuttles finish a queue; state survives a restart | 09 | Proves the M3 done-when end to end |

---

## 3. Survey A: what a task is at M3

A task is the unit of work the manager schedules. Three candidates:

| | A: a **move task** — go to lane `A01-L02` (chosen) | B: a **pallet transfer** — load pallet `P` at slot `A`, unload at slot `B` | C: a **WMS order** — a store or retrieve request from the warehouse |
|---|---|---|---|
| Can today's v2 contract carry it? | **yes** — `moveCommand { ShuttleId, ToLane }` already exists | no — v2 has no load/unload command; it needs a contract change | no — there is no order message and no WMS until M6 |
| Executable end to end at M3? | yes, with the lesson-06 sender | not until the wire grows | not until M6 |
| Does it match what spec M3 asks? | yes — "a queue of tasks … finished by three shuttles" | richer than M3 asks, and it pre-builds M6 | that is M6's job |
| Throwaway risk | none — the fleet always needs moves | a model built before its command exists | pre-building a later milestone |

**Decision A: at M3, a task is a move task — a shuttle must go to a target lane.** It is the only thing the frozen v2 contract can ask a shuttle to do, so a task can actually finish. The task record is built so it can grow: the target is a field, and the state machine does not care what the work *is*, only where the task sits in its life. The eventual transfer task (M6 store/retrieve) adds fields and a mapping, not a new lifecycle.

Rejected for now: the pallet transfer. It is the *eventual* shape of real work, and this record says so plainly. But M1 already moves pallets inside the shuttle's local state machine, and there is no `load`/`unload` command on the wire. Assigning a transfer task at M3 would build a task the system cannot execute. It waits for the contract to grow.

Rejected: a WMS order. M6 owns the WMS. A task at M3 is internal work, not a customer order.

---

## 4. Survey B: where the task model lives

AGENTS.md: pure decision logic should be *"pure code with no broker, database, or network calls, so it can be unit tested alone."* Three placements:

| | A: a new pure project `FleetCore` (chosen) | B: inside `FleetManager` | C: in `Contracts` |
|---|---|---|---|
| References at all | none | `Messaging`, `MqttMessaging`, `Contracts` | none (it is the wire project) |
| Testable with no broker or database | yes, trivially | yes, but tests load the MQTT stack through the app | yes, but tests would mix wire types with domain types |
| Matches the `ShuttleCore` precedent | yes — the pure domain, parallel to the shuttle's pure domain | no | no |
| Where M4's traffic rules go later | the same project | mixed in with the app's I/O | wrong home |
| Cost | one class library and one test project | none | none |

**Decision B: the task model, its lifecycle, and the queue live in a new pure project, `FleetCore`.** It references nothing — no Contracts, no Messaging, no MQTTnet, no Npgsql — exactly as `ShuttleCore` references nothing. `FleetManager` (the app) will reference `FleetCore` and drive it, the way `ShuttleSim` references `ShuttleCore`. `FleetCore.Tests` drives the pure code with no broker and no database.

The name is forward-looking on purpose: M4's traffic rules are fleet-manager decision logic and belong in the same pure home. `FleetCore` is not "TaskCore" because tasks are not the only pure thing the fleet manager will own.

Rejected: putting the model in `FleetManager`. It would make the pure decision logic load `MQTTnet` through the app's references, and it blurs the line the spec draws between decisions and I/O.

Rejected: putting it in `Contracts`. `Contracts` is the versioned wire vocabulary. A task is an internal domain object, not a message. Mixing them would tempt a wire change that this record does not make.

---

## 5. Decision: the task record's shape

`FleetTask` is a **guarded, mutable object**, not an immutable record with a reducer. This mirrors `Shuttle` (record 06): the state changes through named event methods that take `DateTime now`, and a wrong call is refused. Record 06 already rejected the reducer shape as *"a second concept (functional cores) not yet taught."* This record keeps the same shape so the owner learns one pattern, not two.

```csharp
// src/FleetCore/FleetTask.cs — pure; no I/O, no clock, no ids generated here
public sealed class FleetTask
{
    public string TaskId { get; }              // the caller passes a fresh id
    public string ToLane { get; }              // the work: the lane to reach
    public DateTime CreatedAt { get; }         // the caller passes the time
    public TaskState State { get; private set; } = TaskState.Queued;
    public string? AssignedShuttleId { get; private set; }
    public string? FailureReason { get; private set; }

    // Assign / Start / Complete / Fail — each guarded, each takes DateTime now
}
```

Field-by-field reasons:

| Field | Why it exists | Where it is used later |
|---|---|---|
| `TaskId` | One task, one identity. The board rejects a duplicate id, and support can name the task. | SQL primary key (lesson 08) |
| `ToLane` | The work content at M3: the lane the shuttle must reach. | Becomes the `moveCommand.ToLane` (lesson 08) |
| `CreatedAt` | The queue is first-in-first-out; the oldest queued task goes first. | The SQL ordering column (lesson 08) |
| `State` | The lifecycle position. Private set — only the event methods change it. | The dashboard's task column (M7) |
| `AssignedShuttleId` | Which shuttle owns the task, once assigned. Null before assignment. | Reassignment picks a new value (M5) |
| `FailureReason` | Why a task failed, in words an operator can read. Null unless failed. | Alarm/diagnostics text (M5, M7) |

**Time and identity are inputs, not calls.** `FleetTask` never calls `DateTime.UtcNow` and never calls `Guid.NewGuid`. The caller passes both, exactly as record 06's rule (*"the world clock is passed in, never slept on"*) requires. This is what makes the whole model testable in microseconds and keeps `FleetCore` pure. `appsettings`-free for the same reason: there is nothing to configure in pure logic yet.

Rejected: an immutable `record` plus a `Transition(task, event) -> task` reducer. It is a fine shape for a large machine, but it needs the rack and every field threaded through each call, and it would teach a second concept in the first M3 lesson. The guarded object is the smallest thing that teaches the same lesson.

---

## 6. Decision: the five states and their wire names

The spec names exactly five states: **queued, assigned, in progress, done, failed**. The code uses a C# `enum TaskState` with those five, and one tested function maps each to its wire name, exactly as `ShuttleStateWire.ToWireName` did for the shuttle (record 06).

| Enum | Wire name | Plain meaning |
|---|---|---|
| `Queued` | `queued` | Waiting for a shuttle. |
| `Assigned` | `assigned` | A shuttle is chosen; the work has not started. |
| `InProgress` | `in_progress` | The shuttle has begun. |
| `Done` | `done` | Finished. Terminal. |
| `Failed` | `failed` | Could not finish. Terminal. |

The mapping lives in `TaskStateWire.ToWireName()` in `FleetCore`, and a unit test asserts all five names. **Why `in_progress` and not `in progress` or `inProgress`:** the state vocabulary is all-lowercase (matching the shuttle's `idle`, `moving`), a wire token cannot contain a space, and the two-word state needs one join — underscore is the common wire spelling for a state value and keeps every letter lowercase. `inProgress` would mix camelCase into an all-lowercase vocabulary. This is decided here so the dashboard and the WMS do not invent their own spelling later.

**Why the enum, not loose strings:** a typo in a state name becomes a compile error, not a silent `"don"` on the wire. This is record 06's Decision 1 restated for tasks.

---

## 7. Decision: the transitions and their guards

Each event is one method that takes `DateTime now`. A wrong call throws `TaskRejectedException` — a named type, so the tests can assert the exact failure, exactly as `ShuttleRejectedException` does for the shuttle.

| Event | Allowed from | Guards besides state | Effect on success |
|---|---|---|---|
| `Assign(shuttleId, now)` | `Queued` | `shuttleId` is not blank | `Assigned`; remembers the shuttle |
| `Start(now)` | `Assigned` | — | `InProgress` |
| `Complete(now)` | `InProgress` | — | `Done` |
| `Fail(reason, now)` | `Queued`, `Assigned`, or `InProgress` | `reason` is not blank | `Failed`; remembers the reason |

The rules, said plainly:

- **One step forward at a time.** `Queued → Assigned → InProgress → Done` is a line. You cannot skip a step, and you cannot go backwards.
- **`Fail` can happen from any live state.** A task can fail before it is assigned (the manager gives up), while assigned (the shuttle never starts), or while in progress (the move is rejected or the shuttle goes silent). It cannot fail once it is `Done` or already `Failed`.
- **A terminal state is a sink at M3.** `Done` and `Failed` accept nothing. Re-opening a failed task — reassigning a faulted shuttle's work — is M5's job and will need its own transition, decided then.
- **Time must not go backwards.** Every event rejects a `now` earlier than the previous event's, exactly as the shuttle does (record 06 §3). Equal timestamps are allowed.

**Mapping the endings to M2's sender.** `Complete` follows a successful `moveCommand` round trip (`Acknowledged`, status reached). `Fail` follows a rejected ack (the reason becomes `FailureReason`), a `NoAcknowledgement` give-up, or a shuttle fault. That mapping is assignment policy and belongs to lesson 08; this record fixes only what the task itself allows.

Rejected: a `bool` return from each event. A forgotten check becomes a silent no-op, and the wrong-call tests lose their teeth. Rejected: one exception type per reason — the guards, not the types, are what the tests assert (record 06 made the same call).

---

## 8. Decision: the queue is pure and in-memory; SQL is a later layer

The task queue is a `TaskBoard` in `FleetCore`: a list of `FleetTask` objects, an id-uniqueness check, a lookup by id, and `NextQueued()` — the oldest task still in `Queued`. It is the "task queue" the spec names.

```csharp
// src/FleetCore/TaskBoard.cs — pure; no database, no broker, no clock
public sealed class TaskBoard
{
    public FleetTask Add(FleetTask task);       // rejects a duplicate TaskId
    public FleetTask Get(string taskId);        // throws if missing
    public bool TryGet(string taskId, out FleetTask? task);
    public FleetTask? NextQueued();             // first-in-first-out; skips non-queued
    public int Count { get; }
}
```

**Why the board is pure and separate from SQL:** the spec says decisions must be testable without a database, and M3's hard part — "state survives a restart" — is a persistence lesson on its own. If the board called Npgsql directly, the lifecycle tests would need a running PostgreSQL container for no reason. So the board holds tasks in memory, and a later lesson adds a store that saves and loads the board.

**Why `NextQueued()` skips non-queued tasks:** once the head task is assigned, it still lives in the board so its progress can be tracked, but it is no longer waiting for a shuttle. `NextQueued()` returns the oldest task that is still `Queued`, so the queue advances without re-handing out work. This is the seed of lesson 08's assignment loop.

**Why `Add` takes a `FleetTask`, not a lane and a time:** the caller owns id generation and the clock (section 5), so tests add tasks with known ids and times and the board stays deterministic. The board owns order (insertion = first-in-first-out) and uniqueness.

**Where the queue is *not*:** not in the database (next lesson), not in the message contract (a task is not a wire message), and not inside `ShuttleCore` (tasks are the fleet manager's side, shuttles are the vehicle's side).

---

## 9. Rejected options, and why

- **A pallet-transfer task now.** Needs a `load`/`unload` command that v2 does not have. Building the model before its wire is throwaway. Deferred to the contract growth that M6 needs.
- **The task model inside `FleetManager` or `Contracts`.** Mixes pure decisions with I/O, or mixes the internal domain with the wire vocabulary. Rejected (Survey B).
- **An immutable reducer.** Correct for a big machine, but a second concept in the first M3 lesson. Rejected (section 5).
- **Loose `string` states.** A typo reaches the wire instead of the compiler. Rejected (section 6).
- **A `bool`-returning or no-guard state machine.** A forgotten check becomes a silent no-op. Rejected (section 7).
- **The board talking to PostgreSQL directly.** Forces every lifecycle test to run a container and skips the isolation the spec asks for. Rejected (section 8).
- **Re-opening a `Failed` task at M3.** That is reassignment, and it is M5's *"reassign a failed shuttle's work."* A terminal-state sink now is honest; the transition arrives with its owner.

---

## 10. Verification table

Checked this session (2026-10-10). The lifecycle-and-queue mechanism ran as a throwaway console toy in `/tmp/opencode/tasktoy`; the mutation ran in `/tmp/opencode/tasktoy-mutation`; both are deleted afterwards. Read-only commands and file reads ran against this repo. No experiment touched `src/`.

| Claim | How checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| The `classlib` and `xunit` templates exist | `dotnet new list classlib`; `dotnet new list xunit` | both listed |
| The five-state lifecycle works on a toy | `/tmp/opencode/tasktoy` `dotnet run` | see the exact output below; exit 0 |
| Removing a state guard changes behaviour (the guard is load-bearing) | toy mutation: `Complete` with no state check | `refused complete` line disappears and `drill-3` reads `Done`; see below |
| A pure FIFO board hands out the oldest queued task | toy: `NextQueued()` after two queued tasks | `queue head: bike-1`; after progress, `queue head now: drill-3` |
| The solution has 12 projects (this lesson adds 2) | `grep -c '<Project Path' src/ShuttleFleet.slnx` | prints `12` |
| The contract is untouched; no task message exists | `grep -nE '^Version:' docs/contract/v1.md docs/contract/v2.md`; `test -f docs/contract/v3.md` | `Version: 1`, `Version: 2`; no `v3.md`; no task type in `src/Contracts` |
| The name `FleetCore` is unused, so the project is new | `ls src/FleetCore`; `grep -rn 'TaskState\|FleetTask\|TaskBoard' src --include='*.cs'` | no such directory; no matches |
| `ShuttleCore` is the pure-project precedent (references nothing) | read `src/ShuttleCore/ShuttleCore.csproj` | no `ProjectReference`, no `PackageReference` |
| The guarded-object pattern and `now`-as-input exist | read `src/ShuttleCore/Shuttle.cs`, `ShuttleState.cs`, `ShuttleRejectedException.cs` | private-set state, guarded methods, `DateTime now`, one named exception, `ToWireName()` mapping |

Exact toy run (`/tmp/opencode/tasktoy`, `dotnet run`, exit 0):

```
queue head: bike-1
bike-1 end state: Done
refused start: cannot start while Queued
refused complete: cannot complete while Queued
mower-2 end state: Failed (part missing)
queue head now: drill-3
drill-3 was never touched: Queued
```

Exact mutation run (`/tmp/opencode/tasktoy-mutation`, the `Complete` guard removed, `dotnet run`, exit 0):

```
queue head: bike-1
bike-1 end state: Done
refused start: cannot start while Queued
mower-2 end state: Failed (part missing)
queue head now: (none)
drill-3 was never touched: Done
```

The mutation proves the state guard is what refuses the wrong call: without it, `drill-3` completes from `Queued` and its state becomes `Done`.

Not yet verified:

- The owner's `FleetCore`, `TaskBoard`, and their tests — this is the assignment, not a report on an implementation.
- The SQL schema, the driver, and the restart survival — the next M3 record and lesson.
- The assignment policy (which shuttle, load balancing) and the mapping of a task to a `moveCommand` — lesson 08.
- Reassignment of a failed task — M5. Traffic — M4. WMS orders — M6.

---

## 11. What would change this choice

- **The wire grows a load/unload command (M6, or earlier if the transfer task is pulled forward)** → the task record gains source/destination slots and a kind, and a new record links here. The five states and the transition rules stay.
- **The dashboard or WMS needs a different state spelling** → `ToWireName()` changes in one place; a wire contract page records it. The enum does not change.
- **M5 adds reassignment** → `Failed` stops being a sink; a new transition (for example `Requeue`) and a record linking here. The spec's five states are unchanged; only the edges grow.
- **M4's traffic control needs to reserve a task's route** → `FleetCore` gains a traffic module beside the task module, still pure; no move to another project.
- **The queue must be ordered by more than arrival (priority)** → `CreatedAt` gains a priority field and `NextQueued()` sorts by it; a new record links here.
- **The task model outgrows the guarded object** → a reducer core with a thin guarded shell; a new record links here.

---

## 12. What this record unblocks

- Lesson 07 can fix the exact names its acceptance greps for: `FleetCore`, `FleetTask`, `TaskState`, `TaskRejectedException`, `TaskBoard`, and `TaskStateWire.ToWireName`.
- M3's "task queue with a clear lifecycle" becomes real and unit-tested with no broker and no database, because the model is pure (section 4) and time and ids are inputs (section 5).
- Lesson 08 starts from a decided task shape and a decided queue: it adds assignment (which shuttle) and a persistence layer, instead of re-deciding the model.
- M4's traffic rules and M5's reassignment have a pure home (`FleetCore`) and a terminal-state edge to change, both named in section 11.
