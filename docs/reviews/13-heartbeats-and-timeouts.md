# Review 13: Heartbeats, `lastSeenAt`, and the timeout window (lesson 13, M5 first slice)

**Date:** 2026-10-13
**Review of:** the owner's working tree, which is uncommitted. `git status` shows `M src/ShuttleSim.Tests/ShuttleEndpointTests.cs`, `M src/ShuttleSim/Program.cs`, `M src/ShuttleSim/ShuttleEndpoint.cs`, and untracked `src/FleetManager/ShuttleHealth.cs`, `src/ShuttleSim/HeartbeatPublisher.cs`, `src/FleetManager.Tests/ShuttleHealthTests.cs`. HEAD is `9cd8b22`. This review modified no file under `src/`. No broker, container, or `dotnet run` was started; no such process is left running.
**Scope:** lesson 13 against its own assignment and acceptance criteria, [record 20](../decisions/20-liveness-heartbeats-and-timeouts.md), and the end-product spec's M5 line.
**Contract checked:** `docs/contract/v2.md` is still `Version: 2`; no `v3.md`. No wire change.

**Verdict: not yet**

The shape is mostly right — the two files exist with the pinned names, `HeartbeatPublisher` is clean, the endpoint method is correct, the contract is untouched, and protected files are unchanged. But the build-and-run gate fails (one test red), the core monotonic rule is missing from `ShuttleHealth`, both required config keys are absent, and the heartbeat is wired so that it can never actually publish. Four blockers. Fix them and answer the explain-it questions.

---

## Acceptance criteria check

Run from the repo root unless a phase says `src/`. Verdict evidence is from a clean `dotnet build` and the exact suite command in the lesson.

