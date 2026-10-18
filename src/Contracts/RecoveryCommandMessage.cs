namespace Contracts;

public sealed record RecoveryCommandMessage(MessageEnvelope Envelope, string ShuttleId);