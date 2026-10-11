# Hints for lesson 08: SQL persistence so fleet state survives a restart

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- **Keep FleetCore pure.** Interface in `FleetCore`, impl in `FleetManager`. No Npgsql in `FleetCore`.
- **Hydration needed.** `FleetTask` state is private-set; add internal constructor + `InternalsVisibleTo("FleetManager")`.
- **Schema minimal.** `fleet_tasks` with task_id (PK text), to_lane (text NOT NULL), created_at (timestamptz NOT NULL), state (text NOT NULL, wire name), assigned_shuttle_id (text NULL), failure_reason (text NULL).
- **Order on load.** `ORDER BY created_at ASC, task_id ASC`.
- **Map wire names.** Save: `task.State.ToWireName()`; load: reverse from string to `TaskState`.
- **Restart proof uses new instances.** Create new board/repo after persisting.

---

## Tier 2 — direction

1. Add hydration: in `FleetTask.cs`, add `using System.Runtime.CompilerServices;` and `[assembly: InternalsVisibleTo("FleetManager")]` (top level). Add internal constructor taking all fields including state/assigned/failure/lastEventAt.
2. Add `IFleetTaskRepository.cs` to `FleetCore`.
3. Add `Npgsql` PackageReference to `FleetManager.csproj`.
4. Implement `PostgresFleetTaskRepository` in `FleetManager`. Use parameterized queries, `CREATE TABLE IF NOT EXISTS`, transactions for SaveAll.
5. Add restart-survival test. Start DB with docker compose before running test if needed.
6. Ensure FleetCore csproj has no DB refs.

---

## Tier 3 — near-solution snippets

**FleetCore/IFleetTaskRepository.cs**
```csharp
namespace FleetCore;

public interface IFleetTaskRepository
{
    Task SaveAsync(FleetTask task, CancellationToken ct);
    Task SaveAllAsync(IEnumerable<FleetTask> tasks, CancellationToken ct);
    Task<IEnumerable<FleetTask>> LoadAllAsync(CancellationToken ct);
    Task<FleetTask?> GetAsync(string taskId, CancellationToken ct);
}
```

**FleetCore/FleetTask.cs additions**
```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FleetManager")]

public sealed class FleetTask
{
    // ... existing ...
    internal FleetTask(string taskId, string toLane, DateTime createdAt, TaskState state,
        string? assignedShuttleId, string? failureReason, DateTime? lastEventAt)
    {
        TaskId = taskId; ToLane = toLane; CreatedAt = createdAt;
        State = state; AssignedShuttleId = assignedShuttleId; FailureReason = failureReason;
        _lastEventAt = lastEventAt;
    }
}
```

**Table**
```sql
CREATE TABLE IF NOT EXISTS fleet_tasks (
  task_id text PRIMARY KEY,
  to_lane text NOT NULL,
  created_at timestamptz NOT NULL,
  state text NOT NULL,
  assigned_shuttle_id text NULL,
  failure_reason text NULL
);
```

**FleetManager.csproj add**
```xml
<PackageReference Include="Npgsql" Version="8.0.6" />
```
(Use compatible version; check existing environment.)