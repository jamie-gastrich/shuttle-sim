# Review 15: The serve loop and the alarm publish path (lesson 15, M5 third slice)

**Date:** 2026-10-14
**Review of:** `7c6b4cc M5: Lesson 15 First attempt` (HEAD); earlier commits for this slice `4173496` (lesson) and `5062472` (worked examples + integration test). Clean working tree (`git status --porcelain` empty).
**Scope:** lesson 15's acceptance criteria (phases 1–9, including 7b), [record 22](../decisions/22-serve-loop-and-alarm-publish.md), record 21 (v3 `alarm`), and the end-product spec's M5 line and definition of done.
**Skills applied:** `dotnet-style`, `messaging-contract`, `fleet-control`.
**Contract checked:** unchanged at `Version: 3`; `MessageTypes.Alarm = "alarm"`; no `v4.md`; protected files, cores, and `Dockerfile.build` unchanged vs HEAD.

**Verdict: pass with changes**

The product code is a faithful implementation of record 22: `FleetServeLoop` depends only on `IMessageBus`, takes its clock as `Func<DateTime>`, guards the known-shuttle set with its own lock, evaluates on a `PeriodicTimer` cadence fed from configuration, publishes `AlarmMessage` on `_topics.Alarm(...)` with `MessageFamily.Alarm`, uses the same tick's `now` for the envelope, and reads sender/config cadence from configuration. The transport lane (enum, topics, QoS record, bus switch arm, both config files) is in place, both app configurations carry `"Alarm": 1`, and the contract stayed at v3. The build is clean and every test that exists is green, including the real-broker integration check against Mosquitto and the 3 database-backed integration tests (3/3 with broker + database up).

The gate count is not met: the pure suite is **105 tests / 8 `Passed!` assemblies / 0 failures** where the assignment requires **108 / 8 / 0 with `FleetManager.Tests` at 35 and `Messaging.Tests` at 9**. The gap is real and not cosmetic: three of the seven required loop tests are absent (`A_latched_lost_shuttle_publishes_nothing_again`, `A_recovered_shuttle_clears_the_latch_and_can_raise_again`, `An_invalid_status_message_is_ignored`), and the constructor does not validate its reference arguments. Until those are in place the rising-edge clear branch and the status-intake validation are untested, and two mutations I ran stayed completely green. This is **not a passing review**, so per the rules the README milestone table is **not** updated.

---

## Gates run

| Gate | Command | Result |
|---|---|---|
| 1 — build | `cd src && dotnet build` | exit 0, **0 warnings / 0 errors** |
| 2 — pure suite | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **RC 0, 8 `Passed!`, 0 `Failed!`, 105 tests** (not 108). `FleetManager.Tests` = 32 (required 35), `Messaging.Tests` = 9 (required 9), `MqttMessaging.Tests` = 2, `Contracts.Tests` = 7, `ShuttleCore.Tests` = 25, `ShuttleSim.Tests` = 6, `TrafficCore.Tests` = 6, `FleetCore.Tests` = 18. |
| 2b — integration suite | with broker + database up: `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"` | **RC 0, 3 passed, 0 failed** (FleetManager.Tests): end-to-end, restart survival, `An_alarm_reaches_the_broker_on_the_shuttle_topic`. |
| 2c — new broker-only test | `dotnet test ... --filter "FullyQualifiedName~An_alarm_reaches_the_broker_on_the_shuttle_topic"` with only broker up | RC 0, 1 passed, 0 failed. |
| 3 — 7b name walk | `dotnet test ShuttleFleet.slnx --list-tests` with anchored greps | 6 of the 9 pinned names present, **3 missing** (below). |
| 4 — accept. criteria | Phases 1–9 below | 8 of 9 sub-parts green; Phase 7 count and 7b fail. |
| 5 — real broker | Mosquitto + integration test | passed (1/1 for the new test; 3/3 total integration). |
| 6 — mutations | throwaway copy `/tmp/opencode/l15mut`, 9 mutations | see mutation table. |
| 7 — protected files | `git diff --quiet HEAD -- src/Contracts docs/contract ...` | clean. |

