# Hints for lesson 04: the warehouse model and one shuttle

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- Four new projects, four jobs: `ShuttleCore` (the rack and the machine, and it references
  **nothing**), `ShuttleCore.Tests` (the transitions, rejections, and rack facts),
  `ShuttleSim` (the seven-line demo), `ShuttleSim.Tests` (the one test that runs the demo).
  Ask which of the four is missing before you touch any code.
- The milestone's "done when" has two halves, and the assignment repeats both: a **scripted
  sequence** that really moves a pallet, and **every state transition** covered by a named test.
  A green happy path is not enough; the rejections are half the suite.
- `ShuttleCore` may not contain a duration literal. If you catch yourself typing
  `TimeSpan.FromSeconds(5)` there, stop — the number belongs in the caller (the demo or a test).
  The acceptance greps `src/ShuttleCore` for exactly that shape.
- Nothing may sleep. `Thread.Sleep` and `Task.Delay` are banned across all of `src/`; time is
  passed in as `DateTime now`, never waited on. A scenario that takes 17 virtual seconds should
  finish in milliseconds.
- The rack validates ids by **membership**, not by parsing. You built every lane and slot id in
  the constructor, so `IsLane("A01-L03")` is a lookup that is simply absent in a two-lane aisle;
  `IsLane("banana")` likewise. No regex.
- `Position` changes on `CompleteMove`, not on `StartMove`. While moving, the contract's
  "lane the shuttle is in" is still the origin, because the target has not been reached. The
  demo's fourth line proves it: `"State":"moving"` with `"Position":"A01-L01"`.
- Fault is a **sink** at M1. `RaiseFault` enters it from any other state; every event afterwards
  is rejected. Recovery is M5 — do not invent a way out.
- The demo test runs the built app in a child process, so it must bound the wait (`WaitForExit`
  with a named timeout constant, and kill the process tree if it overruns). Review 03 left this
  exact gap in `StartTests.cs`; do not repeat it here.

---

## Tier 2 — direction

- Order of work, and why:

  1. `ShuttleState.cs`, `ShuttleTiming.cs`, `ShuttleRejectedException.cs` — three tiny files
     with no dependencies, so everything else can compile against them.
  2. `Rack.cs` next — build the ids, then the read surface, then `Place`/`Take`. Test it in
     isolation before a shuttle exists; the rack is the world, and the shuttle is only a visitor.
  3. `Shuttle.cs` — the constructor and read properties first, then the events in pairs
     (`StartMove`/`CompleteMove`, then load, then unload, then `RaiseFault`). Notice the three
     habits the lecture highlights: guard before you change, reject with the one exception type,
     keep `_lastEventAt` for the clock check.
  4. `ShuttleCore.Tests` — write the transition tests as you add each event, not after. The ten
     names are in the assignment; the six rejection tests prove the guards exist.
  5. `ShuttleSim/Program.cs` — the fixed scenario, one `Report(...)` after each step.
  6. `ShuttleSim.Tests/DemoTests.cs` — the child-process test, with the timeout.
  7. Run the acceptance block one phase at a time, reading every exit code and every printed
     number directly.

- The project graph is fixed: six new `ProjectReference` edges, ten in total. `ShuttleCore`
  references nothing — that is the compiler enforcing "pure logic", and the acceptance fails if
  a reference slips in. `ShuttleSim` references `Contracts` and `ShuttleCore`; `ShuttleSim.Tests`
  references all three. Nothing references `FleetManager`.
- Distance is **aisle-major ordinals**: lanes are numbered inside an aisle, aisles in order. In a
  `2 × 2` rack, `A01-L01 → A02-L02` is three lanes, so a `5 s` per-lane time is `15 s`. Put the
  arithmetic in `Rack.DistanceInLanes`; the shuttle multiplies by `MovePerLane`.
- The clock check rejects `now < lastEventAt`; **equal is allowed**. Arriving and starting to
  unload in the same instant is legal. Update `_lastEventAt` on every event that succeeds.
- Guard order inside `StartUnload` matters for the tests: check the state, then that the slot is
  in the current lane, then that the shuttle is carrying a pallet, then that the target slot is
  empty. `StartLoad` is the mirror: slot in this lane, shuttle empty-handed, slot occupied.
- The demo's numbers (`2 × 2 × 2`, `5/2/2`, the epoch) are **scenario data**, not configuration
  — the same class as lesson 02's example payload. The no-duration-literal gate is scoped to
  `ShuttleCore` on purpose; putting the numbers in `Program.cs` is correct. Addresses, ports, and
  real timeouts stay banned (M2 introduces configuration).
