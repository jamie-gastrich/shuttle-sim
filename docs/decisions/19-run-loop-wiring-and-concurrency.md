# Decision Record 19: Run loop wiring, concurrency, and the gate-then-dispatcher sequence

**Date:** 2026-10-13
**Milestone:** M4 (Traffic control) — lesson 12: wiring the gate into a real run loop.
**Status:** Accepted. Append-only. Links back to records 16, 17, 18. Supersedes nothing.
**Builds on:** [record 16](16-traffic-segments-and-deadlock-rule.md) (all-or-nothing whole-route reservation), [record 17](17-occupancy-and-fault-reservations.md) (occupancy vs reservation, release on departure, fault behavior), [record 18](18-deferred-dispatch-and-storage-policies.md) (deferred dispatch and storage policies).
**Replaced by:** a later record that changes the run loop's structure, the concurrency model, or the gate sequence. Append-only.
**Wire contract:** no change to `docs/contract/v1.md` or `v2.md`. No new message kinds; no version bump.

---

## 1. What we are deciding

Lesson 11 built the pure gate (`RunLoopGate`) and the occupancy view (`OccupancyMap`). The gate is proven by unit tests, but nothing calls it. M4's done-when requires *"a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock."* That needs a **run loop** — the piece that drives shuttles through the gate, assigns tasks, and updates state.

Review 11 flagged two things to decide in the wiring lesson:

1. **Concurrency.** `OccupancyMap` and `SegmentReservations` are plain `Dictionary` fields. Once the run loop and the status-reporting path run at the same time, concurrent access can corrupt state or throw.
2. **The gate-then-dispatcher sequence.** `RunLoopGate.CanMove` calls `TrafficController.TryReserveRoute`, which reserves the whole route as a side effect of a `bool`-returning query. If the process stops between `CanMove` returning `true` and `TaskDispatcher.AssignNextAsync` running, that route stays blocked forever.

This record settles both, plus the run loop's shape.

---

## 2. Decision A: a `RunLoop` class in `FleetManager`, driving shuttles directly

The run loop is the piece that makes the gate and dispatcher work together. It repeatedly:

1. Peeks at the next queued task.
2. Asks the gate *"may this shuttle move to this destination?"*
3. If yes, assigns the task and starts the move.
4. When the move completes, updates occupancy and releases the transit reservation.

| | A: `RunLoop` class in `FleetManager`, drives `Shuttle` objects directly (chosen) | B: wire the loop into `Program.cs` only | C: a separate `FleetLoop` project |
|---|---|---|---|
| Testable without a broker or DB | yes — the test drives the class | **no** — `Program.cs` is an exe with MQTT setup | yes |
| Matches the `ShuttleSim` precedent (app layer drives `ShuttleCore`) | yes | yes | yes |
| New projects | none | none | one |
| The loop can be unit-tested | yes | no | yes |

**Decision A: a `RunLoop` class in `FleetManager`.** It takes the occupancy map, track, traffic controller, gate, task board, dispatcher, and a list of `Shuttle` objects. The test drives the class directly — no broker, no database, no process. `Program.cs` can later call this class, but the loop is testable on its own. Rejected: wiring only into `Program.cs` (the loop cannot be unit-tested without MQTT); a new project (one more seam for no gain — the loop is app logic, like the dispatcher).

**Why the loop drives `Shuttle` objects directly.** In a real deployment, the fleet manager sends MQTT commands to shuttle processes. In the simulation, the run loop is inside the same process as the shuttles, so it calls `Shuttle.StartMove` and `Shuttle.CompleteMove` directly. This is the same simplification the M3 end-to-end test used ("process-in-spirit"). The spec's rule — *"each simulated shuttle plays the role of the vehicle's onboard controller; it talks to the fleet manager only through the documented message contract"* — governs the ShuttleSim process, not the run loop's internal simulation. M5 will add the MQTT status path that makes the concurrency real.

---

## 3. Decision B: `lock` around shared state, not `ConcurrentDictionary`

`OccupancyMap` and `SegmentReservations` are plain `Dictionary` fields. Two threads will touch them at once: the run loop (reads occupancy, reserves routes) and the status-reporting path (writes occupancy from `ShuttleStatus.Position`).

| | A: `lock` around all access (chosen) | B: `ConcurrentDictionary` | C: leave as-is (plain `Dictionary`) |
|---|---|---|---|
| Multi-step operations are atomic | yes — the lock covers the whole sequence | **no** — `TryReserveAll` checks then writes; two threads can interleave between the passes | **no** |
| Simple to reason about | yes — one lock, one owner | harder — fine-grained but still racy for check-then-write | simplest, but unsafe |
| Matches the dotnet-style rule | yes — *"protect shared state; prefer a single owner"* | partly | **no** |

**Decision B: a `lock` around all access to `OccupancyMap` and `SegmentReservations`.** The operations are multi-step: `IsOccupiedByOthers` reads many entries; `TryReserveAll` checks every segment and then writes every segment. `ConcurrentDictionary` protects each individual read or write, but it does not make a check-then-write sequence atomic — two threads can still interleave between the check and the write, and the two-pass reservation can leave a half-held route. A single `lock` makes the whole sequence atomic. Rejected: `ConcurrentDictionary` (does not fix the check-then-write race); leaving as-is (unsafe).

