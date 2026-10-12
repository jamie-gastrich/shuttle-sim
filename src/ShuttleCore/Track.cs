namespace ShuttleCore;

public sealed class Track
{
    private readonly IReadOnlyList<string> _segments;

    public Track(Rack rack)
    {
        ArgumentNullException.ThrowIfNull(rack);

        _segments = rack.LaneId.ToList();
    }

    public IReadOnlyList<string> SegmentIds => _segments;

    public bool IsSegment(string segmentId)
    {
        if (string.IsNullOrWhiteSpace(segmentId))
        {
            return false;
        }

        return _segments.Contains(segmentId, StringComparer.Ordinal);
    }

    public IReadOnlyList<string> Route(string fromLane, string toLane)
    {
        if (!IsSegment(fromLane))
        {
            throw new ArgumentException($"unknown lane {fromLane}", nameof(fromLane));
        }

        if (!IsSegment(toLane))
        {
            throw new ArgumentException($"unknown lane {toLane}", nameof(toLane));
        }

        var orderedSegments = _segments.ToList();
        var fromIndex = orderedSegments.IndexOf(fromLane);
        var toIndex = orderedSegments.IndexOf(toLane);
        var start = Math.Min(fromIndex, toIndex);
        var end = Math.Max(fromIndex, toIndex);

        return orderedSegments.Skip(start).Take(end - start + 1).ToList();
    }
}