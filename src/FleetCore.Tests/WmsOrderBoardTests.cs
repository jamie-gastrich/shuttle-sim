namespace FleetCore.Tests;

public class WmsOrderBoardTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SubmitOrderRequest Request(
        string orderId = "ORD-1", string laneId = "A01-L01") =>
        new(orderId, WmsOrderKinds.Store, "PLT-001", laneId);

    [Fact]
    public void First_submit_is_created_and_received()
    {
        var board = new WmsOrderBoard();

        var result = board.Submit(Request(), Start);

        Assert.Equal(SubmitOutcome.Created, result.Outcome);
        Assert.Equal("received", result.Order.State);   // literal pin — kills a state rename
        Assert.Equal("ORD-1", result.Order.OrderId);
        Assert.Equal(Start, result.Order.ReceivedAt);
    }

    [Fact]
    public void Identical_resubmit_is_already_known_and_keeps_the_original_received_at()
    {
        var board = new WmsOrderBoard();
        var first  = board.Submit(Request(), Start);
        var second = board.Submit(Request(), Start.AddMinutes(1));   // <-- different now

        Assert.Equal(SubmitOutcome.AlreadyKnown, second.Outcome);
        Assert.Equal(Start, second.Order.ReceivedAt);   // <-- NOT the second now
        Assert.Equal(first.Order, second.Order);        // the stored order, not a new one
    }

    [Fact]
    public void Same_order_id_with_a_different_lane_is_a_conflict()
    {
        var board = new WmsOrderBoard();
        board.Submit(Request(), Start);

        var second = board.Submit(Request(laneId: "A02-L02"), Start);

        Assert.Equal(SubmitOutcome.Conflict, second.Outcome);
    }

    [Fact]
    public void Unknown_order_is_not_found()
    {
        var board = new WmsOrderBoard();

        var found = board.TryGet("ORD-unknown", out var order);

        Assert.False(found);
        Assert.Null(order);
    }
}