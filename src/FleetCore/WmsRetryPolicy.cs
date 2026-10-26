namespace FleetCore;

public sealed class WmsRetryPolicy
{
    private readonly TimeSpan _baseDelay;
    private readonly TimeSpan _maxDelay;

    public WmsRetryPolicy(TimeSpan baseDelay, TimeSpan maxDelay)
    {
        if (baseDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(baseDelay), "Base delay must be positive.");
        if (maxDelay < baseDelay)
            throw new ArgumentOutOfRangeException(nameof(maxDelay), "Maximum delay must be at least the base delay.");

        _baseDelay = baseDelay;
        _maxDelay = maxDelay;
    }

    public TimeSpan DelayFor(int failures)
    {
        if (failures < 1)
            throw new ArgumentOutOfRangeException(nameof(failures), "Failure count must be positive.");

        var delay = _baseDelay;
        for (var failure = 1; failure < failures && delay < _maxDelay; failure++)
        {
            delay = delay.Ticks > _maxDelay.Ticks / 2
                ? _maxDelay
                : TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, _maxDelay.Ticks));
        }

        return delay;
    }
}