- The demo prints with `JsonSerializer.Serialize(status)` using **default options** — no
  `WriteIndented`, no naming policy. `ShuttleStatus` is unchanged and the property names are the
  wire names. Seven lines, nothing else on stdout.
- The demo test must **parse** the JSON, not grep strings in C#. Deserialize each line to
  `ShuttleStatus` and assert the state sequence; that is what the `JsonSerializer` and
  `ShuttleStatus` greps are checking for.

---

## Tier 3 — near-solution

Copy-paste shapes. Every block is a **template**: the shape and the fixed facts come from the
assignment, the typing is not done for you — type every line yourself. Every name the acceptance
greps for appears verbatim below. A failure on your side is a typing slip, not my syntax.

### The three small files

```csharp
// src/ShuttleCore/ShuttleState.cs
namespace ShuttleCore;

public enum ShuttleState
{
    Idle,
    Moving,
    Loading,
    Unloading,
    Fault,
}

public static class ShuttleStateWire
{
    public static string ToWireName(this ShuttleState state) => state switch
    {
        ShuttleState.Idle => "idle",
        ShuttleState.Moving => "moving",
        ShuttleState.Loading => "loading",
        ShuttleState.Unloading => "unloading",
        ShuttleState.Fault => "fault",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, "unknown shuttle state"),
    };
}
```

```csharp
// src/ShuttleCore/ShuttleTiming.cs
namespace ShuttleCore;

public sealed record ShuttleTiming(TimeSpan MovePerLane, TimeSpan Load, TimeSpan Unload);
```

```csharp
// src/ShuttleCore/ShuttleRejectedException.cs
namespace ShuttleCore;

public sealed class ShuttleRejectedException : Exception
{
    public ShuttleRejectedException(string message) : base(message)
    {
    }
}
```

### `src/ShuttleCore/Rack.cs`

```csharp
namespace ShuttleCore;

public sealed class Rack
{
    private readonly Dictionary<string, int> _laneOrdinals = new();
    private readonly Dictionary<string, string> _slotLanes = new();
    private readonly Dictionary<string, string?> _palletsBySlot = new();

    public Rack(int aisleCount, int lanesPerAisle, int slotsPerLane)
    {
        if (aisleCount < 1) throw new ArgumentOutOfRangeException(nameof(aisleCount));
        if (lanesPerAisle < 1) throw new ArgumentOutOfRangeException(nameof(lanesPerAisle));
        if (slotsPerLane < 1) throw new ArgumentOutOfRangeException(nameof(slotsPerLane));

        AisleCount = aisleCount;
        LanesPerAisle = lanesPerAisle;
        SlotsPerLane = slotsPerLane;

        var ordinal = 0;
        for (var aisle = 1; aisle <= aisleCount; aisle++)
        for (var lane = 1; lane <= lanesPerAisle; lane++)
        {
            var laneId = $"A{aisle:00}-L{lane:00}";
            _laneOrdinals[laneId] = ordinal++;
            for (var slot = 1; slot <= slotsPerLane; slot++)
            {
                var slotId = $"{laneId}-S{slot:00}";
                _slotLanes[slotId] = laneId;
                _palletsBySlot[slotId] = null;
            }
        }
    }

    public int AisleCount { get; }
    public int LanesPerAisle { get; }
    public int SlotsPerLane { get; }
    public IEnumerable<string> LaneIds => _laneOrdinals.Keys;
    public IEnumerable<string> SlotIds => _palletsBySlot.Keys;

    public bool IsLane(string laneId) => _laneOrdinals.ContainsKey(laneId);
    public bool IsSlot(string slotId) => _palletsBySlot.ContainsKey(slotId);
    public bool IsSlotInLane(string slotId, string laneId) =>
        _slotLanes.TryGetValue(slotId, out var home) && home == laneId;

    public int DistanceInLanes(string fromLane, string toLane) =>
        Math.Abs(Ordinal(fromLane) - Ordinal(toLane));

    public bool IsOccupied(string slotId) => PalletAt(slotId) is not null;

    public string? PalletAt(string slotId) => _palletsBySlot.TryGetValue(slotId, out var pallet)
        ? pallet
        : throw new ArgumentException($"unknown slot {slotId}", nameof(slotId));

    public void Place(string slotId, string palletId)
    {
        if (!_palletsBySlot.ContainsKey(slotId)) throw new ArgumentException($"unknown slot {slotId}");
        if (_palletsBySlot[slotId] is not null) throw new InvalidOperationException($"{slotId} is occupied");
        _palletsBySlot[slotId] = palletId;
    }

    public string Take(string slotId)
    {
        if (!_palletsBySlot.ContainsKey(slotId)) throw new ArgumentException($"unknown slot {slotId}");
        var pallet = _palletsBySlot[slotId] ?? throw new InvalidOperationException($"{slotId} is empty");
        _palletsBySlot[slotId] = null;
        return pallet;
    }

    private int Ordinal(string laneId) => _laneOrdinals.TryGetValue(laneId, out var ordinal)
        ? ordinal
        : throw new ArgumentException($"unknown lane {laneId}", nameof(laneId));
}
```

