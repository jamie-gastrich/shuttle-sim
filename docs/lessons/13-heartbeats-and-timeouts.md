# Lesson 13: Heartbeats, lastSeenAt, and the timeout window

**Milestone:** M5 (Faults and recovery) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 12: The run loop, concurrency, and the gate-then-dispatcher sequence](12-run-loop-wiring.md) · **Decision:** [record 20](../decisions/20-liveness-heartbeats-and-timeouts.md); builds on [record 17](../decisions/17-occupancy-and-fault-reservations.md), [record 18](../decisions/18-deferred-dispatch-and-storage-policies.md), and [record 19](../decisions/19-run-loop-wiring-and-concurrency.md)

**Concepts this assignment requires:**

- [ ] **The heartbeat** — a regular "I am alive" signal from each shuttle. We reuse the existing `shuttleStatus` message; no new wire kind. Taught in [The heartbeat](#1-the-heartbeat). Fixed by [record 20 §2](../decisions/20-liveness-heartbeats-and-timeouts.md).
- [ ] **`lastSeenAt`** — the fleet manager's per-shuttle record of when it last heard from a shuttle, stamped with its own clock at receipt, and never moved backwards. Taught in [`lastSeenAt`](#2-lastseenat). Fixed by [record 20 §3](../decisions/20-liveness-heartbeats-and-timeouts.md).
- [ ] **Timeout detection** — the pure rule that turns a stale `lastSeenAt` into "unresponsive", with the window from configuration. Taught in [Timeout detection](#3-timeout-detection). Fixed by [record 20 §4](../decisions/20-liveness-heartbeats-and-timeouts.md).

Three new concepts. That is the cap.

Four things this assignment needs are **assignment requirements, not concepts**. They change where code lives and how it runs, not the ideas above:

- a pure `ShuttleHealth` class in `FleetManager` ([record 20 §5](../decisions/20-liveness-heartbeats-and-timeouts.md));
- a `HeartbeatPublisher` in `ShuttleSim` ([record 20 §5](../decisions/20-liveness-heartbeats-and-timeouts.md));
- two configuration keys, `Shuttle:HeartbeatIntervalMs` and `FleetManager:LivenessTimeoutMs` ([record 20 §4](../decisions/20-liveness-heartbeats-and-timeouts.md));
- a `lock` inside `ShuttleHealth`, because the status path writes while queries read ([record 20 §4](../decisions/20-liveness-heartbeats-and-timeouts.md)).

They are specified under [The assignment](#the-assignment).

**Not in this lesson:** alarms with severity, operator-acknowledged recovery, reassigning a failed shuttle's work, restart reconciliation, auto-release on timeout (all forbidden or later in M5), WMS orders (M6), the dashboard (M7), and any wire-contract change.

---

## Where this fits

M5 begins here. The spec's M5 line is: *"Heartbeats and timeouts. Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges."*

That is a big milestone. Record 17 Decision D already split it. M4 ended with an in-memory occupancy map and **no** time-based expiry. M5 adds, in order: heartbeats and timeouts (this lesson), then alarms, then recovery and reassignment. This lesson does the first slice only.

The gap is easy to state. Today the fleet manager can only react to messages that arrive. It has no idea what *silence* means. If a shuttle's link dies mid-task, the fleet manager waits forever and the lane behind that shuttle stays blocked (record 17 §4: a lost shuttle is an obstacle until recovery).

```text
     this lesson                                    earlier (built)
     ───────────                                    ───────────────
  shuttle process
     │  every HeartbeatIntervalMs
     ▼
  publish shuttleStatus ──────────────►  (existing status topic, family Status)
     │
     │  fleet manager receives ANY message from a shuttle
     ▼
  ShuttleHealth.RecordSeen(shuttleId, now)          lastSeenAt per shuttle
     │
     │  on demand, with the run loop's clock
     ▼
  ShuttleHealth.IsUnresponsive(shuttleId, now)  ──►  now - lastSeenAt > window
     │
     ▼  (M5, next lesson)
  raise an alarm with severity   ──►  operator acknowledges  ──►  recovery
```

M4's done-when is met. This lesson starts M5's done-when — *"every injected fault leads to the right alarm and the right recovery"* — by making "lost communication" *detectable*. The alarm itself is the next slice.

**The honest weak spot.** A heartbeat proves the heartbeat loop runs. It does not prove the whole shuttle is healthy. In the sim, timing is perfect, there is no wireless jitter, and messages do not actually drop. The toy below fakes a silence; a real deployment sees partial silences, late messages, and bursts. Say this plainly rather than overclaiming.

### No hard-coded values

AGENTS.md is flat: *"No hard-coded names, topics, timeouts, retry counts, or sizes. Use configuration."* The heartbeat interval comes from `Shuttle:HeartbeatIntervalMs` and the liveness window from `FleetManager:LivenessTimeoutMs`. `ShuttleHealth` takes the window in its constructor; `HeartbeatPublisher` takes the interval in its constructor. The classes hold no literals. Test values are test fixtures and stay in the tests.

---

## Verification table

Everything below was checked by a command or a file read on **2026-10-13**. The baseline suite ran against a clean copy of `src/` at `/tmp/opencode/l13base` (no `bin/` or `obj/` copied in). The toy ran at `/tmp/opencode/l13toy`. Both scratch directories were deleted afterwards. No file under `src/` was changed.

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green with no DB | `/tmp/opencode/l13base`: `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; **84** tests; `FleetManager.Tests` = **16** |
| No liveness code exists yet | `grep -rniE 'heartbeat\|lastSeen\|health\|liveness' src --include='*.cs' --include='*.json'` | no matches |
| The shuttle publishes status only on a move | read `src/ShuttleSim/ShuttleEndpoint.cs` | `PublishStatusAsync` at `:75` (`moving`) and `:79` (`idle`); nowhere else |
| The contract has no heartbeat/alarm/recovery kind | read `docs/contract/v2.md` | three kinds: `moveCommand`, `commandAck`, `shuttleStatus`; `Version: 2` |
| `ShuttleStatus.Timestamp` exists | read `docs/contract/v2.md` | `Timestamp` field, ISO 8601 UTC |
| MQTTnet offers will + keep-alive | read `~/.nuget/packages/mqttnet/5.2.0.1603/lib/net10.0/MQTTnet.xml` | `MqttClientOptions.WillTopic`, `WillDelayInterval`, `KeepAlivePeriod` present |
| The fleet manager has no long-running status path | read `src/FleetManager/Program.cs` | status handled only inside `RunMoveAsync`, for one move round trip |
| The toy shows `lastSeenAt` and the timeout window | `/tmp/opencode/l13toy`, `dotnet run` | `SH-02` silent `3.0s` → `ok`, `4.0s` → `UNRESPONSIVE` at `00:00:06`; exit 0 |
| Contract is still v2 | `grep -qE '^Version: 2$' docs/contract/v2.md` | match; no `v3.md` |

Not yet verified: `ShuttleHealth`, `HeartbeatPublisher`, the two config keys, and the new tests — this assignment specifies them and the reviewer measures them.

---

## Options survey

The spec's learning method asks for up to three options per real choice, compared, before a decision record picks one. There are two real choices in this slice.

### Survey 1: How does a shuttle prove it is alive? (→ [record 20 §2](../decisions/20-liveness-heartbeats-and-timeouts.md))

| | A: periodic `shuttleStatus` (chosen) | B: a dedicated `heartbeat` kind | C: broker keep-alive + Last Will |
|---|---|---|---|
| New wire kind and version bump | **no** — stays v2 | **yes** — a fifth kind, `v3.md` | no |
| Carries state and position for free | **yes** | **no** — an empty ping | no |
| Detects a hung shuttle process | yes | yes | **no** — only a dropped connection |
| Extra work | small | a new kind, parser, topic, tests | will config + broker behavior to verify |

**Decision 1: the heartbeat is a periodic `shuttleStatus`.** The status already carries `State`, `Position`, `Timestamp`, and `FaultReason`. A missed heartbeat is also a missed state update. The contract stays v2 and keeps its three kinds. Rejected: a dedicated `heartbeat` kind (extra surface and a version bump for something status already carries); broker keep-alive + Last Will (the wrong layer — it reports a lost connection, not a stuck process, and needs will/retained configuration). The installed MQTTnet 5.2.0.1603 does expose `WillTopic` and `KeepAlivePeriod`; option C is available, just not right here.

### Survey 2: Whose clock stamps `lastSeenAt`? (→ [record 20 §3](../decisions/20-liveness-heartbeats-and-timeouts.md))

| | A: the manager's clock at receipt (chosen) | B: the shuttle's `Timestamp` | C: the later of the two |
|---|---|---|---|
| Immune to shuttle clock skew | **yes** | **no** | partly |
| Answers "have *I* heard from it?" | **yes** | no | close |
| Simple to explain and test | **yes** | yes | less so |

**Decision 2: stamp `lastSeenAt` with the fleet manager's clock at receipt.** The question liveness answers is about the fleet manager's own clock. The shuttle's `Timestamp` stays for order and diagnostics. Rejected: the shuttle's `Timestamp` (a skewed clock distorts the window); the later of the two (no clear winner and harder to explain).

**What counts as liveness.** Any inbound message from a shuttle updates `lastSeenAt` — a status, a heartbeat status, or an ack. If we hear from the shuttle at all, it is alive.

---

## The lecture

Today we are going to learn about three things: **the heartbeat**, **`lastSeenAt`**, and **timeout detection**.

### 1) The heartbeat

A **heartbeat** is a small, regular signal that says "I am still here."

Real-world picture: a night guard walks a building and radios in every hour. If the hourly radio call does not come, the dispatcher knows something is wrong, even though nobody reported a problem. The radio call carries no news. Its *absence* is the message.

Why do we need this now? Because the fleet manager can only react to messages that arrive. A shuttle that is moving sends a `moving` status and later an `idle` status. A shuttle that is idle and has no work sends nothing. And a shuttle whose link dies sends nothing too. From the fleet manager's side, "doing nothing" and "dead" look the same. Silence has no meaning until we define a rhythm.

The fix: the shuttle publishes its current status on a fixed interval, even when nothing changed.

Here is the shape of the publisher. You write the body.

```csharp
// src/ShuttleSim/HeartbeatPublisher.cs — shape only; you write the body
public sealed class HeartbeatPublisher
{
    private readonly ShuttleEndpoint _endpoint;
    private readonly TimeSpan _interval;

    public HeartbeatPublisher(ShuttleEndpoint endpoint, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _interval = interval;
    }

    public async Task RunAsync(Func<DateTime> clock, CancellationToken cancellationToken)
    {
        // every _interval, publish the shuttle's current status
    }
}
```

Line by line:

- `_interval` is the cadence. It comes from `Shuttle:HeartbeatIntervalMs`. The constructor rejects a non-positive interval, the same way `RetryPolicy` does.
- `RunAsync` takes a `clock`. That is a function that returns "now". Passing the clock in keeps the class easy to test and keeps `DateTime.UtcNow` out of the loop body.
- `RunAsync` takes a `CancellationToken`. The heartbeat must stop when the shuttle stops. No `Thread.Sleep`, no `async void`.
- The body waits for each interval and calls the endpoint's publish method. The wait uses a `PeriodicTimer`, which does not drift and does not busy-wait.

The endpoint already has a private method that builds and publishes a status. This lesson makes a public one that publishes the **current** state. That is the heartbeat. It uses the existing `shuttleStatus` kind and the existing `shuttle/{id}/status` topic. Nothing on the wire changes.

Where this appears later: the next M5 lesson wires the fleet-side status path that receives these messages and calls `RecordSeen`. M7's dashboard will show the heartbeat as "last seen" per shuttle.

### 2) `lastSeenAt`

**`lastSeenAt`** is the last time the fleet manager heard from a shuttle.

Real-world picture: a forklift dispatcher writes on a clipboard the time of each forklift's last call. At any moment, the dispatcher can glance down and see "SH-02? Last call 4 minutes ago." That timestamp is `lastSeenAt`.

Two rules make it trustworthy.

**Rule 1: use the manager's clock.** The shuttle stamps its own `Timestamp` on every message. Do not use it for liveness. Shuttle clocks drift. The manager's clock is the one that answers "have *I* heard from it?".

**Rule 2: never move backwards.** `RecordSeen` only moves `lastSeenAt` forward. If an older message arrives late, or a duplicate arrives, ignore its time. MQTT "at least once" delivery can repeat a message, and a retained status can replay an old one. A backwards jump would make a live shuttle look dead. The existing code already uses this idea: `FleetTask` and `Shuttle` both have an `EnsureInOrder` guard.

Here is the shape of the tracker. You write the body.

```csharp
// src/FleetManager/ShuttleHealth.cs — shape only; you write the body
public sealed class ShuttleHealth
{
    private readonly TimeSpan _livenessTimeout;
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _lastSeenAt = new(StringComparer.Ordinal);

    public ShuttleHealth(TimeSpan livenessTimeout)
    {
        // reject a non-positive window
    }

    public void RecordSeen(string shuttleId, DateTime seenAt)
    {
        lock (_lock)
        {
            // keep the newest stamp; ignore an older one
        }
    }

    public DateTime? LastSeenAt(string shuttleId)
    {
        lock (_lock)
        {
            // return the stored stamp, or null if we have never heard from it
        }
    }
}
```

Line by line:

- `_livenessTimeout` is the window. It comes from `FleetManager:LivenessTimeoutMs`. The constructor rejects a non-positive window.
- `_lock` guards the dictionary. The status path writes `RecordSeen`; the run loop or an alarm check reads. Two threads at once would corrupt a plain `Dictionary` (record 19 §3). The lock makes the whole read-modify-write in `RecordSeen` one atomic step, so the "is this newer?" check and the store cannot be split.
- `_lastSeenAt` maps a shuttle id to a time. `StringComparer.Ordinal` is used everywhere in this codebase for ids.
- `RecordSeen` stores the stamp only if it is newer than the one already there.
- `LastSeenAt` is a read for the dashboard and for a later alarm record.

**Why one timestamp and not a flag.** We could store a boolean `IsUnresponsive`. Do not. A flag has to be cleared by a second code path, and a late heartbeat must clear it instantly. A timestamp cannot go stale: it is always compared against `now`.

Where this appears later: the next M5 lesson calls `RecordSeen` from the MQTT status handler. The alarm record will read `LastSeenAt`. Record 18's closest-eligible dispatch will treat an unresponsive shuttle as not eligible.

### 3) Timeout detection

**Timeout detection** is the rule that reads `lastSeenAt` and decides whether a shuttle is lost.

Real-world picture: a traffic light controller. It expects a heartbeat from each detector every second. If it has not heard from one for three seconds, it assumes the detector is dead and fails safe. It does not wait for the detector to say "I am dead" — the detector cannot say that.

The rule is small:

```text
unresponsive  =  never seen  OR  (now - lastSeenAt) > window
```

Two details matter.

**Detail 1: the window comes from configuration.** `ShuttleHealth` is built with the window. No literal lives inside it. Different warehouses tolerate different silence; the value is config, not code.

**Detail 2: "exactly the window" is still responsive.** The comparison is strictly greater than. If the heartbeat interval is 1 second and the window is 3 seconds, a shuttle silent for exactly 3 seconds has missed 3 beats and is still called responsive; it is called lost only once it is *past* 3 seconds. We pin this boundary with a test so nobody has to guess.

Here is the query shape. You write the body.

```csharp
// src/FleetManager/ShuttleHealth.cs — shape only; you write the body
public bool IsUnresponsive(string shuttleId, DateTime now)
{
    lock (_lock)
    {
        // never seen -> true
        // else: now - lastSeenAt > _livenessTimeout
    }
}

public IReadOnlyCollection<string> UnresponsiveShuttles(DateTime now)
{
    lock (_lock)
    {
        // every shuttle whose stamp is stale at `now`
    }
}
```

Line by line:

- `IsUnresponsive` takes `now` as a parameter. It never calls `DateTime.UtcNow`. That keeps it pure and lets a test drive any clock. The run loop already works this way: it carries `_currentTime` and passes it to every check.
- `UnresponsiveShuttles` is the list view the alarm slice will use. It returns a snapshot, so the caller can act without holding the lock.
- Both methods only read. The only write is `RecordSeen`.

**Why derive it instead of store it.** A derived answer is always consistent with the timestamp. There is no flag to forget to clear. When a late heartbeat arrives, the next `IsUnresponsive` call already sees the new timestamp and returns `false`.

Where this appears later: the next M5 lesson raises an alarm when `IsUnresponsive` turns true. Record 17 §4 already forbids releasing a lost shuttle's lanes just because the window passed; the alarm is knowledge, not action.

### Recap in plain words

- **The heartbeat** is a regular "I am alive" signal. We reuse `shuttleStatus`; there is no new message kind.
- **`lastSeenAt`** is when the manager last heard from a shuttle. Stamp it with the manager's clock at receipt, and never move it backwards.
- **Timeout detection** is `never seen OR now - lastSeenAt > window`. The window comes from config, and "exactly the window" is still responsive. Derive the verdict; do not store it.

---

## Worked example (toy, not the assignment)

This toy shows the key idea: heartbeats arrive, `lastSeenAt` advances, and a silence past the window flips a shuttle to `UNRESPONSIVE`. It also shows the boundary — silent for exactly the window is still `ok`. It is a toy. It does **not** use the real `ShuttleHealth` (you build that), it has no MQTT, and it is **not** the assignment. Do not copy it into `src/`.

Everything below ran in `/tmp/opencode/l13toy` and was deleted afterwards. It is one console project. `L13Toy.csproj` (complete):

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

`Program.cs` (complete):

```csharp
// Worked-example toy for lesson 13: liveness, lastSeenAt, and the timeout window.
// This is a TOY, clearly not the assignment. It does not touch anything under src/.

// ── Toy health table: shuttleId -> the last time the manager heard from it ──
var lastSeenAt = new Dictionary<string, DateTime>(StringComparer.Ordinal);

// ── Config: a heartbeat every second, and a 3-second silence window ──
var heartbeatInterval = TimeSpan.FromSeconds(1);
var livenessTimeout = TimeSpan.FromSeconds(3);

// SH-01 keeps talking. SH-02 goes silent after its 3rd heartbeat. SH-03 never talks.
var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
var silentAfter = 3;
var beats = new Dictionary<string, int>(StringComparer.Ordinal)
{
    ["SH-01"] = 0,
    ["SH-02"] = 0,
};

Console.WriteLine("=== Liveness: lastSeenAt and the timeout window ===");
Console.WriteLine($"heartbeat={heartbeatInterval.TotalSeconds}s  timeout={livenessTimeout.TotalSeconds}s");
Console.WriteLine();

for (var tick = 0; tick < 8; tick++)
{
    // A heartbeat arrives. Any message from a shuttle proves it is alive.
    foreach (var shuttleId in new[] { "SH-01", "SH-02" })
    {
        beats[shuttleId]++;
        if (shuttleId == "SH-02" && beats[shuttleId] > silentAfter)
        {
            Console.WriteLine($"{clock:HH:mm:ss}  {shuttleId}  (no heartbeat)");
            continue;
        }

        lastSeenAt[shuttleId] = clock; // stamp with the manager's clock, at receipt
        Console.WriteLine($"{clock:HH:mm:ss}  {shuttleId}  heartbeat -> lastSeenAt={clock:HH:mm:ss}");
    }

    // The timeout check, on the manager's own clock.
    foreach (var shuttleId in new[] { "SH-01", "SH-02", "SH-03" })
    {
        if (!lastSeenAt.TryGetValue(shuttleId, out var seen))
        {
            Console.WriteLine($"          {shuttleId}  never seen -> UNRESPONSIVE");
            continue;
        }

        var silentFor = clock - seen;
        var unresponsive = silentFor > livenessTimeout;
        Console.WriteLine(
            $"          {shuttleId}  silent {silentFor.TotalSeconds:0.0}s -> {(unresponsive ? "UNRESPONSIVE" : "ok")}");
    }

    Console.WriteLine();
    clock += heartbeatInterval;
}
```

Run it:

```bash
cd /tmp/opencode/l13toy
dotnet run
```

Observed output (exit 0):

```text
=== Liveness: lastSeenAt and the timeout window ===
heartbeat=1s  timeout=3s

00:00:00  SH-01  heartbeat -> lastSeenAt=00:00:00
00:00:00  SH-02  heartbeat -> lastSeenAt=00:00:00
          SH-01  silent 0.0s -> ok
          SH-02  silent 0.0s -> ok
          SH-03  never seen -> UNRESPONSIVE

00:00:01  SH-01  heartbeat -> lastSeenAt=00:00:01
00:00:01  SH-02  heartbeat -> lastSeenAt=00:00:01
          SH-01  silent 0.0s -> ok
          SH-02  silent 0.0s -> ok
          SH-03  never seen -> UNRESPONSIVE

00:00:02  SH-01  heartbeat -> lastSeenAt=00:00:02
00:00:02  SH-02  heartbeat -> lastSeenAt=00:00:02
          SH-01  silent 0.0s -> ok
          SH-02  silent 0.0s -> ok
          SH-03  never seen -> UNRESPONSIVE

00:00:03  SH-01  heartbeat -> lastSeenAt=00:00:03
00:00:03  SH-02  (no heartbeat)
          SH-01  silent 0.0s -> ok
          SH-02  silent 1.0s -> ok
          SH-03  never seen -> UNRESPONSIVE

00:00:04  SH-01  heartbeat -> lastSeenAt=00:00:04
00:00:04  SH-02  (no heartbeat)
          SH-01  silent 0.0s -> ok
          SH-02  silent 2.0s -> ok
          SH-03  never seen -> UNRESPONSIVE

00:00:05  SH-01  heartbeat -> lastSeenAt=00:00:05
00:00:05  SH-02  (no heartbeat)
          SH-01  silent 0.0s -> ok
          SH-02  silent 3.0s -> ok
          SH-03  never seen -> UNRESPONSIVE

00:00:06  SH-01  heartbeat -> lastSeenAt=00:00:06
00:00:06  SH-02  (no heartbeat)
          SH-01  silent 0.0s -> ok
          SH-02  silent 4.0s -> UNRESPONSIVE
          SH-03  never seen -> UNRESPONSIVE

00:00:07  SH-01  heartbeat -> lastSeenAt=00:00:07
00:00:07  SH-02  (no heartbeat)
          SH-01  silent 0.0s -> ok
          SH-02  silent 5.0s -> UNRESPONSIVE
          SH-03  never seen -> UNRESPONSIVE
```

Read the output.

- `SH-02 heartbeat -> lastSeenAt=00:00:02` — a heartbeat arrived. `lastSeenAt` moves to the manager's clock, `00:00:02`.
- `SH-02 silent 3.0s -> ok` at `00:00:05` — silent for exactly the window is still responsive. This is the boundary.
- `SH-02 silent 4.0s -> UNRESPONSIVE` at `00:00:06` — past the window. The heartbeat was missed 4 seconds ago, so the shuttle is called lost.
- `SH-03 never seen -> UNRESPONSIVE` — a shuttle we have never heard from is unresponsive from the start. That is the "never seen" branch.

Your assignment is this toy with the real names and the real plumbing: a pure `ShuttleHealth` in `FleetManager`, a `HeartbeatPublisher` in `ShuttleSim`, the two config keys, and the tests. The alarm and the recovery are the next lessons.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. Why is "doing nothing" and "dead" the same thing to the fleet manager before this lesson? What does a heartbeat add?
2. Why is the heartbeat a periodic `shuttleStatus` and not a new `heartbeat` message kind? What does each choice cost?
3. Why does `lastSeenAt` use the manager's clock and not the shuttle's `Timestamp`? What breaks if you use the shuttle's clock?
4. Why must `RecordSeen` never move `lastSeenAt` backwards? Name two ways an older message can arrive.
5. A shuttle has been silent for exactly the timeout window. Is it unresponsive? Why did we choose that boundary, and how is it pinned?

---

## The assignment

Build M5's first slice: heartbeats and timeouts. A shuttle publishes a periodic status. A pure fleet-side tracker records `lastSeenAt` and answers "is this shuttle unresponsive?". No alarms yet.

### Requirements

1. **Add the pure tracker: `src/FleetManager/ShuttleHealth.cs`.**
   - `public sealed class ShuttleHealth` with constructor `ShuttleHealth(TimeSpan livenessTimeout)`.
   - The constructor throws `ArgumentOutOfRangeException` when `livenessTimeout <= TimeSpan.Zero`.
   - `public void RecordSeen(string shuttleId, DateTime seenAt)` — records `seenAt` for the shuttle. It stores the stamp only if the shuttle has no stamp yet or `seenAt` is newer than the stored one. An older or equal stamp leaves the stored value unchanged.
   - `public DateTime? LastSeenAt(string shuttleId)` — the stored stamp, or `null` when the shuttle has never been seen.
   - `public bool IsUnresponsive(string shuttleId, DateTime now)` — `true` when the shuttle has never been seen, or when `now - lastSeenAt > _livenessTimeout`. "Exactly the window" returns `false`.
   - `public IReadOnlyCollection<string> UnresponsiveShuttles(DateTime now)` — a snapshot list of every known shuttle that is unresponsive at `now`.
   - Guard the dictionary with `private readonly object _lock = new();` and `lock (_lock)` in every public method. The read-modify-write in `RecordSeen` is one atomic step.
   - Validate a blank `shuttleId` (throw `ArgumentException`), like `OccupancyMap` does.
   - The file must contain **no** `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, `Npgsql`, `MQTTnet`, `Messaging`, `Contracts`, or `Postgres`. It stays pure: no clock, no ids, no I/O.
   - It must contain no hard-coded lane ids or intervals.

2. **Add the heartbeat publisher: `src/ShuttleSim/HeartbeatPublisher.cs`.**
   - `public sealed class HeartbeatPublisher` with constructor `HeartbeatPublisher(ShuttleEndpoint endpoint, TimeSpan interval)`.
   - Both arguments are validated: `endpoint` is not null; `interval > TimeSpan.Zero`.
   - `public async Task RunAsync(Func<DateTime> clock, CancellationToken cancellationToken)` — waits `interval` between ticks using a `PeriodicTimer`, and each tick publishes the shuttle's current status through the endpoint, stamped with `clock()`.
   - It must not use `Thread.Sleep`, `.Wait()`, `.Result`, or `async void`.
   - Same namespace convention as `ShuttleEndpoint` in that project (no namespace declaration).

3. **Let the endpoint publish a heartbeat: `src/ShuttleSim/ShuttleEndpoint.cs`.**
   - Add `public Task PublishStatusAsync(DateTime sentAt, CancellationToken cancellationToken)` that publishes the shuttle's **current** state, position, and fault reason as a `shuttleStatus` message on the existing `shuttle/{id}/status` topic with `MessageFamily.Status`.
   - Keep the existing private status-publishing path used by `HandleCommandAsync` (`moving`, then `idle`) working exactly as it does today. Do not change its behavior.
   - No new message kind. The heartbeat reuses `shuttleStatus`.

4. **Wire the heartbeat into the shuttle's serve loop: `src/ShuttleSim/Program.cs`.**
   - Read `Shuttle:HeartbeatIntervalMs` from configuration and validate it is positive.
   - Start a `HeartbeatPublisher` with `TimeSpan.FromMilliseconds(heartbeatIntervalMs)` and `() => DateTime.UtcNow`.
   - Run it with a `CancellationToken` that is cancelled when the process stops (Ctrl+C), and stop the publisher cleanly before returning. No process may be left running.

5. **Configuration, no literals.**
   - `src/ShuttleSim/appsettings.json`: add `"HeartbeatIntervalMs"` under `Shuttle` (a positive integer, e.g. `1000`).
   - `src/FleetManager/appsettings.json`: add `"LivenessTimeoutMs"` under `FleetManager` (a positive integer, e.g. `3000`). It is read when the fleet-side status path is wired in the next lesson; adding the key now keeps the timeout out of code.

6. **Add the pure tracker tests: `src/FleetManager.Tests/ShuttleHealthTests.cs`.** Five tests, with these exact names:
   - `A_recent_heartbeat_keeps_a_shuttle_responsive`
   - `Silence_past_the_timeout_marks_a_shuttle_unresponsive`
   - `A_shuttle_silent_for_exactly_the_timeout_is_still_responsive`
   - `A_new_heartbeat_makes_a_silent_shuttle_responsive_again` (also record an older stamp after a newer one, and assert `LastSeenAt` did not move backwards)
   - `A_shuttle_never_seen_is_unresponsive`
   - These tests are **pure**: no `Integration` trait, no database, no broker.

7. **Add the heartbeat test: `src/ShuttleSim.Tests/ShuttleEndpointTests.cs`.** One test with this exact name:
   - `Heartbeat_publishes_the_current_status_without_a_command`
   - Build an endpoint with the existing `FakeBus`, call the new public publish method, and assert one `Status` message is published with the shuttle's current state and position. It is **pure**: no broker, no database.

8. **Keep the contract at v2.** No new message kinds, no `docs/contract/v3.md`, no version bump.

9. **Do not touch these:** `src/FleetManager/TaskDispatcher.cs`, `src/FleetManager/Program.cs`, `src/TrafficCore/`, `src/ShuttleCore/`, `src/Contracts/`, `docs/contract/`, `docker/Dockerfile.build`. `FleetManager/Program.cs` still has no run loop wiring.

10. **M5 later items are not present.** No alarms, no severity, no operator acknowledgement, no recovery, no reassignment, no `Release` on timeout, no restart reconciliation. `ShuttleHealth` is in-memory only.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are asserted, not admired. Read every line's purpose before you run it.

```bash
# Phase 1 — the new pieces exist, with the pinned names
test -f src/FleetManager/ShuttleHealth.cs || echo "missing ShuttleHealth.cs"
test -f src/ShuttleSim/HeartbeatPublisher.cs || echo "missing HeartbeatPublisher.cs"
test -f src/FleetManager.Tests/ShuttleHealthTests.cs || echo "missing ShuttleHealthTests.cs"
grep -qE 'class ShuttleHealth' src/FleetManager/ShuttleHealth.cs
grep -qE 'class HeartbeatPublisher' src/ShuttleSim/HeartbeatPublisher.cs
for m in RecordSeen LastSeenAt IsUnresponsive UnresponsiveShuttles; do grep -qE "\b$m\b" src/FleetManager/ShuttleHealth.cs || echo "missing ShuttleHealth member: $m"; done
grep -qE 'PublishStatusAsync' src/ShuttleSim/ShuttleEndpoint.cs
```
```bash
# Phase 2 — purity: no clock, no ids, no I/O types in the new pure file
grep -rnE 'DateTime\.UtcNow|Guid\.NewGuid|Task\.Delay|Thread\.Sleep' src/FleetManager/ShuttleHealth.cs   # nothing
grep -rniE 'Npgsql|MQTTnet|Messaging|Contracts|Postgres' src/FleetManager/ShuttleHealth.cs              # nothing
grep -rnE '"A[0-9]{2}-L[0-9]{2}' src/FleetManager/ShuttleHealth.cs                                      # nothing
```
```bash
# Phase 3 — the lock is really there
grep -qE 'private readonly object _lock' src/FleetManager/ShuttleHealth.cs
grep -qE 'lock *\(' src/FleetManager/ShuttleHealth.cs || echo "FAIL: ShuttleHealth must guard shared state"
```
```bash
# Phase 4 — the heartbeat is config-driven, timed, and cancellable
grep -qE 'HeartbeatIntervalMs' src/ShuttleSim/appsettings.json
grep -qE 'HeartbeatIntervalMs' src/ShuttleSim/Program.cs
grep -qE 'LivenessTimeoutMs' src/FleetManager/appsettings.json
grep -qE 'PeriodicTimer' src/ShuttleSim/HeartbeatPublisher.cs || echo "FAIL: HeartbeatPublisher must use PeriodicTimer"
grep -qE 'CancellationToken' src/ShuttleSim/HeartbeatPublisher.cs
grep -rnE 'Thread\.Sleep|\.Result|\.Wait\(\)|async void' src/ShuttleSim/HeartbeatPublisher.cs          # nothing
```
```bash
# Phase 5 — build, and the pure suite with NO database needed (the container's command).
# The counts are gated, not admired.
cd src
dotnet build
T=$(mktemp); dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration" > "$T" 2>&1; RC=$?
echo "---RC:$RC---"; grep -E 'Passed!|Failed!' "$T"
P=$(grep -c 'Passed!' "$T"); F=$(grep -c 'Failed!' "$T")
TOTAL=$(grep -oE 'Passed: +[0-9]+' "$T" | grep -oE '[0-9]+' | awk '{s+=$1} END{print s}')
echo "assemblies-passed=$P assemblies-failed=$F total=$TOTAL"
[ "$RC" -eq 0 ] && [ "$P" -eq 8 ] && [ "$F" -eq 0 ] && [ "$TOTAL" -eq 90 ] \
  && echo "suite gate OK" || echo "FAIL: expected RC 0, 8 Passed!, 0 Failed!, 90 tests"
grep -E 'Passed: +21,.*FleetManager\.Tests\.dll' "$T" && echo "FleetManager.Tests=21 OK" || echo "FAIL: FleetManager.Tests should show 21"
grep -E 'Passed: +6,.*ShuttleSim\.Tests\.dll' "$T" && echo "ShuttleSim.Tests=6 OK" || echo "FAIL: ShuttleSim.Tests should show 6"
rm -f "$T"

# Phase 5b — the new tests are really there, by anchored name
L=$(mktemp); dotnet test ShuttleFleet.slnx --list-tests > "$L" 2>&1
for t in A_recent_heartbeat_keeps_a_shuttle_responsive Silence_past_the_timeout_marks_a_shuttle_unresponsive A_shuttle_silent_for_exactly_the_timeout_is_still_responsive A_new_heartbeat_makes_a_silent_shuttle_responsive_again A_shuttle_never_seen_is_unresponsive Heartbeat_publishes_the_current_status_without_a_command; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                                            # nothing
rm -f "$L"
cd ..
```
```bash
# Phase 6 — no test launches docker
grep -rniE 'docker' src/*.Tests --include='*.cs' && echo "FAIL: docker referenced in a test" || echo "OK: no docker in tests"
```
```bash
# Phase 7 — protected files unchanged, contract still v2
git diff --quiet HEAD -- src/FleetManager/TaskDispatcher.cs src/FleetManager/Program.cs src/TrafficCore src/ShuttleCore src/Contracts docs/contract docker/Dockerfile.build \
  && echo "OK: dispatcher, FleetManager Program, core, contract and Dockerfile unchanged" \
  || echo "FAIL: a protected file changed"
grep -qE '^Version: 2$' docs/contract/v2.md
test ! -f docs/contract/v3.md
for k in moveCommand commandAck shuttleStatus; do grep -qE "\`$k\`" docs/contract/v2.md || echo "missing contract kind: $k"; done
```

**Proof details.** The assignment adds two product files (`ShuttleHealth.cs`, `HeartbeatPublisher.cs`), modifies two product files (`ShuttleEndpoint.cs` gains one public method; `ShuttleSim/Program.cs` starts and stops the heartbeat), edits two `appsettings.json` files, and adds one test file plus one test. The pure test total goes from **84 to 90**: `FleetManager.Tests` gains five tests (16 → 21), `ShuttleSim.Tests` gains one (5 → 6). The gate is `RC 0`, 8 `Passed!`, 0 `Failed!`, 90 total.

What the reviewer will mutate to prove the tests are load-bearing:
- remove the monotonic guard in `RecordSeen` → `A_new_heartbeat_makes_a_silent_shuttle_responsive_again` goes red;
- change `>` to `>=` in the timeout comparison → `A_shuttle_silent_for_exactly_the_timeout_is_still_responsive` goes red;
- remove the timeout comparison entirely (always responsive) → `Silence_past_the_timeout_marks_a_shuttle_unresponsive` and `A_shuttle_never_seen_is_unresponsive` go red;
- make `PublishStatusAsync` hard-code `idle` → `Heartbeat_publishes_the_current_status_without_a_command` goes red when the shuttle is not idle.

### Stretch goal

Prove the thread-safety claim with a test: start two `Task`s in a loop, one calling `RecordSeen` and one calling `IsUnresponsive`, and assert no exception is thrown and the final `LastSeenAt` is the newest stamp. Comment that this is the status path's safety guarantee for the next lesson.

### Not in this assignment

- **Alarms with severity** — the next M5 lesson. This lesson only makes "lost" detectable.
- **Operator-acknowledged recovery and reassignment** — later M5 lessons.
- **Wiring the fleet-side status path** that calls `RecordSeen` — the next M5 lesson. Today `ShuttleHealth` is proven by unit tests.
- **Restart reconciliation** — M5 later; `ShuttleHealth` is in-memory only.
- **Auto-release on timeout** — forbidden (record 17 §4). The window passing is knowledge, not action.
- **WMS orders** — M6. **Dashboard** — M7.
- **A contract version bump.** The heartbeat reuses `shuttleStatus`. The contract stays v2.

---

## Closing

Read the spec's M5 line once more: *"Heartbeats and timeouts. Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges."*

This lesson makes "lost communication" **detectable**. The shuttle publishes a periodic status (the heartbeat). The fleet manager records `lastSeenAt` with its own clock and never lets it slip backwards. A pure rule turns a stale timestamp into "unresponsive", with the window from configuration. No alarm yet, no recovery yet — those are the next slices of M5.

Before you call this done, be ready to explain without notes:

- why "doing nothing" and "dead" look the same until a heartbeat gives silence a meaning;
- why the heartbeat reuses `shuttleStatus` instead of adding a message kind;
- why `lastSeenAt` uses the manager's clock and must never move backwards;
- why "exactly the window" is still responsive, and how the test pins it;
- why `ShuttleHealth` stores one timestamp instead of a boolean flag.

When the reviewer passes this, M5 has its first slice: heartbeats and timeouts are built, pure, tested, and wired into the shuttle, and the fleet manager can tell a quiet shuttle from a lost one. The alarm that acts on that knowledge is next.
