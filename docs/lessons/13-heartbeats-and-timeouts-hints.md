# Hints for lesson 13: Heartbeats, lastSeenAt, and the timeout window

**Decision:** [record 20](../decisions/20-liveness-heartbeats-and-timeouts.md) — the heartbeat signal, the liveness clock, and the timeout rule.

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- **The heartbeat** is a periodic `shuttleStatus`, not a new message kind. The shuttle publishes its current status on an interval so that silence becomes meaningful. No contract change; no new topic.
- **`lastSeenAt`** is the fleet manager's record of when it last heard from a shuttle. Stamp it with the manager's clock at receipt, not the shuttle's `Timestamp`. Never move it backwards: an older or duplicate message must not erase a newer stamp.
- **Timeout detection** is `never seen OR now - lastSeenAt > window`. The window comes from configuration. "Exactly the window" is still responsive — the comparison is strictly greater than. Derive the verdict; do not store a boolean.
- **`ShuttleHealth`** is pure in `FleetManager`: no `DateTime.UtcNow`, no I/O. Every method takes `now`. Guard the dictionary with a `lock`, because the status path writes while queries read.
- **`HeartbeatPublisher`** lives in `ShuttleSim`. It takes the interval in its constructor and uses a `PeriodicTimer` with a `CancellationToken`. No `Thread.Sleep`, no `.Wait()`, no `async void`.
- **Config, no literals:** `Shuttle:HeartbeatIntervalMs` and `FleetManager:LivenessTimeoutMs`.
- **Do not change** `FleetManager/TaskDispatcher.cs`, `FleetManager/Program.cs`, `TrafficCore`, `ShuttleCore`, `Contracts`, the contract docs, or the Dockerfile. No alarms, no recovery, no reassignment.

---

## Tier 2 — direction

1. **`ShuttleHealth` shape.** Add `src/FleetManager/ShuttleHealth.cs`. Constructor takes `TimeSpan livenessTimeout` and rejects a non-positive value. Field `private readonly object _lock = new();` and `private readonly Dictionary<string, DateTime> _lastSeenAt = new(StringComparer.Ordinal);`.
2. **`RecordSeen`.** Inside the lock: if there is no entry, or `seenAt` is newer than the stored value, store `seenAt`. An older or equal value is ignored. Validate a blank id.
3. **`LastSeenAt`.** Inside the lock: return the stored value or `null`.
4. **`IsUnresponsive`.** Inside the lock: if there is no entry, return `true`; otherwise return `now - stored > _livenessTimeout`.
5. **`UnresponsiveShuttles`.** Inside the lock: collect every known id whose stamp is stale at `now`, and return it as a snapshot (`ToArray()`).
6. **`HeartbeatPublisher` shape.** Add `src/ShuttleSim/HeartbeatPublisher.cs`. Constructor takes `ShuttleEndpoint endpoint` and `TimeSpan interval`; validate both. `RunAsync(Func<DateTime> clock, CancellationToken cancellationToken)` creates a `PeriodicTimer(interval)`, and while `await timer.WaitForNextTickAsync(cancellationToken)` succeeds, calls `_endpoint.PublishStatusAsync(clock(), cancellationToken)`.
7. **Endpoint publish.** In `src/ShuttleSim/ShuttleEndpoint.cs`, add `public Task PublishStatusAsync(DateTime sentAt, CancellationToken cancellationToken)` that publishes the current `_shuttle` state, position, and fault reason. Reuse the existing private publish path; do not change what `HandleCommandAsync` does.
8. **Serve-loop wiring.** In `src/ShuttleSim/Program.cs`, read and validate `Shuttle:HeartbeatIntervalMs`, build the publisher, start `RunAsync` with a `CancellationTokenSource`, and cancel it when the process stops. Do not leave a process running.
9. **Tests.** `src/FleetManager.Tests/ShuttleHealthTests.cs` with the five pinned names (including the monotonic-and-clears case and the exact-boundary case). Add the one pinned endpoint test to `src/ShuttleSim.Tests/ShuttleEndpointTests.cs` using the existing `FakeBus`.
10. **Run the phases in order.** The counts are gated, not admired: Phase 5 expects `RC 0`, **8** `Passed!`, **0** `Failed!`, **90** tests, `FleetManager.Tests` = 21, `ShuttleSim.Tests` = 6.

