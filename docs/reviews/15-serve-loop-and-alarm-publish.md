# Review 15 — Serve loop and alarm publish (M5, lesson 15)

**Reviewer:** Reviewer agent
**Verdict: pass**

## Gate results

1. `cd src && dotnet build` — exit 0, 0 warnings, 0 errors.
2. Pure suite (no database, no broker):
   ```
   cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
   ```
   RC 0, 8 `Passed!`, 0 `Failed!`, 109 tests total:
   `ShuttleCore.Tests` 25, `FleetCore.Tests` 18, `TrafficCore.Tests` 6,
   `Contracts.Tests` 7, `Messaging.Tests` 9, `MqttMessaging.Tests` 2,
   `ShuttleSim.Tests` 6, `FleetManager.Tests` 36.
   Requested baseline was 108 / `FleetManager.Tests` 35. The extra test is
   `FleetServeLoopTests.Sweep_uses_the_injected_clock_not_UtcNow_for_the_liveness_check`
   added to close mutation (i) (below). The lesson’s printed count (108/35) is
   now stale; that is a lesson-document mismatch, not a student failure.
3. Phase 7b anchored-name walk passed: all 10 pinned names found
   (`The_constructor_rejects_a_blank_fleet_manager_id_and_a_non_positive_interval`,
   `Subscribe_uses_the_all_statuses_filter_and_the_status_family`,
   `A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender`,
   `A_latched_lost_shuttle_publishes_nothing_again`,
   `A_recovered_shuttle_clears_the_latch_and_can_raise_again`,
   `An_invalid_status_message_is_ignored`,
   `Two_shuttles_are_evaluated_independently`,
   `TopicScheme_alarm_topics_use_the_per_shuttle_branch_and_the_wildcard`,
   `An_alarm_reaches_the_broker_on_the_shuttle_topic`,
   `Sweep_uses_the_injected_clock_not_UtcNow_for_the_liveness_check`).
4. Phases 1–6, 8, 9 passed:
   - Files, names, members present (`FleetServeLoop`, `IMessageBus`, `Func<DateTime>`,
     `SubscribeAsync`, `RunAsync`, `HandleStatusAsync`, `EvaluateAndPublishAsync`).
   - Alarm lane present in `MessageFamily`, `TopicScheme`, `QosOptions`, and the bus
     switch.
   - Configuration carries `"Alarm": 1` in both apps and
     `AlarmEvaluationIntervalMs` in the manager.
   - `serve` command reads `LivenessTimeoutMs`, `AlarmEvaluationIntervalMs`,
     `LostCommunicationSeverity`, and builds `FleetServeLoop`.
   - `FleetServeLoop.cs` contains no `DateTime.UtcNow`, no MQTT/Npgsql/Postgres
     reference, has `private readonly object _knownLock`, and uses
     `lock (_knownLock)`.
   - `FleetManager.Tests.csproj` references `MqttMessaging`; `RecordingBus` has its
     own (non-colliding) name; integration trait present.
   - No Docker reference in test code.
   - Protected files (`Contracts`, `docs/contract`, `ShuttleHealth`, `AlarmEvaluator`,
     `RunLoop`, cores, Dockerfile) unchanged; contract still v3 and no v4.
5. Real-broker integration check run with broker and database up:
   `dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"` —
   RC 0, 3 `Passed!`, 0 `Failed!`. The new
   `An_alarm_reaches_the_broker_on_the_shuttle_topic` received one
   `AlarmMessage` whose `Alarm.Code` was `AlarmCodes.LostCommunication`. Broker and
   database stopped afterward.

## Acceptance criteria

- [x] `FleetServeLoop` exists with the constructor shape and validation rules from
      record 22 §8. Evidence: `src/FleetManager/FleetServeLoop.cs`; pure tests pass.
- [x] `SubscribeAsync` subscribes to `AllStatuses` with `MessageFamily.Status`.
      Evidence: `Subscribe_uses_the_all_statuses_filter_and_the_status_family` passes.
- [x] `RunAsync` drives `EvaluateAndPublishAsync(_clock(), ...)` on a `PeriodicTimer`.
      Evidence: code present and pure suites green.
- [x] `HandleStatusAsync` accepts only current-version `shuttleStatus` envelopes with
      non-blank id/MessageId and ignores everything else, without throwing.
      Evidence: `An_invalid_status_message_is_ignored` passes.
- [x] Sweep snapshots the known set under the lock, then evaluates it without holding
      the lock across publishing. Evidence: code + all pure tests pass.
- [x] A lost shuttle publishes one `AlarmMessage` on `shuttle/SH-01/alarm` with
      `MessageFamily.Alarm`, configured sender, `CorrelationId == null`,
      `SentAt == now`, and `AlarmCodes.LostCommunication`. Evidence:
      `A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender`
      passes.
