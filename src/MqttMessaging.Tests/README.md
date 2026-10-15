# src/MqttMessaging.Tests

Tests for `src/MqttMessaging`. Uses xunit with an in-memory or real service, depending on the trait.

## Depends on

- `../MqttMessaging/MqttMessaging.csproj`
- ../MqttMessaging ../Messaging ../Contracts (test broker runs in-process, no Docker)

## Overview

MqttMessageBus pub/sub over an in-process MqttServer — no Docker needed. Covers QoS mapping from MessageFamily, topic filtering, and envelope round-trip serialization.

**Running**: `cd src && dotnet test MqttMessaging.Tests/MqttMessaging.Tests.csproj`
