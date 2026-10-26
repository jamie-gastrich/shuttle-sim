# Review 28: Mock WMS reliability

**Verdict:** pass

**Reviewer:** beam-free (reviewer)
**Date:** 2026-10-26
**Build command:** `cd src && dotnet build --no-incremental`
**Test command:** `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"`

## Build and test result

- `dotnet build --no-incremental` on the current (uncommitted) tree: **PASS**, 0 warnings, 0 errors (17.1 s). `MockWms` and `MockWms.Tests` now build.
- `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"`: **PASS**, 213 tests, 0 failed — MockWms.Tests 9, FleetCore.Tests 63, FleetManager.Tests 73, ShuttleCore.Tests 31, TrafficCore.Tests 6, Contracts.Tests 8, Messaging.Tests 10, MqttMessaging.Tests 3, ShuttleSim.Tests 10.
- The `using MockWms;` line is present at the top of `src/MockWms/Program.cs`; the blocker from the first review is gone. The `int.MaxValue` guard in `ScheduleRetry` is gone (now plain `previous + 1, or 1`), retry log lines now include the computed delay, and the README documents `hang` as client-cancelled.
- Manual smoke test (mock on 5090): duplicate push returned `200`, `200` with `applied=1, duplicate=1`; `error` mode returned `500` without applying; invalid body `400`; conflict `409`; read routes `200`/`404`; `slow` applied then delayed; `hang` did not apply and the client timed out; `POST /mock/mode` with a missing `slowDelayMs` kept the current delay; reset cleared ledger and counters while keeping the mode. Mock process was stopped and port 5090 is free.

## Acceptance criteria

| # | Criterion | Result | Evidence |
|---|---|---|---|
| 1 | `cd src && dotnet build --no-incremental` | PASS | 0 warnings, 0 errors |
| 2 | `cd src && dotnet test ... --filter "Category!=Integration"` | PASS | 213 passed, 0 failed |
| 3 | `test -f src/FleetCore/WmsDeliveryOutcome.cs` | PASS | file present |
| 4 | `test -f src/FleetCore/WmsRetryPolicy.cs` | PASS | file present |
| 5 | `test -f src/FleetCore/WmsDeliveryFailures.cs` | PASS | file present |
| 6 | `test -f src/MockWms/MockWmsLedger.cs` | PASS | file present |
| 7 | `test -f src/MockWms/Program.cs` | PASS | file present |
| 8 | `test -f src/MockWms.Tests/MockWmsLedgerTests.cs` | PASS | file present |
| 9 | `grep -q "MockWms" src/ShuttleFleet.slnx` | PASS | lines 9-10 |
| 10 | `grep -q "MaxRetryDelayMs" src/FleetManager/appsettings.json` | PASS | line 36 |
| 11 | `grep -q "WmsDeliveryOutcome.FromStatus" src/FleetManager/WmsStatusPusher.cs` | PASS | line 107 |
| 12 | `grep -q "DelayFor" src/FleetManager/WmsStatusPusher.cs` | PASS | line 131 |
| 13 | `grep -q "WmsRoutes.DeliveryFailures" src/FleetManager/Program.cs` | PASS | line 311 |
| 14 | `grep -q "WmsDeliveryFailures" src/FleetManager/Program.cs` | PASS | lines 176, 259 |
| 15 | `test -z "$(git diff ... src/Contracts docs/contract)"` | PASS | no diff (MQTT 4, WMS REST 2) |
| 16 | All 20 R11 test names present and passing | PASS | `--list-tests` shows all 20 base names (one `[Theory]` expands to 2) |
| 17 | Manual smoke test (duplicate, error, invalid, slow, hang, conflict, read, reset) | PASS | see Build and test result |

## Top issues (max 5)

