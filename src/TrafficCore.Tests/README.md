# src/TrafficCore.Tests

Tests for `src/TrafficCore`. Uses xunit with an in-memory or real service, depending on the trait.

## Depends on

- `../TrafficCore/TrafficCore.csproj`
- ../TrafficCore ../ShuttleCore

## Overview

TrafficController CanMove/CommitMove, SegmentReservations expiry, and the collision/deadlock stress scenario (4+ shuttles). All pure — no services needed.

**Running**: `cd src && dotnet test TrafficCore.Tests/TrafficCore.Tests.csproj`
