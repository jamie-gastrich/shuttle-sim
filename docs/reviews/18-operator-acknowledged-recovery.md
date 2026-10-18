# Review 18 — Operator-acknowledged recovery (M5, lesson 18)

**Reviewer:** Reviewer agent
**Verdict: not yet**

Review of: the owner's **uncommitted working tree** (no commit yet for lesson 18).
`git status` at review time: six modified files (`src/Contracts/MessageTypes.cs`,
`src/Messaging/MessageFamily.cs`, `src/Messaging/TopicScheme.cs`,
`src/MqttMessaging/MqttMessageBus.cs`, `src/ShuttleCore/Shuttle.cs`,
`src/ShuttleCore.Tests/StateTransitionTests.cs`) and four untracked files
(`src/Contracts/RecoveryCommandMessage.cs`, `src/Contracts/RecoveryAckMessage.cs`,
`src/Contracts.Tests/RecoveryMessageTests.cs`, `docs/contract/v4.md`).

**Scope:** lesson 18's nine requirements and its acceptance criteria,
[record 26](../decisions/26-operator-acknowledged-recovery.md), contract v3→v4,
and the end-product spec's M5 line ("Recovery that an operator acknowledges").
This review modified no file under `src/`.

**Process note:** the mutation runs below were done in a throwaway copy
`/tmp/opencode/l18mut`, which was deleted; the workspace `src/` was never
modified (the `git status` after the review is identical to before it). No
broker, database, or container was started (`docker ps -q` → empty), no
`dotnet run` was left behind, and `dotnet build-server shutdown` was run. The
integration suite (`Category=Integration`) was **not** run — it needs the
broker and Postgres, and nothing in this slice touches a transport code path;
that claim is **not yet verified** by execution, only by reading.

## Gate results (verbatim)

1. `cd src && dotnet build` — **RC 0**:
   ```
   Build succeeded.
       0 Warning(s)
       0 Error(s)
   ```
2. Pure suite (no database, no broker):
   ```
   cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
   ```
   `RC 0`, 8 `Passed!`, 0 `Failed!`, **137** tests total (baseline 134):
   ```
   Passed!  - Failed:     0, Passed:    31, Skipped:     0, Total:    31 - ShuttleCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    22, Skipped:     0, Total:    22 - FleetCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     9, Skipped:     0, Total:     9 - Messaging.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8 - Contracts.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7 - ShuttleSim.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6 - TrafficCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5 - ... (FleetManager.Tests = 52)
   Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2 - MqttMessaging.Tests.dll (net10.0)
   ```
   Exact spread vs the review-17 baseline (134):
   `ShuttleCore.Tests` 29→**31** (+2, the two `Recover` facts),
   `Contracts.Tests` 7→**8** (+1, `Recovery_messages_survive_json_roundtrip`),
   everything else unchanged; `FleetManager.Tests` **52**, `Messaging.Tests` **9**
   (unchanged — see blocker 1).
3. All lesson acceptance-criteria greps pass: `recoveryCommand`/`recoveryAck`
   in `MessageTypes.cs`, `Current = 4`, `Recover` in `Shuttle.cs`, `Recovery` in
   `MessageFamily`, and both kinds present in `docs/contract/v4.md`. The three
   new files exist.

The build and the suite are green, so this is not a gate failure. It is a
**requirements** failure: one required test does not exist.

## Acceptance criteria

