# Lesson 03: Docker services and the fresh-container build

**Milestone:** M0 (Repo and tooling) · **Tooling:** Docker ·
**Prerequisite:** [Lesson 02: The first message contract](02-first-message-contract.md)

**Concepts this assignment requires:**

- [ ] **A container image and the Dockerfile** — an image is a frozen recipe; a container
      is a running instance of it; the Dockerfile is the recipe's steps; the build context
      and `.dockerignore` decide what the recipe can see. This is what makes a build
      "fresh." Taught in [The image, the container, and the Dockerfile](#the-image-the-container-and-the-dockerfile).
      Used by: `docker/Dockerfile.build` and `.dockerignore` (this assignment), and every
      containerized milestone after it.
- [ ] **Compose: several services, one file** — `docker compose` runs a whole *stack*:
      one YAML file declares the services, each service gets a private network with its
      name as a hostname, and ports publish a container's port to the host. Taught in
      [Compose: services, names, and ports](#compose-services-names-and-ports).
      Used by: `docker/compose.yml` (this assignment) — the broker and the database.
- [ ] **Configuration, not hard-coded wiring** — the AGENTS.md rule lands here: everything
      that differs between machines (ports, names, addresses) lives in one configuration
      file with overridable `${VAR:-default}` values, and no C# file repeats them. Taught in
      [Configuration, not hard-coded wiring](#configuration-not-hard-coded-wiring).
      Used by: `docker/compose.yml` and the acceptance's "no wiring in code" gate.

Three new concepts. That is the cap.

Docker's vocabulary is mostly nouns you already hold: an **image**, a **container**, a
**port**, a **volume**. Three words for later, not today: **QoS** (a delivery guarantee
level for a message — M2's transport record), **TLS** (encrypting broker traffic — M2),
and **volume** (a container's disk that outlives the container — M3's persistence record).
All three belong to the records linked in section [Options survey](#options-survey).

---

## Where this fits

Lesson 01 built the empty `.NET` frame. Lesson 02 put the first real thing on it: the
`ShuttleStatus` message. This lesson closes M0. The spec says M0 is *"Repo and tooling …
a Docker setup for the broker and database, and one real message type defined and unit
tested"*, and its "done when" is one line:

> *Done when: clean build and tests pass in a fresh container.*

Lesson 02 already wrote the message. This lesson delivers the rest, in two halves:

1. **The environment half.** *"A Docker setup for the broker and database."* AGENTS.md
   promises it too — *"Supporting services (message broker, database) run through Docker.
   See `docker/` once M0 is done."* Today `docker/` is empty and nobody can point to
   anything. After this assignment `docker/compose.yml` starts the broker and the
   database, and `docker/README.md` says how.

2. **The verification half.** *"Clean build and tests pass in a fresh container."* This is
   the sentence the whole milestone rests on. A "fresh container" means: no host `bin/`,
   no host `obj/`, no NuGet cache that was never checked in, no SDK that happened to be
   installed. Everything the build needs arrives **from the container's own image**,
   starting from nothing — like a shuttle pulling into an empty dock on its first day and
   checking itself before it carries a single pallet.

Here is why this matters for a software problem. Your green suite today says: on *this*
machine, with *these* cached artifacts, the tests pass. That is a fact about your machine,
not about your code. The moment another machine — a colleague's, or tomorrow's you — runs
the suite, whether it passes should depend only on the repository, not on what happened to
be installed. The fresh container is the machine that proves it.

### Check what you already know

Answer these out loud before you read on. Treat anything you hesitate on as new material.

1. `docker compose up -d` prints success quickly and then a service quietly crash-loops.
   Been there? Why do you think the command "always exits 0" right then?
2. A teammate says "the fleet manager should just open `localhost:1883`." List the two
   ways that sentence breaks the rules you have met in this repo so far.
3. What is actually inside a build folder like `bin/`? If a container's build "copies
   everything from the context," what would a stale `bin/` hide, and who would find out
   the hard way?

---

## Verification table

Everything below was checked by running a command on **2026-10-08**. The compose and
broker/database experiments ran in `/tmp/opencode/lesson03-scratch/`; the toy in
`/tmp/opencode/lesson03-toy/`. Every container was stopped and every image was deleted
afterwards (`docker compose down`, `docker rmi`). On this repo I ran read-only `find`,
`grep`, and `docker` config commands only; no container from these experiments ever
touched `src/`.

| Claim | How I checked | Result |
|---|---|---|
| Installed tools | `dotnet --version`; `docker --version`; `docker compose version` | `10.0.112` (exit 0); `Docker version 29.8.1, build 4a63305`; Compose v5 |
| Docker daemon is running | `docker info --format '{{.ServerVersion}}'` | `29.8.1` |
| No broker/database image was on this machine before this session | `docker images` | only `hello-world`, `mower-sim:m0`, `ros:lyrical-ros-base`, `ubuntu:24.04` |
| The pinned image tags all exist | `docker manifest inspect` on `eclipse-mosquitto:2.0`, `eclipse-mosquitto:2`, `postgres:17`, `mcr.microsoft.com/dotnet/sdk:10.0` | all exit 0 |
| A compose file with services `broker` and `database` lists exactly those two | throwaway compose file, `docker compose -f compose.yml config --services` | prints `broker`, `database`, exit 0 |
| `${VAR:-default}` substitution works in compose service values | throwaway compose file with `ports: "127.0.0.1:${MQTT_HOST_PORT:-1883}:1883"` | `docker compose config` shows the resolved `1883` |
| An OS environment variable overrides the default without editing the file | `MQTT_HOST_PORT=18830 DB_HOST_PORT=54330 docker compose config` | shows `published: "18830"` and `"54330"`; defaults stay when the vars are absent |
| Relative paths in a compose file resolve against the compose file's folder | `-f docker/compose.yml` run from a parent directory | bind source resolves to `…/docker/mosquitto.conf` |
| The default Mosquitto 2 container refuses remote clients | started `eclipse-mosquitto:2.0`, then ran a client container on the same network | broker log: `Starting in local only mode... Create a configuration file which defines a listener to allow remote access`; client: `Error: Connection refused`, exit 1 |
| A mounted config file ends local-only mode | mounted `listener 1883` + `allow_anonymous true` | broker log: `Opening ipv4 listen socket on port 1883`; client on the compose network connects and receives a retained message, exit 0 |
| The host loopback port reaches the broker | client on the host network, `mosquitto_sub -h 127.0.0.1 -p 1883` | receives the same retained message, exit 0 |
| Mosquitto's version under the `:2.0` tag | container log | `mosquitto version 2.0.22` |
| Postgres refuses to start without a password | compose service with no `POSTGRES_PASSWORD` | `up -d` exits **0** but the container then exits; log: `Error: Database is uninitialized and superuser password is not specified. You must specify POSTGRES_PASSWORD to a non-empty value` |
| `up -d`'s exit 0 does not mean "ready" | the same misconfigured run | `docker compose ps -q | wc -l` prints **1** (the name-less postgres already exited); `docker compose exec` fails with `service "database" is not running`, exit 1 |
| With a password the database comes up | compose with a password + `pg_isready` healthcheck | healthcheck passes; `docker compose up -d --wait` exits 0 and reports the container **Healthy** |
| `postgres:17` listens on 5432 | container log | `database system is ready to accept connections` |
| The repo's `global.json` pin is satisfiable inside the SDK image | built the reference Dockerfile, then `docker run --rm … dotnet --version` | image SDK is `10.0.401`, acceptable given `10.0.112` + `rollForward: latestFeature` |
| The repo builds in a fresh container | `docker build -f docker/Dockerfile.build -t … .` on a reference shape | exit 0, `Build succeeded. 0 Warning(s) 0 Error(s)` |
| The suite passes in that container | `docker run --rm …` (default command) | exit 0; exactly two `Passed!` lines: `Passed: 2` (Contracts.Tests), `Passed: 1` (FleetManager.Tests) |
| `.dockerignore` keeps host build output out | dropped `src/Contracts/bin/POISON.txt` on the host, built, checked the image | file absent inside the image; the `bin/` that exists there is the container's own fresh output |
| Doctor's own sanity check, toy 1: an image runs and exits | `FROM busybox` + `CMD ["echo", "unit 1 ready"]` | `docker run --rm` prints `unit 1 ready`, exit 0 |
| Doctor's own sanity check, toy 2: a service name is a hostname | two `busybox` services, one pings the other by name | `PING foreman (172.18.0.3): 56 data bytes`, exit 0 |

Not yet verified:

- Any file the owner is about to write — this assignment is a contract, not a report.
- MQTT behaviour under loss and duplication (M2's lesson, against the real broker).
- The .NET database driver (Npgsql or EF Core, M3) and a persistence volume — decision
  records 02 and 03 defer both.
- Whether ports 1883/5432 are free on the machine where the reviewer runs the gates — the
  compose file's overrides are exactly for that case.

---

## Options survey

Lesson 02's pattern, three times this time — because M0's "done when" has three
sub-questions that each demand a real choice: *which transport and broker*, *which
database*, and *what shape the fresh-container build takes*. Each choice is decided here
and logged in a decision record. The records carry the full reasons; the surveys below are
the compressed version.

### Survey 1: transport and broker (→ [decision record 02](../decisions/02-message-transport.md))

Lesson 02 kept the words MQTT, topic, and endpoint for "decision record 02 … at M2." Here
is the twist: the compose file must name a **broker image** today — M0's Docker setup runs
one. So record 02 is written today, for a narrow reason: it confirms the spec's transport
choices (MQTT for fleet messages, REST for the mock WMS) and picks the broker. Topics,
QoS, retries, credentials, and TLS stay M2's problem, where they belong (the record says
so, and the M2 record must link back).

For the broker, three honest options:

| | A: Eclipse Mosquitto | B: EMQX | C: HiveMQ CE |
|---|---|---|---|
| Footprint | one small process, config is two text lines | Erlang build + dashboard + clustering | JVM, heavier |
| What M0-M2 needs from it | a broker that containers can publish and subscribe to | nothing extra at M2 | nothing extra |
| Where it is used for real | the reference broker everyone learns on | scale-out fleets | enterprise fleets |

**Decision 1: Eclipse Mosquitto, image `eclipse-mosquitto:2.0`, with a two-line config
file and a dev-time posture of anonymous, plaintext, loopback-only** — decided and logged
in [record 02](../decisions/02-message-transport.md), including the trade-off and what
would change it (a real deployment, M2's credential lesson).

### Survey 2: the database (→ [decision record 03](../decisions/03-database.md))

"M0 … a Docker setup for the broker **and database**." A database, as a *service*:

| | A: PostgreSQL 17 | B: SQLite | C: Microsoft SQL Server |
|---|---|---|---|
| Runs as a containerised service (the setup must have a database half) | yes | **no** — a library inside your process, no server | yes |
| Weight on a laptop | ~650 MB on disk (measured) | in-process | ~1.5 GB + EULA |
| M3's lesson "fleet manager survives a restart" | real: server outlives its client, manager reconnects | hidden: the file just opens | real |

**Decision 2: PostgreSQL 17, image `postgres:17`** — decided and logged in
[record 03](../decisions/03-database.md). The decisive reason, in one line: M0's setup
must contain a database *service*, and M3's whole lesson is a database that outlives its
client — SQLite gives this milestone neither. The record also notes SQL Server's honest
appeal in a Microsoft-shop warehouse and what would make it win.

### Survey 3: the fresh-container build's shape (→ [decision record 04](../decisions/04-fresh-container-build.md))

"How does 'a fresh container build and test the repo' become a folder on disk and a
command?": 

| | A: `docker/Dockerfile.build`, context = repo root | B: root `Dockerfile` | C: a `build` service in compose |
|---|---|---|---|
| Home in this repo | `docker/` is AGENTS.md's file for "reproducible environment" | no home in the layout | compose exists to run *services*, not one-off builds |
| Fresh-container guarantee | yes (copies `global.json` + `src/`) | yes | yes, but needs `--profile` to stay out of `up -d` |

**Decision 3: `docker/Dockerfile.build`, context = repository root, with `.dockerignore`
at the root** — decided and logged in [record 04](../decisions/04-fresh-container-build.md).
The record fixes the facts that make it honest: base image
`mcr.microsoft.com/dotnet/sdk:10.0` (record 01), `global.json` and `src/` copied, `dotnet
build` runs at image-build time, `dotnet test` is the default command, and `.dockerignore`
keeps `.git` and host `bin/`/`obj/` out — while `global.json`, `src/`, and `docs/` must
stay in (lesson 02's stretch test reads the contract page from the build output).

**Numbering note.** Record 01a kept `02` for transport and `03` for the database, so
today writes 02, 03, and 04. The next number available is 05.

---

## Lecture

Today we are going to learn about a container image and the Dockerfile, Compose's
service file, and why configuration lives in one place instead of in code.

### The image, the container, and the Dockerfile

An **image** is a frozen recipe: the operating system pieces, the runtime, the files —
everything your program needs to run, sealed into one read-only bundle. A **container**
is that recipe actually running: a box where the process starts, does its work, and
exits. The image does not change while the container runs. You can run the same image a
hundred times; each run is a fresh container.

The **Dockerfile** is the recipe's steps, written as instructions: start from this base
image, copy these files, run this step. A build reads the Dockerfile and produces the
image.

Two forces turn a naive Dockerfile into a *good* one:

**The build context is what the recipe can see.** When you run a build, you name a folder
— *"build, using the current directory as the source"* — and the Dockerfile can only
`COPY` things from that folder. It cannot read your home directory, your NuGet cache, or
your SSH keys. That is a feature, not a limitation: it is how a container stays "fresh."
The context also has a veto pile, `.dockerignore`: a file listing what the context must
NOT carry. Contexts can be enormous — the workspace in this repo holds `spec/`, `docs/`,
`src/`, and `docker/` — and `.dockerignore` is how you stop a stale `bin/` or the whole
`.git` history from hitching a ride.

**Is a build "fresh" or just "a build"?** Consider what a normal `dotnet build` on your
host uses: the SDK that happens to be installed, cached NuGet packages, and the previous
build's output. All three can hide cracks. The project named its SDK (`global.json`,
pinned to 10.0.112); the container names its SDK too (*"start from
`mcr.microsoft.com/dotnet/sdk:10.0`"*) — and measured this session, the container's SDK
is 10.0.401, which `rollForward: latestFeature` accepts, so the pin is a promise here the
same way it is on the host. The container has no pre-built output (`.dockerignore` keeps
`bin/`/`obj/` out), so the only `bin/` a build can produce starts from nothing. When the
suite passes there, the answer to "was that your machine or your code?" is: your code.

A container build also compiles the project's whole structure — including the contract
tests that read `docs/`. Lesson 02's stretch test finds the contract page relative to
`bin/`, so the context must keep `docs/` in the image alongside `src/`. The image is not
just the code; it is the code *plus the files the code is required to read.*

### Compose: services, names, and ports

A single container is one box. This milestone needs **two** boxes that run at the same
time and can reach each other: the broker and the database. Running two by hand — two
`docker run` commands, two port flags, two containers to remember to stop — works once and
scales poorly. Compose is the tool for exactly this: one file declares the whole stack,
and one command brings it up or down.

A compose file (`docker/compose.yml`) declares a list of **services** — one file
describes the whole stack. A service names a shell we can launch: "take image X, publish these ports,
inject these environment variables, mount this file." Compose then:

- creates one private **network** for the project, and gives it the project name (with a
  `name:` key in the file, measured: `name: shuttle-fleet` produces a
  `shuttle-fleet_default` network),
- runs one container per service — `shuttle-fleet-broker-1`, `shuttle-fleet-database-1`
  — and
- registers each service **by its service name as a hostname**.

That last one is the whole magic of a fleet. A container can reach the broker's MQTT port
by the hostname `broker`, and the database by `database` — no IP addresses to remember,
nobody to tell the containers where they live. Measured in the toy below: a container
named `winder` pinged `foreman` and internally Docker resolved it; the hostname *was* the
service name.

**Ports** are the door between the containers' private network and the host. Containers on
the private network reach each other directly, but something *outside* (your host tools,
later the fleet manager running on the host) needs a published port: `ports: "HOST:CONTAINER"`.
The host side is where configuration matters, and where the next section's rule bites.

**Two commands only**, from AGENTS.md (read them before running the assignment):

```
docker compose -f docker/compose.yml up -d
docker compose -f docker/compose.yml down
```

Honest note about `up -d` (measured, twice): `up -d`'s exit code means "compose sent the
containers off", not "the services are ready". The misconfigured postgres exited after
`up -d` reported 0 — the container quietly died, and a count of *running* containers
caught it. **"Started" and "ready" are different facts.** That is exactly why the
acceptance below counts running containers after a settle beat instead of trusting the
command's exit code.

### Configuration, not hard-coded wiring

AGENTS.md's rule: *"No hard-coded names, topics, timeouts, retry counts, or sizes. Use
configuration."* This lesson is where the rule stops being an idea and starts being
checkable, because Docker services are full of things that differ by machine:

- which port 1883 is free on *this* host,
- which port 5432 is free on *this* host,
- which user/password the database accepts,
- (later) the broker's address as seen by each client.

None of these are properties of the code. They are environment. The rule says: keep them
in configuration, in one place, overridable — and never copy them into `src/`.

Compose gives you the override for free. A value like
`"127.0.0.1:${MQTT_HOST_PORT:-1883}:1883"` reads: publish on the host's loopback address
(a practice from records 02/03 — dev traffic stays on this machine), on the port named by
the `MQTT_HOST_PORT` environment variable, defaulting to `1883` when the variable is
unset. Measured this session: with `MQTT_HOST_PORT=18830`, the resolved configuration
published 18830 without editing the file. The *default* allows one-port setups; the
*override* is how a reviewer with a busy port runs the same file. Notice the default lives
in the compose file (configuration) and the environment variable lives in the shell
(also configuration, just further out). Neither is in code.

The gate for this rule is a `grep` over `src/` for the words that would betray a
hard-coded address — `broker`, `localhost`, `:1883`, `postgres`, `Host=`, and so on. The
fleet manager at M2 will read its broker address from configuration; today there is no
configuration, so the only honest answer for `src/` is *nothing at all*. Code that knows
the topology of the warehouse where it runs is code that cannot be moved to a machine
with different ports. A shuttle that carries a warehouse's address book in its firmware
is the same failure as a program that does.

Configuration also means the trade-offs are written down. The broker runs **anonymous and
plaintext, on the host loopback** (record 02). The database gets **default credentials,
also loopback-only** (record 03). Both are stated dev-time postures with a recorded "what
would change this" — the reviews that follow this lesson can hold the owner to those
reasons, because they are logged, not guessed.

### Recap

An image is the frozen recipe; a container is one run of it. A Dockerfile is the recipe's
steps. The build context limits what the recipe can copy, and `.dockerignore` is its veto
pile — the two together are what make a build fresh.

Compose declares a stack of services in one YAML file. Services reach each other by name
on a private network; ports are the door to the host; `up -d` and `down` are the two
commands.

Configuration means no address, port, or name written in code. Compose's
`${VAR:-default}` keeps machine-specific values out of `src/` and in one configuration
file, overridable. The trade-offs (anonymous broker, dev credentials) are logged in the
records, not invented later.

---

## Worked example

A toy: a corridor on a simulated floor — one image under test and two containers that
talk by name. It is **not** the assignment: different domain, different names, empty
`busybox` images instead of your repo's SDK, no `docker/compose.yml` in the repository.
It exists to show three mechanics — image vs container, the Dockerfile, and service-name
reachability — small enough to see in one glance. Do not copy it. The toy stays a toy.

Everything below ran in `/tmp/opencode/lesson03-toy/` and was deleted afterwards
(containers down, both images removed). Tiny images (`busybox` is about 2 MB), so these
were fast.

**Part 1 — an image, then containers from it.**

```bash
mkdir -p /tmp/opencode/lesson03-toy && cd /tmp/opencode/lesson03-toy
printf 'FROM busybox\nCMD ["echo", "unit 1 ready"]\n' > Dockerfile.toy
docker build -f Dockerfile.toy -t lesson03-toy-img .
docker run --rm lesson03-toy-img
docker run --rm lesson03-toy-img
```

Observed: the build finished (`=> … DONE 0.3s`), and each `docker run` printed
`unit 1 ready` and exited 0. Two runs, two fresh containers, one frozen image. The second
run has no memory of the first — that is the "fresh" the milestone means.

**Part 2 — two containers that reach each other by the name the compose file gives
them.**

```bash
cat > compose.yml <<'EOF'
name: toy-floor
services:
  foreman:
    image: busybox
    command: ["sleep", "300"]
  winder:
    image: busybox
    command: ["ping", "-c", "2", "foreman"]
EOF
docker compose -f compose.yml up --abort-on-container-exit
```

First attempt, observed: `winder-1 | ping: bad address 'foreman'` and exit 1. The two
containers came up at the same moment and `winder` asked DNS for `foreman` a beat too
early. That is the "started ≠ ready" race again, this time on the inside of one machine.
A settled version —

```bash
cat > compose.yml <<'EOF'
name: toy-floor
services:
  foreman:
    image: busybox
    command: ["sleep", "300"]
  winder:
    image: busybox
    command: ["sh", "-c", "sleep 2; ping -c 2 foreman"]
EOF
docker compose -f compose.yml up --abort-on-container-exit
docker compose -f compose.yml down
```

Observed: `winder-1 | PING foreman (172.18.0.3): 56 data bytes` and exit 0 — `winder`
pinned `foreman` by name, Docker's private network supplied the address. Then `down`
stopped both and removed the `toy-floor_default` network.

The host port and hostname lines of the assignment do exactly the same thing one layer
out: the compose file names `broker` and `database`, and the containers reach them by
those names.

---

## Say it out loud

Answer these without notes, the way you would in an interview:

1. Your teammate says "the build passed on my machine, so the code is fine." What made me
   measure `dotnet test` in an empty container, and which two files keep a host's build
   output out of that container's build?
2. Why does Mosquitto 2 need a config file at all? What does its default container do,
   what log line gave it away, and where does the file live in this repo?
3. Name the three things from this lesson you would put in a compose file rather than in
   `src/`, and the syntax that lets a port have a default and an override at once.
4. `docker compose up -d` exits 0, and five minutes later the dashboard is empty.
   Explain what "0" actually meant, using what I measured with the password-less
   postgres. What does the acceptance block count instead, and why is that better?
5. Two decision records shipped this week. Which one lets M2's lesson start from a decided
   broker, and what did it defer on purpose?

---

## Assignment

Write the Docker setup for the broker and the database, and the Dockerfile that proves the
repo builds clean in a fresh container. This is your code. Type it, run it, be able to
explain every line.

### Requirements

1. **`docker/compose.yml`** — the stack, with the facts below **fixed**, not a menu:
   - top-level `name: shuttle-fleet` (the project name; measured: it names the private
     network and the containers `shuttle-fleet-broker-1` / `shuttle-fleet-database-1`),
   - two services named `broker` and `database`, exactly — nothing else in `up -d`;
   - **`broker`**: image `eclipse-mosquitto:2.0`; publish the container's 1883 on the host
     **loopback only**, as `"127.0.0.1:${MQTT_HOST_PORT:-1883}:1883"`; mount
     `./mosquitto.conf:/mosquitto/config/mosquitto.conf:ro`;
   - **`database`**: image `postgres:17`; publish its 5432 as
     `"127.0.0.1:${DB_HOST_PORT:-5432}:5432"`; feed it the connection values from the
     environment with defaults, using exactly these variable names:
     `POSTGRES_USER: ${SHUTTLE_DB_USER:-shuttle}`,
     `POSTGRES_PASSWORD: ${SHUTTLE_DB_PASSWORD:-shuttle}`,
     `POSTGRES_DB: ${SHUTTLE_DB_NAME:-shuttle}`.
   Every value above is configuration by design (section
   [Configuration, not hard-coded wiring](#configuration-not-hard-coded-wiring)). Be able
   to say why each fixed fact sits where it sits — the pins and the loopback are the
   records' promises about reproducibility and dev posture.
2. **`docker/mosquitto.conf`** — a config file whose two operating lines are
   `listener 1883` and `allow_anonymous true`, plus whatever comments you need to explain
   *why those two lines exist* (record 02: the default is local-only, and anonymous is the
   stated dev posture until M2's credential lesson). Explain out loud why a broker in a
   compose file *needs* this file where a broker on its own didn't.
3. **`docker/Dockerfile.build`** — the fresh-container build. All five facts fixed by
   record 04: `FROM mcr.microsoft.com/dotnet/sdk:10.0` as the first instruction; a
   `WORKDIR`; `COPY global.json ./` and `COPY src/ ./src/` (the pin must be in the
   container before anything builds); a `RUN` that builds the solution; and `dotnet test`
   as the image's **default command** (`CMD`), so the suite's verdict is the image's exit
   code. Walk yourself through each line the night before review.
4. **`.dockerignore`**, at the repository root (the build context is the root). It keeps
   out `.git` and host build output (`**/bin/`, `**/obj/`). It must **not** exclude
   `src/`, `docs/`, or `global.json` — the container needs all three (lesson 02's stretch
   test reads the contract page from the build output).
5. **`docker/README.md`** — the pointer AGENTS.md promises ("See `docker/` once M0 is
   done"). In your own words: what lives here, the two commands (`docker compose -f
   docker/compose.yml up -d` / `… down`), and the overridable values
   (`MQTT_HOST_PORT`, `DB_HOST_PORT`, `SHUTTLE_DB_USER`, `SHUTTLE_DB_PASSWORD`,
   `SHUTTLE_DB_NAME`).
6. **No C# file changes.** The fresh-container build compiles and tests the `src/` from
   lessons 01–02 as it is. Nothing in `src/` may name the broker, a port, an address, or
   a database this lesson — requirement 6 is where the configuration rule is enforced.

No MQTT topics or QoS, no C# MQTT library, no Npgsql or EF: those are M2's and M3's
records. This assignment runs the *infrastructure* and proves the *build*.

### Acceptance criteria (all command-checkable)

From the repository root. Read every exit code directly, never through a pipe. Judge the
`grep` commands by output where noted, exactly as lesson 02 taught.

```bash
# Phase 1 — the five files exist; the loop prints nothing
for f in docker/compose.yml docker/mosquitto.conf docker/Dockerfile.build .dockerignore docker/README.md; do
  test -f "$f" || echo "missing: $f"
done
```

```bash
# Phase 2 — the compose file parses and declares the decided stack
docker compose -f docker/compose.yml config >/dev/null        # exit 0 — it parses
docker compose -f docker/compose.yml config --services        # prints exactly two lines: broker, database
grep -q '^name: shuttle-fleet$' docker/compose.yml            # exit 0
for s in broker database; do
  grep -qE "^[[:space:]]*$s:" docker/compose.yml || echo "no service: $s"   # prints nothing
done
grep -q 'image: eclipse-mosquitto:2.0' docker/compose.yml     # exit 0 — pinned, not floating
grep -q 'image: postgres:17' docker/compose.yml               # exit 0
! grep -q ':latest' docker/compose.yml                        # exit 0 — no un-pinned tag anywhere
grep -q 'mosquitto.conf' docker/compose.yml                   # exit 0 — the config is mounted
grep -q '127.0.0.1' docker/compose.yml                        # exit 0 — host loopback only
grep -Fq '${' docker/compose.yml                              # exit 0 — something is overridable
for v in MQTT_HOST_PORT DB_HOST_PORT SHUTTLE_DB_USER SHUTTLE_DB_PASSWORD SHUTTLE_DB_NAME; do
  grep -q "\${$v" docker/compose.yml || echo "missing var: $v"    # prints nothing
done
```

With `docker/` empty the `config` command fails — nothing to parse — and every file grep in
phases 1–2 exits 1. On a correct file they all exit 0; read `--services`'s two lines by
name.

```bash
# Phase 3 — the stack runs, stays up, and stops cleanly
docker compose -f docker/compose.yml up -d                    # exit 0 — but read the note below
sleep 5                                                       # "started" and "ready" are different facts
docker compose -f docker/compose.yml ps                       # eyeball: both containers Up
docker compose -f docker/compose.yml ps -q | wc -l            # prints 2 (judge by output)
docker compose -f docker/compose.yml down                     # exit 0 — nothing left running
```

Two things about that block. First, the `sleep 5` is a check convention, not product
configuration: it is how the check stops being a race. Measured with the password-less
postgres: `up -d` exited **0**, and within seconds the container had exited — `ps -q |
wc -l` printed 1, and a compose `exec` failed with `service "database" is not running`.
Counting running containers after a settle beat is the check the lesson trusts, not the
command's own exit code. Second, if the count prints 1 because *one* service died, the
probable causes are a real config mistake (like that password) or a busy host port — the
override variables (`MQTT_HOST_PORT`, `DB_HOST_PORT`) exist for exactly the second cause.
Verify with `docker compose -f docker/compose.yml logs database` and
`… logs broker`, and explain what you see.

```bash
# Phase 4 — no service address or port has leaked into code; prints nothing
grep -rniE 'broker|mqtt|localhost|127\.0\.0\.1|:1883|:5432|postgres|Host=' src --include='*.cs'
```

Measured today: this grep prints nothing. There is no `!` on this line — judge by **output**,
not by exit code: any line it prints is an address or a service name living in code, and
requirement 6 says none may. When a value truly must be read at M2, it will come from
configuration — never from a `grep`-able literal.

```bash
# Phase 5 — the fresh-container build: compile clean, then suite green, both inside the image
T=$(mktemp)
docker build -f docker/Dockerfile.build -t shuttle-fleet-build . >"$T" 2>&1   # read $? here: exit 0
rm -f "$T"
for f in 'FROM mcr.microsoft.com/dotnet/sdk:10.0' 'dotnet build' 'dotnet test' 'global.json'; do
  grep -qF "$f" docker/Dockerfile.build || echo "Dockerfile missing: $f"       # prints nothing
done
U=$(mktemp)
docker run --rm shuttle-fleet-build >"$U" 2>&1                    # read $? here: exit 0
grep -c 'Passed!' "$U"                                             # prints 2 — one line per test project
rm -f "$U"
```

Honest timing note: the *first* `docker build` downloads the `.NET` SDK base image
(several hundred MB), and `docker run` then re-uses it — slow once, cached after. The
four `grep`s check the facts that keep the build fresh: the pinned base (record 01), the
build-then-test split (record 04), and the copied `global.json` without which the
container could silently build against a different SDK. The `grep -c 'Passed!'` prints 2
only when **both** test projects actually ran — the same "a DLL with no tests still
exits 0" trap lesson 02 measured.

```bash
# Phase 6 — the .dockerignore promises: three lines present, three words absent
for pat in '.git' 'bin/' 'obj/'; do
  grep -qF "$pat" .dockerignore || echo "dockerignore missing: $pat"          # prints nothing
done
for pat in 'global.json' 'docs/' 'src/'; do
  if grep -qF "$pat" .dockerignore; then echo "dockerignore wrongly excludes: $pat"; fi
done                                                                          # prints nothing
```

The first loop names the promises (keep the history and the host's build output out of
every context); the second loop names the non-promises the earlier lesson made — the
container still needs `global.json`, `src/`, and `docs/`. And a teaser: a `.dockerignore`
line like `docs/` is exactly the kind of "passes on my machine, fails reviewed elsewhere"
trap this gate exists to catch — lesson 02's stretch test reads the contract page from
the build output, so the mistake would hide *inside the container only*.

From `src/`, the old gates must still hold — nothing in this lesson may touch them:

```bash
dotnet build        # exit 0
dotnet test         # exit 0
```

### Stretch goal

Give `database` a **healthcheck** and let compose wait for it. Add a healthcheck using
the image's own `pg_isready` and the same `SHUTTLE_DB_*` variables you set, something in
the shape `pg_isready -U ${SHUTTLE_DB_USER:-shuttle} -d ${SHUTTLE_DB_NAME:-shuttle}`
(measured this session: the healthcheck passes and `docker compose up -d --wait` exits 0
and reports the container **Healthy**).

Accept: `docker compose -f docker/compose.yml up -d --wait` exits 0 and
`docker compose -f docker/compose.yml ps` shows `(healthy)` in the database's status
column; and for the second half, `MQTT_HOST_PORT=18830 docker compose -f
docker/compose.yml config` shows the broker published on 18830 while a plain
`… config` still shows 1883 (measured — this is the override working without any file
edit).

Say out loud while you build it: what does the healthcheck make *true*, and what does
`--wait` mean that plain `up -d` never did?

### Not in this assignment

MQTT topics, QoS, retries, idempotency, credentials, and TLS — M2, whose record must link
back to [record 02](../decisions/02-message-transport.md). Npgsql, EF Core, and the
persistence volume — M3, linked to [record 03](../decisions/03-database.md). A runtime
image for the fleet manager — nothing to ship yet; noted in [record 04](../decisions/04-fresh-container-build.md).
Continuous integration. The mock WMS's REST API (M6). `ShuttleSim` (M1).

---

## Closing M0

Read the spec's line once more: *"Done when: clean build and tests pass in a fresh
container."* After this assignment, the repo can honestly claim all four words: **clean**
(the compile gate), **build** (inside the container), **tests pass** (the suite's own exit
code), and **fresh** (no host artifact can leak through `.dockerignore`).

Who says "done"? Not you, and not I. AGENTS.md gives the reviewer that line: the
milestone table in `README.md` moves to "done" only when the review passes, with the
verifying command written beside it. Lesson 04 — the first milestone lesson — waits for
that pass. Meanwhile the working tree may still be dirty, and that is fine: commits are
yours, small and frequent, one concept each.