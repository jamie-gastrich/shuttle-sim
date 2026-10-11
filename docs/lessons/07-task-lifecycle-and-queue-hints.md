# Hints for lesson 07: the task and its lifecycle

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- Two new projects and three new shapes. The shapes: `TaskState` (the five spec names), `FleetTask`
  (the guarded machine), `TaskBoard` (the queue). Ask which one is missing before you touch any code.
- **Copy the `Shuttle` shape from M1.** `Shuttle.cs`, `ShuttleState.cs`, and
  `ShuttleRejectedException.cs` are the template. Same pattern: a private-set state, one method per
  move, `DateTime now` passed in, one named exception, one `ToWireName()`.
- **The state is `private set`.** Only the `Assign`/`Start`/`Complete`/`Fail` methods change it. If
  the outside could set `State`, the guards would be pointless.
- **Ids and time are inputs.** `FleetTask` never calls `Guid.NewGuid()` or `DateTime.UtcNow`. The
  caller passes them. That is what makes the tests deterministic and keeps `FleetCore` pure.
- **`Fail` can happen from three states; `Complete` from one.** A task can fail before a shuttle is
  chosen, while assigned, or while in progress. It can only complete after it is in progress.
- **The board is a list plus two checks.** A `List<FleetTask>` keeps insertion order. `Add` rejects a
  duplicate id. `NextQueued` walks from the front for the first task whose state is `Queued`.
- **Add both projects to `src/ShuttleFleet.slnx`.** `dotnet build` from `src/` builds the solution;
  an unlisted project and its tests will not run.
- **`FleetCore` references nothing.** No `ProjectReference`, no `PackageReference`. If you find
  yourself adding a `using` for `Contracts` or `Messaging`, stop — that is the wrong project.
- Keep the house rules: trailing newline, no bare `WaitForExit()`, no nine-space indent.

---

## Tier 2 — direction

- Order of work, and why:

  1. `TaskState` — the enum and `ToWireName`. Test the five names with no dependencies.
  2. `TaskRejectedException` — one line, then done.
  3. `FleetTask` — fields, constructor guards, then `Assign`, `Start`, `Complete`, `Fail`, then the
     private `EnsureInOrder`. Test each ending before wiring the board.
  4. `TaskBoard` — `Add`, `TryGet`, `Get`, `NextQueued`, `Count`. Test the fifo order and the
     duplicate check.
  5. The acceptance gates, including the by-hand mutation.

- **`FleetTask` fields.** `TaskId`, `ToLane`, and `CreatedAt` are set in the constructor and never
  change. `State` starts `Queued`. `AssignedShuttleId` and `FailureReason` start null.
- **The guards, in order.** Every move first calls `EnsureInOrder(now)`, then checks the state, then
  checks blank inputs (shuttle id for `Assign`, reason for `Fail`). Check the state before the blank
  input so a wrong-state call is reported as wrong-state.
- **`EnsureInOrder`.** Keep a private `DateTime? _lastEventAt`. Reject `now` earlier than it; allow
  equal. Set it at the end of every successful move. This is exactly `Shuttle.EnsureInOrder`.
- **`NextQueued`.** `_tasks.FirstOrDefault(task => task.State == TaskState.Queued)`. It returns null
  when none is queued, so the return type is `FleetTask?`.
- **The board's order is the list's order.** Do not sort; insertion into the `List` is the queue
  order. `CreatedAt` is there for the SQL lesson, which will order by it after a restart.
- **`Get` vs `TryGet`.** `TryGet` uses `FirstOrDefault` and returns the bool. `Get` calls `TryGet`
  and throws `KeyNotFoundException` when missing.
- **Tests use a fixed base time.** `private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0,
  DateTimeKind.Utc);`, exactly like `ShuttleCore.Tests/StateTransitionTests.cs`. Advance it with
  `Start.AddSeconds(1)` for the clock-order test.
- **The mutation.** After the suite is green, delete the `if (State != TaskState.InProgress)` line in
  `Complete`, run `dotnet test`, and watch `Complete_while_assigned_is_rejected` fail. Revert.

---

## Tier 2b — the fixed names the acceptance greps for

