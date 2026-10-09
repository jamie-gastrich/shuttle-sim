namespace ShuttleCore.Tests;

public class ScriptedMoveTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly ShuttleTiming Timing =
        new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));

    [Fact]
    public void Scripted_sequence_moves_a_pallet_from_one_slot_to_another()
    {
        var rack = new Rack(aisleCount: 2, lanesPerAisle: 2, slotsPerLane: 2);
        rack.Place("A01-L01-S01", "PLT-001");
        var shuttle = new Shuttle("SH-01", rack, "A01-L01", Timing);

        shuttle.StartLoad("A01-L01-S01", Start);
        shuttle.CompleteLoad(Start.Add(Timing.Load));
        shuttle.StartMove("A01-L02", Start.Add(Timing.Load));
        shuttle.CompleteMove(Start.Add(Timing.Load).Add(Timing.MovePerLane));
        var atSlot = Start.Add(Timing.Load).Add(Timing.MovePerLane).Add(Timing.Load);
        shuttle.StartUnload("A01-L02-S02", atSlot);
        shuttle.CompleteUnload(atSlot.Add(Timing.Unload));

        Assert.Equal("PLT-001", rack.PalletAt("A01-L02-S02"));
        Assert.Null(rack.PalletAt("A01-L01-S01"));
        Assert.Equal(ShuttleState.Idle, shuttle.State);
        Assert.Null(shuttle.CarriedPalletId);
    }
}