## Lesson acceptance phase results

| Phase | Result | Evidence |
|---|---|---|
| 1 — pinned files and members | pass | `FleetServeLoop.cs` exists; `public sealed class FleetServeLoop`; `IMessageBus`, `Func<DateTime>` present; `SubscribeAsync`, `RunAsync`, `HandleStatusAsync`, `EvaluateAndPublishAsync` all present. |
| 2 — alarm transport lane | pass | `MessageFamily.cs:8` `Alarm` is the last member; `TopicScheme.cs:19-20` `Alarm`/`AllAlarms` (`src/Messaging/TopicScheme.cs`); `MqttMessageBusOptions.cs:3` `QosOptions(..., Alarm)` last; `MqttMessageBus.cs:102` `MessageFamily.Alarm => ...`; `FleetServeLoop.cs:96` publishes with `MessageFamily.Alarm`. |
| 3 — configuration | pass | `FleetManager/appsettings.json:9` and `ShuttleSim/appsettings.json:9` both `{ Command:1, Ack:1, Status:1, Alarm:1 }`; `FleetManager/appsettings.json:19` `"AlarmEvaluationIntervalMs": 1000`. |
| 4 — serve command | pass | `Program.cs:14` `"serve"`; `:138-145` reads `LivenessTimeoutMs`, `AlarmEvaluationIntervalMs`, `LostCommunicationSeverity` and validates positivity; `:147-153` builds `TopicScheme`, `ShuttleHealth`, `AlarmEvaluator`, real `MqttMessageBus`, and `FleetServeLoop` with `() => DateTime.UtcNow`; Ctrl+C block at `:156-182`. No-arg banner and `move` path unchanged. |
| 5 — loop purity / lock | pass | `grep DateTime.UtcNow src/FleetManager/FleetServeLoop.cs` prints nothing; `MqttMessaging`/`Npgsql`/`Postgres` not named; `private readonly object _knownLock = new();` at `:16`; `lock (_knownLock)` at `:68` and `:78`. |
| 6 — test project / fake / trait | pass | `FleetManager.Tests.csproj:27` references `MqttMessaging`; `class RecordingBus` at `FleetServeLoopTests.cs:6`; `[Trait("Category", "Integration")]` at `FleetServeLoopIntegrationTests.cs:8`; `(Topic, Payload, Family)` recorded at `:8`. |
| 7 — build + suite gate | **fail on count** | Build green and all tests green, but total is **105**, not 108; `FleetManager.Tests` = 32, not 35. The gate's RC/assembly-count (`8 Passed!`, 0 `Failed!`) and database-free run are fine. |
| 7b — pinned test names | **fail** | Present: `The_constructor_rejects_…`, `Subscribe_uses_…`, `A_lost_shuttle_…`, `Two_shuttles_…`, `TopicScheme_alarm_topics_…`, `An_alarm_reaches_…` (and `A_latched…` is not there). **Missing**: `A_latched_lost_shuttle_publishes_nothing_again`, `A_recovered_shuttle_clears_the_latch_and_can_raise_again`, `An_invalid_status_message_is_ignored`. |
| 8 — no docker in tests | pass | `grep -rniE docker src/*.Tests` printed nothing. |
| 9 — contract and scope | pass | `Current = 3` (`MessageTypes.cs`); `v3.md` `Version: 3`; no `v4.md`; protected-files diff clean; no `blockedPath`/`palletNotDetected` raised outside `AlarmCodes`. |

## Mutation results (throwaway copy `/tmp/opencode/l15mut`, cleared after)

