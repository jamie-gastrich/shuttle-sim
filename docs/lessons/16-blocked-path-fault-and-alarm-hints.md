# Hints for lesson 16: The blocked-path fault and the second alarm source

**Decision:** [record 23](../decisions/23-blocked-path-fault-and-second-alarm.md) — a blocked path is detected by `Shuttle.StartMove` from rack model data, reported as a `fault` status level, and published as one `blockedPath` alarm per edge by the existing serve loop. No contract bump.

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- **Three product files are new.** `src/ShuttleCore/FaultReasons.cs`, `src/FleetManager/FaultAlarmCodes.cs`, and `src/FleetManager.Tests/FaultAlarmCodesTests.cs`. Everything else is an edit. If you find yourself making a fourth new product file, stop and re-read the requirements.
- **`FaultReasons` goes in `ShuttleCore`, not `Contracts`.** The shuttle's wire vocabulary already lives there: `ShuttleStateWire.ToWireName` is in `ShuttleCore`. Put `FaultReasons` beside it. `ShuttleCore` stays dependency-free.
- **The rack owns the obstacle.** Add a `HashSet<string> _blockedLanes` field. `MarkBlocked`, `IsBlocked`, and `IsRouteBlocked` read and write it. An unknown lane throws `ArgumentException` — reuse the private `Ordinal` helper, which already throws that.
- **The route is a range of ordinals, both ends included.** The rack already numbers every lane: `A01-L01`=0, `A01-L02`=1, `A02-L01`=2, `A02-L02`=3. So the route from one lane to another is every ordinal from `Math.Min` to `Math.Max`. Use `<=`, not `<`. The destination is part of the route.
- **The simplest `IsRouteBlocked` walks the blocked set, not the lanes.** For each blocked lane, ask "is its ordinal inside the range?" One loop, no reverse lookup.
- **The check goes in `StartMove` after the lane check.** Order today: `EnsureInOrder`, `EnsureState(Idle)`, `if (!_rack.IsLane(toLane)) throw`, then set `_moveToLane`. Insert the blocked check between the `IsLane` throw and `_moveToLane = toLane`. Put it after the `IsLane` check so an unknown lane still throws `ShuttleRejectedException`, exactly as today.
- **A fault is a normal return, not an exception.** Call `RaiseFault(FaultReasons.BlockedPath, now)` and `return`. Do not throw. The caller must inspect `_shuttle.State`.
- **The endpoint gains a third outcome.** After `StartMove` returns without throwing, check `_shuttle.State == ShuttleState.Fault`. If so: ack `accepted: false` with `_shuttle.FaultReason`, publish the status with the **public** `PublishStatusAsync(now, ct)` overload, and return.
- **Do not mark the guard on the fault path.** `_guard.MarkCompleted(messageId)` belongs only on the accepted path. If the fault path marked it, a retry would hit the duplicate branch and ack `Accepted = true` for a move that never happened.
- **No literals on the wire.** The phrase `"blocked path"` is `FaultReasons.BlockedPath`. The state `"fault"` is `ShuttleState.Fault.ToWireName()`. The code `"blockedPath"` is `AlarmCodes.BlockedPath`. The severity `"warning"` comes from config. No file should hold a bare string for these.
- **The evaluator restructure is where the bug hides.** The current `Evaluate` returns inside the lock as soon as it sees its one condition. With two conditions you must build a list and check both before returning. An early `return` after `lostCommunication` means a faulted-and-silent shuttle never publishes its second alarm.
- **Validate the new inputs.** The constructor validates **both** severities. `Evaluate` throws `ArgumentOutOfRangeException` for a `faultCode` that is neither `null` nor `AlarmCodes.BlockedPath`.
- **The latch key does not change.** It is already `(string ShuttleId, string Code)`. Both conditions share the one set and the one lock. That is the whole point of record 21.
- **The loop stores the latest fault code per shuttle.** Add `private readonly Dictionary<string, string> _faultCodes = new(StringComparer.Ordinal);`, guarded by the **existing** `_knownLock`. A healthy status removes the entry; a fault status sets it. Then snapshot the ids **and** a copy of the dictionary under one lock acquisition, and evaluate outside the lock.
- **`FleetServeLoop.cs` now names `ShuttleCore` types.** Add `using ShuttleCore;`. `ShuttleState.Fault.ToWireName()` is a `ShuttleCore` member.
- **`FleetManager.csproj` needs an explicit `ShuttleCore` reference.** It compiles today only because `TrafficCore` pulls `ShuttleCore` in transitively, and `RunLoop.cs:2` already writes `using ShuttleCore;`. Add the direct `<ProjectReference Include="..\ShuttleCore\ShuttleCore.csproj" />`.
- **The endpoint test helper must expose the rack.** `CreateShuttle()` in `ShuttleEndpointTests.cs` builds its own `Rack` and returns only the `Shuttle`. A blocked-lane test must mark the lane **before** the shuttle moves, so the test needs the rack. Build the rack in the test, or change the helper to take one.
- **You will touch more call sites than you think.** `AlarmEvaluatorTests.cs` has **7** `new AlarmEvaluator(...)` and **15** `Evaluate(...)` calls. `FleetServeLoopTests.cs` has **8** more `new AlarmEvaluator(...)` — one per test. Let the compiler list them; it will.
- **Counts.** Pure tests go **109 → 124**: `ShuttleCore.Tests` 25 → **29** (+4), `ShuttleSim.Tests` 6 → **7** (+1), `FleetManager.Tests` 36 → **46** (+10). Gate: RC 0, **8** `Passed!`, 0 `Failed!`, 124 total.

