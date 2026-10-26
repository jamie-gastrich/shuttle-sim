namespace MockWms;

public sealed record MockModeRequest(string? Mode, int? SlowDelayMs);
public sealed record MockWmsControlState(string Mode, int SlowDelayMs);

public sealed class MockWmsControl
{
    public const string Ok = "ok";
    public const string Slow = "slow";
    public const string Error = "error";
    public const string Hang = "hang";

    private readonly object _gate = new();
    private string _mode = Ok;
    private int _slowDelayMs;

    public MockWmsControl(int slowDelayMs)
    {
        if (slowDelayMs < 1)
            throw new ArgumentOutOfRangeException(nameof(slowDelayMs), "Slow delay must be positive.");
        _slowDelayMs = slowDelayMs;
    }

    public MockWmsControlState Snapshot()
    {
        lock (_gate)
            return new MockWmsControlState(_mode, _slowDelayMs);
    }

    public bool TrySetMode(
        MockModeRequest? request,
        out MockWmsControlState state,
        out Dictionary<string, string[]> errors)
    {
        errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["body"] = ["A request body is required."];
            state = Snapshot();
            return false;
        }

        if (request.Mode is not (Ok or Slow or Error or Hang))
            errors["mode"] = ["mode must be 'ok', 'slow', 'error', or 'hang'."];
        if (request.SlowDelayMs is <= 0)
            errors["slowDelayMs"] = ["slowDelayMs must be positive."];

        if (errors.Count != 0)
        {
            state = Snapshot();
            return false;
        }

        lock (_gate)
        {
            _mode = request.Mode!;
            if (request.SlowDelayMs is int delay)
                _slowDelayMs = delay;
            state = new MockWmsControlState(_mode, _slowDelayMs);
        }

        return true;
    }
}
