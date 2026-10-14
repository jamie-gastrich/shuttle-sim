# Review 13: Heartbeats, `lastSeenAt`, and the timeout window (lesson 13, M5 first slice)

**Date:** 2026-10-13 (re-review)
**Review of:** the owner's uncommitted working tree. `git status` shows `M src/FleetManager/appsettings.json`, `M src/ShuttleSim.Tests/ShuttleEndpointTests.cs`, `M src/ShuttleSim/Program.cs`, `M src/ShuttleSim/ShuttleEndpoint.cs`, `M src/ShuttleSim/appsettings.json`, and untracked `src/FleetManager/ShuttleHealth.cs`, `src/FleetManager.Tests/ShuttleHealthTests.cs`, `src/ShuttleSim/HeartbeatPublisher.cs`. HEAD is unchanged (`95213ae`).
**Scope:** the one open should-fix from the pass-with-changes review (nested lock) plus every item in that review's "What the reviewer is looking for" section, lesson 13's acceptance criteria, [record 20](../decisions/20-liveness-heartbeats-and-timeouts.md), and the end-product spec's M5 line.
**Contract checked:** `docs/contract/v2.md` is still `Version: 2`; no `v3.md`. No wire change.
**Process note:** this review modified no file under `src/`. The latest fixes touched only `ShuttleHealth.cs`, the two test files, the two `appsettings.json`, and the new public endpoint method; the serve loop in `Program.cs` is unchanged from the prior end-to-end verification (below). Scratch mutation builds ran in `/tmp/opencode/l13m` and were deleted. No broker, container, or `dotnet run` process is left running. `git status` after the review is identical to before it.

**Verdict: pass**

The only thing holding the prior review at "pass with changes" was the nested lock in `ShuttleHealth`. It is fixed cleanly: every public method now reads or writes the dictionary directly under a single lock acquisition, and no public method calls a public sibling. The other follow-ups are done too — the dead `using ShuttleCore;` is gone, the endpoint heartbeat test is now load-bearing (mutation-proven), and the monotonic assertion fails usefully. The pure-suite gate is green at the exact gated counts, protected files and the contract are untouched, and no later-M5/M6/M7 scope leaked in. This slice passes. M5 stays `in progress` — alarms, recovery, and reassignment are still ahead — and a verification note is recorded in `README.md`.

---

## Should-fix / follow-up resolution status

| # | Item from the previous review | Status | Evidence |
|---|---|---|---|
| 1 | `ShuttleHealth` re-entered its own lock (`IsUnresponsive` called `LastSeenAt`; `UnresponsiveShuttles` called `IsUnresponsive`) | **fixed** | `ShuttleHealth.cs:45-55` `IsUnresponsive` reads `_lastSeenAt.TryGetValue` directly under its own `lock`; `:57-63` `UnresponsiveShuttles` filters `_lastSeenAt` directly; `:17-30` `RecordSeen` and `:32-43` `LastSeenAt` likewise touch the dictionary directly. Read all four bodies: one `lock (_lock)` each, no call to a public sibling. |
| 2 | Dead `using ShuttleCore;` in `ShuttleHealthTests.cs` | **fixed** | `src/FleetManager.Tests/ShuttleHealthTests.cs:1` is now `namespace FleetManager;` — the file has no `using` directives and no `Shuttle`/`Rack`. The pure liveness test no longer depends on `ShuttleCore`. |
| 3 | Endpoint heartbeat test not load-bearing for "current state" | **fixed** | `ShuttleEndpointTests.cs:104` calls `shuttle.StartMove("A01-L02", Start)` before `PublishStatusAsync`, then asserts `moving` and `A01-L01`. Mutation in `/tmp/opencode/l13m` (hard-code `"idle"` in the public method) → `Heartbeat_publishes_the_current_status_without_a_command` **Failed! 1**. |
| 4 | Monotonic assertion hid its values (`Assert.True(a == b)`) | **fixed** | `ShuttleHealthTests.cs:53` is now `Assert.Equal(now.AddSeconds(75), shuttleHealth.LastSeenAt(ShuttleId));`. On failure the report shows both timestamps. |
| 5 | Trailing whitespace on `Program.cs` blank lines | **partial (cosmetic)** | `Program.cs:100` is still a whitespace-only blank line; a new one exists at `ShuttleHealth.cs:25`. Does not affect build, tests, or behavior. |

---

## Acceptance criteria check

Run from the repo root unless a phase says `src/`. Evidence is from a clean `dotnet build` and the exact suite command in the lesson, run on 2026-10-13.

