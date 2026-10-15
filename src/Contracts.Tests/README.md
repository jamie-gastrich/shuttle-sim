# src/Contracts.Tests

Tests for `src/Contracts`. Uses xunit with an in-memory or real service, depending on the trait.

## Depends on

- `../Contracts/Contracts.csproj`
- ../Contracts

## Overview

Envelope construction, message body serialization, version constants, and alarm shape validation. All pure — no services needed.

**Running**: `cd src && dotnet test Contracts.Tests/Contracts.Tests.csproj`
