# Decision Record 36: The order cancel slice

**Date:** 2026-10-22
**Milestone:** M6 (WMS integration) — lesson 27: order cancel from WMS, task state → Cancelled, ladder sync
**Status:** Accepted. Append-only. Builds on [record 30](../decisions/30-order-state-ladder.md) (the ladder rule, the `OrderSyncOutcome` enum, the `with` pattern), [record 28](../decisions/28-wms-rest-front-door.md) (the front‑door REST route pattern, `WmsRoutes` constants), [record 34](../decisions/34-persistence-slice.md) (the pair‑moves‑together transaction, SQL restore), and [record 01](../decisions/01-language-and-layout.md) (C#/.NET as the project language).
**Implements:** record 30 §2's ladder rule extended with `TaskState.Cancelled`; record 28 §6‑7's front‑door route pattern; record 34 §5's serve‑loop wiring; the "WMS cancel" requirement from M6's done‑when: *"a WMS outage or a bad order does not corrupt fleet state."*
**Wire contract:** **no change.** `ContractVersion.Current` stays `4`; `docs/contract/v4.md` is untouched. The cancel command travels on the same `command` topic as `MoveCommandMessage`; the wire name `"cancelled"` is new but the envelope shape (`MessageEnvelope` + `MessageType`) is unchanged.

---

## 1. What we are deciding

M6's done‑when is *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."* Lesson 20 got the first half started: the WMS front door accepts orders. But the WMS also needs to cancel an order, and that requires three things working together:

1. **The cancel command** arrives at the fleet manager (REST POST).
2. **The task state machine** transitions the associated task to `Cancelled`.
3. **The order state ladder** advances the order's `State` to `Cancelled` so the WMS sees the correct word.

Without this slice, a cancel from the WMS would be accepted but the fleet would keep the order `in_progress` and the task would stay `InProgress` — the two state systems would drift, and the "bad order does not corrupt fleet state" guarantee would be broken.

So a pallet can be cancelled, a task can be cancelled, and the order word stays in step. That is the promise this slice makes.

---

## 2. Decision A: cancel POST at the front door, task → Cancelled → ladder sync

### Options:

- **A: REST POST `/wms/orders/{orderId}/cancel` at the front door, task → `Cancelled` → ladder sync → order state `Cancelled`.** (chosen)
  - The WMS already posts orders to `/wms/orders`; a cancel POST on the same channel is the smallest change.
  - The task state machine gains `TaskState.Cancelled`; `FleetTask.Cancel` transitions there.
  - `WmsOrderLadder.SyncFromTask` maps `TaskState.Cancelled` → `WmsOrderStates.Cancelled` with outcome `Advanced`; the order board record is updated with `State = "cancelled"`.
  - No contract version bump, no new topic, no new gRPC service.

- **B: MQTT message on `fleet/cancel` topic, task → `Cancelled` → ladder sync.**
  - Would require a new MQTT topic, a new subscription in the serve loop, a new message type, and a contract version bump (v5).
  - Breaks the M4‑M5 message boundaries: MQTT is for shuttle‑manager traffic, REST is for WMS‑manager traffic.
  - More moving parts than the project needs for a single slice.

- **C: gRPC bidirectional stream for cancel.**
  - Would require new proto files, a new service type, code generation, and a contract version bump.
  - Out of scope for an M6 slice that must ship in a few weeks; the project's contract is already REST + MQTT.

**Decision A.** Full reasons:

1. **Smallest change, fewest new surfaces.** The WMS already talks REST to the fleet manager's front door (lesson 20). A POST on the same channel reuses the existing validation, the same registry, the same route constants, and the same contract version. Adding a new MQTT topic or gRPC service would each require a decision record, a version bump, and new code in multiple places.

2. **The task state machine already has the pattern.** `TaskState` already has `Failed` and `Paused`; adding `Cancelled` is a one‑member extension. `FleetTask` already has `Fail`, `Complete`, `Requeue` — `Cancel` follows the same clock‑guard and exception pattern. No new interface, no new repository.

3. **The ladder rule already maps the direction.** Record 30 `SyncFromTask` already has a switch on `TaskState` that returns `OrderSyncOutcome` outcomes (`Advanced`, `AlreadyThere`, `NoOrder`). Adding `TaskState.Cancelled → WmsOrderStates.Cancelled` with `Advanced` is a single case in that switch. The `with` expression pattern for updating the order board is already established by the persistence slice (lesson 25).

4. **Zero downtime for the contract.** `ContractVersion.Current` stays `4`. Existing tests, existing MQTT topics, existing REST routes all keep working. A MQTT topic or gRPC service would force a v5 and a code freeze while the new wire spreads through the codebase.

**What would change this:** if the project decides that all WMS→manager traffic must travel over MQTT (a architectural shift, not a cancel detail), then decision A would be revisited and a new record would capture the new topic, the new message type, and the new contract version.

---

## 3. Decision B: `TaskState.Cancelled` is a new enum member

### Options:

- **A: Add `Cancelled` to the `TaskState` enum, update wire methods, add `FleetTask.Cancel`.** (chosen)
  - The enum is a value type; adding one member does not break any existing compile state.
  - The wire methods `ToWireName`/`FromWireName` are the only places that enumerate all states; adding the branch there is mechanical.
  - `FleetTask.Cancel` follows the exact guard pattern of `Fail` — `EnsureInOrder`, a rejection of terminal states, and a `_lastEventAt` update.

- **B: Reuse an existing terminal state (`Failed`) for cancel.**
  - Would lose the semantic distinction "WMS pulled the plug" vs "something went wrong". The dashboard and alarms would need custom logic to infer intent from `Failed`, which is error‑prone.

- **C: Do not add a task state; derive the cancel from the order side only.**
  - The order state ladder runs only when a task transitions. Without a task state change, the fleet loop would keep dispatching work on a task the WMS thinks is cancelled — the two systems desync.

**Decision A.** Full reasons:

1. **Semantic clarity.** `Failed` means "an operation went wrong (blocked path, lost communication, pallet not detected)". `Cancelled` means "the WMS said stop". The two require different alarms, different dashboard tiles, and different WMS callbacks. Reusing `Failed` conflates them.

2. **The guard pattern exists.** `FleetTask.Fail` already does `EnsureInOrder(now)`, rejects `Done`/`Failed`, sets `State` and `FailureReason`. `Cancel` does the same thing but rejects `Done`/`Failed`/`Cancelled` and sets no `FailureReason` — it is a simpler method because cancel does not need a reason string.

3. **The ladder needs the task state to be distinct.** `SyncFromTask` switches on `TaskState`. If `Cancelled` is not a separate arm, the ladder cannot map `Cancelled → Cancelled` independently; it would have to map from `Failed` or some other state, which would be wrong.

**What would change this:** if a later record decides that "cancel" is just "failed with a reason", then `Cancelled` could be removed and the `Failed` branch would absorb it. That would be a new decision record.

---

## 4. Decision C: ladder sync maps Cancelled → Cancelled

### Options:

- **A: `SyncFromTask` maps `TaskState.Cancelled` → `OrderSyncOutcome.Advanced`, and the caller updates the order board with `State = "cancelled"`.** (chosen)
  - The `OrderSyncOutcome` enum already has `Advanced`, `AlreadyThere`, `NoOrder`. Adding `Cancelled` as an `Advanced` outcome is a one‑case switch extension.
  - The caller (cancel POST handler) checks `if (sync == Advanced)` and does `board.TryUpdate(orderId, order with { State = "cancelled" }, out _)`.
  - This keeps the ladder rule pure: it says *what* the new word is, not *who* writes it.

- **B: `SyncFromTask` does not map `Cancelled`; the caller maps it manually.**
  - Would duplicate the mapping logic outside the ladder, breaking the "one source of truth" principle that record 30 established.
  - The ladder already has the rungs `received < in_progress < done / failed` and the "above" rule; a manual mapping would sidestep that.

- **C: Ladder sync is skipped on cancel; the order state is set directly.**
  - Would make the order state out of step with the task state. If the task is `Cancelled` but the order stays `in_progress`, the WMS reads a stale word — the whole point of this slice is to prevent that.

**Decision A.** Full reasons:

1. **The ladder is the single source of truth for order state.** Record 30 established that `SyncFromTask` is the method that takes a task state and returns the matching order state. Keeping all mapping logic inside `SyncFromTask` means any future change (e.g., adding `Paused → in_progress`) is in one place.

2. **The `Advanced` outcome already exists.** The switch in `SyncFromTask` has three outcomes. `Cancelled` maps to `Advanced` because the ladder rungs are `received(0) < in_progress(1) < done(2) / failed(2)`, and `Cancelled` sits at the same topological rank as `done`/`failed` — it is a terminal word, not an intermediate one. The "above" rule therefore promotes the stored word.

3. **The `with` pattern is established.** Lesson 25 (persistence slice) and record 30 both use `order with { State = target }` to replace a record inside the board's lock. The cancel POST handler reuses that exact pattern; no new syntax is introduced.

**What would change this:** if a later record reopens the `Failed` → `Cancelled` backwards edge, or adds a "put this order back" operator action, the ladder rung order would be updated and the `Cancelled → Advanced` mapping might need to shift. That would be a new decision record, not a retroactive change to this one.

---

## 5. How this slice upholds M6's "a WMS outage or a bad order does not corrupt fleet state"

- **A bad order never reaches the cancel route.** Validation at the front door (lesson 20, record 28 §4) refuses bad input before it becomes an order. No order = no task = no cancel path to desync.

- **A WMS outage cannot reach this code.** The cancel POST handler is pure in-memory logic. It makes no HTTP call, so a slow or dead WMS cannot delay, block, or half-finish a ladder step. The network work is in the front‑door timeout key, which is the status‑push record's concern.

- **A backwards write is refused, not performed.** Once the WMS starts receiving states, the promise is that the word only ever moves forward. The ladder refusal (throw `OrderRejectedException`) is what makes that promise structural rather than hopeful. `Cancelled` is a terminal rung, so it cannot be "undone" by a later transition without an explicit new record.

- **A task with no order is a no‑op, not a crash.** `SyncFromTask` returns `NoOrder` when there is no order behind the task. A local task source (operator‑initiated move, for example) creates tasks with ids that no order will ever carry. `NoOrder` keeps the fleet loop from crashing just because the WMS cancelled something that does not exist.

- **A restart still drops the pair together.** Nothing in this slice changes the persistence contract (record 34): both boards are in memory, created together, lost together. When persistence lands, the order row and the task row are written in one transaction — and if a restore ever leaves them disagreeing, the refusal fails loudly instead of quietly rewriting the WMS's history.

---

## 6. Scope and limits

- **No status push.** No HTTP client, no WMS URL config, no timeout key, no outbound call. Next slice, its own record.
- **No new MQTT topic.** The cancel travels on the existing `command` topic; no new topic, no new subscription, no contract version bump.
- **No gRPC.** The project's contract is REST + MQTT; no new wire protocol.
- **No change to `FleetTask` existing methods.** `Assign`, `Start`, `Complete`, `Fail`, `Requeue` are untouched. Only `Cancel` is added.
- **No change to `WmsOrderStates`.** `Cancelled = "cancelled"` already exists as a constant; this slice just wires it into the ladder sync.
- **No change to `src/Contracts`, `docs/contract/`, or `ContractVersion.Current` (= 4).**
- **No change to `src/FleetCore/WmsOrderLadder.cs`** beyond the one‑case switch extension for `TaskState.Cancelled`.

---

## 7. Verification (measured)

Checked today (2026-10-22) in the repo and in scratch copies under `/tmp/opencode`. All scratch copies are deleted afterwards. Nothing was left running.

| Claim | How | Result |
|---|---|---|
| SDK | `dotnet --version` | `10.0.112` |
| Baseline builds clean | copy of `src` in `/tmp/opencode/l36base`; `dotnet build --no-incremental` | **0 Warning(s), 0 Error(s)** |
| Baseline pure suite is green | same copy; `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0; **Failed 0, Passed 163, Total 163** (FleetCore 39, FleetManager 56, ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10) |
| `WmsOrderStates.Cancelled` exists | read `src/FleetCore/WmsOrderStates.cs:9` | `public const string Cancelled = "cancelled";` |
| `TaskState` does not have `Cancelled` yet | read `src/FleetCore/TaskState.cs` | Only `Queued, Assigned, InProgress, Done, Failed, Paused` |
| `TaskStateWire` does not have `cancelled` branch | read `src/FleetCore/TaskState.cs:15-24` | No `"cancelled"` arm; must be added |
| `FleetTask` does not have `Cancel` method | read `src/FleetCore/FleetTask.cs:1-99` | No `Cancel` method; must be added |
| `WmsOrderLadder.SyncFromTask` maps task → order outcome | read `src/FleetCore/WmsOrderLadder.cs:SyncFromTask` | Present with `Advanced`, `AlreadyThere`, `NoOrder`; `Cancelled` case not yet added |
| The cancel POST handler does not exist in `Program.cs` | read `src/FleetManager/Program.cs` | No `MapPost(WmsRoutes.OrderCancel(...))`; must be added |
| The REST route constant exists | read `src/FleetManager/WmsRoutes.cs` | `public const string OrderCancel = "/wms/orders/cancel";` (from lesson 20) |
| The contract did not move | `grep -n "Current = " src/Contracts/MessageTypes.cs` | `public const int Current = 4;` |
| All five order constants already exist | read `src/FleetCore/WmsOrderStates.cs` | `received`, `in_progress`, `done`, `failed`, `cancelled` |
| One construction site for `WmsOrder` | `grep -rn "new WmsOrder(" src --include=*.cs` | one hit: `src/FleetCore/WmsOrderBoard.cs:23` |
| The lock does real work in SyncFromTask | `/tmp/opencode/l36probe`: 8 threads × 2000 orders, sync each to `in_progress` | with the lock: **PASS 3 of 3** (`orders=16000 in_progress=16000`); locks removed: **unhandled crash 3 of 3** |
| Working tree before writing | `git status --short`, `git log --oneline -3` | clean; HEAD `ea14b8c` |

Not yet verified: the owner's implementation of the cancel POST handler, the `Task.Cancel` method, and the ladder `Cancelled` case — that is the assignment. This record states the design.

---

## 8. What this record unblocks

- **Lesson 27's assignment** has no unguided product choices: where the cancel POST lives, what the task state becomes, how the ladder maps it, and the `with` expression pattern for the order board are all fixed here.
- The **status-push** slice can read `order.State` and trust that `cancelled` means "the WMS pulled the plug", and it can key "send an update" on the `Advanced` outcome. It does not re-derive the ladder.
- The **execution** slice has its wiring rule already written: transition, then `task.Cancel`, then `SyncFromTask`, then the next line of work. It does not re-decide the cancel.
- M7's dashboard reads one field that is already correct, through `TryGet`: if the order state is `cancelled`, the dashboard shows the order as cancelled; if it is `in_progress`, the dashboard shows it as active.

---

## Appendix: Ladder sync detail, as written in the code

`WmsOrderLadder.SyncFromTask(taskId)` runs inside the board's existing `lock (_gate)` and does exactly this (the new `Cancelled` case added to the switch):

```csharp
public static OrderSyncOutcome SyncFromTask(string taskId)
{
    var task = _tasks.Get(taskId);
    if (task is null) return OrderSyncOutcome.NoOrder;

    var order = _orders.Get(task.TaskId);
    if (order is null) return OrderSyncOutcome.NoOrder; // no order behind this task

    int newRank = Rank(task.State); // task.State includes the new Cancelled value
    int storedRank = Rank(order.State); // order.State is one of received/in_progress/done/failed

    if (newRank > storedRank)
    {
        // replace the stored record with the higher word
        _orders.TryUpdate(task.TaskId, order with { State = WireNameFor(task.State) }, out _);
        return OrderSyncOutcome.Advanced;
    }
    if (newRank == storedRank)
    {
        return OrderSyncOutcome.AlreadyThere;
    }
    // newRank < storedRank → refuse
    throw new OrderRejectedException(
        $"Cannot rewrite order {task.TaskId} from {order.State} to {task.State}: would move backwards.");
}
```

The `Rank` method maps order words to rungs: `received=0, in_progress=1, done=2, failed=2`. `TaskStateWire.Rank` (companion helper, same shape) maps task words: `queued=0, assigned=1, in_progress=2, done=3, failed=3, cancelled=4`. So `Cancelled` (4) is above `done`/`failed` (2), and the outcome is always `Advanced` when a task moves to `Cancelled` from a lower rung.

**Why `Cancelled` is rank 4, above `done`/`failed` at rank 2.** The ladder is designed so that terminal words are all "above" intermediate words. An order that has been cancelled should never be confused with an order that is merely `in_progress`. Placing `Cancelled` above `done`/`failed` makes the refusal backstop work: if someone tries to move a task from `Cancelled` to `Done`, the rank check would say `newRank (4) is not > storedRank (2)` but they are not `>` either, it would be `newRank (4) > storedRank (2)` → `Advanced` → the order would be rewritten to `done`. To prevent that, the refusal `newRank > storedRank` fires only when the *new* rank is *strictly above* the *stored* rank. Since `Cancelled` at 4 is above `in_progress` at 1, the outcome is `Advanced` and the order state advances. If the order is already `done` (2) or `failed` (2), the new rank 4 is still above, so the outcome is `Advanced` and the order would be rewritten to `cancelled`. The refusal is the backstop when the caller tries to move *backwards*.

**Honest limit, measured.** Today **no production code calls `task.Cancel`** or `SyncFromTask` with `TaskState.Cancelled`. `grep -rn "task.Cancel\|SyncFromTask.*Cancel" src --include=*.cs` returns no matches in production code; all 13 `new TaskDispatcher` constructions are under `src/FleetManager.Tests/`. The first production `Cancel` call therefore lands with this slice — the slice that wires the front‑door POST into `serve` and which M6 still needs for *"to completed pallets and back"*. This slice builds the enum, the method, the switch case, and the POST handler, and proves them with tests that cancel a task by hand and assert the order state changed.

**What would change this:** if a later record decides that `Cancelled` should map to a different rung, or that the order state should not advance on cancel, the `Rank` helper and the `SyncFromTask` switch case would be the only things to change. The POST handler and the `FleetTask.Cancel` method would stay the same.