| # | Requirement | Result | Evidence |
|---|---|---|---|
| 1 | `ShuttleHealth` exists with pinned members | **partial** | Phase 1: class and all four members present (`RecordSeen`, `LastSeenAt`, `IsUnresponsive`, `UnresponsiveShuttles`). Constructor takes `TimeSpan`, rejects non-positive. |
| 1a | `RecordSeen` is monotonic (never moves backwards) | **fail** | `ShuttleHealth.cs:26` assigns unconditionally: `_lastSeenAt[shuttleId] = seenAt;`. Scratch harness compiling the owner's file: `RecordSeen(t0)`, `RecordSeen(t0+60s)`, `RecordSeen(t0+5s)` → `LastSeenAt` = `t0+5s` (expected `t0+60s`). |
| 1b | Blank `shuttleId` throws `ArgumentException` | pass | Validated in `LastSeenAt`, `RecordSeen`, `IsUnresponsive`, consistent with `OccupancyMap`. |
| 1c | `IsUnresponsive` boundary: exactly the window is responsive | pass | Scratch harness: exact window → `False`, one second past → `True`. Comparison is `>` at `ShuttleHealth.cs:54`. |
| 1d | `UnresponsiveShuttles` returns a snapshot | pass | Returns `...ToList()` at `ShuttleHealth.cs:63`; scratch harness returned `[SH-02]`. |
| 1e | No `DateTime.UtcNow`/I/O/contract types in the pure file | pass | Phase 2 greps printed nothing. |
| 2 | `HeartbeatPublisher` uses `PeriodicTimer` + `CancellationToken`, no blocking | pass | `HeartbeatPublisher.cs:16-18`. Phase 4: `PeriodicTimer` and `CancellationToken` present; no `Thread.Sleep`/`.Result`/`.Wait()`/`async void`. |
| 3 | `ShuttleEndpoint.PublishStatusAsync(DateTime, CancellationToken)` publishes current state | pass | `ShuttleEndpoint.cs:82-83` delegates to the existing private path. Existing `moving`/`idle` path at `:75`,`:79` unchanged. Test passes. |
| 4 | `Shuttle:HeartbeatIntervalMs` read and validated in `Program.cs` | **partial** | Code reads and validates it (`Program.cs:118-121`) but see issue 4: it is read *after* shutdown. |
| 4a | Heartbeat started before the run and stopped cleanly on Ctrl+C | **fail** | `Program.cs:118-124` builds and starts the publisher *after* `await stopped.Task` (`:111`), then `stopping.Cancel()` at `:126` runs immediately. The publisher never reaches a tick. |
| 5 | `Shuttle:HeartbeatIntervalMs` in `ShuttleSim/appsettings.json` | **fail** | Phase 4: key missing. Only `Shuttle:Id`/`StartLane` are present. |
| 5a | `FleetManager:LivenessTimeoutMs` in `FleetManager/appsettings.json` | **fail** | Phase 4: key missing. |
| 6 | Five pinned `ShuttleHealthTests` exist | **partial** | All five names present. `Silence_past_the_timeout_marks_a_shuttle_unresponsive` fails. `A_new_heartbeat_makes_a_silent_shuttle_responsive_again` does not test monotonicity as required. |
| 7 | `Heartbeat_publishes_the_current_status_without_a_command` present and pure | pass | Present; uses `FakeBus`, asserts one `Status`, `idle`, `A01-L01`, timestamp `Start`. |
| 8 | Contract stays v2 | pass | Phase 7: `Version: 2`, no `v3.md`, three kinds intact. |
| 9 | Protected files unchanged | pass | Phase 7: `TaskDispatcher.cs`, `FleetManager/Program.cs`, `TrafficCore`, `ShuttleCore`, `Contracts`, `docs/contract`, `docker/Dockerfile.build` all unchanged. |
| 10 | No M5 later items / M6 / M7 scope creep | pass | Greps found only pre-existing `AcknowledgeAsync` (M2 ack) in the changed files. No alarms, recovery, reassignment, persistence. |
| — | Build | pass | `dotnet build` exit 0, 0 warnings, 0 errors. |
| — | Pure suite gate (`RC 0`, 8 `Passed!`, 0 `Failed!`, 90 tests, `FleetManager.Tests=21`, `ShuttleSim.Tests=6`) | **fail** | `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → RC **1**, **7** `Passed!`, **1** `Failed!`, **89** tests. `FleetManager.Tests` 20/21 (1 failed); `ShuttleSim.Tests` 6/6. |

**Observed failure:**

```
Failed FleetManager.ShuttleHealthTests.Silence_past_the_timeout_marks_a_shuttle_unresponsive
  Assert.True() Failure
  Expected: True
  Actual:   False
