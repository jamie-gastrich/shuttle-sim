using FleetCore;

namespace FleetManager;

public sealed class TaskDispatcher (TaskBoard board, IFleetTaskRepository repository, OrderSync orderSync)
{
    private readonly TaskBoard _board = board;
    private readonly IFleetTaskRepository _repository = repository;
    private readonly OrderSync _orderSync = orderSync;

    public async Task<FleetTask?> AssignNextAsync(string shuttleId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.NextQueued();
        if (task is null)
            return null;

        task.Assign(shuttleId, now);
        _orderSync.Note(task.TaskId);
        await _repository.SaveAsync(task, cancellationToken);
        return task;
    }

    public async Task StartAsync(string taskId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Start(now);
        _orderSync.Note(taskId);
        await _repository.SaveAsync(task, cancellationToken);
    }

    public async Task CompleteAsync(string taskId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Complete(now);
        _orderSync.Note(taskId);
        await _repository.SaveAsync(task, cancellationToken);
    }

    public async Task FailAsync(string taskId, string failureReason, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Fail(failureReason, now);
        _orderSync.Note(taskId);
        await _repository.SaveAsync(task, cancellationToken);
    }

    public async Task RequeueAsync(string taskId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Requeue(now);
        _orderSync.Note(taskId);
        await _repository.SaveAsync(task, cancellationToken);
    }
}