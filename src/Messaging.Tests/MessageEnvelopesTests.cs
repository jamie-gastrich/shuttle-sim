namespace Messaging.Tests;
using Messaging;
using Contracts;

public sealed class MessageEnvelopesTests
{
	[Fact]
	public void MessageEnvelopes_generate_a_unique_id_and_version_two()
	{
		var first = MessageEnvelopes.Create(MessageTypes.MoveCommand, "fleet-manager", DateTime.UtcNow);
		var second = MessageEnvelopes.Create(MessageTypes.MoveCommand, "fleet-manager", DateTime.UtcNow);

		Assert.Equal(ContractVersion.Current, first.ContractVersion);
		Assert.NotEqual(first.MessageId, second.MessageId);
		Assert.Equal(MessageTypes.MoveCommand, first.MessageType);
		Assert.Null(first.CorrelationId);
	}
}
