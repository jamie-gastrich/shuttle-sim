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
}