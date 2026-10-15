# src/TrafficCore

Lane-segment reservations. Pure decision code that answers "can this shuttle reserve this route now?" without any broker or database.

## Depends on

- `ShuttleCore` (track geometry).

## Used by

- `FleetManager` (via `RunLoopGate`); `TrafficCore.Tests`.

## Overview

TrafficCore answers one question: "can this shuttle reserve the segments of this route right now?" It is pure decision code — no broker, no database, no clock.

```text
  FleetManager run loop (through RunLoopGate)
      │
      │  controller.IsRouteFree(shuttleId, fromLane, toLane)   → bool (pure check)
      │  controller.TryReserveRoute(shuttleId, fromLane, toLane)
      │      → TrafficDecision { Granted, ShuttleId, Segments, Reason }
      │
      │  when the shuttle arrives:
      │  controller.Release(shuttleId)        → frees its segments
      ▼
  SegmentReservations holds the state; Track.Route gives the segments.
```

`IsRouteFree` is a pure check; `TryReserveRoute` mutates the reservation table
but still touches no I/O. `RunLoopGate` in `FleetManager` combines this with the
occupancy map before it allows a move. Segments are reservations over lanes, not
physical pieces — a shuttle may not stop inside one (record 16); records 17 and
19 cover occupancy and the run-loop wiring. Collisions and deadlock are covered
by the stress scenario in `TrafficCore.Tests` (4+ shuttles).

## The files

| File | Where it fits |
|---|---|
| `TrafficController.cs` | The decision surface `FleetManager` calls: `TryReserveRoute`, `IsRouteFree`, `Release`. |
| `SegmentReservations.cs` | The reservation table behind the controller: `TryReserveAll`, `Release`, `IsOccupiedByOthers`. |
| `TrafficDecision.cs` | The result: `Granted`, `ShuttleId`, `Segments`, `Reason`. |
