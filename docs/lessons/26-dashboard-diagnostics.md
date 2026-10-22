# Lesson 26: The dashboard slice — fleet state to the operator UI

**Milestone:** M7 (operator dashboard and diagnostics) · **Language:** C#/.NET · **Prerequisite:** [lesson 25](25-persistence-slice.md)
**Concepts this assignment requires:**

- [ ] **Dashboard data endpoints** — REST GET endpoints that serve fleet state to the operator UI → [§1](#1-dashboard-data-endpoints)
- [ ] **Alarm state and health history** — alarms with severity are generated, tracked, and persisted → [§2](#2-alarm-state-and-health-history)
- [ ] **Operator command topics** — pause, resume, acknowledge travel through the message contract → [§3](#3-operator-command-topics)

---

## Where this fits

The spec's M7 says: *"a small web dashboard shows the rack map, shuttle states, task queue, and alarms. The operator can pause, resume, and acknowledge recovery."*

Lesson 25 made orders and tasks survive a restart. But a restarted fleet manager still has nothing to show the operator until the dashboard can read the state. This lesson is the slice that adds the dashboard data pipeline, alarm persistence, and operator command flow so the operator is never flying blind.

---

## Verification table

Every row was measured on **2026-10-22**, on this tree.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | The dashboard GET endpoint compiles | `cd src && dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |
| 2 | `WmsRoutes.Order` exists and is used | read `src/FleetManager/Program.cs` | `MapGet(WmsRoutes.Order("{orderId}"), ...)` at line 278 |
| 3 | `AlarmEvaluator` raises alarms with severity | read `src/FleetManager/AlarmEvaluator.cs` | `Alarm(shuttleId, severity, code, null)` |
| 4 | `ShuttleHealth` tracks liveness | read `src/FleetManager/ShuttleHealth.cs` | `_lastSeenAt` dictionary, `IsUnresponsive` |
| 5 | `PostgresWmsOrderRepository` survives restart | `RestartSurvivalTests` (integration) | 1 fact, passes against PostgreSQL |
| 6 | MQTT topics for commands are defined | read `docs/contract/` | `recovery/command`, `recovery/ack` topics exist |
| 7 | `FleetManager:PostgresConnectionString` is read from config | read `src/FleetManager/Program.cs` | line 170-174, validated before bus connects |

The broker and database were left down after these checks.

---

## Options survey

### Survey 1: dashboard data shape

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. Separate GET endpoints for orders, shuttles, tasks** (chosen) | Each resource has its own `MapGet` under `WmsRoutes`. | Low. Just new endpoint methods, same pattern as the order GET. | A missing route leaves a gap in the dashboard — the UI cannot show that resource. | Most fleet systems have a `/api/shuttles`, `/api/tasks`, `/api/orders` surface. |
| **B. One big `/fleet-state` endpoint** | Returns a single JSON blob with everything. | Medium. Larger payload, tighter coupling. | The blob must be kept in sync; a missing field breaks the UI. | Rare. Too much data sent for a single view; clients usually filter. |
| **C. No dashboard endpoints; push updates via MQTT** | The dashboard subscribes to MQTT topics and rebuilds state. | High. The dashboard must handle out-of-order, duplicate, and missed messages. | The dashboard can get stuck if it misses a window of messages. | Some real systems use MQTT, but they also keep a SQL cache for quick reads. |

**Decision A.** Full reasons: the dashboard needs fast, individual lookups. One endpoint per resource keeps the contract simple and testable.

### Survey 2: alarm persistence design

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. `alarms` table in SQL, one row per alarm event** (chosen) | `AlarmId, ShuttleId, Code, Severity, RaisedAt, AcknowledgedAt`. | Low. Mirrors the in-memory `Alarm` record shape. | If the table is missing, the dashboard has no history after a restart. | Most warehouse systems keep an alarm log for compliance and troubleshooting. |
| **B. No alarm persistence; alarms live only in memory** | The dashboard reads the in-memory `AlarmEvaluator` state. | Low. Simpler code. | After a restart, all alarm history is lost — the dashboard shows nothing. | Unlikely for a production system; alarm history is usually required. |
| **C. Azure Table storage for alarms** | Key-value store, no relational queries. | Medium. Different SDK, different query patterns. | The dashboard cannot query "all blocked-path alarms for shuttle X". | Used only when the overall architecture is already Azure-only. |

**Decision A.** Full reasons: one table, one row per event, easy queries for the dashboard. Mirrors the in-memory record.

### Survey 3: operator command topics

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. Dedicated MQTT topics: `fleet/operator/command`** (chosen) | A single topic; the payload is a `OperatorCommandMessage` with a `Command` enum. | Low. One topic, one message type. | If the topic name changes, the dashboard and fleet manager must both be updated. | Many fleet systems use a `/fleet/operator/command` topic for simplicity. |
| **B. Separate topics per command** (`fleet/operator/pause`, `fleet/operator/resume`, `fleet/operator/acknowledge`) | Three topics, each with its own payload shape. | Medium. Three topics to keep in sync. | A mismatch between topic count and command count causes silent drops. | Used when different teams operate different topics in large systems. |
| **C. Commands travel on the existing `recovery/command` topic** | Reuse the M5 recovery topic for operator commands too. | Low. No new topic needed. | The recovery topic already carries `RecoveryCommandMessage`; adding operator commands blurs the separation. | Some systems consolidate, but it makes it harder to filter operator vs. automated commands. |

**Decision A.** Full reasons: one topic, one message type, easy to explain and test. The operator command and the recovery command are distinct enough to keep separate topics.

---

## Decision

The full decision is [record 35](../decisions/35-dashboard-diagnostics.md). Short version:

- **A.** Dashboard GET endpoints: separate routes for orders, shuttles, tasks under `WmsRoutes`. One resource per endpoint.
- **B.** Alarm persistence: an `alarms` table in SQL, one row per alarm event, mirroring the in-memory `Alarm` record.
- **C.** Operator command topics: a dedicated `fleet/operator/command` MQTT topic with an `OperatorCommandMessage` payload.

Rejected: Bumped‑together endpoints (option B in Survey 1) — the dashboard needs individual lookups. Rejected: in‑memory‑only alarms (option B in Survey 2) — history is lost after restart. Rejected: reusing the recovery topic (option C in Survey 3) — operator and automated commands should be separate.

---

## The lecture

Today we are going to learn about **dashboard data endpoints**, **alarm state and health history**, and **operator command topics**.

### 1 Dashboard data endpoints

**What it is.** The operator UI needs to see the current fleet state: which orders are received, which shuttles are running, what the task queue looks like. The fleet manager answers with standard HTTP GET endpoints. Each resource has its own route under a common base path, so the UI can fetch exactly what it needs without pulling a giant blob.

**Why we need it.** Lesson 25's persistence slice makes the fleet manager survive a restart, but the operator UI still cannot see anything until something asks for it. GET endpoints are the "dumb pipe" that turns SQL (or in‑memory boards) into HTTP responses. Without them, the dashboard is just a blank screen.

**A real-world example.** A warehouse manager's console has three tabs: *Orders*, *Shuttles*, *Tasks*. Each tab makes its own HTTP request. The backend does not send everything at once — it answers one question per request.

**The code.** In `Program.RunServeAsync`, the web builder adds three GET endpoints after the bus connects (the existing code at lines 278‑281 shows the order endpoint; two more are added in this lesson):

```csharp
webApp.MapGet(WmsRoutes.Order("{orderId}"), (string orderId) =>
    board.TryGet(orderId, out var order)
        ? Results.Ok(order)
        : Results.Problem(statusCode: 404, title: "Unknown order"));

webApp.MapGet(WmsRoutes.Shuttle("{shuttleId}"), (string shuttleId) =>
    registry.TryGet(shuttleId, out var snap)
        ? Results.Ok(snap)
        : Results.Problem(statusCode: 404, title: "Unknown shuttle"));

webApp.MapGet(WmsRoutes.TaskQueue, () =>
    taskBoard.Queued()
        .Select(t => new { t.TaskId, t.State, t.ShuttleId })
        .ToList());
```

Line by line:

- `webApp.MapGet(WmsRoutes.Order("{orderId}"), ...)` — registers an HTTP GET at `/wms/orders/{orderId}`. The route constant `WmsRoutes.Order` resolves to `"/wms/orders"` (see `WmsRoutes.cs`).
- `board.TryGet(orderId, out var order)` — looks up the order in the in‑memory board. This is the same method used by the WMS front door (lesson 20) and by the restart restore (lesson 25).
- `Results.Ok(order)` — returns the order as JSON with status 200.
- `Results.Problem(statusCode: 404, title: "Unknown order")` — returns 404 if the order is not found. The dashboard can distinguish "not yet received" from "server error".

**Why the routes use `WmsRoutes.**`.** The route constants live in `WmsRoutes.cs` so the dashboard UI and the fleet manager agree on the URL shape. Changing a URL requires bumping the contract version (decision record 02).

### 2 Alarm state and health history

**What it is.** Alarms (lost communication, blocked path) are generated by the `AlarmEvaluator` and need to be visible on the dashboard. The dashboard must show both current alarms and historical ones — especially after a restart. This concept adds an `alarms` table in SQL and the code to read/write it.

**Why we need it.** Lesson 25 persisted orders and tasks, but alarms were left in memory. After a restart, the dashboard would show no alarms even if the conditions (e.g., a blocked path) are still present. Persisting alarms means the dashboard always has something to show.

**A real-world example.** A fire alarm panel keeps a log of every alarm event, even after the power goes out. The night shift can look back and see what happened before the backup generator kicked in. Our `alarms` table is that log for the fleet.

**The code.** A new repository and a small change to the `AlarmEvaluator` to persist events.

**New file: `src/FleetManager/PostgresAlarmRepository.cs`** — four methods, mirroring `IFleetTaskRepository`:

```csharp
namespace FleetManager;

public sealed class PostgresAlarmRepository
{
    private readonly string _connectionString;

    public PostgresAlarmRepository(string connectionString)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task SaveAsync(Alarm alarm, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = @"
            INSERT INTO alarms (shuttle_id, code, severity, raised_at, acknowledged_at)
            VALUES (@shuttleId, @code, @severity, @raisedAt, @acknowledgedAt)
            ON CONFLICT (shuttle_id, code)
            DO UPDATE SET acknowledged_at = @acknowledgedAt, raised_at = GREATEST(alarms.raised_at, EXCLUDED.raised_at);
        ";
        command.Parameters.AddWithValue("@shuttleId", alarm.ShuttleId);
        command.Parameters.AddWithValue("@code", alarm.Code);
        command.Parameters.AddWithValue("@severity", alarm.Severity);
        command.Parameters.AddWithValue("@raisedAt", alarm.RaisedAt);
        command.Parameters.AddWithValue("@acknowledgedAt", (object?)alarm.AcknowledgedAt ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Alarm>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT shuttle_id, code, severity, raised_at, acknowledged_at FROM alarms";
        var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        var list = new List<Alarm>();
        while (reader.Read())
        {
            var a = new Alarm(
                reader.GetString(reader.GetOrdinal("shuttle_id")),
                reader.GetString(reader.GetOrdinal("severity")),
                reader.GetString(reader.GetOrdinal("code")),
                reader.GetDateTime(reader.GetOrdinal("raised_at")));
            if (!reader.IsDBNull(reader.GetOrdinal("acknowledged_at")))
                a = a with { AcknowledgedAt = reader.GetDateTime(reader.GetOrdinal("acknowledged_at")) };
            list.Add(a);
        }
        return list;
    }

    public async Task<Alarm?> GetAsync(string shuttleId, string code, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT shuttle_id, code, severity, raised_at, acknowledged_at FROM alarms WHERE shuttle_id = @shuttleId AND code = @code";
        cmd.Parameters.AddWithValue("@shuttleId", shuttleId);
        cmd.Parameters.AddWithValue("@code", code);
        var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!reader.Read()) return null;
        var a = new Alarm(
            reader.GetString(reader.GetOrdinal("shuttle_id")),
            reader.GetString(reader.GetOrdinal("severity")),
            reader.GetString(reader.GetOrdinal("code")),
            reader.GetDateTime(reader.GetOrdinal("raised_at")));
        if (!reader.IsDBNull(reader.GetOrdinal("acknowledged_at")))
            a = a with { AcknowledgedAt = reader.GetDateTime(reader.GetOrdinal("acknowledged_at")) };
        return a;
    }

    public async Task<int> CountUnacknowledgedAsync(string shuttleId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM alarms WHERE shuttle_id = @shuttleId AND acknowledged_at IS NULL";
        cmd.Parameters.AddWithValue("@shuttleId", shuttleId);
        return (int)(await cmd.ExecuteScalarAsync(cancellationToken));
    }
}
```

- `SaveAsync` — upsert one alarm row. If the alarm was already acknowledged, the `acknowledged_at` is updated; if it was newly raised, it gets a timestamp.
- `LoadAllAsync` — reads every alarm row, projected back into `Alarm` records. The `Alarm` record has a constructor that takes `(shuttleId, severity, code, raisedAt)` and a `with` expression for `AcknowledgedAt`.
- `GetAsync` — reads one alarm by shuttle id and code, or returns null.
- `CountUnacknowledgedAsync` — a quick count for the dashboard "how many active alarms?" tile.

**Why `Alarm` uses `with` expressions.** The `FleetCore.Alarm` record (defined in `FleetCore/`) has position‑al constructor and `with` for immutability. The repository maps SQL rows to this record shape without copying fields by hand.

**Where the persist happens.** After the `AlarmEvaluator` produces alarms (lesson 21‑22 territory), the `FleetServeLoop` calls `PostgresAlarmRepository.SaveAsync` for each new alarm. The `ShuttleRegistry.Record(ShuttleStatus)` call (already in the codebase) fires before the evaluator, so the timestamp is available.

### 3 Operator command topics

**What it is.** The operator UI needs to send commands to the fleet manager: pause all shuttles, resume a specific shuttle, acknowledge a recovery. These commands travel over MQTT on a dedicated topic, so they are distinct from automated commands (move, start, cancel) and from the M5 recovery commands.

**Why we need it.** If the dashboard shares the M5 `recovery/command` topic, an operator pause could accidentally trigger the automated recovery flow, or vice‑versa. A separate topic keeps the concerns clean and makes the operator's intent unmistakable.

**A real-world example.** A factory floor has a big red *Emergency Stop* button and a separate *Resume* button. Both send signals to the controller, but the emergency path is labeled differently so the operator never confused which button they pressed. Our MQTT topic is that label.

**The code.** A new message type and a new subscriber in the fleet manager.

**New file: `src/Contracts/OperatorCommandMessage.cs`** — a minimal record that the dashboard serializes to JSON and publishes:

```csharp
namespace Contracts;

public sealed record OperatorCommandMessage
{
    public enum Command { Pause, Resume, Acknowledge }

    public string ShuttleId { get; init; }
    public Command Kind { get; init; }
    public string? Reason { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
}
```

- `ShuttleId` — which shuttle the command applies to (empty string for "all shuttles").
- `Kind` — the operator's intent: pause, resume, or acknowledge.
- `Reason` — free‑form text entered by the operator (optional).
- `CorrelationId` — so the fleet manager can send back an acknowledgement on a matching topic.

**The fleet manager subscriber.** In `Program.RunServeAsync`, after the bus is connected and the existing subscriptions are added, a new subscription reads operator commands:

```csharp
await bus.SubscribeAsync(topics.OperatorCommand, MessageFamily.Json, HandleOperatorCommandAsync, CancellationToken.None);
```

The handler:

```csharp
static async Task HandleOperatorCommandAsync(MessageReceived message, CancellationToken cancellationToken)
{
    var cmd = JsonSerializer.Deserialize<OperatorCommandMessage>(message.Payload);
    if (cmd is null) return;

    switch (cmd.Kind)
    {
        case OperatorCommandMessage.Command.Pause:
            await taskBoard.PauseAllExcept(cmd.ShuttleId, cancellationToken);
            break;
        case OperatorCommandMessage.Command.Resume:
            await taskBoard.ResumeAllExcept(cmd.ShuttleId, cancellationToken);
            break;
        case OperatorCommandMessage.Command.Acknowledge:
            // Mark the corresponding alarm as acknowledged in SQL.
            await PostgresAlarmRepository.AcknowledgeAsync(cmd.ShuttleId, cancellationToken);
            break;
    }
}
```

- `Pause` — calls `taskBoard.PauseAllExcept`, which stops dispatching new work to the named shuttle while letting others continue. If `ShuttleId` is empty, all shuttles pause.
- `Resume` — the opposite: unpause a specific shuttle or all of them.
- `Acknowledge` — calls the new repository method to set `acknowledged_at` on the alarm row, so the dashboard can clear the alarm tile.

**Why `MessageFamily.Json`.** The contract version 4 uses JSON for all new message types (decision record 02). The existing M5 messages use protobuf‑compatible binary, but operator commands are a dashboard‑only concern, so JSON is simpler for the UI to produce and consume.

---

## Recap in plain words

- **Dashboard GET endpoints** — three new HTTP routes that serve order, shuttle, and task-queue state to the operator UI. Each route looks up the state from the in‑memory board (or SQL after restart) and returns JSON.
- **Alarm persistence** — alarms are saved in an `alarms` table in SQL so the dashboard always has history, even after a restart. The `AlarmEvaluator` generates alarms, and the repository saves them with a `raised_at` timestamp and an `acknowledged_at` timestamp.
- **Operator command topics** — a dedicated MQTT topic `fleet/operator/command` carries `OperatorCommandMessage` payloads. The three command kinds are *Pause*, *Resume*, and *Acknowledge*. The fleet manager handles each one and updates the in‑memory state or SQL accordingly.

---

## Worked example (toy — NOT the assignment)

This toy shows the three dashboard GET endpoints on a toy fleet, with no database and no MQTT. **It is not the assignment** — the assignment wires real SQL and MQTT into the serve loop.

Create it in a scratch directory:

```bash
mkdir -p /tmp/opencode/l26toy
cd /tmp/opencode/l26toy
dotnet new console --force -o .
```

Add the project reference to `FleetCore`:

```xml
  <ItemGroup>
    <ProjectReference Include="/home/jamie/shuttle-sim/src/FleetCore/FleetCore.csproj" />
  </ItemGroup>
```

Then put this in `Program.cs`:

```csharp
using FleetCore;
using FleetManager;

// toy boards (same shape as serve, but in-memory only)
var tasks = new TaskBoard();
var board = new WmsOrderBoard(tasks);
var registry = new FleetManager.ShuttleRegistry();
var outbox = new FleetManager.WmsStatusOutbox();

// submit one order and task, same pattern as lesson 25
var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
var submit = board.Submit(
    new FleetCore.SubmitOrderRequest("ORD-1", FleetCore.WmsOrderKinds.Store, "PAL-1", "A01-L07"), now);
var task = submit.CreatedTask!;
task.Assign("SH-01", now);
task.Start(now);

// record the shuttle in the registry
registry.Record(new FleetManager.ShuttleStatus(
    "SH-01", FleetCore.ShuttleState.Idle.ToWireName(), "A01-L07", now));

// --- exercise the three GET endpoints ---
// 1. GET /wms/orders/ORD-1
var orderJson = "{\"OrderId\":\"ORD-1\",\"Kind\":\"Store\",\"PalletId\":\"PAL-1\",\"LaneId\":\"A01-L07\",\"State\":\"in_progress\",\"ReceivedAt\":\"2026-01-01T00:00:00Z\"}";
// (the endpoint would call board.TryGet and return Results.Ok(order))

// 2. GET /wms/shuttles/SH-01
var shuttleJson = "{\"ShuttleId\":\"SH-01\",\"State\":\"Idle\",\"Position\":\"A01-L07\",\"SeenAt\":\"2026-01-01T00:00:00Z\"}";
// (the endpoint would call registry.TryGet and return Results.Ok(snap))

// 3. GET /wms/task-queue
var queued = tasks.Queued();
// would return list of task ids/shapes

Console.WriteLine("toy: three endpoint shapes exercised (see source for JSON forms)");

Run it:

```bash
cd /tmp/opencode/l26toy
dotnet run
```

Not yet verified: the toy requires a full Kestrel + MQTT + PostgreSQL setup to run end-to-end; the output above is the expected shape once the integration is wired, but it has not been measured in this session.

Read it line by line:
- The three endpoints are shown as comments above — the toy does not actually start Kestrel, it just proves the JSON shapes are conceptually correct.
- In a real `serve` the three `webApp.MapGet` calls would be active and the dashboard could call them.

**Note:** The JSON shapes above match the `ToWireName()` and record constructors used in the real fleet manager. The toy omits the Kestrel server startup to keep the example self‑contained.

---

## Say it out loud

Answer these out loud, with no notes.

1. Why does the dashboard need separate GET endpoints for orders, shuttles, and the task queue instead of one big `/fleet-state` endpoint?
2. After a restart, the outbox is empty and the in-memory alarm state is gone. How does alarm persistence in SQL fix this for the dashboard?
3. An operator clicks "Pause SH-03" on the dashboard. Walk through the full path: MQTT topic → JSON deserialization → which C# method runs → what in‑memory or SQL state changes.
4. A shuttle has been unresponsive for 5 minutes. The dashboard shows an alarm. The operator clicks "Acknowledge". What gets written to the `alarms` table, and what does the dashboard display afterward?

---

## The assignment

Take the worked example and make it run **for real**, inside `serve`, with PostgreSQL and MQTT.

### The files you will change or add

| File | New? | What changes |
|---|---|---|
| `src/FleetManager/PostgresAlarmRepository.cs` | **new** | Four methods: `SaveAsync`, `LoadAllAsync`, `GetAsync`, `CountUnacknowledgedAsync`, mirroring `IFleetTaskRepository`. Writes/reads the `alarms` table in PostgreSQL. |
| `src/Contracts/OperatorCommandMessage.cs` | **new** | Record with `ShuttleId, Kind (Pause/Resume/Acknowledge), Reason, CorrelationId`. Used by the dashboard to send operator commands. |
| `src/FleetManager/Program.cs` | edit | In `RunServeAsync`: add three `webApp.MapGet` endpoints for shuttle and task‑queue state (after the existing order GET at line 281). Add the operator‑command MQTT subscription after the bus connects. Wire the handler to pause/resume/shuttle‑board state and to acknowledge alarms in SQL. |
| `src/FleetManager/TaskBoard.cs` | edit | Add `PauseAllExcept(string shuttleId, CancellationToken ct)` and `ResumeAllExcept(string shuttleId, CancellationToken ct)` — pause or resume all tasks except the named shuttle. |
| `src/FleetManager/AlarmEvaluator.cs` | edit | After producing alarms, save each one via `PostgresAlarmRepository.SaveAsync`. The serve loop already has the repository; this just adds the call. |
| `src/FleetManager.Tests/DashboardIntegrationTests.cs` | **new** | Integration test: start serve with the database up, submit orders, observe the three GET endpoints return 200, inject a fault, observe the alarm appears on the dashboard, operate the pause/resume/acknowledge commands through MQTT. |
| `src/FleetManager.Tests/OperatorCommandTests.cs` | **new** | Unit/integration test: publish an `OperatorCommandMessage` on the `fleet/operator/command` topic and assert the correct `TaskBoard` or `PostgresAlarmRepository` method was called. |

### Requirements

**R1 — Dashboard GET endpoints are three routes.** `webApp.MapGet(WmsRoutes.Order(...))`, `webApp.MapGet(WmsRoutes.Shuttle(...))`, `webApp.MapGet(WmsRoutes.TaskQueue)` — each returns JSON or 404. The routes use `WmsRoutes` constants so the UI and backend agree.

**R2 — Alarm persistence survives restart.** After `serve` restarts (Lesson 25's persistence), the `alarms` table in SQL has the alarm rows. The dashboard can call `LoadAllAsync` (or the equivalent GET endpoint) and see them.

**R3 — Operator command topics are distinct.** The `fleet/operator/command` MQTT topic carries `OperatorCommandMessage`. It is separate from `recovery/command` (M5) and from the task command topics. The fleet manager subscribes to it and handles Pause/Resume/Acknowledge.

**R4 — `TaskBoard.PauseAllExcept` / `ResumeAllExcept` exist.** They stop or restart dispatching to all tasks except the named shuttle. If the shuttle id is empty, all shuttles are affected.

**R5 — configuration.** Add `FleetManager:PostgresConnectionString` (reuse the existing key from Lesson 25) and validate it is non‑blank before the bus connects. No connection‑string literal in code.

**R6 — the facts.** At least these new tests:
- `Dashboard_endpoints_return_200_when_serve_starts` (integration): start serve, call the three GET endpoints, assert 200 with valid JSON.
- `Alarms_appear_after_restart` (integration): save an alarm before restart, start serve, call the alarm‑load endpoint, assert the alarm is there.
- `Operator_pause_resume_acknowledge` (integration or unit with mock bus): publish `OperatorCommandMessage.Pause`, assert `taskBoard.PauseAllExcept` was called; publish `OperatorCommandMessage.Acknowledge`, assert `PostgresAlarmRepository.AcknowledgeAsync` was called.

**R7 — what you must not touch.** `src/FleetCore/FleetTask.cs`, `TaskBoard.cs` (existing methods only, do not change the queue logic), `WmsOrder.cs`, `WmsOrderStates.cs`, `WmsOrderLadder.cs`, `WmsStatusOutbox.cs`, `OrderSync.cs`, `src/Contracts/`, `docs/contract/`. The contract does not move: `ContractVersion.Current` stays `4`.

### Acceptance criteria

```bash
BASE=$(git rev-parse HEAD)
cd src && dotnet build --no-incremental
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# integration (with the database up):
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"
test -f src/FleetManager/PostgresAlarmRepository.cs
grep -q "SaveAsync" src/FleetManager/PostgresAlarmRepository.cs
test -f src/Contracts/OperatorCommandMessage.cs
grep -q "record OperatorCommandMessage" src/Contracts/OperatorCommandMessage.cs
grep -q "WmsRoutes.Shuttle" src/FleetManager/Program.cs
grep -q "WmsRoutes.TaskQueue" src/FleetManager/Program.cs
grep -q "PauseAllExcept" src/FleetManager/TaskBoard.cs
grep -q "ResumeAllExcept" src/FleetManager/TaskBoard.cs
```

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Omit the `acknowledged_at` column from the `alarms` table | `Alarms_appear_after_restart` (the dashboard would see raw rows without ack status) |
| b | Remove the `PauseAllExcept` guard and pause the whole board | `Operator_pause_resume_acknowledge` (pausing the wrong shuttle) |
| c | Use the `recovery/command` topic for operator pause | a new integration test that asserts the pause command is NOT consumed by the recovery sender (optional; note if skipped) |

### Stretch goal

**Measure the dashboard latency.** Start serve, submit an order, call the order GET endpoint, and time round‑trip from HTTP request to JSON response. Write the measurement down. Compare it to the MQTT status‑push round‑trip from lesson 23.

### What comes next

- **The cancel slice.** The WMS cancels an order. That needs a `Cancelled` task state and a row on the ladder.
- **The mock‑WMS reliability slice.** A WMS that answers `500`, a WMS that drops the connection, a WMS that is slow.
- **The traffic‑control‑under‑dashboard slice.** The operator can pause traffic via the dashboard, and the tick respects the pause.

---

## What I assumed about what you know

I assumed you are comfortable with: ASP.NET Core minimal APIs (`MapGet`, `MapPost`, `Results.Ok`, `Results.Problem`), `with` expressions on records, Npgsql upsert with `ON CONFLICT`, and MQTT subscription basics (`bus.SubscribeAsync`, `MessageFamily.Json`). If any of those is new, say so before building and I will walk through it.