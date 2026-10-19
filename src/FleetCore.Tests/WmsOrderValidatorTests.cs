namespace FleetCore.Tests;

public class WmsOrderValidatorTests
{
    private static SubmitOrderRequest Valid() =>
        new(OrderId: "ORD-1", Kind: WmsOrderKinds.Store, PalletId: "PLT-001", LaneId: "A01-L01");

    [Fact]
    public void Valid_order_has_no_errors()
    {
        var errors = WmsOrderValidator.Validate(Valid());

        Assert.Empty(errors);
    }

    [Fact]
    public void Unknown_kind_gives_only_a_kind_error()
    {
        var errors = WmsOrderValidator.Validate(Valid() with { Kind = "move" });

        var error = Assert.Single(errors);              // kills "a second field is flagged too"
        Assert.Equal("kind", error.Key);                // kills "wrong wire key"
        Assert.Contains("store", error.Value[0]);       // kills "message dropped the valid kinds"
    }

    [Fact]
    public void Blank_required_fields_give_all_three_errors_at_once()
    {
        var errors = WmsOrderValidator.Validate(
            Valid() with { OrderId = "  ", PalletId = null, LaneId = "" });

        Assert.Equal(3, errors.Count);                  // <-- THIS kills mutation (a)
        Assert.Contains("orderId", errors.Keys);
        Assert.Contains("palletId", errors.Keys);
        Assert.Contains("laneId", errors.Keys);
        Assert.DoesNotContain("kind", errors.Keys);     // kind is still valid here
    }

    [Fact]
    public void Missing_kind_is_the_same_error_as_an_unknown_kind()
    {
        var missing = WmsOrderValidator.Validate(Valid() with { Kind = null });
        var unknown = WmsOrderValidator.Validate(Valid() with { Kind = "move" });

        Assert.Equal(missing["kind"], unknown["kind"]); // one rule, one message
    }
}