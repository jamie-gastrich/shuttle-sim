using FleetCore;

namespace FleetManager;

public sealed class TaskDispatcher (TaskBoard board, IFleetTaskRepository repository)
{
    private readonly TaskBoard _board = board;
    private readonly IFleetTaskRepository _repository = repository;

    public async Task<FleetTask?> AssignNextAsync(string shuttleId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.NextQueued();
        if (task is null)
            return null;

        task.Assign(shuttleId, now);
        await _repository.SaveAsync(task, cancellationToken);
        return task;
    }

    public async Task StartAsync(string taskId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Start(now);
        await _repository.SaveAsync(task, cancellationToken);
    }

    public async Task CompleteAsync(string taskId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Complete(now);
        await _repository.SaveAsync(task, cancellationToken);
    }

    public async Task FailAsync(string taskId, string failureReason, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Fail(failureReason, now);
        await _repository.SaveAsync(task, cancellationToken);
    }
}