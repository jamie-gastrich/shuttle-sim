namespace FleetCore;

public sealed class PauseState
{
    private readonly object _gate = new();
    private readonly HashSet<string> _paused = new(StringComparer.Ordinal);

    public void Pause(string shuttleId)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) return;
        lock (_gate) { _paused.Add(shuttleId); }
    }

    public void Resume(string shuttleId)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) return;
        lock (_gate) { _paused.Remove(shuttleId); }
    }

    public bool IsPaused(string shuttleId)
    {
        if (string.IsNullOrWhiteSpace(shuttleId)) return false;
        lock (_gate) { return _paused.Contains(shuttleId); }
    }

    public void PauseAll(IEnumerable<string>? shuttleIds)
    {
        if (shuttleIds is null) return;
        lock (_gate)
        {
            foreach (var shuttleId in shuttleIds)
                if (!string.IsNullOrWhiteSpace(shuttleId)) _paused.Add(shuttleId);
        }
    }

    public void ResumeAll(IEnumerable<string>? shuttleIds)
    {
        if (shuttleIds is null) return;
        lock (_gate)
        {
            foreach (var shuttleId in shuttleIds)
                if (!string.IsNullOrWhiteSpace(shuttleId)) _paused.Remove(shuttleId);
        }
    }

    public IReadOnlyCollection<string> Snapshot()
    {
        lock (_gate) { return _paused.ToArray(); }
    }
}