| # | Requirement | Result | Evidence |
|---|---|---|---|
| 1 | `ShuttleHealth` exists with pinned members | pass | Phase 1: `class ShuttleHealth`, `RecordSeen`/`LastSeenAt`/`IsUnresponsive`/`UnresponsiveShuttles` all present; constructor rejects non-positive. |
| 1a | `RecordSeen` is monotonic | pass | `:25` newest-wins guard. Mutation A (force store) → `A_new_heartbeat_makes_a_silent_shuttle_responsive_again` **Failed! 1**. |
| 1b | Blank `shuttleId` throws `ArgumentException` | pass | Validated in all three id-taking methods, consistent with `OccupancyMap`. |
| 1c | `IsUnresponsive` boundary: exactly the window is responsive | pass | `:53` uses `>`. Mutation B (`>`→`>=`) → `A_shuttle_silent_for_exactly_the_timeout_is_still_responsive` **Failed! 1**. |
| 1d | `UnresponsiveShuttles` returns a snapshot | pass | `:61` returns `...ToList()` under the one lock. |
| 1e | One atomic operation per public method (no nested public calls) | pass | Phase 3 and the file read: each of the four public methods takes `_lock` once and reads/writes `_lastSeenAt` directly. |
| 1f | No `DateTime.UtcNow`/I/O/contract types in the pure file | pass | Phase 2 greps printed nothing. No hard-coded lane ids. |
| 2 | `HeartbeatPublisher` uses `PeriodicTimer` + `CancellationToken`, no blocking | pass | `HeartbeatPublisher.cs:14-19`; Phase 4 found no `Thread.Sleep`/`.Result`/`.Wait()`/`async void`. |
| 3 | `ShuttleEndpoint.PublishStatusAsync(DateTime, CancellationToken)` publishes current state | pass | `ShuttleEndpoint.cs:82-83` delegates to the existing private path; `moving`/`idle` path unchanged; mutation-proven load-bearing. |
| 4 | `Shuttle:HeartbeatIntervalMs` read and validated in `Program.cs` | pass | `Program.cs:101-104`. |
| 4a | Heartbeat started before the run and stopped cleanly on Ctrl+C | pass | `Program.cs:105-107` before the `stopped`/`CancelKeyPress` wait (`:110-124`); `:125` cancels, `:127-133` awaits with the `when (stopping.IsCancellationRequested)` filter. Serve loop unchanged from the prior review, where it was proven end-to-end with the broker (one `shuttleStatus` per ~1000 ms, SIGINT RC 0, no leftover process). |
| 5 | `Shuttle:HeartbeatIntervalMs` in `ShuttleSim/appsettings.json` | pass | `:11`, value 1000. |
| 5a | `FleetManager:LivenessTimeoutMs` in `FleetManager/appsettings.json` | pass | `:17`, value 3000. |
| 6 | Five pinned `ShuttleHealthTests` exist and pass | pass | All five names present via `--list-tests`; all pass. |
| 7 | `Heartbeat_publishes_the_current_status_without_a_command` present and pure | pass | Present; `FakeBus`; asserts one `Status`, `moving`, `A01-L01`, timestamp `Start`. |
| 8 | Contract stays v2 | pass | `Version: 2`, no `v3.md`, three kinds intact. |
| 9 | Protected files unchanged | pass | `git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs src/TrafficCore src/ShuttleCore src/Contracts docs/contract docker/Dockerfile.build` → clean. |
| 10 | No M5 later items / M6 / M7 scope creep | pass | Greps for alarm/severity/acknowledg/reassign/recover/persist/db in the new and changed files find nothing. |
| — | Build | pass | `dotnet build` exit 0, 0 warnings, 0 errors. |
| — | Pure suite gate (`RC 0`, 8 `Passed!`, 0 `Failed!`, 90 tests, `FleetManager.Tests=21`, `ShuttleSim.Tests=6`) | pass | `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → RC 0, 8 `Passed!`, 0 `Failed!`, 90 total; `FleetManager.Tests` 21/21; `ShuttleSim.Tests` 6/6. |
| — | Mutation set still bites after the refactor | pass | A: monotonic guard removed → monotonic test red. B: `>`→`>=` → boundary test red. C: always-responsive → silence and never-seen tests red (2 failed). Endpoint: hard-coded `idle` → heartbeat test red. All four in `/tmp/opencode/l13m`. |

---

## Top issues (max 5), ranked

No blockers and no should-fix items remain. What follows is cleanup-level only.

### 1. consider — trailing whitespace (cosmetic)
`ShuttleHealth.cs:25` ends the guard line with a space, and `Program.cs:100` is a whitespace-only blank line. Harmless; a consistent editor setting (trim trailing whitespace) prevents this in the diffs reviewers read.

### 2. consider — the transition test name still promises one assertion it does not make
`A_new_heartbeat_makes_a_silent_shuttle_responsive_again` (`ShuttleHealthTests.cs:42-55`) records `+60s` ("simulate silence") but never asserts the shuttle was unresponsive *at that point*; it only asserts the final state at `+75s`. The test is correct and the name is not false, but pinning the intermediate `Assert.True(shuttleHealth.IsUnresponsive(ShuttleId, now.AddSeconds(60)))` would make the "silent → responsive again" arc explicit. Optional; the prior review flagged this too.

Nothing else is worth spending time on. The slice is small and clean.

---

## What is solid

- **The last behavior gap is closed, not worked around.** Every `ShuttleHealth` method now does exactly one thing under one lock: `RecordSeen` does the read-modify-write, the three queries read `_lastSeenAt` directly. No method calls a locking sibling. This is the "one atomic operation per public method" discipline the lesson and record 20 §4 ask for, and it will not silently change meaning if `LastSeenAt` later gains I/O or a different lock.
- **The endpoint test now earns its name.** Moving the fixture off idle before publishing makes "current state" provable: hard-coding `idle` turns it red. The prior review's lesson defect on that mutation claim is now moot.
- **The monotonic assertion now fails usefully** (`Assert.Equal` with both timestamps).
- **Discipline held.** Protected files untouched, contract still v2, no alarms/recovery/reassignment/persistence, no docker in tests, build at 0 warnings, gate green at the exact gated counts.
- **The tests are mutation-proven load-bearing**, all four named mutations producing the expected failures after the refactor.

---

## Explain-it check

Answer these out loud or in writing before the next lesson. An interviewer would ask exactly these.

1. **Monotonicity.** In `RecordSeen`, why is the guard `seenAt > previous` and not `>=`? Name the two concrete ways an older-or-equal stamp arrives over MQTT, and say what the next lesson's alarm would do if the guard were removed.
2. **Lock discipline.** Each `ShuttleHealth` method takes `_lock` once and reads `_lastSeenAt` directly. Explain what `lock`/`Monitor` does when the same thread locks the same object twice, why the *old* version (calling `LastSeenAt` from `IsUnresponsive`) was still correct but a maintenance trap, and what future change to `LastSeenAt` would have turned that nesting into a real bug.
3. **Serve-loop shutdown.** Walk through `Program.cs` from `:105` onward. Why must `heartbeat.RunAsync` be started *before* `await stopped.Task`? Why is `stopping.Cancel()` followed by `await heartbeatTask` rather than just cancelling? Why does that await raise `OperationCanceledException`, and why does the `when (stopping.IsCancellationRequested)` filter matter?

**Lines/blocks to study (unclear or worth re-reading):**
- `src/FleetManager.Tests/ShuttleHealthTests.cs:42-55` — the test name vs. the assertions; what intermediate state could you pin?
- `src/FleetManager/ShuttleHealth.cs:45-63` — confirm each method is one atomic operation and say why that matters.
- `src/FleetManager/ShuttleHealth.cs:25` and `src/ShuttleSim/Program.cs:100` — trailing whitespace from the edits.

---

## Revisit list

- **Lock discipline.** One lock, one atomic operation per public method; never call a locking sibling from inside another lock even though `Monitor` allows it. Compare with `OccupancyMap`/`SegmentReservations`. Now correct here — be able to explain why.
- **Test names must earn their names — including from the outside.** For each new test, ask "would this go red if the behavior it names were broken?" Then run the mutation in a scratch copy. The heartbeat/endpoint test now does.
- **Assertions that fail usefully.** `Assert.Equal` over `Assert.True(a == b)`; include the expected and actual values.
- **Async lifecycle and cancellation.** Start the long-running task, keep its `Task`, signal, await, catch the expected cancellation — explain the ordering from memory.
- **Configuration as a contract.** Both keys exist; be ready to say who reads each and when (`HeartbeatIntervalMs` now; `LivenessTimeoutMs` in the next lesson).

---

## End-product check

This slice is the *detection* half of M5's "lost communication": a heartbeat producer and a pure rule that turns a stale `lastSeenAt` into "unresponsive". The structure, purity, and boundary rule carry forward intact — the alarm slice, record 18's closest-eligible dispatch, and M7's "last seen" panel will all read exactly these objects. The monotonic guard is the safety property that keeps duplicate or retained MQTT traffic from manufacturing a false "lost" verdict, and it is correct and pinned. The lock discipline is now the shape the next lesson's MQTT status handler will rely on when it writes `RecordSeen` concurrently with queries. Contract v2 is untouched, protected files are untouched, and no later-M5 scope has leaked in. Residual risk is cosmetic only (trailing whitespace, one optional intermediate assertion); nothing here corrupts fleet state.

---

## Note for the professor (lesson/hints defect — still open)

1. **The hints addendum line map is still stale.** `docs/lessons/13-heartbeats-and-timeouts-hints.md:153-157` still says "The method **already** does all of this, in order: `:79` reads `Shuttle:HeartbeatIntervalMs`; `:102-103` throws …; `:104` `using var stopping`…". That is false for the starting state (`HEAD:src/ShuttleSim/Program.cs` has no heartbeat config, no CTS, and `return 0` at `:118`). The numbers match neither the pre-change nor the current file. This is very likely what led the owner to drop the whole heartbeat block below the Ctrl+C wait in the first place. Rewrite it to describe the starting state accurately and say plainly which lines go before the wait and which after.
2. **The endpoint mutation claim is now accurate.** The lesson (line 578) says hard-coding `idle` in `PublishStatusAsync` turns `Heartbeat_publishes_the_current_status_without_a_command` red "when the shuttle is not idle". The owner changed the fixture to move the shuttle before publishing, so the claim now holds (verified). No lesson edit strictly needed, but the addendum could note that the test fixture must be non-idle for the mutation to bite.

Otherwise the lesson held to the three-concept cap (`heartbeat`, `lastSeenAt`, timeout detection) and covered every item in "Concepts this assignment requires".
