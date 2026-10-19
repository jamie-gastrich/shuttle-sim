namespace FleetCore;

public static class WmsOrderValidator
{
    public static Dictionary<string, string[]> Validate(SubmitOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.OrderId))
            errors["orderId"] = ["orderId is required."];
        if (!WmsOrderKinds.IsKnown(request.Kind))
            errors["kind"] = ["kind must be 'store' or 'retrieve'."];
        if (string.IsNullOrWhiteSpace(request.PalletId))
            errors["palletId"] = ["palletId is required."];
        if (string.IsNullOrWhiteSpace(request.LaneId))
            errors["laneId"] = ["laneId is required."];

        return errors;
    }
}