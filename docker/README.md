# Docker support services for Shuttle Fleet

This directory holds the supporting infrastructure for the simulation: a message broker and a database. The broker is MQTT, which lets the simulated shuttles and the fleet manager exchange commands and status updates without tying them to one process or one laptop window. The database is PostgreSQL, which keeps the fleet state, task history, and other durable information in one place that can survive restarts and be queried by the app.

These are services, not little windows you casually open on your laptop. They are long-running background processes that listen on network ports, store state, and are expected to keep running while the rest of the system talks to them. That is why they are started with Docker Compose instead of being launched as ad-hoc desktop apps: they act like shared infrastructure for the whole project.

## Start and stop the services

Run these from the repo root:

```bash
docker compose -f docker/compose.yml up -d
docker compose -f docker/compose.yml down
```

The first command brings the broker and database up in the background. The second tears the stack down cleanly when you are done.

## Override values

The Docker Compose file accepts environment overrides so the ports and database credentials do not collide with anything already running on your machine. These are the values you can override:

- `MQTT_HOST_PORT` — the host port exposed for the MQTT broker
- `DB_HOST_PORT` — the host port exposed for PostgreSQL
- `SHUTTLE_DB_USER` — the PostgreSQL user name
- `SHUTTLE_DB_PASSWORD` — the PostgreSQL password
- `SHUTTLE_DB_NAME` — the PostgreSQL database name

If you do not set them, the defaults are `1883`, `5432`, `shuttle`, `shuttle`, and `shuttle`.

## Why `:latest` is banned

`:latest` is banned here because it is not reproducible: a container tag that moves over time can silently change the broker or database behavior and make the project fail in ways that no one can explain later.
