namespace FleetCore;

public static class WmsRoutes
{
    public const string Orders = "/api/orders";
    public const string OrderStatus = "/api/order-status";
    public const string DeliveryFailures = "/wms/delivery-failures";
    public const string ShuttleBase = "/wms/shuttles";
    public const string TaskQueue = "/wms/task-queue";

    // The route template uses "{orderId}"; the built path uses the value.
    public static string Order(string orderId) => $"{Orders}/{orderId}";
    public static string Shuttle(string shuttleId) => $"{ShuttleBase}/{shuttleId}";
}