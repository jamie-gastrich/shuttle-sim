namespace Contracts;

public sealed record RecoveryAckMessage(MessageEnvelope Envelope, bool Accepted, string? Reason);