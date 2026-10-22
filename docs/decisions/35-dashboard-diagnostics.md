# Decision record 35: Dashboard slice design

## Status: chosen

### Dashboard data endpoints (Survey 1, option A)

**What it means.** Three separate `webApp.MapGet` routes under `WmsRoutes`: one for orders (`/wms/orders/{orderId}`), one for shuttles (`/wms/shuttles/{shuttleId}`), and one for the task‑queue GET (`/wms/task-queue`). Each endpoint looks up its resource from the in‑memory board (or SQL after restart) and returns JSON or 404.

**Why chosen.** The operator UI needs to fetch exactly the piece it is showing — the *Orders* tab does not need the full shuttle list, and the *Shuttles* tab does not need the full order detail. Separate endpoints keep the payload small and the contract simple. Option B (one big `/fleet-state` blob) was rejected because it sends too much data for a single view and creates a coupling risk: a change to the task-shape breaks the orders tab, even though they are unrelated.

**What would change the choice.** If the dashboard prototype proves that fetching three separate endpoints adds too much HTTP overhead compared to one blob, we could revisit and merge them under a new contract version.

### Alarm persistence design (Survey 2, option A)

**What it means.** An `alarms` table in PostgreSQL, one row per alarm event, with columns `shuttle_id, code, severity, raised_at, acknowledged_at`. The repository upserts on `(shuttle_id, code)` so a new raising updates `raised_at` and a new acknowledgment sets `acknowledged_at`.

**Why chosen.** Lesson 25 persisted orders and tasks, but alarms were left in memory. After a restart the dashboard would show no alarm history. Option B (in‑memory‑only) was rejected because the dashboard must work after a restart just like the order and task state does. Option C (Azure Table storage) was rejected because the project already uses PostgreSQL for tasks and orders — adding a second store would increase complexity without benefit.

**What would change the choice.** If the project migrates the whole fleet to a document‑based store (e.g., Cosmos DB), the alarm table could be replaced by a container. Until then, PostgreSQL is the consistent choice.

### Operator command topics (Survey 3, option A)

**What it means.** A dedicated MQTT topic `fleet/operator/command` carries `OperatorCommandMessage` payloads. The three command kinds are `Pause`, `Resume`, and `Acknowledge`. This topic is separate from the M5 `recovery/command` topic and from the task‑command topics used by the executor.

**Why chosen.** If operator commands share the M5 `recovery/command` topic, an operator pause could accidentally trigger the automated recovery flow, or a recovery command from the fleet could be confused with an operator intent. A separate topic keeps the concerns clean and makes the operator's intent unmistakable — the dashboard can publish and the fleet manager handles it without interfering with the heartbeat/recovery machinery.

**What would change the choice.** If the system is redesigned to have a single command bus where every message carries a `source` field that distinguishes operator vs. automated origins, the topic consolidation could be revisited. But that would be a larger contract change and is not planned for M7.

---

## Rejected options (append-only, not rewritten)

- Survey 1 option B: one big `/fleet-state` endpoint — rejected for sending too much data and creating coupling.
- Survey 1 option C: push updates via MQTT only — rejected because the dashboard needs fast individual lookups and MQTT does not guarantee quick availability.
- Survey 2 option B: in‑memory‑only alarms — rejected because alarm history is lost after restart.
- Survey 2 option C: Azure Table storage — rejected because the project already uses PostgreSQL consistently.
- Survey 3 option B: separate topics per command — rejected because it adds topic‑sync risk without real benefit for three commands.
- Survey 3 option C: reuse `recovery/command` — rejected because it blurs the separation between operator intent and automated recovery.

---

## References

- Decision 02: message contract and MQTT topic names
- Decision 03: PostgreSQL as the fleet state database
- Decision 21: alarms with severity
- Decision 20: liveness heartbeats and timeouts
- Lesson 25: persistence slice (orders + tasks survive restart)