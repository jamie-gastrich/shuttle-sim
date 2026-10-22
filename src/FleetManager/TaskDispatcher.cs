using FleetCore;
using Npgsql;

namespace FleetManager;

public sealed class TaskDispatcher (TaskBoard board, IFleetTaskRepository repository, OrderSync orderSync,
    IWmsOrderRepository? orderRepository = null, string? connectionString = null)
{
    private readonly TaskBoard _board = board;
    private readonly IFleetTaskRepository _repository = repository;
    private readonly OrderSync _orderSync = orderSync;
    private readonly IWmsOrderRepository? _orderRepository = orderRepository;
    private readonly string? _connectionString = connectionString;

    public async Task<FleetTask?> AssignNextAsync(string shuttleId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.NextQueued();
        if (task is null)
            return null;

        task.Assign(shuttleId, now);
        _orderSync.Note(task.TaskId);
        await SaveAsync(task, cancellationToken);
        return task;
    }

    public async Task StartAsync(string taskId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Start(now);
        _orderSync.Note(taskId);
        await SaveAsync(task, cancellationToken);
    }

    public async Task CompleteAsync(string taskId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Complete(now);
        _orderSync.Note(taskId);
        await SaveAsync(task, cancellationToken);
    }

    public async Task FailAsync(string taskId, string failureReason, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Fail(failureReason, now);
        _orderSync.Note(taskId);
        await SaveAsync(task, cancellationToken);
    }

    public async Task RequeueAsync(string taskId, DateTime now, CancellationToken cancellationToken)
    {
        var task = _board.Get(taskId);
        task.Requeue(now);
        _orderSync.Note(taskId);
        await SaveAsync(task, cancellationToken);
    }

    private async Task SaveAsync(FleetTask task, CancellationToken cancellationToken)
    {
        if (_orderRepository is PostgresWmsOrderRepository pgOrder
            && _repository is PostgresFleetTaskRepository pgTask
            && !string.IsNullOrWhiteSpace(_connectionString))
        {
            if (!_orderSync.Board.TryGet(task.TaskId, out var order) || order is null)
                throw new InvalidOperationException($"no order for task {task.TaskId}");

            await using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await pgTask.SaveAsync(task, connection, transaction, cancellationToken);
            await pgOrder.SaveAsync(order, connection, transaction, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        await _repository.SaveAsync(task, cancellationToken);
        if (_orderRepository is not null && _orderSync.Board.TryGet(task.TaskId, out var fallbackOrder) && fallbackOrder is not null)
            await _orderRepository.SaveAsync(fallbackOrder, cancellationToken);
    }
}