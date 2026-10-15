# src/FleetCore.Tests

Tests for `src/FleetCore`. Uses xunit with an in-memory or real service, depending on the trait.

## Depends on

- `../FleetCore/FleetCore.csproj`
- ../FleetCore

## Overview

Task lifecycle transitions (Queued → Dispatched → Completed/Failed), retry counting, TaskBoard ordering, and repository interface contract. All pure — no services needed.

**Running**: `cd src && dotnet test FleetCore.Tests/FleetCore.Tests.csproj`