### `src/ShuttleCore/Shuttle.cs`

```csharp
namespace ShuttleCore;

public sealed class Shuttle
{
    private readonly Rack _rack;
    private readonly ShuttleTiming _timing;
    private ShuttleState _state = ShuttleState.Idle;
    private string _position;
    private string? _carriedPalletId;
    private string? _faultReason;
    private DateTime? _lastEventAt;
    private DateTime _moveStartedAt;
    private string _moveToLane = string.Empty;
    private DateTime _phaseStartedAt;
    private string _phaseSlot = string.Empty;

    public Shuttle(string shuttleId, Rack rack, string startLane, ShuttleTiming timing)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) throw new ArgumentException("id required", nameof(shuttleId));
        if (!rack.IsLane(startLane)) throw new ArgumentException($"unknown lane {startLane}", nameof(startLane));

        ShuttleId = shuttleId;
        _rack = rack;
        _timing = timing;
        _position = startLane;
    }

    public string ShuttleId { get; }
    public ShuttleState State => _state;
    public string Position => _position;
    public string? CarriedPalletId => _carriedPalletId;
    public string? FaultReason => _faultReason;

    public void StartMove(string toLane, DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Idle, "start a move");
        if (!_rack.IsLane(toLane)) throw new ShuttleRejectedException($"unknown lane {toLane}");
        _moveToLane = toLane;
        _moveStartedAt = now;
        _state = ShuttleState.Moving;
        _lastEventAt = now;
    }

    public void CompleteMove(DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Moving, "complete a move");
        var travel = TravelTime(_position, _moveToLane);
        if (now - _moveStartedAt < travel)
            throw new ShuttleRejectedException($"travel to {_moveToLane} has not elapsed");
        _position = _moveToLane;
        _state = ShuttleState.Idle;
        _lastEventAt = now;
    }

    public void StartLoad(string slotId, DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Idle, "start loading");
        EnsureSlotInLane(slotId);
        if (_carriedPalletId is not null) throw new ShuttleRejectedException("already carrying a pallet");
        if (!_rack.IsOccupied(slotId)) throw new ShuttleRejectedException($"slot {slotId} holds no pallet");
        _phaseSlot = slotId;
        _phaseStartedAt = now;
        _state = ShuttleState.Loading;
        _lastEventAt = now;
    }

    public void CompleteLoad(DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Loading, "complete loading");
        if (now - _phaseStartedAt < _timing.Load) throw new ShuttleRejectedException("load not elapsed");
        _carriedPalletId = _rack.Take(_phaseSlot);
        _state = ShuttleState.Idle;
        _lastEventAt = now;
    }

    public void StartUnload(string slotId, DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Idle, "start unloading");
        EnsureSlotInLane(slotId);
        if (_carriedPalletId is null) throw new ShuttleRejectedException("not carrying a pallet");
        if (_rack.IsOccupied(slotId)) throw new ShuttleRejectedException($"slot {slotId} is occupied");
        _phaseSlot = slotId;
        _phaseStartedAt = now;
        _state = ShuttleState.Unloading;
        _lastEventAt = now;
    }

    public void CompleteUnload(DateTime now)
    {
        EnsureInOrder(now);
        EnsureState(ShuttleState.Unloading, "complete unloading");
        if (now - _phaseStartedAt < _timing.Unload) throw new ShuttleRejectedException("unload not elapsed");
        _rack.Place(_phaseSlot, _carriedPalletId!);
        _carriedPalletId = null;
        _state = ShuttleState.Idle;
        _lastEventAt = now;
    }

    public void RaiseFault(string reason, DateTime now)
    {
        EnsureInOrder(now);
        if (_state == ShuttleState.Fault) throw new ShuttleRejectedException("already faulted");
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("reason required", nameof(reason));
        _faultReason = reason;
        _state = ShuttleState.Fault;
        _lastEventAt = now;
    }

    private void EnsureInOrder(DateTime now)
    {
        if (_lastEventAt is DateTime previous && now < previous)
            throw new ShuttleRejectedException($"clock went backwards: {now:O} < {previous:O}");
    }

    private void EnsureState(ShuttleState expected, string what)
    {
        if (_state != expected)
            throw new ShuttleRejectedException($"cannot {what} while {_state.ToWireName()}");
    }

    private void EnsureSlotInLane(string slotId)
    {
        if (!_rack.IsSlot(slotId)) throw new ShuttleRejectedException($"unknown slot {slotId}");
        if (!_rack.IsSlotInLane(slotId, _position))
            throw new ShuttleRejectedException($"slot {slotId} is not in lane {_position}");
    }

    private TimeSpan TravelTime(string fromLane, string toLane) =>
        TimeSpan.FromTicks(_rack.DistanceInLanes(fromLane, toLane) * _timing.MovePerLane.Ticks);
}
```

