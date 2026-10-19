# WMS REST contract v1

Version: 1

## SubmitOrderRequest

| Field      | JSON type | C# type  | Meaning                                                              | Required |
| ---------- | --------- | -------- | -------------------------------------------------------------------- | -------- |
| `orderId`  | `string`  | `string` | the WMS's own order number, the key, never reused for different work | yes      |
| `kind`     | `string`  | `string` | store or retrieve                                                    | yes      |
| `palletId` | `string`  | `string` | the pallet the order acts on                                         | yes      |
| `laneId`   | `string`  | `string` | the lane (for a store, where it goes; for a retrieve, where it sits) | yes      |

```json
{
  "orderId": "ORD-1001",
  "kind": "store",
  "palletId": "PLT-001",
  "laneId": "A01-L01"
}
```

---

## WmsOrder

| Field        | JSON type              | C# type    | Meaning                                                                                         | Required |
| ------------ | ---------------------- | ---------- | ----------------------------------------------------------------------------------------------- | -------- |
| `orderId`    | `string`               | `string`   | the WMS's own order number, the key, never reused for different work                            | yes      |
| `kind`       | `string`               | `string`   | store or retrieve                                                                               | yes      |
| `palletId`   | `string`               | `string`   | the pallet the order acts on                                                                    | yes      |
| `laneId`     | `string`               | `string`   | the lane (for a store, where it goes; for a retrieve, where it sits)                            | yes      |
| `state`      | `string`               | `string`   | the order's place in the ladder                                                                 | yes      |
| `receivedAt` | `string, ISO 8601 UTC` | `DateTime` | the fleet's acceptance time in UTC, on an identical re-submit, receivedAt keeps the first value | yes      |

```json
{
  "orderId": "ORD-1001",
  "kind": "store",
  "palletId": "PLT-001",
  "laneId": "A01-L01",
  "state": "received",
  "receivedAt": "2026-10-19T10:00:00Z"
}
```

---

## Errors

application/problem+json

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "laneId": ["laneId is required."] }
}
```

---

## Routes and status codes

| Route                                            | Situation                                               | Status                                            | Body                                          |
| ------------------------------------------------ | ------------------------------------------------------- | ------------------------------------------------- | --------------------------------------------- |
| `POST /api/orders`                               | Order stored, first time                                | `201 Created` + `Location: /api/orders/{orderId}` | the order                                     |
| `POST /api/orders`                               | Same `orderId`, all four field values identical (retry) | `200 OK`                                          | the stored order (original `receivedAt`)      |
| `POST /api/orders`                               | Same `orderId`, any field value different               | `409 Conflict`                                    | problem+json, title `Order id already used`   |
| `POST /api/orders`                               | Validation errors                                       | `400 Bad Request`                                 | problem+json from `Results.ValidationProblem` |
| `GET /api/orders/{orderId}`                      | Known id                                                | `200 OK`                                          | the order                                     |
| `GET /api/orders/{orderId}`                      | Unknown id                                              | `404 Not Found`                                   | problem+json, title `Unknown order`           |
| `POST /api/orders` (a request that binds a body) | Wrong content type                                      | `415`                                             | framework-generated                           |
| `POST /api/orders` (a request that binds a body) | Malformed JSON                                          | `400`                                             | framework-generated                           |

---

## Validation rules

| Field      | Rule                   | Message                               |
| ---------- | ---------------------- | ------------------------------------- |
| `orderId`  | not null or whitespace | `orderId is required.`                |
| `kind`     | `store` or `retrieve`  | `kind must be 'store' or 'retrieve'.` |
| `palletId` | not null or whitespace | `palletId is required.`               |
| `laneId`   | not null or whitespace | `laneId is required.`                 |

---

## Changing this contract

Changing a message in this file needs a decision record in `docs/decisions/`, a
version bump to this page, and it never bumps the MQTT contract (ContractVersion.Current stays 4)
