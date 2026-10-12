# Hints for lesson 11: Physical occupancy, the fleet-side occupancy view, and the run-loop gate

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- **Occupancy vs reservation:** Occupancy = "I am physically here" (blocks routes even if idle/working). Transit reservation = "I may use this route for a move" (via `TrafficController.TryReserveRoute`), released on departure/when transit ends.
- **Dispatcher:** Do not change `TaskDispatcher`. The gate goes **before** `AssignNextAsync`. Record 15a §2 says its shape stays the same.
- **Both checks:** Before granting, reject if any segment in route is occupied by a different shuttle OR if `TryReserveRoute` refuses (transit conflict).
- **Faults:** Last-known occupancy stays put until recovery clears it. No auto-release on timeout (record 17 §4). No heartbeats/lastSeen yet (M5).
- **Contract:** `ShuttleStatus.Position` already exists — no wire change.
- **Project references:** `FleetManager` gains `TrafficCore`; `FleetManager.Tests` gains `TrafficCore` and `ShuttleCore`. `RunLoopGate` needs `TrafficController` and `Track`.

---

## Tier 2 — direction

1. **OccupancyMap.** Add `src/FleetManager/OccupancyMap.cs` (`namespace FleetManager;`). A `Dictionary<string, string>` maps shuttleId to occupiedLane. `GetOccupiedLane` returns the lane or `null`. `SetOccupiedLane` writes it. `IsOccupiedByOthers(route, requesterId)` checks if any segment in route is occupied by a shuttle other than requesterId.
2. **RunLoopGate.** Add `src/FleetManager/RunLoopGate.cs` (`namespace FleetManager;`). Constructor takes `OccupancyMap`, `Track` (from `ShuttleCore`), `TrafficController` (from `TrafficCore`). `CanMove(shuttleId, destinationLane)` gets position `P` from occupancy; if `null`, refuse; gets route via `track.Route(P, destinationLane)`; checks `IsOccupiedByOthers`; then calls `traffic.TryReserveRoute(shuttleId, P, destinationLane)`; returns `true` only if both pass.
3. **Test project.** Add `src/FleetManager.Tests/RunLoopGateTests.cs` with the four `[Fact]`s. Build a real `Track` from `new Rack(1, 6, 1)`, a real `TrafficController`, and an `OccupancyMap`. No `Integration` trait, no DB, no broker.
4. **Project references.** `FleetManager.csproj` gains `<ProjectReference Include="..\TrafficCore\TrafficCore.csproj" />`. `FleetManager.Tests.csproj` gains `..\TrafficCore\TrafficCore.csproj` and `..\ShuttleCore\ShuttleCore.csproj`. FleetManager goes from 4 to 5 ProjectReferences; FleetManager.Tests goes from 3 to 5.
5. **Release timing:** On move completion (transit ends), call `traffic.Release(shuttleId)`. The destination remains occupied (update occupancy to destination when shuttle arrives). Document in a comment.
6. **Don't over-scope:** No `lastSeenAt`, no timeout, no reconciliation persistence, no auto-Release on fault detection.
7. **Run the phases in order.** The counts are gated, not admired: Phase 4 expects `RC 0`, **8** `Passed!`, **0** `Failed!`, **83** tests, `FleetManager.Tests` = 15. Phase 5 scans **every** test project for docker launches.

---

## Tier 3 — near-solution shapes

These are **shapes**, not the solution. You still write the bodies and the tests.

**OccupancyMap shape**
```csharp
public sealed class OccupancyMap
{
    // shuttleId -> occupiedLane
    public string? GetOccupiedLane(string shuttleId) { /* dictionary lookup */ }
    public void SetOccupiedLane(string shuttleId, string lane) { /* dictionary write */ }
    public bool IsOccupiedByOthers(IEnumerable<string> route, string requesterId)
    {
        // for each segment in route, check if any OTHER shuttle occupies it
    }
}
```

**RunLoopGate shape**
```csharp
public sealed class RunLoopGate
{
    public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic) { /* store */ }

    public bool CanMove(string shuttleId, string destinationLane)
    {
        var position = _occupancy.GetOccupiedLane(shuttleId);
        if (position is null) return false;
        var route = _track.Route(position, destinationLane);
        if (_occupancy.IsOccupiedByOthers(route, shuttleId)) return false;
        return _traffic.TryReserveRoute(shuttleId, position, destinationLane).Granted;
    }
}
```

**Gate order reminder (say it out loud)**
> Occupancy first, then transit reservation. Both must be clear. The dispatcher is untouched.

**Test names the acceptance walks**
```
Parked_shuttle_blocks_route_through_its_lane
Route_is_clear_after_occupied_shuttle_moves
Gate_allows_route_when_no_occupancy_conflict
Gate_refuses_when_transit_reservation_conflicts
```

**Suite gate (the container's command)**
```bash
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# expects RC 0, 8 Passed!, 0 Failed!, 83 tests, FleetManager.Tests=15
```

**If stuck on fault case:** Remember — "lost communication" means knowledge is uncertain. The shuttle is still physically in its last known position until physically cleared; so occupancy stays. `Release` is post-recovery (operator acknowledges). That is M5 in full detail, but the principle is: never auto-release on timeout.

**Acceptance sanity:** `TaskDispatcher.cs` diff --quiet vs HEAD is true. Pure suite (Category!=Integration) stays green. No new message kinds, no contract version bump.

---

Stuck? These hints cover the key ideas without giving solution code. The acceptance criteria in the lesson file are the authoritative spec.
