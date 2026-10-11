namespace FleetCore;
public sealed class TaskBoard
{
    private readonly List<FleetTask> _tasks = new();

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
        _tasks.FirstOrDefault(task => task.State == TaskState.Queued);

    public int Count => _tasks.Count;
}