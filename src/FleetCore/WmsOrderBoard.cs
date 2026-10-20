namespace FleetCore;

public sealed class WmsOrderBoard
{
    private readonly Dictionary<string, WmsOrder> _orders = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly TaskBoard _tasks;

    public WmsOrderBoard(TaskBoard tasks)
       => _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

    public SubmitResult Submit(SubmitOrderRequest request, DateTime now)
    {
        lock (_gate)
        {
            if (_orders.TryGetValue(request.OrderId!, out var existing))
            {
                return SameFields(existing, request)
                    ? new SubmitResult(SubmitOutcome.AlreadyKnown, existing, null)
                    : new SubmitResult(SubmitOutcome.Conflict, existing, null);
            }

            var order = new WmsOrder(
                request.OrderId!, request.Kind!, request.PalletId!, request.LaneId!,
                WmsOrderStates.Received, now);
            _orders.Add(order.OrderId, order);

            var created = CreateTask(order);
            _tasks.Add(created);

            return new SubmitResult(SubmitOutcome.Created, order, created);
        }
    }

    public bool TryGet(string orderId, out WmsOrder? order)
    {
        lock (_gate)
            return _orders.TryGetValue(orderId, out order);
    }

    private static FleetTask CreateTask(WmsOrder order)
        => new(order.OrderId, order.LaneId, order.ReceivedAt);

    private static bool SameFields(WmsOrder existing, SubmitOrderRequest request)
        => existing.Kind == request.Kind
        && existing.PalletId == request.PalletId
        && existing.LaneId == request.LaneId;

    public OrderSyncOutcome SyncFromTask(string taskId)
    {
        lock (_gate)
        {
            if (!_orders.TryGetValue(taskId, out var order))
                return OrderSyncOutcome.NoOrder;

            var target = WmsOrderLadder.ForTask(_tasks.Get(taskId).State);
            if (target == order.State)
                return OrderSyncOutcome.AlreadyThere;

            if (WmsOrderLadder.Rank(target) <= WmsOrderLadder.Rank(order.State))
                throw new OrderRejectedException($"The {target} is not above {order.State}");

            _orders[taskId] = order with { State = target };
            return OrderSyncOutcome.Advanced;
        }
    }
}