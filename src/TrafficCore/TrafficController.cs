using ShuttleCore;

namespace TrafficCore;

public class TrafficController
{
    private readonly Track _track;
    private readonly SegmentReservations _reservations;

    public TrafficController(Track track, SegmentReservations reservations)
    {
        _track = track ?? throw new ArgumentNullException(nameof(track));
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
    }

    public TrafficDecision TryReserveRoute(string shuttleId, string fromLane, string toLane)
    {
        var route = _track.Route(fromLane, toLane);
        var granted = _reservations.TryReserveAll(shuttleId, route);

        if (granted)
        {
            return new TrafficDecision(true, shuttleId, route, null);
        }

        var blockedSegment = route.FirstOrDefault(segmentId =>
            _reservations.OwnerOf(segmentId) is string owner && !string.Equals(owner, shuttleId, StringComparison.Ordinal));

        var blockedOwner = blockedSegment is null
            ? "unknown"
            : _reservations.OwnerOf(blockedSegment) ?? "unknown";

        return new TrafficDecision(false, shuttleId, route, blockedSegment is null
            ? "route unavailable"
            : $"{blockedSegment} held by {blockedOwner}");
    }

    public void Release(string shuttleId)
    {
        _reservations.Release(shuttleId);
    }
}