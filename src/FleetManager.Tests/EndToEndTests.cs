using FleetCore;

namespace FleetManager.Tests;

[Trait("Category", "Integration")]
[Collection("Database")]
public class EndToEndTests
{
    [Fact]
    public async Task Three_shuttles_finish_queue_and_state_survives_restart()
    {
        var cancellationToken = CancellationToken.None;
        var connectionString = TestDatabase.BuildConnectionString();
        var repository = new PostgresFleetTaskRepository(connectionString);
        await TestDatabase.ResetAsync(repository, cancellationToken);

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var board = new TaskBoard();
        var expectedTasks = new List<FleetTask>();
        for (var index = 0; index < 9; index++)
        {
            var task = new FleetTask($"task-{index + 1}", $"A01-L{index + 1:00}", start.AddMinutes(index));
            expectedTasks.Add(task);
            board.Add(task);
        }

        var dispatcher = new TaskDispatcher(board, repository);
        var shuttleIds = new[] { "SH-01", "SH-02", "SH-03" };
        var now = start.AddHours(1);
        for (var index = 0; index < expectedTasks.Count; index++)
        {
            var expectedTask = expectedTasks[index];
            var shuttleId = shuttleIds[index % shuttleIds.Length];
            var assignedTask = await dispatcher.AssignNextAsync(shuttleId, now, cancellationToken);
            Assert.NotNull(assignedTask);
            Assert.Equal(expectedTask.TaskId, assignedTask.TaskId);

            now = now.AddSeconds(1);
            await dispatcher.StartAsync(assignedTask.TaskId, now, cancellationToken);
            now = now.AddSeconds(1);
            await dispatcher.CompleteAsync(assignedTask.TaskId, now, cancellationToken);
            now = now.AddSeconds(1);
        }

        Assert.All(expectedTasks, task => Assert.Equal(TaskState.Done, task.State));
        for (var index = 0; index < expectedTasks.Count; index++)
            Assert.Equal(shuttleIds[index % shuttleIds.Length], expectedTasks[index].AssignedShuttleId);
        Assert.Null(board.NextQueued());

        var holdTask = new FleetTask("task-hold", "B02-L01", now);
        expectedTasks.Add(holdTask);
        board.Add(holdTask);
        await repository.SaveAsync(holdTask, cancellationToken);

        var restartedRepository = new PostgresFleetTaskRepository(TestDatabase.BuildConnectionString());
        var restartedBoard = new TaskBoard();
        var reloadedTasks = (await restartedRepository.LoadAllAsync(cancellationToken)).ToList();
        foreach (var task in reloadedTasks)
            restartedBoard.Add(task);

        Assert.Equal(expectedTasks.Count, restartedBoard.Count);
        Assert.Equal(expectedTasks.Select(task => task.TaskId), reloadedTasks.Select(task => task.TaskId));
        for (var index = 0; index < expectedTasks.Count; index++)
        {
            var expectedTask = expectedTasks[index];
            var actualTask = reloadedTasks[index];
            Assert.Equal(expectedTask.TaskId, actualTask.TaskId);
            Assert.Equal(expectedTask.ToLane, actualTask.ToLane);
            Assert.Equal(expectedTask.CreatedAt, actualTask.CreatedAt);
            Assert.Equal(expectedTask.State, actualTask.State);
            Assert.Equal(expectedTask.AssignedShuttleId, actualTask.AssignedShuttleId);
        }

        Assert.Equal("task-hold", restartedBoard.NextQueued()!.TaskId);
    }
}