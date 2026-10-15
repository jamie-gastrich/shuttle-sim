# src/FleetManager.Tests

Tests for `src/FleetManager`. Uses xunit with an in-memory or real service, depending on the trait.

## Depends on

- `../FleetManager/FleetManager.csproj`
- ../Contracts ../Messaging ../TrafficCore ../ShuttleCore ../MqttMessaging (for the integration test)

## Overview

Unit tests for ShuttleHealth, AlarmEvaluator, TaskDispatcher, RunLoopGate, ReliableCommandSender, and FleetServeLoop (with a fake bus). Integration tests (tagged [Trait("Category", "Integration")]) exercise the real Postgres database and an in-process MQTT broker — run those with the broker and database from docker/compose.yml up.

**Running**: `cd src && dotnet test FleetManager.Tests/FleetManager.Tests.csproj`
