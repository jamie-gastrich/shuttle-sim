using FleetCore;
using ShuttleCore;
using TrafficCore;

namespace FleetManager;

public sealed record RunResult(int Ticks, bool AllDone);

public sealed class RunLoop
{
    private readonly OccupancyMap _occupancy;
    private readonly TrafficController _traffic;
    private readonly RunLoopGate _gate;
    private readonly TaskBoard _board;
    private readonly TaskDispatcher _dispatcher;
    private readonly IReadOnlyList<Shuttle> _shuttles;
    private readonly Dictionary<string, FleetTask> _activeTasksByShuttle;
    private readonly TimeSpan _tickInterval;
    private DateTime _currentTime;
    private int _ticks;

    public RunLoop(
        OccupancyMap occupancy,
        Track track,
        TrafficController traffic,
        RunLoopGate gate,
        TaskBoard board,
        TaskDispatcher dispatcher,
        IReadOnlyList<Shuttle> shuttles,
        DateTime startTime,
        TimeSpan tickInterval)
    {
        _occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        _traffic = traffic ?? throw new ArgumentNullException(nameof(traffic));
        _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        _board = board ?? throw new ArgumentNullException(nameof(board));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _shuttles = shuttles ?? throw new ArgumentNullException(nameof(shuttles));
        _currentTime = startTime;
        _tickInterval = tickInterval;
        _activeTasksByShuttle = new Dictionary<string, FleetTask>(StringComparer.Ordinal);
    }

    public async Task<RunResult> RunAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (IsDone())
            {
                return new RunResult(_ticks, true);
            }

            var result = await TickAsync(cancellationToken);
            if (result.AllDone)
            {
                return result;
            }
        }

        return new RunResult(_ticks, false);
    }

    public async Task<RunResult> TickAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (IsDone())
        {
            return new RunResult(_ticks, true);
        }

        foreach (var shuttle in _shuttles)
        {
            if (shuttle.State != ShuttleState.Idle)
            {
                continue;
            }

            var task = _board.NextQueued();
            if (task is null)
            {
                continue;
            }

            if (!_gate.CanMove(shuttle.ShuttleId, task.ToLane))
            {
                continue;
            }

            var assigned = await _dispatcher.AssignNextAsync(shuttle.ShuttleId, _currentTime, cancellationToken);
            if (assigned is null)
            {
                continue;
            }

            if (!_gate.CommitMove(shuttle.ShuttleId, task.ToLane))
            {
                // Fail the assigned task so a lost reservation race cannot leave it stranded outside the queue.
                await _dispatcher.FailAsync(assigned.TaskId, "Route reservation failed after assignment.", _currentTime, cancellationToken);
                continue;
            }

            shuttle.StartMove(task.ToLane, _currentTime);
            if (shuttle.State == ShuttleState.Fault)
            {
                // The move never began. Give the work back to the queue and free the
                // route reservation that was made for a move that did not happen.
                await _dispatcher.RequeueAsync(assigned.TaskId, _currentTime, cancellationToken);
                _traffic.Release(shuttle.ShuttleId);
                continue;
            }
            _activeTasksByShuttle[shuttle.ShuttleId] = assigned;
            // Persist InProgress...
            await _dispatcher.StartAsync(assigned.TaskId, _currentTime, cancellationToken);
        }

        _currentTime += _tickInterval;

        foreach (var shuttle in _shuttles)
        {
            if (shuttle.State != ShuttleState.Moving)
            {
                continue;
            }
            try
            {
                shuttle.CompleteMove(_currentTime);
                // Release frees the transit reservation; occupancy records the physical destination, which remains occupied.
                _occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
                _traffic.Release(shuttle.ShuttleId);

                if (_activeTasksByShuttle.TryGetValue(shuttle.ShuttleId, out var task))
                {
                    // Persist Done after physical completion; repository errors propagate and leave the active entry in place.
                    await _dispatcher.CompleteAsync(task.TaskId, _currentTime, cancellationToken);
                    _activeTasksByShuttle.Remove(shuttle.ShuttleId);
                }
            }
            catch (ShuttleRejectedException)
            {
                // The move is not complete for this time step yet.
            }
        }

        _ticks++;
        return new RunResult(_ticks, IsDone());
    }

    private bool IsDone()
    {
        var hasQueuedTask = _board.NextQueued() is not null;
        var hasActiveTask = _activeTasksByShuttle.Count > 0;
        var hasMovingShuttle = _shuttles.Any(shuttle => shuttle.State == ShuttleState.Moving);
        return !hasQueuedTask && !hasActiveTask && !hasMovingShuttle;
    }
}
