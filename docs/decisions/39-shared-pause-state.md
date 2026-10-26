# Decision 39: Shared, thread-safe pause state for the gate and the dashboard

**Date:** 2026-10-26
**Milestone:** M7 (operator dashboard and diagnostics)
**Status:** Accepted
**Builds on:** decision 38 (traffic control under the dashboard)

## Context

Lesson 29 put a per-shuttle pause set inside `RunLoopGate`. The set is a plain
`HashSet<string>`. It works in the single-threaded sim tests. But review 29
flagged two problems:

1. There are two pause sets now — the gate's shuttle-id set and
   `TaskBoard`'s task-id set. They can drift: an operator can pause movement
   while tasks still dispatch, or pause dispatch while shuttles keep moving.
2. The gate is touched by two threads once the dashboard is wired: the MQTT
   operator-command handler thread and the tick loop thread. A plain `HashSet`
   is not thread-safe. A concurrent `Add` and `Contains` can corrupt the set
   or throw.

M7's done-when requires a scenario to be "run and recovered from the
dashboard alone", which means the dashboard's Pause/Resume must reach the
movement gate. So the pause state must be (a) reachable from `serve` without
a rack/track/traffic model, and (b) safe to touch from more than one thread.

## Options

### Survey 1: Where the pause set lives

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. Keep the set inside `RunLoopGate`** | The gate owns a `HashSet<string>` as today. | Lowest. | `serve` has no `RunLoopGate` (no rack/track/traffic), so the dashboard cannot reach the movement pause without instantiating a whole gate. The two sets stay separate. | Seen in small single-process systems. |
| **B. Extract a `PauseState` in `FleetCore`** (chosen) | A small class in `FleetCore` owns the paused shuttle-id set and exposes `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`. `RunLoopGate` holds one. `serve` instantiates one directly. | Low. One new file in `FleetCore`; `RunLoopGate` delegates. | Two instances must be the *same* instance to stay in sync; the composition root must pass one instance around. | Common: a shared "movement permission" object that both the gate and the operator-facing API use. |
| **C. Put the pause set in `TaskBoard`** | `TaskBoard` owns the set; the gate asks the board. | Medium. | `FleetCore` must not learn about shuttles... but shuttle ids are just strings, so it is allowed. Still, mixing task dispatch and movement permission in one class blurs the boundary the reviewer already flagged. | Rare; most systems keep movement permission separate from work queue. |

**Decision B.** The pause set is a movement concern, but it must be reachable
from `serve` without a rack model. Extracting it into `FleetCore` lets `serve`
create one instance and hand the same instance to any in-process gate. The
keys stay shuttle ids (not task ids), so this does not change `TaskBoard`'s
dispatch-pause semantics from lesson 26.

### Survey 2: Thread safety

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. `lock` around a `HashSet`** (chosen) | A private lock object guards every read and write of the set. | Low. Easy to read and explain. | Lock contention is negligible for a small set touched once per tick and once per operator command. | The default choice for a small shared set. |
| **B. `ConcurrentDictionary<string, byte>`** | Use the concurrent collection as a set. | Low. | `IsPaused` is `ContainsKey`, fine. But the "all paused ids" snapshot needs `Keys.ToArray()`, and the all-shuttle helpers loop under no lock, so a snapshot can be stale. Less obvious to an interviewer. | Used when contention is high. Not here. |
| **C. No lock, rely on single thread** | Document that the set is single-threaded. | Lowest. | The moment a second thread touches it (the MQTT handler), it is wrong. The dashboard wiring makes that moment certain. | Only valid before any concurrency exists. |

**Decision A.** A `lock` is the simplest correct answer and is easy to defend
in an interview. The set is tiny and touched rarely, so contention is not a
concern.

### Survey 3: Who creates the instance

| Option | What it means | Complexity | How it fails | How real fleet systems do it |
|---|---|---|---|---|
| **A. `serve` creates one and passes it to the handler and to any gate** (chosen) | The composition root owns the instance. | Low. | If a caller forgets to pass it, a second instance is created and the sets drift. Mitigated by keeping the gate's 3-arg constructor (creates its own) for tests and sim-only code. | Standard composition-root ownership. |
| **B. A static singleton** | One global instance. | Lowest. | Hidden dependency; tests cannot isolate; two fleet managers in one process would share pause state. | Avoided in testable code. |

**Decision A.** The composition root owns the instance. Tests keep using the
gate's 3-arg constructor, which creates its own `PauseState`, so no test
changes.

## Decision

- **A.** Extract `PauseState` into `FleetCore` (new file `PauseState.cs`). It owns
  a `HashSet<string>` of paused shuttle ids (ordinal) guarded by a lock, and
  exposes `Pause`, `Resume`, `IsPaused`, `PauseAll`, `ResumeAll`, plus a
  `Snapshot()` for diagnostics.
- **B.** `RunLoopGate` holds a `PauseState`. The 3-arg constructor creates one
  internally (backward compatible). A new 4-arg constructor accepts a shared
  instance. `Pause`/`Resume`/`IsPaused`/`PauseAll`/`ResumeAll` delegate.
- **C.** `serve` creates one `PauseState` and the operator-command handler
  calls `_pauseState.Pause`/`Resume`/`PauseAll`/`ResumeAll`. `TaskBoard`'s
  dispatch pause from lesson 26 is unchanged; the handler drives both, so a
  dashboard Pause stops dispatch (TaskBoard) and movement (`PauseState`).
- No change to the message contract. `ContractVersion.Current` stays `4`.
  No new configuration key. `TaskBoard` is unchanged.

## What would make us revisit

- If a second process (the sim) must observe the same pause state, an
  in-process object is not enough; the pause must travel over MQTT or be
  stored. That is a cross-process slice, tracked separately.
- If contention on the lock ever shows up in profiling, switch to a
  reader-writer lock or a concurrent collection.
- If `TaskBoard`'s dispatch pause and `PauseState`'s movement pause drift in
  practice, unify the keys (pause by shuttle id in both) in a later slice.
- Rejected: static singleton (option B in Survey 3) — hidden dependency.
- Rejected: no lock (option C in Survey 2) — unsafe once the handler thread
  exists.

## Consequences

- `FleetCore` gains one small, pure, thread-safe class.
- `RunLoopGate`'s public pause API is unchanged; existing tests pass.
- `serve` gains one instance and two handler calls.
- The dashboard's Pause/Resume now reaches the movement gate when the sim runs
  in the same process (scenario runner). Cross-process pause is a follow-up.
- `TaskBoard`'s pause semantics (lesson 26) are untouched; the handler drives
  both sets together so they cannot drift on a single operator click.