```

---

## Top issues (max 5), ranked

### 1. blocker — `RecordSeen` is not monotonic; the whole point of `lastSeenAt` is defeated
`ShuttleHealth.cs:26` overwrites unconditionally. The assignment (requirement 1) and record 20 §3 are explicit: store `seenAt` only if it is the first stamp or newer than the stored one. This is not a style point. In a real system, MQTT "at least once" delivery and retained status both replay old messages. With this code, a duplicate or a retained status arriving after a fresh heartbeat moves `lastSeenAt` backwards, and a live shuttle is then reported unresponsive. In later M5 slices that false "lost" verdict feeds the alarm and record 18's closest-eligible dispatch, so a healthy shuttle could be flagged and skipped — exactly the operator-trust failure M5 exists to prevent. The required test that pins this (`A_new_heartbeat_makes_a_silent_shuttle_responsive_again` must also record an *older* stamp after a newer one and assert `LastSeenAt` did not move backwards) is not written that way: it only records `now`, `now+60`, `now+75` — always increasing — and never asserts `LastSeenAt`. So the mutation the lesson names (remove the monotonic guard → test red) cannot even run, because there is no guard and no assertion.

### 2. blocker — a named test is written wrong and fails, so the build-and-run gate is red
`Silence_past_the_timeout_marks_a_shuttle_unresponsive` (`ShuttleHealthTests.cs:21-32`) calls `RecordSeen(SH-01, now)` then `RecordSeen(SH-01, now+60s)`, then checks `IsUnresponsive(SH-01, now+60s)`. The second `RecordSeen` does not simulate silence — it sets the last-seen stamp *to the moment being queried*, so `now - lastSeen = 0`, and the assertion `True` fails. The implementation is correct here (scratch harness: past-window → `True`); the test is simply not testing what its name says. It should record one heartbeat at `now` and query at `now+31s` for a 30 s window. A test that fails is an automatic "not yet"; a test that tests the wrong thing would be worse if it passed.

### 3. blocker — both configuration keys are missing
`Shuttle:HeartbeatIntervalMs` is absent from `src/ShuttleSim/appsettings.json`, and `FleetManager:LivenessTimeoutMs` is absent from `src/FleetManager/appsettings.json`. Phase 4 fails on both. `Program.cs:118` reads a key that does not exist, so `GetValue<int>` returns `0` and the app throws `InvalidOperationException` at startup every time. AGENTS.md forbids hard-coded timeouts; a value that is never written down is not configuration. Add both (positive integers, e.g. `1000` and `3000`).

### 4. blocker — the heartbeat is started after the Ctrl+C wait, so it never publishes
In `RunServerAsync`, the config read, the `CancellationTokenSource`, and `heartbeat.RunAsync(...)` are all placed *after* `await stopped.Task` (`Program.cs:111`), and `stopping.Cancel()` (`:126`) runs immediately after the task is created. Trace it: `RunAsync` runs synchronously to the first `await timer.WaitForNextTickAsync(...)`, which parks; then `Cancel()` is called before any interval elapses, so no tick ever fires. Over the wire, a running shuttle emits nothing — the liveness feature is inert. The hints addendum is explicit: the CTS and the publisher start *before* the wait, and only `stopping.Cancel(); await heartbeatTask;` belongs after the `try/finally` (hence the heartbeat block currently sits too low by the whole wait). The `OperationCanceledException` catch with the `stopping.IsCancellationRequested` filter is correct and should stay — but it should wrap a task that actually ran.

### 5. should-fix — `ShuttleHealth` re-enters its own lock, and the pure tests drag in `ShuttleCore` for an id
`IsUnresponsive` (`:43`) locks `_lock`, then calls `LastSeenAt` (`:53`), which locks `_lock` again; `UnresponsiveShuttles` (`:58`) locks and calls `IsUnresponsive`, locking a third time. `Monitor` is reentrant so it works today, but it means the "one atomic read" is split across nested public methods and every query pays redundant id validation. Read the dictionary directly inside each locked method instead of calling the sibling. Separately, `ShuttleHealthTests.cs` constructs a full `Shuttle`/`Rack`/`ShuttleTiming` (lines 10-11, repeated five times) only to obtain the string `"SH-01"`. That couples a pure liveness test to `ShuttleCore` and hides what is being tested; use a plain string constant. Minor: trailing whitespace on `Program.cs:117` and `:125`, and the publisher field is assigned after the interval check (order does not matter, but the pinned shape validates the endpoint first).

---

## What is solid

- **`HeartbeatPublisher` is exactly the pinned shape.** `PeriodicTimer`, `using`, `CancellationToken` passed through, `clock()` injected for testability, constructor validates both arguments, no `Thread.Sleep`/`.Result`/`.Wait()`/`async void`. Record 20 §5 followed.
- **`ShuttleEndpoint` change is minimal and correct.** One new public method delegating to the existing private publish path; the `moving`/`idle` command path is byte-for-byte unchanged, so no regression there.
- **`IsUnresponsive` is pure and carries `now`.** The boundary comparison is `>` (exactly the window is responsive), confirmed by direct execution. This is the hardest detail in the lesson and it is right.
- **`UnresponsiveShuttles` returns a snapshot**, as the next slice needs.
- **Discipline held.** `TaskDispatcher.cs` untouched, contract still v2 with no `v3.md`, no alarms/recovery/reassignment, no docker in tests, build clean at 0 warnings.
- **The new endpoint test is genuinely load-bearing** on the state it asserts (`idle`/`A01-L01`), not just on message count.

---

## Explain-it check

Answer these out loud or in writing before the next lesson.

1. **Monotonicity.** Record 20 §3 says `RecordSeen` must never move `lastSeenAt` backwards. Name the two concrete ways an older stamp can arrive over MQTT, and explain — in terms of the alarm the next lesson will raise — what goes wrong when `RecordSeen` overwrites a newer stamp with an older one.
2. **Derived versus stored, and the boundary.** Why does `ShuttleHealth` keep one timestamp instead of a boolean `IsUnresponsive` flag, and why is a shuttle silent for *exactly* the window still treated as responsive rather than lost? Where is that boundary pinned in the tests?
3. **Serve-loop shutdown.** Walk through `RunAsync` from `Program.cs:104` onward. Why must the heartbeat be started *before* `await stopped.Task`, and why is `stopping.Cancel()` followed by `await heartbeatTask` rather than just cancelling? Why does the `await` raise `OperationCanceledException`, and why is the `when (stopping.IsCancellationRequested)` filter important?

**Lines/blocks to study (unclear, likely-misread, or copied-looking):**
- `src/FleetManager/ShuttleHealth.cs:17-28` — `RecordSeen` carries the shape comment `// keep the newest stamp; ignore an older one` but no code doing it. Explain what is missing.
- `src/FleetManager.Tests/ShuttleHealthTests.cs:10-11` — why a `Shuttle` and `Rack` are constructed for a test that only needs a string id.
- `src/FleetManager.Tests/ShuttleHealthTests.cs:28-31` — re-read this test line by line; state what `now - lastSeen` equals.
- `src/ShuttleSim/Program.cs:104-136` — the ordering of start, wait, cancel, await. Mark which line belongs where.

