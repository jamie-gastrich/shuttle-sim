# Lesson 28: Mock WMS reliability — Build Report

## Summary

Added a separate in-memory MockWms receiver with duplicate-safe order status handling and runtime `ok`, `slow`, `error`, and `hang` modes. The fleet manager now classifies HTTP outcomes, records permanent delivery failures, and schedules retries per order with configurable exponential backoff.

The mock has no references to `FleetCore` or `Contracts`. `src/Contracts/`, `docs/contract/`, and `ContractVersion.Current` were not changed.

## Files created

| File | Description |
|---|---|
| `src/MockWms/MockOrder.cs` | Mock-owned status body and validation |
| `src/MockWms/MockWmsLedger.cs` | Locked idempotent ledger, counters, and snapshots |
| `src/MockWms/MockWmsControl.cs` | Locked mode and delay configuration |
| `src/MockWms/Program.cs` | Mock WMS HTTP routes |
| `src/MockWms/appsettings.json` | Listener and slow-delay configuration |
| `src/MockWms/README.md` | Local run instructions and mock-only route/mode notes |
| `src/MockWms.Tests/MockWmsLedgerTests.cs` | Ledger facts |
| `src/MockWms.Tests/MockWmsControlTests.cs` | Mode and delay facts |
| `src/FleetCore/WmsDeliveryOutcome.cs` | Pure HTTP status classification |
| `src/FleetCore/WmsRetryPolicy.cs` | Pure capped exponential backoff policy |
| `src/FleetCore/WmsDeliveryFailures.cs` | Thread-safe in-memory permanent-failure board |
| `src/FleetCore.Tests/WmsDeliveryOutcomeTests.cs` | Delivery outcome facts |
| `src/FleetCore.Tests/WmsRetryPolicyTests.cs` | Backoff facts |

## Files modified

| File | Changes |
|---|---|
| `src/FleetCore/WmsRoutes.cs` | Added delivery-failures route |
| `src/FleetManager/WmsStatusPusher.cs` | Added outcome handling, per-order retry pacing, and constructors for real/fake clocks |
| `src/FleetManager/Program.cs` | Added retry configuration validation, policy/failure-board wiring, and failure endpoint |
| `src/FleetManager/appsettings.json` | Added retry base and maximum delays |
| `src/FleetManager.Tests/WmsStatusPusherTests.cs` | Added fake-clock and retry/permanent/success cases |
| `src/ShuttleFleet.slnx` | Registered MockWms and MockWms.Tests |

## Verification

- `cd src && dotnet build --no-incremental` — passed, 0 warnings and 0 errors.
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` — passed, 213 tests across the non-integration suite.
- `cd src && dotnet test ShuttleFleet.slnx --list-tests` — the 20 required R11 test names are present.
- Assignment wiring checks passed; no changes under `src/Contracts/` or `docs/contract/`.
- Live HTTP smoke checks passed on `http://127.0.0.1:5090`: duplicate push returned `200` with one applied entry and `duplicate=1`; `error` returned `500` without applying; invalid input returned `400`; `slow` returned `200`; `hang` timed out without applying; identity conflict returned `409`; read routes returned the entry or `404`; reset cleared the ledger/counters and preserved the selected mode.
- Port 5090 was clear after the smoke checks.

Integration tests requiring supporting services were not run; the requested non-integration suite passed.

## Review 28 follow-up

The reviewer found that the committed snapshot was missing `using MockWms;` in
the top-level app. That import is now present. The retry failure count was
simplified to increment from its previous value, retry log messages now include
the computed delay, and the README clarifies that `hang` waits until the client
cancels and is not a production WMS.

After these corrections:

- `cd src && dotnet build --no-incremental` — passed, 0 warnings and 0 errors.
- `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` — passed, 213 tests, 0 failed.
- `git diff --check` — passed.

## Study first

- `src/MockWms/MockWmsLedger.cs` — identity check, duplicate check, then rank comparison inside one lock.
- `src/FleetManager/WmsStatusPusher.cs` — retry state keyed by order id and the due-time check before sending.
- `src/FleetCore/WmsRetryPolicy.cs` — doubling with a maximum-delay clamp.

Owner sign-off: pending owner review; add the date and "explained and verified" after completing the review gate.