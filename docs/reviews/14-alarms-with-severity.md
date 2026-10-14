# Review 14: Alarms with severity, rising-edge detection, and contract v3 (lesson 14, M5 second slice)

**Date:** 2026-10-14
**Review of:** `dc35900 M5: Lesson 14 First attempt` at HEAD, clean working tree (`git status --porcelain` empty). Parent is `bef790b` (hints edit); the commit touches only `docs/contract/v3.md`, four new `src/Contracts/*` files, `src/FleetManager/AlarmEvaluator.cs`, `src/FleetManager/appsettings.json`, two new test files, and one test-rename in `src/Messaging.Tests/MessageEnvelopesTests.cs`.
**Scope:** lesson 14's acceptance criteria (phases 1–9), the "Proof details" and mutation list, [record 21](../decisions/21-alarms-with-severity.md), and the end-product spec's M5 line and definition of done.
**Contract checked:** `docs/contract/v3.md` is `Version: 3` with the `alarm` kind, the `Alarm`/`AlarmMessage` shapes, severities, codes, and a `ContractVersion: 3` example. `docs/contract/v2.md` is byte-unchanged vs `HEAD~1`.
**Process note:** this review modified no file under `src/` (`git status` identical before and after). Scratch mutation builds ran in `/tmp/opencode/l14m` and were deleted. `docker compose -f docker/compose.yml down` was run after the optional integration check; no broker, container, database, or `dotnet run` process is left. The pre-existing `/tmp/opencode/l14base` and `/tmp/opencode/l14toy` directories (from lesson authoring) were not created by this review and were left alone.

**Verdict: pass with changes**

The code half of this slice is exactly right. The v3 shapes match record 21 to the character, `AlarmEvaluator` implements the rising-edge latch with the same `Add`/`Remove` rule and one-lock discipline, the config key is in place with no literal severity, and every one of the ten new tests is mutation-proven load-bearing. The build is clean (0 warnings / 0 errors) and the pure suite hits the exact gated counts (RC 0, 8 `Passed!`, 0 `Failed!`, 100 tests; `Contracts.Tests` 7, `FleetManager.Tests` 28). Protected files, `v2.md`, and the carried-over contract shapes are untouched, and no later-M5/M6/M7 scope leaked into product code.

Two explicit acceptance items are unmet and must be fixed before this slice can be called done: (1) `docs/contract/v3.md` never states the alarm's topic and never adds the `alarm` row to the kinds table, so the source of truth does not document where the new kind travels; (2) `Alarm_wire_names_match_the_contract` omits the `ContractVersion.Current == 3` assertion the assignment names. Both are small and localized. The verdict is **pass with changes** — not yet done, but the owner's work is fundamentally sound. Per the rules, the README milestone table and verification notes are **not** updated.

---

## Lesson acceptance phase results

Run from the repo root unless a phase says `src/`. Evidence is from a clean `dotnet build` and the exact suite/list commands in the lesson on 2026-10-14.