1. **[should-fix, process] Owner sign-off line is still not in the required format.** `docs/builds/28-mock-wms-reliability-report.md` ends with `Owner sign-off: pending owner review; add the date and "explained and verified" after completing the review gate.` `AGENTS.md` requires `Owner sign-off: <date>, explained and verified`. The gate exists to protect the "explain every line" goal; please replace the pending line with the dated, verified token before committing. This is a process item, not a code defect, and does not block the code verdict.
2. **[consider] `WmsRetryPolicy.DelayFor` is harder to explain than it needs to be.** `src/FleetCore/WmsRetryPolicy.cs:24-32` uses a `delay < _maxDelay` loop with a `delay.Ticks > _maxDelay.Ticks / 2` branch to clamp. It is equivalent to the decision record's `Math.Min(delay.Ticks * 2, _maxDelay.Ticks)` form for all inputs the constructor allows, and the tests pin the same sequence (`1000, 2000, 4000, 8000, 16000, 30000`). The half-max branch is a clever optimisation that an interviewer will ask about; simplifying it to the record's form makes the doubling-and-cap rule readable in one line. Not required for correctness.
3. **[consider] `MockWmsLedger.Apply` throws on an unknown state.** `src/MockWms/MockWmsLedger.cs:62` calls `Rank(order.State)`, whose `_ => throw` arm fires for anything outside the five known words. The route validates the state first, so this only affects direct callers (tests or a future non-validated caller), but a malformed state then surfaces as an unhandled 500 instead of a 400. If the mock ever accepts input that bypasses `TryCreate`, add a guard or return a result. Low risk today.
4. **[consider] Empty `src/MockWms/Properties/` directory.** The launch-profile gotcha was handled (the folder is empty, so no random port overrides `appsettings.json`), but an empty directory is left behind. Tidy it up; no functional effect.

## What is solid

- The three pure pieces remain clean and I/O-free: `WmsDeliveryOutcome.FromStatus`, `WmsRetryPolicy.DelayFor`, and `WmsDeliveryFailures` have no clock, no `HttpClient`, and no logging, and the `int?` "no reply" input keeps network exceptions out of the rule.
- The mock is a genuinely separate process with its own `MockOrder` type and its own rank table; it does not reference `FleetCore` or `Contracts`, so a JSON field-name mismatch surfaces as a failing test instead of being hidden by a shared type.
- The ledger's lock makes check-and-write one step, and the identity check runs before the rank check, so a `done` followed by a `failed` for the same order stays `done`. `Cancelled_is_above_done` and `A_done_word_is_not_replaced_by_failed` pin this.
- The pusher keeps the old four-argument constructor working by delegating with `new WmsRetryPolicy(interval, interval)` and a private board, so existing call sites keep "try again next pass" while the new behaviour is testable with a fake clock.
- Configuration is validated at startup (`RetryBaseDelayMs > 0`, `MaxRetryDelayMs >= RetryBaseDelayMs`, `MockWms:SlowDelayMs > 0`), and no URL, delay, or port is hard-coded in the pusher or the mock's routes. `/mock/*` is documented as test-only control surface, and `docs/contract/` and `src/Contracts/` are untouched (MQTT 4, WMS REST 2).

## Explain-it check

An interviewer for this codebase would most likely ask:

1. **"In `WmsStatusPusher.SendPendingAsync`, an order that is 'not due' is put back in the outbox and skipped. Why is the due-time check keyed on `orderId` rather than on the HTTP request, and what happens to the wait if the order's word changes from `in_progress` to `done` while the WMS is down?"** (See `src/FleetManager/WmsStatusPusher.cs:51-65`, `95-140`.)
2. **"In `MockWmsLedger.Apply`, the identity check compares `kind`, `palletId`, and `laneId` before checking rank. Why must identity be checked first, and what would go wrong if a later `done` word for the same order arrived with a different pallet id?"** (See `src/MockWms/MockWmsLedger.cs:24-46`.)
3. **"In `WmsRetryPolicy.DelayFor`, the loop clamps at the max delay using a half-max comparison. Walk me through why the result for failure counts 1..6 is 1000, 2000, 4000, 8000, 16000, 30000 ms, and what changes if the base delay is larger than half the cap."** (See `src/FleetCore/WmsRetryPolicy.cs:19-33`.)

