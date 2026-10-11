# Review 06: The acknowledgement timeout and the retry that closes M2 (lesson 06, M2 sender slice)

**Date:** 2026-10-10
**Reviewed:** commits `1667e99` (`M2: Add lesson on acknowledgement timeout and retry mechanism for M2`
— docs-only: lesson 06, its hints, and decision record 10) and `641165e` (`M2: feat: implement
reliable command sender with retry policy and add configuration options` — the owner's
implementation; 7 files, +338/−21). On top of `e989c85` (lesson-05 slice passed in reviews 05b/05c,
plus the S1/S2 fixes `6edec76` and `e989c85`). Branch `master`.
**Working tree at review time:** clean (`git status --porcelain` empty); HEAD `641165e`. This is the
professor's lesson/decision commit followed by the owner's implementation commit — nothing else.
**Scope:** lesson 06 against its own assignment **and** the whole M2 done-when in the spec.
**Contract checked:** `docs/contract/v1.md` untouched; `docs/contract/v2.md` untouched and still
`Version: 2`; no `v3.md`. The retry is a send-side change only — no wire change.

**Verdict: pass — M2 closes.**

The sending half of reliability is real, and it is the honest complement of the receiving half that
review 05 proved. The sender builds the command and its envelope **once**, resends the **same bytes**
(same `MessageId` *and* same `SentAt`) until an ack arrives, stops immediately on a **rejected** ack,
backs off a configured amount between tries, and gives up after `MaxAttempts` with a definite local
failure and a non-zero exit. Every deterministic proof is there: the fake-bus test that drops the
first ack and asserts the same id is sent twice, the test that proves a new id per attempt turns that
proof red, the rejected-and-accepted stop tests, the give-up bound, and the pure policy's three
guards. Over the real broker the round trip, the lost-command retry, the give-up path, and the
rejected path all behave as the record says. The contract stays v2; M1 is untouched; the fresh
container is green; and both lesson-05 idempotency mutations still go red when broken.

