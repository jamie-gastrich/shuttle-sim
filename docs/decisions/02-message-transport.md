# Decision 02: Message transport (MQTT + REST) and the broker

**Date:** 2026-10-08
**Milestone:** M0 (Repo and tooling) — decides the transport that M2 wires, and the broker that M0's `docker/compose.yml` runs.
**Status:** Accepted. Written for lesson 03, where the broker becomes real.
**Supersedes:** none. The spec already fixes *that* transport is MQTT and REST; this record does the honest check the spec asks for and then picks the broker implementation.
**Replaced by:** any later record that changes these choices must say so and link back here. Records are append-only.

---

## 1. Why this record exists now, and not at M2

Lesson 02 said the words MQTT, topic, and endpoint "belong to decision record 02 … at M2." This record is written **now**, for a narrow reason: M0's "done when" needs a Docker setup for a message broker, and `docker/compose.yml` must name a broker **image and a broker configuration**. A choice made today carries straight into M2's round trip, so it belongs in the log.

What is deliberately **not** decided here: topic names, delivery guarantees (QoS), retries, idempotency keys, credentials or TLS, the C# client library, and the mock-WMS API shape. Those are the substance of M2 (and M6), and each gets its own record there — the next M2 record must link back to this one.

Scope, in one sentence: **this record confirms MQTT and REST per the spec, then picks the MQTT broker implementation and the dev-time broker posture for this repo.**

---

## 2. What we are deciding

Two things:

1. Confirm the **transport protocols** the spec fixes: MQTT between the fleet manager and the shuttles; REST between the mock WMS and the fleet manager. The spec says records "confirm" these choices, so this record checks them honestly against the alternatives, the way record 01 checked C#.
2. Pick the **broker** that runs in the M0 Docker setup: which image, and how the broker is configured for development.

---

## 3. Survey A: the transport protocols

The spec names MQTT and REST. Three honest options for **fleet-manager ⇄ shuttle** messaging:

| | A: MQTT (spec) | B: AMQP (e.g. RabbitMQ) | C: plain TCP or gRPC sockets |
|---|---|---|---|
| Who speaks it | small embedded-style devices, industrial messaging | enterprise message queues | anything you protocol yourself |
| Message types this project needs | publish/subscribe one-to-many (status, alarms to any subscriber) | routed queues, great for work queues | you build routing yourself |
| Duplicates/loss/replay behaviour | built into broker + QoS levels (M2 lesson material) | built in | hand-rolled |
| Works with the spec's sim shuttles | yes | add a broker server container ~ same effort | more code, less teaching |
| Dashboard/ops story (M7) | broker logs, retained topics | similar | you build it |

The spec already fixed MQTT; the check endorses it: the fleet's messages are **publish/subscribe style** (every shuttle reports status; the fleet manager and later the dashboard both want to *listen*). MQTT is the protocol built for exactly that, it is what real automated-vehicle systems use (VDA 5050, the published AGV interface standard, is MQTT-based), and the broker is something M0's compose file can run with one service.

For **mock WMS ⇄ fleet manager**: the spec says REST. Checked against gRPC and WebSocket — the meaningful rest is the spec's own: the WMS is a business system talking to the fleet manager's *edge*, not a device on a noisy network; request/response over HTTP is the least exotic, most explainable interface for that, and it is what every WMS/WCS integration engineer expects at M6.

**Verdict: MQTT and REST. The spec's choice holds.**

---

## 4. Survey B: the broker implementation

Three options, all free and containerised. All three speak MQTT.

| | A: Eclipse Mosquitto | B: EMQX | C: HiveMQ CE |
|---|---|---|---|
| Footprint | one small C binary, single process, plain config file | Erlang build, dashboard, clustering out of the box | JVM-based, needs memory |
| What it adds to this repo at M0-M2 | nothing extra — the config is two text lines | a web dashboard and cluster features nothing uses yet at M2 | a JVM runtime and licence questions for a portfolio |
| Speed to first round trip | minutes | minutes to a dashboard nobody needs yet | minutes |
| Where it is used for real | reference broker everyone learns on, small fleet controllers | scale-out deployments | enterprise fleets |

## Decision A: Eclipse Mosquitto 2.0, image `eclipse-mosquitto:2.0`

