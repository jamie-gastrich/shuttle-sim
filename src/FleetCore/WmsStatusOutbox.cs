namespace FleetCore;

public sealed class WmsStatusOutbox
{
    private readonly HashSet<string> _pending = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public void NoteSync(string taskId, OrderSyncOutcome outcome)
    {
        if (outcome == OrderSyncOutcome.Advanced)
            MarkPending(taskId);
    }

    public void MarkPending(string orderId)
    {
        lock (_gate)
        {
            _pending.Add(orderId);
        }

    }

    public List<string> Claim()
    {
        lock (_gate)
        {
            var copy = new List<string>(_pending);
            _pending.Clear();
            return copy;
        }

    }

    public int PendingCount
    {
        get
        {
            lock (_gate)
            {
                return _pending.Count;
            }
        }
    }

}