# Review 27: Order cancel slice (M6) — re-review after fixes (3)

**Verdict:** pass

**Reviewer:** beam-free (reviewer)
**Date:** 2026-10-26
**Build command:** `cd src && dotnet build --no-incremental`
**Test command:** `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` (plus `Category=Integration` with Docker up)

## Build and test result

- `dotnet build --no-incremental`: **PASS**, 0 warnings, 0 errors.
- `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"`: **PASS**, 219 tests, 0 failed (FleetCore 67, FleetManager 75, ShuttleCore 31, TrafficCore 6, Contracts 8, Messaging 10, MqttMessaging 3, MockWms 9, ShuttleSim 10).
- Targeted run `dotnet test ShuttleFleet.slnx -v m --filter "FullyQualifiedName~Cancel"`: **PASS**, 7 passed, 0 failed (FleetCore 4, FleetManager 2, MockWms 1).
- Integration run with Docker up (`Category=Integration`): **PASS**, 4 passed, 0 failed.
- Docker was brought down after the run; no broker/container is left running.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | `cd src && dotnet build --no-incremental` | PASS | 0 warnings, 0 errors |
| 2 | `cd src && dotnet test ... --filter "Category!=Integration"` | PASS | 219 passed, 0 failed |
| 3 | Integration suite with DB/broker up | PASS | 4 passed, 0 failed |
| 4 | `test -f src/FleetCore/TaskState.cs` + `grep -q "Cancelled"` | PASS | `Cancelled` member present |
| 5 | `test -f src/FleetCore/FleetTask.cs` + `grep -q "public void Cancel"` | PASS | method present |
| 6 | `test -f src/FleetManager/Program.cs` + `grep -q "WmsRoutes.OrderCancel"` | PASS | endpoint wired via `WmsOrderCancelEndpoint.Handle` |
| 7 | `grep -q "SyncFromTask" src/FleetCore/WmsOrderBoard.cs` | PASS | `SyncFromTask` on the board |
| 8 | R6 `Cancel_endpoint_returns_200_and_updates_state` | PASS | `WmsEndpointTests.cs`, TestHost, asserts 200 + state `cancelled` |
| 9 | R6 `Task_Cancel_transitions_state` | PASS | `TaskLifecycleTests.cs`, asserts `State == TaskState.Cancelled` |
| 10 | R6 `Ladder_sync_on_cancel_advances_order` | PASS | `WmsOrderLadderTests.cs`, asserts `Advanced` + state `cancelled` |
| 11 | R6 `Cancel_endpoint_is_idempotent_for_repeated_requests` | PASS | `WmsEndpointTests.cs`, two POSTs both 200, state `cancelled` |
| 12 | Contract untouched (`src/Contracts/`, `docs/contract/`) | PASS | `ContractVersion.Current` = 4; no diff |
| 13 | `Paused` arm in ladder | PASS | `WmsOrderLadder.ForTask(TaskState.Paused) == "in_progress"`; test asserts it |

## Prior findings: verification

- **[blocker, process] Owner sign-off token format** — **resolved.** `docs/builds/27-order-cancel-slice-report.md` ends with `Owner sign-off: 2026-10-26, explained and verified`.
- **[should-fix] Endpoint test duplicates the handler instead of exercising `Program.cs`** — **resolved.** The cancel logic now lives in `WmsOrderCancelEndpoint.Handle`, and both `Program.cs` (`webApp.MapPost(WmsRoutes.OrderCancel("{orderId}"), (string orderId) => WmsOrderCancelEndpoint.Handle(...))`) and the two `WmsEndpointTests` register the same delegate. There is no longer a second copy of the handler to drift. The test does not boot `RunServeAsync`, but because `Program.cs` is now a one-line delegate to the shared method, the handler under test is the handler in production.
- **[should-fix] Idempotency catch is too broad** — **resolved.** The `catch (TaskRejectedException) when (...)` is gone. `WmsOrderCancelEndpoint.Handle` checks `task.State == TaskState.Cancelled` *before* calling `task.Cancel`, so an already-cancelled task never enters `Cancel` and its `EnsureInOrder` guard is not invoked on the retry path. No exception is swallowed.
- **[consider] No repeated-cancel test** — **resolved.** `Cancel_endpoint_is_idempotent_for_repeated_requests` now exists and passes.
- **[consider] Paused arm** — **resolved.** `TaskState.Paused => WmsOrderStates.InProgress` is present and asserted.
- **[consider] Minor style/clarity** — **partly resolved.** `WmsRoutes` now marks the legacy names (`WmsOrderCancelBase`, `WmsOrderCancel`) `[Obsolete]` pointing at the canonical ones. `WmsOrderLadder.cs` still has no trailing newline (minor).