- [x] A second sweep while latched publishes nothing. Evidence:
      `A_latched_lost_shuttle_publishes_nothing_again` passes.
- [x] A recovered shuttle clears the latch and can raise again. Evidence:
      `A_recovered_shuttle_clears_the_latch_and_can_raise_again` passes.
- [x] Two shuttles are reported independently. Evidence:
      `Two_shuttles_are_evaluated_independently` passes.
- [x] The clock used for both the liveness check and the envelope is the injected
      clock. Evidence: `Sweep_uses_the_injected_clock_not_UtcNow_for_the_liveness_check`
      passes.
- [x] Transport lane added: `MessageFamily.Alarm`, `TopicScheme.Alarm` /
      `AllAlarms`, `QosOptions.Alarm`, and the bus switch arm.
      Evidence: phase 2 grep checks and `TopicScheme_alarm_topics_use_the_per_shuttle_branch_and_the_wildcard`.
- [x] Both `appsettings.json` files include `"Alarm": 1`, and the manager includes
      `AlarmEvaluationIntervalMs`. Evidence: phase 3 grep checks.
- [x] `serve` command wires and runs the loop until Ctrl+C with the
      `ShuttleSim/Program.cs` cancellation pattern. Evidence: code review of
      `RunServeAsync`.
- [x] `FleetManager.Tests` can compile against `MqttMessaging`. Evidence: csproj
      change and all suites green.
- [x] Integration (real bus): an alarm reaches the broker on the shuttle topic.
      Evidence: `An_alarm_reaches_the_broker_on_the_shuttle_topic` passed.
- [x] Contract untouched at v3; no `v4.md`; protected files unchanged.
      Evidence: phase 9 grep / `git diff --quiet`.

## Top issues (max 5), ranked

1. **consider** — The evaluator’s liveness input is the manager’s own clock, not
   anything the shuttle report carries: `HandleStatusAsync` calls
   `_health.RecordSeen(shuttleId, _clock())`. That is exactly the scope lesson 13
   picked (record 20 §6) and the spec’s “the sim has no real network” caveat
   still stands: a status that arrives late but is still accepted re-arms the
   shuttle. It is not a bug in this slice, but the two-clock domain seam is real
   and is called out in record 22 §10.
2. **consider** — The known-shuttle set and the alarm latch are in memory only.
   A manager restart forgets who it has heard from, so an already-lost shuttle is
   not reported until it is heard from again. This matches the recorded scope
   boundary in record 22 §3/§10, but it means “saved state never loses or
   duplicates a pallet” (M5 done-when) is not yet proven for the alarm path.
   Persistence of the set is a later slice; do not let operator-facing messaging
   imply otherwise.
3. **consider** — The integration test asserts only
   `Alarm.Code == AlarmCodes.LostCommunication`. Topic, family, and envelope fields
   are pinned by `TopicScheme_alarm_topics_use_...` and by
   `A_lost_shuttle_publishes_one_alarm_with_the_tick_clock_and_configured_sender`, so
   the wire path is covered in aggregate, but a wider end-to-end assertion would
   fail sooner if the serialization settings drift. Low risk.
4. **consider** — `HandleStatusAsync` catches only `JsonException`. A payload that
   is valid JSON but has an unexpected runtime deserialization shape would
   propagate to the bus callback. The lesson explicitly specifies the
   `JsonException` boundary, so this is by design, but it is worth revisiting if
   the dashboard ever sends non-status traffic on the `+/status` filter.
5. **consider** — `FleetServeLoopTests.cs` ends without a trailing newline and
   keeps a duplicate `Assert.Equal(2, bus.Published.Count);` line in the recovered-shuttle
   test. No functional effect; a cleanup commit on the test file is enough.

No blockers; no should-fix items.

## Explain-it check

Answer these out loud or in writing before lesson 16. They are not answered here.

1. `FleetServeLoop` depends on `IMessageBus` and takes `Func<DateTime> _clock`, even
   though the production constructor passes `() => DateTime.UtcNow`. Why inject
   both instead of calling the bus and `DateTime` directly inside the loop? What
   test would become impossible or slower if you bound the loop to
   `MqttMessageBus`?
2. `EvaluateAndPublishAsync` copies `_knownShuttles` into an array under
   `_knownLock`, then performs the evaluation and the publishes with the lock
   released. Why not evaluate inside the lock? What would happen if `PublishAsync`
   ran while holding `_knownLock`?
3. In `HandleStatusAsync`, `RecordSeen` uses `_clock()` while the envelope in a real
   status carries `SentAt`. Why does the alarm path prefer the manager clock over
   the shuttle clock, and how does that interact with the “two clocks” seam called
   out in the lecture?

