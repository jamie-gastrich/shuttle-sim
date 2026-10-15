# src/ShuttleSim.Tests

Tests for `src/ShuttleSim`. Uses xunit with an in-memory or real service, depending on the trait.

## Depends on

- `../ShuttleSim/ShuttleSim.csproj`
- ../ShuttleSim ../ShuttleCore ../Contracts ../Messaging

## Overview

ShuttleEndpoint command handling (dedupe, ack, state advance), heartbeat publishing, and status publication — all over a fake IMessageBus. All pure — no services needed.

**Running**: `cd src && dotnet test ShuttleSim.Tests/ShuttleSim.Tests.csproj`