## Top issues (max 5), ranked

1. **[consider] The repeated-cancel path neither checks clock ordering nor notifies the WMS.** In `WmsOrderCancelEndpoint.Handle`, the already-cancelled branch skips `task.Cancel` (so `EnsureInOrder` never runs) and calls `board.SyncFromTask` directly, so a retry carrying an earlier `now` is silently accepted and the outbox is never marked. No state corruption results — `SyncFromTask` is rank-based and idempotent, and `Cancelled` is the top rung — but the event timestamp is not validated and a WMS that relies on the status push may never receive the `cancelled` word. *Why it matters in a real shuttle system: a cancelled order that the WMS still shows as active is how an operator dispatches work the fleet has already stopped, and a retry with a skewed clock is exactly the kind of fault a fleet system should at least log.* Suggested fix: on the already-cancelled branch, run the same `EnsureInOrder` check against the task's last event time (or expose it), and route the sync through `OrderSync` so the outbox is marked pending on `Advanced`.

2. **[consider] `WmsOrderCancelEndpoint` couples order id to task id implicitly.** It does `taskBoard.TryGet(order.OrderId, ...)`, which only works because `WmsOrderBoard.CreateTask` sets `TaskId = order.OrderId`. A future change to task-id generation would turn every cancel into a 404 with no compile-time signal. *Why it matters: a cancel that silently 404s leaves the task running while the WMS thinks it cancelled — the two state systems drift, which is the exact failure this slice exists to prevent.* Suggested fix: carry a `TaskId` on `WmsOrder` (or add a lookup on the board) so the link is explicit.

3. **[consider] `WmsRoutes` still has two "canonical" names for one path.** `OrderCancelBase` (a constant, `/wms/orders/cancel`, which is not actually a prefix of the real `/wms/orders/{id}/cancel`) and `OrderCancel(orderId)` (the method that builds the real path) are both presented as canonical, and the obsolete aliases point at different ones. *Why it matters: support effort — the next person to add a route has to guess which name to use, and a wrong guess changes the URL the WMS calls.* Suggested fix: pick one canonical name, document it in the decision record, and make the others thin aliases with a single obsolete message.

4. **[consider] `WmsOrderLadder.cs` has no trailing newline**, and `WmsOrderCancelEndpoint.Handle` does not guard against `SyncFromTask` throwing `OrderRejectedException`/`ArgumentOutOfRangeException` for an unknown order state (returns an unhandled 500 instead of a 409). Both are minor, but the latter is a latent 500 on bad data. *Why it matters: operator trust — an unexpected 500 on a cancel looks like the fleet manager is down even when it is just confused.*

## What is solid

- The handler is now a single source of truth: `Program.cs` delegates to `WmsOrderCancelEndpoint.Handle`, and both cancel tests call the same method, so the test can no longer pass while production differs.
- The idempotency path is explicit and exception-free: `task.State == TaskState.Cancelled` is checked first, `Cancel` is never called on an already-cancelled task, and the stored state is read back through `TryGet` (single writer, no redundant write).
- `FleetTask.Cancel` keeps the established guard pattern (`EnsureInOrder`, terminal-state rejection, `_lastEventAt = now`) and does not touch `Assign`, `Start`, `Complete`, `Fail`, or `Requeue`.
- The ladder mapping is correct and minimal: `ForTask(TaskState.Cancelled) == WmsOrderStates.Cancelled`, rank 3 above `done`/`failed` at 2, and the new `Paused` arm maps to `in_progress`.
- Restored cancelled tasks remain in the outbox re-push set in `Program.cs`, so a restart does not silently drop a cancelled order's status.
- `src/Contracts/` and `docs/contract/` are untouched; `ContractVersion.Current` stays 4. Build is clean (0 warnings, 0 errors).

## Explain-it check

An interviewer for this codebase would most likely ask:

1. **"`WmsOrderCancelEndpoint.Handle` checks `task.State == TaskState.Cancelled` before calling `task.Cancel`. Why check state instead of catching `TaskRejectedException`, and what ordering guarantee is lost on the retry path that the first cancel had?"** (See `src/FleetManager/WmsOrderCancelEndpoint.cs`, the already-cancelled branch.)

2. **"`Program.cs` is now `webApp.MapPost(WmsRoutes.OrderCancel("{orderId}"), (string orderId) => WmsOrderCancelEndpoint.Handle(...))`. The two endpoint tests register the same delegate on a TestHost app. Does that test exercise `Program.cs`, and if a regression appeared only in `RunServeAsync`'s wiring, would any test fail?"** (See `src/FleetManager/Program.cs` and `src/FleetManager.Tests/WmsEndpointTests.cs`.)

