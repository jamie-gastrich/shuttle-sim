using FleetCore;

namespace FleetManager.Tests;

public class TaskDispatcherTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AssignNextAsync_assigns_the_oldest_queued_task_and_saves_it()
    {
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(board, repository);
        var oldestTask = new FleetTask("task-oldest", "A01-L01", Start);
        var newerTask = new FleetTask("task-newer", "A01-L02", Start.AddSeconds(1));
        board.Add(oldestTask);
        board.Add(newerTask);

        var task = await dispatcher.AssignNextAsync("SH-01", Start.AddMinutes(1), CancellationToken.None);

        Assert.NotNull(task);
        Assert.Equal(oldestTask.TaskId, task.TaskId);
        Assert.Equal("SH-01", task.AssignedShuttleId);
        Assert.Equal(1, repository.SaveCount);
        Assert.Equal(task.TaskId, (await repository.GetAsync(task.TaskId, CancellationToken.None))!.TaskId);
    }

    [Fact]
    public async Task AssignNextAsync_returns_null_when_nothing_is_queued()
    {
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(board, repository);

        var task = await dispatcher.AssignNextAsync("SH-01", Start, CancellationToken.None);

        Assert.Null(task);
        Assert.Equal(0, repository.SaveCount);
    }

    [Fact]
    public async Task A_rejected_transition_throws_and_saves_nothing()
    {
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(board, repository);
        var task = new FleetTask("task-1", "A01-L01", Start);
        board.Add(task);
        await dispatcher.AssignNextAsync("SH-01", Start.AddSeconds(1), CancellationToken.None);
        var savesBeforeRejectedTransition = repository.SaveCount;

        await Assert.ThrowsAsync<TaskRejectedException>(() =>
            dispatcher.CompleteAsync(task.TaskId, Start.AddSeconds(2), CancellationToken.None));

        Assert.Equal(savesBeforeRejectedTransition, repository.SaveCount);
        Assert.Equal(TaskState.Assigned, task.State);
    }

    [Fact]
    public async Task Round_robin_order_assigns_shuttles_in_turn()
    {
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(board, repository);
        var shuttleIds = new[] { "SH-01", "SH-02", "SH-03" };
        for (var index = 0; index < 6; index++)
            board.Add(new FleetTask($"task-{index + 1}", $"A01-L{index + 1:00}", Start.AddSeconds(index)));

        var assignedTasks = new List<FleetTask>();
        for (var index = 0; index < 6; index++)
        {
            var shuttleId = shuttleIds[index % shuttleIds.Length];
            var task = await dispatcher.AssignNextAsync(shuttleId, Start.AddMinutes(1).AddSeconds(index), CancellationToken.None);
            Assert.NotNull(task);
            assignedTasks.Add(task);
        }

        for (var index = 0; index < assignedTasks.Count; index++)
        {
            Assert.Equal(shuttleIds[index % shuttleIds.Length], assignedTasks[index].AssignedShuttleId);
        }
    }

    [Fact]
    public async Task Reload_preserves_fifo_order()
    {
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var tasks = new[]
        {
            new FleetTask("task-1", "A01-L01", Start),
            new FleetTask("task-2", "A01-L02", Start.AddSeconds(1)),
            new FleetTask("task-3", "A01-L03", Start.AddSeconds(2)),
        };
        await repository.SaveAllAsync(tasks, CancellationToken.None);

        var reloadedBoard = new TaskBoard();
        foreach (var task in await repository.LoadAllAsync(CancellationToken.None))
            reloadedBoard.Add(task);

        Assert.Equal(tasks.Length, reloadedBoard.Count);
        Assert.Equal(tasks.Select(task => task.TaskId),
            (await repository.LoadAllAsync(CancellationToken.None)).Select(task => task.TaskId));
        Assert.Equal("task-1", reloadedBoard.NextQueued()!.TaskId);
    }

    [Fact]
    public async Task FailAsync_persists_the_failure()
    {
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(board, repository);
        var task = new FleetTask("task-1", "A01-L01", Start);
        board.Add(task);

        await dispatcher.FailAsync(task.TaskId, "lane blocked", Start.AddSeconds(1), CancellationToken.None);

        var persistedTask = await repository.GetAsync(task.TaskId, CancellationToken.None);
        Assert.NotNull(persistedTask);
        Assert.Equal(TaskState.Failed, persistedTask.State);
        Assert.Equal("lane blocked", persistedTask.FailureReason);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task RequeueAsync_returns_the_task_to_queued_and_saves_it()
    {
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(board, repository);
        var task = new FleetTask("task-1", "A01-L01", Start);
        board.Add(task);
        await dispatcher.AssignNextAsync("SH-01", Start.AddSeconds(1), CancellationToken.None);
        Assert.Equal(1, repository.SaveCount);
        
        await dispatcher.RequeueAsync(task.TaskId, Start.AddSeconds(2), CancellationToken.None);
        Assert.Equal(2, repository.SaveCount);
        Assert.NotNull(task);
        Assert.Equal(TaskState.Queued, task.State);
        Assert.Null(task.AssignedShuttleId);
    }

    [Fact]
    public async Task RequeueAsync_for_a_task_that_is_not_assigned_throws_and_saves_nothing()
    {
        var board = new TaskBoard();
        var repository = new InMemoryFleetTaskRepository();
        var dispatcher = new TaskDispatcher(board, repository);
        var task = new FleetTask("task-1", "A01-L01", Start);
        board.Add(task);

        await Assert.ThrowsAsync<TaskRejectedException>(() => 
            dispatcher.RequeueAsync(task.TaskId, Start.AddSeconds(2), CancellationToken.None));
        Assert.Equal(TaskState.Queued, task.State);
        Assert.Equal(0, repository.SaveCount);
    }
}