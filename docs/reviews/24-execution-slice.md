# Review: Lesson 24 — The execution slice

**Verdict:** pass

**Build/test evidence:** `cd src && dotnet build --no-incremental` → 0 warnings, 0 errors. `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → 8 projects, `Failed: 0, Passed: 186, Skipped: 0, Total: 186` (FleetCore 52, FleetManager 66). Contract untouched: `ContractVersion.Current = 4`, `git diff --stat` on `src/Contracts` and `docs/contract` prints nothing.

## Acceptance criteria

- [x] R1 `FleetCore/OrderSync.cs` — sealed, two null-checked deps, `Note` calls `SyncFromTask` then `NoteSync`, returns outcome. No I/O.
- [x] R2 `TaskDispatcher` third required arg `_orderSync`; `_orderSync.Note(taskId)` after all five moves and before `SaveAsync` — count is 5.
- [x] R3 `[NotNullWhen(true)]` on `TryGet`; only that change in the file.
- [x] R4 `InMemoryFleetTaskRepository` moved to `src/FleetManager/`, namespace `FleetManager`; test copy deleted; tests still resolve it.
- [x] R5 `ShuttleRegistry` — ordinal dictionary, one lock, `Record`/`IsIdle`/`PositionOf`/`FreeShuttles`; unknown shuttle is not idle and not free; free list in id order.
- [x] R6 `TaskExecutionLoop` — constructor checks, `HandleStatusAsync` with envelope checks, `TickAsync` (finish then give), `RunAsync` with `PeriodicTimer` and per-tick catch.
- [x] R7 `Program.RunServeAsync` — status handler on `AllStatuses`, ack handler on `AllAcks`, both before the tick starts; `RetryPolicy` built from `AckTimeoutMs`/`MaxAttempts`/`BackoffMs`; move command built with `MessageEnvelopes.Create(MessageTypes.MoveCommand, …)` and `MoveCommandMessage`.
- [x] R8 `WmsEndpoint.Parse` refuses non-http(s) before the bus connects; `WmsStatusPusher.SendPendingAsync` catch-all puts the word back and logs; `Program` awaits all background tasks under shutdown-cancellation plus catch-all (4 of them); tick never dies.
- [x] R9 `TaskExecutionIntervalMs` in `appsettings.json` and validated `< 1` in `Program`.
- [x] R10 All ten named facts present and green.
- [x] R11 No forbidden file moved; `ReliableCommandSender` gained only `: ICommandSender`.

## Top issues (max 5), ranked

1. **consider — `WmsStatusPusher.RunAsync` has no per-tick catch.** `SendPendingAsync` now has a per-order catch-all, so a single bad order cannot escape. But if `Claim()` or the loop itself throws, the push loop dies silently and the WMS stops hearing words. The tick's `RunAsync` does catch per tick (G4); the pusher should match. Low risk today because the catch-all covers the measured shapes, but it is the one loop that is not yet "never dies".

2. **consider — `GiveWorkToFreeShuttlesAsync` stops handing out work as soon as the queue is empty.** That is correct for this slice (one queue, drained in id order), but it means a free shuttle that appears later in the same tick does not get a task if an earlier shuttle already emptied the queue. Fine for one task per tick beat; note it if the traffic slice raises the number of free shuttles per beat.

3. **consider — `_working` is a plain `Dictionary` guarded only by single-threaded timer access.** Today only the timer thread touches it, so it is safe. If the stretch goal (one worker per shuttle) lands, this dictionary becomes shared state and needs a lock or message passing. Worth a comment or a lock before that work starts, not now.

## What is solid

- The transition line is in exactly one place and is a required constructor argument, so no dispatcher can be built without it; the compiler, not a reviewer, enforces it.
- `OrderSync` is pure and lives in `FleetCore`; the dispatcher talks to one thing ("tell me what happened to this task") instead of two.
- "Newest truth wins" is pinned by a fact that parses the POST body with `JsonDocument`, not by a log line.
- The picture treats an unknown shuttle as not free, so the tick never gives work to a shuttle it has never heard from.
- The failure policy has three layers and each one is tested or at least reachable by a named fact.

## Explain-it check

1. You put the transition line inside the dispatcher, after every move, instead of in the tick. Why? What breaks in a month if it lives in the tick?
2. A task is assigned, starts, and finishes before the sender pushes anything. How many words does the WMS hear, and what are they? Why is that the right answer rather than three?
3. The tick waits for the shuttle's acknowledgement before it does anything else. A shuttle stops answering. What happens to the rest of the fleet this beat, and what would you change in a real warehouse?

Unclear/copied blocks to study:
- `FinishFinishedWorkAsync` copies `_working` with `ToArray()` and then removes from `_working` while walking the copy. The copy is the trick; make sure you can say why the removal is safe.
- `GiveWorkToFreeShuttlesAsync` returns early on a null task. Trace which shuttles in the loop have already been handled when that happens.

## Revisit list

- Per-tick vs per-order exception handling (pusher loop hardening).
- Thread-safety of `_working` before the one-worker-per-shuttle stretch goal.
- Traffic control: the tick still gives work without consulting the occupancy map (named, deferred to the traffic slice).

## End-product check

This slice is the first time an order actually becomes a moving pallet and a word comes back to the WMS, which is the first half of M6's done-when. It leaves the pair in memory (restart forgets everything, as it already did), does not reserve lanes, and does not handle cancellation, so the persistence, traffic, and cancel slices still owe work. The risk it introduces is the honest one written in decision 33 §11: two shuttles can be sent to the same lane. That is named, not hidden, and cannot matter with one shuttle.

## README status

M6 remains `in progress` (this slice is one of several; persistence, cancel, mock-WMS, and traffic slices are still owed). No table change.
filePath">
/home/jamie/shuttle-sim/docs/reviews/24-execution-slice.md