- Projects: `src/FleetCore/FleetCore.csproj`, `src/FleetCore.Tests/FleetCore.Tests.csproj`.
- `TaskState` (`Queued`, `Assigned`, `InProgress`, `Done`, `Failed`) — `public enum`.
- `TaskStateWire.ToWireName()` — `queued`, `assigned`, `in_progress`, `done`, `failed`.
- `TaskRejectedException` — `public sealed class`.
- `FleetTask`: `TaskId`, `ToLane`, `CreatedAt`, `State`, `AssignedShuttleId`, `FailureReason`;
  methods `Assign`, `Start`, `Complete`, `Fail`.
- `TaskBoard`: `Add`, `TryGet`, `Get`, `NextQueued`, `Count`.
- Test names are listed in the assignment; the anchored `--list-tests` walk checks all eighteen.
- The exact grep strings: `enum TaskState`, `TaskRejectedException`, `NextQueued`,
  `ProjectReference Include="..\FleetCore\FleetCore.csproj"`, and the purity greps
  (`DateTime\.UtcNow`, `Guid\.NewGuid`, `Npgsql`, `MQTTnet`).

---

## Tier 3 — near-solution

Templates, not a solution: the shapes and the fixed names come from record 11 and the assignment;
type every line yourself. Every name the acceptance greps for appears verbatim below.

### The state and its wire names

```csharp
// src/FleetCore/TaskState.cs
namespace FleetCore;

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

### The exception

```csharp
// src/FleetCore/TaskRejectedException.cs
namespace FleetCore;

public sealed class TaskRejectedException : Exception
{
    public TaskRejectedException(string message) : base(message)
    {
    }
}
```

### The task, sketched

```csharp
// src/FleetCore/FleetTask.cs — shape; you write every guard and message
namespace FleetCore;

public sealed class FleetTask
{
    private DateTime? _lastEventAt;

    public FleetTask(string taskId, string toLane, DateTime createdAt)
    {
        // reject blank taskId and blank toLane; assign the three read-only properties
    }

    public string TaskId { get; }
    public string ToLane { get; }
    public DateTime CreatedAt { get; }
    public TaskState State { get; private set; } = TaskState.Queued;
    public string? AssignedShuttleId { get; private set; }
    public string? FailureReason { get; private set; }

    public void Assign(string shuttleId, DateTime now)
    {
        // EnsureInOrder; must be Queued; shuttleId not blank; set the two fields; _lastEventAt = now
    }

    public void Start(DateTime now)
    {
        // EnsureInOrder; must be Assigned; State = InProgress; _lastEventAt = now
    }

    public void Complete(DateTime now)
    {
        // EnsureInOrder; must be InProgress; State = Done; _lastEventAt = now
    }

    public void Fail(string reason, DateTime now)
    {
        // EnsureInOrder; not Done or Failed; reason not blank; set FailureReason and State; _lastEventAt = now
    }

    private void EnsureInOrder(DateTime now)
    {
        // reject now earlier than _lastEventAt; equal is allowed
    }
}
```

### The board, sketched

```csharp
// src/FleetCore/TaskBoard.cs
namespace FleetCore;

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

    public bool TryGet(string taskId, out FleetTask? task)
    {
        task = _tasks.FirstOrDefault(existing => existing.TaskId == taskId);
        return task is not null;
    }

    public FleetTask Get(string taskId) =>
        TryGet(taskId, out var task) ? task! : throw new KeyNotFoundException($"unknown task {taskId}");

    public FleetTask? NextQueued() =>
        _tasks.FirstOrDefault(task => task.State == TaskState.Queued);

    public int Count => _tasks.Count;
}
```

### The test skeleton

```csharp
// src/FleetCore.Tests/TaskLifecycleTests.cs
namespace FleetCore.Tests;

public class TaskLifecycleTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Assign_from_queued_enters_assigned()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);

        task.Assign("SH-01", Start);

        Assert.Equal(TaskState.Assigned, task.State);
        Assert.Equal("SH-01", task.AssignedShuttleId);
    }

    [Fact]
    public void Start_while_queued_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);

        Assert.Throws<TaskRejectedException>(() => task.Start(Start.AddSeconds(1)));
    }
}
```

What is deliberately *not* given here: the guard bodies and messages, the `EnsureInOrder` body, the
other sixteen test bodies, and the by-hand mutation. Type every line yourself and be able to explain,
out loud, why the state is private-set, why `Fail` has three entry states, and why `NextQueued` skips
non-queued tasks.
