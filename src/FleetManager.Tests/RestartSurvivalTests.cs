using FleetCore;

namespace FleetManager.Tests;

[Trait("Category", "Integration")]
[Collection("Database")]
public class RestartSurvivalTests
{
    [Fact]
    public async Task State_survives_restart_after_persisting_tasks()
    {
        var cancellationToken = CancellationToken.None;
        var repo = new PostgresFleetTaskRepository(TestDatabase.BuildConnectionString());
        await TestDatabase.ResetAsync(repo, cancellationToken);

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var queuedA = new FleetTask("task-queued-a", "A01-L01", start.AddTicks(7));
        var queuedB = new FleetTask("task-queued-b", "A01-L02", start.AddMinutes(1));

        var assigned = new FleetTask("task-assigned", "A01-L03", start.AddMinutes(2));
        assigned.Assign("SH-01", start.AddMinutes(2).AddSeconds(1));

        var inProgress = new FleetTask("task-in-progress", "A01-L04", start.AddMinutes(3));
        inProgress.Assign("SH-02", start.AddMinutes(3).AddSeconds(1));
        inProgress.Start(start.AddMinutes(3).AddSeconds(2));

        var done = new FleetTask("task-done", "A01-L05", start.AddMinutes(4));
        done.Assign("SH-03", start.AddMinutes(4).AddSeconds(1));
        done.Start(start.AddMinutes(4).AddSeconds(2));
        done.Complete(start.AddMinutes(4).AddSeconds(3));

        var failed = new FleetTask("task-failed", "A01-L06", start.AddMinutes(5));
        failed.Fail("rack empty", start.AddMinutes(5).AddSeconds(1));

        var expected = new[] { queuedA, queuedB, assigned, inProgress, done, failed };

        await repo.SaveAllAsync(expected, cancellationToken);

        var reloaded = (await repo.LoadAllAsync(cancellationToken)).ToList();
        var boardAfterRestart = new TaskBoard();
        foreach (var task in reloaded)
            boardAfterRestart.Add(task);

        Assert.Equal(expected.Length, boardAfterRestart.Count);
        Assert.Equal(new[]
        {
            "task-queued-a",
            "task-queued-b",
            "task-assigned",
            "task-in-progress",
            "task-done",
            "task-failed",
        }, reloaded.Select(task => task.TaskId).ToArray());

        foreach (var expectedTask in expected)
        {
            var actual = boardAfterRestart.Get(expectedTask.TaskId);
            Assert.Equal(expectedTask.TaskId, actual.TaskId);
            Assert.Equal(expectedTask.ToLane, actual.ToLane);
            Assert.Equal(expectedTask.CreatedAt, actual.CreatedAt);
            Assert.Equal(expectedTask.State, actual.State);
            Assert.Equal(expectedTask.AssignedShuttleId, actual.AssignedShuttleId);
            Assert.Equal(expectedTask.FailureReason, actual.FailureReason);
        }

        Assert.Equal("task-queued-a", boardAfterRestart.NextQueued()!.TaskId);
        Assert.Equal("task-queued-b", boardAfterRestart.Get("task-queued-b").TaskId);
    }

    [Fact]
    public async Task Orders_survive_a_restart_alongside_tasks()
    {
        var cancellationToken = CancellationToken.None;
        var connectionString = TestDatabase.BuildConnectionString();
        var taskRepository = new PostgresFleetTaskRepository(connectionString);
        var orderRepository = new PostgresWmsOrderRepository(connectionString);
        await TestDatabase.ResetAsync(taskRepository, cancellationToken);

        await using (var connection = new Npgsql.NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new Npgsql.NpgsqlCommand("DELETE FROM wms_orders", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var task = new FleetTask("ORD-1", "A01-L07", start);
        task.Assign("SH-01", start.AddSeconds(1));
        task.Start(start.AddSeconds(2));

        var order = new WmsOrder("ORD-1", WmsOrderKinds.Store, "PAL-1", "A01-L07", WmsOrderStates.InProgress, start);

        await taskRepository.SaveAsync(task, cancellationToken);
        await orderRepository.SaveAsync(order, cancellationToken);

        // Reload both, as serve does on start.
        var reloadedTasks = (await taskRepository.LoadAllAsync(cancellationToken)).ToList();
        var reloadedOrders = (await orderRepository.LoadAllAsync(cancellationToken)).ToList();

        var taskBoard = new TaskBoard();
        foreach (var t in reloadedTasks) taskBoard.Add(t);
        var orderBoard = new WmsOrderBoard(taskBoard);
        foreach (var o in reloadedOrders) orderBoard.Load(o);

        Assert.Equal(1, taskBoard.Count);
        Assert.True(orderBoard.TryGet("ORD-1", out var restoredOrder));
        Assert.Equal(WmsOrderStates.InProgress, restoredOrder!.State);
        Assert.Equal(TaskState.InProgress, taskBoard.Get("ORD-1").State);
    }
}