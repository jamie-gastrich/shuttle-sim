# Lesson 07: The task and its lifecycle (the first M3 piece)

**Milestone:** M3 (Fleet manager core) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 06: The acknowledgement timeout and the retry that closes M2](06-ack-timeout-and-retry.md) · **Decision:** [record 11](../decisions/11-task-lifecycle-and-queue.md)

**Concepts this assignment requires:**

- [ ] **The task record** — one unit of work with an id, a target, and a state. Taught in
      [The task record](#the-task-record). Fixed by
      [record 11 §5](../decisions/11-task-lifecycle-and-queue.md).
- [ ] **The task lifecycle** — the five states the spec names, and the guarded moves between them.
      Taught in [The task lifecycle](#the-task-lifecycle). Fixed by
      [record 11 §6-§7](../decisions/11-task-lifecycle-and-queue.md); M5 adds reassignment.
- [ ] **The task board** — a pure, first-in-first-out queue that holds tasks and hands out the next
      queued one. Taught in [The task board](#the-task-board). Fixed by
      [record 11 §8](../decisions/11-task-lifecycle-and-queue.md); lesson 08 adds SQL and assignment.

Three new concepts. That is the cap.

Three words for later, not today: **assignment policy** (which shuttle gets which task — lesson 08),
**persistence** (saving the queue so it survives a restart — lesson 08), and **reassignment** (giving a
failed task to another shuttle — M5).

---

## A short note about lesson 06

The review of lesson 06 filed two **lesson defects** (L1, L2), not owner misses. Both are fixed in
[lesson 06](06-ack-timeout-and-retry.md) and its hints:

- **L1:** Phase 5a/5b grepped the shuttle's log (`$S`/`$S2`) for status lines the shuttle never
  printed. Fixed by **assigning** the missing behaviour: requirement 8 now asks the shuttle's `serve`
  mode to log each message it handles and publishes, so the `$S` greps are about work that was asked
  for.
- **L2:** Phase 5b's prose promised an attempt-log grep the block did not have. Fixed by adding the
  grep (`grep -qE 'attempt [0-9]+ of [0-9]+' "$M2F"`) and an attempt-logging bullet to requirement 2.

Both are small follow-ups on the already-closed M2 slice. They are **not** part of this lesson's
assignment, but the reviewer can check them when the next review runs. The paper trail is here, in
[lesson 06](06-ack-timeout-and-retry.md)'s requirement 2, requirement 8, and Phase 5b block, and in
its hints.

---

## Where this fits

M0-M2 built the road. There is a fleet manager and a shuttle, they speak a versioned contract over
MQTT, and a command is not silently lost. What is missing is **work**. The manager has nothing to hand
out and nothing to track.

The spec's M3 says:

> *"A task queue with a clear lifecycle (queued, assigned, in progress, done, failed). Assign tasks to
> one shuttle, then several. Save state in SQL. Done when: a queue of tasks is finished by three
> shuttles, and fleet state survives a fleet manager restart."*

That is a lot for one lesson. So M3 comes in small pieces, in the same order M1 did:

| M3 piece | Lesson | What it adds |
|---|---|---|
| The task, its lifecycle, and the pure queue | **07 (this one)** | The shape everything else stands on |
| Assignment and the SQL save/load | 08 | Which shuttle, and living in PostgreSQL |
| Three shuttles finish a queue; restart survives | 09 | The M3 done-when, end to end |

This lesson builds only the first piece. It is **pure code**: no broker, no MQTT, no database, no
clock. That is on purpose. AGENTS.md says decision logic stays separate from input and output, so it
can be tested alone. The pure task machine is the same idea as the pure shuttle machine you built in
M1 (lesson 04, record 06). You already know this shape. Today you point it at a new domain.

---

## Before we start: check what you know

These are about state machines and queues in general. If any is not quick, reread lesson 04's machine
section before you start.

1. In `Shuttle`, the state is `private set` and changes only through `StartMove`, `CompleteMove`, and
   the other event methods. Why not just make `State` a public property anyone can set?
2. `Shuttle` takes `DateTime now` on every event and never calls `DateTime.UtcNow`. What does that buy
   the tests? (Say it in one sentence.)
3. A queue is a line. If you want a **first-in-first-out** queue, which task should leave the line
   first: the one added first, or the one added last? Why?

If "first-in-first-out" felt shaky, that is fine. Today makes it real.

---

## Verification table

Everything below was checked by running a command or reading an installed file on **2026-10-10**. The
lifecycle-and-queue mechanism ran as a throwaway console toy at `/tmp/opencode/tasktoy`, and a mutated
copy at `/tmp/opencode/tasktoy-mutation`. Both are deleted afterwards. File reads were run against
this repo; no experiment touched `src/`.

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| The class-library and xUnit templates exist | `dotnet new list classlib`; `dotnet new list xunit` | both listed |
| A five-state task machine runs on a toy | toy: `dotnet run` | `queue head: bike-1`; `bike-1 end state: Done`; `refused start: cannot start while Queued`; `mower-2 end state: Failed (part missing)`; exit 0 |
| A pure FIFO board hands out the oldest queued task | toy: `NextQueued()` with three tasks | `queue head: bike-1`, then `queue head now: drill-3` |
| Removing a state guard changes behaviour | toy mutation: `Complete` with no state check | the `refused complete` line disappears and `drill-3` reads `Done` |
| The solution has 12 projects today | `grep -c '<Project Path' src/ShuttleFleet.slnx` | prints `12` |
| The contract is untouched; a task is not on the wire | read `docs/contract/v1.md`, `v2.md`; `test -f docs/contract/v3.md` | `Version: 1`, `Version: 2`, no `v3.md`; no task type in `src/Contracts` |
| `FleetCore` does not exist yet | `ls src/FleetCore`; `grep -rn 'FleetTask\|TaskBoard\|TaskState' src --include='*.cs'` | no such directory; no matches |
| A pure project is the house pattern | read `src/ShuttleCore/ShuttleCore.csproj` | no `ProjectReference`, no `PackageReference` |
| The guarded-object pattern exists to copy | read `src/ShuttleCore/Shuttle.cs`, `ShuttleState.cs`, `ShuttleRejectedException.cs` | private-set state, `DateTime now`, one named exception, one `ToWireName()` |

Not yet verified:

- Your `FleetCore` project and tests — this assignment *specifies* them; it is not a report on code.
- SQL persistence, the restart survival, the assignment policy, and the task-to-`moveCommand` mapping
  — lessons 08 and 09.
- Reassignment of a failed task — M5. Traffic — M4. WMS orders — M6.

---

## Options survey

There were two real design choices. The full reasons are in
[record 11](../decisions/11-task-lifecycle-and-queue.md); here is the compressed version.

### Survey 1: what is a task at M3? (→ [record 11 §3](../decisions/11-task-lifecycle-and-queue.md))

| | A: a move task — "go to lane `A01-L02`" (chosen) | B: a pallet transfer — load here, unload there | C: a WMS order |
|---|---|---|---|
| Today's v2 contract can carry it | **yes** — `moveCommand { ShuttleId, ToLane }` exists | no — no load/unload command yet | no — no WMS until M6 |
| Executable end to end at M3 | yes | not until the wire grows | not until M6 |
| Throwaway risk | none — moves are always needed | builds a model before its command exists | pre-builds M6 |

**Decision 1: at M3 a task is a move task — a shuttle must reach a target lane.** It is the only thing
the frozen v2 contract can ask a shuttle to do, so a task can truly finish. The record is built so the
work content can grow later: the state machine does not care *what* the work is, only where the task
sits in its life.

### Survey 2: where does the pure task queue live? (→ [record 11 §4](../decisions/11-task-lifecycle-and-queue.md))

| | A: a new pure project `FleetCore` (chosen) | B: inside `FleetManager` | C: in `Contracts` |
|---|---|---|---|
| References at all | none | `Messaging`, `MqttMessaging`, `Contracts` | none |
| Testable with no broker or database | yes, trivially | yes, but tests load the MQTT stack | yes, but mixes domain and wire types |
| Matches the `ShuttleCore` precedent | yes | no | no |

**Decision 2: the task model, lifecycle, and queue live in a new pure project, `FleetCore`, that
references nothing.** `FleetManager` will reference it and drive it, the way `ShuttleSim` drives
`ShuttleCore`. The name is forward-looking: M4's traffic rules are fleet decision logic and belong in
the same pure home.

---

## The lecture

Today we are going to learn about three things: **the task record**, **the task lifecycle**, and
**the task board**.

### The task record

A **task** is one unit of work. It is the thing the fleet manager schedules, hands to a shuttle, and
tracks until it is finished or given up.

Think of a work order at a repair shop. It has a number, it says what to do, and it has a status:
waiting, given to a mechanic, being worked on, fixed, or failed. A task is that work order for a
shuttle.

At M3 the work is simple: **go to a lane**. That is the one command the M2 contract can send. So a
task carries the lane to reach. But the task is more than its work. It also carries its identity and
its place in its life:

```csharp
// src/FleetCore/FleetTask.cs — pure; no I/O, no clock, no ids generated here
public sealed class FleetTask
{
    public string TaskId { get; }
    public string ToLane { get; }
    public DateTime CreatedAt { get; }
    public TaskState State { get; private set; } = TaskState.Queued;
    public string? AssignedShuttleId { get; private set; }
    public string? FailureReason { get; private set; }

    public FleetTask(string taskId, string toLane, DateTime createdAt)
    {
        if (string.IsNullOrWhiteSpace(taskId))
            throw new ArgumentException("task id required", nameof(taskId));
        if (string.IsNullOrWhiteSpace(toLane))
            throw new ArgumentException("target lane required", nameof(toLane));
        TaskId = taskId;
        ToLane = toLane;
        CreatedAt = createdAt;
    }
}
```

Read it line by line.

- `TaskId` is the task's name. One task, one identity. The board rejects a second task with the same
  id. This is the same idea as the command `MessageId` you met in M2.
- `ToLane` is the work: the lane the shuttle must reach.
- `CreatedAt` is when the task entered the queue. The queue is first-in-first-out, so the oldest
  queued task goes first. This is the field that lets you sort later.
- `State` starts at `Queued`. It is `private set`, so the outside world cannot put a task into a
  nonsense state. Only the lifecycle methods change it.
- `AssignedShuttleId` is null until a shuttle is chosen. It answers "who owns this task?"
- `FailureReason` is null unless the task fails. It holds words an operator can read.

The constructor refuses a blank id or a blank lane. A task with no name cannot be tracked, and a task
with nowhere to go is not a task.

Notice what is **not** here. `FleetTask` never calls `DateTime.UtcNow`. It never calls
`Guid.NewGuid`. The caller passes the id and the time in. That is the rule from record 06: time is an
input. It is what lets a test build a task at a known time with a known id, with no clock and no
randomness. A test that depends on the wall clock is a flaky test.

Where you will meet this again: the `ToLane` field becomes the `moveCommand.ToLane` when lesson 08
maps a task to a command. The `TaskId` becomes the primary key in the database. The `FailureReason`
becomes the alarm text an operator reads in M5 and M7.

---

### The task lifecycle

A **lifecycle** is the set of states a thing can be in and the moves between them. A **state machine**
is the code that allows only the legal moves.

The spec names five states. We put them in an enum, so a typo is a compile error and not a wrong
string on the wire:

```csharp
// src/FleetCore/TaskState.cs
public enum TaskState
{
    Queued,       // waiting for a shuttle
    Assigned,     // a shuttle is chosen; work has not started
    InProgress,   // the shuttle has begun
    Done,         // finished (terminal)
    Failed,       // could not finish (terminal)
}
```

The contract speaks lowercase names, so exactly one small function maps enum to wire name. The shuttle
already does this in `ShuttleStateWire.ToWireName`, and a test checks it. Do the same for tasks:

```csharp
// src/FleetCore/TaskState.cs
public static class TaskStateWire
{
    public static string ToWireName(this TaskState state) => state switch
    {
        TaskState.Queued => "queued",
        TaskState.Assigned => "assigned",
        TaskState.InProgress => "in_progress",
        TaskState.Done => "done",
        TaskState.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown task state"),
    };
}
```

Why `in_progress`? The state words are all lowercase, a wire token cannot hold a space, and the
two-word state needs one join. The underscore keeps every letter lowercase. A camelCase
`inProgress` would mix styles. Record 11 decided this so the dashboard and the WMS do not invent their
own spelling later.

Now the moves. Each move is one method. It takes `DateTime now`. A wrong call is refused with a named
exception, `TaskRejectedException` — the same idea as `ShuttleRejectedException`.

```csharp
// src/FleetCore/FleetTask.cs — the guarded moves
public void Assign(string shuttleId, DateTime now)
{
    EnsureInOrder(now);
    if (State != TaskState.Queued)
        throw new TaskRejectedException($"cannot assign while {State}");
    if (string.IsNullOrWhiteSpace(shuttleId))
        throw new TaskRejectedException("shuttle id required");
    AssignedShuttleId = shuttleId;
    State = TaskState.Assigned;
    _lastEventAt = now;
}
```

`Assign` is the move from `Queued` to `Assigned`. Read it:

- `EnsureInOrder(now)` refuses a clock that went backwards, exactly like the shuttle.
- The state check comes first. You can only assign a task that is still waiting. Assign it twice and
  the second call is refused.
- The shuttle id must not be blank. An assigned task with no shuttle is a lie.
- On success, two things change: the task remembers its shuttle, and its state becomes `Assigned`.

The next two moves are one step at a time:

```csharp
public void Start(DateTime now)
{
    EnsureInOrder(now);
    if (State != TaskState.Assigned)
        throw new TaskRejectedException($"cannot start while {State}");
    State = TaskState.InProgress;
    _lastEventAt = now;
}

public void Complete(DateTime now)
{
    EnsureInOrder(now);
    if (State != TaskState.InProgress)
        throw new TaskRejectedException($"cannot complete while {State}");
    State = TaskState.Done;
    _lastEventAt = now;
}
```

`Start` needs `Assigned`. `Complete` needs `InProgress`. You cannot skip a step. A task that is only
`Queued` cannot complete; a task that is `Assigned` cannot complete either, because no shuttle has
started it.

The last move is different. **Fail** can happen from three states, because work can go wrong at three
points:

```csharp
public void Fail(string reason, DateTime now)
{
    EnsureInOrder(now);
    if (State is TaskState.Done or TaskState.Failed)
        throw new TaskRejectedException($"cannot fail while {State}");
    if (string.IsNullOrWhiteSpace(reason))
        throw new TaskRejectedException("reason required");
    FailureReason = reason;
    State = TaskState.Failed;
    _lastEventAt = now;
}
```

- A task can fail while `Queued` (the manager gives up before assigning it), while `Assigned` (the
  shuttle never starts), or while `InProgress` (the move is rejected, or the shuttle goes silent).
- A task cannot fail once it is `Done` or already `Failed`. Those two are **terminal**: nothing comes
  after them.
- The reason must not be blank. A failure with no words is useless to an operator.

A real-world picture: a repair ticket. It can be cancelled before it is given to a mechanic, dropped
when the mechanic cannot start, or failed mid-repair when the part breaks. Once it is marked fixed or
failed, you do not change the ticket; you open a new one. Re-opening failed work — reassigning a
faulted shuttle's task — is M5's job, and it will need its own move.

Here is the whole lifecycle in one picture. Arrows are the allowed moves:

```
            Assign              Start             Complete
  Queued ───────────▶ Assigned ─────────▶ InProgress ───────────▶ Done
    │                    │                     │
    │ Fail               │ Fail                │ Fail
    ▼                    ▼                     ▼
  Failed ◀───────────────┴─────────────────────┘
```

One step forward, never backward. Fail from any live state. Done and Failed are the end.

Where you will meet this again: lesson 08's assignment loop maps the M2 sender's endings onto these
moves. An `Acknowledged` command lets you `Start` and later `Complete`. A rejected ack or a
`NoAcknowledgement` give-up maps to `Fail`.

---

### The task board

A **board** (also called a queue here) is the place that holds the tasks and keeps them in order. The
spec's phrase is "a task queue". This is that queue, and it is pure: a list in memory, nothing else.

A real-world picture: the tickets waiting on a rack at the repair shop's front desk. New tickets go on
the right. The mechanic takes the one from the left — the oldest one. That is first-in-first-out.

```csharp
// src/FleetCore/TaskBoard.cs — pure; no database, no broker, no clock
public sealed class TaskBoard
{
    private readonly List<FleetTask> _tasks = new();

    public FleetTask Add(FleetTask task)
    {
        if (_tasks.Any(existing => existing.TaskId == task.TaskId))
            throw new TaskRejectedException($"duplicate task id {task.TaskId}");
        _tasks.Add(task);
        return task;
    }

    public FleetTask? NextQueued() =>
        _tasks.FirstOrDefault(task => task.State == TaskState.Queued);

    public int Count => _tasks.Count;
}
```

Read it:

- `_tasks` is a plain `List<FleetTask>`. A list keeps the order things were added. That order **is**
  the queue order.
- `Add` refuses a duplicate task id, then adds the task at the end. The list order is first-in order.
- `NextQueued` walks from the front and returns the **first** task whose state is `Queued`. If none is
  queued, it returns null.
- `Count` is how many tasks the board holds.

Why does `NextQueued` check the state instead of just returning the first task? Because a task that is
already `Assigned` or `InProgress` still lives on the board so its progress can be tracked. It is no
longer waiting for a shuttle, so the queue must skip it and hand out the next waiting one. If it did
not skip, the manager would keep re-handing out the same task and never reach the ones behind it.

The board also needs a lookup by id, so any part of the manager can find one task:

```csharp
public bool TryGet(string taskId, out FleetTask? task)
{
    task = _tasks.FirstOrDefault(existing => existing.TaskId == taskId);
    return task is not null;
}

public FleetTask Get(string taskId) =>
    TryGet(taskId, out var task) ? task! : throw new KeyNotFoundException($"unknown task {taskId}");
```

`TryGet` answers yes or no. `Get` throws when the task is missing, which is right when the caller is
sure it exists.

Notice the board has **no database**. It saves nothing and loads nothing. That is on purpose. The
spec says decisions must be testable without a database, and "survive a restart" is its own lesson.
Lesson 08 adds a store that saves and loads this board. The board stays pure.

Where you will meet this again: lesson 08 teaches `TaskBoard.NextQueued()` as the seed of the
assignment loop — take the next waiting task, pick a shuttle, and `Assign` it. The SQL layer in lesson
08 saves this same list to PostgreSQL, ordering by `CreatedAt` so the queue order survives the
restart.

---

### Recap

A **task record** is one unit of work: an id, a target lane, the time it was created, its state, and
who owns it. The id and the time are passed in, never generated inside, so tests are deterministic.

The **task lifecycle** is five states — queued, assigned, in progress, done, failed — with guarded
moves. One step forward at a time. Fail from any live state. Done and Failed are terminal. A wrong
move throws `TaskRejectedException`.

The **task board** is the pure queue. A list in first-in order, a duplicate check, a lookup by id, and
`NextQueued()` that hands out the oldest task still waiting. No broker, no database, no clock.

That is M3's first piece: the pure shape everything else stands on.

---

## Worked example

A toy: a repair shop's ticket moves through its five states, and the front desk keeps tickets in a
first-in-first-out queue. It is **not** the assignment — no fleet, no shuttle, no broker, no database,
no clock. It exists to show the lifecycle and the queue at a size you can hold in one glance. Do not
copy it. The toy stays a toy.

Everything below ran in `/tmp/opencode/tasktoy` and was deleted afterwards. It is one console project.

`Program.cs` (complete):

```csharp
// A toy: a repair shop's ticket moves through its five states, and the
// front desk keeps tickets in a first-in-first-out queue.
// It is NOT the assignment. No fleet, no broker, no database.

var desk = new TicketBoard();
var bike = desk.Add("bike-1");
var mower = desk.Add("mower-2");
var drill = desk.Add("drill-3");

Console.WriteLine($"queue head: {desk.NextQueued()!.Id}");

bike.Assign("alice");
bike.Start();
bike.Complete();
Console.WriteLine($"bike-1 end state: {bike.State}");

// Two refused moves: a ticket cannot start or complete before it is ready.
try
{
    mower.Start();
}
catch (TicketRejectedException exception)
{
    Console.WriteLine($"refused start: {exception.Message}");
}

try
{
    drill.Complete();
}
catch (TicketRejectedException exception)
{
    Console.WriteLine($"refused complete: {exception.Message}");
}

mower.Assign("bob");
mower.Fail("part missing");
Console.WriteLine($"mower-2 end state: {mower.State} ({mower.FailureReason})");
Console.WriteLine($"queue head now: {desk.NextQueued()?.Id ?? "(none)"}");
Console.WriteLine($"drill-3 was never touched: {drill.State}");

public enum TicketState { Queued, Assigned, InProgress, Done, Failed }

// The front desk: a pure, first-in-first-out queue of tickets.
public sealed class TicketBoard
{
    private readonly List<Ticket> _tickets = new();

    public Ticket Add(string id)
    {
        if (_tickets.Any(ticket => ticket.Id == id))
            throw new TicketRejectedException($"duplicate ticket id {id}");
        var ticket = new Ticket(id);
        _tickets.Add(ticket);
        return ticket;
    }

    public Ticket? NextQueued() =>
        _tickets.FirstOrDefault(ticket => ticket.State == TicketState.Queued);

    public int Count => _tickets.Count;
}

// One ticket: a small guarded state machine, one state per method call.
public sealed class Ticket
{
    public string Id { get; }
    public TicketState State { get; private set; } = TicketState.Queued;
    public string? Assignee { get; private set; }
    public string? FailureReason { get; private set; }

    public Ticket(string id) => Id = id;

    public void Assign(string who)
    {
        if (State != TicketState.Queued)
            throw new TicketRejectedException($"cannot assign while {State}");
        if (string.IsNullOrWhiteSpace(who))
            throw new TicketRejectedException("assignee required");
        Assignee = who;
        State = TicketState.Assigned;
    }

    public void Start()
    {
        if (State != TicketState.Assigned)
            throw new TicketRejectedException($"cannot start while {State}");
        State = TicketState.InProgress;
    }

    public void Complete()
    {
        if (State != TicketState.InProgress)
            throw new TicketRejectedException($"cannot complete while {State}");
        State = TicketState.Done;
    }

    public void Fail(string reason)
    {
        if (State is TicketState.Done or TicketState.Failed)
            throw new TicketRejectedException($"cannot fail while {State}");
        if (string.IsNullOrWhiteSpace(reason))
            throw new TicketRejectedException("reason required");
        FailureReason = reason;
        State = TicketState.Failed;
    }
}

public sealed class TicketRejectedException(string message) : Exception(message);
```

Run it:

```bash
cd /tmp/opencode/tasktoy
dotnet run
```

Observed output (exit 0):

```
queue head: bike-1
bike-1 end state: Done
refused start: cannot start while Queued
refused complete: cannot complete while Queued
mower-2 end state: Failed (part missing)
queue head now: drill-3
drill-3 was never touched: Queued
```

Read the output:

- `queue head: bike-1` — the board hands out the **oldest** queued ticket, not the newest. That is
  first-in-first-out.
- `bike-1 end state: Done` — three moves in a row (`Assign`, `Start`, `Complete`) took it from queued
  to done.
- `refused start: cannot start while Queued` — `mower.Start()` was called before `Assign`. The state
  guard refused it.
- `refused complete: cannot complete while Queued` — `drill.Complete()` was called on a queued ticket.
  Refused again. The guard in `Complete` is what stopped a ticket from being finished that was never
  started.
- `mower-2 end state: Failed (part missing)` — `Fail` is allowed from `Queued` and remembers the
  reason.
- `queue head now: drill-3` — `bike-1` is done and `mower-2` failed, so the queue skips both and hands
  out the still-queued `drill-3`.
- `drill-3 was never touched: Queued` — the refused `Complete` did not change the state.

Now the mutation that shows the guard means something. Remove the state check inside `Complete`:

```csharp
public void Complete()
{
    State = TicketState.Done;   // the guard is gone
}
```

Observed output:

```
queue head: bike-1
bike-1 end state: Done
refused start: cannot start while Queued
mower-2 end state: Failed (part missing)
queue head now: (none)
drill-3 was never touched: Done
```

The `refused complete` line is gone. `drill-3` was completed from `Queued`, so it reads `Done` even
though nobody touched it. The guard is load-bearing: without it, a wrong move silently succeeds. A
green run proves the toy works; the mutation proves the checks would *notice* the wrong move.

Your `FleetTask` is this toy with the real names from record 11: `TaskState`, `TaskRejectedException`,
`TaskId`, `ToLane`, and `DateTime now` on every move.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. `FleetTask.State` is a `private set` property. Why not a public setter? What bug does the private
   setter stop that a public one would allow?
2. The lifecycle allows `Fail` from `Queued`, `Assigned`, and `InProgress`, but `Complete` only from
   `InProgress`. Explain why each `Fail` entry is real, and why `Complete` needs just one.
3. `TaskBoard.NextQueued()` returns the first task whose state is `Queued`, not simply the first task.
   What would break if it returned the first task no matter its state?
4. `FleetTask` never calls `Guid.NewGuid` or `DateTime.UtcNow`; the caller passes both. Where do the
   id and the time come from in the real fleet manager, and why does passing them in make the tests
   deterministic?
5. `Done` and `Failed` are terminal at M3. Which later milestone needs to re-open failed work, and
   what will it have to add to this machine?

---

## The assignment

Build the pure task model and its queue: a new `FleetCore` project with the task record, the five-state
lifecycle, and an in-memory first-in-first-out board, plus unit tests. This is the first piece of M3's
task queue. SQL persistence and the assignment policy are the next lessons; do not build them here.

### Requirements

1. **New project `src/FleetCore/FleetCore.csproj`** — a class library, `net10.0`, `ImplicitUsings`
   and `Nullable` enabled, exactly like `src/ShuttleCore/ShuttleCore.csproj`. It must reference
   **nothing**: no `ProjectReference`, no `PackageReference`. Add it to `src/ShuttleFleet.slnx`.

2. **New test project `src/FleetCore.Tests/FleetCore.Tests.csproj`** — xUnit, `net10.0`, matching
   `src/ShuttleCore.Tests/ShuttleCore.Tests.csproj`. Reference only `..\FleetCore\FleetCore.csproj`.
   Add it to `src/ShuttleFleet.slnx`.

3. **`src/FleetCore/TaskState.cs`** — the five states and the wire mapping, exactly as record 11 §6:
   ```csharp
   public enum TaskState { Queued, Assigned, InProgress, Done, Failed }

   public static class TaskStateWire
   {
       public static string ToWireName(this TaskState state) => state switch
       {
           TaskState.Queued => "queued",
           TaskState.Assigned => "assigned",
           TaskState.InProgress => "in_progress",
           TaskState.Done => "done",
           TaskState.Failed => "failed",
           _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown task state"),
       };
   }
   ```

4. **`src/FleetCore/TaskRejectedException.cs`** — a `sealed` exception type with one constructor
   taking a message, exactly like `ShuttleRejectedException`.

5. **`src/FleetCore/FleetTask.cs`** — the guarded task object:
   - constructor `FleetTask(string taskId, string toLane, DateTime createdAt)`; reject a blank
     `taskId` or `toLane` with `ArgumentException` (parameter name included);
   - read-only `TaskId`, `ToLane`, `CreatedAt`; `TaskState State { get; private set; }` starting at
     `Queued`; `string? AssignedShuttleId`; `string? FailureReason`;
   - `Assign(string shuttleId, DateTime now)` — only from `Queued`; reject a blank `shuttleId`;
   - `Start(DateTime now)` — only from `Assigned`;
   - `Complete(DateTime now)` — only from `InProgress`;
   - `Fail(string reason, DateTime now)` — from `Queued`, `Assigned`, or `InProgress`; reject a blank
     `reason`; **not** from `Done` or `Failed`;
   - every move rejects a `now` earlier than the previous move's timestamp (a private
     `_lastEventAt`, exactly like `Shuttle`); equal timestamps are allowed;
   - a wrong move throws `TaskRejectedException` with a message that names the current state.

6. **`src/FleetCore/TaskBoard.cs`** — the pure queue:
   - `FleetTask Add(FleetTask task)` — reject a duplicate `TaskId` with `TaskRejectedException`; keep
     insertion order;
   - `bool TryGet(string taskId, out FleetTask? task)`;
   - `FleetTask Get(string taskId)` — throw `KeyNotFoundException` when missing;
   - `FleetTask? NextQueued()` — the **first** task in insertion order whose `State` is `Queued`;
     `null` when none is queued;
   - `int Count { get; }`.

7. **`src/FleetCore.Tests/`** — pure tests, no broker and no database. At least:
   - `Assign_from_queued_enters_assigned`;
   - `Start_from_assigned_enters_in_progress`;
   - `Complete_from_in_progress_enters_done`;
   - `Fail_from_queued_enters_failed`;
   - `Fail_from_assigned_enters_failed`;
   - `Fail_from_in_progress_enters_failed`;
   - `Assign_twice_is_rejected`;
   - `Start_while_queued_is_rejected`;
   - `Complete_while_assigned_is_rejected`;
   - `Fail_after_done_is_rejected`;
   - `Assign_with_a_blank_shuttle_is_rejected`;
   - `Fail_with_a_blank_reason_is_rejected`;
   - `A_move_with_a_timestamp_before_the_previous_one_is_rejected`;
   - `Task_wire_names_match_record_11`;
   - `Board_rejects_a_duplicate_task_id`;
   - `Board_hands_out_the_oldest_queued_task_first`;
   - `Board_next_queued_skips_assigned_and_finished_tasks`;
   - `Board_get_returns_the_task_by_id`.
   Every `[Fact]` carries at least one `Assert`. Use a fixed `new DateTime(2026, 1, 1, 0, 0, 0,
   DateTimeKind.Utc)` base time, like the `ShuttleCore` tests.

8. **Pure means pure.** `FleetCore` must contain **no** `DateTime.UtcNow`, no `Guid.NewGuid`, no
   `Task.Delay`, no `Thread.Sleep`, and no references to `Npgsql`, `MQTTnet`, `Messaging`, or
   `Contracts`. Ids and times are inputs. The acceptance greps for this.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are
asserted, not admired. **Read every line's purpose before you run it.**

```bash
# Phase 1 — the new projects exist and the solution grows by exactly two
test -f src/FleetCore/FleetCore.csproj || echo "missing FleetCore.csproj"          # nothing
test -f src/FleetCore/FleetTask.cs || echo "missing FleetTask.cs"                 # nothing
test -f src/FleetCore/TaskBoard.cs || echo "missing TaskBoard.cs"                 # nothing
test -f src/FleetCore/TaskState.cs || echo "missing TaskState.cs"                 # nothing
test -f src/FleetCore.Tests/FleetCore.Tests.csproj || echo "missing tests"        # nothing
grep -c '<Project Path' src/ShuttleFleet.slnx                                     # prints 14 (12 + 2)
grep -q 'ProjectReference Include="..\\FleetCore\\FleetCore.csproj"' \
  src/FleetCore.Tests/FleetCore.Tests.csproj                                      # exit 0
grep -c 'ProjectReference\|PackageReference' src/FleetCore/FleetCore.csproj       # prints 0: FleetCore references nothing
```

```bash
# Phase 2 — pure means pure: no clock, no ids, no I/O, no wire types inside FleetCore
grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' \
  src/FleetCore --include='*.cs'                                                  # nothing
grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts' src/FleetCore --include='*.cs'    # nothing
grep -rniE 'Npgsql|MQTTnet|Postgres' src/FleetCore/FleetCore.csproj               # nothing
grep -q 'TaskRejectedException' src/FleetCore/FleetTask.cs                        # exit 0
grep -q 'enum TaskState' src/FleetCore/TaskState.cs                               # exit 0
grep -q 'NextQueued' src/FleetCore/TaskBoard.cs                                   # exit 0
```

```bash
# Phase 3 — from src/: build, run the suite, walk the new names
cd src
dotnet build                                                                      # exit 0, 0 warnings
T=$(mktemp); dotnet test > "$T" 2>&1                                              # exit 0 (read $? here)
grep -c 'Passed!' "$T"                                                            # prints 7 (six suites + FleetCore.Tests)
grep -c 'No test is available' "$T"                                               # prints 0
rm -f "$T"
L=$(mktemp); dotnet test --list-tests > "$L" 2>&1
for t in Assign_from_queued_enters_assigned \
         Start_from_assigned_enters_in_progress \
         Complete_from_in_progress_enters_done \
         Fail_from_queued_enters_failed \
         Fail_from_assigned_enters_failed \
         Fail_from_in_progress_enters_failed \
         Assign_twice_is_rejected \
         Start_while_queued_is_rejected \
         Complete_while_assigned_is_rejected \
         Fail_after_done_is_rejected \
         Assign_with_a_blank_shuttle_is_rejected \
         Fail_with_a_blank_reason_is_rejected \
         A_move_with_a_timestamp_before_the_previous_one_is_rejected \
         Task_wire_names_match_record_11 \
         Board_rejects_a_duplicate_task_id \
         Board_hands_out_the_oldest_queued_task_first \
         Board_next_queued_skips_assigned_and_finished_tasks \
         Board_get_returns_the_task_by_id; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                         # nothing
rm -f "$L"
cd ..
```

The `Passed!` count moves **6 → 7**: this lesson adds one project and its test suite; it changes no
existing project. If your suite count did not change, the new tests are not connected to the solution.

```bash
# Phase 4 — no regression: the M1 demo and the manager's start line still hold
cd src
F=$(mktemp); dotnet run --project ShuttleSim/ShuttleSim.csproj > "$F" 2>&1         # exit 0
grep -c '"ShuttleId"' "$F"                                                        # prints 7 (M1 demo unchanged)
rm -f "$F"
G=$(mktemp); dotnet run --project FleetManager/FleetManager.csproj > "$G" 2>&1    # exit 0
grep -q 'Fleet Manager Coming Online' "$G"                                        # exit 0
rm -f "$G"
cd ..
```

```bash
# Phase 5 — the contract is untouched: a task is not a wire message
grep -qE '^Version: 2$' docs/contract/v2.md                                       # exit 0
grep -qE '^Version: 1$' docs/contract/v1.md                                       # exit 0
test ! -f docs/contract/v3.md || echo "no wire change this lesson"                # nothing
grep -rniE 'TaskState|FleetTask|TaskBoard' src/Contracts --include='*.cs' \
  --exclude-dir=obj --exclude-dir=bin                                             # nothing
```

**Mutation check (do this by hand, then revert).** Delete the state check inside `Complete` so it
becomes `State = TaskState.Done;` with no guard. From `src/`, `dotnet test` must now **fail**:
`Complete_while_assigned_is_rejected` goes red. That is what proves the guard is load-bearing. Revert
the change and run `dotnet test` again to confirm it is green. Do not leave the mutation in.

### Stretch goal

Add the seed of lesson 08's assignment loop to `TaskBoard`:

```csharp
public FleetTask? AssignNext(string shuttleId, DateTime now);
```

It takes the task `NextQueued()` would return, calls `Assign(shuttleId, now)` on it, and returns it;
it returns `null` when no task is queued. Write one test that assigns two tasks in order and proves
the board hands them out oldest first, and one test that `AssignNext` returns `null` on an empty
queue. Keep it pure. Accept: all suites stay green and the method reads as "take the head, assign it,
return it."

### Not in this assignment

- **SQL persistence and the restart survival** — lesson 08, record 03. The `TaskBoard` stays pure; a
  later store saves and loads it.
- **The assignment policy (which shuttle, load balancing) and mapping a task to a `moveCommand`** —
  lesson 08.
- **Reassignment of a failed task** — M5. `Done` and `Failed` are terminal at M3.
- **Traffic reservations** — M4. **WMS orders** — M6. **Dashboard** — M7.
- **Any new wire message.** A task is internal at M3; the contract stays v2.

---

## Closing

Read the spec's M3 line once more: *"A task queue with a clear lifecycle (queued, assigned, in
progress, done, failed)."* This lesson makes that queue real and testable with no broker and no
database, because the model is pure and time and ids are inputs.

You are not done with M3. Assignment, SQL, and the three-shuttle restart proof are still ahead. But
every one of them stands on this shape, so build it well and be ready to say, without notes, why the
task state is private-set, why `Fail` can happen from three states while `Complete` happens from one,
and why `NextQueued()` skips tasks that are already assigned.
