namespace TrafficCore;

public class SegmentReservations
{
    private readonly Dictionary<string, string> _ownerBySegment = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public bool TryReserveAll(string ownerId, IEnumerable<string> segmentIds)
    {
        lock (_lock)
        {
            var requested = segmentIds.Distinct(StringComparer.Ordinal).ToList();

            foreach (var segmentId in requested)
            {
                var currentOwner = OwnerOf(segmentId);
                if (currentOwner is not null && !string.Equals(currentOwner, ownerId, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            foreach (var segmentId in requested)
            {
                _ownerBySegment[segmentId] = ownerId;
            }

            return true;
        }
    }

    public void Release(string ownerId)
    {
        lock (_lock)
        {
            foreach (var segmentId in _ownerBySegment
                         .Where(kvp => string.Equals(kvp.Value, ownerId, StringComparison.Ordinal))
                         .Select(kvp => kvp.Key)
                         .ToList())
            {
                _ownerBySegment.Remove(segmentId);
            }
        }
    }

    public string? OwnerOf(string segmentId)
    {
        lock (_lock)
        {
            return _ownerBySegment.TryGetValue(segmentId, out var owner)
                ? owner
                : null;
        }
    }

    public bool IsOccupiedByOthers(IEnumerable<string> segmentIds, string requesterId)
    {
        lock (_lock)
        {
            ArgumentNullException.ThrowIfNull(segmentIds);

            if (string.IsNullOrWhiteSpace(requesterId))
            {
                throw new ArgumentException("A requester id is required.", nameof(requesterId));
            }

            foreach (var segmentId in segmentIds.Distinct(StringComparer.Ordinal))
            {
                var owner = OwnerOf(segmentId);
                if (owner is not null && !string.Equals(owner, requesterId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public IReadOnlyCollection<string> SegmentsOwnedBy(string ownerId)
    {
        lock (_lock)
        {
            return _ownerBySegment
                .Where(kvp => string.Equals(kvp.Value, ownerId, StringComparison.Ordinal))
                .Select(kvp => kvp.Key)
                .ToArray();
        }
    }
}