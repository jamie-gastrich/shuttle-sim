# src/ShuttleCore

The shuttle. A state machine for a single automated pallet shuttle: `Idle/Moving/Loading/Unloading/Fault`, a warehouse slot, movement timings, rack and lane models. Pure C#, no I/O.

## Depends on

- Nothing.

## Used by

- `ShuttleSim` (wraps one `Shuttle` with a bus endpoint and heartbeat publisher)
- `TrafficCore` (reads the track geometry)
- `ShuttleCore.Tests`.

## Overview

ShuttleCore is the pure state machine for one shuttle. It has no I/O, no clock, and no knowledge of the network — the caller supplies time and calls the transitions.

```text
  ShuttleSim (or a test)
      │
      │  new Shuttle(shuttleId, rack, startLane, timing)
      │  shuttle.StartMove(toLane, now)     → validates the lane, enters Moving
      │  shuttle.CompleteMove(now)          → after travel time, enters Idle
      │  shuttle.StartLoad(slot, now)       → enters Loading
      │  shuttle.CompleteLoad(now)          → takes the pallet, enters Idle
      │  shuttle.StartUnload(slot, now)     → enters Unloading
      │  shuttle.CompleteUnload(now)        → places the pallet, enters Idle
      │  shuttle.RaiseFault(reason, now)    → enters Fault (from any state)
      │  shuttle.State  /  shuttle.Position → read by the caller
      ▼
  Caller reads State and decides what to do next
      (ShuttleSim publishes a status message; the run loop advances the move)

TrafficCore and the FleetManager run loop read Track/Rack directly as well.
```

Every method takes `now`, and the machine rejects time that goes backwards, so
a whole scenario replays deterministically in a unit test (no sleeps, no
broker). Illegal transitions throw `ShuttleRejectedException` instead of
silently succeeding. See records 05 and 06.

## The files

| File | Where it fits |
|---|---|
| `Shuttle.cs` | The state machine: `StartMove`/`CompleteMove`, `StartLoad`/`CompleteLoad`, `StartUnload`/`CompleteUnload`, `RaiseFault`; holds position, carried pallet, fault reason. |
| `ShuttleState.cs` | The enum `Idle/Moving/Loading/Unloading/Fault` and the wire-name mapping. |
| `ShuttleTiming.cs` | Times for move-per-lane, load, and unload (config, never hard-coded). |
| `Rack.cs` | The warehouse grid: lanes, slots, `Place`/`Take`, occupancy, and lane distance. |
| `Track.cs` | The lane segments and `Route(from, to)` — the geometry TrafficCore reserves against. |
| `ShuttleRejectedException.cs` | Thrown on an illegal transition (unknown lane, load while moving, clock going backwards). |