All mutations restored from `FleetServeLoop.cs.orig` between runs. Baseline for the suite (with only the owner's four loop tests) is `FleetManager.Tests` = 32, 0 failures.

| # | Mutation applied | Tests run | Result |
|---|---|---|---|
| (a) | publish family `MessageFamily.Alarm` → `MessageFamily.Status` | `A_lost_shuttle_publishes_one_alarm_...` | **1 Failed!** ✓ — catches the wrong family. |
| (b) | topic `_topics.Alarm(alarm.ShuttleId)` → `_topics.Status(alarm.ShuttleId)` | same | **1 Failed!** ✓ — catches the wrong topic. |
| (c) | `sentAt` → `DateTime.UtcNow` in `PublishAlarmAsync` | same | **1 Failed!** ✓ — envelope time stops matching the sweep's `now`. |
| (d) | `_fleetManagerId` → a *different* literal (`"fleet-manager-HARDCODED"`) | same | **1 Failed!** ✓ — but see issue 3: the same-literal case stayed green (see below). |
| (e) | delete `_knownShuttles.Add(shuttleId)` body | full pure suite | **2 Failed!** ✓ (`A_lost_...` and `Two_shuttles_...`). The lesson predicts 4 failures; the owner's missing tests are exactly the other two. |
| (f) | iterate `_health.UnresponsiveShuttles(now)` instead of the known set | full pure suite | **0 Failed!** — stayed green because `A_recovered_shuttle_clears_the_latch_and_can_raise_again` is absent. This is the mutation the review flags as unpinned (issue 1). |
| (g) | drop the envelope version check (accept any version) in `HandleStatusAsync` | full pure suite | **0 Failed!** — stayed green because `An_invalid_status_message_is_ignored` is absent (issue 1). |
| (h) | subscribe family `MessageFamily.Status` → `MessageFamily.Command` | `Subscribe_uses_the_all_statuses_filter_and_the_status_family` | **1 Failed!** ✓ — but only because the test also asserts the family (it does; keep both checks). |
| (i) | make the sweep read `DateTime.UtcNow` instead of `now` in both the liveness check and the publish | `A_lost_shuttle_publishes_one_alarm_...` | **1 Failed!** ✓. |

Mutation (d) detail: with the owner's current fixture the configured fleet-manager id and the expected literal are both `"fleet-manager"`, so `_fleetManagerId` → `"fleet-manager"` stayed green (RC 0). The production code is still correct (loop reads `messaging.FleetManagerId` in `Program.cs`), but the test, as written, cannot tell a hardcoded `"fleet-manager"` from the configured one. Issue 3 asks for either a distinct fixture id or an assertion against the injected value.

---

## Top issues (max 5), ranked

### 1. blocker — three of the seven required loop tests are absent
`FleetServeLoopTests.cs` (122 lines) contains only `The_constructor_rejects_…`, `Subscribe_uses_…`, `A_lost_shuttle_publishes_…`, and `Two_shuttles_…`. The pinned `A_latched_lost_shuttle_publishes_nothing_again`, `A_recovered_shuttle_clears_the_latch_and_can_raise_again`, and `An_invalid_status_message_is_ignored` were never written, so Phase 7 reports 105/108 and two of the lesson's pinned behavior mutations (f) and (g) stay green. That means nothing currently proves the rising-edge latch ever *stops* re-raising, and nothing proves a bad/mismatched status payload cannot mutate `_health`. The product code appears right (`AlarmEvaluator.Evaluate` calls on the known set with `lost` level, `HandleStatusAsync` validates version/type/id), but in a shuttle system this is the exact failure mode M5 exists to prevent: a latch that never clears silently after recovery, or intake state poisoned by a late/out-of-order message. **Fix:** add the three missing tests following the lesson's given shapes, with the full envelope/body assertion and the invalid-status variants (wrong version, wrong `MessageType`, blank `MessageId`, blank `Status.ShuttleId`, malformed JSON).

### 2. blocker — the constructor does not validate its reference arguments
`FleetServeLoop.cs:19-33` checks `fleetManagerId` (whitespace → `ArgumentException`) and `evaluationInterval` (≤ zero → `ArgumentOutOfRangeException`), but never null-checks `bus`, `topics`, `health`, `evaluator`, or `clock`. The decision record and the lesson say `ArgumentNullException` for a null reference; the hints' shape shows `bus ?? throw new ArgumentNullException(...)` for each. As written, a wiring mistake (`new FleetServeLoop(null, ...)`) survives construction and surfaces as a `NullReferenceException` on the first tick/subscriber instead of a loud startup failure. **Fix:** add the five null guards before the value checks, matching the shape in `docs/lessons/15-serve-loop-and-alarm-publish-hints.md` Tier 3.

### 3. should-fix — the "hard-coded sender" mutation is not currently load-bearing
The configured fleet-manager id in the pure-test harness is `"fleet-manager"` and the assertion is `Assert.Equal("fleet-manager", alarm.Envelope.SenderId)`, so when I mutated `_fleetManagerId → "fleet-manager"` the test stayed green (mutation (d), same-literal case). The name `_fleetManagerId` is being read from config in `Program.cs`, so production is fine, but the test cannot distinguish a hard-coded literal from a wired value. This is the same "test must not assert against the exact constant it allowed on the outbound side" gap review 14 flagged for the version literal. **Fix:** either make the harness use a clearly different id (e.g. `"fm-test-01"`) and the assertion use that variable, or assert the sender against the injected fleet-manager id instead of a string literal.

### 4. should-fix — the invalid-version intake and the recovery/clear branch have no test
Directly caused by issue 1: `HandleStatusAsync`'s `ContractVersion` comparison and `AlarmEvaluator`'s `lost = false` clear branch are exercised only incidentally (if at all). A real broker delivers late, duplicated, or stale status in M5+; the tests need to pin "ignored, no state change" for a bad envelope and "clear + fresh re-raise" for a returning shuttle. See (f) and (g) staying green in the mutation table.

### 5. consider — style/whitespace
`MessageFamily.cs:3-9` has trailing whitespace on every line and a non-uniform layout vs the rest of `src/Messaging/`. `TopicSchemeTests.cs:32` has a trailing tab. `FleetServeLoop.cs:19-21` is unusually indented relative to the class body. None of it affects behavior, but dotnet-style review convention for this repo is that warnings are "things to fix," and these make diffs noisier. The `dotnet-style` skill calls for `_camelCase` fields (held here), `Async` suffix (held), and `record` for immutable messages (held — shapes untouched).

## What is solid

- **Single seam, used consistently.** `FleetServeLoop` names only `IMessageBus` and `TopicScheme`; no `MqttMessaging`, `Npgsql`, topic string, shuttle id, severity, sender literal, interval, or `DateTime.UtcNow` in `FleetServeLoop.cs`. The clock is injected (`Func<DateTime>`), and `EvaluateAndPublishAsync(DateTime now, …)` never reads one. That keeps the decision code pure-testable, as the `fleet-control` skill requires.
- **No races across the two threads.** `_knownShuttles` is the only state shared between the bus thread (intake) and the timer/`CancellationToken` (sweep), and it is protected by `_knownLock` with a `ToArray()` copy taken *outside* the lock before any publish; no `await` is held inside the lock. `ShuttleHealth` and `AlarmEvaluator` carry their own locks. This matches record 19's "one lock per shared object" discipline.
- **One clock per tick.** `RunAsync` calls `_clock()` once per tick and passes that single value into `EvaluateAndPublishAsync`, which uses it both for `IsUnresponsive(id, now)` and for `PublishAlarmAsync(alarm, now, …)`; the envelope's `SentAt` therefore agrees with the decision time. Test (c) confirms it.
- **The transport lane is complete.** `MessageFamily.Alarm` appended last (no enum renumbering), `QosOptions.Alarm` appended last, one bus switch arm added, config key present in both apps, topic helpers present. No version bump: v3 already carries `alarm`, and `docs/contract/v3.md` / `ContractVersion.Current` were left untouched; the protected-files diff is clean.
- **Config flows through, no literals.** Sender from `Messaging:FleetManagerId`, cadence from `FleetManager:AlarmEvaluationIntervalMs`, window from `LivenessTimeoutMs`, severity from `LostCommunicationSeverity`, QoS from the `Qos` block — all read in `Program.cs` (lines 138-153). `FleetServeLoop` holds none of them.
- **The real-broker path is proven.** `An_alarm_reaches_the_broker_on_the_shuttle_topic` passes against Mosquitto (1/1 when run alone; all 3 integration tests green with broker + database). The alarm arrives on the wire on the wildcard topic with `Code == AlarmCodes.LostCommunication`. Disposition (`await using`, two distinct client ids, `WaitAsync(timeout)`) is sound.

## Explain-it check

Answer these out loud or in writing before the next lesson. An interviewer would ask exactly these.

1. **Who, not just what, is lost.** `FleetServeLoop` evaluates the ids it has heard from on `+/status`, not `_health.UnresponsiveShuttles(now)`. Trace what happens to a shuttle that goes silent at t=0, recovers at t=2 (`Evaluate(id, false)`), and then goes silent again at t=5, under each of the two choices. Say precisely which call's return value prevents the second silence from being missed — and why iterating only the currently-lost list breaks that.
2. **One clock per tick.** In `EvaluateAndPublishAsync`, both `IsUnresponsive(id, now)` and `PublishAlarmAsync(alarm, now, …)` use the same `now`. What breaks for an operator's incident timeline if the publish instead used `DateTime.UtcNow`? Name the field in the envelope that catches it and the test that asserts equality.
3. **Why four edits for one lane, no bump.** Adding `MessageFamily.Alarm` touches the enum, `TopicScheme`, `QosOptions` + the `MqttMessageBus` switch, and config. Explain why publishing on a plain new topic string instead fails (where exactly: which line throws), and why this slice still counts as "no contract change" despite adding a new topic and a new bus family — which file is the source of truth that already carried `alarm`?

**Lines / blocks to study (unclear, copied, or worth re-reading):**

- `src/FleetManager/FleetServeLoop.cs:19-33` — the constructor checks two of the seven conditions the contract requires. Identify which five are missing and move them ahead of the value checks.
- `src/FleetManager/FleetServeLoop.cs:45-73` — `HandleStatusAsync`'s deserialize-and-validate; compare each condition line against the same pattern in `ShuttleEndpoint`/`ReliableCommandSender` and state why the `_clock()` inside it (not the envelope's `SentAt`) is what gets written to `_health`.
- `src/FleetManager/FleetServeLoop.cs:57-64` — the `status?.Envelope is not { } envelope` pattern; read it once as "the envelope must exist with all of these properties" and be ready to say which of `ContractVersion`, `MessageType`, `MessageId`, `ShuttleId` a partial/forged message could still pass.
- `src/FleetManager.Tests/FleetServeLoopTests.cs:70-101` — the one alarm assertion. Note specifically `Assert.Single` before deserialization and why the topic is also asserted (the integration test does not assert the topic).
- `src/FleetManager.Tests/FleetServeLoopTests.cs:113-121` — `Assert.Equal(2, bus.Published.Count)` proves *something* was published twice but identifies nothing. Ask what happens if the production code started publishing alarms keyed on the wrong id.
- `src/FleetManager.Tests/FleetServeLoopIntegrationTests.cs:38-49` — the fixed clock is captured *before* the handler is wired; explain why `start` staying constant while the sweep uses `start.AddSeconds(31)` is essential, and what `WaitAsync(TimeSpan.FromSeconds(32))` is guarding.
- `src/FleetManager/Program.cs:156-182` — the `CancellationTokenSource`/`TaskCompletionSource`/handler/try-finally; be able to answer "why complete a `TaskCompletionSource` instead of just calling `stopping.Cancel()` in the handler" and what `eventArgs.Cancel = true` is protecting against.
- `src/MqttMessaging.Tests/MqttMessageBusTests.cs` (around the `new QosOptions(...)` call sites) — the fourth positional value was appended; confirm both sites pass exactly four and that the argument order matches `Command, Ack, Status, Alarm`.

