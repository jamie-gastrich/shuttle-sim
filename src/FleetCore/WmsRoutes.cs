namespace FleetCore;

public static class WmsRoutes
{
    public const string Orders = "/api/orders";
    public const string OrderStatus = "/api/order-status";
    public const string DeliveryFailures = "/wms/delivery-failures";
    public const string ShuttleBase = "/wms/shuttles";
    public const string TaskQueue = "/wms/task-queue";
    public const string WmsOrders = "/wms/orders";
    public const string WmsOrderCancelBase = "/wms/orders/cancel";
    public const string OrderCancelBase = "/wms/orders/cancel";

    // The route template uses "{orderId}"; the built path uses the value.
    public static string Order(string orderId) => $"{Orders}/{orderId}";
    public static string OrderCancel(string orderId) => $"{WmsOrders}/{orderId}/cancel";
    public static string WmsOrderCancel(string orderId) => $"{WmsOrders}/{orderId}/cancel";
    public static string Shuttle(string shuttleId) => $"{ShuttleBase}/{shuttleId}";
}