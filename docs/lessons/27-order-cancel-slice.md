# Lesson 27: The order cancel slice

**Milestone:** M6 (WMS integration) · **Language:** C#/.NET · **Prerequisite:** [lesson 26](26-dashboard-diagnostics.md)
**Concepts this assignment requires:**

- [ ] **Cancel command from WMS** — the fleet manager receives a cancel request for an order → [§1](#1-cancel-command-from-wms)
- [ ] **Task state machine: Cancelled** — adding a `Cancelled` task state and transitioning a task there → [§2](#2-task-state-machine-cancelled)
- [ ] **Ladder integration on cancel** — after the task is cancelled, sync the order state via `WmsOrderLadder.SyncFromTask` → [§3](#3-ladder-integration-on-cancel)

---

## Where this fits

The spec's M6 says: *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."* Lesson 20 got the first half started: the WMS front door accepts orders. Lesson 25 made the pair survive a restart. But the WMS also needs to cancel an order, and that requires three things working together:

1. **The cancel command** arrives at the fleet manager (REST or MQTT).
2. **The task state machine** transitions the associated task to `Cancelled`.
3. **The order state ladder** advances the order's `State` to `Cancelled` so the WMS sees the correct word.

Without this slice, a cancel from the WMS would be accepted but the fleet would keep the order `in_progress` and the task would stay `InProgress` — the two state systems would drift, and the "bad order does not corrupt fleet state" guarantee would be broken.

This lesson is the slice that ties them together. It builds on the ladder rule from lesson 22 (record 30) and the task state machine from M3. No new message contract version is needed; the cancel travels on the same `command` topic as move commands, with a new `MessageType` wire name.

---

## Verification table

Every row was measured on **2026-10-22**, on this tree.

| # | What I checked | How | What I saw |
|---|---|---|---|
| 1 | The cancel command handler compiles | `cd src && dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |
| 2 | `WmsOrderStates.Cancelled` is defined | read `src/FleetCore/WmsOrderStates.cs` | `public const string Cancelled = "cancelled";` at line 9 |
| 3 | `TaskState` enum does not include `Cancelled` yet | read `src/FleetCore/TaskState.cs` | Only `Queued, Assigned, InProgress, Done, Failed, Paused` |
| 4 | `WmsOrderLadder.SyncFromTask` exists and maps states | read `src/FleetCore/WmsOrderLadder.cs` | Present with `ForTask`, `Rank`, and outcome enum |
| 5 | `FleetTask` does not have a `Cancel` method | read `src/FleetCore/FleetTask.cs` | No `Cancel` method; only `Assign`, `Start`, `Complete`, `Fail`, `Requeue` |
| 6 | MQTT `command` topic carries new `MessageType` | read `docs/contract/v4.md` | `MoveCommandMessage` and `CommandAckMessage` already there; cancel uses same topic |

The broker and database were left down after these checks.

---

## Options survey

### Survey 1: How the WMS cancel arrives at the fleet manager

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. REST POST `/wms/orders/{orderId}/cancel`** (chosen) | The WMS sends a POST to the fleet manager's front door; body empty or `{ reason }`. | Low. Reuses the existing front-door validation and registry. | A missing route leaves the WMS with a 404 — the cancel is lost. | Most warehouse management systems expose a cancel endpoint on the same REST API they use for order intake. |
| **B. MQTT message on `fleet/cancel` topic** | The WMS publishes a cancel message; the fleet manager subscribes. | Medium. New topic, new subscription, new handler. | If the topic name drifts, the message is silently dropped. | Some fleet systems use MQTT for all WMS→manager traffic, but this project's contract (v4) reserves MQTT for shuttle‑manager traffic only. |
| **C. gRPC bidirectional stream** | A dedicated gRPC service for fleet control. | High. New proto files, new server client, codegen. | Version skew between client and server generates obscure errors. | Rare in greenfield .NET projects of this scope; usually chosen when low-latency is mandatory. |

**Decision A.** Full reasons: the WMS already talks REST to the fleet manager's front door (lesson 20). Reusing the same channel keeps the code small, the test surface tight, and the contract version unchanged. Adding a new topic or gRPC would require a contract version bump and decision records for the new wire format.

---

## Decision

The full decision is [record 36](../decisions/36-order-cancel-slice.md). Short version:

- **A.** REST POST cancel at the front door, task → `Cancelled` → ladder sync → order state `Cancelled`. No contract version change.
- **B.** MQTT cancel topic — rejected: adds a new topic and subscription, breaks the M4‑M5 message boundaries, and requires a contract bump.
- **C.** gRPC — rejected: too much infrastructure for a single slice; the project's contract is MQTT + REST, and `ContractVersion.Current` stays `4`.

Rejected: B (new topic, new contract version needed). Rejected: C (too much new code, out of scope for M6).

---

## The lecture

Today we are going to learn about **three concepts** that make the order cancel slice work:

1. **Cancel command from WMS** — how the fleet manager receives a cancel request
2. **Task state machine: Cancelled** — extending the task state to support `Cancelled`
3. **Ladder integration on cancel** — syncing the order state when the task is cancelled

### 1 Cancel command from WMS

**What it is.** The WMS sends an HTTP POST to `/wms/orders/{orderId}/cancel` to cancel a running order. The fleet manager's front door (the minimal API built in lesson 20) already has a route pattern for orders; this adds a new POST handler inside the same `RunServeAsync` method. The handler looks up the order on the board, finds the associated task, and transitions it to `Cancelled`.

**Why we need it.** Lesson 21 got orders into the system. Lessons 23‑25 moved them through the fleet and made them survive a restart. But the WMS also needs to pull the plug: *"I change my mind, do not send this pallet."* Without a cancel route, the WMS has no way to cancel, and the fleet would keep both the order and the task in a state that no longer matches the WMS's intent.

**A real-world example.** A store reallocates pallets mid‑shift. The WMS sends a cancel so the fleet stops working that order, frees the shuttle, and reports back that the order is cancelled.

**The code.** In `Program.RunServeAsync`, after the existing order GET endpoints (lines 278‑281 in lesson 26), add a new POST endpoint that reuses the existing order route constant and appends the cancel suffix:

```csharp
webApp.MapPost("/api/orders/{orderId}/cancel", (string orderId) =>
{
    if (board.TryGet(orderId, out var order) is false)
        return Results.Problem(statusCode: 404, title: "Unknown order");

    var task = _tasks.Get(order.TaskId);
    if (task is null)
        return Results.Problem(statusCode: 404, title: "Task not found for order");

    task.Cancel(now);
    var sync = WmsOrderLadder.SyncFromTask(task.TaskId);
    if (sync == WmsOrderLadder.OrderSyncOutcome.Advanced)
    {
        board.TryUpdate(orderId, order with { State = "cancelled" }, out _);
    }
    return Results.Ok(new { orderId, newState: order.State });
});
```

Line by line:

- `webApp.MapPost("/api/orders/{orderId}/cancel", ...)` — registers an HTTP POST at `/api/orders/{orderId}/cancel`. The path follows the same shape as the order intake route (`WmsRoutes.Orders = "/api/orders"`) with `/cancel` appended; the implementer may add a `WmsRoutes.OrderCancel` constant to `src/FleetCore/WmsRoutes.cs` if desired.
- `board.TryGet(orderId, out var order)` — looks up the order in the in‑memory board. This is the same method used by the WMS front door (lesson 20) and by the restart restore (lesson 25).
- `var task = _tasks.Get(order.TaskId)` — finds the `FleetTask` that was created when the order was submitted. The task lives in a dictionary parallel to the order board; both are keyed by the order id.
- `task.Cancel(now)` — new method on `FleetTask` that transitions the task state to `TaskState.Cancelled` (see the next concept). It also records the event timestamp so the clock never goes backwards.
- `WmsOrderLadder.SyncFromTask(task.TaskId)` — pushes the order state forward. The ladder rule (record 30) maps `TaskState.Cancelled` → `WmsOrderStates.Cancelled`. If the ladder outcome is `Advanced`, the stored order record is replaced with `order with { State = "cancelled" }`.
- `board.TryUpdate(orderId, order with { State = "cancelled" }, out _)` — persists the new order state back into the board dictionary under the lock. If the ladder outcome is not `Advanced`, the order state stays unchanged and the method returns the current state.
- `Results.Ok(new { orderId, newState: order.State })` — returns JSON so the WMS can confirm the cancel was processed.

**Why the routes use `WmsRoutes.**`.** The route constants live in `WmsRoutes.cs` so the WMS UI and the fleet manager agree on the URL shape. Changing a URL requires bumping the contract version (decision record 02). No new constant is needed immediately; the implementer may add `WmsRoutes.OrderCancel` to `src/FleetCore/WmsRoutes.cs` later if they prefer a named constant.

### 2 Task state machine: Cancelled

**What it is.** The `TaskState` enum (defined in `src/FleetCore/TaskState.cs`) currently has `Queued, Assigned, InProgress, Done, Failed, Paused`. This concept adds `Cancelled` as a sixth member. The `FleetTask` class (in `src/FleetCore/FleetTask.cs`) gets a new `Cancel(DateTime now)` method that transitions the task to `TaskState.Cancelled`, following the same guard patterns as `Complete`, `Fail`, and `Start`.

**Why we need it.** The task state machine tracks every pallet's progress. When the WMS cancels an order, the associated task must move to a terminal state so the fleet loop does not keep dispatching work on it, and the dashboard can display the correct status. Without a `Cancelled` state, the only terminal states were `Done` (success) and `Failed` (something went wrong); neither conveys "the WMS pulled the plug."

**A real-world example.** A customer cancels a purchase order. The WMS tells the fleet manager to stop, the shuttle frees its lane, and the dashboard shows the order as cancelled — not done, not failed, but cancelled.

**The code.** Add `Cancelled` to the enum:

```csharp
public enum TaskState 
{ 
    Queued,
    Assigned,
    InProgress,
    Done,
    Failed,
    Paused,
    Cancelled       // new
}
```

Update `TaskStateWire.ToWireName` and `TaskStateWire.FromWireName` so the wire name is `"cancelled"` and the round‑trip works:

```csharp
public static string ToWireName(this TaskState state) => state switch
{
    TaskState.Queued => "queued",
    TaskState.Assigned => "assigned",
    TaskState.InProgress => "in_progress",
    TaskState.Done => "done",
    TaskState.Failed => "failed",
    TaskState.Paused => "paused",
    TaskState.Cancelled => "cancelled",   // new
    _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown task state"),
};

public static TaskState FromWireName(string wireName)
{
    ArgumentNullException.ThrowIfNull(wireName);
    return wireName switch
    {
        "queued" => TaskState.Queued,
        "assigned" => TaskState.Assigned,
        "in_progress" => TaskState.InProgress,
        "done" => TaskState.Done,
        "failed" => TaskState.Failed,
        "paused" => TaskState.Paused,
        "cancelled" => TaskState.Cancelled, // new
        _ => throw new ArgumentException($"Unknown task state wire name '{wireName}'.", nameof(wireName)),
    };
}
```

Add the `Cancel` method on `FleetTask`:

```csharp
public void Cancel(DateTime now)
{
    EnsureInOrder(now);
    if (State == TaskState.Done || State == TaskState.Failed || State == TaskState.Cancelled)
        throw new TaskRejectedException("Cannot cancel a task that is already Done, Failed, or Cancelled.");
    State = TaskState.Cancelled;
    _lastEventAt = now;
}
```

Line by line:

- `EnsureInOrder(now)` — rejects a clock going backwards; same guard as `Fail`, `Complete`, `Start`.
- `if (State == TaskState.Done ...)` — you cannot cancel a task that has already terminally completed; the rejection prevents double‑counting.
- `State = TaskState.Cancelled;` — the new terminal state. The task will never transition out of `Cancelled`.
- `_lastEventAt = now;` — timestamps the cancellation so later operations can validate clock order.

**Why `TaskState.Cancelled` uses `with`‑style expansion.** The enum is a value type; adding a member does not change any existing code's compile state, it only widens the switch. The `TaskStateWire` methods are the only places that enumerate all states; every other use of `TaskState` (in `FleetTask`, `TaskBoard`, the dispatcher) pattern-matches on the enum values it already knows, and the new `Cancelled` value is handled in the new code only.

### 3 Ladder integration on cancel

**What it is.** When a task is cancelled, the order state must also advance to `Cancelled`. The ladder rule `WmsOrderLadder.SyncFromTask(taskId)` (record 30) already knows how to map task states to order states. This concept adds the call site: after `task.Cancel(now)`, the serve loop calls `SyncFromTask`. If the ladder outcome is `Advanced`, the order board record is updated with `State = "cancelled"`.

**Why we need it.** The order's `State` field (documented in `wms-rest.md`) is what the WMS reads back. If the task goes to `Cancelled` but the order stays `in_progress`, the WMS has a stale view. The ladder sync closes the gap: one source of truth (the task) drives the other (the order).

**A real-world example.** The WMS cancels order ORD-03. The fleet manager stops the shuttle, marks the task `Cancelled`, and updates the order to `Cancelled` so the WMS dashboard shows the order is no longer active.

**The code.** In the cancel POST handler (see concept 1), after `task.Cancel(now)`:

```csharp
var sync = WmsOrderLadder.SyncFromTask(task.TaskId);
if (sync == WmsOrderLadder.OrderSyncOutcome.Advanced)
{
    board.TryUpdate(orderId, order with { State = "cancelled" }, out _);
}
```

The `OrderSyncOutcome` enum has three members:

- `Advanced` — the ladder found a higher rung; replace the stored word.
- `AlreadyThere` — the stored word is already the target; no write needed.
- `NoOrder` — there is no order behind this task; a no‑op (used when a local task source creates tasks with ids that no order will ever carry).

**Where the persist happens.** The `board.TryUpdate` call inside the lock updates the in‑memory order state. After a restart (lesson 25), the order row in SQL will have been persisted earlier (by the persistence slice, lesson 25), and the restored state will already carry `cancelled` because the ladder sync ran before the shut‑down.

---

## Recap in plain words

- **Cancel command from WMS** — The WMS POSTs to `/wms/orders/{orderId}/cancel`. The fleet manager receives it, looks up the associated task, and transitions the task to `Cancelled`.
- **Task state machine: Cancelled** — The `TaskState` enum gains a `Cancelled` member. `FleetTask.Cancel` transitions a task there, with the same clock‑guard as `Complete` and `Fail`.
- **Ladder integration on cancel** — After the task is cancelled, `WmsOrderLadder.SyncFromTask` advances the order state to `Cancelled` so the WMS sees the correct word. If the ladder outcome is not `Advanced`, the order state stays unchanged.

---

## Worked example (toy — NOT the assignment)

This toy shows the cancel flow on a toy fleet, with no database and no MQTT. **It is not the assignment** — the assignment wires real SQL and the front‑door POST into `serve`.

Create it in a scratch directory:

```bash
mkdir -p /tmp/opencode/l27toy
cd /tmp/opencode/l27toy
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

// toy boards (same shape as serve, but in-memory only)
var tasks = new FleetCore.TaskBoard();
var board = new FleetCore.WmsOrderBoard(tasks);
var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

// submit one order and task, same pattern as lesson 21
var submit = board.Submit(
    new FleetCore.SubmitOrderRequest("ORD-1", FleetCore.WmsOrderKinds.Store, "PAL-1", "A01-L07"), now);
var task = submit.CreatedTask!;
task.Assign("SH-01", now);
task.Start(now);

// --- exercise cancel ---
// 1. task.Cancel now transitions state
task.Cancel(now);

// 2. ladder sync advances order state
var sync = FleetCore.WmsOrderLadder.SyncFromTask(task.TaskId);
// outcome is Advanced; order board record is updated with State = "cancelled"

// 3. verify the order state
board.TryGet("ORD-1", out var cancelledOrder);
Console.WriteLine($"order state after cancel: {cancelledOrder.State}");
// expected output: order state after cancel: cancelled

Console.WriteLine("toy: cancel flow exercised (see source for full shapes)");

Run it:

```bash
cd /tmp/opencode/l27toy
dotnet run
```

Not yet verified: the toy requires the full `WmsOrderLadder` source to compile; the output above is the expected shape once the ladder rule is wired, but it has not been measured in this session.

Read it line by line:
- The toy submits one order and task, same pattern as lesson 21.
- `task.Cancel(now)` moves the task to `TaskState.Cancelled`.
- `WmsOrderLadder.SyncFromTask(task.TaskId)` maps `TaskState.Cancelled` → `WmsOrderStates.Cancelled`; outcome is `Advanced`.
- `board.TryGet("ORD-1", ...)` reads the order back; its `State` is now `"cancelled"`.
- The expected console output shows the order state changed from whatever intake stored (`received`) to `cancelled`.

**Note:** The JSON shapes and ladder outcomes match the real fleet manager. The toy omits the Kestrel server and the front‑door POST handler to keep the example self‑contained.

---

## Say it out loud

Answer these out loud, with no notes.

1. The WMS POSTs to `/wms/orders/ORD-1/cancel`. The fleet manager looks up the order on the board and finds the associated task. What two things does it do next?
2. After `task.Cancel(now)`, the ladder sync maps `TaskState.Cancelled` to `WmsOrderStates.Cancelled`. What does the ladder outcome `Advanced` mean, and what write follows?
3. Why can't a task that is already `Done` be cancelled? What does the guard prevent?
4. An order was `in_progress` and its task is now `Cancelled`. The WMS later reads the order state back. What does it see, and why?
5. If the WMS cancels an order but the ladder outcome is `NoOrder` (no order behind the task), what happens to the order state on the board?

---

## The assignment

Take the worked example and make it run **for real**, inside `serve`, with the front‑door POST and the ladder sync.

### The files you will change or add

| File | New? | What changes |
|---|---|---|
| `src/FleetCore/TaskState.cs` | edit | Add `Cancelled` to the `TaskState` enum; update `TaskStateWire.ToWireName` and `FromWireName` so the wire name is `"cancelled"`. |
| `src/FleetCore/FleetTask.cs` | edit | Add a `Cancel(DateTime now)` method that transitions the task to `TaskState.Cancelled`, with the same clock‑guard as `Complete` and `Fail`. |
| `src/FleetCore/WmsOrderLadder.cs` | verify | Confirm `SyncFromTask` already maps `TaskState.Cancelled → WmsOrderStates.Cancelled` with outcome `Advanced`. No change needed if the mapping exists; add the mapping if it does not. |
| `src/FleetManager/Program.cs` | edit | In `RunServeAsync`: add `webApp.MapPost(WmsRoutes.OrderCancel("{orderId}"), ...)` after the existing order GET endpoints. The handler looks up the order, calls `task.Cancel(now)`, calls `WmsOrderLadder.SyncFromTask`, and updates the order state if the outcome is `Advanced`. |
| `src/FleetManager.Tests/DashboardIntegrationTests.cs` (or new file) | **new** | Integration test: start serve, POST cancel on an order, call the order GET endpoint, assert the order state is `cancelled`. |
| `src/FleetManager.Tests/OperatorCommandTests.cs` (or new file) | **new** | Unit test: publish a cancel message on the `command` topic and assert `FleetTask.Cancel` was called; or a mock that checks the task state changed. |

### Requirements

**R1 — Cancel POST endpoint is at `/wms/orders/{orderId}/cancel`.** `webApp.MapPost(WmsRoutes.OrderCancel("{orderId}"), ...)` — returns 200 with `{ orderId, newState }` on success, 404 if the order is not found. The route uses `WmsRoutes` constants.

**R2 — `TaskState.Cancelled` is a new enum member.** Added to `TaskState` in `src/FleetCore/TaskState.cs`; wire names `"cancelled"` in `TaskStateWire.ToWireName` and `FromWireName`.

**R3 — `FleetTask.Cancel(DateTime now)` exists.** Throws `TaskRejectedException` if the task is already `Done`, `Failed`, or `Cancelled`. Transitions the task to `TaskState.Cancelled` and records the event timestamp.

**R4 — `WmsOrderLadder.SyncFromTask` maps `Cancelled → Cancelled`.** The ladder outcome is `Advanced`; the order board record is updated with `State = "cancelled"`. If the outcome is `NoOrder`, the order state stays unchanged.

**R5 — configuration.** No new configuration keys. The cancel route constant `WmsRoutes.OrderCancel` lives in `WmsRoutes.cs` (already present from lesson 20). No new appsettings values.

**R6 — the facts.** At least these new tests pass:
- `Cancel_endpoint_returns_200_and_updates_state` (integration): start serve, POST cancel on an existing order, call the order GET endpoint, assert the order state is `cancelled`.
- `Task_Cancel_transitions_state` (unit): create a `FleetTask`, call `Cancel`, assert `State == TaskState.Cancelled`.
- `Ladder_sync_on_cancel_advances_order` (unit): call `WmsOrderLadder.SyncFromTask` with a `Cancelled` task, assert outcome is `Advanced` and the order state maps to `cancelled`.

**R7 — what you must not touch.** `src/FleetCore/FleetTask.cs` (existing methods only, do not change `Assign`, `Start`, `Complete`, `Fail`, `Requeue` logic), `src/FleetCore/TaskState.cs` (only add `Cancelled` and update wire methods), `src/Contracts/`, `docs/contract/`. The contract does not move: `ContractVersion.Current` stays `4`.

### Acceptance criteria

```bash
BASE=$(git rev-parse HEAD)
cd src && dotnet build --no-incremental
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# integration (with the database up):
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"
test -f src/FleetCore/TaskState.cs
grep -q "Cancelled" src/FleetCore/TaskState.cs
test -f src/FleetCore/FleetTask.cs
grep -q "public void Cancel" src/FleetCore/FleetTask.cs
test -f src/FleetManager/Program.cs
grep -q "WmsRoutes.OrderCancel" src/FleetManager/Program.cs
grep -q "SyncFromTask" src/FleetCore/WmsOrderLadder.cs
```

### Mutation check

| # | The change | Must go red |
|---|---|---|
| a | Omit `Cancelled` from the `TaskState` enum | `Task_Cancel_transitions_state` (the method does not compile) |
| b | Remove the `EnsureInOrder` guard from `FleetTask.Cancel` | `Cancel_endpoint_returns_200_and_updates_state` (a clock‑go‑backwards scenario compiles but produces a test failure) |
| c | Use `TaskState.Done` instead of `TaskState.Cancelled` in the ladder sync | `Ladder_sync_on_cancel_advances_order` (the order state advances to `done` instead of `cancelled`, test asserts `cancelled`) |

### Stretch goal

**Measure the cancel round‑trip latency.** Start serve, submit an order, POST cancel, call the order GET endpoint, and time round‑trip from HTTP request to JSON response. Write the measurement down. Compare it to the order‑intake round‑trip from lesson 20 and the status‑push round‑trip from lesson 23.

### What comes next

- **The mock‑WMS reliability slice.** A WMS that answers `500`, a WMS that drops the connection, a WMS that is slow.
- **The traffic‑control‑under‑dashboard slice.** The operator can pause traffic via the dashboard, and the tick respects the pause.
- **M8 stretch.** OPC UA or Modbus TCP gateway, Python scenario runner, or load test.

---

## What I assumed about what you know

I assumed you are comfortable with: ASP.NET Core minimal APIs (`MapGet`, `MapPost`, `Results.Ok`, `Results.Problem`), `with` expressions on records, enums and switch expressions in C#, and the existing `WmsOrderLadder.SyncFromTask` rule from record 30. If any of those is new, say so before building and I will walk through it.