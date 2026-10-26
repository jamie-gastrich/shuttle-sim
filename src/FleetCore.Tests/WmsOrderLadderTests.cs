namespace FleetCore.Tests;

public class WmsOrderLadderTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static SubmitOrderRequest Request(
        string orderId = "ORD-1", string laneId = "A01-L01") =>
        new(orderId, WmsOrderKinds.Store, "PLT-001", laneId);

    private static string StateOf(WmsOrderBoard board, string orderId)
    {
        Assert.True(board.TryGet(orderId, out var order));
        Assert.NotNull(order);
        return order.State;
    }

    [Fact]
    public void Ladder_maps_every_task_state_to_its_order_word()
    {
        Assert.Equal("received", WmsOrderLadder.ForTask(TaskState.Queued));
        Assert.Equal("received", WmsOrderLadder.ForTask(TaskState.Assigned));
        Assert.Equal("in_progress", WmsOrderLadder.ForTask(TaskState.InProgress));
        Assert.Equal("in_progress", WmsOrderLadder.ForTask(TaskState.Paused));
        Assert.Equal("done", WmsOrderLadder.ForTask(TaskState.Done));
        Assert.Equal("failed", WmsOrderLadder.ForTask(TaskState.Failed));
        Assert.Equal("cancelled", WmsOrderLadder.ForTask(TaskState.Cancelled));
    }

    [Fact]
    public void Ladder_sync_on_cancel_advances_order()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(Request(), Start);
        var task = tasks.Get("ORD-1");

        task.Assign("SHUTTLE-1", Start);
        task.Start(Start.AddSeconds(1));
        task.Cancel(Start.AddSeconds(2));

        var result = board.SyncFromTask("ORD-1");

        Assert.Equal(OrderSyncOutcome.Advanced, result);
        Assert.Equal("cancelled", StateOf(board, "ORD-1"));
    }

    [Fact]
    public void Sync_after_accept_says_AlreadyThere_and_changes_nothing()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(Request(), Start);

        var result = board.SyncFromTask("ORD-1");

        Assert.Equal(OrderSyncOutcome.AlreadyThere, result);
        Assert.Equal("received", StateOf(board, "ORD-1"));
    }

    [Fact]
    public void Sync_walks_the_order_to_in_progress_then_done()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(Request(), Start);
        var task = tasks.Get("ORD-1");

        task.Assign("SHUTTLE-1", Start);
        Assert.Equal(OrderSyncOutcome.AlreadyThere, board.SyncFromTask("ORD-1"));
        Assert.Equal("received", StateOf(board, "ORD-1"));

        task.Start(Start);
        Assert.Equal(OrderSyncOutcome.Advanced, board.SyncFromTask("ORD-1"));
        Assert.Equal("in_progress", StateOf(board, "ORD-1"));

        task.Complete(Start);
        Assert.Equal(OrderSyncOutcome.Advanced, board.SyncFromTask("ORD-1"));
        Assert.Equal("done", StateOf(board, "ORD-1"));
    }

    [Fact]
    public void Sync_after_a_requeue_keeps_the_order_received()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(Request(), Start);
        var task = tasks.Get("ORD-1");

        task.Assign("SHUTTLE-1", Start);
        task.Requeue(Start);
        Assert.Equal(OrderSyncOutcome.AlreadyThere, board.SyncFromTask("ORD-1"));
        Assert.Equal("received", StateOf(board, "ORD-1"));
    }

    [Fact]
    public void Sync_of_a_failed_task_marks_the_order_failed()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        board.Submit(Request(), Start);
        var task = tasks.Get("ORD-1");

        task.Fail("route lost", Start);
        Assert.Equal(OrderSyncOutcome.Advanced, board.SyncFromTask("ORD-1"));
        Assert.Equal("failed", StateOf(board, "ORD-1"));
    }

    [Fact]
    public void Sync_of_a_task_with_no_order_returns_NoOrder()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);

        tasks.Add(new FleetTask("LOCAL-7", "A01-L01", Start));
        Assert.Equal(OrderSyncOutcome.NoOrder, board.SyncFromTask("LOCAL-7"));

        Assert.False(board.TryGet("LOCAL-7", out var order));
        Assert.Null(order);
        Assert.Equal(OrderSyncOutcome.NoOrder, board.SyncFromTask("never-existed"));
    }

}