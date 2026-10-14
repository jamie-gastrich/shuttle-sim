# Review 13: Heartbeats, `lastSeenAt`, and the timeout window (lesson 13, M5 first slice)

**Date:** 2026-10-13 (re-review)
**Review of:** the owner's uncommitted working tree. `git status` shows `M src/FleetManager/appsettings.json`, `M src/ShuttleSim.Tests/ShuttleEndpointTests.cs`, `M src/ShuttleSim/Program.cs`, `M src/ShuttleSim/ShuttleEndpoint.cs`, `M src/ShuttleSim/appsettings.json`, and untracked `src/FleetManager/ShuttleHealth.cs`, `src/FleetManager.Tests/ShuttleHealthTests.cs`, `src/ShuttleSim/HeartbeatPublisher.cs`. HEAD is unchanged from the previous review.
**Scope:** the four blockers and one should-fix from the previous review (below), lesson 13's acceptance criteria, [record 20](../decisions/20-liveness-heartbeats-and-timeouts.md), and the end-product spec's M5 line.
**Contract checked:** `docs/contract/v2.md` is still `Version: 2`; no `v3.md`. No wire change.
**Process note:** this review modified no file under `src/`. A broker was started (`docker compose -f docker/compose.yml up -d broker`) only for the end-to-end serve test and stopped with `docker compose down`; scratch mutation builds ran in `/tmp/opencode/l13rereview` and were deleted. No container, broker, or `dotnet run` process is left running. `git status` after the review is identical to before it.

**Verdict: pass with changes**

All four blockers are fixed and verified: the monotonic guard is in and mutation-proven, the silence test is correct, both config keys are present, and the heartbeat is now started before the Ctrl+C wait and is proven to publish over a real broker with a clean SIGINT exit. The pure-suite gate is green. One should-fix from the previous review is only half done — `ShuttleHealth` still re-enters its own lock through `LastSeenAt` / `IsUnresponsive` — and that is the only thing keeping this from a clean pass. It is benign today (`Monitor` is reentrant) but it is exactly the "one atomic operation per public method" discipline the lesson teaches, so fix it and this slice passes. No milestone table change is made on a pass-with-changes verdict.

---

## Previous blockers — resolution status

| # | Blocker from the prior review | Status | Evidence |
|---|---|---|---|
| 1 | `RecordSeen` not monotonic (`ShuttleHealth.cs:26` overwrote unconditionally) | **fixed** | `ShuttleHealth.cs:25` now `if (!_lastSeenAt.TryGetValue(shuttleId, out var previous) || seenAt > previous)`. The monotonic test records an *older* stamp (`now+10s` after `now+75s`) and asserts `LastSeenAt == now+75s`. Mutation: replacing the guard with an unconditional store turns `A_new_heartbeat_makes_a_silent_shuttle_responsive_again` red (1 failed / 4 passed in `FleetManager.Tests`). |
| 2 | `Silence_past_the_timeout_marks_a_shuttle_unresponsive` recorded and queried at the same instant | **fixed** | `ShuttleHealthTests.cs:26,28`: records one heartbeat at `now`, asserts `IsUnresponsive(ShuttleId, now+31s)` is `True` against a 30 s window. Passes. |
| 3 | Both config keys missing | **fixed** | `src/ShuttleSim/appsettings.json:11` has `"HeartbeatIntervalMs": 1000`; `src/FleetManager/appsettings.json:17` has `"LivenessTimeoutMs": 3000`. Both positive ints. `Program.cs:102-105` reads and validates the shuttle key. |
| 4 | Publisher started after `await stopped.Task`, then cancelled immediately | **fixed** | `Program.cs:106-108` builds the CTS and starts `heartbeat.RunAsync(...)` *before* the `stopped`/`CancelKeyPress` wait (`:118-125`); `:127` cancels, `:130-135` awaits with the `when (stopping.IsCancellationRequested)` filter. End-to-end: with the broker up, `dotnet ShuttleSim.dll serve` published one `shuttleStatus` every ~1000 ms and `timeout --signal=INT --preserve-status 4` returned **RC 0** with no leftover process. |
| 5 | should-fix: `ShuttleHealth` re-enters its own lock; pure tests build a full `Shuttle`/`Rack` for an id | **partial** | Tests: fixed — `ShuttleHealthTests.cs` now uses `const string ShuttleId = "SH-01"` and no `Shuttle`/`Rack`. Lock: **not fixed** — `IsUnresponsive` at `:55` still calls `LastSeenAt` while holding `_lock`, and `UnresponsiveShuttles` at `:65` still calls `IsUnresponsive` while holding `_lock`. |

