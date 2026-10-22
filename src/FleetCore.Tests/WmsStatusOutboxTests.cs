namespace FleetCore.Tests;

public class WmsStatusOutboxTests
{
    [Fact]
    public void NoteSync_Advanced_marks_the_order_pending()
    {
        var outbox = new WmsStatusOutbox();

        outbox.NoteSync("ORD-1", OrderSyncOutcome.Advanced);
        Assert.Equal(1, outbox.PendingCount);

        var claimed = outbox.Claim();

        Assert.Equal(new[] { "ORD-1" }, claimed);
        Assert.Equal(0, outbox.PendingCount);
    }
    
    [Fact]
    public void NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing()
    {
        var outbox = new WmsStatusOutbox();

        outbox.NoteSync("ORD-1", OrderSyncOutcome.AlreadyThere);
        outbox.NoteSync("ORD-2", OrderSyncOutcome.NoOrder);

        Assert.Equal(0, outbox.PendingCount);
        Assert.Empty(outbox.Claim());
    }
    
    [Fact]
    public void NoteSync_twice_marks_once()
    {
        var outbox = new WmsStatusOutbox();
        
        outbox.NoteSync("ORD-1", OrderSyncOutcome.Advanced);
        outbox.NoteSync("ORD-1", OrderSyncOutcome.Advanced);
        
        var claimed = outbox.Claim();

        Assert.Single(claimed);
        Assert.Equal("ORD-1", claimed[0]);
    }
}