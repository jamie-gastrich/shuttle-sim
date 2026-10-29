using System.Buffers;
using System.Text;
using MQTTnet;
using MQTTnet.Protocol;
using Messaging;

namespace MqttMessaging;

public sealed class MqttMessageBus : IMessageBus
{
	private readonly IMqttClient _client;
	private readonly MqttMessageBusOptions _options;
	private readonly List<(string Filter, Func<MessageReceived, CancellationToken, Task> Handler)> _subscriptions = new();
	private readonly object _subscriptionsLock = new();

	public MqttMessageBus(MqttMessageBusOptions options)
	{
		_options = options;
		_client = new MqttClientFactory().CreateMqttClient();
		_client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
	}

	public async Task ConnectAsync(CancellationToken cancellationToken)
	{
		var options = new MqttClientOptionsBuilder()
			.WithTcpServer(_options.Host, _options.Port)
			.WithClientId(_options.ClientId)
			.Build();

		await _client.ConnectAsync(options, cancellationToken);
	}

	public Task PublishAsync(string topic, string payload, MessageFamily family,
		CancellationToken cancellationToken)
	{
		var message = new MqttApplicationMessageBuilder()
			.WithTopic(topic)
			.WithPayload(payload)
			.WithQualityOfServiceLevel(ToMqttQos(family))
			.Build();

		return _client.PublishAsync(message, cancellationToken);
	}

	public async Task SubscribeAsync(string topicFilter, MessageFamily family,
		Func<MessageReceived, CancellationToken, Task> handler, CancellationToken cancellationToken)
	{
		lock (_subscriptionsLock)
			_subscriptions.Add((topicFilter, handler));

		try
		{
			var options = new MqttClientSubscribeOptionsBuilder()
				.WithTopicFilter(filter => filter
					.WithTopic(topicFilter)
					.WithQualityOfServiceLevel(ToMqttQos(family)))
				.Build();

			await _client.SubscribeAsync(options, cancellationToken);
		}
		catch
		{
			lock (_subscriptionsLock)
				_subscriptions.RemoveAll(subscription => subscription.Filter == topicFilter &&
					subscription.Handler == handler);
			throw;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_client.IsConnected)
			await _client.DisconnectAsync();

		_client.Dispose();
	}

	private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs eventArgs)
	{
		var message = new MessageReceived(
			eventArgs.ApplicationMessage.Topic,
			Encoding.UTF8.GetString(eventArgs.ApplicationMessage.Payload.ToArray()));

		(string Filter, Func<MessageReceived, CancellationToken, Task> Handler)[] subscriptions;
		lock (_subscriptionsLock)
			subscriptions = _subscriptions.ToArray();

		foreach (var subscription in subscriptions)
		{
			if (Matches(subscription.Filter, message.Topic))
				await subscription.Handler(message, CancellationToken.None);
		}
	}

	private MqttQualityOfServiceLevel ToMqttQos(MessageFamily family)
	{
		var qualityOfService = family switch
		{
			MessageFamily.Command => _options.Qos.Command,
			MessageFamily.Ack => _options.Qos.Ack,
			MessageFamily.Status => _options.Qos.Status,
			MessageFamily.Alarm => _options.Qos.Alarm,
			MessageFamily.Recovery => _options.Qos.Command,
			MessageFamily.Json => _options.Qos.Command,
			_ => throw new ArgumentOutOfRangeException(nameof(family))
		};

		return (MqttQualityOfServiceLevel)(int)qualityOfService;
	}

	private static bool Matches(string filter, string topic)
	{
		var filterLevels = filter.Split('/');
		var topicLevels = topic.Split('/');

		for (var index = 0; index < filterLevels.Length; index++)
		{
			if (filterLevels[index] == "#")
				return index == filterLevels.Length - 1;

			if (index >= topicLevels.Length)
				return false;

			if (filterLevels[index] != "+" && filterLevels[index] != topicLevels[index])
				return false;
		}

		return filterLevels.Length == topicLevels.Length;
	}
}
