namespace FleetCore.Tests;

public class WmsContractLiteralsTests
{
    [Fact] public void Store_wire_name()  => Assert.Equal("store", WmsOrderKinds.Store);
    [Fact] public void Retrieve_wire_name() => Assert.Equal("retrieve", WmsOrderKinds.Retrieve);
    [Fact] public void Received_wire_name() => Assert.Equal("received", WmsOrderStates.Received);

    [Fact]
    public void Orders_route() => Assert.Equal("/api/orders", WmsRoutes.Orders);

    [Fact]
    public void Order_status_route_is_pinned() => Assert.Equal("/api/order-status", WmsRoutes.OrderStatus);

    [Fact]
    public void Order_route_builds_the_path() =>
        Assert.Equal("/api/orders/ORD-1", WmsRoutes.Order("ORD-1"));
}