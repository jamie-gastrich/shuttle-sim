namespace Messaging;

public sealed record MessageReceived(string Topic, string Payload);