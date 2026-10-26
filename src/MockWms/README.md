# Mock WMS

Run the mock from `src` with:

```bash
dotnet run --project MockWms/MockWms.csproj
```

It listens on `http://127.0.0.1:5090`, configured in `appsettings.json`.

## Routes

| Route | Purpose |
|---|---|
| `POST /api/order-status` | Receive the WMS order status pushed by the fleet manager. |
| `GET /api/orders` | List ledger entries by order id. |
| `GET /api/orders/{orderId}` | Read one ledger entry, or return `404`. |
| `GET /mock/state` | Read the active mode, slow delay, ledger entries, and counters. |
| `POST /mock/mode` | Set `ok`, `slow`, `error`, or `hang`; `slowDelayMs` is optional. |
| `POST /mock/reset` | Clear ledger entries and counters without changing the active mode. |

## Delivery modes

| Mode | Receiver behavior |
|---|---|
| `ok` | Apply the word and return `200`. |
| `slow` | Apply the word, wait for the configured delay, then return `200`. |
| `error` | Return `500` without applying the word. |
| `hang` | Wait until the client cancels the request without applying the word. |

The `/mock/*` routes are test controls, not part of the WMS REST contract. The
mock keeps its ledger in memory and loses it when the process restarts.
In `hang` mode, the request remains open until its client times out or otherwise
cancels it. A client without a timeout can leave that request open indefinitely.
This mock is a test tool, not a production WMS.
