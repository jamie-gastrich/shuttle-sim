using System.Text.Json;
using Contracts;
using FleetCore;
using Messaging;

namespace FleetManager;

public sealed class TaskExecutionLoop
{
    private readonly TaskDispatcher _dispatcher;
    private readonly ShuttleRegistry _registry;
    private readonly ICommandSender _sender;
    private readonly string _fleetManagerId;
    private readonly TimeSpan _interval;
    private readonly Func<DateTime> _clock;
    // The TaskBoard cannot answer "which task is this shuttle working on", so the
    // tick keeps its own memory of the shuttles it has handed work to.
    private readonly Dictionary<string, FleetTask> _working = new(StringComparer.Ordinal);

    public TaskExecutionLoop(TaskDispatcher dispatcher, ShuttleRegistry registry, ICommandSender sender,
        string fleetManagerId, TimeSpan interval, Func<DateTime> clock)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        if (string.IsNullOrWhiteSpace(fleetManagerId))
            throw new ArgumentException("A Fleet Manager id is required.", nameof(fleetManagerId));
        _fleetManagerId = fleetManagerId;
        _interval = interval > TimeSpan.Zero ? interval : throw new ArgumentOutOfRangeException(nameof(interval));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public Task HandleStatusAsync(MessageReceived message, CancellationToken cancellationToken)
    {
        ShuttleStatusMessage? status;
        try
        {
            status = JsonSerializer.Deserialize<ShuttleStatusMessage>(message.Payload);
        }
        catch (JsonException)
        {
            return Task.CompletedTask;
        }

        if (status?.Envelope is not { } envelope
            || envelope.ContractVersion != ContractVersion.Current
            || envelope.MessageType != MessageTypes.ShuttleStatus
            || string.IsNullOrWhiteSpace(envelope.MessageId)
            || string.IsNullOrWhiteSpace(status.Status?.ShuttleId))
        {
            return Task.CompletedTask;
        }

        _registry.Record(status.Status);
        return Task.CompletedTask;
    }

    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await FinishFinishedWorkAsync(cancellationToken);
        await GiveWorkToFreeShuttlesAsync(cancellationToken);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            try
            {
                await TickAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"task execution: tick failed {ex.GetType().Name} {ex.Message}");
            }
        }
    }

    private async Task FinishFinishedWorkAsync(CancellationToken cancellationToken)
    {
        // Copy the dictionary so it can be changed while we walk it.
        var working = _working.ToArray();
        foreach (var (shuttleId, task) in working)
        {
            if (task.State != TaskState.InProgress)
                continue;
            if (!_registry.IsIdle(shuttleId))
                continue;
            if (_registry.PositionOf(shuttleId) != task.ToLane)
                continue;

            await _dispatcher.CompleteAsync(task.TaskId, _clock(), cancellationToken);
            _working.Remove(shuttleId);
        }
    }

    private async Task GiveWorkToFreeShuttlesAsync(CancellationToken cancellationToken)
    {
        foreach (var shuttleId in _registry.FreeShuttles())
        {
            if (_working.ContainsKey(shuttleId))
                continue;

            var task = await _dispatcher.AssignNextAsync(shuttleId, _clock(), cancellationToken);
            if (task is null)
                return;
            _working[shuttleId] = task;

            var envelope = MessageEnvelopes.Create(
                MessageTypes.MoveCommand, _fleetManagerId, _clock());
            var command = new MoveCommandMessage(envelope, shuttleId, task.ToLane);

            CommandSendResult result;
            try
            {
                result = await _sender.SendAsync(command, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The task is still good; the shuttle is not. Put it back in the
                // queue so another shuttle can try, and let the tick carry on.
                await _dispatcher.RequeueAsync(task.TaskId, _clock(), cancellationToken);
                _working.Remove(shuttleId);
                Console.WriteLine(
                    $"task execution: command {command.Envelope.MessageId} to {shuttleId} for task {task.TaskId} failed {ex.GetType().Name}, requeued");
                continue;
            }

            if (result.Status == CommandSendStatus.Acknowledged)
            {
                await _dispatcher.StartAsync(task.TaskId, _clock(), cancellationToken);
            }
            else
            {
                await _dispatcher.RequeueAsync(task.TaskId, _clock(), cancellationToken);
                _working.Remove(shuttleId);
            }
        }
    }
}
