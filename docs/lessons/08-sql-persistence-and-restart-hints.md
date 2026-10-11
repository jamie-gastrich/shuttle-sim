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
- **The DB test lives in `FleetManager.Tests`,** not `FleetCore.Tests`. Tag it `[Trait("Category", "Integration")]`.
- **The container has no database and no `docker` binary.** Never start Docker from the test. The build command excludes the `Integration` trait; a separate with-DB command runs it.
- **Connection string from config.** `SHUTTLE_DB_CONNECTION_STRING`, else `SHUTTLE_DB_HOST` / `DB_HOST_PORT` / `SHUTTLE_DB_USER` / `SHUTTLE_DB_PASSWORD` / `SHUTTLE_DB_NAME`. Local values are only defaults.

---

## Tier 2 — direction

1. Add hydration: in `FleetTask.cs`, add `using System.Runtime.CompilerServices;` and `[assembly: InternalsVisibleTo("FleetManager")]` (top level). Add internal constructor taking all fields including state/assigned/failure/lastEventAt.
2. Add `IFleetTaskRepository.cs` to `FleetCore`.
3. Add `Npgsql` PackageReference to `FleetManager.csproj`.
4. Implement `PostgresFleetTaskRepository` in `FleetManager`. Use parameterized queries, `CREATE TABLE IF NOT EXISTS`, transactions for SaveAll.
5. Add the restart-survival test to `FleetManager.Tests` (not `FleetCore.Tests`). Put `[Trait("Category", "Integration")]` on the class or the test method. Build the connection string from the environment variables; do not start Docker from the test.
6. Change `docker/Dockerfile.build` line 6 to `CMD ["dotnet", "test", "src/ShuttleFleet.slnx", "-v", "m", "--filter", "Category!=Integration"]`. Keep the `RUN dotnet build` line as-is.
7. Ensure `FleetCore` csproj has no DB refs, and `FleetCore.Tests` does not reference `FleetManager`.

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

**FleetManager.Tests/RestartSurvivalTests.cs — trait and config**
```csharp
[Trait("Category", "Integration")]
public class RestartSurvivalTests
{
    private static string BuildConnectionString()
    {
        var whole = Environment.GetEnvironmentVariable("SHUTTLE_DB_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(whole))
            return whole;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("SHUTTLE_DB_HOST") ?? "127.0.0.1",
            Username = Environment.GetEnvironmentVariable("SHUTTLE_DB_USER") ?? "shuttle",
            Password = Environment.GetEnvironmentVariable("SHUTTLE_DB_PASSWORD") ?? "shuttle",
            Database = Environment.GetEnvironmentVariable("SHUTTLE_DB_NAME") ?? "shuttle",
        };
        var port = Environment.GetEnvironmentVariable("DB_HOST_PORT");
        if (!string.IsNullOrWhiteSpace(port))
        {
            if (!int.TryParse(port, out var value))
                throw new InvalidOperationException("DB_HOST_PORT must be a valid integer.");
            builder.Port = value;
        }
        return builder.ConnectionString;
    }
}
```

**docker/Dockerfile.build line 6 (filtered CMD)**
```
CMD ["dotnet", "test", "src/ShuttleFleet.slnx", "-v", "m", "--filter", "Category!=Integration"]
```

**Two-gate commands**
```bash
# container is green with no DB (the integration test is excluded)
docker run --rm shuttle-fleet-build

# the DB test really runs against a real DB (not skipped)
docker compose -f docker/compose.yml up -d database
dotnet test src/FleetManager.Tests/FleetManager.Tests.csproj -v m --logger "console;verbosity=detailed" --filter "Category=Integration"
docker compose -f docker/compose.yml down
```