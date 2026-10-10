namespace Contracts;

public sealed record MessageEnvelope(
    int ContractVersion,
    string MessageType,
    string MessageId,
    string? CorrelationId,
    string SenderId,
    DateTime SentAt
);