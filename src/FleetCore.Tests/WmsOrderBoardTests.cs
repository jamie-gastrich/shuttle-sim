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
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);

        var result = board.Submit(Request(), Start);

        Assert.Equal(SubmitOutcome.Created, result.Outcome);
        Assert.Equal("received", result.Order.State);   // literal pin — kills a state rename
        Assert.Equal("ORD-1", result.Order.OrderId);
        Assert.Equal(Start, result.Order.ReceivedAt);
    }

    [Fact]
    public void Identical_resubmit_is_already_known_and_keeps_the_original_received_at()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        var first  = board.Submit(Request(), Start);
        var second = board.Submit(Request(), Start.AddMinutes(1));   // <-- different now

        Assert.Equal(SubmitOutcome.AlreadyKnown, second.Outcome);
        Assert.Equal(Start, second.Order.ReceivedAt);   // <-- NOT the second now
        Assert.Equal(first.Order, second.Order);        // the stored order, not a new one
    }

    [Fact]
    public void Same_order_id_with_a_different_lane_is_a_conflict()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(Request(), Start);

        var second = board.Submit(Request(laneId: "A02-L02"), Start);

        Assert.Equal(SubmitOutcome.Conflict, second.Outcome);
    }

    [Fact]
    public void Unknown_order_is_not_found()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);

        var found = board.TryGet("ORD-unknown", out var order);

        Assert.False(found);
        Assert.Null(order);
    }

    [Fact]
    public void First_submit_creates_exactly_one_queued_task()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);

        var result = board.Submit(Request(), Start);

        Assert.NotNull(result.CreatedTask);
        Assert.Equal("ORD-1", result.CreatedTask.TaskId);
        Assert.Equal(result.Order.LaneId, result.CreatedTask.ToLane);
        Assert.Equal(TaskState.Queued, result.CreatedTask.State);
        Assert.Equal(1, tasks.Count);
    }

    [Fact]
    public void Identical_resubmit_adds_no_second_task()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(Request(), Start);

        var second = board.Submit(Request(), Start.AddMinutes(1));

        Assert.Null(second.CreatedTask);
        Assert.Equal(1, tasks.Count);
    }

    [Fact]
    public void Conflict_adds_no_task()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(Request(), Start);

        var second = board.Submit(Request(laneId: "A02-L02"), Start);
        
        Assert.Null(second.CreatedTask);
        Assert.Equal(1, tasks.Count);
    }

    [Fact]
    public void A_second_order_adds_a_second_task()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        var first  = board.Submit(Request(), Start);
        var second = board.Submit(Request(orderId: "ORD-2", laneId: "A02-L02"), Start.AddMinutes(1));
        
        
        Assert.NotNull(first.CreatedTask);
        Assert.NotNull(second.CreatedTask);
        Assert.Equal(2, tasks.Count);
    }
}