using ShuttleCore;
using TrafficCore;
using FleetCore;

namespace FleetManager;

public sealed class RunLoopGate
{
    private readonly PauseState _pauseState;
    private OccupancyMap Occupancy { get; }
    private Track Track { get; }
    private TrafficController Traffic { get; }

    public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic)
        : this(occupancy, track, traffic, new PauseState())
    {
    }

    public RunLoopGate(
        OccupancyMap occupancy,
        Track track,
        TrafficController traffic,
        PauseState pauseState)
    {
        Occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        Track = track ?? throw new ArgumentNullException(nameof(track));
        Traffic = traffic ?? throw new ArgumentNullException(nameof(traffic));
        _pauseState = pauseState ?? throw new ArgumentNullException(nameof(pauseState));
    }

    public PauseState PauseState => _pauseState;

    public void Pause(string shuttleId) => _pauseState.Pause(shuttleId);

    public void Resume(string shuttleId) => _pauseState.Resume(shuttleId);

    public bool IsPaused(string shuttleId) => _pauseState.IsPaused(shuttleId);

    public void PauseAll(IEnumerable<string> shuttleIds) => _pauseState.PauseAll(shuttleIds);

    public void ResumeAll(IEnumerable<string> shuttleIds) => _pauseState.ResumeAll(shuttleIds);

    public bool CanMove(string shuttleId, string destinationLane)
    {
        if (_pauseState.IsPaused(shuttleId))
        {
            return false;
        }

        var currentLane = Occupancy.GetOccupiedLane(shuttleId);
        if (currentLane is null)
        {
            return false;
        }

        var route = Track.Route(currentLane, destinationLane);
        if (Occupancy.IsOccupiedByOthers(route, shuttleId))
        {
            return false;
        }

        if (!Traffic.IsRouteFree(shuttleId, currentLane, destinationLane))
        {
            return false;
        }
        return true;
    }

    public bool CommitMove(string shuttleId, string destinationLane)
    {
        if (_pauseState.IsPaused(shuttleId))
        {
            return false;
        }

        var currentLane = Occupancy.GetOccupiedLane(shuttleId);
        if (currentLane is null)
        {
            return false;
        }

        var decision = Traffic.TryReserveRoute(shuttleId, currentLane, destinationLane);
        return decision.Granted;
    }
}