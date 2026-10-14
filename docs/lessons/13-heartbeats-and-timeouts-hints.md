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

## Addendum — ShuttleSim serve-loop shutdown (owner question)

**For lesson 13 (heartbeats and timeouts).** The owner asked where to put the "wait for Ctrl+C, then `stopping.Cancel(); await heartbeatTask;`" step, and what it does. The serve loop in this lesson is the **shuttle simulator**: `src/ShuttleSim/Program.cs`, in the method `RunServerAsync()`. It is **not** `src/FleetManager/Program.cs`. The fleet manager is untouched by this assignment; its own serve loop is a later lesson.

### Where it goes in `RunServerAsync()`

The method already does all of this, in order:

- `:79` reads `Shuttle:HeartbeatIntervalMs`; `:102-103` throws when it is not positive.
- `:104` `using var stopping = new CancellationTokenSource();` — the shutdown token.
- `:105-106` builds `HeartbeatPublisher` and starts the background task:
  `var heartbeatTask = heartbeat.RunAsync(() => DateTime.UtcNow, stopping.Token);`
- `:108` prints "Shuttle … listening on …".
- `:109` `var stopped = new TaskCompletionSource(...);`
- `:110-115` hooks `Console.CancelKeyPress`. On Ctrl+C it sets `eventArgs.Cancel = true` and calls `stopped.TrySetResult()`.
- `:116-123` `try { await stopped.Task; } finally { Console.CancelKeyPress -= cancelHandler; }` — this is the "wait for Ctrl+C" wait.

The two missing lines belong **after** that `try/finally` (after its closing brace at `:123`) and **before** `return 0;` at `:125`. The tail of `RunServerAsync()` should look like this:

```csharp
    try
    {
        await stopped.Task;
    }
    finally
    {
        Console.CancelKeyPress -= cancelHandler;
    }

    stopping.Cancel();
    await heartbeatTask;

    return 0;
}
```

### What the two lines do

`stopping.Cancel()` — **raise the signal.** The token created at `:104` is the same token passed into `RunAsync` at `:106`. `Cancel()` flips that token to "cancelled". The heartbeat loop is parked inside `PeriodicTimer.WaitForNextTickAsync(stopping.Token)` (`src/ShuttleSim/HeartbeatPublisher.cs:17`). Cancelling the token ends that wait, so the loop stops asking for the next tick. Picture a night guard on a radio schedule: you tell the guard "shift's over, stop calling in" instead of grabbing the radio out of their hand.

`await heartbeatTask` — **wait for it to actually finish.** `Cancel()` only raises a flag. It does not prove the loop has left. `heartbeatTask` is the `Task` returned by `RunAsync`. Awaiting it waits until `RunAsync` has returned. Without this line, `RunServerAsync` would `return 0` while the publisher task is still in flight, and `dotnet run` would tear the process down around it. With it, shutdown is ordered: signal, then wait, then return. Picture a forklift: tell it to stop, wait until it is actually parked, then shut the gate. Do not shut the gate while it is still moving.

**Expect `OperationCanceledException` at this await.** `PeriodicTimer.WaitForNextTickAsync(token)` reports cancellation by *throwing* `OperationCanceledException`. `HeartbeatPublisher.RunAsync` does not catch it, so a bare `await heartbeatTask;` after `Cancel()` will throw. That is the normal way this loop ends — the heartbeat saying "I stopped because you told me to". I confirmed this in a scratch probe: `await` on a `RunAsync` built from `PeriodicTimer` + a cancelled token threw `OperationCanceledException`. To let Ctrl+C exit cleanly with code 0, catch exactly that case:

```csharp
    stopping.Cancel();
    try
    {
        await heartbeatTask;
    }
    catch (OperationCanceledException) when (stopping.IsCancellationRequested)
    {
        // Expected: the heartbeat loop stopped because we cancelled it.
    }

    return 0;
```

The `when (stopping.IsCancellationRequested)` filter is important: it catches the shutdown you caused and lets a real, unexpected fault still surface.

### Why `using var stopping` matters

`using var stopping` at `:104` means the `CancellationTokenSource` is **disposed** when `RunServerAsync` returns (the end of the method's scope). Two different jobs are in play:

- `Cancel()` **signals** the waiters (the heartbeat loop). It is what actually tells the work to stop.
- `Dispose()` (from `using`) **releases** the source's internal resources — its callback registrations and timer handle — instead of leaving them for the garbage collector.

They are not substitutes. Drop the `using` and the source still works, but its cleanup time is unpredictable. Drop the `Cancel()` and the `using` disposes a source whose signal was never raised, so the loop is never told to stop. You need both, and `using var` orders them the right way: `Cancel()` runs first, `await heartbeatTask` drains the loop, and `Dispose()` happens as the method exits. The `using var` lives for the whole method, so it disposes **after** the await, never before it.

**Do not leave a process running.** With these two lines in place, Ctrl+C stops the publisher, waits for it, and only then returns. That is what the lesson's "no process may be left running" requirement means at the code level.

---

Stuck? These hints cover the key ideas without giving the full solution. The acceptance criteria in the lesson file are the authoritative spec.
