namespace Contracts;
public sealed record MoveCommandMessage(MessageEnvelope Envelope, string ShuttleId, string ToLane);