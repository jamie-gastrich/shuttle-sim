namespace MockWms;

public sealed record MockOrder(
    string OrderId,
    string Kind,
    string PalletId,
    string LaneId,
    string State,
    DateTime ReceivedAt)
{
    public static bool TryCreate(
        MockOrder? request,
        out MockOrder? order,
        out Dictionary<string, string[]> errors)
    {
        order = null;
        errors = new Dictionary<string, string[]>();

        if (request is null)
        {
            errors["body"] = ["A request body is required."];
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.OrderId))
            errors["orderId"] = ["orderId is required."];
        if (request.Kind is not ("store" or "retrieve"))
            errors["kind"] = ["kind must be 'store' or 'retrieve'."];
        if (string.IsNullOrWhiteSpace(request.PalletId))
            errors["palletId"] = ["palletId is required."];
        if (string.IsNullOrWhiteSpace(request.LaneId))
            errors["laneId"] = ["laneId is required."];
        if (request.State is not ("received" or "in_progress" or "done" or "failed" or "cancelled"))
            errors["state"] = ["state is not recognized."];
        if (request.ReceivedAt == default || request.ReceivedAt.Kind != DateTimeKind.Utc)
            errors["receivedAt"] = ["receivedAt must be a UTC timestamp."];

        if (errors.Count != 0)
            return false;

        order = request;
        return true;
    }
}