| Req | Status | Evidence |
|---|---|---|
| 1 `Shuttle.Recover` | pass | `Shuttle.cs:144-152`: `EnsureInOrder(now)`; guard `_state != ShuttleState.Fault` → `ShuttleRejectedException($"cannot recover while {_state.ToWireName()}")`; `_state = Idle`, `_faultReason = null`, `_lastEventAt = now`; `Position`/`CarriedPalletId` untouched. Exactly record 26 Decision B. |
| 2 `MessageTypes` + version | pass | `MessageTypes.cs:9-10` adds `RecoveryCommand = "recoveryCommand"`, `RecoveryAck = "recoveryAck"`; `Current = 4` (`:13`). Diff shows no other kind changed; `MessageEnvelope.cs` untouched. |
| 3 `RecoveryCommandMessage` | pass | New file, plain positional record `(MessageEnvelope Envelope, string ShuttleId)`. **No** `JsonPropertyName` attributes, no `System.Text.Json.Serialization` using — matches `MoveCommandMessage`/`CommandAckMessage`/`AlarmMessage`/`ShuttleStatusMessage`. (The lesson's requirement 3 and hints Tier 1 tell you to copy JsonPropertyName attributes that the other records do not have; following the *code* was right — see note for the professor.) |
| 4 `RecoveryAckMessage` | pass | New file, plain record `(MessageEnvelope, bool Accepted, string? Reason)` — byte-for-byte the `CommandAckMessage` shape. |
| 5 `TopicScheme` recovery topics | pass (code) | `TopicScheme.cs:21-23`: `{root}/{id}/recoveryCommand`, `{root}/{id}/recoveryAck`, `{root}/+/recoveryAck`. Correct shapes and the wildcard uses `+`. |
| 6 `MessageFamily.Recovery` | pass | `MessageFamily.cs:9` adds the member; nothing else in the enum moved. |
| 7 `ToMqttQos` mapping | pass | `MqttMessageBus.cs:103`: `MessageFamily.Recovery => _options.Qos.Command`, added before the default throw. `MqttMessageBusOptions.cs` is **unchanged** (`git status` clean for it): `QosOptions` is still the 4-arg record and all four existing call sites still build. The brief detour that added a `Recovery` QoS field was correctly reverted — reusing Command QoS is what lesson and hints both ask for. |
| 8 tests | **fail (partial)** | 8a pass: `Recover_from_fault_enters_idle_and_clears_fault_reason` and `Recover_from_idle_is_rejected` exist, are green, and are load-bearing (M1/M2 below). 8b partial: `Recovery_messages_survive_json_roundtrip` exists and is green, but it **does not verify wire names/constants** (M3/M5/M7 below) — the requirement says "Also verify wire names/constants". 8c **missing**: there is **no recovery-topic test in `src/Messaging.Tests/TopicSchemeTests.cs`** (`Messaging.Tests` is still 9, same as baseline; `grep -rn -i recovery src/Messaging.Tests/` matches nothing outside `bin/`). |
| 9 `docs/contract/v4.md` | partial | Exists; full copy of v3 with the version bumped everywhere (title, `Version: 4`, every example `"ContractVersion": 4`, and the closing paragraph now says `v5.md`/`v4.md`); summary table has both new rows with the right wire names; full `## RecoveryCommandMessage` and `## RecoveryAckMessage` sections with field tables and example JSON. `docs/contract/v3.md` is untouched (`git status` clean). Three doc defects: wrong direction sentence, copy-pasted field text, and wrong topic-segment column — see issue 3. |

## Top issues (max 5), ranked

1. **blocker — the recovery-topic test required by requirement 8c does not
   exist.** The lesson names the file and the content: "`src/Messaging.Tests/
   TopicSchemeTests.cs` — add test for recovery topics (command/ack/wildcard)".
   `TopicSchemeTests.cs` still has only the three pre-existing facts
   (per-shuttle branch, wildcards, alarm topics). This is not a stylistic gap:
   **mutation M4** (change `RecoveryAck` to return `{root}/{id}/recoveryAckX`)
   leaves the whole suite **green** — nothing in the repo pins the recovery
   topics or the `+/recoveryAck` wildcard. When the wiring slice subscribes
   `AllRecoveryAcks` and the dashboard later publishes on
   `{root}/{id}/recoveryCommand`, a topic typo is discovered on a live broker
   with a shuttle that never receives its recovery command and stays faulted in
   an aisle — while the manager, having sent the command, waits for an ack that
   was published to a topic nobody subscribed to. Topics are the contract's
   addressing layer; they get a test like everything else.

2. **blocker — requirement 8b's "verify wire names/constants" is not done, and
   the roundtrip test structurally cannot do it.** `RecoveryMessageTests` builds
   its envelope from `MessageTypes.RecoveryCommand` (the constant), not the
   literal `"recoveryCommand"`, and asserts nothing about the serialized JSON.
   Three mutations prove the hole:
   - **M3**: rename the constant's value to `"recoveryCmd"` → suite **green**.
   - **M5**: set `ContractVersion.Current` back to `3` → suite **green**.
   - **M7**: add `[property: JsonPropertyName("accepted")]` (lowercase) to
     `RecoveryAckMessage.Accepted` → suite **green**. A roundtrip is symmetric:
     it serializes and deserializes with the same options, so *any* rename is
     invisible to it, and M7 is a silent wire-format change that would break a
     deployed v3/v4 peer that still expects `Accepted`.
   Add the two one-line assertions the lesson asks for —
   `Assert.Equal("recoveryCommand", MessageTypes.RecoveryCommand);`,
   `Assert.Equal("recoveryAck", MessageTypes.RecoveryAck);`,
   `Assert.Equal(4, ContractVersion.Current);` (or pin the literals through a
   serialize-and-inspect check) — `AlarmMessageTests.cs:36` already shows the
   house pattern. For a *contract* slice, the wire identity is the deliverable;
   today it is guarded only by a grep in the lesson, not by the suite.

3. **should-fix — `docs/contract/v4.md` describes `RecoveryCommandMessage`
   backwards, and mis-states the topic segments.** Line 209 says *"Sent by the
   shuttle to the fleet manager."* — the direction is manager → shuttle, as
   record 26 Decision A says, as the example on line 224 shows
   (`"SenderId": "fleet-manager"`), and as `TopicScheme.RecoveryCommand` (a
   per-shuttle *command* branch) requires. Line 214's field text *"Id of the
   shuttle this move command is addressed to"* is copy-paste from
   `MoveCommandMessage`. And in the summary table (lines 24-25) the third
   column — "Topic kind segment" — says `command` and `ack`, but the actual
   segments per `TopicScheme` are `recoveryCommand` and `recoveryAck` (compare:
   `CommandAckMessage`'s segment really is `ack`). This file is the source of
   truth the M7 dashboard will be built from; a reader who trusts column three
   subscribes `+/ack` and `+/command` and never sees a recovery message.

4. **should-fix — the recovery success test does not pin what record 26 says
   the transition preserves.** `Recover_from_fault_enters_idle_and_clears_fault_reason`
   (`StateTransitionTests.cs:172-184`) asserts `State` and `FaultReason` only.
   **M8** (Recover also does `_position = string.Empty;` — a shuttle that
   forgets where it is after recovery) leaves the suite **green**. Record 26
   Decision B makes "preserves position and carried pallet" part of the
   behaviour, and a recover that resets the position is exactly the bug that
   gets a shuttle re-routed from a lane it is physically still sitting in. One
   extra line (`Assert.Equal("A01-L01", shuttle.Position);`) pins it.

5. **consider — the repo's two index READMEs are now stale.**
   `src/Contracts/README.md:29` still lists the message records as
   "`MoveCommandMessage`, `ShuttleStatusMessage`, `CommandAckMessage`, `AlarmMessage`"
   and `:42` still says MessageTypes holds "the kind strings (`moveCommand`,
   `commandAck`, `shuttleStatus`, `alarm`)". `src/Messaging/README.md:49` says
   the wildcards are "`AllAcks`/`AllStatuses`/`AllAlarms`" and `:51` says the
   family enum is "`Command/Ack/Status/Alarm`". These READMEs are the map a
   reader uses before opening the code; every lesson that adds a kind has left
   them a little more wrong. Cheap to fix alongside this slice. Two smaller
   notes: `v4.md` moves the two Alarm sections after `ShuttleStatus` (v3 had
   them before `MoveCommandMessage`), which makes a version-to-version diff of
   the contract noisier than it needs to be; and `RecoveryMessageTests.cs`
   mixes tabs and spaces where every neighbouring `Contracts.Tests` file uses
   4 spaces.

## What is solid

- `Shuttle.Recover` is exactly the lesson's shape and follows the file's own
  transition discipline: `EnsureInOrder` → guard → fields → `_lastEventAt`,
  same as `RaiseFault` and `CompleteMove`. Both of its tests are load-bearing
  (M1: drop the `_faultReason = null` clear → red; M2: drop the Fault-only
  guard → red), including the exact-message assertion on the rejection.
- The contract additions are disciplined: additive only, `MessageEnvelope` and
  every pre-existing kind untouched, `docs/contract/v3.md` unchanged, and
  `v4.md` carries the version bump into every example and into the closing
  "Changing this contract" paragraph. That is how an append-only contract
  history should look.
- Both new records match the house convention of *plain* positional records —
  no attributes, no serializer using — which is what the codebase actually
  does, not what the lesson text claims it does. Reading the code instead of
  the prose was the right call.
- The QoS detour was caught and reverted cleanly: `QosOptions` is the original
  4-field record, `MqttMessageBusOptions.cs` shows no diff, and the mapping
  reuses `_options.Qos.Command` in the right place (before the default throw).
  No hard-coded topic, name, or timeout was introduced anywhere in the slice.

## Explain-it check

Answer these out loud or in writing before the next lesson. They are not
answered here.

1. `Recover` throws `ShuttleRejectedException` when the shuttle is not in
   `Fault`, and the future serve loop will call it on a message from the bus.
   Commands are retried and can be duplicated (`ReliableCommandSender`, the
   idempotency guard). Trace what happens when the manager re-sends a
   `recoveryCommand` because the ack was lost: the shuttle has already
   recovered, so `Recover` throws — should the endpoint ack `Accepted: false`,
   ack `true`, or treat it as a duplicate? What does that answer tell you about
   whether `Recover`'s rejection is a *command* rejection or a *state-machine*
   rejection, and which of the two belongs on the wire?
2. `MessageFamily.Recovery => _options.Qos.Command`, and `QosOptions` has a
   separate `Ack` field. When the wiring slice publishes the *ack* direction
   with `MessageFamily.Recovery`, which QoS does it get, and what changes if an
   operator configures `Ack: ExactlyOnce` and `Command: AtLeastOnce` for the
   other lanes? State the cost of each option: one family per direction
   (`Recovery` + reuse `Ack` for the ack), a fifth `RecoveryAck` QoS field, or
   what you have now.
3. Contract v4 is additive, but `ShuttleEndpoint.cs:37` and
   `FleetServeLoop.cs:60` both drop any envelope whose `ContractVersion` is not
   `ContractVersion.Current`. Walk a mixed-version rollout — manager upgraded to
   4, shuttle still on 3 — and say exactly which messages are dropped, on which
   side, and what an operator sees when a fault happens mid-upgrade. Then say
   what the code would have to do for "old code ignores unknown kinds" (the
   lesson's Survey 3 claim) to actually be true.

Also study these lines/blocks; they look unclear, copied, or easy to gloss over:

- `docs/contract/v4.md:209` and `:214` — the direction sentence and the
  "move command" field text. Which one of the three artifacts (sentence, field
  text, example JSON) is wrong, and which two agree?
- `docs/contract/v4.md:24-25` — the summary table's third column. Read
  `TopicScheme.cs:21-23` and say what the real topic kind segments are.
- `src/Contracts.Tests/RecoveryMessageTests.cs:12-13` — the envelope is built
  from `MessageTypes.RecoveryCommand`. Explain why M3 (changing that constant's
  value to `"recoveryCmd"`) leaves this test green, and write the one line that
  would turn it red.
- `src/ShuttleCore.Tests/StateTransitionTests.cs:172-184` — walk the assertions
  and name the property a buggy `Recover` could clobber that no assertion
  covers (M8's answer). Why is that property the one record 26 singles out?
- `src/MqttMessaging/MqttMessageBus.cs:99-104` — the five-case switch. Say why
  the `Recovery` case must sit before the `_ => throw` arm, and what the
  runtime error looks like if a new enum member is added and the mapping is
  forgotten (check `MqttMessageBusTests` for whether any test would notice).

## Revisit list

- A roundtrip test is symmetric and cannot fail on a rename. Wire identity
  (`MessageType` literal, `ContractVersion`, JSON property casing) needs
  assertions that compare against **literals**, not against the same constants
  the production code uses. `AlarmMessageTests.cs:36` is the model.
- Mutation testing as a habit: M3–M8 each passed while removing or corrupting
  something this lesson added. "The suite is green" and "the behaviour is
  pinned" are different claims; the second one is what a review measures.
- The contract has three layers that must agree: the C# types, the wire names,
  and the prose. A wrong sentence in `v4.md` is a defect in the product's
  interface, same as a wrong topic string — the doc is what the dashboard (M7)
  and the controls engineers will read.
- QoS families: one family per message *direction* versus per *kind*, and what
  silent config drift looks like when they are conflated.
- State-machine rejections vs command rejections, and how idempotency (lesson
  11's guard) interacts with a transition that may legitimately already have
  happened.

## Mutation results

Run in a throwaway copy under `/tmp/opencode/l18mut` (deleted); the workspace
`src/` was never modified. Baseline in the copy was RC 0, 8 `Passed!`, 137.
Each mutation was applied alone and the pure suite re-run. (An earlier batch of
M2/M3 runs was contaminated by an incremental-build mtime trap in my harness —
the restored source kept an older timestamp than the mutated assembly — so
every result below was re-produced after `bin`/`obj` were wiped and the
baseline re-confirmed green.)

| Mutation | Targeted test/behaviour | Result |
|---|---|---|
| M1 `Recover` does not clear `_faultReason` | `Recover_from_fault_enters_idle_and_clears_fault_reason` | **red** (1 failed) |
| M2 `Recover` loses the Fault-only guard | `Recover_from_idle_is_rejected` | **red** (1 failed) |
| M3 `MessageTypes.RecoveryCommand` value → `"recoveryCmd"` | wire-name identity | **green** — gap, issue 2 |
| M4 `TopicScheme.RecoveryAck` → `{root}/{id}/recoveryAckX` | recovery topics | **green** — gap, blocker 1 |
| M5 `ContractVersion.Current` → `3` | version pin | **green** — gap, issue 2 |
| M6 `MessageFamily.Recovery` → `_options.Qos.Status` | QoS mapping | **green** — not required by lesson 8; noted for the wiring slice |
| M7 `[JsonPropertyName("accepted")]` on `RecoveryAckMessage.Accepted` | wire casing | **green** — the roundtrip is symmetric, issue 2 |
| M8 `Recover` also clears `Position` | position preservation (record 26) | **green** — gap, issue 4 |

Two of the lesson's behaviours are pinned (the transition and its guard); the
contract half of the slice — kinds, version, topics, JSON casing — is pinned by
nothing.

## Note for the professor

- **Lesson defect, requirement 3/4 and hints Tier 1:** both say the new records
  should carry `JsonPropertyName` attributes "like other message types", and
  the hints say "Copy the envelope pattern exactly (JsonPropertyName attributes
  match)". The existing records (`MoveCommandMessage`, `CommandAckMessage`,
  `AlarmMessage`, `ShuttleStatusMessage`) have **no** attributes and no
  `System.Text.Json.Serialization` using. The owner followed the code and was
  right to; a student who follows the prose will add attributes no other record
  has. Fix the requirement text and the hint to say "plain positional records,
  same as `MoveCommandMessage` — the serializer's default casing is the wire
  format".
- Requirement 8c is unambiguous and was not done — that part is on the owner,
  not the lesson. Requirement 8b's "Also verify wire names/constants" is one
  short clause; spelling out *how* (assert the literals, as `AlarmMessageTests`
  does) would have closed blocker 2.
- No other lesson defect: the concept list has exactly three items, all three
  are taught in the body, and the slice stayed inside its cap. The "Not in this
  lesson" list correctly fences off the serve-loop/endpoint wiring, and the
  owner did not smuggle any of it in — the production diff is exactly
  `Recover`, two records, two constants, three topic methods, one enum member,
  and one QoS case.
- Consider saying explicitly in a future contract lesson that a contract doc
  section must state direction, sender, and topic segment, since the M7 slice
  will implement against `v4.md` rather than against this lesson's prose.

## README

No change. This is not a pass, and per the milestone-table rule the README is
untouched on a not-yet verdict. M5 stays **in progress**: it still owes the
blockers above, `palletNotDetected` fault injection/raising, the actual
recovery wiring (manager sends the command on operator ack, endpoint calls
`Recover` and publishes healthy status), and the M5 done-when proof.

---
---

# Re-review — lesson 18 (2026-10-17)

**Reviewer:** Reviewer agent
**Verdict: pass**

Re-review of the same working tree after the two blockers above. Everything
below was re-verified from scratch; nothing from the first run was trusted.
This re-review modified no file under `src/`. The mutation runs were done in a
fresh throwaway copy `/tmp/opencode/l18mut2` (deleted, together with its
build/test logs; the workspace `git status` after the re-review is identical to
before it). No broker, database, or container was started, and no `dotnet run`
was left behind. The integration suite (`Category=Integration`) was again **not**
run — this slice still touches no transport code path; that claim remains
**not yet verified** by execution.

## Gate results (verbatim, fresh)

1. `cd src && dotnet build` — **RC 0**: `0 Warning(s)`, `0 Error(s)`.
2. `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"`
   — **RC 0**, 8 `Passed!`, 0 `Failed!`, **138** tests total:
   ```
   Passed!  - Failed:     0, Passed:    22, Skipped:     0, Total:    22 - FleetCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    31, Skipped:     0, Total:    31 - ShuttleCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     8, Skipped:     0, Total:     8 - Contracts.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    10, Skipped:     0, Total:    10 - Messaging.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2 - MqttMessaging.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7 - ShuttleSim.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:     6, Skipped:     0, Total:     6 - TrafficCore.Tests.dll (net10.0)
   Passed!  - Failed:     0, Passed:    52, Skipped:     0, Total:    52 - FleetManager.Tests.dll (net10.0)
   ```
   Spread vs the review-17 baseline (134): `ShuttleCore.Tests` 29→**31**
   (+2, the two `Recover` facts), `Contracts.Tests` 7→**8** (+1), and — new
   since the failed review — `Messaging.Tests` 9→**10** (+1, the recovery-topic
   fact). Everything else unchanged. 134 + 4 = **138**.

## Blocker resolution

1. **Blocker 1 (missing recovery-topic test) — resolved.**
   `TopicSchemeTests.TopicScheme_recovery_topics_use_the_per_shuttle_branch`
   exists, is green, and asserts the three literals
   `shuttle/SH-01/recoveryCommand`, `shuttle/SH-01/recoveryAck`,
   `shuttle/+/recoveryAck`. Mutations **M10** (`TopicScheme.RecoveryAck` →
   `{root}/{id}/recoveryAckX`) and **M11** (`AllRecoveryAcks` →
   `{root}/+/recoveryAckX`) each turn it red (1 failed). The old M4 hole is
   closed: no topic typo in the recovery branch survives the suite.
2. **Blocker 2 (no wire-name/version pinning) — resolved.**
   `RecoveryMessageTests` now carries the three literal assertions the lesson
   asked for (`Assert.Equal("recoveryCommand", MessageTypes.RecoveryCommand);`,
   same for `recoveryAck`, and `Assert.Equal(4, ContractVersion.Current);`)
   plus `Assert.Contains("\"Accepted\"", ackJson)`. Mutations confirm each is
   load-bearing: **M9** (constant → `"recoveryCmd"`) red,
   **M12** (`ContractVersion.Current` → `3`) red, **M13**
   (`[property: JsonPropertyName("accepted")]` on `Accepted`) red. The
   serialize-and-inspect check catches the casing change the symmetric
   roundtrip structurally cannot.

## Should-fix / consider items from the first review

| First-review item | Status |
|---|---|
| 3 `v4.md` direction sentence backwards | **fixed** — line 209 now "Sent by a fleet manager to the shuttle." |
| 3 `v4.md` "move command" field text | **fixed** — line 214 now "Id of the shuttle this recovery command is addressed to". |
| 3 `v4.md` summary-table topic segments | **fixed** — rows 24-25 now say `recoveryCommand` / `recoveryAck`, matching `TopicScheme.cs`. |
| 4 position-preservation assertion | **fixed** — `Recover_from_fault_enters_idle_and_clears_fault_reason` now asserts `Assert.Equal("A01-L01", shuttle.Position)`. Mutation **M14** (Recover also clears `_position`) turns it red. |
| 5 stale `src/Contracts/README.md` / `src/Messaging/README.md` | **mostly fixed** — record lists, files tables, `MessageFamily` member list and `AllRecoveryAcks` wildcard all updated. One stale line remains (leftover 3 below). |
| 5 `v4.md` section reordering vs v3 | **not fixed** — still a consider-level nit (leftover 2). |
| 5 tabs/spaces in `RecoveryMessageTests.cs` | **not fixed** — still a consider-level nit (leftover 4). |

Whole-file sanity of `v4.md` re-checked: every example JSON says
`"ContractVersion": 4`, title/`Version:` say 4, the closing paragraph points at
`v5.md`/`v4.md`, no trailing whitespace and no tabs anywhere, and
`docs/contract/v3.md` is untouched (`git status` clean for it). The records are
still plain positional records with no `[JsonPropertyName]` and no serializer
using, matching every neighbouring contract record. `MessageFamily.Recovery`
still maps to `_options.Qos.Command` (`MqttMessageBus.cs:103`), and
`QosOptions` is still the original 4-field record — no `Recovery` field was
sneaked into the config surface. `Shuttle.Recover` is unchanged from the
first review's reading: `EnsureInOrder` → Fault-only guard → state/reason/
`_lastEventAt`, position and carried pallet untouched.

## Acceptance criteria (re-checked)

| Req | Status | Evidence |
|---|---|---|
| 1 `Shuttle.Recover` | pass | unchanged from first review; position preservation now also asserted in tests (M14 red). |
| 2 `MessageTypes` + version 4 | pass | literal assertions pin both wire names and `Current == 4` (M9/M12 red). |
| 3 `RecoveryCommandMessage` | pass | plain record, no attributes. |
| 4 `RecoveryAckMessage` | pass | plain record; wire casing pinned by `Assert.Contains` (M13 red). |
| 5 `TopicScheme` recovery topics | pass | code + literal test; M10/M11 red. |
| 6 `MessageFamily.Recovery` | pass | member present, nothing else moved. |
| 7 `ToMqttQos` mapping | pass | `Recovery => Qos.Command`, `QosOptions` unchanged (M6-style mapping change still unpinned by any test — see leftover 5). |
| 8 tests | pass | 8a/8b/8c all present and load-bearing; suite 138, RC 0. |
| 9 `docs/contract/v4.md` | pass | full copy, version 4 throughout, direction/field-text/topic-segments corrected; one new example-level inconsistency (leftover 1). |

## Top issues (re-review), ranked

No blockers remain. Leftovers, none of which block:

1. **should-fix — `docs/contract/v4.md` `RecoveryAckMessage` example has the
   wrong sender.** Line 252 says `"SenderId": "fleet-manager"` while the
   section (line 235) says "Sent by a shuttle to the fleet manager", and the
   `CommandAckMessage` example correctly uses `"SH-01"`. Same class of defect
   as the first review's issue 3: this file is what the M7 dashboard and the
   wiring slice implement against, and an operator debugging "why did the
   manager treat its own ack as a shuttle report" gets their first hint from
   this example. One-line fix; correct it the next time the contract is touched
   rather than in a dedicated pass.
2. **consider — `v4.md` still reorders the `AlarmMessage`/`Alarm` sections**
   after `ShuttleStatus` (v3 had them before `MoveCommandMessage`), so a
   `diff v3.md v4.md` shows a 100-line block move in addition to the real
   additions. Order the new sections to match the previous page next time.
3. **consider — `src/Contracts/README.md:42`** still lists the kind strings as
   "(`moveCommand`, `commandAck`, `shuttleStatus`, `alarm`)" — the two recovery
   kinds were added to line 29 and the files table but not here.
4. **consider — `src/Contracts.Tests/RecoveryMessageTests.cs` still mixes tabs
   and spaces** (lines 11-18) in a project where every other file uses 4
   spaces uniformly.
5. **consider — the `MessageFamily.Recovery => Qos.Command` mapping is still
   pinned by no test** (old mutation M6: change the arm to `Qos.Status`, suite
   stays green). Not required by this lesson and `MqttMessaging.Tests` has no
   seam for it yet; make it a named check when the wiring slice decides the
   ack direction's family (explain-it question 2).

## What is solid (re-review delta)

- The fixes are exactly the shape the review asked for and nothing else moved:
  `git diff` shows the additions confined to the two test files and the two
  READMEs; no production code changed between the failed review and this one.
- The literal assertions follow the house pattern (`AlarmMessageTests.cs:36`)
  rather than inventing a new one, and the `Assert.Contains` on the serialized
  ack is a genuinely good addition — it is the check that keeps a future
  attribute-based refactor from silently renaming the wire casing.
- Both READMEs describe the recovery kinds in the same vocabulary the code
  uses (manager → shuttle body, `Accepted` plus optional `Reason`).

## Explain-it check (refreshed)

Answer these out loud or in writing before the next lesson. They are not
answered here. The three questions from the first review are still live —
work them too, in their original form further up this file:

1. `Recovery_messages_survive_json_roundtrip` now mixes two kinds of assertion:
   a symmetric roundtrip (`Assert.Equal(commandMessage, deserializedCommand)`)
   and three literal/pinning assertions on the constants and the JSON text.
   For each of the six mutations M9–M14 further up this file, say which
   assertion catches it and why the roundtrip alone cannot. Then write the
   seventh mutation you think this test would still survive, and say whether
   pinning it belongs in this test, in `AlarmMessageTests`, or nowhere.
2. `Assert.Contains("\"Accepted\"", ackJson)` passes today because the
   serializer's default casing matches the property name. Explain what
   `JsonSerializerOptions` would make this assertion pass while the actual
   wire format changed under it, and why asserting on the JSON *text* is
   stronger than asserting on `ackMessage.Accepted` after a roundtrip.
3. The topic test hard-codes the root `"shuttle"`:
   `new TopicScheme("shuttle")`. Trace what breaks in the wiring slice and the
   integration environment if the deployed root changes to `"fleet/shuttle"`
   while this test and the production config disagree — which component picks
   the real root, and what does the test prove (and not prove) about it?

Also study these lines/blocks; they look unclear, copied, or easy to gloss over:

- `src/Contracts.Tests/RecoveryMessageTests.cs:11-18` — the envelope is still
  built from `MessageTypes.RecoveryCommand` while lines 30-32 assert the
  literals. Explain why both exist and which one is doing the load-bearing work
  for M9.
- `src/Contracts.Tests/RecoveryMessageTests.cs:33` — `Assert.Contains` on raw
  JSON text. Why is this the only assertion in the file that can see M13's
  rename, and what is its weakness (it checks presence, not full shape)?
- `docs/contract/v4.md:252` — `"SenderId": "fleet-manager"` in the
  `RecoveryAckMessage` example. Compare with `docs/contract/v4.md:86` (the
  `CommandAckMessage` example) and decide which one the wiring slice should
  copy when it publishes the ack.
- `src/Messaging.Tests/TopicSchemeTests.cs:35-42` — three literal strings in a
  row. Explain the cost this test imposes the next time someone renames a topic
  kind on purpose (which files must change in the same commit), and why that
  cost is the point.
- `src/ShuttleCore.Tests/StateTransitionTests.cs:176-184` — the `Position`
  assertion is one line. Explain the real-world failure it stands in for: a
  recover that blanked the position, and what the fleet manager would do with
  an empty-position status from an idle shuttle.

## Revisit list (additions)

- Pinning strategies ranked by what they actually catch: symmetric roundtrip <
  `Assert.Contains` on wire text < full literal-equality on serialized JSON <
  golden files. Know which defect class each tier is for; do not reach for the
  strongest tier everywhere.
- Doc-as-interface: `v4.md` has now been wrong in two different ways across two
  reviews (direction sentence, topic segments, and now sender in an example).
  When the contract doc is the implementation source for a later milestone,
  treat its example JSON as test fixtures — every field in them is a claim.
- The difference between "the test file was updated" and "the suite's coverage
  grew": Messaging.Tests 9→10 is what closed blocker 1; editing assertions
  inside an existing 8-test assembly is what closed blocker 2. Both kinds of
  growth are legitimate; know which one you are making.
- Mixed whitespace in a file you just created is a small signal: it means the
  editor was assembling code from copied fragments. Read the file end-to-end
  once before writing the test — that habit also catches wrong variable names
  in copied fixtures (the `Rec-123` envelope id in this test is still a move
  command's GUID-style id from the example JSON, not a recovery-flavoured one).

## Note for the professor

- The lesson defect from the first review is still open: requirements 3/4
  (lines 269, 272) and the lecture body (lines 131-152) still show
  `JsonPropertyName` attributes that no message record in the repo has, and the
  hints file still tells the student to copy them. The owner again followed the
  code and was again right to. Fix the prose.
- Requirement 8b's "Also verify wire names/constants" was the whole of blocker
  2; the owner's fix shows the missing piece was one sentence of method, not
  effort. Spell out "assert the literals, do not assert the constants against
  themselves" in the revised text.
- No other lesson defect found. Three concepts, all taught, cap respected;
  the "Not in this lesson" fence held (no serve-loop/endpoint wiring snuck in —
  production diff is still exactly `Recover`, two records, two constants, three
  topic methods, one enum member, one QoS arm).

## README (re-review)

Milestone table row untouched: M5 stays **in progress** — this pass covers one
M5 slice. The M5 verification note in the root `README.md` was updated with the
lesson-18 slice, the verifying command, and the observed 138-test totals. M5
still owes: `palletNotDetected` fault injection/raising, the actual recovery
wiring (manager sends `recoveryCommand` on operator ack; endpoint calls
`Recover`, publishes `recoveryAck` and healthy status), and the M5 done-when
proof.