3. **"`board.SyncFromTask` is the single writer and the handler only reads back through `TryGet`. Why is the redundant `board.TryUpdate(... State = "cancelled")` from the lesson snippet wrong, and what would a second write risk?"** (See `src/FleetCore/WmsOrderBoard.cs`, `SyncFromTask`, and `src/FleetManager/WmsOrderCancelEndpoint.cs`.)

Lines and blocks that look unclear, copied, or unexplained, to study before the next lesson:

- `src/FleetManager/WmsOrderCancelEndpoint.cs` (both branches) — explain why the already-cancelled branch still calls `SyncFromTask`, and what `finalState` fallback hides.
- `src/FleetManager/WmsOrderCancelEndpoint.cs` line `taskBoard.TryGet(order.OrderId, ...)` — explain the implicit order-id == task-id assumption.
- `src/FleetCore/WmsRoutes.cs` — `OrderCancelBase` constant vs `OrderCancel` method; explain which is canonical and why the others exist.
- `src/FleetCore/WmsOrderLadder.cs` — missing trailing newline; explain the `Paused => InProgress` choice and why `Cancelled` is rank 3.
- `src/FleetManager.Tests/WmsEndpointTests.cs` — duplicated TestHost setup across two tests; explain what is and is not covered.
- `docs/builds/27-order-cancel-slice-report.md` — stale file list (does not mention `WmsOrderCancelEndpoint.cs`, the csproj change, or the repeated-cancel test); confirm the sign-off line is the one `AGENTS.md` requires.

## Revisit list

- Single source of truth for handlers: extract to a method both production and tests call, so a test cannot pass against a copy.
- Idempotency without exceptions: check state before mutating; distinguish "already in target state" from a genuine guard failure (clock ordering) and surface the latter.
- Outbox integration: derived state changes that the WMS must see should go through `OrderSync`, not `SyncFromTask` alone, so the outbox is marked.
- Implicit identity coupling: when two boards share a key, make the link explicit rather than relying on construction-time equality.
- Ladder rank ordering: terminal words outrank intermediate words; refusal makes "no backwards moves" structural.
- Owner review gate: the sign-off token format is part of the process, not optional (satisfied).

## End-product check

This slice delivers the WMS cancel path M6 requires: a cancel POST reaches the front door through a shared handler, the task moves to `Cancelled`, and the order word follows through the ladder, with no contract version change and no new transport. The core state-machine and ladder logic is sound and covered by the required tests plus the new repeated-cancel test, and the handler is now a single writer with a read-back final state. The remaining items are verification-depth and observability concerns (clock ordering on retry, implicit order/task id coupling, outbox notification, route naming), not correctness blockers, and none of them corrupts fleet state. M6's other slices (order intake, ladder, status push, execution, persistence, dashboard diagnostics, mock WMS reliability) are already reviewed passing, so with this pass M6 may be marked done in the README milestone table. The reviewer does not mark it done on this verdict; the owner commits and updates the milestone state.

## Lesson defects (for the professor)

- The acceptance criterion `grep -q "SyncFromTask" src/FleetCore/WmsOrderLadder.cs` still cannot pass on this codebase: record 30 placed `SyncFromTask` on `WmsOrderBoard` and kept `WmsOrderLadder` as the pure `ForTask`/`Rank` rule. The implementer correctly followed the architecture. Suggest changing the criterion to `grep -q "SyncFromTask" src/FleetCore/WmsOrderBoard.cs` or to `grep -q "TaskState.Cancelled => WmsOrderStates.Cancelled" src/FleetCore/WmsOrderLadder.cs`.
- The lecture snippet for the cancel handler still shows `board.TryUpdate(... State = "cancelled")` after `SyncFromTask`, which teaches the redundant write the code now correctly avoids. Clarify in the lesson that `SyncFromTask` is the writer and the handler only reads back.
- The mutation check (b) ("remove the `EnsureInOrder` guard") is not guarded by any test: `Cancel_endpoint_returns_200_and_updates_state` cancels once with a fresh `DateTime.UtcNow` and never exercises a backwards clock, and the new repeated-cancel test also uses fresh timestamps. Re-point the mutation at a clock-backwards scenario or add such a test.
- R6 test names are present and passing, including the new repeated-cancel test; the remaining gap is fidelity (full `serve` pipeline), not presence.
- The `Owner sign-off: <date>, explained and verified` token requirement in `AGENTS.md` is met by the report; no process item remains open.
- The lesson lists three concepts and stays within the limit; no new concept was introduced by the re-review work.