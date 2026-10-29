namespace FleetCore;
public sealed class TaskBoard
{
    private readonly List<FleetTask> _tasks = new();
    private readonly HashSet<string> _pausedTaskIds = new(StringComparer.Ordinal);

    public FleetTask Add(FleetTask task)
    {
        if (_tasks.Any(existing => existing.TaskId == task.TaskId))
            throw new TaskRejectedException($"duplicate task id {task.TaskId}");
        _tasks.Add(task);
        return task;
    }

    public bool TryGet(string taskId, out FleetTask? task)
    {
        task = _tasks.FirstOrDefault(existing => existing.TaskId == taskId);
        return task is not null;
    }

    public FleetTask Get(string taskId) =>
        TryGet(taskId, out var task) ? task! : throw new KeyNotFoundException($"unknown task {taskId}");

    public FleetTask? NextQueued() =>
        _tasks.FirstOrDefault(t => t.State == TaskState.Queued && !_pausedTaskIds.Contains(t.TaskId));

    public IEnumerable<FleetTask> Queued() =>
        _tasks.Where(t => t.State == TaskState.Queued && !_pausedTaskIds.Contains(t.TaskId));

    public int Count => _tasks.Count;

    public Task PauseAllExcept(string shuttleId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shuttleId))
        {
            // An empty shuttle id means "all shuttles": pause every queued task.
            foreach (var task in _tasks)
                _pausedTaskIds.Add(task.TaskId);
            return Task.CompletedTask;
        }

        foreach (var task in _tasks)
        {
            // A queued task has no owner yet. Pausing one shuttle must not hide
            // work that another shuttle can still take, so only work already
            // assigned to a different shuttle is paused here. Work owned by the
            // named shuttle is left running.
            if (task.AssignedShuttleId is { } owner && owner != shuttleId)
                _pausedTaskIds.Add(task.TaskId);
        }
        return Task.CompletedTask;
    }

    public Task ResumeAllExcept(string shuttleId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(shuttleId))
        {
            _pausedTaskIds.Clear();
            return Task.CompletedTask;
        }

        foreach (var task in _tasks)
        {
            // Clear the paused mark for everything except work still owned by the
            // named shuttle. Including unowned tasks here also clears the mark
            // when paused work was requeued and no longer has an owner.
            if (task.AssignedShuttleId != shuttleId)
                _pausedTaskIds.Remove(task.TaskId);
        }
        return Task.CompletedTask;
    }
}