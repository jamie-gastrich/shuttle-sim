# Build report: Lesson 24 — The execution slice

Owner sign-off: Jamie

## Summary

Built the execution slice: a pure `OrderSync` transition line in `FleetCore`, the line wired into all five `TaskDispatcher` moves, a `ShuttleRegistry` picture fed by shuttle status, a `TaskExecutionLoop` tick, an `ICommandSender` seam, a `WmsEndpoint` URL check, an in-memory repository moved into production, and the `serve` wiring with the failure policy. Ten new facts land; the pure suite goes from 176 to 186 with zero failures.

## Files

New:
- `src/FleetCore/OrderSync.cs` — pure transition line.
- `src/FleetManager/InMemoryFleetTaskRepository.cs` — moved from the test project, namespace `FleetManager`.
- `src/FleetManager/ShuttleRegistry.cs` — the picture.
- `src/FleetManager/ICommandSender.cs` — the seam.
- `src/FleetManager/TaskExecutionLoop.cs` — the tick.
- `src/FleetManager/WmsEndpoint.cs` — WMS URL check.
- `src/FleetCore.Tests/OrderSyncTests.cs` — facts 1–3.
- `src/FleetManager.Tests/ShuttleRegistryTests.cs` — fact 6.
- `src/FleetManager.Tests/TaskExecutionLoopTests.cs` — facts 7–8.
- `src/FleetManager.Tests/ExecutionSliceTests.cs` — facts 4–5.
- `src/FleetManager.Tests/WmsEndpointTests.cs` — fact 10.

Edited:
- `src/FleetCore/WmsOrderBoard.cs` — `[NotNullWhen(true)]` on `TryGet`.
- `src/FleetManager/TaskDispatcher.cs` — required `OrderSync` argument, line after all five moves.
- `src/FleetManager/WmsStatusPusher.cs` — catch-all that puts the word back.
- `src/FleetManager/ReliableCommandSender.cs` — `: ICommandSender`.
- `src/FleetManager/Program.cs` — wiring, new key, catch-alls.
- `src/FleetManager/appsettings.json` — `TaskExecutionIntervalMs`.
- `src/FleetManager.Tests/TaskDispatcherTests.cs`, `EndToEndTests.cs`, `RunLoopStressTests.cs`, `RunLoopReassignmentTests.cs` — new constructor argument.
- `src/FleetManager.Tests/WmsStatusPusherTests.cs` — fact 9.

Deleted:
- `src/FleetManager.Tests/InMemoryFleetTaskRepository.cs` — moved to production.

## How to run it

```
cd src
dotnet build --no-incremental
dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
```

## Verification

1. Clean build, no warnings:
   - Command: `cd src && dotnet build --no-incremental`
   - Output: `0 Warning(s), 0 Error(s)`.

2. Pure suite green (baseline 176 → 186):
   - Command: `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"`
   - Output: `Failed: 0, Passed: 186, Skipped: 0, Total: 186`.

3. Transition line pure, only place the sync is called:
   - `test -f src/FleetCore/OrderSync.cs` (pass); `grep -q "SyncFromTask"` (pass); `grep -q "NoteSync"` (pass); no `HttpClient|HttpRequest|IMessageBus|MqttMessaging|Console.` (pass); `grep -c "_orderSync.Note" src/FleetManager/TaskDispatcher.cs` → `5`.

4. Dispatcher cannot be built without the line:
   - `grep -q "OrderSync" src/FleetManager/TaskDispatcher.cs` (pass); no `OrderSync orderSync = null` or `OrderSync?` (pass).

5. Nullability contract landed, nothing else moved:
   - `grep -q "NotNullWhen" src/FleetCore/WmsOrderBoard.cs` (pass); `git diff --stat` → 4 lines (using + attribute).

6. The picture:
   - `test -f src/FleetManager/ShuttleRegistry.cs` (pass); `grep -q "lock ("` (pass); `grep -q "FreeShuttles"` (pass).

7. The tick:
   - `test -f src/FleetManager/TaskExecutionLoop.cs` (pass); has `PeriodicTimer`, `CompleteAsync`, `AssignNextAsync`, `StartAsync`, `RequeueAsync`; `test -f src/FleetManager/ICommandSender.cs` (pass); `grep -q "ICommandSender"` (pass).

8. Picture fed and acks heard before the tick starts:
   - `grep -q "AllStatuses"`, `AllAcks`, `HandleStatusAsync` in `Program.cs` (pass).

9. Failure policy:
   - `test -f src/FleetManager/WmsEndpoint.cs` (pass); `grep -q "Uri.UriSchemeHttp"` and `Uri.UriSchemeHttps` (pass); `grep -q "WmsEndpoint" Program.cs` (pass); `grep -c "catch (Exception"` in `WmsStatusPusher.cs` → 1; in `Program.cs` → 4.

10. Configuration:
    - `grep -q "TaskExecutionIntervalMs" Program.cs` (pass); `grep -q '"TaskExecutionIntervalMs"' appsettings.json` (pass).

11. Contract did not move:
    - `grep -q "Current = 4" src/Contracts/MessageTypes.cs` (pass); `git diff --stat "$BASE" -- src/Contracts docs/contract` prints nothing.

12. Reviewed types did not move:
    - `git diff --stat "$BASE"` on the listed files prints nothing.

12b. Sender gained the interface name and nothing else:
    - `grep -q ": ICommandSender"` (pass); diff → 2 lines (1 insertion, 1 deletion).

13. Ten facts exist: all ten names found under `src/FleetCore.Tests` and `src/FleetManager.Tests`.

Mutation checks (run in scratch copies under `/tmp/opencode`, then removed):
- Deleting `_orderSync.Note(taskId)` from `CompleteAsync` makes `Dispatching_a_task_moves_the_order_and_owes_a_word` fail.
- Deleting it from `StartAsync` makes `The_newest_word_wins_the_wms_hears_done_not_in_progress` fail.
- Dropping the position check in the tick's finish step makes `The_tick_completes_a_task_when_the_shuttle_is_idle_at_the_lane` fail.

## Choices made

- Kept `TaskDispatcher` as a primary constructor (no body, no null checks) to match the house shape in R2; the required argument has no default.
- `WmsStatusPusher` catch-all uses `MarkPending(orderId)` with the claimed id, matching the existing per-order variable in that scope.
- `ShuttleRegistry.FreeShuttles()` returns a fresh `List<string>`; `IsIdle` compares against `ShuttleState.Idle.ToWireName()`.

## Study first

1. `src/FleetCore/OrderSync.cs` — the whole line, five lines.
2. `src/FleetManager/TaskDispatcher.cs:17-53` — where the line sits relative to each move and the save.
3. `src/FleetManager/TaskExecutionLoop.cs` `GiveWorkToFreeShuttlesAsync` — finish, give, start-or-give-back.
4. `src/FleetManager/Program.cs:197-208` — the wiring order (subscriptions before the tick starts).
5. `src/FleetManager/WmsStatusPusher.cs:58-62` — the catch-all that keeps the word.

## Outside scope

- Persistence of the order/task pair (persistence slice).
- Cancel slice, mock-WMS slice, traffic slice, dashboard slice.
- The `.opencode/agents/*.md` changes present in the working tree belong to the role update, not this lesson.

## Stopped early

None.
filePath">
/home/jamie/shuttle-sim/docs/builds/24-execution-slice-report.md