### The test files — one worked example per category

Type the other bodies yourself; the ten transition names and six rejection names are listed in
the assignment. Every test takes the same shape.

```csharp
// src/ShuttleCore.Tests/StateTransitionTests.cs (one example; write all ten)
private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
private static readonly ShuttleTiming Timing =
    new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

[Fact]
public void StartMove_from_idle_enters_moving()
{
    var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
    var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);

    shuttle.StartMove("A01-L02", Start);

    Assert.Equal(ShuttleState.Moving, shuttle.State);
}
```

```csharp
// src/ShuttleCore.Tests/RejectionTests.cs (one example; write all six)
[Fact]
public void CompleteMove_before_travel_time_has_passed_is_rejected()
{
    var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
    var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
    shuttle.StartMove("A02-L02", Start);                 // three lanes × 5 s = 15 s

    Assert.Throws<ShuttleRejectedException>(() => shuttle.CompleteMove(Start.AddSeconds(14)));
}
```

```csharp
// src/ShuttleCore.Tests/ScriptedMoveTests.cs
[Fact]
public void Scripted_sequence_moves_a_pallet_from_one_slot_to_another()
{
    var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
    rack.Place("A01-L01-S01", "PLT-001");
    var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);

    shuttle.StartLoad("A01-L01-S01", Start);
    shuttle.CompleteLoad(Start.Add(Timing.Load));
    shuttle.StartMove("A01-L02", Start.Add(Timing.Load));
    shuttle.CompleteMove(Start.Add(Timing.Load).Add(Timing.MovePerLane));
    var atSlot = Start.Add(Timing.Load).Add(Timing.MovePerLane).Add(Timing.Load);
    shuttle.StartUnload("A01-L02-S02", atSlot);
    shuttle.CompleteUnload(atSlot.Add(Timing.Unload));

    Assert.Equal("PLT-001", rack.PalletAt("A01-L02-S02"));
    Assert.Null(rack.PalletAt("A01-L01-S01"));
    Assert.Equal(ShuttleState.Idle, shuttle.State);
    Assert.Null(shuttle.CarriedPalletId);
}
```

The four rack tests and the wire-name test are short and behaviour-named, exactly as the
assignment lists them; each needs at least one `Assert`.

### `src/ShuttleSim/Program.cs`

```csharp
using System.Text.Json;
using Contracts;
using ShuttleCore;

var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
var timing = new ShuttleTiming(
    MovePerLane: TimeSpan.FromSeconds(5),
    Load: TimeSpan.FromSeconds(2),
    Unload: TimeSpan.FromSeconds(2));
var shuttle = new Shuttle("SH-01", rack, startLane: "A01-L01", timing);

var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
rack.Place("A01-L01-S01", "PLT-001");

Report(start);

shuttle.StartLoad("A01-L01-S01", start);
Report(start);

var loaded = start.Add(timing.Load);
shuttle.CompleteLoad(loaded);
Report(loaded);

shuttle.StartMove("A02-L02", loaded);
Report(loaded);

var travel = TimeSpan.FromTicks(rack.DistanceInLanes("A01-L01", "A02-L02") * timing.MovePerLane.Ticks);
var arrived = loaded.Add(travel);
shuttle.CompleteMove(arrived);
Report(arrived);

shuttle.StartUnload("A02-L02-S02", arrived);
Report(arrived);

var placed = arrived.Add(timing.Unload);
shuttle.CompleteUnload(placed);
Report(placed);

void Report(DateTime at)
{
    var status = new ShuttleStatus(
        shuttle.ShuttleId,
        shuttle.State.ToWireName(),
        shuttle.Position,
        at,
        shuttle.FaultReason);
    Console.WriteLine(JsonSerializer.Serialize(status));
}
```

