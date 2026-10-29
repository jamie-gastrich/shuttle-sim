namespace ShuttleCore.Tests;

public class RackTests
{
    [Fact]
    public void Rack_builds_every_lane_and_slot_from_its_dimensions()
    {
        
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);

        Assert.Equal(2, rack.AisleCount);
        Assert.Equal(2, rack.LanesPerAisle);
        Assert.Equal(2, rack.SlotsPerLane);
        Assert.True(rack.IsLane("A01-L01"));
        Assert.True(rack.IsSlot("A01-L01-S01"));
        Assert.True(rack.IsSlot("A01-L01-S02"));
        Assert.True(rack.IsLane("A01-L02"));
        Assert.True(rack.IsSlot("A01-L02-S01"));
        Assert.True(rack.IsSlot("A01-L02-S02"));
        Assert.True(rack.IsLane("A02-L01"));
        Assert.True(rack.IsSlot("A02-L01-S01"));
        Assert.True(rack.IsSlot("A02-L01-S02"));
        Assert.True(rack.IsLane("A02-L02"));
        Assert.True(rack.IsSlot("A02-L02-S01"));
        Assert.True(rack.IsSlot("A02-L02-S02"));
    }
    
    [Fact]
    public void Lane_distance_counts_lanes_within_an_aisle_then_aisles()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);

        Assert.Equal(1, rack.DistanceInLanes("A01-L01", "A01-L02"));
        Assert.Equal(3, rack.DistanceInLanes("A01-L01", "A02-L02"));
    }
    
    [Fact]
    public void Placing_a_pallet_marks_the_slot_occupied()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        rack.Place("A01-L01-S01", "PLT-001");
        Assert.True(rack.IsOccupied("A01-L01-S01"));
        Assert.Equal("PLT-001", rack.PalletAt("A01-L01-S01"));
    }
    
    [Fact]
    public void Taking_a_pallet_leaves_the_slot_empty()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        rack.Place("A01-L01-S01", "PLT-001");
        rack.Take("A01-L01-S01");
        Assert.False(rack.IsOccupied("A01-L01-S01"));   
        Assert.Null(rack.PalletAt("A01-L01-S01"));
    }

    [Fact]
    public void IsRouteBlocked_is_false_when_no_lane_on_the_route_is_blocked()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        rack.MarkBlocked("A02-L01");
        Assert.False(rack.IsRouteBlocked("A01-L01", "A01-L02"));
    }

    [Fact]
    public void MarkBlocked_for_an_unknown_lane_is_rejected()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        Assert.Throws<ArgumentException>(() => rack.MarkBlocked("A01-L03"));
    }

    [Fact]
    public void UnmarkBlocked_removes_a_previously_blocked_lane()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        rack.MarkBlocked("A01-L02");
        rack.UnmarkBlocked("A01-L02");
        Assert.False(rack.IsBlocked("A01-L02"));
    }

    [Fact]
    public void UnmarkBlocked_for_an_unknown_lane_is_rejected()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        Assert.Throws<ArgumentException>(() => rack.UnmarkBlocked("A01-L03"));
    }
}
