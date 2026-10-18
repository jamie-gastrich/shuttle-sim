# src/Contracts

Shared wire shapes. The single source of truth for what crosses the network between the shuttle and the fleet manager.

## Depends on

- Nothing. Framework only.

## Used by

Every project that sends or reads a message: `Messaging`, `MqttMessaging`, `FleetManager`, `ShuttleSim` (and their `.Tests`). The contract is consumed by both sides equally.

## Overview

Contracts is the shared vocabulary. It has no I/O and no logic beyond data shapes — just records, enums, and version constants that both sides agree on.

```text
  FleetManager ──┐                        ┌── ShuttleSim
                 │                        │
                 ▼                        ▼
              Contracts (shared records + enums + ContractVersion)
                 │                        │
                 ▼                        ▼
  Messaging builds envelopes/topics   MqttMessaging serializes to bytes
  around these shapes                 over the wire
```

Every message that crosses the network is one of the records defined here:
`MoveCommandMessage`, `ShuttleStatusMessage`, `CommandAckMessage`, `AlarmMessage`, `RecoveryCommandMessage`, `RecoveryAckMessage`.
The envelope wrapper (`MessageEnvelope`) adds `MessageId`, `CorrelationId`,
`SenderId`, `SentAt`, and the `ContractVersion` — so a version mismatch fails
fast at intake instead of corrupting state.

**Changing anything here is a contract change**: it needs a decision record
and normally a version bump in `docs/contract/`. See records 07 and 21.

## The files

| File | Where it fits |
|---|---|
| `MessageEnvelope.cs` | The wrapper every message carries: `ContractVersion`, `MessageType`, `MessageId`, `CorrelationId`, `SenderId`, `SentAt`. Read first on intake. |
| `MessageTypes.cs` | The kind strings (`moveCommand`, `commandAck`, `shuttleStatus`, `alarm`, `recoveryCommand`, `recoveryAck`) and `ContractVersion.Current`. The shared vocabulary keys. |
| `MoveCommandMessage.cs` | manager → shuttle body: which shuttle, which lane. |
| `CommandAckMessage.cs` | shuttle → manager body: `Accepted` plus an optional `Reason`. |
| `ShuttleStatusMessage.cs` | shuttle → manager body wrapping a `ShuttleStatus`. |
| `ShuttleStatus.cs` | The status fields: shuttle id, state, position, timestamp, fault reason. |
| `AlarmMessage.cs` | manager → WMS body wrapping an `Alarm` (the v3 kind). |
| `Alarm.cs` | One alarm: shuttle id, severity, code, detail. |
| `AlarmCodes.cs` | The code constants (`lostCommunication`, `blockedPath`, `palletNotDetected`). |
| `AlarmSeverities.cs` | The severity constants (`info`, `warning`, `critical`). |
| `RecoveryCommandMessage.cs` | manager → shuttle body: which shuttle. |
| `RecoveryAckMessage.cs` | shuttle → manager body: `Accepted` plus an optional `Reason`. |
