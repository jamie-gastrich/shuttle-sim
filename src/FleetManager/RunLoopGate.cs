using ShuttleCore;
using TrafficCore;

namespace FleetManager;

public sealed class RunLoopGate
{
    private readonly HashSet<string> _pausedShuttles = new(StringComparer.Ordinal);
    private OccupancyMap Occupancy { get; }
    private Track Track { get; }
    private TrafficController Traffic { get; }

    public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic)
    {
        Occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        Track = track ?? throw new ArgumentNullException(nameof(track));
        Traffic = traffic ?? throw new ArgumentNullException(nameof(traffic));
    }

    public void Pause(string shuttleId) => _pausedShuttles.Add(shuttleId);

    public void Resume(string shuttleId) => _pausedShuttles.Remove(shuttleId);

    public bool IsPaused(string shuttleId) => _pausedShuttles.Contains(shuttleId);

    public void PauseAll(IEnumerable<string> shuttleIds)
    {
        foreach (var shuttleId in shuttleIds)
        {
            Pause(shuttleId);
        }
    }

    public void ResumeAll(IEnumerable<string> shuttleIds)
    {
        foreach (var shuttleId in shuttleIds)
        {
            Resume(shuttleId);
        }
    }

    public bool CanMove(string shuttleId, string destinationLane)
    {
        if (IsPaused(shuttleId))
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
        if (IsPaused(shuttleId))
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