# src/Messaging

The seam. Abstractions and pure helpers for moving messages: the bus interface, topic scheme, envelopes, retry policy, idempotency guard. No broker, no database.

## Depends on

- `Contracts` (message bodies and types).

## Used by

- `MqttMessaging` (implements `IMessageBus` over MQTTnet)
- `FleetManager` (publishes commands, subscribes to status/acks, serves alarms)
- `ShuttleSim` (subscribes to commands, publishes status + heartbeat)
- Both `.Tests` projects (fakes, end-to-end)

## Overview

This is the seam between decision logic and the network. It defines *how* messages travel — which topic, which QoS family, which id, retry, dedupe — without touching a broker.

```text
  App code (FleetManager / ShuttleSim)
      │
      │  IMessageBus.PublishAsync(message, family, topic)
      │  IMessageBus.SubscribeAsync(filter, handler)
      ▼
  Messaging (this project)
      │  TopicScheme      — builds / parses {root}/{shuttleId}/{kind}
      │  MessageEnvelopes — fills MessageId, SenderId, SentAt
      │  MessageFamily    — labels the traffic lane (Command / Ack / Status / Alarm / Recovery)
      │  RetryPolicy      — pure backoff + attempt counting
      │  IdempotencyGuard — pure dedupe by MessageId
      ▼
  IMessageBus implementation (MqttMessaging in production, a fake in tests)
```

Nothing here opens a socket. Every helper is pure C# and unit-tests with no
services. The concrete bus is injected by the app entry point, so the same
decision code runs against a fake bus in tests and a real broker in production.

**Adding a new message kind** (e.g. Alarm) needs four edits: a `MessageFamily`
enum member, a `TopicScheme` branch + wildcard, a `ToMqttQos` arm in
`MqttMessaging`, and a config key for the QoS. See records 07 and 21.

## The files

| File | Where it fits |
|---|---|
| `IMessageBus.cs` | The single I/O seam: `ConnectAsync`, `PublishAsync`, `SubscribeAsync`, `DisposeAsync`. |
| `TopicScheme.cs` | Builds the topics `{root}/{shuttleId}/{kind}` and the wildcards `AllAcks`/`AllStatuses`/`AllAlarms`/`AllRecoveryAcks`. |
| `MessageEnvelopes.cs` | Fills a `MessageEnvelope` with a fresh id, sender, and timestamp; correlation is optional. |
| `MessageFamily.cs` | The enum `Command/Ack/Status/Alarm/Recovery` — the lane label that selects QoS. |
| `MessageReceived.cs` | What a handler is handed: the `Topic` and the `Payload`. |
| `RetryPolicy.cs` | The `RetryPolicy` record: `MaxAttempts`, `AckTimeout`, `Backoff`. Pure. |
| `IdempotencyGuard.cs` | `IsDuplicate`/`MarkCompleted` dedupe by `MessageId`. Pure. |
| `MessagingOptions.cs` | The config record: host, port, client id, topic root, manager id, idempotency cache size. |
