# Hints for lesson 14: Alarms with severity, rising-edge detection, and contract v3

**Decision:** [record 21](../decisions/21-alarms-with-severity.md) — dedicated `alarm` kind (v3), severity taxonomy, and rising-edge detection.

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- **An alarm is an event.** A status is a level; an alarm says "a bad condition just started." The latch means "raise once, stay quiet, clear and re-raise". 
- **Edge vs level.** If you raise whenever the condition is true, you flood the dashboard. Only raise when it flips from false to `true`. Clear the latch when it goes `false`.
- **Dedicated kind.** Use `alarm`, not `FaultReason` on a `shuttleStatus`. The alarm body carries `Severity` and `Code`; severity lives in the body (not the envelope). 
- **Contract v3.** Bump `ContractVersion.Current = 3` and add `MessageTypes.Alarm = "alarm"`. `v2.md` must stay as it is; write `docs/contract/v3.md`.
- **Code vocabulary.** Severities: `info`, `warning`, `critical`. Codes: `lostCommunication`, `blockedPath`, `palletNotDetected`. The manager's lost-communication severity comes from `FleetManager:LostCommunicationSeverity`; don't hard-code it.
- **Pure evaluator.** `AlarmEvaluator` may reference `Contracts` (deliberate). It must contain no clock, no I/O, no MQTT. Guard the latch with a `lock`; one lock per public method.
- **Counts.** Contracts.Tests 7 (+3), FleetManager.Tests 28 (+7), total 100, 8 `Passed!`, 0 `Failed!`, RC 0. Rename the Messaging.Tests test method (count unchanged).

---

## Tier 2 — direction

1. **v3 types in `src/Contracts/`.** Create `Alarm.cs`, `AlarmMessage.cs`, `AlarmSeverities.cs`, `AlarmCodes.cs`. In `MessageTypes.cs` add the `alarm` constant and set `Current = 3`. Do **not** change the envelope, status, commands, or acks. 
2. **The latch key.** Use a `HashSet<(string ShuttleId, string Code)>`. `Add` returns `true` only if the pair was not already there — that is your rising edge in one line. When clearing, remove the pair.
3. **`AlarmEvaluator` shape.** Constructor takes `lostCommunicationSeverity`; reject anything not in the three severity values with `ArgumentOutOfRangeException`. `Evaluate(string shuttleId, bool lostCommunication)` validates a blank id, takes `lock (_lock)` once, uses `_raised.Add(key)` for the edge, calls `_raised.Remove(key)` on false, and returns `IReadOnlyList<Alarm>`. Return an `Alarm` with the configured severity, `Code = AlarmCodes.LostCommunication`, `Detail = null`.
4. **Severity validation.** Compare against the three `AlarmSeverities` constants. Don't create a new literal; use them. 
5. **Purity check.** No `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, `Postgres`. But **do** allow `using Contracts;` and returning `Contracts.Alarm`.
6. **Contract page `docs/contract/v3.md`.** Version 3. Include `alarm` in the kinds table with topic segment `alarm` and topic `{root}/{shuttleId}/alarm`. Give `AlarmMessage` and `Alarm` field tables, severity and code lists, and a JSON example with `ContractVersion: 3` and `MessageType: "alarm"`. Leave `docs/contract/v2.md` untouched.
7. **Tests.** Contracts.Tests: round-trip, field names, wire names. FleetManager.Tests: 7 cases (new raises once; held stays quiet; cleared re-raises; healthy raises nothing; two latched independently; configured severity carried; unknown severity rejected). Rename the stale Messaging.Tests method to `..._and_the_current_version`; keep assertions unchanged.
8. **Counts and gates.** `Contracts.Tests=7`, `FleetManager.Tests=28`, total 100, 8 `Passed!`, 0 `Failed!`. Verify the stale name is gone. Verify `v2.md` Version 2 and `v3.md` Version 3.

---

## Tier 3 — near-solution shapes

These are shapes, not the solution. You still write the tests and any small details.

**`Alarm.cs`**
```csharp
public sealed record Alarm(
    string ShuttleId,
    string Severity,
    string Code,
    string? Detail
);
```

**`AlarmMessage.cs`**
```csharp
public sealed record AlarmMessage(MessageEnvelope Envelope, Alarm Alarm);
```

**`AlarmSeverities.cs`**
```csharp
public static class AlarmSeverities
{
    public const string Info = "info";
    public const string Warning = "warning";
    public const string Critical = "critical";
}
```

**`AlarmCodes.cs`**
```csharp
public static class AlarmCodes
{
    public const string LostCommunication = "lostCommunication";
    public const string BlockedPath = "blockedPath";
    public const string PalletNotDetected = "palletNotDetected";
}
```

**`MessageTypes.cs` (relevant changes)**
```csharp
public static class MessageTypes
{
    public const string MoveCommand = "moveCommand";
    public const string CommandAck = "commandAck";
    public const string ShuttleStatus = "shuttleStatus";
    public const string Alarm = "alarm";
}
public static class ContractVersion
{
    public const int Current = 3;
}
```

**`AlarmEvaluator.cs` (shape only)**
```csharp
using Contracts;

namespace FleetManager;

public sealed class AlarmEvaluator
{
    private readonly string _lostCommunicationSeverity;
    private readonly object _lock = new();
    private readonly HashSet<(string ShuttleId, string Code)> _raised = new();

    public AlarmEvaluator(string lostCommunicationSeverity)
    {
        if (lostCommunicationSeverity is not (AlarmSeverities.Info or AlarmSeverities.Warning or AlarmSeverities.Critical))
            throw new ArgumentOutOfRangeException(nameof(lostCommunicationSeverity));
        _lostCommunicationSeverity = lostCommunicationSeverity;
    }

    public IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication)
    {
        if (string.IsNullOrWhiteSpace(shuttleId))
            throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));

        lock (_lock)
        {
            var key = (shuttleId, AlarmCodes.LostCommunication);
            if (lostCommunication && _raised.Add(key))
                return new[] { new Alarm(shuttleId, _lostCommunicationSeverity, AlarmCodes.LostCommunication, null) };
            if (!lostCommunication)
                _raised.Remove(key);
            return Array.Empty<Alarm>();
        }
    }
}
```

**`appsettings.json` (FleetManager section)**
```json
"FleetManager": {
    "RoundTripTimeoutMs": 10000,
    "AckTimeoutMs": 2000,
    "MaxAttempts": 3,
    "BackoffMs": 500,
    "LivenessTimeoutMs": 3000,
    "LostCommunicationSeverity": "critical"
}
```

**The renaming change.** In `Messaging.Tests/MessageEnvelopesTests.cs`, change the method name only. Everything else (the `Assert.Equal(ContractVersion.Current, ...)`) stays exactly the same.

**If a test feels trivial.** The latch is the point. Prove the test bites when you remove the `Add` guard or the `Remove` clear. For the "held condition" case, do: evaluate true → 1; evaluate true again → 0. For "two shuttles latched independently": SH-01 true→1, SH-02 true→1, SH-01 stays true→0, SH-02 true→0, both false→0. 

---

## Final sanity check

- `Contracts.Tests` shows 7 passed. `FleetManager.Tests` 28. `Messaging.Tests` still 8, with the new test name present and the old one gone.
- `v2.md` has `Version: 2` and no alarm/kind; `v3.md` has `Version: 3` and `alarm` with the topic.
- `AlarmEvaluator` compiles and references `Contracts` but no I/O/clock.
- The locks match `ShuttleHealth`'s discipline: one lock, one atomic operation per public method.