**Scope.** The lock is added to `OccupancyMap` (lesson 11's file) and `SegmentReservations` (lesson 10's file). Both are modified, not replaced. The `TrafficController` calls into `SegmentReservations`; it does not need its own lock because the reservations object protects itself.

---

## 4. Decision C: split the gate into `CanMove` (pure) + `CommitMove` (reserve)

`RunLoopGate.CanMove` calls `TrafficController.TryReserveRoute`, which reserves the route as a side effect. The run loop needs to ask *"may I move?"* without reserving, then reserve only when it is ready to move.

| | A: split into `CanMove` (pure) + `CommitMove` (reserve) (chosen) | B: keep `CanMove` as-is, release on failure | C: a reservation token that must be committed or rolled back |
|---|---|---|---|
| `CanMove` is pure (no side effects) | yes | **no** — it reserves | yes |
| No reservation leak | yes — nothing is reserved until `CommitMove` | **no** — a failure between `CanMove` and `AssignNextAsync` leaks | yes |
| Simple to explain and test | yes | harder — the release path is hidden | harder — a token is more machinery |
| Matches AGENTS.md separation rule | yes — decision logic is pure | partly | yes |

**Decision C: split the gate.** `CanMove` becomes a pure check — it reads occupancy and transit reservations but writes nothing. `CommitMove` reserves the route (and may fail if another shuttle grabbed it first). The run loop:

1. Calls `CanMove` — if `false`, skip this shuttle this tick.
2. Calls `TaskDispatcher.AssignNextAsync` — assigns the task.
3. Calls `CommitMove` — if `false`, the task stays assigned but the shuttle does not move; the loop retries next tick.

Rejected: keeping `CanMove` as-is with release-on-failure (the release path is hidden and easy to get wrong); a reservation token (more machinery than M4 needs).

**Why a pure `CanMove` can still check transit reservations.** `SegmentReservations.OwnerOf(segment)` is a read. `CanMove` reads every segment's owner; it writes nothing. So the pure check is: *"is every segment in this route either free or owned by this shuttle?"* That is a read of the reservation table, not a write.

**Adding `IsRouteFree` to `TrafficController`.** The pure check needs a way to ask *"is this route free?"* without reserving. `TrafficController` gains:

```csharp
public bool IsRouteFree(string shuttleId, string fromLane, string toLane)
{
    var route = _track.Route(fromLane, toLane);
    return route.All(segment =>
    {
        var owner = _reservations.OwnerOf(segment);
        return owner is null || string.Equals(owner, shuttleId, StringComparison.Ordinal);
    });
}
```

This is a pure read. `RunLoopGate.CanMove` uses it for the transit-reservation check.

---

## 5. What would change these choices

- **The run loop moves to a separate process or service** → the `RunLoop` class stays, but its transport changes from direct calls to MQTT commands; new record.
- **The concurrency model changes** (e.g., the fleet manager becomes multi-instance) → the `lock` is per-process; a distributed lock or database-backed reservation would be needed; new record.
- **The gate sequence changes** (e.g., a reservation token is needed for a two-phase commit) → Decision C is superseded; new record.
- **Sub-lane granularity arrives** (record 05 §4 resolved) → `Track.Route` returns sub-lane segments; the run loop and gate do not change; new record linking to record 16.

---

## 6. Verification table (measured)

Experiments ran on 2026-10-13. The baseline suite ran against the real `src/`; the toy ran at `/tmp/opencode/l12toy`. The toy directory is deleted afterwards. No file under `src/` was changed.

| Claim | How checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` | `10.0.112` |
| Baseline pure suite is green | `dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | RC 0, **8** `Passed!` lines, 0 `Failed!`; **83** tests |
| `OccupancyMap` is a plain `Dictionary` (not thread-safe) | read `src/FleetManager/OccupancyMap.cs:5` | `private readonly Dictionary<string, string> _shuttleToLane` |
| `SegmentReservations` is a plain `Dictionary` (not thread-safe) | read `src/TrafficCore/SegmentReservations.cs:5` | `private readonly Dictionary<string, string> _ownerBySegment` |
| `RunLoopGate.CanMove` reserves as a side effect | read `src/FleetManager/RunLoopGate.cs:35` | `Traffic.TryReserveRoute(shuttleId, currentLane, destinationLane)` |
| `TrafficController` has no pure check method | `grep -n 'public' src/TrafficCore/TrafficController.cs` | only `TryReserveRoute` and `Release` |
| The toy run loop drives 3 shuttles through the gate with no collisions and no deadlock | toy at `/tmp/opencode/l12toy`, `dotnet run` | 3 shuttles finish in 3 rounds; collisions 0; exit 0 |
| A head-on scenario deadlocks (contrast) | same toy, round-robin destinations | all shuttles blocked forever; 0 done in 20 rounds |

Not yet verified: the `RunLoop` class, the `IsRouteFree` method, the split gate, the `lock`-protected `OccupancyMap` and `SegmentReservations`, and the stress test — this assignment specifies them and the reviewer measures them.

---

## 7. What this record unblocks

- Lesson 12 can assign a `RunLoop` class with a fixed shape, a split gate (`CanMove` + `CommitMove`), and `lock`-protected shared state, with no unguided choices.
- The M4 done-when's *"a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock"* is provable by a pure test that drives the `RunLoop` class.
- M5 has a named hook: the status-reporting path calls `OccupancyMap.SetOccupiedLane`, and the run loop reads it — both under the same `lock`.
