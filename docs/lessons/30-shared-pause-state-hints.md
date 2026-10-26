# Lesson 30 hints

## Tier 1 — nudge

- `PauseState` is a new file in `FleetCore`. It is the only place the `HashSet` lives.
- The lock is a `private readonly object _gate = new();`. Every method that reads or writes the set takes it.
- `RunLoopGate` keeps its 3-arg constructor. Add a 4-arg one that accepts a `PauseState`. Delegate the pause methods.
- In `serve`, create the `PauseState` once and call it from the handler next to the existing `TaskBoard` calls.
- The concurrency test should use `Task.WhenAll` over several threads doing Pause/Resume/IsPaused/Snapshot. It passes when no exception is thrown.

## Tier 2 — direction

- `PauseState` shape:
  ```csharp
  public sealed class PauseState
  {
      private readonly object _gate = new();
      private readonly HashSet<string> _paused = new(StringComparer.Ordinal);
      public void Pause(string shuttleId) { if (string.IsNullOrWhiteSpace(shuttleId)) return; lock (_gate) _paused.Add(shuttleId); }
      public void Resume(string shuttleId) { if (string.IsNullOrWhiteSpace(shuttleId)) return; lock (_gate) _paused.Remove(shuttleId); }
      public bool IsPaused(string shuttleId) { if (string.IsNullOrWhiteSpace(shuttleId)) return false; lock (_gate) return _paused.Contains(shuttleId); }
      public void PauseAll(IEnumerable<string> ids) { lock (_gate) { foreach (var id in ids) if (!string.IsNullOrWhiteSpace(id)) _paused.Add(id); } }
      public void ResumeAll(IEnumerable<string> ids) { lock (_gate) { foreach (var id in ids) if (!string.IsNullOrWhiteSpace(id)) _paused.Remove(id); } }
      public IReadOnlyCollection<string> Snapshot() { lock (_gate) return _paused.ToArray(); }
  }
  ```
- `RunLoopGate`:
  ```csharp
  private readonly PauseState _pauseState;
  public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic)
      : this(occupancy, track, traffic, new PauseState()) { }
  public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic, PauseState pauseState)
  {
      // ... existing null checks ...
      _pauseState = pauseState ?? throw new ArgumentNullException(nameof(pauseState));
  }
  public PauseState PauseState => _pauseState;
  public void Pause(string id) => _pauseState.Pause(id);
  // ... delegate the rest ...
  ```
- `CanMove`: replace `IsPaused(shuttleId)` with `_pauseState.IsPaused(shuttleId)`.
- In `Program.cs` `RunServeAsync`: `var pauseState = new PauseState();` right after `var taskBoard = new TaskBoard();`. In the handler, add the `pauseState.Pause`/`Resume`/`*All` calls.
- The concurrency test: spawn 4 tasks, each doing 1000 ops; `await Task.WhenAll`; assert no exception and `state.Snapshot().Count` is in range.

## Tier 3 — near-solution

The full `PauseState.cs`:

```csharp
namespace FleetCore;

public sealed class PauseState
{
    private readonly object _gate = new();
    private readonly HashSet<string> _paused = new(StringComparer.Ordinal);

    public void Pause(string shuttleId)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) return;
        lock (_gate) { _paused.Add(shuttleId); }
    }

    public void Resume(string shuttleId)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) return;
        lock (_gate) { _paused.Remove(shuttleId); }
    }

    public bool IsPaused(string shuttleId)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) return false;
        lock (_gate) { return _paused.Contains(shuttleId); }
    }

    public void PauseAll(IEnumerable<string> shuttleIds)
    {
        if (shuttleIds is null) return;
        lock (_gate)
        {
            foreach (var id in shuttleIds)
                if (!string.IsNullOrWhiteSpace(id)) _paused.Add(id);
        }
    }

    public void ResumeAll(IEnumerable<string> shuttleIds)
    {
        if (shuttleIds is null) return;
        lock (_gate)
        {
            foreach (var id in shuttleIds)
                if (!string.IsNullOrWhiteSpace(id)) _paused.Remove(id);
        }
    }

    public IReadOnlyCollection<string> Snapshot()
    {
        lock (_gate) { return _paused.ToArray(); }
    }
}
```

The gate edits (only the pause-related lines change; occupancy/traffic stay):

```csharp
private readonly PauseState _pauseState;

public PauseState PauseState => _pauseState;

public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic)
    : this(occupancy, track, traffic, new PauseState()) { }

public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic, PauseState pauseState)
{
    Occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
    Track = track ?? throw new ArgumentNullException(nameof(track));
    Traffic = traffic ?? throw new ArgumentNullException(nameof(traffic));
    _pauseState = pauseState ?? throw new ArgumentNullException(nameof(pauseState));
}

public void Pause(string shuttleId) => _pauseState.Pause(shuttleId);
public void Resume(string shuttleId) => _pauseState.Resume(shuttleId);
public bool IsPaused(string shuttleId) => _pauseState.IsPaused(shuttleId);
public void PauseAll(IEnumerable<string> shuttleIds) => _pauseState.PauseAll(shuttleIds);
public void ResumeAll(IEnumerable<string> shuttleIds) => _pauseState.ResumeAll(shuttleIds);
```

In `CanMove`, change `if (IsPaused(shuttleId))` to `if (_pauseState.IsPaused(shuttleId))`. In `CommitMove`, same.

In `Program.cs`, add after `var taskBoard = new TaskBoard();`:

```csharp
var pauseState = new PauseState();
```

And in the handler, add the `pauseState.*` calls next to the `taskBoard.*` calls. Use `pauseState.PauseAll`/`ResumeAll` when `cmd.ShuttleId` is blank.

The concurrency test:

```csharp
[Fact]
public async Task Snapshot_is_consistent_under_concurrency()
{
    var state = new PauseState();
    const int threads = 4;
    const int ops = 1000;
    var tasks = new Task[threads];
    for (var t = 0; t < threads; t++)
    {
        var id = t;
        tasks[t] = Task.Run(() =>
        {
            for (var i = 0; i < ops; i++)
            {
                state.Pause($"SH-{id}");
                _ = state.IsPaused($"SH-{id}");
                if (i % 2 == 0) state.Resume($"SH-{id}");
                _ = state.Snapshot();
            }
        });
    }
    await Task.WhenAll(tasks);
    // No exception means the lock held. Count is 0..4.
    Assert.InRange(state.Snapshot().Count, 0, threads);
}
```
