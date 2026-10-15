namespace MqttMessaging;

public sealed record MqttMessageBusOptions(string Host, int Port, string ClientId, QosOptions Qos);
public sealed record QosOptions(MqttQualityOfService Command, MqttQualityOfService Ack, MqttQualityOfService Status, MqttQualityOfService Alarm);