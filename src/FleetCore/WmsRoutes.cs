namespace FleetCore;

public static class WmsRoutes
{
    public const string Orders = "/api/orders";
    public const string OrderStatus = "/api/order-status";

    // The route template uses "{orderId}"; the built path uses the value.
    public static string Order(string orderId) => $"{Orders}/{orderId}";
}