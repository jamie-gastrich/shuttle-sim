namespace MockWms.Tests;

public class MockWmsLedgerTests
{
    private static MockOrder Order(string state = "in_progress", string palletId = "PLT-001")
        => new("ORD-1", "store", palletId, "A01-L01", state, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void A_first_word_is_applied()
    {
        var ledger = new MockWmsLedger();

        Assert.Equal(MockWmsApplyResult.Applied, ledger.Apply(Order()));
        Assert.Equal(Order(), Assert.Single(ledger.Snapshot()));
        Assert.Equal(new MockWmsLedgerCounts(1, 0, 0, 0), ledger.Counts());
    }

    [Fact]
    public void The_same_word_twice_is_a_duplicate()
    {
        var ledger = new MockWmsLedger();

        ledger.Apply(Order());

        Assert.Equal(MockWmsApplyResult.Duplicate, ledger.Apply(Order()));
        Assert.Equal(new MockWmsLedgerCounts(1, 1, 0, 0), ledger.Counts());
    }

    [Fact]
    public void An_older_word_does_not_replace_a_newer_one()
    {
        var ledger = new MockWmsLedger();

        ledger.Apply(Order("done"));

        Assert.Equal(MockWmsApplyResult.Stale, ledger.Apply(Order("in_progress")));
        Assert.Equal("done", Assert.Single(ledger.Snapshot()).State);
        Assert.Equal(new MockWmsLedgerCounts(1, 0, 1, 0), ledger.Counts());
    }

    [Fact]
    public void A_different_pallet_for_the_same_order_is_a_conflict()
    {
        var ledger = new MockWmsLedger();
        ledger.Apply(Order());

        Assert.Equal(MockWmsApplyResult.Conflict, ledger.Apply(Order(palletId: "PLT-002")));
        Assert.Equal(new MockWmsLedgerCounts(1, 0, 0, 1), ledger.Counts());
    }

    [Fact]
    public void Cancelled_is_above_done()
    {
        var ledger = new MockWmsLedger();
        ledger.Apply(Order("done"));

        Assert.Equal(MockWmsApplyResult.Applied, ledger.Apply(Order("cancelled")));
        Assert.Equal("cancelled", Assert.Single(ledger.Snapshot()).State);
    }

    [Fact]
    public void A_done_word_is_not_replaced_by_failed()
    {
        var ledger = new MockWmsLedger();
        ledger.Apply(Order("done"));

        Assert.Equal(MockWmsApplyResult.Stale, ledger.Apply(Order("failed")));
        Assert.Equal("done", Assert.Single(ledger.Snapshot()).State);
    }
}
