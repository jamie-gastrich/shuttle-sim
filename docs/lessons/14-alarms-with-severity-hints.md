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

---

## Addendum — writing `AlarmEvaluatorTests` (owner question)

**File and header.** Put it at `src/FleetManager.Tests/AlarmEvaluatorTests.cs`, matching `ShuttleHealthTests.cs`:

```csharp
using Contracts;

namespace FleetManager;

public class AlarmEvaluatorTests
{
    // [Fact] methods here
}
```

- No `using Xunit;` — `FleetManager.Tests.csproj:18` has a global `<Using Include="Xunit" />`. `ShuttleHealthTests.cs` relies on the same.
- `using Contracts;` is what lets you name `Alarm`, `AlarmCodes`, and `AlarmSeverities` (the test project references `Contracts`).
- No `[Trait]` on any test. These are **pure**: no database, no broker. Phase 8 greps for `docker` in `src/*.Tests` and must print nothing.

**The API under test** (record 21 §6):
- `new AlarmEvaluator(string lostCommunicationSeverity)` — throws `ArgumentOutOfRangeException` unless the value is `AlarmSeverities.Info`, `Warning`, or `Critical`.
- `IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication)` — one `Alarm` on the rising edge; empty while the condition holds, when it was never true, and after a clear.
- `Alarm(string ShuttleId, string Severity, string Code, string? Detail)`. The lost-comm alarm is `Code = AlarmCodes.LostCommunication`, `Detail = null`, `Severity` = the constructor value.

**The one gotcha that breaks most of these tests:** the latch is *stateful*. Every test below except the construction tests must call `Evaluate` more than once on **one** evaluator instance. If you build a fresh `AlarmEvaluator` between calls you reset the latch, every call looks like a rising edge, and the test proves nothing.

**Test-by-test skeleton.** These are the arrange/act/assert; you still write the file.

1. `A_new_lost_communication_condition_raises_one_alarm`
```csharp
var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
var alarm = Assert.Single(evaluator.Evaluate("SH-01", true));
Assert.Equal("SH-01", alarm.ShuttleId);
Assert.Equal(AlarmCodes.LostCommunication, alarm.Code);
Assert.Null(alarm.Detail);
```

2. `A_condition_that_stays_true_does_not_raise_again`
```csharp
var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
Assert.Single(evaluator.Evaluate("SH-01", true));   // starts: raises
Assert.Empty(evaluator.Evaluate("SH-01", true));    // holds: silent
```

3. `A_cleared_condition_can_raise_again`
```csharp
var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
Assert.Single(evaluator.Evaluate("SH-01", true));
Assert.Empty(evaluator.Evaluate("SH-01", false));   // clear re-arms
Assert.Single(evaluator.Evaluate("SH-01", true));   // raises again
```

4. `A_healthy_shuttle_raises_nothing`
```csharp
var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
Assert.Empty(evaluator.Evaluate("SH-01", false));
Assert.Empty(evaluator.Evaluate("SH-01", false));   // and latches nothing
```

5. `Two_shuttles_are_latched_independently` — interleave two ids; this proves the latch key carries the shuttle id.
```csharp
var evaluator = new AlarmEvaluator(AlarmSeverities.Critical);
Assert.Single(evaluator.Evaluate("SH-01", true));   // SH-01 raises
Assert.Single(evaluator.Evaluate("SH-02", true));   // SH-02 raises too
Assert.Empty(evaluator.Evaluate("SH-01", true));    // SH-01 still latched
Assert.Empty(evaluator.Evaluate("SH-02", false));   // clear SH-02 only
Assert.Empty(evaluator.Evaluate("SH-01", true));    // SH-01 untouched
Assert.Single(evaluator.Evaluate("SH-02", true));   // SH-02 re-armed
```

6. `The_configured_severity_is_carried_on_the_alarm` — use a value that is *not* the one in `appsettings.json`, so a hard-coded default fails.
```csharp
var evaluator = new AlarmEvaluator(AlarmSeverities.Warning);
var alarm = Assert.Single(evaluator.Evaluate("SH-01", true));
Assert.Equal(AlarmSeverities.Warning, alarm.Severity);
```
(The appsettings value is `critical`; if the code hard-codes it, this goes red.)

7. `An_unknown_severity_is_rejected`
```csharp
Assert.Throws<ArgumentOutOfRangeException>(() => new AlarmEvaluator("emergency"));
```
Add a second `Assert.Throws` for `""` if you want; both must throw.

**Style, to match the repo.**
- Use `const string ShuttleId = "SH-01";` when a test uses one id, like `ShuttleHealthTests.cs:8`.
- `Assert.Single(list)` **returns** the one element — grab the alarm with it instead of `list[0]` plus a separate count assert. It fails usefully (one vs many).
- Comments that explain the latch are welcome; comments that hand over the solution are not.

**Which mutation each test catches** (from the lesson's list):
- delete `_raised.Add(key)` → #2 goes red;
- delete `_raised.Remove(key)` → #3 goes red;
- hard-code the severity → #6 goes red;
- drop the constructor validation → #7 goes red.
Note the honest gap the lesson already states: keying the latch on the shuttle id *alone* instead of `(shuttleId, code)` does **not** turn #5 red today, because there is only one code. #5 is still worth writing — it is the forward-proof for the second code.