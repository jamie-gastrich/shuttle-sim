namespace Contracts;

public sealed record Alarm(
    string ShuttleId,
    string Severity,
    string Code,
    string? Detail
);