## Revisit list

- **Constructor validation and `ArgumentNullException` timing.** Failures at startup vs. on first use; the "wake it up early" argument for wiring mistakes in a long-running service.
- **Rising edge vs. level, and the clear branch.** Re-derive `connected → true → true → false → true` for the alarm firing. The reason the clear branch sends nothing (it only re-arms).
- **Test constants vs. moving symbols, reprised.** Review 14 revisited it for the version; here it recurs for the sender literal. The general technique: inject a value that differs from the expected literal to catch hard-coding.
- **Mutation testing as a review tool.** Each mutation listed asks "does this changed thing turn a test red?" Be able to run one on a throwaway copy and read the pass/fail difference; each one mutates a different seam.
- **Two-clock seams.** `RunLoop` uses the simulated `_currentTime`; `FleetServeLoop` uses `DateTime.UtcNow` (and receives it here as injected). When they (later) share a clock domain, which side has to change.

## End-product check

This slice makes M5's "lost communication" fault leave the fleet manager: a heartbeat stream stops, the bus-connected `FleetServeLoop` notices on its next cadence tick, and an `AlarmMessage` publishes on `shuttle/{id}/alarm` at QoS 1 with the right severity and a per-episode, re-arming latch. Structurally it carries forward cleanly — the `Alarm` transport lane mirrors the existing `Status` lane exactly, so M7 can widen its picture with a single `{root}/+/alarm` subscription, and the recovery slice later reuses the same loop and the same four-edit lane pattern for its own kind. The risks are the known ones from record 20 §6 and 22 §10: the known set and the latch are in memory only (a manager restart forgets them and re-raises), and two clock domains still do not agree. The new risk this review adds is *verification debt*: the rising-edge clear branch and the status-intake validation are currently unpinned by tests, so a regression there would not be caught by CI until issue 1 (and the missing constructor guards behind issue 2) are fixed. Nothing in this slice corrupts fleet state or duplicates a pallet; the alarm path is read-only against everything M5 owns.

