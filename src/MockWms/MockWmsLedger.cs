namespace MockWms;

public enum MockWmsApplyResult
{
    Applied,
    Duplicate,
    Stale,
    Conflict,
}

public sealed record MockWmsLedgerCounts(int Applied, int Duplicate, int Stale, int Conflict);

public sealed class MockWmsLedger
{
    private readonly object _gate = new();
    private readonly Dictionary<string, MockOrder> _orders = new(StringComparer.Ordinal);
    private int _applied;
    private int _duplicate;
    private int _stale;
    private int _conflict;

    public MockWmsApplyResult Apply(MockOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);

        lock (_gate)
        {
            if (_orders.TryGetValue(order.OrderId, out var old))
            {
                if (old.Kind != order.Kind || old.PalletId != order.PalletId || old.LaneId != order.LaneId)
                {
                    _conflict++;
                    return MockWmsApplyResult.Conflict;
                }

                if (old.State == order.State)
                {
                    _duplicate++;
                    return MockWmsApplyResult.Duplicate;
                }

                if (Rank(order.State) <= Rank(old.State))
                {
                    _stale++;
                    return MockWmsApplyResult.Stale;
                }
            }

            _orders[order.OrderId] = order;
            _applied++;
            return MockWmsApplyResult.Applied;
        }
    }

    public bool TryGet(string orderId, out MockOrder? order)
    {
        lock (_gate)
            return _orders.TryGetValue(orderId, out order);
    }

    public IReadOnlyList<MockOrder> Snapshot()
    {
        lock (_gate)
            return _orders.Values.OrderBy(order => order.OrderId, StringComparer.Ordinal).ToList();
    }

    public MockWmsLedgerCounts Counts()
    {
        lock (_gate)
            return new MockWmsLedgerCounts(_applied, _duplicate, _stale, _conflict);
    }

    public void Reset()
    {
        lock (_gate)
        {
            _orders.Clear();
            _applied = 0;
            _duplicate = 0;
            _stale = 0;
            _conflict = 0;
        }
    }

    private static int Rank(string state) => state switch
    {
        "received" => 0,
        "in_progress" => 1,
        "done" => 2,
        "failed" => 2,
        "cancelled" => 3,
        _ => throw new ArgumentException($"Unknown order state '{state}'.", nameof(state)),
    };
}
