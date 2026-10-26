using ShuttleCore;
using TrafficCore;
using FleetCore;

namespace FleetManager.Tests;

public class RunLoopGateTests
{
    [Fact]
    public void Parked_shuttle_blocks_route_through_its_lane()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var traffic = new TrafficController(track, new SegmentReservations());
        var occupancy = new OccupancyMap();
        occupancy.SetOccupiedLane("Shuttle-01", "A01-L01");
        occupancy.SetOccupiedLane("Shuttle-02", "A01-L03");
        var gate = new RunLoopGate(occupancy, track, traffic);

        var canMove = gate.CanMove("Shuttle-01", "A01-L06");

        Assert.False(canMove);
    }

    [Fact]
    public void Route_is_clear_after_occupied_shuttle_moves()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var traffic = new TrafficController(track, new SegmentReservations());
        var occupancy = new OccupancyMap();
        occupancy.SetOccupiedLane("Shuttle-01", "A01-L01");
        occupancy.SetOccupiedLane("Shuttle-02", "A01-L06");
        var gate = new RunLoopGate(occupancy, track, traffic);

        var canMove = gate.CanMove("Shuttle-01", "A01-L05");

        Assert.True(canMove);
    }

    [Fact]
    public void Gate_allows_route_when_no_occupancy_conflict()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var traffic = new TrafficController(track, new SegmentReservations());
        var occupancy = new OccupancyMap();
        occupancy.SetOccupiedLane("Shuttle-01", "A01-L01");
        var gate = new RunLoopGate(occupancy, track, traffic);

        var canMove = gate.CanMove("Shuttle-01", "A01-L05");

        Assert.True(canMove);
    }

    [Fact]
    public void Gate_refuses_when_transit_reservation_conflicts()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var traffic = new TrafficController(track, new SegmentReservations());
        var occupancy = new OccupancyMap();
        occupancy.SetOccupiedLane("Shuttle-01", "A01-L01");
        var gate = new RunLoopGate(occupancy, track, traffic);

        var routeDecision = traffic.TryReserveRoute("Shuttle-02", "A01-L02", "A01-L04");
        Assert.True(routeDecision.Granted);

        var canMove = gate.CanMove("Shuttle-01", "A01-L05");

        Assert.False(canMove);
    }

    [Fact]
    public void CanMove_returns_false_when_paused()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var traffic = new TrafficController(track, new SegmentReservations());
        var occupancy = new OccupancyMap();
        occupancy.SetOccupiedLane("SH-01", "A01-L01");
        var gate = new RunLoopGate(occupancy, track, traffic);
        gate.Pause("SH-01");

        var canMove = gate.CanMove("SH-01", "A01-L02");

        Assert.False(canMove);
        gate.Resume("SH-01");
        Assert.True(gate.CanMove("SH-01", "A01-L02"));
    }

    [Fact]
    public void CommitMove_returns_false_when_paused()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var reservations = new SegmentReservations();
        var traffic = new TrafficController(track, reservations);
        var occupancy = new OccupancyMap();
        occupancy.SetOccupiedLane("SH-01", "A01-L01");
        var gate = new RunLoopGate(occupancy, track, traffic);
        gate.Pause("SH-01");

        Assert.False(gate.CommitMove("SH-01", "A01-L02"));
        Assert.Empty(reservations.SegmentsOwnedBy("SH-01"));
    }

    [Fact]
    public void CanMove_returns_true_after_resume()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var traffic = new TrafficController(track, new SegmentReservations());
        var occupancy = new OccupancyMap();
        occupancy.SetOccupiedLane("SH-01", "A01-L01");
        var gate = new RunLoopGate(occupancy, track, traffic);
        gate.Pause("SH-01");

        gate.Resume("SH-01");

        Assert.True(gate.CanMove("SH-01", "A01-L05"));
    }

    [Fact]
    public void PauseAll_pauses_every_listed_shuttle()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var traffic = new TrafficController(track, new SegmentReservations());
        var gate = new RunLoopGate(new OccupancyMap(), track, traffic);
        var shuttleIds = new[] { "SH-01", "SH-02", "SH-03" };

        gate.PauseAll(shuttleIds);

        Assert.All(shuttleIds, shuttleId => Assert.True(gate.IsPaused(shuttleId)));

        gate.ResumeAll(shuttleIds);

        Assert.All(shuttleIds, shuttleId => Assert.False(gate.IsPaused(shuttleId)));
    }

    [Fact]
    public void CanMove_returns_false_when_paused_via_shared_state()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var traffic = new TrafficController(track, new SegmentReservations());
        var occupancy = new OccupancyMap();
        occupancy.SetOccupiedLane("SH-01", "A01-L01");
        var pauseState = new PauseState();
        var gate = new RunLoopGate(occupancy, track, traffic, pauseState);
        pauseState.Pause("SH-01");

        Assert.False(gate.CanMove("SH-01", "A01-L02"));
    }

    [Fact]
    public void Shared_state_reflects_pause_made_through_gate()
    {
        var track = new Track(new Rack(aisleCount: 1, lanesPerAisle: 6, slotsPerLane: 1));
        var traffic = new TrafficController(track, new SegmentReservations());
        var pauseState = new PauseState();
        var gate = new RunLoopGate(new OccupancyMap(), track, traffic, pauseState);

        gate.Pause("SH-01");

        Assert.True(pauseState.IsPaused("SH-01"));
    }
}