namespace ShuttleCore.Tests;

public class RecoveryTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ShuttleTiming Timing =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

    [Fact]
    public void Recover_clears_the_blocked_lane_that_caused_the_fault()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.MarkBlocked("A01-L02");

        shuttle.StartMove("A01-L02", Start);
        shuttle.Recover(Start.AddSeconds(1));

        Assert.False(rack.IsBlocked("A01-L02"));
    }

    [Fact]
    public void Recover_does_not_clear_an_unrelated_blocked_lane()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.MarkBlocked("A01-L02");
        rack.MarkBlocked("A02-L01");

        shuttle.StartMove("A01-L02", Start);
        shuttle.Recover(Start.AddSeconds(1));

        Assert.False(rack.IsBlocked("A01-L02"));
        Assert.True(rack.IsBlocked("A02-L01"));
    }

    [Fact]
    public void Recover_for_a_non_blocked_fault_unmarks_nothing()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.MarkBlocked("A01-L02");

        shuttle.RaiseFault("lost communication", Start);
        shuttle.Recover(Start.AddSeconds(1));

        Assert.True(rack.IsBlocked("A01-L02"));
    }
}
