# Review 30: Shared pause state

**Verdict:** pass

## Acceptance criteria checklist

| # | Criterion | Evidence |
|---|-----------|----------|
| 1 | `class PauseState` exists in `src/FleetCore/PauseState.cs` | Present, 50 lines, thread-safe with `lock` |
| 2 | `lock` appears in `PauseState.cs` | Line 11, 17, 23, 29, 39, 48 — every method locks `_gate` |
| 3 | `PauseState` referenced in `src/FleetManager/RunLoopGate.cs` | Field `_pauseState`, two constructors (3-arg creates own, 4-arg injects), all pause ops delegate |
| 4 | `PauseState` referenced in `src/FleetManager/Program.cs` | `var pauseState = new PauseState();` at line 203; handler calls `pauseState.Pause/Resume/PauseAll/ResumeAll` |
| 5 | No changes in `src/Contracts/` or `docs/contract/` | `git diff` shows no modifications |
| 6 | `ContractVersion.Current` stays `4` | Unchanged |
| 7 | `TaskBoard.cs`, `FleetTask.cs`, `TaskExecutionLoop.cs`, `appsettings.json` untouched | Confirmed via `git diff` |
| 8 | All existing tests pass (232 tests, 0 failed) | `dotnet test` — 232 passed, 0 failed |
| 9 | PauseState unit tests pass (6 tests) | `dotnet test --filter "FullyQualifiedName~PauseState"` — 6 passed, 0 failed |
| 10 | RunLoopGate tests pass (10 tests) | `dotnet test --filter "FullyQualifiedName~RunLoopGate"` — 10 passed, 0 failed |
| 11 | `Pause_is_idempotent` test passes | Pause "SH-01" twice, `Snapshot().Count == 1` ✓ |
| 12 | `Resume_on_unpaused_id_does_nothing` test passes | Resume without pause, `IsPaused` is false ✓ |
| 13 | `PauseAll_pauses_every_listed_shuttle` test passes | Three ids paused, all `IsPaused` ✓ |
| 14 | `ResumeAll_resumes_every_listed_shuttle` test passes | Three ids resumed, all `IsPaused` false ✓ |
| 15 | `Blank_and_null_ids_are_ignored` test passes | Null/whitespace ids ignored, snapshot correct ✓ |
| 16 | `Snapshot_is_consistent_under_concurrency` test passes | 4 threads × 1000 ops, no exception, count == 4 ✓ |
| 17 | `CanMove_returns_false_when_paused` test passes | Gate pause → CanMove false ✓ |
| 18 | `CanMove_returns_false_when_paused_via_shared_state` test passes | Shared `PauseState` injected, pause through state → CanMove false ✓ |
| 19 | `CommitMove_returns_false_when_paused` test passes | CommitMove also refuses paused shuttle ✓ |
| 20 | `Shared_state_reflects_pause_made_through_gate` test passes | Gate.Pause → `_pauseState.IsPaused` true ✓ |
| 21 | `PauseAll_pauses_every_listed_shuttle` (gate) test passes | Gate.PauseAll → all IsPaused true ✓ |
| 22 | No contract or config change (R4) | appsettings.json, TaskBoard, FleetTask, contracts all untouched |

## Top issues (max 5)

| # | Issue | Blocker / Should-fix / Consider | Why it matters for a real shuttle system |
|---|-------|---|---|
| 1 | **None — implementation is clean** | — | All requirements from the lesson, decision record, and acceptance criteria are met. No blocking issues. |
| 2 | **Handler drives both pause sets (R3)** | Should-fix verify | The handler calls both `taskBoard.PauseAllExcept` and `pauseState.Pause/Resume`. If one set is missed (e.g., only dispatch pause, no movement pause), shuttles keep moving despite operator command — lost pallet or stuck shuttle. The implementation does both calls, so this is correct. |
| 3 | **`registry.ShuttleIds()` provides the full list for pause-all** | Consider | When the operator pauses/resumes "all shuttles", the handler uses `registry.ShuttleIds()` to know which shuttle IDs exist. If the registry is empty (e.g., at startup before any status messages arrive), pause-all is a no-op. This is acceptable — the system cannot pause shuttles it doesn't know about. |
| 4 | **Two-constructor pattern in `RunLoopGate`** | Consider | The 3-arg constructor creates a private `PauseState` for backward compatibility; the 4-arg accepts a shared instance for `serve`. A test or scenario that creates a gate without the 4-arg constructor gets an independent pause state, which would not reflect pauses made through `serve`'s `PauseState`. This is by design — tests stay isolated. |
| 5 | **Thread safety relies on `lock` discipline** | Should-fix verify (in production) | The `lock (_gate)` in every `PauseState` method is correct but fragile: if a new method is added and the lock is forgotten, the set can corrupt again. The mutation check (a) in the lesson confirms this test would fail. This is a normal trade-off for a small shared set; real systems would eventually switch to a reader-writer lock or `ConcurrentDictionary`. |

## What is solid

