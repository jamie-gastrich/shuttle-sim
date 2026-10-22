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

    public async Task PauseAllExcept(string shuttleId, CancellationToken cancellationToken)
    {
        var all = _tasks.ToList();
        foreach (var t in all)
        {
            if (t.TaskId != shuttleId)
                _pausedTaskIds.Add(t.TaskId);
        }
    }

    public async Task ResumeAllExcept(string shuttleId, CancellationToken cancellationToken)
    {
        var all = _tasks.ToList();
        foreach (var t in all)
        {
            if (t.TaskId != shuttleId)
                _pausedTaskIds.Remove(t.TaskId);
        }
    }
}