| Phase | Result | Evidence |
|---|---|---|
| 1 — new pieces exist with pinned names | pass | All five files + `docs/contract/v3.md` present; `Alarm.cs:3` record, `AlarmMessage.cs:3`, `AlarmEvaluator.cs:5` class and `:20` method signature match the greps. |
| 2 — v3 wire names and version bump | pass | `MessageTypes.cs:8` `"alarm"`; `:13` `Current = 3`; `AlarmSeverities.cs:5-7` `info`/`warning`/`critical`; `AlarmCodes.cs:5-7` `lostCommunication`/`blockedPath`/`palletNotDetected`. |
| 3 — purity (no clock/ids/I-O) | pass | Greps for `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, `Postgres`, and hard-coded lane ids printed nothing. `AlarmEvaluator.cs:1` has `using Contracts;` (deliberate). |
| 4 — latch and lock are really there | pass | `AlarmEvaluator.cs:8` `private readonly object _lock`; `:25` `lock (_lock)`; `:9` `HashSet<(string ShuttleId, string Code)>`. |
| 5 — configuration carries the severity | pass | `appsettings.json:18` `"LostCommunicationSeverity": "critical"`. |
| 6 — the contract page | **partial** | `v3.md:3` `Version: 3`; `alarm` term present; `critical`/`lostCommunication` present; the three carried-over kinds present; `v2.md` byte-unchanged. **FAIL:** the topic grep `shuttle/\{shuttleId\}/alarm` returns no match anywhere in `v3.md`, and the kinds table (`v3.md:18-22`) has no `alarm` row. |
| 7 — build + pure suite gate | pass | `dotnet build` exit 0, 0 warnings / 0 errors. `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` → RC 0, **8** `Passed!`, 0 `Failed!`, **100** total; `Contracts.Tests` **7**, `FleetManager.Tests` **28**, `ShuttleSim.Tests` **6**, `Messaging.Tests` **8**. Gate "suite gate OK". |
| 7b — pinned test names present / stale name gone | pass | All eleven pinned names present by anchored `--list-tests` match; `MessageEnvelopes_generate_a_unique_id_and_version_two` gone, `..._and_the_current_version` present. |
| 8 — no docker in tests | pass | `grep docker src/*.Tests --include='*.cs'` printed nothing. |
| 9 — protected files / carried-over shapes / no later-M5 scope | pass | `git diff --quiet dc35900^` on `ShuttleCore`, `TrafficCore`, `FleetCore`, `ShuttleHealth.cs`, `TaskDispatcher.cs`, `Program.cs`, `v2.md`, `Dockerfile.build` and the five carried-over contract shapes → clean. No `MessageFamily.Alarm`/`TopicScheme.Alarm`/`QosOptions.Alarm`/`AllAlarms` in product code; `blockedPath`/`palletNotDetected` appear only as `AlarmCodes` constants and in `AlarmMessageTests`. |
| Optional integration suite | pass | With `docker compose up -d`: `dotnet test ShuttleFleet.slnx -v m --filter "Category=Integration"` → RC 0, 2 passed, 0 failed (`FleetManager.Tests`). Torn down afterwards. |

### Mutation results (`/tmp/opencode/l14m`, clean copy without `bin/`/`obj/`)

| # | Mutation | Expected test | Result |
|---|---|---|---|
| a | drop `_raised.Add(key)` guard (`if (lostCommunication)`) | `A_condition_that_stays_true_does_not_raise_again` | **Failed! 1** ✓ |
| b | drop `_raised.Remove(key)` clear branch | `A_cleared_condition_can_raise_again` | **Failed! 1** ✓ |
| c | hard-code the severity in the returned `Alarm` | `The_configured_severity_is_carried_on_the_alarm` | **Failed! 1** ✓ |
| d | drop the constructor severity validation | `An_unknown_severity_is_rejected` | **Failed! 1** ✓ |
| e | force `Alarm.Severity` into the wrong JSON field (`[JsonPropertyName("WrongSeverity")]`) | `Alarm_json_keeps_the_agreed_field_names` | **Failed! 1** ✓ |
| f | key the latch on shuttle id alone (`HashSet<string>`) | `Two_shuttles_are_latched_independently` | **Passed! 1** ✓ — matches the lesson's honest note: one code today, so this is a forward-proof, not a load-bearing mutation. Verified, not assumed. |

Scratch copy deleted.

---

## Top issues (max 5), ranked

### 1. blocker — `docs/contract/v3.md` does not document the alarm kind or its topic
The whole point of v3 is that `alarm` becomes a routable kind. The page adds an `AlarmMessage` section and a JSON example, but the kinds table (`v3.md:18-22`) still lists only `moveCommand`/`commandAck`/`shuttleStatus`, and the string `{root}/{shuttleId}/alarm` (or `shuttle/{id}/alarm`) appears **nowhere** in the file. Lesson requirement 2 (bullets 2 and 6) and Phase 6 both require it. This matters for the end product, not just the checklist: lesson 15 publishes on that exact topic and M7 subscribes to `{root}/+/alarm`; the contract is the source of truth for that route, so an undocumented topic is the kind of gap that causes an integration mismatch and support calls later. **Fix:** add the row `| AlarmMessage | alarm | alarm |` to the kinds table and a sentence stating the alarm travels on `{root}/{shuttleId}/alarm`.

### 2. should-fix — `Alarm_wire_names_match_the_contract` does not pin the version
`AlarmMessageTests.cs:34-43` asserts `MessageTypes.Alarm`, the three severities, and the three codes, but never asserts `ContractVersion.Current == 3`. Lesson requirement 5 names that assertion first. Nothing else in the suite pins the literal `3` either — the roundtrip and envelope tests compare against `ContractVersion.Current`, which moves with the constant. A future accidental revert to `2` would keep the pure suite green. **Fix:** add `Assert.Equal(3, ContractVersion.Current);`.

### 3. consider — the constructor takes `_lock` for no reason
`AlarmEvaluator.cs:13` wraps the validation and field assignment in `lock (_lock)`. No other thread can hold a reference to an object that is still under construction, so the lock protects nothing, and it slightly muddies the "one lock per public method" story the lesson teaches. Harmless, but removing it makes the concurrency reasoning cleaner. (Keep the lock in `Evaluate`.)

### 4. consider — `v3.md` section descriptions are wrong/uncertain
`v3.md:39` says `Sent by a fleet manager to the dashboard?.` and `:70` says `Sent by a shuttle to the fleet manager?` about the `Alarm` body. An alarm goes from the fleet manager onto the bus (the dashboard consumes it in M7), and `Alarm` is a body, not a message that anyone sends on its own. Drop the question marks and state the direction plainly, matching the confident tone of the v2 sections.

### 5. consider — cosmetic cleanups
`MessageTypes.cs:8` ends `"alarm"; ` with trailing whitespace, and `AlarmEvaluator.cs:10` is a whitespace-only blank line. In `AlarmMessageTests.cs:14,25` the fixture pairs `AlarmCodes.BlockedPath` with the detail string `"Pallet Not Detected"`; the code and detail contradict each other and could confuse a future reader of the test. Use a matching pair or `null`.

---

## What is solid

- **The record-21 shapes are reproduced exactly, with no drift.** `Alarm`, `AlarmMessage`, `AlarmSeverities`, `AlarmCodes`, `MessageTypes.Alarm`, and `Current = 3` all match §6 line for line; nothing else in `src/Contracts/` was touched.
- **`AlarmEvaluator` is the record's algorithm.** The `(shuttleId, code)` key, `_raised.Add(key)` as the rising edge, `_raised.Remove(key)` only on the falling edge, the blank-id `ArgumentException`, the closed-set `ArgumentOutOfRangeException`, and "clear sends nothing" are all present and correct (`AlarmEvaluator.cs:11-37`).
- **Every new test earns its name.** All five behaviors the lesson named as load-bearing turned red under mutation, including the JSON-field-name test, which the lesson correctly identified as the guard against a silently renamed wire field.
- **The config boundary is respected.** No severity literal in the evaluator; the value arrives through the constructor and lives in `appsettings.json`.
- **Discipline held across the slice:** `v2.md` byte-unchanged, protected files untouched, no `MessageFamily`/`TopicScheme`/`QosOptions.Alarm`, no serve loop in `Program.cs`, no shuttle-raised faults. The pure suite gate is exactly on target.

---

## Explain-it check

Answer these out loud or in writing before the next lesson. An interviewer would ask exactly these.

1. **Event vs level.** Why is an alarm a dedicated `alarm` kind instead of a `FaultReason` on the existing `shuttleStatus`? Say concretely what an event can express that a level cannot — and why a lost link is the case where the difference matters most.
2. **Severity's home and type.** In `Alarm.cs`, `Severity` is a `string` in the body. Why the body and not the envelope, and why a string with constants in `AlarmSeverities` instead of a C# `enum`? What exactly would go onto the wire if you used an enum, and what breaks if someone reorders its members later?
3. **The latch, precisely.** In `AlarmEvaluator.Evaluate`, what does `_raised` store, and why the pair `(shuttleId, code)` rather than the shuttle id alone? Trace `true → true → false → true` for one shuttle and say, at each call, what is returned and what is in `_raised`. Explain why `HashSet.Add`'s `false` return *is* the rising-edge test.
4. **The clear branch.** Why does the `if (!lostCommunication) _raised.Remove(key);` branch send nothing? If you deleted that line, describe what an operator would see on the dashboard over a one-hour silence, and which test catches it.
5. **The bump and the rejected sender.** Why does v3 get a new page instead of an edit to `v2.md`? The change is additive — every v2 message is still valid JSON — so why does `ContractVersion.Current = 3` still cause our own `ShuttleEndpoint.cs:37` to reject a v2 sender? Name the line and the trade-off the record accepts.
6. **Mutation reasoning.** `The_configured_severity_is_carried_on_the_alarm` constructs the evaluator with `Warning` while `appsettings.json` says `critical`. Why does that mismatch make the test go red if the severity is hard-coded? And: keying the latch on shuttle id alone leaves `Two_shuttles_are_latched_independently` green — does that make the test worthless, or is it a forward-proof? Explain your answer.

**Lines/blocks to study (unclear, copied, or worth re-reading):**
- `src/FleetManager/AlarmEvaluator.cs:13` — the lock inside the constructor. Ask yourself what state it protects and whether any other thread can see this object yet.
- `src/Contracts.Tests/AlarmMessageTests.cs:34-43` — the missing `ContractVersion.Current == 3` assertion; and `:14,25` the `blockedPath` code paired with a pallet-not-detected detail.
- `docs/contract/v3.md:18-22` and the absent topic — compare against `v2.md`'s kinds table and record 07's `{root}/{shuttleId}/{kind}` scheme.
- `src/Contracts/MessageTypes.cs:8` and `src/FleetManager/AlarmEvaluator.cs:10` — trailing whitespace from the edits.

---

## Revisit list

- **Event vs level, and edge detection.** The rising-edge latch is the load-bearing idea of this slice; be able to derive it from "a level is true for a long time, an event happens once" without notes.
- **Wire strings vs enums, and closed sets.** Why the repo uses constant strings for `State` and `Severity`; what a numeric enum costs on the wire; what "closed set" commits you to.
- **Contract versioning and receiver compatibility.** Additive changes still break a strict `== Current` receiver; what a version-*range* check would buy during a rollout, and why the record defers it.
- **Lock discipline.** What a lock is for (shared mutable state reachable by more than one thread), and the corollary that a constructor and a local `HashSet` created per call do not need one.
- **Tests must pin the agreed literal, not a moving symbol.** Asserting against `ContractVersion.Current` is right for round-trips but cannot catch an accidental change to `Current` itself; the version bump needs at least one literal assertion.

---

## End-product check

This slice freezes M5's alarm vocabulary: a dedicated `alarm` kind at contract v3, a closed severity set, and a pure `AlarmEvaluator` that turns the level from lesson 13's `ShuttleHealth.IsUnresponsive` into a single event per fault episode. The structure carries forward cleanly — lesson 15 wires the publish path against exactly these shapes and this topic, and M7 renders the severity. The residual risks are the known ones written down in record 21 §8: the latch is in memory only, so a fleet-manager restart re-raises a still-silent shuttle's alarm (recovery/reconciliation is a later slice), and `== Current` will reject v2 senders during any mixed rollout. The new risk this review adds is the contract-page gap: if lesson 15 wires against a topic the contract never names, an integration mismatch becomes a "read the code to find the truth" problem instead of a "read the contract" one. That is why issue 1 is a blocker despite the code being correct. Nothing here corrupts fleet state or drops a pallet.

---

## Note for the professor (lesson/hints defects)

1. **Phase 6's topic check fails silently.** `docs/lessons/14-alarms-with-severity.md:662` is `grep -qE 'shuttle/\{shuttleId\}/alarm|\{root\}/\{shuttleId\}/alarm' docs/contract/v3.md` with no `&& echo` or `|| echo`. When the owner omits the topic (issue 1), the command simply returns non-zero and the phase output shows nothing to flag it. Every other gating grep echoes a PASS/FAIL; this one should too, or the owner cannot see the miss from the acceptance run alone.
2. **Phase 9's scope grep over-matches the tests.** `:707-708` excludes only `src/Contracts/AlarmCodes.cs`, so it flags the legitimate fixture usages `AlarmCodes.BlockedPath` / `AlarmCodes.PalletNotDetected` in `src/Contracts.Tests/AlarmMessageTests.cs`. The lesson says the expected result is "nothing"; either exclude `*.Tests` as well or change the stated expectation, otherwise a correct owner appears to have leaked scope.
3. **The honest mutation note is accurate.** The claim that keying the latch on the shuttle id alone does **not** turn `Two_shuttles_are_latched_independently` red is verified: with one code today, that test stays green under the mutation. Good that the lesson says so rather than overclaiming. The remaining five mutations all bite exactly as the lesson predicts.
4. **Counts and the three-concept cap held.** The measured suite is exactly RC 0 / 8 `Passed!` / 0 `Failed!` / 100, with `Contracts.Tests` 7 and `FleetManager.Tests` 28. The lesson's three concepts (alarm/severity, rising-edge latch, contract v3) are each taught and each has a corresponding deliverable, with the four assignment requirements listed separately. No lesson change needed there.
5. **Minor:** record 21 §9 and the lesson's verification table both state `/tmp/opencode/l14base` and `/tmp/opencode/l14toy` were deleted, but both directories still exist in `/tmp/opencode`. Cosmetic; worth a cleanup so the process claim is true.

## Re-review (lesson 14)

**Date:** 2026-10-14  
**Reviewer:** Re-review of current working tree (post-owner fixes). Original review at commit 815e14b; current HEAD 815e14b with uncommitted changes. Owner fixes applied since first review: added `alarm` row to kinds table and topic line in `docs/contract/v3.md`; amended lesson 14 requirement 5 and Phase 6/9 gates; removed constructor lock from `AlarmEvaluator`; fixed trailing whitespace in `MessageTypes.cs`; changed AlarmMessageTests fixture to use `Detail: null`.

**Verdict:** pass

**Acceptance gates (verified):**
- Phases 1-9: all PASS in current tree. Phase 6 now checks for the kinds table row `| AlarmMessage | alarm | alarm |` and the topic `{root}/{shuttleId}/alarm` (both present at v3.md:23 and v3.md:40). Phase 9 scope grep excludes `src/*.Tests` and returns nothing unexpected.
- v2.md byte-unchanged (same SHA256 as at dc35900 and HEAD). Protected files untouched; carried-over contract shapes unchanged.
- Build: clean (0 warnings / 0 errors). Pure suite: RC 0, 8 `Passed!`, 0 `Failed!`, 100 total; `Contracts.Tests` 7, `FleetManager.Tests` 28, `Messaging.Tests` 8 (renamed test present, stale `version_two` absent).

**Regression checks:** `ContractVersion.Current` is `3` in code; renamed test `MessageEnvelopes_generate_a_unique_id_and_the_current_version` present; no stale `version_two` name anywhere in src. When `Current` was mutated to `2` in scratch, all Contracts.Tests, Messaging.Tests, and the full pure suite still passed (trade-off validated).

**Mutation verification (on scratch copy):**
- Remove latch Add guard → `A_condition_that_stays_true_does_not_raise_again` fails (red). ✓
- Remove clear branch → `A_cleared_condition_can_raise_again` fails. ✓  
- Hard-code severity → `The_configured_severity_is_carried_on_the_alarm` fails. ✓
- Drop severity validation → `An_unknown_severity_is_rejected` fails. ✓
- Wrong JSON field name → `Alarm_json_keeps_the_agreed_field_names` fails. ✓
- Key latch on shuttle id only → `Two_shuttles_are_latched_independently` still passes (forward-proof as noted in lesson). ✓
- Change `MessageTypes.Alarm` to "alert" → `Alarm_wire_names_match_the_contract` fails. ✓
- Change `AlarmSeverities.Info` to "verbose" → wire-names test fails. ✓

**Amendment adjudication:** The owner’s pushback to drop the version literal assertion from `Alarm_wire_names_match_the_contract` is accepted. The test correctly guards wire vocabulary; the version is already pinned by Phase 2 (`Current = 3`) and Phase 6 (`Version: 3`). The trade-off (CI-only dotnet test won’t catch accidental Current drift) is acknowledged and documented in the amended lesson. Separating concerns is the right call.

**Blockers:** None. The v3.md topic and kinds row are present; the lesson amendment is in place. All acceptance criteria satisfied.

**Recommendation:** PASS. No further changes required.
