# Decision 03: The fleet state database

**Date:** 2026-10-08
**Milestone:** M0 (Repo and tooling) — picks the database that M0's `docker/compose.yml` runs and M3 starts talking to.
**Status:** Accepted. Written for lesson 03, where the database service becomes real.
**Supersedes:** none.
**Replaced by:** any later record that changes the database must say so and link back here. Records are append-only.

---

## 1. Why this record exists now

The spec's "done when" for M0 includes a Docker setup for "a database", and spec line 14 names this record. `docker/compose.yml` must pick a database **today** so the setup is a real, running service the moment M0 closes — but the first C# code to touch the database is M3's "survive a restart" milestone. This record therefore decides the **server**, not the driver (.NET data-library, Npgsql vs EF Core) — that is M3's record, and it links back here.

Scope, in one sentence: **pick a database server that can run as a Docker service, and the M0 posture for running it in development.**

---

## 2. Survey

| | A: PostgreSQL 17 | B: SQLite | C: Microsoft SQL Server 2022 |
|---|---|---|---|
| Runs as a containerised **service** → gives M0 a real "database" half of the setup | yes | no — SQLite is a library inside your process; there is no server | yes |
| Image weight on a laptop | ~650 MB on disk (measured this session, `docker image ls`) | none (in-process) | ~1.5 GB + EULA acceptance |
| Connection string, pooling, restart behaviour | real lessons (the point of M3) | hidden — the file just opens | real lessons |
| Licence story | free, permissive | public domain | free-to-develop, but EULA env var and product-weight promise |
| First-class .NET driver | Npgsql, EF Core provider (M3) | yes | Microsoft.Data.SqlClient |
| Standard for warehouse-adjacent systems | yes (and in Docker this is the default go-to) | dev tool | yes in Microsoft-shop warehouses |

---

## 3. Decision: PostgreSQL 17, image `postgres:17`

Chosen. The decisive reasons:

1. **M0's setup needs a database *service*.** This milestone's deliverable is literally a Docker setup containing "the broker and the database". A library that lives inside the fleet-manager process (SQLite) makes the database half of the setup invisible — nothing to run, nothing for the compose file to do. PostgreSQL is a server, and a standard one.
2. **M3's lesson is about a server's failure modes.** "Fleet manager survives a restart" means: the process dies, the *server* keeps the data, the manager reconnects and resumes. That lesson only exists if the data lives in a separate process with a connection string, pooling, and a startup handshake — which is exactly what PostgreSQL forces you to learn and SQLite lets you skip. Not choosing it because it is "more SQL"; choosing it because the milestone's whole point is a database that outlives its client.
3. **Free and light.** No EULA, no 1.5 GB image, container-ready with a small, boring setup (verified this session: image runs, healthcheck passes with `pg_isready`).

Rejected:

- **SQLite** — rejected for the reasons above (no server → empty half of M0; hides the connection-and-restart lessons M3 exists to teach). Not rejected because of any virtue of SQLite as a product: if M0's spec had not demanded a database *service* in Docker, SQLite would be a serious candidate on this hardware, and that is noted honestly in section 7.
- **Microsoft SQL Server** — rejected deferral, not an absolute no: it is a fine choice for a real Microsoft-shop WMS/WCS (and this project's owner may well meet it at work one day), but at M0 its 1.5 GB EULA-gated image buys nothing that a learning/fleet sim needs at this milestone. If the owner's target employer is a SQL Server shop, a new record can switch the choice and link back here.

---

## 4. Dev-time posture for the database

**Chosen (with the broker's record 02, section 4):** the database runs in the compose project on its own Docker network; the host publishes it on the loopback only; the three connection values (`SHUTTLE_DB_USER`, `SHUTTLE_DB_PASSWORD`, `SHUTTLE_DB_NAME`) carry a default (`shuttle`) but are overridable through compose environment substitution; the container's data directory is **not** declared in M0 (a named volume for persistence is worthwhile and cheap — see section 6 — but persistence *requirements* arrive at M3 with the restart lesson, and a record there should own the promise).

This mirrors record 02's posture: stated trade-off (dev credentials, loopback-only), disclosed in the record, and the "deploy for real" story is a future decision that must link back here.

---

## 5. Verification table

Container experiments ran in `/tmp/opencode/lesson03-scratch` and were stopped and deleted afterwards; this repo was only read.

| Claim | How checked | Result |
|---|---|---|
| `postgres:17` tag exists | `docker manifest inspect postgres:17` | exit 0 |
| PostgreSQL refuses to start without a password | `docker compose up -d` on a compose service with no `POSTGRES_PASSWORD` | exit 0 (compose starts it) but the container exits; log: "Error: Database is uninitialized and superuser password is not specified. You must specify POSTGRES_PASSWORD to a non-empty value"; `docker compose ps --format … Status` shows the container exited |
| `docker compose up -d` exiting 0 does not mean the service is running | same run | exactly the point: the compose command's exit code is about *starting*, which is why the lesson's acceptance counts running containers |
| With a password the server starts and accepts connections | compose with `POSTGRES_PASSWORD` + healthcheck | `pg_isready -U shuttle -d shuttle` → "shuttle:5432 - accepting connections"; healthcheck passes; `docker compose up -d --wait` exits 0 and reports the container Healthy |
| The default port 5432 is what postgres listens on | container log | "database system is ready to accept connections" on port 5432 |

Not yet verified:

- The .NET data layer (Npgsql or EF Core) — M3's record, links here.
- Whether a named volume for `PGDATA` will be added — M3's record, links here.

---

## 6. Note on persistence

Nothing in the spec requires fleet state to outlive the compose project at M0. The acceptance for lesson 03 therefore only requires the service to **start, run, and stop cleanly**. A future record with the restart milestone decides the volume mount. (For reviewers: `docker compose down` without `-v` already preserves `postgres:17`'s image-declared volume even when one is not mapped explicitly — so the "does down lose data" question can be tested at M3; it is not claimed here as "not yet verified" because nothing in M0 asserts data survives.)

---

## 7. What would change this choice

- If M0's spec dropped the "database in Docker" requirement → SQLite returns as a serious candidate (this record notes it, no doctrine).
- If the owner's target workplace runs a Microsoft SQL shop → new record switches to SQL Server and links here.
- If the fleet grows to where connection pooling / replica behaviour matter (M8) → new record, links here.

---

## 8. What this record unblocks

- Lesson 03's assignment can name a database image and service with measured defaults.
- M3's "survive a restart" lesson starts from a decided, running server.
- The spec's dangling "Decision record 03 picks the database" reference now points at a real file.