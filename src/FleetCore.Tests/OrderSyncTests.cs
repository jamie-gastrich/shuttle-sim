namespace FleetCore.Tests;

public class OrderSyncTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OrderSync_marks_the_word_when_the_task_moved()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        var outbox = new WmsStatusOutbox();
        var orderSync = new OrderSync(board, outbox);

        var submit = board.Submit(
            new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PAL-1", "A01-L07"), Start);
        var task = submit.CreatedTask!;
        task.Assign("SH-01", Start);
        task.Start(Start);

        var outcome = orderSync.Note(task.TaskId);

        Assert.Equal(OrderSyncOutcome.Advanced, outcome);
        Assert.Equal(1, outbox.PendingCount);
        Assert.True(board.TryGet("ORD-1", out var order));
        Assert.Equal(WmsOrderStates.InProgress, order.State);
    }

    [Fact]
    public void OrderSync_marks_nothing_when_the_word_did_not_move()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        var outbox = new WmsStatusOutbox();
        var orderSync = new OrderSync(board, outbox);
        var submit = board.Submit(
            new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PAL-1", "A01-L07"), Start);
        var task = submit.CreatedTask!;

        task.Assign("SH-01", Start);
        var outcome = orderSync.Note(task.TaskId);

        Assert.Equal(OrderSyncOutcome.AlreadyThere, outcome);
        Assert.Equal(0, outbox.PendingCount);
    }

    [Fact]
    public void OrderSync_of_a_task_without_an_order_marks_nothing()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        var outbox = new WmsStatusOutbox();
        var orderSync = new OrderSync(board, outbox);
        tasks.Add(new FleetTask("TASK-9", "A01-L07", Start));

        var outcome = orderSync.Note("TASK-9");

        Assert.Equal(OrderSyncOutcome.NoOrder, outcome);
        Assert.Equal(0, outbox.PendingCount);
    }
}