---

## Note for the professor (lesson/hints defects)

None of substance. Checked against the lesson's own list:

- **Concepts/assignment alignment.** The lesson's three concepts (the serve loop as a testable I/O shell; the known-shuttle set; the alarm transport lane) are each taught in its own section, each fixed in record 22 (§2/§4/§6/§8, §3, §5 respectively). The nine assignment requirements are listed separately and all nine are delivered except the test-count and constructor-validation portions flagged in issues 1–2. The "not in this lesson" list and the later-M5 scope are respected in the product code (no recovery, ack, reassignment, auto-release, `blockedPath`/`palletNotDetected` raised, roster, WMS, or dashboard).
- **The three-concept cap held.** Three concepts, all taught, each has a matching deliverable.
- **One minor note (process, not a lesson defect):** the hint Tier 2 says the total suite goes 100 → 108 and `FleetManager.Tests` 28 → 35, which is exactly what the assignment's Phase 7 asserts. The owner delivered 100 → 105 / 28 → 32, so this is student non-delivery of three tests, not a mis-stated number. No change recommended to the lesson; recommend the owner use the hint's "corrected pure-test harness" section verbatim rather than reconstructing it.
- **Protected-files check agrees with the lesson.** `git diff --quiet HEAD` on `src/Contracts`, `docs/contract`, `ShuttleHealth.cs`, `AlarmEvaluator.cs`, `RunLoop.cs`, the three cores, and `Dockerfile.build` is clean, and `ContractVersion.Current` is still 3. No `v4.md`.
- **Cosmetic (observed, not requested):** `/tmp/opencode/l14base` and `/tmp/opencode/l14toy` from lesson 14 are still present. Unrelated to this slice; cleanup only if the "scratch dirs deleted" claim is to be made literally next time.
- **README not updated.** Per the rules this is a pass-with-changes verdict, not a passing one, so M5 stays "in progress" and the verification notes were not extended with lesson 15 evidence. Once issues 1–2 (and ideally 3–4) are fixed and the pure suite shows 108 total with `FleetManager.Tests` = 35, a re-review should mark M5 further along with the command that verified it.