---

## Revisit list

- **Monotonic reads of shared state.** The `EnsureInOrder` guard pattern in `FleetTask`/`Shuttle` is the same idea record 20 §3 points at; read it again and apply it here.
- **Test names must earn their names.** `Silence_past_the_timeout...` and `A_new_heartbeat_makes_a_silent_shuttle_responsive_again` currently overpromise. For each, ask "would this go red if the behavior it names were broken?" and run the mutation in a scratch copy.
- **Async lifecycle and cancellation.** Start the long-running task, keep its `Task`, signal, await, catch the expected cancellation. Read the hints addendum's "What the two lines do" again.
- **Configuration as a contract.** Every timeout and interval lives in `appsettings.json`; confirm a `dotnet run -- serve` startup reaches "listening" with a positive interval before claiming the slice works.
- **Lock discipline.** One lock, one atomic operation per public method; avoid calling a locking sibling from inside another lock even though `Monitor` allows it.

---

## End-product check

This slice is the *detection* half of M5's "lost communication": a heartbeat producer and a pure rule that turns a stale `lastSeenAt` into "unresponsive". It is a real, carrying-forward piece — the alarm slice, record 18's dispatch eligibility, and M7's "last seen" panel all read exactly these objects. The structure is right and the purity is right; the risk is in the details. A non-monotonic `lastSeenAt` is the single most dangerous bug in this slice: it manufactures false "lost" verdicts from duplicate or retained traffic, which in a warehouse means an operator chases a ghost, and later means a healthy shuttle is skipped for work. The unwired/never-started heartbeat would make the whole feature look implemented while emitting nothing. Both are fixable in a small diff; until then M5's later slices would be built on sand. Contract v2 is untouched, protected files are untouched, and no later-M5 scope has leaked in, so the shape is worth keeping.

---

## What the reviewer is looking for

Concrete, line-level guidance for the re-review. Do not just make the suite green — make the tests prove the behavior.