Two things need fixing but neither gates the milestone done-when (details in
[Findings](#findings)): the `RetryPolicy` guards live in an extension method that **no production
caller invokes**, so the type does not enforce its own invariants on construction (fix: put them in
the primary-constructor body, as the lecture showed); and the Phase 5b instruction to **log each
attempt** was not implemented, so a real resend is invisible in the logs. Both are should-fix.
The gate failures in Phase 5 are a **lesson defect**, not an owner miss — explained under
[Lesson defects](#lesson-defects-for-the-professor).

---

## Commands run, verbatim key output

Exit codes read directly, never through a pipe, except where the lesson's own block pipes or uses
`mktemp`. The acceptance block was run **verbatim from the current lesson file**, from the
directories it names. Mutations ran against throwaway `git archive HEAD` copies under
`/tmp/opencode`, deleted afterwards; no experiment touched `src/`.

### Phase 1 — the new types exist and the project graph stays right

```
$ test -f src/Messaging/RetryPolicy.cs                                   exit 0 (no output)
$ test -f src/FleetManager/ReliableCommandSender.cs                      exit 0 (no output)
$ grep -q 'sealed record RetryPolicy' src/Messaging/RetryPolicy.cs       exit 0
$ grep -q 'enum CommandSendStatus' src/FleetManager/ReliableCommandSender.cs  exit 0
$ grep -q 'record CommandSendResult' src/FleetManager/ReliableCommandSender.cs exit 0
$ grep -q 'ProjectReference Include="..\Messaging\Messaging.csproj"' \
    src/FleetManager.Tests/FleetManager.Tests.csproj                     exit 0
$ grep -c '<Project Path' src/ShuttleFleet.slnx                          prints 12 (no new project)
$ grep -q 'MQTTnet' src/Messaging/Messaging.csproj                       exit 1 (Messaging stays MQTT-free)
```

All green. `FleetManager.Tests.csproj:22-24` keeps the `FleetManager` and `Contracts` references and
adds `..\Messaging\Messaging.csproj`, exactly the edge the assignment asks for.

### Phase 2 — config-not-code, and only the manager's backoff waits

```
$ for k in AckTimeoutMs MaxAttempts BackoffMs RoundTripTimeoutMs; do
    grep -q "$k" src/FleetManager/appsettings.json || echo "missing config key: $k"; done  (no output)
$ grep -rnE '"shuttle/[A-Za-z0-9+_/-]+"' src --include='*.cs' …          exit 1 (no topic literals)
$ grep -rniE '127\.0\.0\.1|localhost|:1883|Host=' src --include='*.cs' … exit 1 (addresses are config)
$ grep -rnE 'Thread\.Sleep|Task\.Delay' src/ShuttleCore src/ShuttleSim \
    src/Contracts src/Messaging --include='*.cs' …                       exit 1 (shuttle/pure side never waits)
$ grep -rn 'Task.Delay' src/FleetManager --include='*.cs' … | grep -c 'ReliableCommandSender.cs'  prints 1
$ grep -q 'policy.Backoff' src/FleetManager/ReliableCommandSender.cs     exit 0
$ grep -rn 'WaitForExit()' src/FleetManager.Tests --include='*.cs'       exit 1 (R2 stays fixed)
$ for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do
    grep -q 'Assert' "$f" || echo "$f"; done                             (no output)
```

All green. The sleep gate's two halves hold: the shuttle and the pure projects never `Task.Delay`,
and the manager has exactly **one** `Task.Delay`, in `ReliableCommandSender.cs`, and it uses
`policy.Backoff`. `appsettings.json:12-17` adds `AckTimeoutMs: 2000`, `MaxAttempts: 3`,
`BackoffMs: 500` beside the existing `RoundTripTimeoutMs: 10000`.

### Phase 3 — from `src/`: build, run the suite, walk the new names

```
$ dotnet build                                                           exit 0, 0 Warning(s), 0 Error(s)
$ T=$(mktemp); dotnet test > "$T" 2>&1                                   exit 0
$ grep -c 'Passed!' "$T"                                                 prints 6
$ grep -c 'No test is available' "$T"                                    prints 0
$ L=$(mktemp); dotnet test --list-tests > "$L" 2>&1
$ for t in Lost_acknowledgement_is_retried_with_the_same_message_id \
           Retries_stop_after_the_configured_attempts \
           Rejected_acknowledgement_is_not_retried \
           Accepted_acknowledgement_stops_retrying \
           RetryPolicy_rejects_fewer_than_one_attempt \
           RetryPolicy_rejects_a_non_positive_ack_timeout \
           RetryPolicy_rejects_a_negative_backoff; do
    grep -qE "\.$t\$" "$L" || echo "missing test: $t"; done              (no output)
```

The six suites, verbatim:

```
Passed!  - Failed: 0, Passed: 22, … ShuttleCore.Tests.dll   (net10.0)
Passed!  - Failed: 0, Passed:  4, … Contracts.Tests.dll     (net10.0)
Passed!  - Failed: 0, Passed:  8, … Messaging.Tests.dll     (net10.0)
Passed!  - Failed: 0, Passed:  5, … FleetManager.Tests.dll  (net10.0)
Passed!  - Failed: 0, Passed:  2, … MqttMessaging.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed:  5, … ShuttleSim.Tests.dll    (net10.0)
```

**46 tests, six `Passed!` lines, 0 "No test is available", the anchored name walk silent.**
`Messaging.Tests` grew 5 → 8 (the three `RetryPolicy` tests); `FleetManager.Tests` grew 1 → 5 (the
four sender tests). No new project. `ShuttleCore.Tests` is still **22/22** — the M1 reference count.

### Phase 4 — no regression: the M1 demo and the manager's start line still hold

```
$ F=$(mktemp); dotnet run --project ShuttleSim/ShuttleSim.csproj > "$F" 2>&1   exit 0
$ grep -c '"ShuttleId"' "$F"                                             prints 7
$ G=$(mktemp); dotnet run --project FleetManager/FleetManager.csproj > "$G" 2>&1 exit 0
$ grep -q 'Fleet Manager Coming Online' "$G"                             exit 0
```

M1 demo unchanged (7 status lines); the M0 no-argument manager path intact.

### Phase 5a — the round trip over the real broker, with NO pre-sleep

```
$ docker compose -f docker/compose.yml up -d broker                      exit 0
$ sleep 2
$ cd src && dotnet build ShuttleFleet.slnx >/dev/null 2>&1 && cd ..      exit 0
$ dotnet src/ShuttleSim/bin/Debug/net10.0/ShuttleSim.dll serve > "$S" 2>&1 &
$ dotnet src/FleetManager/bin/Debug/net10.0/FleetManager.dll move SH-01 A01-L02 > "$M" 2>&1
$ echo "move exit=$MOVE_EXIT"                                            prints move exit=0
$ grep -q '"MessageType":"commandAck"' "$M"                             exit 0
$ grep -q '"Accepted":true' "$M"                                         exit 0
$ grep -q '"State":"moving"' "$M"                                        exit 0
$ grep -q '"State":"idle"' "$M"                                          exit 0
$ grep -c '"State":"moving"' "$S"                                        prints 0   <-- lesson defect, see below
$ grep -c '"State":"idle"' "$S"                                          prints 0   <-- lesson defect, see below
```

The manager's `$M`, verbatim (three JSON lines) — a correlated ack, then `moving` at `A01-L01`,
then `idle` at `A01-L02` five seconds later:

```
{"Envelope":{"ContractVersion":2,"MessageType":"commandAck","MessageId":"e57d…","CorrelationId":"52c1…","SenderId":"SH-01","SentAt":"2026-10-11T01:06:40.8662264Z"},"Accepted":true,"Reason":null}
{"Envelope":{"ContractVersion":2,"MessageType":"shuttleStatus","MessageId":"b4a1…","CorrelationId":null,"SenderId":"SH-01","SentAt":"2026-10-11T01:06:40.8662264Z"},"Status":{"ShuttleId":"SH-01","State":"moving","Position":"A01-L01","Timestamp":"2026-10-11T01:06:40.8662264Z","FaultReason":null}}
{"Envelope":{"ContractVersion":2,"MessageType":"shuttleStatus","MessageId":"9d1d…","CorrelationId":null,"SenderId":"SH-01","SentAt":"2026-10-11T01:06:45.8662264Z"},"Status":{"ShuttleId":"SH-01","State":"idle","Position":"A01-L02","Timestamp":"2026-10-11T01:06:45.8662264Z","FaultReason":null}}
```

The ack's `CorrelationId` is the command's `MessageId`; the move reports exactly one `moving` and
one `idle` pair. The two `$S` greps print **0**, not 1, because `ShuttleSim`'s `serve` mode never
echoes the status payloads it publishes (`src/ShuttleSim/Program.cs:96,63` — only the demo path at
line 63 prints a status, and `RunServerAsync` prints only `listening on …`). This is a defect in the
gate's assumption, not in the owner's work; the idempotency it was reaching for is visible in `$M`
(one pair, confirmed above) and mutation-proved in Phase 3 and below.

### Phase 5b — a lost command is retried over the real broker

```
$ export FleetManager__AckTimeoutMs=1000 FleetManager__MaxAttempts=6 FleetManager__BackoffMs=500
$ dotnet …/FleetManager.dll move SH-01 A01-L02 > "$M2F" 2>&1 &   # manager first
$ sleep 2
$ dotnet …/ShuttleSim.dll serve > "$S2" 2>&1 &                   # shuttle second
$ wait "$MANAGER_PID"; echo "move exit=$MOVE2_EXIT"             prints move exit=0
$ grep -q '"Accepted":true' "$M2F"                              exit 0
$ grep -c '"State":"moving"' "$S2"                              prints 0   <-- same lesson defect
```

The manager's first deliveries landed with no subscriber (the shuttle was not up yet), yet an ack
and the `moving`/`idle` statuses arrived once the shuttle came up — so a resend landed. Measured
manager lifetime after the shuttle started: **1.37 s**; the manager received exactly one `moving`
and one `idle`. The resend itself is not visible in the log because the sender logs no attempts
(should-fix S2).

Two extra real-broker checks (not in the lesson block, run to confirm the record's §7/§8 endings):

```
# Give up: no shuttle at all, AckTimeoutMs=500, MaxAttempts=2, BackoffMs=200
$ dotnet …/FleetManager.dll move SH-01 A01-L02
giveup exit=1  elapsed=1.47s
No acknowledgement for shuttle SH-01, command 50a15c88-…, after 2 attempts.

# Rejected: shuttle up, unknown lane
$ dotnet …/FleetManager.dll move SH-01 A99-L99
rejected exit=1  elapsed=0.35s
{"Envelope":{…"MessageType":"commandAck"…"CorrelationId":"a822…"…},"Accepted":false,"Reason":"unknown lane A99-L99"}
unknown lane A99-L99
```

The give-up is a definite local failure with a non-zero exit and the required (shuttle id, command
id, attempts) line; the rejection returns in one attempt (0.35 s) with the reason on stderr and no
retry. Both match record 10 §7–§8.

### Phase 6 — the contract is untouched and the fresh container still passes

```
$ grep -qE '^Version: 2$' docs/contract/v2.md                            exit 0
$ grep -qE '^Version: 1$' docs/contract/v1.md                            exit 0
$ test ! -f docs/contract/v3.md                                          exit 0 (no v3.md)
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build .       exit 0
$ docker run --rm shuttle-fleet-build                                    exit 0
$ grep -c 'Passed!' (container output)                                   prints 6
```

The fresh container reports the same six `Passed!` lines (22 / 4 / 8 / 5 / 2 / 5) as the host.

---

## Acceptance table

| # | Phase / criterion (lesson wording) | Evidence | Result |
|---|---|---|---|
| 1 | Phase 1: `RetryPolicy.cs` and sender exist | both `test -f` exit 0 | met |
| 2 | Phase 1: `sealed record RetryPolicy` | exit 0 | met |
| 3 | Phase 1: `enum CommandSendStatus` | exit 0 | met |
| 4 | Phase 1: `record CommandSendResult` | exit 0 | met |
| 5 | Phase 1: `FleetManager.Tests` → `Messaging.csproj` | exit 0 | met |
| 6 | Phase 1: `grep -c '<Project Path'` prints 12 | printed 12 | met |
| 7 | Phase 1: `Messaging` does not reference `MQTTnet` | exit 1 (clean) | met |
| 8 | Phase 2: all four config keys present | no output | met |
| 9 | Phase 2: no full-topic literal in app code | exit 1 | met |
| 10 | Phase 2: no address literal in app code | exit 1 | met |
| 11 | Phase 2: shuttle/pure side never sleeps | exit 1 | met |
| 12 | Phase 2: exactly one manager `Task.Delay`, in the sender | prints 1 | met |
| 13 | Phase 2: `policy.Backoff` present | exit 0 | met |
| 14 | Phase 2: no bare `WaitForExit()` (R2) | exit 1 | met |
| 15 | Phase 2: every `[Fact]` file has an `Assert` | no output | met |
| 16 | Phase 3: `dotnet build` exit 0, 0 warnings | exit 0, 0/0 | met |
| 17 | Phase 3: `dotnet test` exit 0 | exit 0 | met |
| 18 | Phase 3: `grep -c 'Passed!'` prints 6 | printed 6 | met |
| 19 | Phase 3: `No test is available` prints 0 | printed 0 | met |
| 20 | Phase 3: anchored walk — all 7 new names | no output | met |
| 21 | Phase 4: demo exit 0, 7 `"ShuttleId"` lines | exit 0, 7 | met |
| 22 | Phase 4: manager start line exit 0 | exit 0 | met |
| 23 | Phase 5a: move exit 0; ack + accepted in `$M` | move exit=0, both exit 0 | met |
| 24 | Phase 5a: `moving` + `idle` in `$M` | both exit 0 | met |
| 25 | Phase 5a: `$S` `moving` count prints 1 | **printed 0** | **lesson defect** |
| 26 | Phase 5a: `$S` `idle` count prints 1 | **printed 0** | **lesson defect** |
| 27 | Phase 5b: move exit 0; `Accepted:true` | move exit=0, exit 0 | met |
| 28 | Phase 5b: `$S2` `moving` count prints 1 | **printed 0** | **lesson defect** |
| 29 | Phase 6: `v2.md` `^Version: 2$`, `v1.md` `^Version: 1$` | exit 0 / 0 | met |
| 30 | Phase 6: no `v3.md` | exit 0 | met |
| 31 | Phase 6: `docker build` exit 0 | exit 0 | met |
| 32 | Phase 6: `docker run` exit 0, 6 `Passed!` | exit 0, 6 | met |
| 33 | Req 2: rejected ack stops, no retry | `Rejected_…` test; broker rejected run 0.35 s, exit 1 | met |
| 34 | Req 2: bounded tries → `NoAcknowledgement` | `Retries_stop_…`; broker give-up exit 1, 2 attempts | met |
| 35 | Req 4: give-up line names shuttle id, command id, attempts | broker give-up line verbatim | met |
| 36 | M2 done-when: full round trip | Phase 5a (broker, observed) | met |
| 37 | M2 done-when: repeated command does not run twice | lesson-05 test + mutations M1/M2 red | met |
| 38 | M2 done-when: a lost acknowledgement is retried | `Lost_acknowledgement_…` + Phase 5b + mutation | met |
| 39 | Req 1: `RetryPolicy` validates on the type | validation exists but is an uncalled extension; construction does not throw | **should-fix** |
| 40 | Phase 5b prose: sender logs each attempt | no attempt logging anywhere | **should-fix** |

**38 of 40 met; the two misses are should-fix, not milestone blockers, and none of the M2 done-when
rows depends on them.** Rows 25, 26, and 28 are pinned to a lesson defect (see below), not to the
owner's code.

---

## Substance checks

### The retry is genuinely idempotent — mutation-proved at both ends

**The sender resends the identical bytes.** `ReliableCommandSender.SendAsync` serializes the command
**once**, before the loop (`ReliableCommandSender.cs:64`), and publishes that same `payload` string
every attempt (`:67`). The waiters are keyed on `command.Envelope.MessageId` once (`:58-60`) and
removed in `finally` (`:85`). I verified byte-identity, not just same-id, with a throwaway assertion
(a fresh `git archive HEAD` copy, `/tmp/opencode/rev06b`, deleted): the two published command
payloads were **string-equal** and their `Envelope.SentAt` values were **equal** — so the resend is
the same `MessageId` *and* the same `SentAt`, exactly as record 10 §4 requires.

**New-id mutation goes red.** In a throwaway copy I changed `SendAsync` to rebuild the envelope
inside the loop (`MessageEnvelopes.Create(...)` per attempt, a fresh `MessageId`):

```
Failed FleetManager.Tests.ReliableCommandSenderTests.Lost_acknowledgement_is_retried_with_the_same_message_id
  Assert.All() Failure: 2 out of 2 items in the collection did not pass.
Failed!  - Failed: 1, Passed: 0, Skipped: 0, Total: 1 - FleetManager.Tests.dll
```

The test's `Assert.All(… MessageId == command.Envelope.MessageId)` catches the classic bug.

**A new id really is a second job.** In the same throwaway copy I added one synthetic receiver test
(`ShuttleSim.Tests`): two commands with the **same** id → the shuttle publishes **2** status messages
(one `moving`/`idle` pair, work once); two commands with **different** ids → **4** status messages
(two pairs, work twice). Both assertions passed on the honest tree. That is the sender-side reason
the same id is load-bearing, on top of the receiver mutation review 05 already proved.

**Lesson-05 idempotency mutations still red.** Re-run in a throwaway copy: `IsDuplicate` → always
`false` turns `Duplicate_move_command_is_acknowledged_but_not_run_twice` red (1/1);
`MarkCompleted` → no-op turns both `IdempotencyGuard_*` tests red (2/2) and the duplicate test red
(1/1). No regression in the receiver half.

### The decision logic is pure and testable

`RetryPolicy` is a pure record in `Messaging` with no broker reference (`Messaging.csproj`
references only `Contracts`; the `MQTTnet` check is exit 1). The loop needs a bus, so it lives in
`FleetManager` and is driven in `FleetManager.Tests` by an in-memory `FakeBus` that records every
`(Topic, Payload, Family)` and can raise a crafted `CommandAckMessage` through the subscribed
handler. No broker, no Docker, `AckTimeout = 20 ms`, `Backoff = TimeSpan.Zero`. That is the same
split lesson 05 used for `ShuttleEndpoint`.

### The two clocks are both real, and the loop is bounded

Per attempt: `waiter.Task.WaitAsync(_policy.AckTimeout, cancellationToken)` (`:70`) — a 2 s clock
that throws `TimeoutException` on this try only. Over the whole job: `Program.cs:69-70` creates one
`CancellationTokenSource` and `CancelAfter(timeoutMs)`, and passes that token to `SendAsync`
(`:76`) and to the status wait (`:80`). `OperationCanceledException` is **not** caught inside
`SendAsync` (only `TimeoutException` is), so a caller cancellation propagates as the requirement
says, and `Program.cs:93-95` maps it to exit 1. A loop bug cannot hang the process.

### The defaults fit the overall cap

`appsettings.json` has `AckTimeoutMs: 2000`, `MaxAttempts: 3`, `BackoffMs: 500`,
`RoundTripTimeoutMs: 10000`. Worst case `3 × 2000 + 2 × 500 = 7000 ms < 10000 ms`. The give-up run
above (2 × 500 + 1 × 200 ≈ 1.2 s budget, measured 1.47 s with process overhead) confirms the
per-attempt wait and the backoff are both actually spent.

### The endings behave as record 10 §7–§8 say

`SendAsync` returns `Acknowledged` on `Accepted = true`, `Rejected` on `Accepted = false`
**without looping** (the `return` sits inside the `try`, before the `catch (TimeoutException)`), and
`NoAcknowledgement` with `Attempts = MaxAttempts` after the loop. `Program.cs:77-91` branches:
acknowledged waits the status under the overall token and exits 0/1; rejected prints the reason to
stderr and exits 1; no-ack prints the failure line and exits 1. Observed on the real broker for
both non-success endings.

### Lesson-05 no-regression, and the S1/S2 fixes

From `src/`: `dotnet build` exit 0 (0/0), `dotnet test` exit 0, six `Passed!` lines,
`ShuttleCore.Tests` 22/22, M1 demo 7 lines, manager start line intact. The lesson-05 R2/R4 gates
re-run green: `WaitForExit(` present, no bare `WaitForExit()`, no nine-space indent, trailing
newline present. Review 05's should-fix **S1** is fixed — `MqttMessageBusTests.Subscribe_receives_
only_the_matching_topic` now publishes the miss first, then the match, awaits a
`TaskCompletionSource` with `WaitAsync(5 s)`, then asserts `matches == 1` (no fixed `Task.Delay`);
**S2** is fixed — the file and class are `MqttMessageBusTests`. `docs/contract/v1.md` unchanged,
`v2.md` still `Version: 2` and no `v3.md`.

### `RetryPolicy` does not self-validate (the reason for S1)

`RetryPolicy.cs:1-2` declares the record with **no** constructor body; the three guards live in
`RetryPolicyExtensions.Validate()` (`:6-14`), a `public static` extension nothing calls in
production: `Program.cs:42-52` validates the raw config itself and never calls `Validate()`, and
`ReliableCommandSender` does not call it either. I confirmed with a throwaway test that
`new RetryPolicy(0, TimeSpan.Zero, TimeSpan.FromMilliseconds(-50))` constructs **without throwing**
(the new test passed). The requirement's behavior is reachable only if a caller remembers to call
`.Validate()`.

---

## Findings

### Blockers

None. Build and tests pass, and every part of the M2 done-when is verified — round trip, no-double-run
(mutation-proved), and lost-ack retry (fake-bus test, real-broker Phase 5b, and the id mutation).

### Should-fix

**S1 — the `RetryPolicy` invariants are not enforced by the type.** Moving the three guards from
`RetryPolicyExtensions.Validate()` into the record's primary-constructor body (the shape the lecture
and the Tier-3 hint both showed) makes an invalid policy unrepresentable, not merely rejectable on
request. Why it matters for a real shuttle system: the retry policy decides how hard the manager
hammers a shuttle and its broker. A future caller (M3 builds task retry on this sender) that
constructs a policy without calling `.Validate()` can silently get `AckTimeout = 0` — every attempt
times out on the spot and the loop becomes a hot retry burst. Today the only production constructor
validates at the config boundary, so there is no live bug; that is why this is a should-fix, not a
blocker. The three tests still pass if they assert on construction instead of on `.Validate()`.

**S2 — the sender does not log each attempt.** Phase 5b's prose asks for it ("for example
`attempt 2 of 6` to stderr … so the log shows the resend happened"), and record 10 §11's support
story depends on it: when a shuttle is briefly gone, the operator or support engineer needs to see
*that* the manager retried and *how many* tries it made, not just the final outcome. Right now the
only evidence of a resend is the clock. Fix: one configured log line around the publish in
`SendAsync`. (The lesson's own acceptance block does not actually grep for it — see lesson defect
L2 — so this is a should-fix, not a gate.)

### Consider

- **C1 — the byte-identity is stronger than the test asserts.** `Lost_acknowledgement_…` checks the
  same `MessageId` but not the same `SentAt` or identical payload bytes. The code is byte-identical
  (verified), but a future change that rebuilt the envelope with the same id and a new `SentAt`
  would slip past. A one-line `Assert.Equal(payloads[0], payloads[1])` closes it.
- **C2 — `ReliableCommandSender.cs:1` still carries the hints' template comment**
  (`// shape; you write the parsing and the dictionary`). It reads like a copied artifact; remove it
  or replace it with a sentence about what the class does.
- **C3 — `$M` receives the `idle` status by a race.** `Program.cs` returns as soon as the **first**
  status arrives (`statusReceived` on `moving`), and `ShuttleEndpoint` publishes `moving` and `idle`
  back-to-back, so the `idle` line in `$M` (which Phase 5a greps for) depends on the second message
  arriving before the process exits. It passed both runs here, but the gate could flake on a loaded
  machine. If the intent is "wait for `idle`", wait for the status whose `State` is `idle`.
- **C4 — `StartTests.cs:7` is a blank line carrying four trailing spaces** (carried from review 05b).
  Cosmetic; strip while the file is open.
- **C5 — carried, unchanged, non-blocking:** `Rack.LaneId`/`SlotId` vs the lesson's `LaneIds`/
  `SlotIds` (C1 from review 04); weaker `RackTests` (C2); the rejected-command reprocess branch has
  no test (C3); `src/ShuttleCore/Shuttle.cs` still ends without a trailing newline (C4).

### Lesson defects (for the professor)

**L1 — Phase 5a/5b grep `$S`/`$S2` for status specs the shuttle never prints.** `ShuttleSim`'s
`serve` mode publishes statuses through `IMessageBus` and prints only the `listening on …` line
(`ShuttleSim/Program.cs:96`); nothing logs the published frames. So
`grep -c '"State":"moving"' "$S"` prints 0, not the "prints 1" the lesson claims, and the same for
`$S2`. The gates fail for a reason outside the owner's work. The idempotency the greps were reaching
for is observable in `$M` (the manager receives exactly one `moving`/`idle` pair) and is
mutation-proved in Phase 3. Two clean fixes: (a) tighten the lesson to grep `$M` for one pair, or
(b) if shuttle-side visibility is wanted, add "make the shuttle log each message it handles/publishes
in `serve` mode" to the assignment (it is a genuinely useful support feature). I recommend (b) —
it is the same operator-visibility reason S2 exists — but it must be assigned, not assumed.

**L2 — Phase 5b's prose and its acceptance block disagree.** The prose says "the acceptance greps
the manager output for [the attempt log]", but the Phase 5b block has no such grep. Add the grep
(for example `grep -qE 'attempt [0-9]+ of [0-9]+' "$M2F"`) once the attempt log exists, so a missing
log fails a gate instead of only the reviewer's eye.

Lesson concepts check: all three required concepts (ack timeout, retry the same message, bounded
tries/giving up) are taught, each has a lecture section, and the lesson stays at the 3-concept cap.

---

## Explain-it questions — NON-BLOCKING

Answer these out loud before the next lesson. They do not block the pass; M2 closes regardless.

1. `SendAsync` serializes the command **once**, outside the loop, and publishes that same string
   every attempt. Why is resending the same bytes — not just the same `MessageId` — what makes the
   retry safe, and what does the receiver's guard do differently if the `SentAt` changes but the id
   does not?
2. The loop catches `TimeoutException` but deliberately does **not** catch
   `OperationCanceledException`. Which timer produces which throw, and why would catching both in
   the same place turn a lost command into an unbounded wait?
3. A rejected ack (`Accepted = false`) returns immediately and is never retried, while a missing ack
   loops. Trace both through `ShuttleEndpoint` and explain why re-sending a rejection is pointless at
   M2, and which later milestone owns "retry a rejected command in case the world changed."
4. `RetryPolicy`'s guards are in an extension method that `Program.cs` never calls, yet the shipped
   manager never builds an invalid policy. Where does the current safety actually come from, and why
   is that fragile enough to be worth moving the guards into the record?

### Blocks to study (unclear, fragile, or copied-looking)

- `src/Messaging/RetryPolicy.cs:4-15` — be able to say why an extension method nobody calls is not
  the same as a type that cannot be built invalid, and move the guards into the constructor body.
- `src/FleetManager/ReliableCommandSender.cs:1` — the leftover template comment; say what the class
  does in one sentence instead.
- `src/FleetManager/Program.cs:60,80,104-118` — the first-status race (C3): why `statusReceived` is
  set by `moving`, and why the Phase 5a `idle` grep still usually passes.
- `src/FleetManager/ReliableCommandSenderTests.cs:64-91` — the fake bus's `_plannedResponses` queue;
  be able to explain how "drop the first ack" is deterministic and why `Backoff = TimeSpan.Zero`
  keeps the suite instant.
- `src/ShuttleSim/Program.cs:96` — why `$S` has no status lines, and what you would add (and where)
  if shuttle-side logging were assigned.

---

## Revisit list

- **A validating type is not the same as a validation helper.** Put invariants in the constructor;
  a method no caller invokes is documentation, not enforcement. This is the same "passes for the
  wrong reason" shape the earlier reviews kept surfacing, now on the pure-logic side.
- **Retries are invisible without a log.** The sender is now the component that decides how hard the
  fleet pushes a silent shuttle; per-attempt logging is the operator's only window into it. This is
  the support-effort theme M5 will make central.
- **A gate can test the harness instead of the product.** Phase 5a/5b grepped the shuttle's stdout
  for output the shuttle was never asked to produce. Read the gate's assumption, then read what the
  process actually prints.
- **Both halves of reliability now exist and are mutation-proven:** the sender resends the same id,
  the receiver's guard refuses to run it twice. Every later command (M3 assignment, M5 recovery)
  stands on this pair.
- Next: M3 (task queue + SQL persistence) — the retry state is in memory today and record 10 §11
  says persistence is where it goes next.

---

## End-product check

M2 was the seam every later milestone crosses: the fleet manager and the shuttle as peers over a
broker, speaking a versioned contract, with commands that are not silently lost. This lesson closes
it. `Contracts` carries the wire shapes unchanged; `Messaging` carries the pure `RetryPolicy` and
stays domain-free and `MQTTnet`-free; `FleetManager` owns the send loop and the give-up outcome,
exactly as `ShuttleSim` owns the receive loop and the guard. Nothing about the wire changed, so
`v1.md` and `v2.md` still describe every byte the system emits, and the operator-facing `AlarmMessage`
is correctly deferred to contract v3 rather than invented early. The residual risk is not in the
transport; it is the two should-fixes — an invariant that lives outside the type, and a resend that
no log records — plus the fact that retry state is in memory and dies with the process, which M3's
persistence will have to pick up. **M2 closes; the sending and receiving halves of reliability are
both real and both mutation-tested.**

---

## README

**Changed.** Per AGENTS.md, the M2 row moves from `not started` to `done`, with the verifying
commands recorded in the existing `### Verification notes` section. No other part of `README.md`
was touched. The note records what I actually built and ran: `dotnet build` exit 0 (0/0);
`dotnet test` exit 0 with six `Passed!` lines (22/4/8/5/2/5); the anchored 7-name walk silent; the
real-broker Phase 5a round trip (`move exit=0` with `commandAck`/`Accepted:true`/`moving`/`idle`)
and the Phase 5b retry (`move exit=0`, `Accepted:true`); the give-up path exit 1 with the failure
line; the rejected path exit 1; the fresh container with six `Passed!`; and the id mutation turning
the retry test red.

---

## What I changed in this repository

- **Added:** `docs/reviews/06-ack-timeout-and-retry.md` (this file).
- **`README.md`:** only the milestone table row for M2 (→ `done`) and the M2 verification note under
  it. Nothing else in the file.
- **`src/`, `docker/`, `docs/lessons/`, `docs/decisions/`, `docs/contract/`, `spec/`:** not touched
  by me. Mutations and probes ran against throwaway `git archive HEAD` copies
  (`/tmp/opencode/rev06`, `rev06b`, `rev06c`) and were deleted.

---

## What the owner must do next

1. **S1:** move the `RetryPolicy` guards into the record's primary-constructor body
   (`RetryPolicy.cs`), and drop the `.Validate()` call from the three tests so they assert on
   construction. Re-run `dotnet test` from `src/`.
2. **S2:** log each attempt in `ReliableCommandSender.SendAsync` (`attempt N of M`, with the command
   id) so a resend is visible. This is what makes Phase 5b's retry observable in a log.
3. Optional while the files are open: byte-equality assertion in the retry test (C1), delete the
   template comment (C2), strip the trailing spaces in `StartTests.cs:7` (C4).
4. Answer the four explain-it questions out loud; M2 is closed, so these are for the next lesson's
   discussion, not a re-review gate.
5. Watch for the professor's fix to lesson 06 Phase 5a/5b (`$S`/`$S2` greps) and the attempt-log
   acceptance grep; neither is your failure.

**M3 (next milestone):** task queue and lifecycle (queued/assigned/in-progress/done/failed), state
in SQL, and fleet state surviving a manager restart. This retry sender is the piece M3 reuses; note
that today's retry state is in memory (record 10 §11) and becomes persistence input.

---

## Cleanup and files changed

- **Cleanup:** `/tmp/opencode/rev06`, `rev06b`, `rev06c` and all review log files deleted;
  `docker compose -f docker/compose.yml down` run after every broker phase (no `shuttle-fleet`
  network, no live container — `docker ps -a` shows only the pre-existing `elastic_jemison`
  `hello-world` container, 12 days old); `dotnet build-server shutdown` run and the orphaned
  `nodeReuse` MSBuild workers killed. `pgrep -a dotnet` now shows only the pre-existing VS Code
  `ProjectSystem.Server.BuildHost` (PID 429473, not mine). `/tmp/opencode` holds only the
  pre-existing `mqttinspect` scratch, left as found. The `shuttle-fleet-build` image tag remains (an
  image, not a process), as review 05 left it.
- **`git status --porcelain` after this review:** this review file plus the one-line M2 table change
  and the M2 verification note in `README.md`.
