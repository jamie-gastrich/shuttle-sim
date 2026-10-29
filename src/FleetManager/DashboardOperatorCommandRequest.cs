namespace FleetManager;

public sealed record DashboardOperatorCommandRequest(string? ShuttleId, string? Kind, string? Reason);
