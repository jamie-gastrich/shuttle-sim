using FleetCore;

namespace FleetCore.Tests;

public class TaskLifecycleTests
{
    private static readonly DateTime Start = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Assign_from_queued_enters_assigned()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);

        task.Assign("SH-01", Start);

        Assert.Equal(TaskState.Assigned, task.State);
        Assert.Equal("SH-01", task.AssignedShuttleId);
    }

    [Fact]
    public void Start_from_assigned_enters_in_progress()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);

        task.Start(Start.AddSeconds(1));

        Assert.Equal(TaskState.InProgress, task.State);
    }

    [Fact]
    public void Complete_from_in_progress_enters_done()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);
        task.Start(Start.AddSeconds(1));

        task.Complete(Start.AddSeconds(2));

        Assert.Equal(TaskState.Done, task.State);
    }

    [Fact]
    public void Fail_from_queued_enters_failed()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);

        task.Fail("reason", Start);

        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal("reason", task.FailureReason);
    }

    [Fact]
    public void Fail_from_assigned_enters_failed()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);

        task.Fail("reason", Start.AddSeconds(1));

        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal("reason", task.FailureReason);
    }

    [Fact]
    public void Fail_from_in_progress_enters_failed()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);
        task.Start(Start.AddSeconds(1));

        task.Fail("reason", Start.AddSeconds(2));

        Assert.Equal(TaskState.Failed, task.State);
        Assert.Equal("reason", task.FailureReason);
    }

    [Fact]
    public void Cancel_from_assigned_enters_cancelled()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);

        task.Cancel(Start.AddSeconds(1));

        Assert.Equal(TaskState.Cancelled, task.State);
    }

    [Fact]
    public void Cancel_after_done_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);
        task.Start(Start.AddSeconds(1));
        task.Complete(Start.AddSeconds(2));

        Assert.Throws<TaskRejectedException>(() => task.Cancel(Start.AddSeconds(3)));
    }

    [Fact]
    public void Assign_twice_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);

        Assert.Throws<TaskRejectedException>(() => task.Assign("SH-02", Start.AddSeconds(1)));
    }
    

    [Fact]
    public void Start_while_queued_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);

        Assert.Throws<TaskRejectedException>(() => task.Start(Start.AddSeconds(1)));
    }

    [Fact]
    public void Complete_while_assigned_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);

        Assert.Throws<TaskRejectedException>(() => task.Complete(Start.AddSeconds(1)));
    }

    [Fact]
    public void Fail_after_done_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);
        task.Start(Start.AddSeconds(1));
        task.Complete(Start.AddSeconds(2));

        Assert.Throws<TaskRejectedException>(() => task.Fail("reason", Start.AddSeconds(3)));
    }

    [Fact]
    public void Assign_with_a_blank_shuttle_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);

        Assert.Throws<ArgumentException>(() => task.Assign("", Start));
    }

    [Fact]
    public void Fail_with_a_blank_reason_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);
        task.Start(Start.AddSeconds(1));

        Assert.Throws<ArgumentException>(() => task.Fail("", Start.AddSeconds(2)));
    }

    [Fact]
    public void A_move_with_a_timestamp_before_the_previous_one_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);

        Assert.Throws<TaskRejectedException>(() => task.Start(Start.AddSeconds(-1)));
    }

    [Fact]
    public void Task_wire_names_match_record_11()
    {
        var queued = TaskStateWire.ToWireName(TaskState.Queued);
        var assigned = TaskStateWire.ToWireName(TaskState.Assigned);
        var in_progress = TaskStateWire.ToWireName(TaskState.InProgress);
        var done = TaskStateWire.ToWireName(TaskState.Done);
        var failed = TaskStateWire.ToWireName(TaskState.Failed);

        Assert.Equal("queued", queued);
        Assert.Equal("assigned", assigned);
        Assert.Equal("in_progress", in_progress);
        Assert.Equal("done", done);
        Assert.Equal("failed", failed);
    }

    [Fact]
    public void Board_rejects_a_duplicate_task_id()
    {
        var task1 = new FleetTask("task-1", "A01-L02", Start);
        var task2 = new FleetTask("task-1", "A01-L02", Start);

        var board = new TaskBoard();
        board.Add(task1);

        Assert.Throws<TaskRejectedException>(() => board.Add(task2));
    }

    [Fact]
    public void Board_hands_out_the_oldest_queued_task_first()
    {
        var task1 = new FleetTask("task-1", "A01-L02", Start);
        var task2 = new FleetTask("task-2", "A01-L02", Start);

        var board = new TaskBoard();
        board.Add(task1);
        board.Add(task2);

        Assert.Equal(task1, board.NextQueued());
    }

    [Fact]
    public void Board_next_queued_skips_assigned_and_finished_tasks()
    {
        var task1 = new FleetTask("task-1", "A01-L02", Start);
        var task2 = new FleetTask("task-2", "A01-L02", Start);
        var task3 = new FleetTask("task-3", "A01-L02", Start);

        task1.Assign("SH-01", Start);

        task2.Assign("SH-02", Start);
        task2.Start(Start.AddSeconds(1));
        task2.Complete(Start.AddSeconds(2));

        var board = new TaskBoard();
        board.Add(task1);
        board.Add(task2);
        board.Add(task3);

        Assert.Equal(task3, board.NextQueued());
    }

    [Fact]
    public void Board_get_returns_the_task_by_id()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        
        var board = new TaskBoard();
        board.Add(task);
        
        Assert.Equal(task, board.Get("task-1"));
    }

    [Fact]
    public void Requeue_from_assigned_returns_the_task_to_queued()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);

        task.Requeue(Start.AddSeconds(1));

        Assert.Equal(TaskState.Queued, task.State);
        Assert.Null(task.AssignedShuttleId);
    }

    [Fact]
    public void Requeue_from_queued_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);

        Assert.Throws<TaskRejectedException>(() => task.Requeue(Start.AddSeconds(1)));
    }

    [Fact]
    public void Requeue_from_in_progress_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);
        task.Start(Start.AddSeconds(1));

        Assert.Throws<TaskRejectedException>(() => task.Requeue(Start.AddSeconds(2)));
    }

    [Fact]
    public void Requeue_from_a_done_task_is_rejected()
    {
        var task = new FleetTask("task-1", "A01-L02", Start);
        task.Assign("SH-01", Start);
        task.Start(Start.AddSeconds(1));
        task.Complete(Start.AddSeconds(2));

        Assert.Throws<TaskRejectedException>(() => task.Requeue(Start.AddSeconds(3)));
    }
        
}