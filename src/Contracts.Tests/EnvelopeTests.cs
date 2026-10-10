using System.Text.Json;
using Contracts;

public sealed class EnvelopeTests
{
	[Fact]
	public void Envelope_round_trips_with_contract_version_and_correlation()
	{
		var envelope = new MessageEnvelope(
			ContractVersion.Current,
			MessageTypes.CommandAck,
			"ack-123",
			"command-456",
			"SH-01",
			new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));

		var json = JsonSerializer.Serialize(envelope);
		var roundTripped = JsonSerializer.Deserialize<MessageEnvelope>(json);

		Assert.NotNull(roundTripped);
		Assert.Equal(ContractVersion.Current, roundTripped.ContractVersion);
		Assert.Equal("command-456", roundTripped.CorrelationId);
		Assert.Equal("ack-123", roundTripped.MessageId);
	}

	[Fact]
	public void Message_kind_is_read_from_the_envelope_before_the_body()
	{
		var envelope = new MessageEnvelope(
			ContractVersion.Current,
			MessageTypes.MoveCommand,
			"command-123",
			null,
			"fleet-manager",
			new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc));
		var json = JsonSerializer.Serialize(new MoveCommandMessage(envelope, "SH-01", "A01-L02"));

		using var document = JsonDocument.Parse(json);
		var parsedEnvelope = document.RootElement
			.GetProperty("Envelope")
			.Deserialize<MessageEnvelope>();

		Assert.NotNull(parsedEnvelope);
		Assert.Equal(MessageTypes.MoveCommand, parsedEnvelope.MessageType);

		var command = JsonSerializer.Deserialize<MoveCommandMessage>(json);
		Assert.NotNull(command);
		Assert.Equal("A01-L02", command.ToLane);
	}
}