---

## Acceptance criteria check

Run from the repo root unless a phase says `src/`. Evidence is from a clean `dotnet build` and the exact suite command in the lesson, run on 2026-10-13.

| # | Requirement | Result | Evidence |
|---|---|---|---|
| 1 | `ShuttleHealth` exists with pinned members | pass | Phase 1: `class ShuttleHealth`, `RecordSeen`/`LastSeenAt`/`IsUnresponsive`/`UnresponsiveShuttles` all present; constructor rejects non-positive. |
| 1a | `RecordSeen` is monotonic | **pass** | `:25` newest-wins guard. Mutation (force store) → monotonic test red. |
| 1b | Blank `shuttleId` throws `ArgumentException` | pass | Validated in all three id-taking methods, consistent with `OccupancyMap`. |
| 1c | `IsUnresponsive` boundary: exactly the window is responsive | pass | `:56` uses `>`. Mutation `>`→`>=` → `A_shuttle_silent_for_exactly_the_timeout_is_still_responsive` red. |
| 1d | `UnresponsiveShuttles` returns a snapshot | pass | `:65` returns `...ToList()`. |
| 1e | No `DateTime.UtcNow`/I/O/contract types in the pure file | pass | Phase 2 greps printed nothing. |
| 2 | `HeartbeatPublisher` uses `PeriodicTimer` + `CancellationToken`, no blocking | pass | `HeartbeatPublisher.cs:14-19`; Phase 4 found no `Thread.Sleep`/`.Result`/`.Wait()`/`async void`. |
| 3 | `ShuttleEndpoint.PublishStatusAsync(DateTime, CancellationToken)` publishes current state | pass | `ShuttleEndpoint.cs:82-83` delegates to the existing private path; `moving`/`idle` path unchanged; test passes. |
| 4 | `Shuttle:HeartbeatIntervalMs` read and validated in `Program.cs` | pass | `Program.cs:102-105`. |
| 4a | Heartbeat started before the run and stopped cleanly on Ctrl+C | pass | `Program.cs:106-108` before the wait; end-to-end RC 0 on SIGINT, no leftover process. |
| 5 | `Shuttle:HeartbeatIntervalMs` in `ShuttleSim/appsettings.json` | pass | `:11`, value 1000. |
| 5a | `FleetManager:LivenessTimeoutMs` in `FleetManager/appsettings.json` | pass | `:17`, value 3000. |
| 6 | Five pinned `ShuttleHealthTests` exist and pass | pass | All five names present via `--list-tests`; all pass. |
| 7 | `Heartbeat_publishes_the_current_status_without_a_command` present and pure | pass | Present; `FakeBus`; asserts one `Status`, `idle`, `A01-L01`, timestamp `Start`. |
| 8 | Contract stays v2 | pass | `Version: 2`, no `v3.md`, three kinds intact. |
| 9 | Protected files unchanged | pass | `git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs src/TrafficCore src/ShuttleCore src/Contracts docs/contract docker/Dockerfile.build` → clean. |
| 10 | No M5 later items / M6 / M7 scope creep | pass | Greps for alarm/recovery/reassign/persist/db in the changed files find nothing. |
| — | Build | pass | `dotnet build` exit 0, 0 warnings, 0 errors. |
| — | Pure suite gate (`RC 0`, 8 `Passed!`, 0 `Failed!`, 90 tests, `FleetManager.Tests=21`, `ShuttleSim.Tests=6`) | pass | `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → RC 0, 8 `Passed!`, 0 `Failed!`, 90 total; `FleetManager.Tests` 21/21; `ShuttleSim.Tests` 6/6. |

---

## Top issues (max 5), ranked

### 1. should-fix — `ShuttleHealth` still re-enters its own lock (the one unresolved item)
`IsUnresponsive` (`ShuttleHealth.cs:45-58`) takes `_lock`, then calls `LastSeenAt` (`:32-43`), which takes `_lock` again and re-validates the id. `UnresponsiveShuttles` (`:60-67`) takes `_lock`, then calls `IsUnresponsive` for each key, locking twice more per shuttle. `Monitor` is reentrant, so this is correct today and no test can catch it — but the lesson and record 20 §4 both say the point of the lock is "the whole read-modify-write is one atomic step" and "one atomic operation per public method". Nesting public locking methods makes each query pay redundant id validation, and it becomes a real bug the day `LastSeenAt` gains I/O, a different lock, or a cancellation check — the inner call would then run under the outer lock unexpectedly. Read `_lastSeenAt` directly under the single lock in each method. This is the only thing between the slice and a clean pass.

### 2. consider (lesson defect, not an owner failure) — the endpoint test is not load-bearing for "current state"
The lesson says the mutation "make `PublishStatusAsync` hard-code `idle` → `Heartbeat_publishes_the_current_status_without_a_command` goes red when the shuttle is not idle". I ran that mutation in the scratch copy: hard-coding `"idle"` in the public method keeps the test **green**, because the fixture `CreateShuttle()` returns an idle shuttle at `A01-L01`. The test does assert the state literal, so it is not wrong — but it cannot distinguish "current state" from "always idle". Either move the shuttle to a non-idle state before the call (e.g. `StartMove`) so the mutation bites, or correct the lesson's mutation claim. The lesson's other three mutation claims are accurate — verified.

### 3. consider — `using ShuttleCore;` in `ShuttleHealthTests.cs:1` is now dead
The tests no longer construct a `Shuttle`/`Rack` (good), so the only reason for the using is gone. Removing it keeps the pure liveness test independent of `ShuttleCore`, which was the point of the should-fix. Does not warn (unused usings are not on by default), so it is cleanup only.

### 4. consider — the monotonic assertion loses its failure message
`ShuttleHealthTests.cs:55` is `Assert.True(shuttleHealth.LastSeenAt(ShuttleId) == now.AddSeconds(75));`. When it fails, the report says `Expected: True, Actual: False` and hides the two timestamps — precisely the values a future maintainer needs. Use `Assert.Equal(now.AddSeconds(75), shuttleHealth.LastSeenAt(ShuttleId))`. Also, the test's name promises the silent→responsive transition; it never asserts the intermediate unresponsive state after `now+60s`, so consider pinning that too.

### 5. consider — trailing whitespace on the new `Program.cs` blank lines
`Program.cs:100-101` and `:126` are blank lines with trailing spaces (visible in `git diff`). Cosmetic; the rest of the diff is clean.

---

## What is solid

- **All four blockers are genuinely fixed, not papered over.** The monotonic guard uses `seenAt > previous` (equal ignored) exactly as the lesson's shape shows; the three named mutations all produce the expected failures (monotonic guard removed → monotonic test red; `>`→`>=` → boundary test red; always-responsive → silence and never-seen red).
- **The serve-loop fix was verified end-to-end, not just read.** With the broker up, `shuttle/SH-01/status` was published every ~1000 ms, SIGINT returned **RC 0**, and no process was left behind. This closes the prior review's "never publishes" blocker with real evidence.
- **The endpoint change is minimal and safe.** One new public method delegating to the existing private publish path; `HandleCommandAsync` is unchanged, so there is no regression in the command path.
- **`HeartbeatPublisher` is exactly the pinned shape** — `PeriodicTimer`, `using`, `CancellationToken` passed through, injected clock, both constructor arguments validated.
- **Discipline held.** Protected files untouched, contract still v2, no alarms/recovery/reassignment/persistence, no docker in tests, build at 0 warnings.
- **The monotonic test now actually pins what it names**: it records an older stamp and asserts `LastSeenAt` did not move back — the exact gap the first review called out.

---

## Explain-it check

Answer these out loud or in writing before the next lesson. An interviewer would ask exactly these.

1. **Monotonicity.** In `RecordSeen`, why is the guard `seenAt > previous` and not `>=`? Name the two concrete ways an older-or-equal stamp arrives over MQTT, and say what the next lesson's alarm would do if the guard were removed.
2. **Nested locking.** `IsUnresponsive` calls `LastSeenAt` while already holding `_lock`. Explain what the runtime does when `Monitor` is asked to lock the same object twice on one thread, and why the code is correct today anyway. Then describe a future change to `LastSeenAt` that would turn this nesting into a real bug — and rewrite the two methods so the dictionary is read directly under one lock.
3. **Serve-loop shutdown.** Walk through `Program.cs` from `:106` onward. Why must `heartbeat.RunAsync` be started *before* `await stopped.Task`? Why is `stopping.Cancel()` followed by `await heartbeatTask` rather than just cancelling? Why does that await raise `OperationCanceledException`, and why does the `when (stopping.IsCancellationRequested)` filter matter?

**Lines/blocks to study (unclear, likely-misread, or copied-looking):**
- `src/FleetManager/ShuttleHealth.cs:45-58` and `:60-67` — the nested lock; state which single operation each method should be.
- `src/FleetManager.Tests/ShuttleHealthTests.cs:1` — why the `using ShuttleCore;` is still there when nothing from it is used.
- `src/FleetManager.Tests/ShuttleHealthTests.cs:55` — re-read the assertion; what does the failure report show, and what should it show?
- `src/ShuttleSim/Program.cs:100-101, 126` — trailing whitespace from the edit.

---

## Revisit list

- **Lock discipline.** One lock, one atomic operation per public method; do not call a locking sibling from inside another lock even though `Monitor` allows it. Compare with `OccupancyMap`/`SegmentReservations`.
- **Test names must earn their names — including from the outside.** For each new test, ask "would this go red if the behavior it names were broken?" Then run the mutation in a scratch copy. The heartbeat/endpoint test currently does not, because its fixture is idle.
- **Assertions that fail usefully.** `Assert.Equal` over `Assert.True(a == b)`; include the expected and actual values.
- **Async lifecycle and cancellation.** Start the long-running task, keep its `Task`, signal, await, catch the expected cancellation — now confirmed end-to-end, so you can explain the ordering from memory.
- **Configuration as a contract.** Both keys exist; be ready to say who reads each and when (`HeartbeatIntervalMs` now; `LivenessTimeoutMs` in the next lesson).

---

## End-product check

This slice is the *detection* half of M5's "lost communication": a heartbeat producer and a pure rule that turns a stale `lastSeenAt` into "unresponsive". The structure, purity, and boundary rule carry forward intact — the alarm slice, record 18's closest-eligible dispatch, and M7's "last seen" panel will all read exactly these objects. The monotonic guard is the safety property that keeps duplicate or retained MQTT traffic from manufacturing a false "lost" verdict, and it is now correct and pinned. Two risks remain, both small: the nested-lock smell in `ShuttleHealth` is a maintenance trap, and the endpoint test does not actually prove "current state" under mutation. Neither corrupts fleet state today; the first is one small edit from correct. Contract v2 is untouched, protected files are untouched, and no later-M5 scope has leaked in, so the shape is worth keeping.

---

## What the reviewer is looking for

Concrete, line-level guidance for the (small) follow-up. The suite is green; do not restructure anything that already works.

1. **`src/FleetManager/ShuttleHealth.cs:45-58`** — inside `IsUnresponsive`, do not call `LastSeenAt`. Read the dictionary under the one lock you already hold:
   `return !_lastSeenAt.TryGetValue(shuttleId, out var lastSeen) || now - lastSeen > _livenessTimeout;`
   Keep the blank-id check inside the lock (or move all three id checks to the top of each method, consistently).
2. **`src/FleetManager/ShuttleHealth.cs:60-67`** — inside `UnresponsiveShuttles`, do not call `IsUnresponsive`. Filter the entries directly under the lock, e.g. `_lastSeenAt.Where(entry => now - entry.Value > _livenessTimeout).Select(entry => entry.Key).ToList();`
3. **`src/FleetManager.Tests/ShuttleHealthTests.cs:1`** — remove `using ShuttleCore;`.
4. **`src/FleetManager.Tests/ShuttleHealthTests.cs:55`** — replace with `Assert.Equal(now.AddSeconds(75), shuttleHealth.LastSeenAt(ShuttleId));` and optionally assert the intermediate unresponsive state at `now+60s` so the test name is honest.
5. **`src/ShuttleSim.Tests/ShuttleEndpointTests.cs:99-113`** — make the heartbeat test prove "current state": move the shuttle off idle before `PublishStatusAsync` (e.g. `shuttle.StartMove("A01-L02", Start)`), then assert `moving`/`A01-L02`. Verify in a scratch copy that hard-coding `"idle"` in `Program.cs`/`ShuttleEndpoint.cs` now turns it red.
6. **`src/ShuttleSim/Program.cs:100-101, 126`** — drop the trailing whitespace.
7. **Re-run the exact gate:** `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` must stay RC 0, 8 `Passed!`, 0 `Failed!`, 90 tests, `FleetManager.Tests=21`, `ShuttleSim.Tests=6`. No milestone table change on this pass-with-changes verdict; the next review with a clean pass can update it.

---

## Note for the professor (lesson/hints defect — still open)

Two defects in the lesson materials, neither the owner's fault:

1. **The hints addendum line map is still stale.** `docs/lessons/13-heartbeats-and-timeouts-hints.md` (lines 153-164) still says "The method **already** does all of this, in order: `:79` reads `Shuttle:HeartbeatIntervalMs`; `:102-103` throws …; `:104` `using var stopping`; `:105-106` …". That is false for the starting state. `git show HEAD:src/ShuttleSim/Program.cs` has no heartbeat config, no CTS, and `return 0` at `:118`; and the *current* file reads the key at `:102`, throws at `:105`, creates `stopping` at `:106`, starts the task at `:107-108`, prints at `:110`, sets `stopped` at `:111`, hooks the handler at `:112-117`, and runs the try/finally at `:118-125`. The addendum's numbers match neither the pre-change nor the post-change file. Presenting a post-solution line map as the starting state is very likely what led the owner to drop the whole heartbeat block below the Ctrl+C wait in the first place. Rewrite it to describe the starting state accurately and to say plainly which lines go before the wait and which after.
2. **The endpoint mutation claim is wrong.** The lesson (line 578) says hard-coding `idle` in `PublishStatusAsync` turns `Heartbeat_publishes_the_current_status_without_a_command` red "when the shuttle is not idle". The pinned test builds an idle shuttle, so the mutation stays green (verified in `/tmp/opencode/l13rereview`). Either change the test fixture to non-idle or change the claim.

Otherwise the lesson held to the three-concept cap (`heartbeat`, `lastSeenAt`, timeout detection) and covered every item in "Concepts this assignment requires".