---

## Tier 2 — direction

Work the fifteen requirements in this order. Steps 1–4 make the product compile; steps 5–8 add the tests; steps 9–11 are the guardrails.

1. **`FaultReasons` and the rack (won't build until both land together).**
   - New `src/ShuttleCore/FaultReasons.cs`: `public const string BlockedPath = "blocked path";` in namespace `ShuttleCore`.
   - `Rack.cs`: add `private readonly HashSet<string> _blockedLanes = new(StringComparer.Ordinal);` and the three members. `MarkBlocked`/`IsBlocked` throw `ArgumentException` for an unknown lane (call `Ordinal` to reuse its throw).

2. **The detection in `Shuttle.StartMove`.**
   - Insert the blocked check after the `IsLane` throw and before `_moveToLane = toLane`. `RaiseFault(FaultReasons.BlockedPath, now)` then `return`.

3. **The endpoint's fault branch.**
   - After the `try/catch`, before `_guard.MarkCompleted`, branch on `_shuttle.State == ShuttleState.Fault`. Ack `accepted: false`, publish the public status, return. Leave the accept path and the reject path exactly as they are.

4. **The two config keys and the shuttle wiring.**
   - `src/ShuttleSim/appsettings.json`: add `"BlockedLanes": []` inside `Shuttle`.
   - `src/ShuttleSim/Program.cs` `RunServerAsync`: read `Shuttle:BlockedLanes` as `string[]` (default empty), build the rack, call `rack.MarkBlocked(lane)` for each, **before** the endpoint is built.

5. **The mapper.**
   - New `src/FleetManager/FaultAlarmCodes.cs`. Pure. `using Contracts;` and `using ShuttleCore;`. A `switch` on `faultReason`: `FaultReasons.BlockedPath => AlarmCodes.BlockedPath`, `_ => null`.

6. **The evaluator.**
   - Constructor gains `string blockedPathSeverity`; validate both against `AlarmSeverities` (the existing `is not (Info or Warning or Critical)` pattern).
   - `Evaluate` gains `string? faultCode`; validate it; build a `List<Alarm>` inside the one lock and check both conditions. Return the list. Blocked-path alarm uses `_blockedPathSeverity`, `AlarmCodes.BlockedPath`, `Detail = null`.

7. **The serve loop.**
   - Field `_faultCodes` (as above), guarded by `_knownLock`.
   - `HandleStatusAsync`: inside the existing lock block, compute the code from the status and set/remove the entry.
   - `EvaluateAndPublishAsync`: snapshot the ids and copy `_faultCodes` under the lock; pass `TryGetValue(...) ? c : null` to `Evaluate`.

8. **The manager wiring.**
   - `src/FleetManager/appsettings.json`: `"BlockedPathSeverity": "warning"` beside `LostCommunicationSeverity`.
   - `src/FleetManager/Program.cs` `RunServeAsync`: read the new key (required) and pass both severities into `new AlarmEvaluator(...)`.
   - `src/FleetManager/FleetManager.csproj`: add the direct `ShuttleCore` reference.

9. **`ShuttleCore.Tests` (four new tests).** Copy the exact names from the lesson; Phase 6b finds them by anchored name.
   - `StartMove_across_a_blocked_lane_enters_fault_with_the_blocked_path_reason`
   - `StartMove_through_a_lane_blocked_between_two_lanes_enters_fault`
   - `IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked`
   - `MarkBlocked_for_an_unknown_lane_is_rejected`

10. **`ShuttleSim.Tests` and `FleetManager.Tests`.**
    - `A_blocked_move_is_acknowledged_as_not_accepted_and_reports_a_fault_status` — build the rack in the test, mark a lane, then move. The existing `FakeBus` is reusable; only the shuttle helper needs the rack.
    - `FaultAlarmCodesTests.cs` (2 tests), `AlarmEvaluatorTests.cs` (5 new + update 7 constructions and 15 `Evaluate` calls), `FleetServeLoopTests.cs` (3 new + update 8 constructions). The lesson lists every name.

11. **Guardrails and the run.**
    - Do not touch `src/Contracts/`, `docs/contract/`, `src/Messaging/`, `src/MqttMessaging/`, or the protected M4/M3 files. `ContractVersion.Current` stays `3`; no `v4.md`.
    - `FaultAlarmCodes.cs` and `FleetServeLoop.cs` must still contain no `DateTime.UtcNow`, `Guid.NewGuid`, `Thread.Sleep`, `MqttMessaging`, or `Npgsql`.
    - Run the phases. Phase 6 is the gate: `cd src && dotnet build`, then the filtered run. Read `RC`, the `Passed!` count, and the total. They must be `0`, `8`, `0`, and `124`.

---

## Tier 3 — near-solution shapes

These are shapes, not the solution. You still write every test, the wiring, and the small details.

**`FaultReasons.cs`**
```csharp
namespace ShuttleCore;

public static class FaultReasons
{
    public const string BlockedPath = "blocked path";
}
```

**`Rack.cs` additions**
```csharp
private readonly HashSet<string> _blockedLanes = new(StringComparer.Ordinal);

public void MarkBlocked(string laneId)
{
    _ = Ordinal(laneId);            // unknown lane -> ArgumentException
    _blockedLanes.Add(laneId);
}

public bool IsBlocked(string laneId)
{
    _ = Ordinal(laneId);            // unknown lane -> ArgumentException
    return _blockedLanes.Contains(laneId);
}

public bool IsRouteBlocked(string fromLane, string toLane)
{
    var start = Math.Min(Ordinal(fromLane), Ordinal(toLane));
    var end = Math.Max(Ordinal(fromLane), Ordinal(toLane));
    foreach (var blocked in _blockedLanes)
        if (_laneOrdinals[blocked] >= start && _laneOrdinals[blocked] <= end)
            return true;
    return false;
}
```

**`Shuttle.StartMove` insertion (between the `IsLane` throw and `_moveToLane = toLane`)**
```csharp
if (_rack.IsRouteBlocked(_position, toLane))
{
    RaiseFault(FaultReasons.BlockedPath, now);
    return;
}
```

**`ShuttleEndpoint.HandleCommandAsync` branch (after the `try/catch`, before `_guard.MarkCompleted`)**
```csharp
if (_shuttle.State == ShuttleState.Fault)
{
    await AcknowledgeAsync(command, accepted: false, reason: _shuttle.FaultReason, now, cancellationToken);
    await PublishStatusAsync(now, cancellationToken);   // the public overload
    return;
}
```

**`FaultAlarmCodes.cs`**
```csharp
using Contracts;
using ShuttleCore;

namespace FleetManager;

public static class FaultAlarmCodes
{
    public static string? ForReason(string? faultReason) => faultReason switch
    {
        FaultReasons.BlockedPath => AlarmCodes.BlockedPath,
        _ => null,
    };
}
```

**`AlarmEvaluator` (shape only — check the two conditions before returning)**
```csharp
private readonly string _blockedPathSeverity;

public AlarmEvaluator(string lostCommunicationSeverity, string blockedPathSeverity)
{
    if (lostCommunicationSeverity is not (AlarmSeverities.Info or AlarmSeverities.Warning or AlarmSeverities.Critical))
        throw new ArgumentOutOfRangeException(nameof(lostCommunicationSeverity));
    if (blockedPathSeverity is not (AlarmSeverities.Info or AlarmSeverities.Warning or AlarmSeverities.Critical))
        throw new ArgumentOutOfRangeException(nameof(blockedPathSeverity));
    _lostCommunicationSeverity = lostCommunicationSeverity;
    _blockedPathSeverity = blockedPathSeverity;
}

public IReadOnlyList<Alarm> Evaluate(string shuttleId, bool lostCommunication, string? faultCode)
{
    if (string.IsNullOrWhiteSpace(shuttleId))
        throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));
    if (faultCode is not (null or AlarmCodes.BlockedPath))
        throw new ArgumentOutOfRangeException(nameof(faultCode));

    lock (_lock)
    {
        var alarms = new List<Alarm>(2);

        var lostKey = (shuttleId, AlarmCodes.LostCommunication);
        if (lostCommunication && _raised.Add(lostKey))
            alarms.Add(new Alarm(shuttleId, _lostCommunicationSeverity, AlarmCodes.LostCommunication, null));
        else if (!lostCommunication)
            _raised.Remove(lostKey);

        var faultKey = (shuttleId, AlarmCodes.BlockedPath);
        if (faultCode == AlarmCodes.BlockedPath && _raised.Add(faultKey))
            alarms.Add(new Alarm(shuttleId, _blockedPathSeverity, AlarmCodes.BlockedPath, null));
        else if (faultCode != AlarmCodes.BlockedPath)
            _raised.Remove(faultKey);

        return alarms;
    }
}
```

**`FleetServeLoop` additions**
```csharp
using ShuttleCore;                                   // add to the usings

private readonly Dictionary<string, string> _faultCodes = new(StringComparer.Ordinal);

// HandleStatusAsync, inside the existing lock (_knownLock) block:
var code = status.Status.State == ShuttleState.Fault.ToWireName()
    ? FaultAlarmCodes.ForReason(status.Status.FaultReason)
    : null;
if (code is null) _faultCodes.Remove(shuttleId);
else _faultCodes[shuttleId] = code;

// EvaluateAndPublishAsync: snapshot ids AND a copy of the fault codes under one lock.
string[] ids;
Dictionary<string, string> faults;
lock (_knownLock)
{
    ids = _knownShuttles.ToArray();
    faults = new Dictionary<string, string>(_faultCodes, StringComparer.Ordinal);
}
foreach (var id in ids)
{
    var lost = _health.IsUnresponsive(id, now);
    var faultCode = faults.TryGetValue(id, out var c) ? c : null;
    foreach (var alarm in _evaluator.Evaluate(id, lost, faultCode))
        await PublishAlarmAsync(alarm, now, cancellationToken);
}
```

**Config and wiring**
```jsonc
// src/ShuttleSim/appsettings.json
"Shuttle": { "Id": "SH-01", "StartLane": "A01-L01", "HeartbeatIntervalMs": 1000, "BlockedLanes": [] },

// src/FleetManager/appsettings.json  (inside FleetManager, beside LostCommunicationSeverity)
"BlockedPathSeverity": "warning",
```
```csharp
// src/ShuttleSim/Program.cs — after the rack is built, before the endpoint
var blockedLanes = configuration.GetSection("Shuttle:BlockedLanes").Get<string[]>() ?? Array.Empty<string>();
foreach (var lane in blockedLanes) rack.MarkBlocked(lane);

// src/FleetManager/Program.cs — RunServeAsync
var blockedPathSeverity = configuration.GetValue<string>("FleetManager:BlockedPathSeverity")
    ?? throw new InvalidOperationException("FleetManager:BlockedPathSeverity is missing.");
var evaluator = new AlarmEvaluator(severity, blockedPathSeverity);
```
```xml
<!-- src/FleetManager/FleetManager.csproj -->
<ProjectReference Include="..\ShuttleCore\ShuttleCore.csproj" />
```

**Test helper change (`ShuttleEndpointTests.cs`, shape only)**
```csharp
var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
rack.MarkBlocked("A02-L02");                       // inject the obstacle before the move
var timing = new ShuttleTiming(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
var shuttle = new Shuttle("SH-01", rack, "A01-L01", timing);
var endpoint = CreateEndpoint(shuttle, bus);
```

**The status fixture for the fault, reused by the loop tests**
```csharp
// ShuttleStatus(ShuttleId, State, Position, Timestamp, FaultReason?)
static string FaultStatusJson(string shuttleId, DateTime sentAt) => JsonSerializer.Serialize(
    new ShuttleStatusMessage(
        new MessageEnvelope(ContractVersion.Current, MessageTypes.ShuttleStatus,
            Guid.NewGuid().ToString(), null, shuttleId, sentAt),
        new ShuttleStatus(shuttleId, "fault", "A01-L01", sentAt, FaultReasons.BlockedPath)));
```

**Common traps** (each one is a real way this slice breaks):

- **Early `return` in `Evaluate`.** Keeping the old `if (lostCommunication && _raised.Add(key)) return ...;` shape means the two-alarm test fails. Build the list.
- **A literal `"fault"` in the loop.** Compare against `ShuttleState.Fault.ToWireName()`, not a typed string.
- **Missing `using ShuttleCore;` in `FleetServeLoop.cs`.** The `ShuttleState` name will not resolve.
- **Marking the guard in the fault branch.** A retry then acks `Accepted = true` for a move that never happened.
- **Exclusive route range.** `< end` misses the destination; the intermediate-lane test catches `<=` mistakes the other way.
- **Calling the private status overload with a literal.** Use the public `PublishStatusAsync(now, ct)` so the state and reason come from the shuttle.
- **One-argument `new AlarmEvaluator(...)` left anywhere.** It is a compile error on purpose: it points you at every call site.

---

## Worked example (teaching artifact — not the assignment)

The subtlest new rule is the **route range**. This tiny console prints the route for three cases and compares the range check with a destination-only check. It ran in `/tmp/opencode/l16range` on 2026-10-15 and was deleted afterwards. It is a toy. It is not the assignment.

`Program.cs` (complete, from `dotnet new console`):

```csharp
// Lesson 16 hints toy: the route range used by Rack.IsRouteBlocked.
// This is the arithmetic only. It is a TOY, not the assignment.
var ordinal = new Dictionary<string, int>(StringComparer.Ordinal)
{
    ["A01-L01"] = 0, ["A01-L02"] = 1, ["A02-L01"] = 2, ["A02-L02"] = 3,
};

string[] Route(string from, string to)
{
    var start = Math.Min(ordinal[from], ordinal[to]);
    var end = Math.Max(ordinal[from], ordinal[to]);
    return ordinal.Where(kv => kv.Value >= start && kv.Value <= end)
                  .OrderBy(kv => kv.Value).Select(kv => kv.Key).ToArray();
}

bool RouteBlocked(string from, string to, HashSet<string> blocked) =>
    Route(from, to).Any(blocked.Contains);

bool DestinationOnly(string to, HashSet<string> blocked) => blocked.Contains(to);

var cases = new (string From, string To, HashSet<string> Blocked)[]
{
    ("A01-L01", "A02-L02", new(StringComparer.Ordinal) { "A02-L02" }), // obstacle = destination
    ("A01-L01", "A02-L01", new(StringComparer.Ordinal) { "A01-L02" }), // obstacle in the middle
    ("A01-L01", "A02-L02", new(StringComparer.Ordinal)),              // clear
};

foreach (var (from, to, blocked) in cases)
{
    var route = Route(from, to);
    Console.WriteLine($"{from} -> {to}  route=[{string.Join(" ", route)}]  blocked=[{string.Join(" ", blocked)}]");
    Console.WriteLine($"    route check:       {RouteBlocked(from, to, blocked)}");
    Console.WriteLine($"    destination-only:  {DestinationOnly(to, blocked)}");
}
```

Run it:

```bash
cd /tmp/opencode/l16range
dotnet run
```

Observed output (a real run on 2026-10-15), exit 0:

```text
A01-L01 -> A02-L02  route=[A01-L01 A01-L02 A02-L01 A02-L02]  blocked=[A02-L02]
    route check:       True
    destination-only:  True
A01-L01 -> A02-L01  route=[A01-L01 A01-L02 A02-L01]  blocked=[A01-L02]
    route check:       True
    destination-only:  False
A01-L01 -> A02-L02  route=[A01-L01 A01-L02 A02-L01 A02-L02]  blocked=[]
    route check:       False
    destination-only:  False
```

Read it like this:

- **Case 2 is the one that matters.** The destination `A02-L01` is clear, but the middle lane `A01-L02` is blocked. The range check says `True`; the destination-only check says `False`. If you wrote a destination-only check, the shuttle would drive through the obstacle. That is exactly what `StartMove_through_a_lane_blocked_between_two_lanes_enters_fault` pins.
- **The route always includes both ends.** Case 1 shows `A01-L01` (the shuttle) and `A02-L02` (the destination) both on the route.
- **A clear route returns `False`.** Case 3 shows the empty blocked set.

The full loop — obstacle to fault status to one alarm per edge, plus a second independent alarm — is in the main lesson's toy (`/tmp/opencode/l16toy`, deleted). That toy's real output shows `blockedPath` published once at `00:00:01`, then `lostCommunication` once at `00:00:06`, with two `CLEAR` lines at `00:00:07`.

---

## Verified facts and counts (check these against your repo)

Every line below was measured on 2026-10-15 in the repo or a clean scratch copy. Use them to self-check.

| Fact | Command | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite | clean copy: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, 8 `Passed!`, 0 `Failed!`, **109** total |
| Baseline per assembly | same run | FleetCore 18, Contracts 7, ShuttleCore 25, Messaging 9, MqttMessaging 2, ShuttleSim 6, TrafficCore 6, FleetManager 36 |
| `AlarmEvaluatorTests` call sites | `grep -c 'new AlarmEvaluator' …` and `grep -o 'Evaluate(' … \| wc -l` | 7 constructions; **15** `Evaluate(` calls |
| `FleetServeLoopTests` constructions | `grep -o 'new AlarmEvaluator' …` | **8** (one per test) |
| `FleetManager.Tests` refs `ShuttleCore` | read `FleetManager.Tests.csproj:26` | present |
| The loop file already imports `ShuttleCore` | read `RunLoop.cs:2` | `using ShuttleCore;` |
| No obstacle data today | `grep -rn 'MarkBlocked\|IsRouteBlocked\|IsBlocked' src --include='*.cs'` | no matches |
| `FaultReasons.cs` does not exist | `ls src/ShuttleCore/FaultReasons.cs` | not found |
| Config keys do not exist | `grep -rn 'BlockedLanes\|BlockedPathSeverity' src` | no matches |
| Contract stays v3 | read `src/Contracts/MessageTypes.cs:13` | `Current = 3` |

---

## Final sanity check

- `ShuttleCore.Tests` shows **29**, `ShuttleSim.Tests` shows **7**, `FleetManager.Tests` shows **46**; total pure **124**; 8 `Passed!`, 0 `Failed!`, RC 0.
- The filtered run needs **no** database and **no** broker. The optional broker demo is the only part that needs `docker compose`.
- `grep -nE 'DateTime\.UtcNow|Guid\.NewGuid|Thread\.Sleep|MqttMessaging|Npgsql' src/FleetManager/FaultAlarmCodes.cs` prints nothing.
- `grep -nE 'DateTime\.UtcNow|MqttMessaging|Npgsql' src/FleetManager/FleetServeLoop.cs` prints nothing.
- `grep -q 'using ShuttleCore;' src/FleetManager/FleetServeLoop.cs` finds it; `grep -q 'ShuttleCore' src/FleetManager/FleetManager.csproj` finds the reference.
- `grep -rniE 'docker' src/*.Tests --include='*.cs'` prints nothing.
- `src/Contracts/MessageTypes.cs` still says `Current = 3`; `docs/contract/v3.md` says `Version: 3`; no `v4.md`.
- `git diff --quiet HEAD -- src/Contracts docs/contract src/Messaging src/MqttMessaging` exits 0.
- A physical shuttle still cannot self-clear a fault. The latch clears only when a healthy status arrives, which no running component can produce yet. That is the next slice, not a bug in this one.
