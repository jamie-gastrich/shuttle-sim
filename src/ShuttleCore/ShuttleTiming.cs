namespace ShuttleCore;

public sealed record ShuttleTiming(TimeSpan MovePerLane, TimeSpan Load, TimeSpan Unload);