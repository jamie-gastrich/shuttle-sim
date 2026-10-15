# src/Messaging.Tests

Tests for `src/Messaging`. Uses xunit with an in-memory or real service, depending on the trait.

## Depends on

- `../Messaging/Messaging.csproj`
- ../Messaging ../Contracts

## Overview

TopicScheme parsing/formatting, MessageEnvelope construction, RetryPolicy backoff, and IdempotencyGuard dedupe. All pure — no services needed.

**Running**: `cd src && dotnet test Messaging.Tests/Messaging.Tests.csproj`