Measured output, for the record — exactly seven lines, and the timestamps are the point:
`00:00:00 idle`, `00:00:00 loading`, `00:00:02 idle`, `00:00:02 moving` (**still**
`"Position":"A01-L01"`), `00:00:17 idle`, `00:00:17 unloading`, `00:00:19 idle` (all
`"Position":"A02-L02"` after arrival). `00:00:17` is `2 s + 3 lanes × 5 s`.

### `src/ShuttleSim.Tests/DemoTests.cs`

```csharp
using System.Diagnostics;
using System.Text.Json;
using Contracts;

namespace ShuttleSim.Tests;

public class DemoTests
{
    private const int DemoTimeoutMs = 30_000;

    [Fact]
    public async Task Demo_prints_a_status_line_per_step()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "ShuttleSim.dll");
        var psi = new ProcessStartInfo { FileName = "dotnet", RedirectStandardOutput = true };
        psi.ArgumentList.Add(dll);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var exited = process.WaitForExit(DemoTimeoutMs);
        if (!exited) process.Kill(entireProcessTree: true);
        Assert.True(exited, $"the demo did not exit within {DemoTimeoutMs} ms");
        var output = await stdout;

        var statuses = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonSerializer.Deserialize<ShuttleStatus>(line))
            .ToList();

        Assert.Equal(7, statuses.Count);
        Assert.Equal(
            new[] { "idle", "loading", "idle", "moving", "idle", "unloading", "idle" },
            statuses.Select(s => s!.State).ToArray());
        Assert.All(statuses, s => Assert.Equal("SH-01", s!.ShuttleId));
        Assert.Equal("A01-L01", statuses[3]!.Position);
        Assert.Equal("A02-L02", statuses[^1]!.Position);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 19, DateTimeKind.Utc), statuses[^1]!.Timestamp);
    }
}
```

The `WaitForExit(DemoTimeoutMs)` is the bounded wait; the `Kill` matters only when the bound
trips, but a test that leaks a hung child process is a test that hangs CI.

### Acceptance snippets you will retype

```bash
# From src/, after the four projects exist
dotnet build                                                      # exit 0
T=$(mktemp); dotnet test > "$T" 2>&1; echo "test exit=$?"; \
  grep -c 'Passed!' "$T"; grep -c 'No test is available' "$T"; rm -f "$T"
L=$(mktemp); dotnet test --list-tests > "$L" 2>&1; \
  grep -cE '\.(StartMove|CompleteMove|StartLoad|CompleteLoad|StartUnload|CompleteUnload|RaiseFault)_' "$L"; rm -f "$L"
F=$(mktemp); dotnet run --project ShuttleSim/ShuttleSim.csproj > "$F" 2>&1; echo "run exit=$?"; \
  grep -c '"ShuttleId"' "$F"; sed -n '4p' "$F"; rm -f "$F"
```

Measured on the reference: `test exit=0`, `Passed!` count **4**, `No test is available` **0**,
the transition-name count **10**, `run exit=0`, status count **7**, and line 4
`{"ShuttleId":"SH-01","State":"moving","Position":"A01-L01",…}`.

---

What is deliberately *not* given here: the bodies of the sixteen state-machine tests beyond the
two worked examples, the four rack tests and the wire test, your comments, and the wording of
anything you write. The fixed facts the acceptance greps for — the project names, the ten-edge
graph, `ShuttleState`/`Shuttle`/`Rack`/`ShuttleTiming`/`ShuttleRejectedException`, the seven
event signatures, the five lowercase wire names, the twenty-three test names, the seven-line
demo — plus the behaviours it *probes* (a pallet moved exactly once, position waiting for
arrival, four `Passed!` lines in a fresh container). Type every line yourself, be able to explain
every line of your version out loud, and keep the review's first question in mind: *why can
nothing in `ShuttleCore` sleep, and where does the 15 seconds of a three-lane move actually
live?*
