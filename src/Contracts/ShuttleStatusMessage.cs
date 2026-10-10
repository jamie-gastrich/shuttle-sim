namespace Contracts;

public sealed record ShuttleStatusMessage(MessageEnvelope Envelope, ShuttleStatus Status);