using FleetCore;
using ShuttleCore;
using TrafficCore;

namespace FleetManager;

public sealed record RunResult(int Ticks, bool AllDone);

public sealed class RunLoop
{
    private readonly OccupancyMap _occupancy;
    private readonly Track _track;
    private readonly TrafficController _traffic;
    private readonly RunLoopGate _gate;
    private readonly TaskBoard _board;
    private readonly TaskDispatcher _dispatcher;
    private readonly IReadOnlyList<Shuttle> _shuttles;
    private readonly Dictionary<string, FleetTask> _activeTasksByShuttle;
    private readonly TimeSpan _tickInterval;
    private DateTime _currentTime;

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
        _track = track ?? throw new ArgumentNullException(nameof(track));
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
        var ticks = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            var hasQueuedTask = _board.NextQueued() is not null;
            var hasActiveTask = _activeTasksByShuttle.Count > 0;
            var hasMovingShuttle = _shuttles.Any(shuttle => shuttle.State == ShuttleState.Moving);

            if (!hasQueuedTask && !hasActiveTask && !hasMovingShuttle)
            {
                return new RunResult(ticks, true);
            }

            await TickAsync(cancellationToken);
            ticks++;
        }

        return new RunResult(ticks, false);
    }

    public void Run(int ticks)
    {
        for (var index = 0; index < ticks; index++)
        {
            Tick();
        }
    }

    public void Tick()
    {
        TickAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
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
                _occupancy.SetOccupiedLane(shuttle.ShuttleId, shuttle.Position);
                _traffic.Release(shuttle.ShuttleId);

                if (_activeTasksByShuttle.TryGetValue(shuttle.ShuttleId, out var task))
                {
                    await _dispatcher.CompleteAsync(task.TaskId, _currentTime, cancellationToken);
                    _activeTasksByShuttle.Remove(shuttle.ShuttleId);
                }
            }
            catch (ShuttleRejectedException)
            {
                // The move is not complete for this time step yet.
            }
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
                continue;
            }

            shuttle.StartMove(task.ToLane, _currentTime);
            await _dispatcher.StartAsync(assigned.TaskId, _currentTime, cancellationToken);
            _activeTasksByShuttle[shuttle.ShuttleId] = assigned;
        }
    }
}