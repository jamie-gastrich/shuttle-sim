namespace ShuttleCore.Tests;

public class TrackTests
{
    [Fact]
    public void Route_between_a_lane_and_itself_is_that_single_segment()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var track = new Track(rack);
        var lane = "A01-L01";

        var route = track.Route(lane, lane);

        Assert.Equal(new[] { lane }, route);
    }

    [Fact]
    public void Route_covers_every_segment_between_two_lanes_in_order()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var track = new Track(rack);

        var route = track.Route("A01-L01", "A01-L02");
        var crossAisleRoute = track.Route("A01-L02", "A02-L01");

        Assert.Equal(new[] { "A01-L01", "A01-L02" }, route);
        Assert.Equal(new[] { "A01-L02", "A02-L01" }, crossAisleRoute);
    }

    [Fact]
    public void Route_to_an_unknown_lane_is_rejected()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var track = new Track(rack);

        Assert.Throws<ArgumentException>(() => track.Route("A01-L01", "A99-L99"));
    }
}