Lines and blocks that look unclear, copied, or unexplained, to study before the next lesson:

- `src/FleetCore/WmsRetryPolicy.cs:24-32` — the `delay.Ticks > _maxDelay.Ticks / 2` branch; explain why it is not just `Math.Min(delay.Ticks * 2, _maxDelay.Ticks)`, or simplify it (top issue 2).
- `src/MockWms/MockWmsControl.cs:45` — `if (request.SlowDelayMs is <= 0)`; explain how the pattern matches `null` versus `0`.
- `src/MockWms/Program.cs:19-23` — `await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)` in `hang` mode; explain what cancels it and when.
- `src/FleetManager/WmsStatusPusher.cs:73` — `ApplyOutcome(orderId, statusCode, now, out var retryDelay)` where `statusCode` is `int` but the method takes `int?`; explain why the implicit conversion is safe in that branch.
- `docs/builds/28-mock-wms-reliability-report.md` — the `Owner sign-off: pending owner review...` line; replace with the dated `Owner sign-off: <date>, explained and verified` token (top issue 1).

## Revisit list

- C# top-level statements and namespace visibility: a top-level `Program.cs` needs `using MockWms;` when the types live in a namespace; the missing import was the first review's blocker.
- Exponential backoff with a ceiling: why doubling, why a cap, why reset on success; be able to derive `1000, 2000, 4000, 8000, 16000, 30000` from base 1000 and cap 30000.
- Idempotent receivers: key by business identity (order id), not by request id; classify apply as applied / duplicate / stale / conflict; never let a late old word move state backward.
- Delivery outcomes: the difference between a `5xx` (retry) and a `4xx` (usually permanent), and why "no reply" is a value (`null`) rather than an exception.
- Per-owner state in a single-threaded loop: why the retry dictionary needs no lock, and why the outbox keeps its own lock.
- Honest limits of in-memory state: what a restart forgets (backoff, failure board) and what survives (order board, task board).

## End-product check

This slice closes the gap M6 left open: the fleet manager now has a real receiver on port 5090, reads what it says, and acts on it — delivered words are retired, retryable failures back off per order, and permanent `4xx` replies are recorded on a board the M7 dashboard can read at `GET /wms/delivery-failures`. The idempotent ledger is what makes the lost-reply case safe: the WMS applies once, the fleet retries, and the ledger sees a duplicate instead of a second pallet. The main risk carried forward is that the retry state and the failure board are in memory, so a restart forgets permanent failures and resets the backoff; that is documented and acceptable for M6, but the diagnostics slice must persist them before operators can trust the failure board across restarts. M6 stays `in progress`: the cancel slice (lesson 27) is not yet in the tree, and its fleet-side route is still missing, so the milestone's "can cancel orders" promise is not yet met. The mock already accepts `cancelled` at rank 3 so it is ready when that slice lands.

## Lesson check

- Concepts taught: 3 (delivery outcomes, the idempotent receiver, backoff with a ceiling) — within the cap of 3.
- All three are covered in the lecture sections and the worked example, and each maps to a requirement (R5/R7, R2/R3, R7/R8). No concept was required but not taught.
- The lesson's verification table measured the baseline correctly (190 pure tests, no listener on 5090, no MockWms project) and the acceptance commands are runnable. The first review found the only inaccuracy was in the build report, not the lesson; the code now builds and tests pass as the report claims.
- The lesson's "not in this lesson" list was respected: no change to `src/Contracts/`, `docs/contract/`, `TaskState`, `WmsOrderLadder`, or the cancel route; MQTT stays 4 and WMS REST stays 2. Verified by diff.