# Review 05: The message contract and the broker boundary (lesson 05, M2 first slice)

**Date:** 2026-10-10
**Reviewed:** commits `862b0ef` (`M2: Refactor code structure…` — docs-only: it flips the M1 README
row to `done`, appends the second review-04 re-review, and carries the professor's lesson 05, hints,
and decisions 07–09; 7 files, +1856/−2) and `8e88760` (`M2: feat:
Implement messaging system with MQTT integration` — the owner's implementation; 36 files, +1091/−40),
on top of `cb895a5` (M1 pass). Branch `master`.
**Working tree at review time:** the professor's **uncommitted** lesson-05 gate fixes in
`docs/lessons/05-message-contract-and-transport.md` and `…-hints.md` (the Phase 2 wire-name gate and
the app-code rescoping of the no-literal/no-sleep greps). I reviewed the owner's code against the
**current** (fixed) lesson, and I checked the fixes themselves (they are sound — see
[The professor's gate fixes](#the-professors-gate-fixes)).
**Scope:** lesson 05 against its own assignment. **M2 does not close here** — retry, ack timeout,
alarms, recovery, and heartbeats are lesson 06, and the spec's full M2 done-when ("a lost
acknowledgement is retried") is not met by design.
**Contract checked:** `docs/contract/v1.md` untouched; `docs/contract/v2.md` added by `8e88760`.

**Verdict: not yet (lesson 05 / M2 first slice).**

The hard parts of this lesson genuinely work. The broker round trip is real: I started the compose
broker, ran the shuttle in `serve` mode, ran `FleetManager move SH-01 A01-L02`, and watched a
correlated `commandAck (Accepted:true)` followed by a `moving` status at `A01-L01` and an `idle`
status at `A01-L02` five seconds later. The idempotency guard is real and **mutation-proven**: make
`IsDuplicate` always return `false`, or make `MarkCompleted` a no-op, and both the guard test and
`Duplicate_move_command_is_acknowledged_but_not_run_twice` go red. The suites are green (6/6
`Passed!` lines, 0 "No test is available"), the reference graph is exactly right (12 projects,
`Messaging` sees only `Contracts`, `ShuttleCore` sees nothing), and the fresh-container run is green.

What blocks the slice is a **lesson requirement the owner has now skipped across four reviews**, and
a **contract document that is not finished and actively misdescribes the messages**:

1. **R2/R4 are not fixed.** `src/FleetManager.Tests/StartTests.cs:21` is still a bare
   `p.WaitForExit()` with no bound; line 10 still has the nine-space indent; the file still ends
   with no trailing newline. Requirement 8 says "R2/R4 first: fix … This is the review-03 defect,
   due now", and Phase 2b greps for exactly these three things. All three go red.
2. **`docs/contract/v2.md` is half-written and wrong.** It declares `Version: 2` and copies
   `ShuttleStatus` forward, then appends four rows — `MessageEnvelope`, `MoveCommandMessage`,
   `CommandAckMessage`, `ShuttleStatusMessage` — typed `string|string|string` as if they were
   *fields of `ShuttleStatus`*, and an empty JSON sample that lists them as `""`. It documents none
   of the four messages and does not contain the wire names. Requirement 12 is unmet, and the Phase
   2 greps that "pass" do so for the wrong reason (they find the bare names).

Everything else is verified green and mutation-tested, so this is a small, well-understood fix away
from closing the slice — but AGENTS.md says tests must pass *and* the assignment's own requirements
must hold, and two do not.

---

## Commands run, verbatim key output

Exit codes read directly (never through a pipe) except where the lesson captures output with
`mktemp`, and the acceptance block was run **verbatim from the current lesson file**, from the
directories the block names. Mutations ran against throwaway copies under `/tmp/opencode`, deleted
afterwards; no experiment touched `src/`.

### Phase 1 — four new projects, twelve total, the right edges, no forbidden ones

```
$ for p in Messaging Messaging.Tests MqttMessaging MqttMessaging.Tests; do
    test -f "src/$p/$p.csproj" || echo "missing project: $p"; done     (no output)
$ grep -c '<Project Path' src/ShuttleFleet.slnx                        prints 12
$ edge Contracts Messaging … edge MqttMessaging FleetManager           (no "missing edge:" output)
$ ! grep -q 'ProjectReference' src/ShuttleCore/ShuttleCore.csproj      exit 0
$ for bad in MqttMessaging ShuttleCore ShuttleSim FleetManager; do
    ! grep -q "..\\$bad\\$bad.csproj" src/Messaging/Messaging.csproj
    || echo "Messaging must not see $bad"; done                        (no output)
$ ! grep -q 'MQTTnet' src/Messaging/Messaging.csproj                   exit 0
$ grep -q 'MQTTnet' src/MqttMessaging/MqttMessaging.csproj             exit 0
$ find src -name '*.cs' … -exec md5sum {} + | sort | uniq -w32 -D      (no output)
```

All thirteen required edges present, `ShuttleCore` still references nothing, `Messaging` sees
neither the adapter nor the domain nor the apps, and only `MqttMessaging` carries `MQTTnet`. The
md5 duplicate check is silent. `Contracts`, `ShuttleCore`, and `Messaging` all have **zero**
`ProjectReference`/`PackageReference` beyond their declared edges.

### Phase 2 — contract v2 exists, v1 untouched, the fixed names exist

```
$ grep -qE '^Version: 2$' docs/contract/v2.md                          exit 0
$ grep -qE '^Version: 1$' docs/contract/v1.md                          exit 0
$ for s in MessageEnvelope MoveCommandMessage CommandAckMessage ShuttleStatusMessage;
    grep -rq "$s" src/Contracts --include='*.cs' || echo "missing type: $s"   (no output)
$ grep -rq 'const int Current = 2' src/Contracts --include='*.cs'      exit 0
$ for k in moveCommand commandAck shuttleStatus;
    grep -q "const string [A-Za-z]* *= *\"$k\"" src/Contracts/MessageTypes.cs
    || echo "missing wire name: $k"                                    (no output)
$ grep -qE '^Version: 2' docs/contract/v2.md ; grep -q 'MessageEnvelope' … ; exit 0 / 0
```

All green. `git diff cb895a5..HEAD -- docs/contract/ src/Contracts/ShuttleStatus.cs` moves exactly
one file — `docs/contract/v2.md` (+42) — so `v1.md` and `ShuttleStatus` are untouched.

### Phase 2 rules — no literals where configuration belongs; no sleeps

```
$ grep -rnE 'Thread\.Sleep|Task\.Delay' src --include='*.cs' \
    --exclude-dir='*.Tests' --exclude-dir='obj' --exclude-dir='bin'    exit 1 (nothing)
$ grep -rnE '"shuttle/[A-Za-z0-9+_/-]+"' src --include='*.cs' …        exit 1 (nothing)
$ grep -rniE '127\.0\.0\.1|localhost|:1883|Host=' src --include='*.cs' … exit 1 (nothing)
$ grep -rn 'WaitForExit()' src/FleetManager.Tests --include='*.cs'
    src/FleetManager.Tests/StartTests.cs:21:        p.WaitForExit();   <-- should be nothing (R2)
$ grep -q 'WaitForExit(' src/FleetManager.Tests/StartTests.cs          exit 0 (trivial)
$ grep -qE 'Timeout' src/FleetManager.Tests/StartTests.cs              exit 1 <-- no named bound (R2)
$ grep -rq '127\.0.0.1\|1883' src/FleetManager/appsettings.json src/ShuttleSim/appsettings.json  exit 0
$ find src -name 'Class1.cs' -o -name 'UnitTest1.cs'                    (no output)
$ for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do
    grep -q 'Assert' "$f" || echo "$f"; done                           (no output)
```

The three scoped gates print nothing — app code has no sleep, no full-topic literal, and no
address literal. **The R2 gate goes red**: the bare `WaitForExit()` at `StartTests.cs:21` is still
there, and there is still no `Timeout` constant anywhere in the file.

### Phase 2b — R2/R4 are actually fixed (review 03's items)

```
$ grep -q 'WaitForExit(' src/FleetManager.Tests/StartTests.cs          exit 0
$ ! grep -qE '[^ ]WaitForExit\(\)' src/FleetManager.Tests/StartTests.cs exit 1  <-- bare() present (R2)
$ grep -nE '^         [^ ]' src/FleetManager.Tests/StartTests.cs
    10:         var dll = Path.Combine(AppContext.BaseDirectory, "FleetManager.dll");   <-- (R4)
$ tail -c1 src/FleetManager.Tests/StartTests.cs | od -An -c
       }                                                               <-- no \n (R4)
$ tail -c1 … | od -An -c | grep -q '\\n'                               exit 1  <-- no trailing newline (R4)
```

Three of the four Phase 2b gates fail. R2 and R4 are exactly as they were at `cb895a5`:
`git diff cb895a5..8e88760 -- src/FleetManager.Tests/StartTests.cs` is empty (the file is not in
the owner's implementation commit at all).

### Phase 3 — from `src/`: build, run the suite, walk the new names

```
$ dotnet build                                                         exit 0, 0 Warning(s), 0 Error(s)
$ T=$(mktemp); dotnet test > "$T" 2>&1                                 exit 0
$ grep -c 'Passed!' "$T"                                               prints 6
$ grep -c 'No test is available' "$T"                                  prints 0
$ L=$(mktemp); dotnet test --list-tests > "$L" 2>&1
$ for t in … 13 names …; do grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done   (no output)
```

The six suites, verbatim:

```
Passed! - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll    (net10.0)
Passed! - Failed: 0, Passed:  4, … Contracts.Tests.dll      (net10.0)
Passed! - Failed: 0, Passed:  5, … Messaging.Tests.dll      (net10.0)
Passed! - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll  (net10.0)
Passed! - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll     (net10.0)
Passed! - Failed: 0, Passed:  1, … FleetManager.Tests.dll   (net10.0)
```

Exact `Passed!` count 6, `No test is available` 0, and the anchored name walk is silent — all 13
anchored names exist. `ShuttleCore.Tests` is still **22/22**, the M1 reference count. The two new
MQTT tests run against an **in-process** `MQTTnet.Server` on a free port (no Docker).

### Phase 4 — no regression: the M1 demo and the manager's start line still hold

```
$ F=$(mktemp); dotnet run --project ShuttleSim/ShuttleSim.csproj > "$F" 2>&1   exit 0
$ grep -c '"ShuttleId"' "$F"                                           prints 7
$ G=$(mktemp); dotnet run --project FleetManager/FleetManager.csproj > "$G" 2>&1 exit 0
$ grep -q 'Fleet Manager Coming Online' "$G"                           exit 0
```

I verified the demo output: 7 `"ShuttleId"` lines, four `idle` and one each of `loading`/`moving`/
`unloading`, and the `moving` line carries `"Position":"A01-L01"` (position does not jump early);
`00:00:17Z` and `00:00:19Z` both appear. The manager's M0 start line is intact.

### Phase 5 — the round trip over the real broker (start, move, assert, stop)

```
$ docker compose -f docker/compose.yml up -d broker                    exit 0
$ cd src && dotnet build ShuttleFleet.slnx && cd ..                    exit 0
$ dotnet …/ShuttleSim.dll serve > "$S" 2>&1 &   ; sleep 3
$ dotnet …/FleetManager.dll move SH-01 A01-L02 > "$M" 2>&1 ; MOVE_EXIT=$?
$ kill "$SHUTTLE_PID"; wait; docker compose -f docker/compose.yml down exit 0
$ echo "move exit=$MOVE_EXIT"                                          prints move exit=0
$ grep -q '"MessageType":"commandAck"' "$M"                           exit 0
$ grep -q '"Accepted":true' "$M"                                       exit 0
$ grep -q '"State":"moving"' "$M"                                      exit 0
$ grep -q '"State":"idle"' "$M"                                        exit 0
```

`$M` (the manager's stdout, verbatim — three JSON lines) and `$S` (the shuttle's):

```
{"Envelope":{"ContractVersion":2,"MessageType":"commandAck","MessageId":"ed33d2b4-…","CorrelationId":"724abb7a-…","SenderId":"SH-01","SentAt":"2026-10-10T04:35:07.7278067Z"},"Accepted":true,"Reason":null}
{"Envelope":{"ContractVersion":2,"MessageType":"shuttleStatus","MessageId":"86b09b9b-…","CorrelationId":null,"SenderId":"SH-01","SentAt":"2026-10-10T04:35:07.7278067Z"},"Status":{"ShuttleId":"SH-01","State":"moving","Position":"A01-L01","Timestamp":"2026-10-10T04:35:07.7278067Z","FaultReason":null}}
{"Envelope":{"ContractVersion":2,"MessageType":"shuttleStatus","MessageId":"c526f98d-…","CorrelationId":null,"SenderId":"SH-01","SentAt":"2026-10-10T04:35:12.7278067Z"},"Status":{"ShuttleId":"SH-01","State":"idle","Position":"A01-L02","Timestamp":"2026-10-10T04:35:12.7278067Z","FaultReason":null}}
Shuttle SH-01 listening on shuttle/SH-01/command
```

The ack's `CorrelationId` is the command's `MessageId` (`724abb7a-…`), the move is reported at
`A01-L01` (`moving`) and completes at `A01-L02` five seconds later (`idle`) — one lane × 5 s — with
no process sleep. This is a real broker round trip, observed.

### Phase 6 — the contract's v1 is untouched and the fresh container still passes

```
$ grep -qE '^Version: 1$' docs/contract/v1.md                          exit 0
$ grep -rq 'public record ShuttleStatus' src/Contracts --include='*.cs' exit 0
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build .     exit 0
$ U=$(mktemp); docker run --rm shuttle-fleet-build > "$U" 2>&1         exit 0
$ grep -c 'Passed!' "$U"                                               prints 6
```

The fresh container reports six `Passed!` lines (22 / 4 / 5 / 2 / 5 / 1) — the same verdict as the
host.

---

## Acceptance table

| # | Phase / criterion (lesson wording) | Evidence | Result |
|---|---|---|---|
| 1 | Phase 1: four `.csproj`s exist, loop prints nothing | printed nothing | met |
| 2 | `grep -c '<Project Path'` prints 12 | printed 12 | met |
| 3 | all 13 required `edge` checks present | no `missing edge:` | met |
| 4 | `ShuttleCore.csproj` references nothing | exit 0 | met |
| 5 | `Messaging` sees no adapter/domain/apps | no output | met |
| 6 | `Messaging` does not reference `MQTTnet` | exit 0 | met |
| 7 | `MqttMessaging` references `MQTTnet` | exit 0 | met |
| 8 | no `.cs` duplicated (md5) | prints nothing | met |
| 9 | Phase 2: `v2.md` has `^Version: 2$` | exit 0 | met |
| 10 | Phase 2: `v1.md` has `^Version: 1$` | exit 0 | met |
| 11 | Phase 2: the four types exist in `Contracts` | no output | met |
| 12 | Phase 2: `const int Current = 2` | exit 0 | met |
| 13 | Phase 2: three wire names in `MessageTypes.cs` | no output | met |
| 14 | Phase 2: `v2.md` grep (`Version`, `MessageEnvelope`, `CommandAckMessage`) | all exit 0 | met* |
| 15 | Phase 2 rules: no `Thread.Sleep`/`Task.Delay` in app code | nothing (exit 1) | met |
| 16 | Phase 2 rules: no full-topic literal in app code | nothing (exit 1) | met |
| 17 | Phase 2 rules: no address literal in app code | nothing (exit 1) | met |
| 18 | Phase 2 rules: no bare `WaitForExit()` in `FleetManager.Tests` | **found `StartTests.cs:21`** | **not met** |
| 19 | Phase 2 rules: `WaitForExit(` present | exit 0 | met |
| 20 | Phase 2 rules: a named `Timeout` bound in `StartTests.cs` | **exit 1 (none)** | **not met** |
| 21 | Phase 2 rules: addresses live in `appsettings.json` | exit 0 | met |
| 22 | Phase 2 rules: no `Class1.cs`/`UnitTest1.cs` | nothing | met |
| 23 | Phase 2 rules: every `[Fact]` file has an `Assert` | no output | met |
| 24 | Phase 2b: `WaitForExit(` present | exit 0 | met |
| 25 | Phase 2b: no bare `WaitForExit()` | **exit 1 (bare present)** | **not met** |
| 26 | Phase 2b: no nine-space indent | **`StartTests.cs:10`** | **not met** |
| 27 | Phase 2b: trailing newline | **last byte `}`, no `\n`** | **not met** |
| 28 | Phase 3: `dotnet build` exit 0, 0 warnings | exit 0, 0/0 | met |
| 29 | Phase 3: `dotnet test` exit 0 | exit 0 | met |
| 30 | Phase 3: `grep -c 'Passed!'` prints 6 | printed 6 | met |
| 31 | Phase 3: `No test is available` prints 0 | printed 0 | met |
| 32 | Phase 3: anchored name walk — all 13 names | no output | met |
| 33 | Phase 4: demo exit 0, 7 `"ShuttleId"` lines | exit 0, 7 | met |
| 34 | Phase 4: manager start line exit 0 | exit 0 | met |
| 35 | Phase 5: move exit 0, ack + accepted + moving + idle | move exit=0, all greps 0 | met |
| 36 | Phase 6: `v1.md` untouched + `ShuttleStatus` record | exit 0 / 0 | met |
| 37 | Phase 6: `docker build` exit 0 | exit 0 | met |
| 38 | Phase 6: `docker run` exit 0, 6 `Passed!` | exit 0, 6 | met |
| 39 | Req 6: `TravelTimeTo` additive; M1 suite still green | diff = one method; 22/22 | met |
| 40 | Req 12: `v2.md` documents the four messages/shapes | **only named as bogus `ShuttleStatus` fields** | **not met** |
| 41 | Req 5: MQTT tests live in `MqttMessageBusTests.cs` | file is `MQTTnetTest.cs` | **not met (consider)** |

**33 of 41 met.** The eight misses are **three root causes**: R2/R4 (rows 18, 20, 24, 25, 26, 27),
the unfinished `v2.md` (row 40), and a file-naming drift (row 41). Row 14 is marked met **\***: the
grep passes, but only because the four type names appear as malformed rows; it is the same
"passes for the wrong reason" class the reviews keep surfacing.

---

## Substance checks

### The round trip genuinely works

Traced: `FleetManager/Program.cs:38-54` builds a `MoveCommandMessage`, subscribes `{root}/+/ack`
and `{root}/+/status`, publishes to `{root}/{id}/command` at the command family's QoS, and awaits
the two `TaskCompletionSource`s under a `CancellationTokenSource(timeoutMs)` timer.
`ShuttleSim/ShuttleEndpoint.cs:24-80` parses the envelope, checks version/kind/recipient, checks the
guard, calls `StartMove`, acks, and publishes `moving` then `idle`. I ran it against the compose
broker and captured the three messages above. Fleet manager → shuttle → ack → status → idle status:
**observed, not inferred**. No `Thread.Sleep`/`Task.Delay` anywhere in app code (Phase 2 rules).

### Idempotency and ack idempotency are real — mutation-tested

Baseline (honest copy): `Duplicate_move_command_is_acknowledged_but_not_run_twice` **passes**. The
test asserts two acks, **both** `Accepted`, position `A01-L02`, and exactly **two** statuses —
i.e. a duplicate command is re-acked but produces no second `moving`/`idle` pair.

Two mutations, one at a time, in `/tmp/opencode/rev05`:

- **M1 — `IdempotencyGuard.IsDuplicate` always returns `false`** (remove the dedupe):
  ```
  Failed! - Failed: 1, Passed: 0, Total: 1
    Assert.All() Failure: 1 out of 2 items in the collection did not pass.
      Error: Assert.True() Failure
  ```
  The duplicate is driven again as a new command; the second `StartMove` fails (already `Moving`) and
  is re-acked `Accepted:false`, so the all-accepted assertion goes red.
- **M2 — `IdempotencyGuard.MarkCompleted` is a no-op** (nothing is remembered):
  ```
  Failed! - Failed: 2, Passed: 0, Total: 2   (Messaging.Tests IdempotencyGuard_*)
  Failed! - Failed: 1, Passed: 0, Total: 1   (ShuttleSim.Tests Duplicate_move_command_…)
  ```

So a repeated command that re-runs **is** caught, and the guard's recording path is genuinely
exercised. This is the spec's M2 done-when ("a repeated command does not run twice") proven at the
test level and mutation-sensitivity-checked, exactly as the reviews demand.

**Ack idempotency** is the first branch of the same test (`Assert.Equal(2, acknowledgements.Length)`
+ `Assert.All(… Assert.True)`): on the honest tree the duplicate re-acks `Accepted:true` and returns
before `StartMove`. Under M1/M2 the pair assertion fails. Same rigor, same test.

**Not tested, and not required here:** the *rejected* branch of record 09 §2 — a duplicate of a
**rejected** command is reprocessed (the guard is not marked on rejection). The endpoint code does
this correctly (`ShuttleEndpoint.cs:63-71`: the ack-on-reject returns before `MarkCompleted`), but
no test drives a rejected command twice. Requirement 11 does not name such a test, so this is a
**consider**, not a miss — flagged because the lesson's "Say it out loud" #3 turns on the asymmetry.

### `Messaging` purity and configuration

`Messaging.csproj` references **only** `Contracts`; no `MQTTnet`, no `ShuttleCore`, no apps
(Phase 1). `IMessageBus`, `TopicScheme`, `IdempotencyGuard`, `MessageEnvelopes`, `MessagingOptions`,
`MessageFamily`, `MessageReceived` are all there; no topic literal lives outside `TopicScheme`
(Phase 2 rules, exit 1). `ShuttleCore.csproj` is still reference-free. `ShuttleEndpoint` lives in
`ShuttleSim` (the app), not in the transport — the domain stays out of `Messaging`.

Configuration: host, port, client id, topic root, fleet-manager id, idempotency cache size, and the
per-family QoS all bind from the `Messaging` section of `appsettings.json` and are overridable by
environment variables (`Program.cs:67-94`, `AddEnvironmentVariables()`); `FleetManager:RoundTripTimeoutMs`
is read and validated positive (`Program.cs:33-35`). The only literals in app `Program.cs` are the
M1 scenario (2×2×2 rack, 5/2/2 timing, `SH-01`, `PLT-001`, `2026-01-01`), which record 06 §4 exempts
as data, and the demo is reached only with no arguments. No hard-coded host/port/topic/retry/size
found in app code.

### The `Shuttle.TravelTimeTo` addition is additive

`git diff cb895a5..8e88760 -- src/ShuttleCore/Shuttle.cs` adds exactly two lines — the public
`TravelTimeTo(string lane)` method — and changes nothing else. `CompleteMove` still uses the private
`TravelTime(fromLane, toLane)`; the new method computes from the current `_position`. No rule
changed, `ShuttleCore.Tests` is still 22/22, and `Messaging`/`Contracts` do not see `ShuttleCore`.
This is the one allowed change and it is correct.

### Contract version discipline

`docs/contract/v1.md` is byte-unchanged (`grep '^Version: 1$'` exit 0; the range diff moves no v1
line). `docs/contract/v2.md` declares `Version: 2`. Records 07 (envelope + topics), 08 (client +
QoS + configuration), and 09 (ack + idempotency) exist and cover the change, so the AGENTS.md
"decision record + version bump" rule is satisfied on the record side. The wire names in
`MessageTypes.cs` match record 07 and the implemented JSON exactly (`moveCommand`/`commandAck`/
`shuttleStatus`, `ContractVersion.Current = 2`, PascalCase envelope fields). **The v2.md document
itself is the problem** — see the blocker below.

---

## Findings

### Blockers (drive the verdict)

**B1 — R2/R4 are still unmet, and requirement 8 says "R2/R4 first".**
`src/FleetManager.Tests/StartTests.cs:21` is a bare `p.WaitForExit()` with no timeout (R2); line 10
carries the nine-space indent; the file has no trailing newline (R4). This is the review-03 defect,
carried through reviews 03 and 04 as "due before the M2 lesson", and folded into lesson 05
requirement 8. The lesson even supplies the reference pattern in the same repo —
`src/ShuttleSim.Tests/DemoTests.cs:9,20-22`: a named `const int DemoTimeoutMs = 30_000;`,
`process.WaitForExit(DemoTimeoutMs)`, and `if (!exited) process.Kill(entireProcessTree: true);`.
The fix is a copy-and-adapt of those four lines. `StartTests.cs` is not touched by `8e88760` at all
(`git diff cb895a5..8e88760 -- src/FleetManager.Tests/StartTests.cs` empty). Phase 2 rules and
Phase 2b are the gates that catch it, and they went red.

**B2 — `docs/contract/v2.md` is unfinished, and misdescribes the messages (requirement 12).**
`docs/contract/v2.md:16-19` appends four rows to the **`## ShuttleStatus`** table:

```
| `MessageEnvelope`      | `string` | `string` | MessageEnvelope | yes |
| `MoveCommandMessage`   | `string` | `string` | MoveCommandMessage | yes |
| `CommandAckMessage`    | `string` | `string` | CommandAckMessage | yes |
| `ShuttleStatusMessage` | `string` | `string` | ShuttleStatusMessage | yes |
```

and the JSON sample (`:21-33`) mirrors them as `"MessageEnvelope": ""` etc., with a stray blank line
at `:32`. This is not documentation of the messages — it presents them as empty string fields of
`ShuttleStatus`. There is **no** `## MessageEnvelope`/`## MoveCommandMessage`/`## CommandAckMessage`/
`## ShuttleStatusMessage` section, the six envelope fields are absent, and the wire names
`moveCommand`/`commandAck`/`shuttleStatus` appear **nowhere** in the file. The contract is the source
of truth per AGENTS.md, so a version page that describes the messages wrongly is worse than a thin
one. The Phase 2 greps pass only because the type *names* occur (row 14). Fix: give v2.md real
sections — the envelope table, one table per message with JSON type / C# type / meaning / required,
the wire names, and a sample for each; keep the copied `ShuttleStatus` section and remove the four
bogus rows from it.

### Should-fix

**S1 — `MQTTnetTest.cs:94` synchronizes with `await Task.Delay(250)` (the race the professor
flagged).** `Subscribe_receives_only_the_matching_topic`
(`src/MqttMessaging.Tests/MQTTnetTest.cs:56-102`) publishes the match, publishes the miss, waits a
fixed 250 ms, then asserts `matches == 1`. Its companion test uses a `TaskCompletionSource` +
`WaitAsync(TimeSpan.FromSeconds(5))` (`:33,46`) — the deterministic pattern. I ran the wildcard test
**10/10 green**, so it is not currently flaky on this machine, but a fixed delay is exactly the
class of synchronization lesson 04 banned on the design side; on a loaded CI it can false-red. Fix:
await a TCS set when the match is received (bounded), or publish the miss first and then await the
match, then assert `matches == 1`, so the test has no timing assumption. This is test-quality, not
product correctness — hence should-fix, not a blocker.

**S2 — the MQTT tests are in `MQTTnetTest.cs`, not the assigned `MqttMessageBusTests.cs`
(requirement 5), and the class is `MQTTnetTest`.** No gate greps the filename, and the two required
*test method* names are present and green, so the substance is met — but the assignment names the
file, and the reviews have consistently held the owner to fixed names. Rename the file and class to
`MqttMessageBusTests`.

### Consider

- **C1 — `Rack.LaneId`/`SlotId` vs the lesson's fixed `LaneIds`/`SlotIds`** (carried from review 04,
  non-blocking, no consumer wired). Unchanged.
- **C2 — `RackTests` weaker than record 05's claims** (carried from review 04: the `1, 1, 3`
  distance triple reduced to `1, 3`; membership rejection of `A01-L01-S03`/`banana` unasserted).
  Unchanged, no regression.
- **C3 — the rejected-command reprocess branch has no test** (record 09 §2 asymmetry). The code is
  correct (`ShuttleEndpoint.cs:63-71` returns before `MarkCompleted` on rejection); only the test is
  missing, and requirement 11 does not name one. Worth a cheap test: send a rejected command twice
  and assert it is re-checked/rejected twice.
- **C4 — `src/ShuttleCore/Shuttle.cs` still ends with no trailing newline** (pre-existing from M1;
  the added `TravelTimeTo` lines did not introduce it — the file's last byte is `}`). Cosmetic,
  unrelated to this lesson's gates.
- **C5 — cosmetic/allowed literals worth naming so they get studied:** `MessageFamily.cs:3-7` uses a
  trailing-comma-per-line layout and `TopicScheme` trims `/` from the root (`TopicScheme.cs:11`),
  which is sensible but undocumented in v2.md. `EnvelopeTests.cs` uses tabs while other files use
  spaces. Non-blocking.

---

## Explain-it questions — NON-BLOCKING

**The owner waived explain-it re-answers as a gate in review 01; reviews 02–04 kept them
non-blocking. Nothing below blocks or lifts the `not yet` verdict — the verdict stands on B1 and B2.
Answer these out loud before re-submitting.**

1. The ack's `CorrelationId` is exactly the command's `MessageId`. Why is the envelope the *only*
   place that field can live, and what can an MQTT topic not carry?
2. QoS 1 can deliver a duplicate (you can see it). Walk the causal chain: why does choosing QoS 1
   *oblige* building the guard in the same lesson, and why does QoS 2 not remove the obligation?
3. `MarkCompleted` is called only on acceptance (`ShuttleEndpoint.cs:73`). Trace what happens when a
   rejected command is delivered twice, and what real event (an obstruction clearing) that asymmetry
   protects. What would break if a rejection were recorded?
4. `TravelTimeTo` is computed **after** `StartMove`, at `ShuttleEndpoint.cs:77`. Why is that
   distance still the origin's, not the destination's? Which M1 rule ("time is an input") does the
   two-status, no-sleep design carry onto the wire?
5. R2/R4 is a test-file defect, but `FleetManager:RoundTripTimeoutMs` is the same idea in a running
   program. What do the `DemoTests.cs` bound and the manager's `CancellationTokenSource` timer have
   in common, and why does an unbounded wait matter more in a networked process than in a test?
6. `Messaging` references only `Contracts`; `MqttMessaging` is the only project that sees `MQTTnet`.
   Why does the compiler, not a convention, enforce "decision logic away from I/O" here, and where
   would `ShuttleEndpoint` have to live for that boundary to break?

### Blocks to study (unclear, fragile, or copied-looking)

- `src/FleetManager.Tests/StartTests.cs:10,20-21` — the R2/R4 defect, four reviews old. Be able to
  say why each of the three Phase 2b gates fails and quote `DemoTests.cs:9,20-22` as the fix.
- `docs/contract/v2.md:16-19,21-33` — the bogus rows and the empty sample. Be able to write the real
  `MessageEnvelope` section and one table per message, and say which wire name belongs to which.
- `src/MqttMessaging.Tests/MQTTnetTest.cs:94` — the 250 ms `Task.Delay`. Say why its companion test
  at `:33,46` is the right pattern and rewrite this one the same way.
- `src/ShuttleSim/ShuttleEndpoint.cs:24-80` — the parse → validate → guard → act → ack → status
  order. Be able to say why `IsDuplicate` is checked before `StartMove` and why the version/kind/
  recipient checks come before both.
- `src/MqttMessaging/MqttMessageBus.cs:108-126` — `Matches`. Note it re-implements wildcard
  matching *in addition to* the broker's; be able to say why the adapter filters locally (its
  `SubscribeAsync` handler dictionary) and why the wildcard test passes regardless.

---

## Revisit list

- **A fixed `sleep`/`Task.Delay` is the wrong synchronization in a test too.** Lesson 04 banned it
  in the domain; this milestone's own MQTT test uses one. "Deterministic hand-off" now applies on
  both sides of the broker seam.
- **A contract page is a deliverable, not a formality.** The Phase 2 greps found the four type names
  in `v2.md` and passed — the same "a gate passed for the wrong reason" pattern as reviews 02–04,
  this time in a Markdown file. Read the grep, then read the document.
- **R2/R4 has now survived four reviews and one lesson requirement.** The pattern to copy has sat in
  `DemoTests.cs` since lesson 04. This is the last review that should have to name it.
- **`MqttMessaging.Tests` proves the adapter (2/2, mutation-proven fail under a broken publish); the
  in-process broker keeps M2 testable with no Docker.** That is the seam working as designed.
- Next: fix B1 and B2, rename the test file (S2), tighten the wildcard test (S1), re-submit. Lesson
  06 then adds retry + ack timeout (record 09's sequel) to actually close M2.

---

## End-product check

Lesson 05 made the shuttle a **peer across a broker**, which is the seam every later milestone
crosses, and the seam holds: `Contracts` carries the wire shapes, `Messaging` carries the
domain-free bus/topics/guard, `MqttMessaging` hides the client, and the two apps talk only through
the broker. The round trip is real and observed; idempotency is real and mutation-proven; M1 is
untouched (demo 7 lines, 22/22 core, `TravelTimeTo` additive); the contract is versioned with
records 07–09. What is *not* done is small and specific: two test-file lines three reviews old, and
one contract page that was left half-written. **M2 does not close in this review** — retry, ack
timeout, alarms, recovery, and heartbeats are lesson 06, and the spec's "a lost acknowledgement is
retried" is explicitly deferred. The residual risk is contained to B1/B2 and a pair of test-quality
items (S1/S2); the architecture and the hard concurrency thinking are sound.

---

## README

**Not changed.** M0 and M1 stay `done`; the **M2 row stays `not started`** — lesson 05 is the first
slice of M2, and the spec's M2 done-when needs a full round trip **including retry of a lost ack**,
which lesson 06 supplies. Per AGENTS.md, a milestone row flips only on a passing *milestone*, and
only the reviewer writes it. When M2 eventually passes, the verification note should record (from
`src/`): `dotnet build` exit 0; `dotnet test` exit 0 with six `Passed!` lines; the Phase 3 anchored
name walk silent; the Phase 5 round trip `move exit=0` with `commandAck`/`Accepted:true`/
`moving`/`idle`; the Phase 6 fresh-container run with six `Passed!`; and the retry test lesson 06
adds. Progress on lesson 05 is recorded in this review, not in the milestone table.

---

## What I changed in this repository

- **Added:** `docs/reviews/05-message-contract-and-transport.md` (this file). Uncommitted — I commit
  only when asked.
- **README.md:** not touched (reason above).
- **`src/`, `docker/`, `docs/lessons/`, `docs/decisions/`, `docs/contract/`, `spec/`:** not touched
  by me. Every mutation ran against `/tmp/opencode/rev05` and `/tmp/opencode/rev05b` (fresh
  `git archive HEAD` copies), both deleted.
- **Cleanup:** `docker compose -f docker/compose.yml up -d broker` was started for Phase 5 and, per
  requirement, `docker compose -f docker/compose.yml down -v` was run; `docker ps -a` shows only the
  pre-existing `elastic_jemison` `hello-world` container (11 days old), and `docker network ls`
  shows no `shuttle-fleet` network. `docker build`/`docker run --rm` for Phase 6 left no container;
  the `shuttle-fleet-build` image tag remains (it is an image, not a process). `dotnet build-server
  shutdown` was run; no `dotnet run`/`dotnet test`/MSBuild node remains. `/tmp/opencode/rev05` and
  `/tmp/opencode/rev05b` deleted (`/tmp/opencode` also holds a pre-existing `mqttinspect` scratch
  dated 2026-10-09 23:58, before this session — left as found). `git status --porcelain` after this
  review: the professor's two `docs/lessons/05-*` files plus this review file.

---

## What the owner must do next

1. **Fix R2/R4 in `src/FleetManager.Tests/StartTests.cs`** (requirement 8, the blocker): add a named
   timeout constant, use `p.WaitForExit(<timeout>)` with `p.Kill(entireProcessTree: true)` on
   overrun (copy `src/ShuttleSim.Tests/DemoTests.cs:9,20-22`), remove the nine-space indent on line
   10, and end the file with a newline. Phase 2 rules and Phase 2b must go green.
2. **Finish `docs/contract/v2.md`** (requirement 12, the blocker): document `MessageEnvelope` (all
   six fields), `MoveCommandMessage`, `CommandAckMessage`, and `ShuttleStatusMessage` with real
   tables and per-message JSON samples, name the wire names (`moveCommand`/`commandAck`/
   `shuttleStatus`), and remove the four bogus rows and the empty sample from the `ShuttleStatus`
   section.
3. **Rename `src/MqttMessaging.Tests/MQTTnetTest.cs` → `MqttMessageBusTests.cs`** (class
   `MqttMessageBusTests`), and **replace the `Task.Delay(250)`** in
   `Subscribe_receives_only_the_matching_topic` with the deterministic `TaskCompletionSource` pattern
   its companion test uses.
4. Answer the explain-it questions out loud (non-blocking), and note that lesson 06 must add retry,
   ack timeout, and the retry-exhausted alarm to actually close M2.
5. Re-submit; I will re-run the full block, the name walk, and both mutations.

**What remains for M2 (lesson 06, not the owner's fault):** the *sending* half of reliability — a
command that is never acknowledged, the timeout that notices
(`FleetManager:RoundTripTimeoutMs` is already the driver bound), the retry that resends it (safe
because the guard is built), and the alarm when retries run out. Alarms, recovery, and heartbeats
are later M2/M5 lessons and contract v3. The `sleep 3` in Phase 5 is the honest wart of lesson 05
and disappears once retry exists.

---

## Re-review 05b — the R2/R4 fix

**Date:** 2026-10-10
**Reviewed:** the owner's **uncommitted working-tree change** to
`src/FleetManager.Tests/StartTests.cs` only (`git diff -- src/FleetManager.Tests/StartTests.cs`,
working tree on top of `8e88760`). Nothing else in `src/` was touched by the owner. The professor's
previously-reviewed and accepted lesson-05 gate fixes in `docs/lessons/05-*.md` are present in the
working tree and were **not** re-reviewed here; this review file is the untracked file from review
05.
**Scope:** the B1 fix only — the R2/R4 call-out plus a no-regression check, exactly as review 05
demanded. B2 (`docs/contract/v2.md`) is **not** addressed by this change and remains open, so the
lesson-05 slice cannot close.

**Verdict of this slice: pass — R2/R4 are fixed.** The overall lesson-05 verdict from review 05
remains **`not yet`** because B2 is still open and untouched. README is not changed (see below).

### The diff

```
+    private const int StartTimeoutMs = 30_000;
+
     [Fact]
-    public void FleetManager_Starts_Successfully()
+    public async Task FleetManager_Starts_Successfully()
     {
-         var dll = Path.Combine(AppContext.BaseDirectory, "FleetManager.dll");
-        var psi = new ProcessStartInfo { FileName = "dotnet", RedirectStandardOutput = true };
+        var dll = Path.Combine(AppContext.BaseDirectory, "FleetManager.dll");
+        var psi = new ProcessStartInfo { FileName = "dotnet", RedirectStandardOutput = true };
         psi.ArgumentList.Add(dll);
-        using var p = Process.Start(psi)!;
-        var output = p.StandardOutput.ReadToEnd();
-        p.WaitForExit();
+        using var process = Process.Start(psi)!;
+        var stdout = process.StandardOutput.ReadToEndAsync();
+        var exited = process.WaitForExit(StartTimeoutMs);
+        if (!exited) process.Kill(entireProcessTree: true);
+        Assert.True(exited, $"the demo did not exit within {StartTimeoutMs} ms");
+        var output = await stdout;
-        Assert.Equal(0, p.ExitCode);
+        Assert.Equal(0, process.ExitCode);
```

Three things to say plainly: the named bound `StartTimeoutMs = 30_000` is the same shape as
`src/ShuttleSim.Tests/DemoTests.cs:9`; the wait is now `WaitForExit(StartTimeoutMs)` with
`Kill(entireProcessTree: true)` on overrun; and `ReadToEndAsync()` is started **before** the wait
and awaited after it, which is the correct order (the old code read synchronously first — a latent
deadlock if the child ever filled its stdout buffer). The old line-10 nine-space indent is gone and
the file now ends in a newline. **The method name did not change** — it is still
`FleetManager_Starts_Successfully`, made `async`; only its body changed. (The "rename" review 05
asked for was `MQTTnetTest.cs` → `MqttMessageBusTests.cs`, which is unrelated and still open as
S2.)

### Commands run, key output

The six lesson phase-2/2b gates, verbatim from the current lesson file, from the repo root:

| Gate (lesson 05) | Command | Result | Verdict |
|---|---|---|---|
| R2 (Phase 2 rules) | `grep -rn 'WaitForExit()' src/FleetManager.Tests --include='*.cs'` | exit 1, no output | **PASS** |
| R2 (Phase 2b) | `! grep -qE '[^ ]WaitForExit\(\)' src/FleetManager.Tests/StartTests.cs` | exit 0 (grep itself exit 1) | **PASS** |
| bounded wait present | `grep -q 'WaitForExit(' src/FleetManager.Tests/StartTests.cs` | exit 0 | **PASS** |
| named bound present | `grep -qE 'Timeout' src/FleetManager.Tests/StartTests.cs` | exit 0 (`StartTimeoutMs`) | **PASS** |
| R4 (no nine-space indent) | `grep -nE '^         [^ ]' src/FleetManager.Tests/StartTests.cs` | exit 1, no output | **PASS** |
| R4 (trailing newline) | `tail -c1 src/FleetManager.Tests/StartTests.cs \| od -An -c \| grep -q '\\n'` | exit 0 (last bytes `}\n}\n\n`) | **PASS** |

Build and tests, from `src/`:

```
$ dotnet build                                              exit 0, 0 Warning(s), 0 Error(s), 12 projects
$ dotnet test                                               exit 0
$ grep -c 'Passed!'                                         prints 6
$ grep -c 'No test is available'                            prints 0
```

The six suites, verbatim:

```
Passed! - Failed: 0, Passed:  4, … Contracts.Tests.dll      (net10.0)
Passed! - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll    (net10.0)
Passed! - Failed: 0, Passed:  5, … Messaging.Tests.dll      (net10.0)
Passed! - Failed: 0, Passed:  1, … FleetManager.Tests.dll   (net10.0)
Passed! - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll  (net10.0)
Passed! - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll     (net10.0)
```

`FleetManager.Tests` is 1/1; `dotnet test --list-tests` shows the single test is
`FleetManager.Tests.StartTests.FleetManager_Starts_Successfully`, and the 13-name anchored walk
prints no `missing test:`. No regression: `ShuttleCore.Tests` is still 22/22.

No-regression checks (Phase 4), from `src/`:

```
$ dotnet run --project ShuttleSim/ShuttleSim.csproj          exit 0; grep -c '"ShuttleId"' prints 7
$ dotnet run --project FleetManager/FleetManager.csproj      exit 0; 'Fleet Manager Coming Online' found
```

### The bounded wait is load-bearing (mutation)

A green test only proves the code passes; the bound has to *notice* a hang. In a throwaway
`git archive HEAD` copy at `/tmp/opencode/r2check` (working-tree `StartTests.cs` overlaid), I set
`StartTimeoutMs = 300` and added `Thread.Sleep(60_000)` to the no-arg manager path, then ran the
test:

```
Failed FleetManager.Tests.StartTests.FleetManager_Starts_Successfully [382 ms]
  the demo did not exit within 300 ms
    at …/StartTests.cs:line 21
Failed! - Failed: 1, Passed: 0, Skipped: 0, Total: 1
```

The test failed **in under half a second instead of hanging** — the `exited == false` branch, the
`Kill(entireProcessTree: true)`, and the assertion are all genuinely exercised. The scratch copy was
deleted.

### B2 is still open

`git diff --stat -- docs/contract/v2.md` is **empty** (unchanged since `8e88760`). It still has the
four bogus rows at `v2.md:16-19` (`| MessageEnvelope | string | string | MessageEnvelope | yes |`,
and the three siblings), the empty JSON sample at `:28-31` (`"MessageEnvelope": ""` …), **zero**
occurrences of the wire names `moveCommand`/`commandAck`/`shuttleStatus`, and only two `##` sections
(`ShuttleStatus`, `Changing this contract`) — there is no `## MessageEnvelope`,
`## MoveCommandMessage`, `## CommandAckMessage`, or `## ShuttleStatusMessage`. Requirement 12 is
unmet. `docs/contract/v1.md` remains untouched (`^Version: 1$`, exit 0).

### New findings

- **Consider (cosmetic):** `src/FleetManager.Tests/StartTests.cs:7` is a blank line carrying four
  trailing spaces (`    `). No gate catches it and it does not affect the build; worth stripping
  while the file is open. Not a blocker.
- **Consider (minor, not blocking):** on the overrun path, `Assert.True(exited, …)` throws before
  `await stdout`, so the `ReadToEndAsync` task is left unobserved. The process is already killed and
  disposed, so this is benign, but a `finally`-style drain or awaiting the task before asserting
  would be tidier. The async read/wait ordering itself is correct.
- Everything review 05 listed as S1 (`Task.Delay(250)` in the wildcard test), S2 (`MQTTnetTest.cs` /
  class `MQTTnetTest` name drift), and C1–C5 remains exactly as it was — none are touched by this
  diff and none are blockers.

### What the owner must do next

1. **Finish `docs/contract/v2.md`** (B2, the only remaining lesson-05 blocker): real sections for
   `MessageEnvelope` (all six fields), `MoveCommandMessage`, `CommandAckMessage`, and
   `ShuttleStatusMessage`; name the wire names; remove the four bogus `ShuttleStatus` rows and the
   empty sample. `v1.md` stays untouched.
2. Then the S1/S2 test-quality items (rename `MQTTnetTest.cs` → `MqttMessageBusTests.cs`, replace
   the fixed `Task.Delay(250)` with a `TaskCompletionSource` hand-off).
3. Answer the review-05 explain-it questions out loud (non-blocking), then re-submit. This time the
   full lesson-05 block, the name walk, and both idempotency mutations can close the slice if v2.md
   is done — retry/ack-timeout/alarms stay lesson 06 by design.

### README

**Not changed.** The milestone table flips only on a *passing* review. This re-review passes the
R2/R4 slice but the lesson-05 verdict remains `not yet` because B2 is open, so the M2 row stays
`not started`. No other file was touched.

### Cleanup and files changed

- **Changed by me:** this review file only (appended the section above). `src/`, `docker/`,
  `docs/lessons/`, `docs/decisions/`, `docs/contract/`, `spec/`, and `README.md` were not touched.
- **Cleanup:** `/tmp/opencode/r2check` deleted; no `dotnet run`/`dotnet test` process remains;
  `dotnet build-server shutdown` run and the orphaned MSBuild worker nodes killed; `docker ps` shows
  no containers (no broker was needed — the tests use an in-process `MQTTnet.Server`). `/tmp/opencode`
  holds only the pre-existing `mqttinspect` scratch (left as found). `git status --porcelain` after
  this review: the professor's two `docs/lessons/05-*` files, the owner's
  `src/FleetManager.Tests/StartTests.cs`, and this review file.

---

## Re-review 05c — the contract v2 page (B2)

**Date:** 2026-10-10
**Reviewed:** the owner's **uncommitted working-tree change** to `docs/contract/v2.md` only (`git diff -- docs/contract/v2.md`, working tree on top of `8e88760`). This is the B2 blocker from review 05. B1 (R2/R4) was already closed in Re-review 05b and I re-ran its six gates to confirm no regression. The professor's previously-reviewed `docs/lessons/05-*.md` gate fixes are unchanged and not re-reviewed here; this review file is the untracked file from review 05.
**Scope:** requirement 12 only — is `docs/contract/v2.md` now a complete, correct contract page per lesson 05 requirement 12 and decision record 07? Nothing else in `src/` was touched by this change.
**Verdict of this slice: pass — B2 is fixed.** With B1 fixed in 05b and B2 fixed here, **lesson 05 (the M2 first slice) now passes.** The overall **M2 milestone does not close** (see below).

### The diff

`git diff --stat -- docs/contract/v2.md` is no longer empty: `+132` on this one file. The change does exactly what review 05 demanded:

- **Added** `## MessageEnvelope` — all six fields with a JSON type, C# type, meaning, and required flag; plus a message-kind → wire-name → topic-kind table (`moveCommand`/`commandAck`/`shuttleStatus`), plus an envelope JSON sample.
- **Added** `## MoveCommandMessage`, `## CommandAckMessage`, `## ShuttleStatusMessage` — each with a real field table (`Envelope` + body fields) and its own JSON sample.
- **Removed** the four bogus rows from the `## ShuttleStatus` table (`| MessageEnvelope | string | string | MessageEnvelope | yes |` and its three siblings) and the empty-sample lines (`"MessageEnvelope": ""`, …), and the stray blank line.
- **Kept** the `## ShuttleStatus` section copied forward, byte-identical to `v1.md`'s table and sample.

### Commands run, key output

Run from the repo root. Exit codes read directly.

Lesson 05 Phase 2 contract gates, verbatim:

| Gate | Result |
|---|---|
| `grep -qE '^Version: 2$' docs/contract/v2.md` | exit 0 |
| `grep -qE '^Version: 1$' docs/contract/v1.md` | exit 0 |
| four type-name greps `MessageEnvelope`/`MoveCommandMessage`/`CommandAckMessage`/`ShuttleStatusMessage` in `src/Contracts` | no `missing type:` output |
| `grep -rq 'const int Current = 2' src/Contracts --include='*.cs'` | exit 0 |
| three wire-name greps `moveCommand`/`commandAck`/`shuttleStatus` in `src/Contracts/MessageTypes.cs` | no `missing wire name:` output |
| `grep -qE '^Version: 2' docs/contract/v2.md`; `grep -q 'MessageEnvelope'`; `grep -q 'CommandAckMessage'` | exit 0 / 0 / 0 |

`git diff -- docs/contract/v1.md` is **empty** — `v1.md` is byte-unchanged. The four greps above also confirm the fixed names in code.

**Requirement-12 read-through (not just the greps).** The document now has real `##` sections for `MessageEnvelope`, `MoveCommandMessage`, `CommandAckMessage`, `ShuttleStatusMessage`, and the copied `## ShuttleStatus`. The six envelope fields each carry a meaning; each message has a field table and a JSON sample; the wire names `moveCommand`/`commandAck`/`shuttleStatus` are named; `ShuttleStatusMessage`'s body field is `Status`; the four bogus rows and the empty sample are gone.

**JSON validity.** A `python3` `json.loads` over every fenced ```json`` block: `v2.md` has **5 blocks, all valid** (keys: envelope; Envelope+ShuttleId+ToLane; Envelope+Accepted+Reason; Envelope+Status; ShuttleId+State+Position+Timestamp+FaultReason). `v1.md`'s 1 block is valid. No trailing commas; no placeholder tokens (`TODO`/`TBD`/`<`/`>`/`...`) in the file.

**Cross-check against the code and the emitted wire format.** I read `MessageTypes.cs`, `MessageEnvelope.cs`, `MoveCommandMessage.cs`, `CommandAckMessage.cs`, `ShuttleStatusMessage.cs`, `ShuttleStatus.cs`, `Messaging/MessageEnvelopes.cs`, and `ShuttleSim/ShuttleEndpoint.cs`, and I serialized the three messages with a throwaway console project in `/tmp/opencode/v2check` that references `Contracts` (deleted after). The emitted frames, verbatim:

```
{"Envelope":{"ContractVersion":2,"MessageType":"moveCommand","MessageId":"aca6…","CorrelationId":null,"SenderId":"fleet-manager","SentAt":"2026-10-08T12:00:00Z"},"ShuttleId":"SH-01","ToLane":"A02-L04"}
{"Envelope":{"ContractVersion":2,"MessageType":"commandAck","MessageId":"76ec…","CorrelationId":"aca6…","SenderId":"SH-01","SentAt":"2026-10-08T12:00:00Z"},"Accepted":true,"Reason":null}
{"Envelope":{"ContractVersion":2,"MessageType":"shuttleStatus","MessageId":"3390…","CorrelationId":null,"SenderId":"SH-01","SentAt":"2026-10-08T12:00:00Z"},"Status":{"ShuttleId":"SH-01","State":"idle","Position":"A02-L04","Timestamp":"2026-10-08T12:00:00Z","FaultReason":null}}
```

Field/type/sample cross-check table:

| v2.md item | Documented | Code / wire source | Match |
|---|---|---|---|
| `MessageEnvelope.ContractVersion` | `int` / `int` | `int ContractVersion` (`MessageEnvelope.cs:4`) | yes |
| `MessageEnvelope.MessageType` | `string` / `string`, wire name | `string MessageType`; constants `moveCommand`/`commandAck`/`shuttleStatus` (`MessageTypes.cs:5-7`) | yes |
| `MessageEnvelope.MessageId` | `string` / `string`, fresh GUID | `string MessageId`; `Guid.NewGuid().ToString()` (`MessageEnvelopes.cs:9`) | yes |
| `MessageEnvelope.CorrelationId` | `string` / `string?`, null on new | `string? CorrelationId` (`MessageEnvelope.cs:7`) | yes |
| `MessageEnvelope.SenderId` | `string` / `string` | `string SenderId` | yes |
| `MessageEnvelope.SentAt` | `string, ISO 8601 UTC` / `DateTime` | `DateTime SentAt` | yes |
| `MoveCommandMessage.{Envelope,ShuttleId,ToLane}` | `MessageEnvelope,string,string` | `MoveCommandMessage.cs:2` | yes |
| `CommandAckMessage.{Envelope,Accepted,Reason}` | `MessageEnvelope,bool,string?` | `CommandAckMessage.cs:2` | yes |
| `ShuttleStatusMessage.Envelope` | `MessageEnvelope` | `ShuttleStatusMessage.cs:3` | yes |
| `ShuttleStatusMessage.Status` (body field name) | `Status` (not `ShuttleStatus`) | `ShuttleStatus Status`; serialized key `"Status"` | yes |
| Ack sample `SenderId` | `SH-01` (the shuttle) | `_shuttleId` (`ShuttleEndpoint.cs:86`) | yes |
| Ack sample `CorrelationId` | the command's `MessageId` | `command.Envelope.MessageId` (`ShuttleEndpoint.cs:86`) | yes |
| Ack sample own `MessageId` | distinct (`99fa5909…`) | fresh GUID per message | yes |
| Status sample `CorrelationId` | `null` | `MessageEnvelopes.Create(..., sentAt)` with no correlation (`ShuttleEndpoint.cs:96`) | yes |
| Status sample body key | `Status` | serialized `"Status"` | yes |
| Sample envelope ids distinct per message | `96dfbc67…` (cmd) / `99fa5909…` (ack) / `02d30a0d…` (status) | one fresh GUID per message | yes |
| Wire names in v2.md | `moveCommand`, `commandAck`, `shuttleStatus` | `MessageTypes.cs` | yes |
| `ShuttleStatus` section copied forward | identical to `v1.md` | `ShuttleStatus.cs`; `v1.md` unchanged | yes |
| Bogus rows (`… | string | string | …`) | absent | `grep` exit 1 | yes |

**Mismatches between `v2.md` and the code/wire: none.** Every field name, C# type, wire name, and JSON sample matches what the code serializes, including the three specifics the task called out: the status body is `Status`; the status envelope's `CorrelationId` is `null`; the ack's `SenderId` is the shuttle and its `CorrelationId` is the command's `MessageId` (not its own id).

### Build and tests (no regression)

From `src/`:

```
$ dotnet build                                          exit 0, 0 Warning(s), 0 Error(s), 12 projects
$ dotnet test                                           exit 0
$ grep -c 'Passed!'                                     prints 6
$ grep -c 'No test is available'                        prints 0
```

```
Passed!  - Failed: 0, Passed: 4,  … Contracts.Tests.dll
Passed!  - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll
Passed!  - Failed: 0, Passed: 5,  … Messaging.Tests.dll
Passed!  - Failed: 0, Passed: 1,  … FleetManager.Tests.dll
Passed!  - Failed: 0, Passed: 2,  … MqttMessaging.Tests.dll
Passed!  - Failed: 0, Passed: 5,  … ShuttleSim.Tests.dll
```

Six `Passed!` lines, 39 tests, 0 "No test is available"; `ShuttleCore.Tests` still 22/22.

The six R2/R4 gates re-run in this working tree (B1's fix, unchanged since 05b): the two bare-`WaitForExit()` gates are clean, `WaitForExit(` and `Timeout` are present, the nine-space-indent gate is clean, and the trailing-newline gate is exit 0 — **all six green.**

### Verdict

**Pass.** Requirement 12 is met: `docs/contract/v2.md` is a complete and correct contract page, cross-checked line-by-line against the code and the emitted wire format. B2 was the last open blocker from review 05. With B1 fixed in 05b and B2 fixed here, **lesson 05's first slice closes and can be marked passed.** README is **not** changed (see below), because a lesson passing is not a milestone passing.

### Residual items (not blockers)

- **S1 (`Task.Delay(250)` in the wildcard test) and S2 (`MQTTnetTest.cs` / class `MQTTnetTest` name drift)** remain exactly as review 05 and 05b left them — should-fix, not blockers; the lesson-05 verdict never stood on them. Worth doing while the milestone is fresh.
- **C1–C5** (Rack naming, weaker `RackTests`, the untested rejected-command reprocess branch, the missing trailing newline in `Shuttle.cs`, cosmetic literal/layout nits) remain as before; none are touched by this diff.
- **Cosmetic in the new `v2.md`:** two blank lines precede some JSON fences (e.g. after the field tables), and the message-kind → wire-name table lives under the `## MessageEnvelope` heading rather than a heading of its own. Neither affects readability or correctness.
- **Lesson-quality note for the professor (not the owner's fault).** Lesson 05's Phase 2 greps for `v2.md` are name-only (`grep -q 'MessageEnvelope' …`); they passed in review 05 for a half-written page. The substance of requirement 12 ("documents … with real tables") is in the prose, not in any gate. Consider strengthening the Phase 2 gate to check for `## MessageEnvelope` and the three wire names, so a future half-written page fails the grep instead of only the reviewer's eye. The lesson stayed within its 3-new-concept cap, and all three concepts (envelope, broker boundary, idempotency) were taught.

### README

**Not changed.** The milestone table flips only on a *passing milestone*, and M2's done-when is not met: the spec requires "a lost acknowledgement is retried", and retry, ack timeout, and the retry-exhausted alarm are lesson 06 (as review 05 and the lesson itself state). The M2 row stays `not started`. Progress on lesson 05 is recorded in this review, not in the table.

### What the owner must do next

1. **Nothing to fix to close lesson 05.** B1 and B2 are both resolved; the slice passes.
2. Optionally clear **S1/S2** (rename `MQTTnetTest.cs` → `MqttMessageBusTests.cs`, class included; replace the fixed `Task.Delay(250)` with the `TaskCompletionSource` hand-off its companion test uses) before lesson 06, since both sit directly on the broker seam lesson 06 extends.
3. Answer the review-05 explain-it questions out loud (non-blocking), then move on to **lesson 06: retry, ack timeout, and the retry-exhausted alarm** — that is what actually closes M2.

### Cleanup and files changed

- **Changed by me:** this review file only (appended the section above). `src/`, `docker/`, `docs/lessons/`, `docs/decisions/`, `docs/contract/`, `spec/`, and `README.md` were not touched.
- **Cleanup:** `/tmp/opencode/v2check` (the serialization cross-check) deleted; no broker/container was started (tests use an in-process `MQTTnet.Server`); `dotnet build-server shutdown` run and the seven orphaned MSBuild worker nodes (reparented to `/init`) killed. `pgrep -a dotnet` now shows only the pre-existing VS Code `Microsoft.VisualStudio.ProjectSystem.Server.BuildHost` (PID 429473, not mine); `docker ps -a` shows only the pre-existing `elastic_jemison` `hello-world` container (11 days old). `git status --porcelain` after this review: the professor's two `docs/lessons/05-*` files, the owner's `src/FleetManager.Tests/StartTests.cs` and `docs/contract/v2.md`, and this review file.
