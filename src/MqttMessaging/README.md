# src/MqttMessaging

The MQTT implementation of `IMessageBus`. Wires `Messaging`'s seam to the real broker (MQTTnet) and to configuration-driven QoS per `MessageFamily`.

## Depends on

- `Contracts`, `Messaging`
- NuGet: `MQTTnet` (5.x)

## Used by

- `FleetManager`, `ShuttleSim`, and their tests (the tests use an in-process `MqttServer` so they stay broker-free).
- `MqttMessaging.Tests` (over an in-process broker).

## Overview

This is the transport layer — it moves bytes between the app and the MQTT broker. It never interprets message content.

```text
  App (FleetManager / ShuttleSim)
      │
      │  PublishAsync(topic, body, family)
      ▼
  MqttMessageBus
      │  1. looks up QoS from MessageFamily via QosOptions (config-driven)
      │  2. serializes the envelope to JSON
      │  3. sends an MQTT PUBLISH to the broker at the given topic
      ▼
  MQTT broker (Mosquitto)
      │
      │  MQTT PUBLISH from the broker
      ▼
  MqttMessageBus
      │  1. matches the topic against registered SubscribeAsync filters
      │  2. deserializes the envelope
      │  3. dispatches to the handler the app registered
      ▼
  App handler (e.g. ShuttleEndpoint.HandleCommandAsync)
```

**Why this exists**: FleetManager and ShuttleSim ask for `IMessageBus` (from
`Messaging`) and never see MQTTnet. Their tests inject a fake bus; the same code
runs against the fake and the real broker. The concrete bus is chosen once in
`Program.cs`.

**Adding a new message kind** requires a new arm in `ToMqttQos` plus a config
key, or every publish throws `ArgumentOutOfRangeException`. See records 08 and 21.

## The files

| File | Where it fits |
|---|---|
| `MqttMessageBus.cs` | The `IMessageBus` implementation over MQTTnet: connect, publish, subscribe, dispatch inbound messages by topic filter. |
| `MqttMessageBusOptions.cs` | The config record: host, port, client id, and the per-family `QosOptions`. |
| `MqttQualityOfService.cs` | The enum `AtMostOnce`/`AtLeastOnce`/`ExactlyOnce`. |
