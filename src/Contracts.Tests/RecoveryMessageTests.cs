using System.Text.Json;

namespace Contracts.Tests;

public sealed class RecoveryMessageTests
{

    [Fact]
    public void Recovery_messages_survive_json_roundtrip()
    {
		var commandEnvelope = new MessageEnvelope(
			ContractVersion.Current,
			MessageTypes.RecoveryCommand,
			"Rec-123",
			null,
			"fleet-manager",
			new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
		);
        var commandMessage = new RecoveryCommandMessage(commandEnvelope, "SH-01");


		var ackEnvelope = new MessageEnvelope(
			ContractVersion.Current,
			MessageTypes.RecoveryAck,
			"Rec-124",
			commandEnvelope.MessageId,
			"SH-01",
			new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc)
		);
        var ackMessage = new RecoveryAckMessage(ackEnvelope, true, null);

        var commandJson = JsonSerializer.Serialize(commandMessage);
        var ackJson = JsonSerializer.Serialize(ackMessage);

        var deserializedCommand = JsonSerializer.Deserialize<RecoveryCommandMessage>(commandJson);
        var deserializedAck = JsonSerializer.Deserialize<RecoveryAckMessage>(ackJson);

        Assert.Equal(commandMessage, deserializedCommand);
        Assert.Equal(ackMessage, deserializedAck);
        Assert.Equal("recoveryCommand", MessageTypes.RecoveryCommand);
        Assert.Equal("recoveryAck", MessageTypes.RecoveryAck);
        Assert.Equal(4, ContractVersion.Current);
        Assert.Equal(commandMessage.Envelope.MessageId, ackMessage.Envelope.CorrelationId);
        Assert.Contains("\"recoveryAck\"", ackJson);
    }
}