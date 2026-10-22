namespace Contracts;

public sealed record OperatorCommandMessage
{
    public enum Command { Pause, Resume, Acknowledge }

    public required string ShuttleId { get; init; }
    public Command Kind { get; init; }
    public string? Reason { get; init; }
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString();
}