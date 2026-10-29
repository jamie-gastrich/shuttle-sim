namespace FleetCore;

// Settled meaning (decision 43): PauseAllExcept(shuttleId) pauses dispatch for
// the named shuttle. A blank id pauses every queued task. The name is historical.
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
            // Pause dispatch for the named shuttle only.
            if (task.AssignedShuttleId == shuttleId)
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
            // Clear the paused mark for the named shuttle's tasks and for
            // unassigned tasks that were paused globally.
            if (task.AssignedShuttleId == shuttleId || task.AssignedShuttleId is null)
                _pausedTaskIds.Remove(task.TaskId);
        }
        return Task.CompletedTask;
    }
}