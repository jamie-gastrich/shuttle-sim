namespace ShuttleCore.Tests;

public class RejectionTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ShuttleTiming Timing =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));


    [Fact]
    public void StartMove_while_not_idle_is_rejected()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.Place("A01-L01-S01", "PLT-001");
        shuttle.StartLoad("A01-L01-S01", Start);

        Assert.Throws<ShuttleRejectedException>(() => shuttle.StartMove("A02-L02", Start.AddSeconds(1)));
    }

    [Fact]
    public void CompleteMove_before_travel_time_has_passed_is_rejected()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        shuttle.StartMove("A02-L02", Start);// Manhattan distance 2 lanes × 5 s = 10 s

        Assert.Throws<ShuttleRejectedException>(() => shuttle.CompleteMove(Start.AddSeconds(9)));
    }

    [Fact]
    public void StartLoad_from_a_different_lane_is_rejected()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);

        Assert.Throws<ShuttleRejectedException>(() => shuttle.StartLoad("A02-L02-S01", Start.AddSeconds(1)));
    }
    
    [Fact]
    public void StartUnload_into_an_occupied_slot_is_rejected()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.Place("A01-L01-S01", "PLT-001");
        rack.Place("A01-L01-S02", "PLT-002");
        shuttle.StartLoad("A01-L01-S01", Start);
        shuttle.CompleteLoad(Start.Add(Timing.Load));

        Assert.Throws<ShuttleRejectedException>(() => shuttle.StartUnload("A01-L01-S02", Start.Add(Timing.Load)));
    }

    [Fact]
    public void Events_after_a_fault_are_rejected()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        shuttle.RaiseFault("lost communication", Start);

        Assert.Throws<ShuttleRejectedException>(() => shuttle.StartMove("A02-L02", Start.AddSeconds(1)));
    }

    [Fact]
    public void Event_with_a_timestamp_before_the_previous_one_is_rejected()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        shuttle.StartMove("A02-L02", Start);

        Assert.Throws<ShuttleRejectedException>(() => shuttle.CompleteMove(Start.AddSeconds(-1)));
    }
}