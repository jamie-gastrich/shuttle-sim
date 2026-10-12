# Hints for lesson 10: The track as segments, the segment reservation, and the deadlock rule

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- **A segment is a lane, and the track comes from the warehouse model.** `src/ShuttleCore/Track.cs` takes a `Rack` and derives its ordered segments from the rack's lanes; a segment id **is** a lane id (record 16 §3). There is no lane list typed into the file.
- **`Route(from, to)` is inclusive of both ends** (record 16 §5): it returns every segment from the lower lane to the higher lane, a same-lane route returns that one lane, and an unknown lane throws `ArgumentException`. Reserve only the destination and a shuttle can be hit from behind.
- **`SegmentReservations` is the single owner of "who holds which segment"** (record 16 §4): one mapping, segment → owner. `TryReserveAll` is **two-pass**: check *every* requested segment first, and only if all are free write them all. A refused route writes nothing — a waiter holds no segment.
- **The rule is all-or-nothing whole-route reservation (no hold-and-wait).** `TrafficController.TryReserveRoute` asks `Track` for the whole route, hands it to the atomic `TryReserveAll`, and returns a `TrafficDecision` (record 16 §5). This is *avoidance*, not detection: ingredient 2 (hold and wait) is removed, so a circular wait cannot form.
- **`TrafficCore` is package-free** (record 16 §2): no `Npgsql`, `MQTTnet`, `Messaging`, `Contracts`, `DateTime.UtcNow`, `Guid.NewGuid`, `Task.Delay`, `Thread.Sleep`, no literal lane id, exactly one `ProjectReference` — to `ShuttleCore`.
- **Do not wire anything into a run loop.** `TrafficController` decides reservations only. Which shuttle gets a task, and what happens when a request is refused, is the next M4 lesson's job. Do not change `TaskDispatcher`, `Program.cs`, the contract, or the Dockerfile.
- **The stress test proves the M4 done-when at the decision level:** four or more shuttles with overlapping routes finish in bounded rounds, and no segment ever has more than one owner.

---

## Tier 2 — direction

1. **Track.** Add `src/ShuttleCore/Track.cs` (`namespace ShuttleCore;`). Constructor `Track(Rack rack)`. Build `SegmentIds` by asking the rack for its lanes in the same aisle-major order `Rack.DistanceInLanes` uses. `IsSegment` is membership in that set. `Route` walks the ordered ids between the two lane indexes.
2. **Reservation table.** Add `src/TrafficCore/SegmentReservations.cs` — a dictionary from segment id to owner id. `TryReserveAll(ownerId, segmentIds)`: first pass all with `OwnerOf`, and if any is held by a different owner return `false` and write nothing; second pass write them all and return `true`. `Release(ownerId)` removes every segment that owner holds. `SegmentsOwnedBy` returns the freed list.
3. **Controller and decision.** Add `src/TrafficCore/TrafficController.cs` (`TrafficController(Track, SegmentReservations)`) and `src/TrafficCore/TrafficDecision.cs` — `public sealed record TrafficDecision(bool Granted, string ShuttleId, IReadOnlyList<string> Segments, string? Reason)`. `TryReserveRoute(shuttleId, fromLane, toLane)` calls `track.Route(...)`, then `reservations.TryReserveAll(shuttleId, route)`; when refused, name the blocking segment and its owner in `Reason`.
4. **Test projects.** `src/ShuttleCore.Tests/TrackTests.cs` with the three `[Fact]`s (same-lane, in-order inclusive route, unknown-lane rejection) and `src/TrafficCore.Tests/TrafficControllerTests.cs` with the six `[Fact]`s, deleting the template's `UnitTest1.cs`. Build the track from a real rack such as `new Rack(1, 8, 1)` — never from a literal lane list. No `Integration` trait, no DB, no broker.
5. **Stress test.** Grant a pending request only when its whole route is free, then complete it and `Release` the owner; after every round assert no segment has two owners; assert all shuttles finish within a bounded round count so a deadlock fails the assertion instead of hanging. The professor's toy finished four shuttles in five rounds — pick a bound that a 4-shuttle deadlock would blow past, and say why in a comment.
6. **Solution.** `dotnet sln src/ShuttleFleet.slnx add src/TrafficCore/TrafficCore.csproj src/TrafficCore.Tests/TrafficCore.Tests.csproj` — the solution goes from 14 to 16 projects, and the pure run from 7 to 8 `Passed!` lines.
7. **Run the phases in order.** The counts are gated, not admired (review 09 L2): Phase 3 expects `RC 0`, **8** `Passed!`, **0** `Failed!`, **79** tests, `TrafficCore.Tests` = 6, `ShuttleCore.Tests` = 25. Phase 4 scans **every** test project for docker launches (review 09 L1).

---

## Tier 3 — near-solution shapes

These are **shapes**, not the solution. You still write the bodies and the tests.

**Track shape**
```csharp
public sealed class Track
{
    public Track(Rack rack) { /* store the ordered lanes; segment id == lane id */ }
    public IReadOnlyList<string> SegmentIds { get; }
    public bool IsSegment(string segmentId) { /* membership, not a scanned list */ }
    public IReadOnlyList<string> Route(string fromLane, string toLane) { /* inclusive of both ends; unknown lane throws */ }
}
```

**Two-pass reservation shape**
```csharp
public bool TryReserveAll(string ownerId, IEnumerable<string> segmentIds)
{
    // pass 1: if any segment has a different owner, return false (write nothing)
    // pass 2: write them all, return true
}
```

**Controller shape**
```csharp
var route = _track.Route(fromLane, toLane);
var granted = _reservations.TryReserveAll(shuttleId, route);
return new TrafficDecision(granted, shuttleId, route, granted ? null : $"<segment> held by <owner>");
```

**Deadlock-rule reminder (say it out loud)**
> A waiting shuttle holds nothing. No hold-and-wait, so no circular wait, so no deadlock. Avoidance, not detection.

**Test names the acceptance walks**
```
Route_between_a_lane_and_itself_is_that_single_segment
Route_covers_every_segment_between_two_lanes_in_order
Route_to_an_unknown_lane_is_rejected
TryReserveRoute_grants_the_whole_route_when_every_segment_is_free
TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken
Release_frees_every_segment_the_owner_held
Two_shuttles_cannot_hold_the_same_segment
Head_on_requests_resolve_without_deadlock
Four_or_more_shuttles_never_share_a_segment_and_all_finish
```

**Suite gate (the container's command)**
```bash
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
# expects RC 0, 8 Passed!, 0 Failed!, 79 tests, TrafficCore.Tests=6, ShuttleCore.Tests=25
```

---

Stuck? These hints cover the key ideas without giving solution code. The acceptance criteria in the lesson file are the authoritative spec.