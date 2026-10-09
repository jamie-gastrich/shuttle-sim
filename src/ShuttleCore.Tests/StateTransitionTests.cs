namespace ShuttleCore.Tests;

public class StateTransitionTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ShuttleTiming Timing =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

    [Fact]
    public void StartMove_from_idle_enters_moving()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);

        shuttle.StartMove("A01-L02", Start);

        Assert.Equal(ShuttleState.Moving, shuttle.State);
    }

    [Fact]
    public void CompleteMove_from_moving_enters_idle()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);

        shuttle.StartMove("A01-L02", Start);
        shuttle.CompleteMove(Start.Add(Timing.MovePerLane));

        Assert.Equal(ShuttleState.Idle, shuttle.State);
    }

    [Fact]
    public void StartLoad_from_idle_enters_loading()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.Place("A01-L01-S01", "PLT-001");

        shuttle.StartLoad("A01-L01-S01", Start);

        Assert.Equal(ShuttleState.Loading, shuttle.State);
    }

    [Fact]
    public void CompleteLoad_from_loading_enters_idle()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.Place("A01-L01-S01", "PLT-001");

        shuttle.StartLoad("A01-L01-S01", Start);
        shuttle.CompleteLoad(Start.Add(Timing.Load));

        Assert.Equal(ShuttleState.Idle, shuttle.State);
    }

    [Fact]
    public void StartUnload_from_idle_enters_unloading()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.Place("A01-L01-S01", "PLT-001");

        shuttle.StartLoad("A01-L01-S01", Start);
        shuttle.CompleteLoad(Start.Add(Timing.Load));
        shuttle.StartUnload("A01-L01-S01", Start.Add(Timing.Load));

        Assert.Equal(ShuttleState.Unloading, shuttle.State);
    }

    [Fact]
    public void CompleteUnload_from_unloading_enters_idle()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.Place("A01-L01-S01", "PLT-001");

        shuttle.StartLoad("A01-L01-S01", Start);
        shuttle.CompleteLoad(Start.Add(Timing.Load));
        shuttle.StartUnload("A01-L01-S01", Start.Add(Timing.Load));
        shuttle.CompleteUnload(Start.Add(Timing.Load).Add(Timing.Unload));

        Assert.Equal(ShuttleState.Idle, shuttle.State);
    }

    [Fact]
    public void RaiseFault_from_idle_enters_fault()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        var reason = "lost communication";

        shuttle.RaiseFault(reason, Start);

        Assert.Equal(ShuttleState.Fault, shuttle.State);
    }

    [Fact]
    public void RaiseFault_from_moving_enters_fault()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        var reason = "blocked path";

        shuttle.StartMove("A01-L02", Start);
        shuttle.RaiseFault(reason, Start);

        Assert.Equal(ShuttleState.Fault, shuttle.State);
    }

    [Fact]
    public void RaiseFault_from_loading_enters_fault()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.Place("A01-L01-S01", "PLT-001");
        var reason = "pallet not detected";

        shuttle.StartLoad("A01-L01-S01", Start);
        shuttle.RaiseFault(reason, Start);

        Assert.Equal(ShuttleState.Fault, shuttle.State);
    }

    [Fact]
    public void RaiseFault_from_unloading_enters_fault()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);
        rack.Place("A01-L01-S01", "PLT-001");
        var reason = "slot is not empty";

        shuttle.StartLoad("A01-L01-S01", Start);
        shuttle.CompleteLoad(Start.Add(Timing.Load));
        shuttle.StartUnload("A01-L01-S01", Start.Add(Timing.Load));
        shuttle.RaiseFault(reason, Start.Add(Timing.Load));

        Assert.Equal(ShuttleState.Fault, shuttle.State);
    }
}