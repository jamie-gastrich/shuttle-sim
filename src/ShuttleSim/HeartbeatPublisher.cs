public sealed class HeartbeatPublisher
{
    private readonly ShuttleEndpoint _endpoint;
    private readonly TimeSpan _interval;

    public HeartbeatPublisher(ShuttleEndpoint endpoint, TimeSpan interval)
    {
        if (interval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(interval));
        _endpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        _interval = interval;
    }

    public async Task RunAsync(Func<DateTime> clock, CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await _endpoint.PublishStatusAsync(clock(), cancellationToken);
    }
}