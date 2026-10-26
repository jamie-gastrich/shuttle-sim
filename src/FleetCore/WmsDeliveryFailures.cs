namespace FleetCore;

public sealed record WmsDeliveryFailure(string OrderId, string Reason);

public sealed class WmsDeliveryFailures
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _reasons = new(StringComparer.Ordinal);

    public void Record(string orderId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        lock (_gate)
            _reasons[orderId] = reason;
    }

    public void Clear(string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        lock (_gate)
            _reasons.Remove(orderId);
    }

    public IReadOnlyList<WmsDeliveryFailure> Snapshot()
    {
        lock (_gate)
        {
            return _reasons
                .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new WmsDeliveryFailure(pair.Key, pair.Value))
                .ToList();
        }
    }
}
