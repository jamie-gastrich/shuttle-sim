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

## Addendum — FleetManager serve-loop cancellation (owner question)

**For lesson 13 (heartbeats and timeouts).** The owner asked where in `src/FleetManager/Program.cs` to put the "wait for Ctrl+C, then `stopping.Cancel(); await heartbeatTask;`" pattern, and what it does.

### Placement relative to existing M2 code

`src/FleetManager/Program.cs` currently has:
- CLI parsing: if no args → print and return; if `move <shuttleId> <toLane>` → call `RunMoveAsync(...)`.
- `RunMoveAsync(...)` is the M2 one-shot demo (connects to bus, subscribes for ack/status for that single move, sends a command with retry, waits for result, returns exit code).

For M5 serve mode, **add a new serve branch** alongside `move` (mirroring `ShuttleSim/Program.cs` which has `serve` mode). Do **not** remove or restructure the existing `move` demo.

Suggested shape:
```csharp
if (args.Length == 0)
{
    Console.WriteLine("Fleet Manager Coming Online");
    return 0;
}

if (args.Length == 3 && args[0] == "move")
{
    return await RunMoveAsync(args[1], args[2]);
}

if (args.Length == 1 && args[0] == "serve")
{
    return await RunServeAsync();
}

Console.Error.WriteLine("Usage: FleetManager move <shuttleId> <toLane>|serve");
return 2;
```

Put the Ctrl+C shutdown code inside `RunServeAsync()` (a new method). Keep `RunMoveAsync()` exactly as-is in spirit; it is the short-lived demo. The serve mode is the long-running one that tracks liveness from incoming `shuttleStatus` messages via `ShuttleHealth`.

### Mechanics: what happens in the serve loop

The phrase "wait for Ctrl+C, then `stopping.Cancel(); await heartbeatTask;`" describes the **graceful shutdown pattern**. In `ShuttleSim/Program.cs`, the background task is `heartbeat.RunAsync(...)` (the periodic heartbeat publisher). In `FleetManager` serve mode, the long-running background work is different (you are receiving messages and updating `ShuttleHealth`), but the same shutdown mechanics apply.

Key points:
1. **Create a cancellation source for shutdown.** `using var stopping = new CancellationTokenSource();` This token controls all background work (subscriptions, timers, run loops).
2. **Start background work with that token.** Examples from ShuttleSim: `var heartbeatTask = heartbeat.RunAsync(clock, stopping.Token);`. In FleetManager serve mode, you might start any long-running tasks (e.g. a liveness checker loop, or just rely on message handlers), collecting the tasks you need to await on shutdown. If all work is event-driven via subscriptions, you still need to wait for an external shutdown signal (Ctrl+C).
3. **Wait for Ctrl+C (operator stop).** Hook `Console.CancelKeyPress` and set a `TaskCompletionSource` so the main task blocks until interrupted. Set `eventArgs.Cancel = true` to allow graceful shutdown. This mirrors ShuttleSim lines ~109-126.
4. **On shutdown, cancel and await.** When Ctrl+C fires, call `stopping.Cancel()` (so all background work sees cancellation) and `await` the background tasks (e.g. `await heartbeatTask;` in ShuttleSim). If there are multiple tasks, await them all (e.g. `Task.WhenAll`).
5. **Clean up.** Dispose buses, tokens, timers (use `using`). Do not leave brokers/listeners running.

**Line-level guidance for FleetManager/Program.cs:** Add the new `serve` branch in arg parsing (after the `move` branch). Implement `RunServeAsync()` similar in structure to `RunServerAsync()` in ShuttleSim but focused on the fleet side:
- Load config (Messaging, FleetManager timeouts/liveness config if needed).
- Connect bus and subscribe to `topics.AllStatuses` (and possibly acks) — when status arrives, deserialize and call `ShuttleHealth.RecordSeen(shuttleId, DateTime.UtcNow)` using the **manager's clock** (see lesson §2, rule 1).
- If you start any periodic task (e.g. logging unresponsive shuttles on a timer), start it with `stopping.Token` and keep a task reference.
- Wire Ctrl+C wait + cancel + await as above.
- Return 0 on clean shutdown.

**Important:** The "heartbeatTask" name in the question is the task for the long-running background operation. In ShuttleSim it's the publisher. In FleetManager serve mode, you may not have a publisher; you might have no periodic task at all if you only react to incoming messages — in that case you still need to wait for Ctrl+C and cancel, but there may be no `heartbeatTask` to await. The pattern is the same: identify what long-running work exists (subscriptions stay alive; message handling continues) and ensure clean shutdown. If nothing to await, just cancel and return.

**What happens there (mechanics):** The serve mode runs until interrupted. The message bus subscriptions remain active; when `shuttleStatus` messages arrive (heartbeats or state changes), your status handler updates `ShuttleHealth.RecordSeen` with the current manager time. Ctrl+C signals the operator wants to stop; canceling `stopping` tells PeriodicTimers, async waits, and any cancellation-aware code to stop; awaiting tasks ensures they finish cleanup before process exits.

This placement keeps the M2 `move` demo intact and adds M5 serve behavior as a separate mode. That matches how ShuttleSim does it and how the lesson structures the work.

---

Stuck? These hints cover the key ideas without giving the full solution. The acceptance criteria in the lesson file are the authoritative spec.
