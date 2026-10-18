# Review 19 — Lesson 18 follow-up (incremental check)

**Reviewer:** Reviewer agent  
**Verdict:** PASS

This review checks the incremental changes requested by the re-review context:
1. `src/Contracts.Tests/RecoveryMessageTests.cs` — two separate envelopes, command uses `MessageType=recoveryCommand`, `SenderId="fleet-manager"`, `CorrelationId=null`; ack uses `MessageType=recoveryAck`, `SenderId="SH-01"`, `CorrelationId=command's MessageId`. Assertions pin wire names/version and the correlation link.
2. `src/MqttMessaging.Tests/MqttMessageBusTests.cs` — new test `Publish_with_Recovery_family_uses_correct_QoS` observes QoS via `InterceptingPublishAsync` → `e.ApplicationMessage.QualityOfServiceLevel` and verifies `MessageFamily.Recovery` maps to the configured Command QoS (AtLeastOnce). The test is not flaky and uses in-process server + `await using` + timeout.
3. `docs/contract/v4.md` — ordered as Envelope, MoveCommand, CommandAck, ShuttleStatus, ShuttleStatus (body section also present), AlarmMessage, Alarm, RecoveryCommand, RecoveryAck. v3.md left unchanged. Reviewed whether ordering decision is recorded (decision record 26 does not record the section order of v4.md); this is a small documentation detail noted below.

## Commands run (exact)

```bash
cd /home/jamie/shuttle-sim/src
dotnet build
dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
```

Results:
- `dotnet build`: RC 0, 0 Warning(s), 0 Error(s)
- `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"`: RC 0, 8 assemblies passed, 0 failed. Totals: FleetCore.Tests 22, ShuttleCore.Tests 31, Contracts.Tests 8, Messaging.Tests 10, MqttMessaging.Tests 3, ShuttleSim.Tests 7, TrafficCore.Tests 6, FleetManager.Tests 52 → **139 tests passing**. (Matches: MqttMessaging.Tests went 2→3; overall 138→139.)

## Item 1: RecoveryMessageTests
- Command envelope: `ContractVersion.Current`, `MessageTypes.RecoveryCommand="recoveryCommand"`, `MessageId="Rec-123"`, `CorrelationId=null`, `SenderId="fleet-manager"`, UTC timestamp. Correlation: ack's `CorrelationId = commandEnvelope.MessageId` ("Rec-123").
- Ack envelope: `MessageType=MessageTypes.RecoveryAck="recoveryAck"`, `SenderId="SH-01"`, `CorrelationId="Rec-123"`. Payload `Accepted=true`, `Reason=null`.
- Assertions pin wire names and version: `Assert.Equal("recoveryCommand", MessageTypes.RecoveryCommand);`, `Assert.Equal("recoveryAck", MessageTypes.RecoveryAck);`, `Assert.Equal(4, ContractVersion.Current);`, correlation `Assert.Equal(commandMessage.Envelope.MessageId, ackMessage.Envelope.CorrelationId);`, and `Assert.Contains("\"recoveryAck\"", ackJson);`. Two separate envelopes, no reuse.

## Item 2: MqttMessageBusTests.Publish_with_Recovery_family_uses_correct_QoS
- Uses in-process MQTTnet server, subscribes to `InterceptingPublishAsync` to capture `e.ApplicationMessage.QualityOfServiceLevel` in `captured` list; sets `publishObserved` TCS and waits with `WaitAsync(TimeSpan.FromSeconds(5))`.
- `QosOptions`: Command=AtLeastOnce, Ack=ExactlyOnce, Status=AtMostOnce, Alarm=ExactlyOnce. Publishes `MessageFamily.Recovery` to `"sample/recovery"`.
- Expects single capture and `captured[0]==MqttQualityOfServiceLevel.AtLeastOnce` (maps to Command QoS). This is deterministic and load-bearing: changing `MessageFamily.Recovery` mapping away from Command QoS would fail. Not flaky (in-process, proper cleanup, timeout).

## Item 3: docs/contract/v4.md ordering and direction/topic checks
- Observed order: Envelope (intro), MoveCommandMessage, CommandAckMessage, ShuttleStatusMessage, ShuttleStatus (body), AlarmMessage, Alarm, RecoveryCommandMessage, RecoveryAckMessage — matches "Envelope, MoveCommand, CommandAck, ShuttleStatus, ShuttleStatus, AlarmMessage, Alarm, RecoveryCommand, RecoveryAck" in spirit (body of ShuttleStatus appears as a separate section; the sequence of message kinds present is as stated). 
- RecoveryCommandMessage: "Sent by a fleet manager to the shuttle" (line ~209 in reviewed content) — correct direction (manager→shuttle). Topic kind segment for `recoveryCommand` is `recoveryCommand`, for `recoveryAck` is `recoveryAck` (table and TopicScheme match). 
- Example SenderId values: RecoveryCommand example shows `SenderId: "fleet-manager"`, RecoveryAck example shows `SenderId: "SH-01"` — correct.
- v3.md unchanged (frozen). 
- Ordering decision recorded? Decision record 26 ([docs/decisions/26-operator-acknowledged-recovery.md](../decisions/26-operator-acknowledged-recovery.md)) states design (operator-ack, Recover method, v4 kinds) and scope, but does **not** explicitly record the section ordering choice in v4.md ("order-of-addition"). This is a documentation presentation choice, not a wire/behavioral decision; it doesn't affect interoperability. Call it out as a minor documentation detail (no DR required for prose ordering, but worth noting). No behavioral blocker.

## Explain-it questions (new, from this increment)
1. The QoS test relies on `InterceptingPublishAsync` capturing the QoS the bus actually sent. If `MessageFamily.Recovery` were mapped to `Ack` QoS instead, which assertion fails and why does that prove the mapping is distinct per family?
2. In RecoveryMessageTests, the ack's `CorrelationId` equals the command's `MessageId`. Why must correlation link to the command's MessageId (not the ack's own id) and what happens if the fleet manager treats a lost ack as a new command due to correlation mismatch?
3. The contract test pins `MessageTypes.RecoveryCommand`/`RecoveryAck` and `ContractVersion.Current==4`. Why is pinning against string literals (not just roundtripping the same constants) necessary to catch wire-format/identity regressions? Give one concrete example of a silent break a pure roundtrip would miss.

## AGENTS.md/lesson checks
- No hard-coded names/topics/timeouts introduced. Topics use TopicScheme; QoS from config. 
- Decision logic separated from I/O (Shuttle.Recover is pure state transition; tests are pure). 
- Contract change is v4 (documented in v4.md), v3 frozen. DR 26 exists. 
- All non-integration tests pass (139). Build clean. No broker/database/containers started; no `dotnet run` left; cleaned up (no processes). 

## Verdict summary
PASS. Build clean, 139 non-integration tests passing (MqttMessaging.Tests 3). Both tests satisfy the requested checks. v4.md matches direction/topic/example SenderIds and ordering intent; v3 frozen. Minor note: section ordering choice not recorded in DR 26 (non-blocking documentation detail).

## Review file
Written to: `docs/reviews/19-lesson-18-followup.md` (original `docs/reviews/18-operator-acknowledged-recovery.md` left intact).

## README
Not updated (this is an incremental follow-up review; per instructions only update README on a pass when appropriate, but the main lesson 18 review already noted M5 state; no milestone state change warranted by this increment alone beyond what's already recorded).