---

## Tier 3 — near-solution shapes

These are **shapes**, not the solution. You still write the bodies and the tests.

**`ShuttleHealth` shape**
```csharp
public sealed class ShuttleHealth
{
    private readonly TimeSpan _livenessTimeout;
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _lastSeenAt = new(StringComparer.Ordinal);

    public ShuttleHealth(TimeSpan livenessTimeout)
    {
        if (livenessTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(livenessTimeout));
        _livenessTimeout = livenessTimeout;
    }

    public void RecordSeen(string shuttleId, DateTime seenAt)
    {
        if (string.IsNullOrWhiteSpace(shuttleId))
            throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));
        lock (_lock)
        {
            if (!_lastSeenAt.TryGetValue(shuttleId, out var previous) || seenAt > previous)
                _lastSeenAt[shuttleId] = seenAt;
        }
    }

    public bool IsUnresponsive(string shuttleId, DateTime now)
    {
        lock (_lock)
        {
            if (!_lastSeenAt.TryGetValue(shuttleId, out var seen))
                return true;
            return now - seen > _livenessTimeout;
        }
    }
}
```

**`HeartbeatPublisher` shape**
```csharp
public sealed class HeartbeatPublisher
{
    private readonly ShuttleEndpoint _endpoint;
    private readonly TimeSpan _interval;

    public HeartbeatPublisher(ShuttleEndpoint endpoint, TimeSpan interval)
    {
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        _interval = interval;
    }

    public async Task RunAsync(Func<DateTime> clock, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await _endpoint.PublishStatusAsync(clock(), cancellationToken);
    }
}
```

**Endpoint publish shape**
```csharp
public Task PublishStatusAsync(DateTime sentAt, CancellationToken cancellationToken) =>
    PublishStatusAsync(_shuttle.State.ToWireName(), sentAt, cancellationToken);
```

**Serve-loop shape**
```csharp
var heartbeatIntervalMs = configuration.GetValue<int>("Shuttle:HeartbeatIntervalMs");
if (heartbeatIntervalMs < 1)
    throw new InvalidOperationException("Shuttle:HeartbeatIntervalMs must be positive.");

using var stopping = new CancellationTokenSource();
var heartbeat = new HeartbeatPublisher(connectedEndpoint, TimeSpan.FromMilliseconds(heartbeatIntervalMs));
var heartbeatTask = heartbeat.RunAsync(() => DateTime.UtcNow, stopping.Token);
// ... wait for Ctrl+C, then stopping.Cancel(); await heartbeatTask;
```

**Boundary reminder (say it out loud)**
> `unresponsive = never seen OR (now - lastSeenAt) > window`. Exactly the window is still responsive.

**Test names the acceptance walks**
```
A_recent_heartbeat_keeps_a_shuttle_responsive
Silence_past_the_timeout_marks_a_shuttle_unresponsive
A_shuttle_silent_for_exactly_the_timeout_is_still_responsive
A_new_heartbeat_makes_a_silent_shuttle_responsive_again
A_shuttle_never_seen_is_unresponsive
Heartbeat_publishes_the_current_status_without_a_command
```

**Suite gate (the container's command)**
```bash
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# expects RC 0, 8 Passed!, 0 Failed!, 90 tests, FleetManager.Tests=21, ShuttleSim.Tests=6
```

**If stuck on the monotonic test:** call `RecordSeen("SH-01", t0)`, then `RecordSeen("SH-01", t0.AddSeconds(10))`, then `RecordSeen("SH-01", t0.AddSeconds(5))`, and assert `LastSeenAt("SH-01")` is `t0.AddSeconds(10)` — the older stamp did not move it back.

**If stuck on the heartbeat test:** the existing `FakeBus` records every `PublishAsync`. Call the new public `PublishStatusAsync(Start, CancellationToken.None)`, then assert exactly one `MessageFamily.Status` message and that its deserialized state and position match the shuttle. No broker needed.

**Acceptance sanity:** the contract stays v2 (`Version: 2`, no `v3.md`); the pure suite stays green with no database; no new message kinds; no process left running.

---

Stuck? These hints cover the key ideas without giving the full solution. The acceptance criteria in the lesson file are the authoritative spec.
