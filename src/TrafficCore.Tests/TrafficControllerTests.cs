using ShuttleCore;

namespace TrafficCore.Tests;

public class TrafficControllerTests
{
    [Fact]
    public void TryReserveRoute_grants_the_whole_route_when_every_segment_is_free()
    {
        var rack = new Rack(aisleCount: 1, lanesPerAisle: 2, slotsPerLane: 2);
        var controller = new TrafficController(new Track(rack), new SegmentReservations());

        var decision = controller.TryReserveRoute("Shuttle1", "A01-L01", "A01-L02");

        Assert.True(decision.Granted);
        Assert.Equal("Shuttle1", decision.ShuttleId);
        Assert.Equal(new[] { "A01-L01", "A01-L02" }, decision.Segments);
        Assert.Null(decision.Reason);
    }

    [Fact]
    public void TryReserveRoute_refuses_and_holds_nothing_when_any_segment_is_taken()
    {
        var rack = new Rack(aisleCount: 1, lanesPerAisle: 2, slotsPerLane: 2);
        var reservations = new SegmentReservations();
        var controller = new TrafficController(new Track(rack), reservations);

        var firstDecision = controller.TryReserveRoute("Shuttle1", "A01-L01", "A01-L02");
        var secondDecision = controller.TryReserveRoute("Shuttle2", "A01-L01", "A01-L02");

        Assert.True(firstDecision.Granted);
        Assert.False(secondDecision.Granted);
        Assert.Empty(reservations.SegmentsOwnedBy("Shuttle2"));
        Assert.Contains("held by Shuttle1", secondDecision.Reason);
    }

    [Fact]
    public void Release_frees_every_segment_the_owner_held()
    {
        var rack = new Rack(aisleCount: 1, lanesPerAisle: 2, slotsPerLane: 2);
        var controller = new TrafficController(new Track(rack), new SegmentReservations());

        controller.TryReserveRoute("Shuttle1", "A01-L01", "A01-L02");
        controller.Release("Shuttle1");

        var decision = controller.TryReserveRoute("Shuttle2", "A01-L01", "A01-L02");

        Assert.True(decision.Granted);
    }

    [Fact]
    public void Two_shuttles_cannot_hold_the_same_segment()
    {
        var rack = new Rack(aisleCount: 1, lanesPerAisle: 3, slotsPerLane: 2);
        var controller = new TrafficController(new Track(rack), new SegmentReservations());

        var firstDecision = controller.TryReserveRoute("Shuttle1", "A01-L01", "A01-L02");
        var secondDecision = controller.TryReserveRoute("Shuttle2", "A01-L02", "A01-L03");

        Assert.True(firstDecision.Granted);
        Assert.False(secondDecision.Granted);
        Assert.Contains("A01-L02 held by Shuttle1", secondDecision.Reason);
    }

    [Fact]
    public void Head_on_requests_resolve_without_deadlock()
    {
        var rack = new Rack(aisleCount: 1, lanesPerAisle: 3, slotsPerLane: 2);
        var controller = new TrafficController(new Track(rack), new SegmentReservations());

        var firstDecision = controller.TryReserveRoute("Shuttle1", "A01-L01", "A01-L02");
        var secondDecision = controller.TryReserveRoute("Shuttle2", "A01-L02", "A01-L03");

        Assert.True(firstDecision.Granted);
        Assert.False(secondDecision.Granted);
    }

    [Fact]
    public void Four_or_more_shuttles_never_share_a_segment_and_all_finish()
    {
        var rack = new Rack(aisleCount: 1, lanesPerAisle: 8, slotsPerLane: 2);
        var controller = new TrafficController(new Track(rack), new SegmentReservations());
        var shuttleIds = new[] { "Shuttle1", "Shuttle2", "Shuttle3", "Shuttle4" };
        var routePairs = new[]
        {
            ("A01-L01", "A01-L02"),
            ("A01-L03", "A01-L04"),
            ("A01-L05", "A01-L06"),
            ("A01-L07", "A01-L08")
        };

        var decisions = shuttleIds
            .Zip(routePairs)
            .Select(pair => controller.TryReserveRoute(pair.First, pair.Second.Item1, pair.Second.Item2))
            .ToList();

        Assert.All(decisions, decision => Assert.True(decision.Granted));
        Assert.Equal(8, decisions.SelectMany(d => d.Segments).Distinct().Count());
    }
}