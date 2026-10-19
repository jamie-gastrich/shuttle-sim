namespace FleetCore;

public sealed record SubmitOrderRequest(string? OrderId, string? Kind, string? PalletId, string? LaneId);

public sealed record WmsOrder(
    string OrderId,
    string Kind,
    string PalletId,
    string LaneId,
    string State,
    DateTime ReceivedAt);

public enum SubmitOutcome { Created, AlreadyKnown, Conflict }

public sealed record SubmitResult(SubmitOutcome Outcome, WmsOrder Order);