Also study these specific lines/blocks; they were flagged as unclear, likely copied,
or easy to gloss over:

- `src/FleetManager/FleetServeLoop.cs` lines 17–28, the constructor assignment that
  uses `evaluationInterval > TimeSpan.Zero ? evaluationInterval : throw new …`
  (conditional throw in an initializer) — understand the exception argument and
  when it is actually evaluated.
- `src/FleetManager/FleetServeLoop.cs`, `HandleStatusAsync`: the
  `status?.Envelope is not { } envelope` pattern test, plus the ordering that
  `Envelope` is checked before `Status`. Ask which field the branch keys on.
- `src/FleetManager/FleetServeLoop.cs`, `EvaluateAndPublishAsync`: the
  `lock (_knownLock) { ids = _knownShuttles.ToArray(); }` block and why it must be
  a copy rather than the set itself.
- `src/FleetManager.Tests/FleetServeLoopTests.cs`, the `RecordingBus` fake’s stored
  `Handler` delegate and how tests use it to feed statuses in without a timer or
  broker.
- `src/FleetManager/Program.cs`, `RunServeAsync`: the `Console.CancelKeyPress`
  handler, the `TaskCompletionSource`, and the `catch
  (OperationCanceledException) when (stopping.IsCancellationRequested)` filter.
- `src/FleetManager.Tests/FleetServeLoopTests.cs` (`An_invalid_status_message_is_ignored`
  plus the `Sweep_uses_the_injected_clock_…` test): the extra clock-domain case
  and the reason it exists in addition to the original seven tests.

## Revisit list

- `PeriodicTimer` lifetime and cancellation semantics in `RunAsync`.
- One-lock-per-object discipline and the snapshot-then-work-outside-the-lock pattern.
- System.Text.Json `is not { }` pattern matching for null/shape validation.
- Composition roots and when production code may call `DateTime.UtcNow`.
- MQTT topic filters, single-level wildcards, and QoS levels per family.
- The two-clock seam (simulated `RunLoop` time vs. wall `DateTime.UtcNow`) and
  why it matters for liveness.

## End-product check

This slice completes the “lost communication” alarm half of M5 end to end: a
heartbeat stops, the manager notices on its next configured cadence, and a
version-3 `AlarmMessage` leaves on `shuttle/{id}/alarm` at QoS 1 for the M7
dashboard to consume. It introduces no new contract kind, so the dashboard,
shuttle, and WMS paths are not disturbed. The main forward risk is that the
known-shuttle set and the alarm latch live in this one process only; a manager
restart, a shuttle that never spoke, or a clock-domain mistake between the run loop
and the serve loop can delay or drop an alarm without any visible failure. Those
limits are written down in records 20 and 22; the recovery slice must decide
persistence, correlation, and single-clock handling deliberately rather than
smuggling them into this loop.

## Mutation results

All nine listed mutations were run in throwaway copies under `/tmp/opencode`; each
turned at least one targeted test red. Key fixes verified: (d) hard-coded sender
now fails because the test uses a distinct fixture id (`fm-test-01`); (g)
invalid-status variants now fail when version/MessageType/blank-id/blank-version
checks are removed or bypassed; (i) the clock-domain sweep test now fails when
`EvaluateAndPublishAsync` reads `DateTime.UtcNow` instead of `now`.

| Mutation | Result |
|---|---|
| (a) `PublishAsync` family `Alarm` → `Status` | red: `A_lost_…_configured_sender` |
| (b) `_topics.Alarm(id)` → `_topics.Status(id)` | red: `A_lost_…_configured_sender` |
| (c) `sentAt` → `DateTime.UtcNow` in `PublishAlarmAsync` | red: `A_lost_…_configured_sender` |
| (d) `_fleetManagerId` → hard-coded `"fleet-manager"` | red: `A_lost_…_configured_sender` |
| (e) remove `_knownShuttles.Add(...)` | red: 4 tests (lost, latched, recovered, two-shuttle); invalid-status and clock-domain stayed green |
| (f) iterate `_health.UnresponsiveShuttles(now)` instead of the known set | red: `A_recovered_…_can_raise_again` |
| (g) accept any contract version in `HandleStatusAsync` | red: `An_invalid_status_message_is_ignored` |
| (h) subscribe family `Status` → `Alarm` | red: `Subscribe_uses_the_all_statuses_filter_and_the_status_family` |
| (i) `EvaluateAndPublishAsync` uses `DateTime.UtcNow` instead of `now` | red: 3 tests (`A_lost_…`, `A_recovered_…`, `Sweep_uses_the_injected_…`) |
""", filePath="/home/jamie/shuttle-sim/docs/reviews/15-serve-loop-and-alarm-publish.md")