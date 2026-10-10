using Contracts;

namespace Messaging;

public static class MessageEnvelopes
{
    public static MessageEnvelope Create(string messageType, string senderId, DateTime sentAt,
        string? correlationId = null) =>
        new(ContractVersion.Current, messageType, Guid.NewGuid().ToString(), correlationId, senderId, sentAt);
}