# src/ShuttleCore.Tests

Tests for `src/ShuttleCore`. Uses xunit with an in-memory or real service, depending on the trait.

## Depends on

- `../ShuttleCore/ShuttleCore.csproj`
- ../ShuttleCore

## Overview

State machine transitions (Idle/Moving/Loading/Unloading/Fault), rejection of bad transitions, rack occupancy rules, track geometry, and wire-name constants. All pure — no services needed.

**Running**: `cd src && dotnet test ShuttleCore.Tests/ShuttleCore.Tests.csproj`
