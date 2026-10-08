using System.Text.Json;

namespace Contracts;

public record ShuttleStatus(
    string ShuttleId,
    string State,
    string Position,
    DateTime Timestamp,
    string? FaultReason
);