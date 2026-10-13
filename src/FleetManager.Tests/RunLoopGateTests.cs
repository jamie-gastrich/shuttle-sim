using ShuttleCore;
using TrafficCore;

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
}