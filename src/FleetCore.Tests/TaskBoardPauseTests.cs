using FleetCore;

namespace FleetCore.Tests;

public class TaskBoardPauseTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Pausing_one_shuttle_leaves_unassigned_queued_tasks_dispatchable()
    {
        var board = new TaskBoard();
        board.Add(new FleetTask("task-1", "A01-L02", Start));
        board.Add(new FleetTask("task-2", "A01-L03", Start));

        await board.PauseAllExcept("SH-01", CancellationToken.None);

        Assert.Equal(new[] { "task-1", "task-2" }, board.Queued().Select(task => task.TaskId));
    }

    [Fact]
    public async Task Pausing_one_shuttle_excludes_work_owned_by_that_shuttle()
    {
        var board = new TaskBoard();
        var ownTask = new FleetTask("task-own", "A01-L02", Start);
        ownTask.Assign("SH-01", Start);
        var otherTask = new FleetTask("task-other", "A01-L03", Start);
        otherTask.Assign("SH-02", Start);
        board.Add(ownTask);
        board.Add(otherTask);

        await board.PauseAllExcept("SH-01", CancellationToken.None);

        // Requeue both so the pause filter is visible on the queue.
        ownTask.Requeue(Start.AddSeconds(1));
        otherTask.Requeue(Start.AddSeconds(2));

        Assert.Contains(board.Queued(), task => task.TaskId == "task-own");
        Assert.DoesNotContain(board.Queued(), task => task.TaskId == "task-other");
    }

    [Fact]
    public async Task Resuming_one_shuttle_restores_its_paused_work()
    {
        var board = new TaskBoard();
        var otherTask = new FleetTask("task-other", "A01-L03", Start);
        otherTask.Assign("SH-02", Start);
        board.Add(otherTask);

        await board.PauseAllExcept("SH-01", CancellationToken.None);
        otherTask.Requeue(Start.AddSeconds(1));
        Assert.DoesNotContain(board.Queued(), task => task.TaskId == "task-other");

        await board.ResumeAllExcept("SH-01", CancellationToken.None);

        Assert.Contains(board.Queued(), task => task.TaskId == "task-other");
    }

    [Fact]
    public async Task A_blank_shuttle_id_pauses_and_resumes_every_queued_task()
    {
        var board = new TaskBoard();
        board.Add(new FleetTask("task-1", "A01-L02", Start));
        board.Add(new FleetTask("task-2", "A01-L03", Start));

        await board.PauseAllExcept("", CancellationToken.None);
        Assert.Empty(board.Queued());

        await board.ResumeAllExcept("", CancellationToken.None);
        Assert.Equal(2, board.Queued().Count());
    }
}
