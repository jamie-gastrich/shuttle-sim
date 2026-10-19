namespace FleetCore;

public sealed class WmsOrderBoard
{
    private readonly Dictionary<string, WmsOrder> _orders = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public SubmitResult Submit(SubmitOrderRequest request, DateTime now)
    {
        lock (_gate)
        {
            if (_orders.TryGetValue(request.OrderId!, out var existing))
            {
                return SameFields(existing, request)
                    ? new SubmitResult(SubmitOutcome.AlreadyKnown, existing)
                    : new SubmitResult(SubmitOutcome.Conflict, existing);
            }

            var order = new WmsOrder(
                request.OrderId!, request.Kind!, request.PalletId!, request.LaneId!,
                WmsOrderStates.Received, now);

            _orders.Add(order.OrderId, order);
            return new SubmitResult(SubmitOutcome.Created, order);
        }
    }

    public bool TryGet(string orderId, out WmsOrder? order)
    {
        lock (_gate)
            return _orders.TryGetValue(orderId, out order);
    }

    private static bool SameFields(WmsOrder existing, SubmitOrderRequest request)
        => existing.Kind == request.Kind
        && existing.PalletId == request.PalletId
        && existing.LaneId == request.LaneId;
}