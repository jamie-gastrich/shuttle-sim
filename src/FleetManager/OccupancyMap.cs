namespace FleetManager;

public sealed class OccupancyMap
{
    private readonly Dictionary<string, string> _shuttleToLane = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public string? GetOccupiedLane(string shuttleId)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(shuttleId))
            {
                throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));
            }

            return _shuttleToLane.TryGetValue(shuttleId, out var lane)
                ? lane
                : null;
        }
    }

    public void SetOccupiedLane(string shuttleId, string lane)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(shuttleId))
            {
                throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));
            }

            if (string.IsNullOrWhiteSpace(lane))
            {
                throw new ArgumentException("The occupied lane is required.", nameof(lane));
            }

            _shuttleToLane[shuttleId] = lane;
        }
    }

    public bool IsOccupiedByOthers(IEnumerable<string> route, string requesterId)
    {
        lock (_lock)
        {
            ArgumentNullException.ThrowIfNull(route);

            if (string.IsNullOrWhiteSpace(requesterId))
            {
                throw new ArgumentException("A requester id is required.", nameof(requesterId));
            }

            foreach (var segment in route.Distinct(StringComparer.Ordinal))
            {
                foreach (var (shuttleId, lane) in _shuttleToLane)
                {
                    if (string.Equals(shuttleId, requesterId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (string.Equals(lane, segment, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}