1. **`src/FleetManager/ShuttleHealth.cs:26`** — replace the unconditional store with a newest-wins store:
   `if (!_lastSeenAt.TryGetValue(shuttleId, out var previous) || seenAt > previous) _lastSeenAt[shuttleId] = seenAt;`
   Keep the id check before the lock (or inside, as now). Then re-read record 20 §3.
2. **`src/FleetManager.Tests/ShuttleHealthTests.cs:21-32`** — fix `Silence_past_the_timeout_marks_a_shuttle_unresponsive`: record exactly one heartbeat at `now`, then assert `IsUnresponsive(id, now.AddSeconds(31))` is `True` for a 30 s window (and you may also add the passing `now.AddSeconds(30)` case, but the exact-boundary test already owns that).
3. **`src/FleetManager.Tests/ShuttleHealthTests.cs:47-60`** — make `A_new_heartbeat_makes_a_silent_shuttle_responsive_again` do what the lesson demands: record `now`, record `now+60s` (now unresponsive), record a *newer* `now+75s`, assert responsive; then record an *older* `now+10s` and assert `LastSeenAt(id) == now+75s` and it is still responsive. Prove it by removing the monotonic guard in a scratch copy and watching this test go red.
4. **`src/ShuttleSim/appsettings.json:11`** — change the `Shuttle` section to include the interval, e.g. `"Shuttle": { "Id": "SH-01", "StartLane": "A01-L01", "HeartbeatIntervalMs": 1000 }`.
5. **`src/FleetManager/appsettings.json:12-17`** — add `"LivenessTimeoutMs": 3000` to the `FleetManager` section.
6. **`src/ShuttleSim/Program.cs:104-136`** — reorder so the publisher is live during the serve:
   - before the `stopped`/`CancelKeyPress` wait: read and validate `Shuttle:HeartbeatIntervalMs`, create `using var stopping = new CancellationTokenSource();`, build `HeartbeatPublisher`, and start `var heartbeatTask = heartbeat.RunAsync(() => DateTime.UtcNow, stopping.Token);`
   - after the `try/finally` that awaits `stopped.Task`: `stopping.Cancel();` then `try { await heartbeatTask; } catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }`.
   Run `dotnet run --project ShuttleSim -- serve`, confirm a heartbeat status is published each interval, press Ctrl+C, and confirm exit code 0 with no process left behind. The `LoggingMessageBus` already prints every publish, so you can see the ticks.
7. **`src/FleetManager.Tests/ShuttleHealthTests.cs`** — drop `using ShuttleCore;` and the `Shuttle`/`Rack` construction; use `const string ShuttleId = "SH-01";`. Re-run the mutation checks the lesson lists (remove monotonic guard → test 3 red; `>` → `>=` → boundary test red; always-responsive → `Silence_past...` and `A_shuttle_never_seen...` red) in a scratch copy, not in `src/`.
8. **Re-run the exact gate:** `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` must be RC 0, 8 `Passed!`, 0 `Failed!`, 90 total, `FleetManager.Tests=21`, `ShuttleSim.Tests=6`. No milestone is updated on a not-yet verdict.

---

## Note for the professor (lesson/hints defect)

The addendum's line map in `13-heartbeats-and-timeouts-hints.md` (lines 153-164) describes the *pre-change* `RunServerAsync` as if it "already" read `Shuttle:HeartbeatIntervalMs` at `:79`, threw at `:102-103`, and created `using var stopping` at `:104`. `git show HEAD:src/ShuttleSim/Program.cs` shows none of that: the pre-change file had no heartbeat config, no CTS, and the wait was already in place. Presenting a post-solution line map as the starting state is very plausibly what led the owner to drop the entire heartbeat block below the Ctrl+C wait instead of splitting it. Worth correcting the addendum to describe the starting state accurately and to say plainly which half goes before the wait and which half after. Everything else in the lesson stayed within the three-concept cap and covered every item in "Concepts this assignment requires".
