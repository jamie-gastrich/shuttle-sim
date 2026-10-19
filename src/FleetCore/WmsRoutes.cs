namespace FleetCore;

public static class WmsRoutes
{
    public const string Orders = "/api/orders";

    // The route template uses "{orderId}"; the built path uses the value.
    public static string Order(string orderId) => $"{Orders}/{orderId}";
}