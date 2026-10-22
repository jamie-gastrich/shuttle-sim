using System.Text.Json;
using Contracts;
using FleetCore;
using Messaging;

namespace FleetManager.Tests;

internal sealed class FakeCommandSender : ICommandSender
{
    public List<MoveCommandMessage> Commands { get; } = new();
    public CommandSendStatus Status { get; set; } = CommandSendStatus.Acknowledged;

    public Task<CommandSendResult> SendAsync(MoveCommandMessage command, CancellationToken cancellationToken)
    {
        Commands.Add(command);
        return Task.FromResult(new CommandSendResult(Status, 1, null));
    }
}

public class TaskExecutionLoopTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static MessageReceived Status(string shuttleId, string state, string position) =>
        new($"shuttle/{shuttleId}/status", JsonSerializer.Serialize(
            new ShuttleStatusMessage(
                new MessageEnvelope(ContractVersion.Current, MessageTypes.ShuttleStatus,
                    Guid.NewGuid().ToString(), null, shuttleId, Start),
                new ShuttleStatus(shuttleId, state, position, Start, null))));

    [Fact]
    public async Task The_tick_completes_a_task_when_the_shuttle_is_idle_at_the_lane()
    {
        var tasks = new TaskBoard();
        var orders = new WmsOrderBoard(tasks);
        var outbox = new WmsStatusOutbox();
        var orderSync = new OrderSync(orders, outbox);
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(tasks, repository, orderSync);
        var submit = orders.Submit(
            new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PAL-1", "A01-L07"), Start);
        var task = submit.CreatedTask!;

        var registry = new ShuttleRegistry();
        var tick = new TaskExecutionLoop(dispatcher, registry, new FakeCommandSender(),
            "fleet-manager", TimeSpan.FromSeconds(1), () => Start);

        // Let the tick give the task to SH-01 so it is working with that shuttle.
        await tick.HandleStatusAsync(Status("SH-01", "idle", "A01-L01"), CancellationToken.None);
        await tick.TickAsync(CancellationToken.None);
        Assert.Equal(TaskState.InProgress, task.State);

        // Idle, but at the wrong lane: the task is not finished.
        await tick.HandleStatusAsync(Status("SH-01", "idle", "A01-L03"), CancellationToken.None);
        await tick.TickAsync(CancellationToken.None);
        Assert.Equal(TaskState.InProgress, task.State);

        // Idle at the task's lane: the task is finished.
        await tick.HandleStatusAsync(Status("SH-01", "idle", "A01-L07"), CancellationToken.None);
        await tick.TickAsync(CancellationToken.None);
        Assert.Equal(TaskState.Done, task.State);
    }

    [Fact]
    public async Task The_tick_gives_a_queued_task_to_a_free_shuttle()
    {
        var tasks = new TaskBoard();
        var orders = new WmsOrderBoard(tasks);
        var outbox = new WmsStatusOutbox();
        var orderSync = new OrderSync(orders, outbox);
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(tasks, repository, orderSync);
        var submit = orders.Submit(
            new SubmitOrderRequest("ORD-1", WmsOrderKinds.Store, "PAL-1", "A01-L07"), Start);
        var task = submit.CreatedTask!;

        var registry = new ShuttleRegistry();
        var sender = new FakeCommandSender();
        var tick = new TaskExecutionLoop(dispatcher, registry, sender,
            "fleet-manager", TimeSpan.FromSeconds(1), () => Start);

        // A shuttle the picture has never heard from gets no work.
        await tick.TickAsync(CancellationToken.None);
        Assert.Equal(TaskState.Queued, task.State);
        Assert.Empty(sender.Commands);

        await tick.HandleStatusAsync(Status("SH-01", "idle", "A01-L01"), CancellationToken.None);
        await tick.TickAsync(CancellationToken.None);

        Assert.Equal(TaskState.InProgress, task.State);
        var command = Assert.Single(sender.Commands);
        Assert.Equal("SH-01", command.ShuttleId);
        Assert.Equal("A01-L07", command.ToLane);
    }
}
