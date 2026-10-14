namespace FleetManager;

public sealed class ShuttleHealth
{
    private readonly TimeSpan _livenessTimeout;
    private readonly object _lock = new();
    private readonly Dictionary<string, DateTime> _lastSeenAt = new(StringComparer.Ordinal);

    public ShuttleHealth(TimeSpan livenessTimeout)
    {
        // reject a non-positive window
        if (livenessTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(livenessTimeout));
        _livenessTimeout = livenessTimeout;
    }

    public void RecordSeen(string shuttleId, DateTime seenAt)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(shuttleId))
            {
                throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));
            }
            if (!_lastSeenAt.TryGetValue(shuttleId, out var previous) || seenAt > previous) 
            {
                _lastSeenAt[shuttleId] = seenAt;
            }
        }
    }

    public DateTime? LastSeenAt(string shuttleId)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(shuttleId))
            {
                throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));
            }
            // return the stored stamp, or null if we have never heard from it
            return _lastSeenAt.GetValueOrDefault(shuttleId);
        }
    }

    public bool IsUnresponsive(string shuttleId, DateTime now)
    {
        lock (_lock)
        {
            if (string.IsNullOrWhiteSpace(shuttleId))
            {
                throw new ArgumentException("A shuttle id is required.", nameof(shuttleId));
            }
            return !_lastSeenAt.TryGetValue(shuttleId, out var lastSeen) || now - lastSeen > _livenessTimeout;
        }
    }

    public IReadOnlyCollection<string> UnresponsiveShuttles(DateTime now)
    {
        lock (_lock)
        {
            return _lastSeenAt.Where(entry => now - entry.Value > _livenessTimeout).Select(entry => entry.Key).ToList();
        }
    }
}