- **`PauseState` is a proper small class** with a private lock object and a `HashSet<string>` using ordinal comparison. Every public method is safe to call from any thread.
- **The two constructors in `RunLoopGate` are a clean composition pattern**: the 3-arg creates its own state (so existing tests and sim code keep working), and the 4-arg lets `serve` inject the one instance it owns. This means the same `PauseState` is shared between the gate and the handler without either one having to know about the other's existence.
- **All mutation checks from the lesson are encoded in the test suite**: `Pause_is_idempotent`, `Resume_on_unpaused_id_does_nothing`, `Snapshot_is_consistent_under_concurrency`, `CanMove_returns_false_when_paused_via_shared_state`, and `Shared_state_reflects_pause_made_through_gate`. All pass.
- **No contract or config change**: `ContractVersion.Current` stays `4`, no new keys in `appsettings.json`, `TaskBoard` is completely unchanged. The message contract version `4` is preserved.
- **Tests cover both angles**: pause/resume through the `PauseState` directly, and pause/resume through the `RunLoopGate` interface. The gate tests also verify that `CommitMove` refuses a paused shuttle.

## Explain-it check

Pick the 3 decisions an interviewer would most likely ask about. **Do not answer them** — just list the questions a reviewer would ask the student out loud.

1. **Why does `PauseState` live in `FleetCore` rather than `FleetManager` or `RunLoopGate`?**  
   *(The student should explain that `serve` has no rack/track/traffic model, so depending on `RunLoopGate` would require building a whole traffic model just to pause. `FleetCore` is the lowest layer both `serve` and the gate can reach without a cycle.)*

2. **The lock lives inside `PauseState`, not inside `RunLoopGate` or the handler. Why?**  
   *(The student should explain that the gate is only one consumer. If the lock were in the gate, the handler would have to go through the gate to pause, but `serve` has no gate. Putting the lock in `PauseState` makes every user safe by construction, with no coordination needed between callers.)*

3. **`RunLoopGate` has two constructors — when is each used, and why keep the 3-arg one at all?**  
   *(The student should explain that the 3-arg constructor creates a private `PauseState` so every existing test and sim keeps working unchanged. The 4-arg constructor accepts a shared instance so `serve` (the composition root) can inject the one instance it owns. Tests that need isolation use the 3-arg constructor and get their own pause state.)*

### Unclear / unexplained lines or blocks for the student to study

- **Line 326 in `Program.cs`**: `pauseState.PauseAll(registry.ShuttleIds())` — the handler uses the registry's list of known shuttle IDs to pause all shuttles. The student should verify that `ShuttleIds()` returns all shuttles currently recorded (from status messages) and understand why this is needed for the blank‑ID branch of pause/resume.
- **Lines 325–335 in `Program.cs`**: The handler calls both `taskBoard.PauseAllExcept(...)` and `pauseState.Pause/Resume(...)`. The student should articulate why both are necessary — one stops dispatch (taskBoard), the other stops movement (PauseState) — and what would drift if only one set of calls were present.
- **The `PauseState` delegation pattern in `RunLoopGate`** (lines 33–41): every public pause op delegates to `_pauseState`. The student should confirm that the gate's public shape is unchanged and that `CanMove` and `CommitMove` both call `_pauseState.IsPaused` internally.

## Revisit list

- **Lock discipline in `PauseState`**: every method must lock `_gate`. If a new method is added, the lock must be remembered. (Mutation check 3a in the lesson confirms this.)
- **Two-constructor pattern in `RunLoopGate`**: 3-arg creates own state; 4-arg injects shared state. Know which constructor your test or composition root uses.
- **`PauseState` vs `TaskBoard` pause semantics**: `PauseState` is keyed by shuttle id; `TaskBoard` is keyed by task id. The handler drives both so a single operator click cannot leave them out of step on a single click, but they are still separate sets with different keys.
- **`ShuttleRegistry.ShuttleIds()` usage**: the pause‑all/resume‑all commands need to know about all shuttles the registry has seen. Study how the registry records status and how the list is used in the handler.

## End-product check

The implementation delivers exactly what lesson 30, decision record 39, and the M7 spec require: a single thread‑safe `PauseState` in `FleetCore` that both `RunLoopGate` and the operator‑command handler in `serve` can use. This closes the gap identified in review 29 — the dashboard Pause now reaches the movement gate, so a full scenario (inject a fault, pause from the dashboard, acknowledge recovery, resume) can be run from the dashboard alone, satisfying M7's done‑when criterion. The pause state is shared in‑process; cross‑process pause (a scenario runner in another process) is tracked as a follow‑up in decision 39 but is out of scope for this lesson. No contract version was bumped, no new configuration keys were added, and all protected files (`TaskBoard`, `FleetTask`, `TaskExecutionLoop`, contracts) remain untouched. The risk introduced is minimal: the only new shared mutable state is the `PauseState` set, which is properly guarded by a lock and covered by the full test suite. future milestones (fault injection, recovery from dashboard, WMS integration) can all build on this shared pause state without retrofitting.