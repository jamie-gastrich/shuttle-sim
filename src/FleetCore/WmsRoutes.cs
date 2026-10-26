namespace FleetCore;

public static class WmsRoutes
{
    public const string Orders = "/api/orders";
    public const string OrderStatus = "/api/order-status";
    public const string DeliveryFailures = "/wms/delivery-failures";
    public const string ShuttleBase = "/wms/shuttles";
    public const string TaskQueue = "/wms/task-queue";
    public const string WmsOrders = "/wms/orders";

    // Canonical cancel path used by the WMS front door; the aliases below remain for compatibility.
    public const string OrderCancelBase = "/wms/orders/cancel";
    [Obsolete("Use OrderCancelBase for the canonical cancel route.")]
    public const string WmsOrderCancelBase = OrderCancelBase;

    // The route template uses "{orderId}"; the built path uses the value.
    public static string Order(string orderId) => $"{Orders}/{orderId}";
    public static string OrderCancel(string orderId) => $"{WmsOrders}/{orderId}/cancel";
    [Obsolete("Use OrderCancel(orderId) for the canonical cancel path.")]
    public static string WmsOrderCancel(string orderId) => OrderCancel(orderId);
    public static string Shuttle(string shuttleId) => $"{ShuttleBase}/{shuttleId}";
}