# Hints for lesson 03: Docker services and the fresh-container build

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- Five files, five jobs: `docker/compose.yml` (the stack), `docker/mosquitto.conf` (why
  the broker is reachable at all), `docker/Dockerfile.build` (the fresh build), the root
  `.dockerignore` (what the build must not see), and `docker/README.md` (how a stranger
  uses it). Ask which one is missing before you touch a terminal.
- `docker compose up -d` prints success even when a service crash-loops. The acceptance
  block therefore **counts running containers after a five-second settle** and then
  **probes each service with a real client in a second container** — a green count is a
  process, a green probe is a service. If the count prints 1, read `docker compose -f
  docker/compose.yml logs database` and `… logs broker` and explain what you see.
- Mosquitto 2 does not listen for remote clients until you say so. Its default is
  local-only; the config file's `listener 1883` line is what makes the container reachable
  by other containers at all. The lesson's verification table has the exact log line.
- A `:latest` tag is the opposite of reproducible: tomorrow's image may differ from
  today's suite. The two pinned tags in the assignment (`eclipse-mosquitto:2.0`,
  `postgres:17`) are facts, not a menu.
- The fresh-container build is the milestone's whole point. If `docker run` passes on your
  machine after your host build succeeded, that proves nothing new; the interesting moment
  is a **clean host** (`docker system prune` is not needed — a fresh container never
  shares the host's `bin/`).
- Anything that would break a brand-new dock (no network, empty cache, another machine)
  is what this lesson is *for*. A "works on wide racks only" test is the thing being
  checked — write nothing that depends on a port being free without the override.

---

## Tier 2 — direction

- Order of work, and why:

  1. `docker/mosquitto.conf` first — the smallest file, and the broker will not work
     without it. Two operating lines plus your comments.
  2. `docker/compose.yml` second — it *loads* that config, pins the images, and names the
     services. Write it top-down: `name:`, `services:`, then `broker:` and `database:`,
     and inside each, `image:`, `ports:`, and the env/volume lines.
  3. `.dockerignore` third — contexts are write-once: anything it misses rides into every
     build.
  4. `docker/Dockerfile.build` fourth — it only needs `src/`, `global.json`, and the SDK.
  5. `docker/README.md` last — it documents the commands you just ran.
  6. Run the acceptance block, one phase at a time, reading every exit code directly.

- The compose file is read top-down too. **Declare `broker` before `database`** — a
  reviewer reads the file top-down, and the broker is the one M2 actually wires, so it
  belongs as service number one. (Not because `config --services` prints them in that
  order: measured, that output order is not stable — the acceptance judges the two
  names, never their order.)
- `docker compose -f docker/compose.yml` is not `docker compose` — the `-f` names the
  file, relative to where you are standing, so **run it from the repository root**. The
  file's *internal* paths, like `./mosquitto.conf`, then resolve against the compose
  file's own folder (`docker/`), wherever you invoke it from. Measured.
- The `127.0.0.1` in the ports line is part of the assignment, not decoration: it is the
  records' promise that dev traffic stays on this machine. `"127.0.0.1:${MQTT_HOST_PORT:-1883}:1883"`
  reads "host loopback : 1883 (or $MQTT_HOST_PORT) : container 1883".
- The database env block does three separate things the acceptance checks for:
  `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB`, each fed from its
  `SHUTTLE_DB_*` variable. Postgres refuses to start without the password — measured, and
  the acceptance's count-of-2 gate is what catches a forgotten one.
- `.dockerignore` removes, it never adds: a line like `docs/` would silently break lesson
  02's stretch test (it reads the contract page from the build output) *inside the
  container only*. The homework mimics the reviewer: build from a clean context, then run
  the suite, then wonder whether you and the container saw the same files.
- The `RUN` and the `CMD` in the Dockerfile do two different jobs. `RUN` answers "does it
  compile", `CMD` answers "do all tests pass". Record 04 §3 chose that split *and*
  rejected the other shape — a second `RUN dotnet test` layer, "not a second `RUN`
  layer". Five lines, no more; you should be able to say why before the review.

---

## Tier 3 — near-solution

Copy-paste shapes. Every block below is a **template**: the shape and the fixed facts come
from the assignment, the typing is not done for you — type every line yourself. Your words
are still yours: the comments, the README's prose, and `WORKDIR`'s directory name. Every
string the acceptance greps for (the two `image:` lines, `name: shuttle-fleet`, the
`FROM` line, `dotnet build`, the five `${VAR:-default}` overrides, the three `.dockerignore`
lines) appears verbatim in the blocks below; the acceptance's other checks are behavioural
(what the image contains, whether a client can reach a service, whether the suite prints
two `Passed!` lines), and every one of those behaviours was measured on 2026-10-08: `docker
compose config` resolved the values, `up`/`down` ran the broker and the database, client
containers reached both, and the image built and ran the suite. A failure on your side is
a typing slip, not my syntax.

### `docker/mosquitto.conf`

```conf
# Why these two lines: Mosquitto 2.x runs local-only until you name a listener.
# allow_anonymous is the dev posture decision record 02 chose on purpose.
listener 1883
allow_anonymous true
```

The `listener 1883` line is the gate that matters — without it the broker would log
`Starting in local only mode` and refuse every container that tries to reach it.

### `docker/compose.yml`

```yaml
name: shuttle-fleet
services:
  broker:
    image: eclipse-mosquitto:2.0
    ports:
      - "127.0.0.1:${MQTT_HOST_PORT:-1883}:1883"
    volumes:
      - ./mosquitto.conf:/mosquitto/config/mosquitto.conf:ro
  database:
    image: postgres:17
    ports:
      - "127.0.0.1:${DB_HOST_PORT:-5432}:5432"
    environment:
      POSTGRES_USER: ${SHUTTLE_DB_USER:-shuttle}
      POSTGRES_PASSWORD: ${SHUTTLE_DB_PASSWORD:-shuttle}
      POSTGRES_DB: ${SHUTTLE_DB_NAME:-shuttle}
```

Measured on this shape: `MQTT_HOST_PORT=18830 docker compose config` resolves the publish
to 18830; `docker compose up -d` brings both up, the count of running containers is 2, and
both client probes exit 0; the database comes up green with those defaults; the `:ro`
mount produces the broker's harmless `chown: …: Read-only file system` warning at start —
cosmetic, and worth saying out loud so it does not read as a failure.

### `.dockerignore`

```text
.git
**/bin/
**/obj/
```

That is all it removes. `src/`, `docs/`, and `global.json` must stay eligible — the build
compiles `src/`, lesson 02's stretch test reads `docs/contract/v1.md` from inside `bin/`,
and `global.json` is what makes the container build against the pinned SDK. The acceptance
checks the three lines above are present, each anchored to the whole line (a substring
like `bin/` would pass while the promise stays broken — measured), and that `global.json`,
`docs/`, and `src/` stay absent.

### `docker/Dockerfile.build`

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /shuttle
COPY global.json ./
COPY src/ ./src/
RUN dotnet build src/ShuttleFleet.slnx -v m
CMD ["dotnet", "test", "src/ShuttleFleet.slnx", "-v", "m"]
```

`WORKDIR`'s name is yours (mine was `/shuttle`). The five fixed facts are the `FROM` pin,
the two `COPY`s, the `RUN` that compiles, and the `CMD` that runs the suite — the suite's
exit code *is* the image's exit code. Measured: the build prints
`Build succeeded. 0 Warning(s) 0 Error(s)` and `docker run --rm shuttle-fleet-build`
exits 0 with two `Passed!` lines (2 for Contracts.Tests, 1 for FleetManager.Tests);
inside the image `dotnet --version` printed `10.0.401`, which the repo's pinned
`10.0.112` accepts via `rollForward: latestFeature`.

### `docker/README.md`

Your prose; three things it must carry, in your own words: what runs here (the broker and
the database, and why they are services rather than windows on a laptop), the two commands
from AGENTS.md (`docker compose -f docker/compose.yml up -d` and `… down`), and the
overridable values (`MQTT_HOST_PORT`, `DB_HOST_PORT`, `SHUTTLE_DB_USER`,
`SHUTTLE_DB_PASSWORD`, `SHUTTLE_DB_NAME`). A sentence that says why `:latest` is banned
here earns its keep.

### Acceptance snippets you will retype

From the repository root, one line at a time, reading every exit code directly where the
block says so.

```bash
# Phase 2 resolved — you should see these other numbers
docker compose -f docker/compose.yml config --services   # broker
                                                        # database (in either order)
docker compose -f docker/compose.yml config | grep -E 'host_ip|published|target' | head -8
```

The second command is a peek, not a gate: it shows the ports as compose will actually
publish them — `host_ip: 127.0.0.1` with `published: "1883"` and `published: "5432"` when
the shell vars are unset (measured; one of the `target` lines is the config mount, not a
port).

```bash
# Phase 3 — the "started vs ready" moment in one line each
docker compose -f docker/compose.yml up -d        # exit 0
sleep 5
docker compose -f docker/compose.yml ps -q | wc -l   # prints 2
docker run --rm --network shuttle-fleet_default eclipse-mosquitto:2.0 \
  mosquitto_pub -h broker -t acceptance/probe -m ok   # exit 0
docker run --rm --network shuttle-fleet_default postgres:17 \
  pg_isready -h database -U "${SHUTTLE_DB_USER:-shuttle}" \
  -d "${SHUTTLE_DB_NAME:-shuttle}"                    # exit 0
docker compose -f docker/compose.yml down             # exit 0, nothing left
```

The two probes are client containers on the stack's own network — no host tool beyond
`docker`, and the network name follows from the fixed `name: shuttle-fleet`. A count of 2
says both processes run; a green probe says both services answer.

```bash
# Phase 5 measured, for the record: fresh-context build, then the suite inside it
T=$(mktemp); docker build -f docker/Dockerfile.build -t shuttle-fleet-build . >"$T" 2>&1; echo "build exit=$?"; rm -f "$T"
U=$(mktemp); docker run --rm shuttle-fleet-build >"$U" 2>&1; echo "test exit=$?"; grep -c 'Passed!' "$U"; rm -f "$U"
```

Measured (2026-10-08, against a `git archive HEAD` snapshot, images deleted afterwards):
`build exit=0`, `test exit=0`, `Passed!` count `2`. The first build downloads the SDK
base image; it is slow once and cached after.

---

What is deliberately *not* given here: the wording of your comments and README, the name
of your `WORKDIR`, and the exact layout you choose inside the blocks. The fixed facts the
acceptance greps for — `name: shuttle-fleet`, the two pinned tags, the five
`${VAR:-default}` overrides, the `FROM` line, `dotnet build`, the three `.dockerignore`
lines — plus the behaviours it *probes* (compiled output inside the image, `global.json`
at the `WORKDIR`, a client reaching each service, two `Passed!` lines). Type every line
yourself, be able to explain every line of your version out loud, and keep the review's
first question in mind: *every port in this file has a default and an override — say
where each one comes from.*