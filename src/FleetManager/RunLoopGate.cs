using ShuttleCore;
using TrafficCore;

namespace FleetManager;

public sealed class RunLoopGate
{
    private OccupancyMap Occupancy { get; }
    private Track Track { get; }
    private TrafficController Traffic { get; }

    public RunLoopGate(OccupancyMap occupancy, Track track, TrafficController traffic)
    {
        Occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
        Track = track ?? throw new ArgumentNullException(nameof(track));
        Traffic = traffic ?? throw new ArgumentNullException(nameof(traffic));
    }

    public bool CanMove(string shuttleId, string destinationLane)
    {
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
        var currentLane = Occupancy.GetOccupiedLane(shuttleId);
        if (currentLane is null)
        {
            return false;
        }

        var decision = Traffic.TryReserveRoute(shuttleId, currentLane, destinationLane);
        return decision.Granted;
    }
}