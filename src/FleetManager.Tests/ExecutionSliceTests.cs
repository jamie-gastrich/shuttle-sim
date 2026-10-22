using System.Text.Json;
using FleetCore;

namespace FleetManager.Tests;

public sealed class ExecutionSliceTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Dispatching_a_task_moves_the_order_and_owes_a_word()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        var outbox = new WmsStatusOutbox();
        var orderSync = new OrderSync(board, outbox);
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(tasks, repository, orderSync);
        var submit = board.Submit(
            new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PAL-1", "A01-L07"), Start);
        var task = submit.CreatedTask!;

        await dispatcher.AssignNextAsync("SH-01", Start, CancellationToken.None);
        await dispatcher.StartAsync(task.TaskId, Start, CancellationToken.None);
        Assert.True(board.TryGet("ORD-1", out var started));
        Assert.Equal(WmsOrderStates.InProgress, started.State);
        Assert.Equal(1, outbox.PendingCount);

        await dispatcher.CompleteAsync(task.TaskId, Start, CancellationToken.None);

        Assert.Equal(TaskState.Done, task.State);
        Assert.True(board.TryGet("ORD-1", out var completed));
        Assert.Equal(WmsOrderStates.Done, completed.State);
        Assert.Equal(1, outbox.PendingCount);
    }

    [Fact]
    public async Task The_newest_word_wins_the_wms_hears_done_not_in_progress()
    {
        var tasks = new TaskBoard();
        var board = new WmsOrderBoard(tasks);
        var outbox = new WmsStatusOutbox();
        var orderSync = new OrderSync(board, outbox);
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(tasks, repository, orderSync);
        var submit = board.Submit(
            new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PAL-1", "A01-L07"), Start);
        var task = submit.CreatedTask!;

        // Both moves happen before the sender goes, so the pile holds one id, not two words.
        await dispatcher.AssignNextAsync("SH-01", Start, CancellationToken.None);
        await dispatcher.StartAsync(task.TaskId, Start, CancellationToken.None);
        Assert.True(board.TryGet("ORD-1", out var started));
        Assert.Equal(WmsOrderStates.InProgress, started.State);
        Assert.Equal(1, outbox.PendingCount);

        await dispatcher.CompleteAsync(task.TaskId, Start, CancellationToken.None);
        Assert.True(board.TryGet("ORD-1", out var completed));
        Assert.Equal(WmsOrderStates.Done, completed.State);
        Assert.Equal(1, outbox.PendingCount);

        var stub = new StubHandler();
        using var client = new HttpClient(stub) { BaseAddress = new Uri("http://wms.test") };
        var pusher = new WmsStatusPusher(board, outbox, client, TimeSpan.FromSeconds(1));

        await pusher.SendPendingAsync(CancellationToken.None);

        var body = Assert.Single(stub.Bodies);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("done", document.RootElement.GetProperty("state").GetString());
        Assert.Equal("ORD-1", document.RootElement.GetProperty("orderId").GetString());
        Assert.Equal(0, outbox.PendingCount);
    }
}
