namespace TrafficCore;

public sealed record TrafficDecision(bool Granted, string ShuttleId, IReadOnlyList<string> Segments, string? Reason);
