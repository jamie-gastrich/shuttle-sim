using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Contracts;
using Messaging;
using MqttMessaging;
using FleetManager;

if (args.Length == 0)
{
	Console.WriteLine("Fleet Manager Coming Online");
	return 0;
}

if (args.Length != 3 || args[0] != "move")
{
	Console.Error.WriteLine("Usage: FleetManager move <shuttleId> <toLane>");
	return 2;
}

return await RunMoveAsync(args[1], args[2]);

async Task<int> RunMoveAsync(string shuttleId, string toLane)
{
	var configuration = new ConfigurationBuilder()
		.SetBasePath(AppContext.BaseDirectory)
		.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
		.AddEnvironmentVariables()
		.Build();

	var messagingSection = configuration.GetSection("Messaging");

	var messaging = messagingSection.Get<MessagingOptions>()
		?? throw new InvalidOperationException("Messaging configuration is missing.");

	var mqtt = messagingSection.Get<MqttMessageBusOptions>()
		?? throw new InvalidOperationException("MQTT configuration is missing.");

	var timeoutMs = configuration.GetValue<int>("FleetManager:RoundTripTimeoutMs");
	if (timeoutMs < 1)
		throw new InvalidOperationException("FleetManager:RoundTripTimeoutMs must be positive.");

	var ackTimeoutMs = configuration.GetValue<int>("FleetManager:AckTimeoutMs");
	if (ackTimeoutMs < 1)
		throw new InvalidOperationException("FleetManager:AckTimeoutMs must be positive.");

	var maxAttempts = configuration.GetValue<int>("FleetManager:MaxAttempts");
	if (maxAttempts < 1)
		throw new InvalidOperationException("FleetManager:MaxAttempts must be positive.");

	var backoffMs = configuration.GetValue<int>("FleetManager:BackoffMs");
	if (backoffMs < 0)
		throw new InvalidOperationException("FleetManager:BackoffMs must not be negative.");

	var topics = new TopicScheme(messaging.TopicRoot);

	var commandEnvelope = MessageEnvelopes.Create(
		MessageTypes.MoveCommand, messaging.FleetManagerId, DateTime.UtcNow);

	var command = new MoveCommandMessage(commandEnvelope, shuttleId, toLane);
	var statusReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

	await using var bus = new MqttMessageBus(mqtt);
	await bus.ConnectAsync(CancellationToken.None);
	var retry = new RetryPolicy(
		maxAttempts,
		TimeSpan.FromMilliseconds(ackTimeoutMs),
		TimeSpan.FromMilliseconds(backoffMs));
	var sender = new ReliableCommandSender(bus, topics, retry);
	using var overall = new CancellationTokenSource();
	overall.CancelAfter(timeoutMs);

	await bus.SubscribeAsync(topics.AllAcks, MessageFamily.Ack, HandleAckAsync, CancellationToken.None);
	await bus.SubscribeAsync(topics.AllStatuses, MessageFamily.Status, HandleStatusAsync, CancellationToken.None);
	try
	{
		var result = await sender.SendAsync(command, overall.Token);
		switch (result.Status)
		{
			case CommandSendStatus.Acknowledged:
				await statusReceived.Task.WaitAsync(overall.Token);
				return 0;
			case CommandSendStatus.Rejected:
				Console.Error.WriteLine(result.Acknowledgement?.Reason ?? "Command rejected.");
				return 1;
			case CommandSendStatus.NoAcknowledgement:
				Console.Error.WriteLine(
					$"No acknowledgement for shuttle {shuttleId}, command {commandEnvelope.MessageId}, after {result.Attempts} attempts.");
				return 1;
			default:
				throw new InvalidOperationException($"Unexpected command result: {result.Status}.");
		}
	}
	catch (OperationCanceledException)
	{
		return 1;
	}

	async Task HandleAckAsync(MessageReceived message, CancellationToken cancellationToken)
	{
		Console.WriteLine(message.Payload);
		await sender.OnAckReceivedAsync(message, cancellationToken);
	}

	Task HandleStatusAsync(MessageReceived message, CancellationToken _)
	{
		Console.WriteLine(message.Payload);
		try
		{
			var status = JsonSerializer.Deserialize<ShuttleStatusMessage>(message.Payload);
			if (status?.Status?.ShuttleId == shuttleId)
				statusReceived.TrySetResult();
		}
		catch (JsonException)
		{
		}

		return Task.CompletedTask;
	}
}