namespace TrafficCore;

public class SegmentReservations
{
    private readonly Dictionary<string, string> _ownerBySegment = new(StringComparer.Ordinal);

    public bool TryReserveAll(string ownerId, IEnumerable<string> segmentIds)
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

    public void Release(string ownerId)
    {
        foreach (var segmentId in _ownerBySegment
                     .Where(kvp => string.Equals(kvp.Value, ownerId, StringComparison.Ordinal))
                     .Select(kvp => kvp.Key)
                     .ToList())
        {
            _ownerBySegment.Remove(segmentId);
        }
    }

    public string? OwnerOf(string segmentId)
    {
        return _ownerBySegment.TryGetValue(segmentId, out var owner)
            ? owner
            : null;
    }

    public IReadOnlyCollection<string> SegmentsOwnedBy(string ownerId)
    {
        return _ownerBySegment
            .Where(kvp => string.Equals(kvp.Value, ownerId, StringComparison.Ordinal))
            .Select(kvp => kvp.Key)
            .ToArray();
    }
}