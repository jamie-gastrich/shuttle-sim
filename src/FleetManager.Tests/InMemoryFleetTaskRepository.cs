using FleetCore;

namespace FleetManager.Tests;

public class InMemoryFleetTaskRepository : IFleetTaskRepository
{
    private readonly List<FleetTask> _tasks = new();

    public int SaveCount { get; private set; }

    public Task<FleetTask?> GetAsync(string taskId, CancellationToken cancellationToken)
    {
        var task = _tasks.FirstOrDefault(t => t.TaskId == taskId);
        return Task.FromResult(task);
    }

    public Task<IEnumerable<FleetTask>> LoadAllAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_tasks.AsEnumerable());
    }

    public Task SaveAllAsync(IEnumerable<FleetTask> tasks, CancellationToken cancellationToken)
    {
        foreach (var task in tasks)
            Upsert(task);

        return Task.CompletedTask;
    }

    public Task SaveAsync(FleetTask task, CancellationToken cancellationToken)
    {
        Upsert(task);
        return Task.CompletedTask;
    }

    private void Upsert(FleetTask task)
    {
        var index = _tasks.FindIndex(existing => existing.TaskId == task.TaskId);
        if (index < 0)
            _tasks.Add(task);
        else
            _tasks[index] = task;

        SaveCount++;
    }
}