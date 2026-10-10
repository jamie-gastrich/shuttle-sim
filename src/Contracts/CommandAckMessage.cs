namespace Contracts;
public sealed record CommandAckMessage(MessageEnvelope Envelope, bool Accepted, string? Reason);