Chosen. It is the reference implementation of MQTT (the one the protocol's own docs assume you learn on), it is the smallest moving part that still behaves like a real broker (verified this session: it publishes and subscribes to and from other containers and from the host), and its configuration at dev time is two lines — which matters because Mosquitto 2.x changed its defaults to **local-only** (see section 6 — without a config file the broker refuses every connection that does not come from inside its own container, which silently kills the whole point of the setup).

Rejected:

- **EMQX** — deferred, not rejected: its dashboard and clustering are real features this milestone has no consumer for. It comes back with a record if M8's load-test pick needs a scale-out broker.
- **HiveMQ CE** — rejected for a learning/portfolio repo: a JVM broker is heavy for the one shuttle-to-fleet-manager link M2 builds, and its licencing story is more than this project should absorb at M0.

## Decision B: dev-time broker configuration

**Chosen: one plain-MQTT listener on `1883`, anonymous, published only on the host's loopback address.**

- **Anonymous:** the first MQTT clients arrive at M2. Whether the broker demands a password is exactly the kind of choice that belongs in the lesson that introduces clients, not the lesson that introduces the broker, so M2 owns authentication.
- **Plaintext, port 1883:** TLS (port 8883) is the same story — a security decision with real content, deferred to M2's record, which must link back here.
- **Host loopback only:** the host publishes the broker as `127.0.0.1:1883`, so nothing on the local network can reach it. The default port number is overridable in the compose file (lesson 03), and the Docker *internal* network still carries shuttles ⇄ fleet-manager traffic when M2 moves them into containers.

This posture is a **stated trade-off, not a security design**: an anonymous, unencrypted MQTT broker bound to the host's loopback is safe only on a developer laptop. The day the sim is exposed beyond the machine, or a real deployment is contemplated, a record must change this (credentials, ACL, TLS).

---

## 5. Rejected transports, and why

- **AMQP/RabbitMQ** — rejected: a work-queue broker is the right tool for *deciding which worker takes a job* (that is M3's assignment logic, which the spec wants pure, not broker-driven). The spec's messages are status and alarms first — subscribe/broadcast, not queues.
- **gRPC / plain sockets** — rejected: hand-rolling routing buys no lesson this project lacks, and loses the "the broker is a real product with real failure modes" material that M2's duplicate/lost/timeout lesson needs.

---

## 6. Verification table

Everything below was checked by running a command this session. Container experiments ran in `/tmp/opencode/lesson03-scratch` with a throwaway compose file and were stopped and deleted afterwards; this repo was only read.

| Claim | How checked | Result |
|---|---|---|
| The spec fixes MQTT and REST and names this record | read `spec/shuttle-spec.md` lines 13-14 | "Decision record 02 confirms the choices"; "Decision record 03 picks the database" |
| Lesson 02 expected this record at M2 | read `docs/lessons/02-first-message-contract.md` | "All three belong to decision record 02 … at M2" |
| `eclipse-mosquitto:2.0` and `:2` tags exist | `docker manifest inspect` | both exit 0 |
| The default Mosquitto container refuses remote clients (local-only mode) | started `eclipse-mosquitto:2.0`, read its log, then ran a client on the same compose network | log: "Starting in local only mode"; client: `Error: Connection refused`, exit 1 |
| A two-line config (`listener 1883` + `allow_anonymous true`) ends local-only mode and makes the broker reachable | mounted the config into the container, re-ran the client | log: "Opening ipv4 listen socket on port 1883"; client `mosquitto_sub -h broker -t …` connected and received a retained message, exit 0 |
| The host can reach the broker through the published loopback port | `mosquitto_sub -h 127.0.0.1 -p 1883` from a host-network container | received the retained message, exit 0 |
| Mosquitto 2.0.22 is the version the `:2.0` tag ships | container log | "mosquitto version 2.0.22" |
| REST stays the spec's choice for the mock WMS | read `spec/shuttle-spec.md` | "The mock WMS talks to the fleet manager over REST" |

Not yet verified:

- Whether M2's clients will run in containers or on the host. The setup supports both; M2 decides.
- Behaviour under message loss, duplication, and delay. That is M2's lesson, against a real broker.
- Authentication and TLS. Deferred to M2 by this record, with instructions to link back here.

---

## 7. What would change this choice

- If M2's round trip needs the broker's dashboard or clustering → revisit EMQX (new record linking here).
- If the sim is exposed beyond the local machine → anonymous plaintext ends immediately (credentials + ACL, and possibly TLS) — new record linking here.
- If a shuttle client lands in a language where the C# MQTT story is weak → the client library is decided at M2, not here.

---

## 8. What this record unblocks

- Lesson 03's assignment can name a broker image, a config file, and a port with the log behind it.
- M2's transport lesson starts from a decided broker instead of a survey.
- The spec's dangling reference — "Decision record 02 confirms the choices" — now points at a real file.