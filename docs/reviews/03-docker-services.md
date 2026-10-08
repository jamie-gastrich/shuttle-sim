# Review 03: Docker services and the fresh-container build (lesson 03)

**Date:** 2026-10-08
**Reviewed:** commits `12b9b37` (`M0: Add Docker services setup and fresh-container build
documentation` — lesson 03 and decision records 02/03/04) and `27ac179` (`M0: Add Docker
support with configuration files for broker and database services` — the five deliverables),
on top of `84633c6`. Branch `master`, working tree clean before I started
(`git status --porcelain` empty, exit 0).
**Scope:** lesson 03 and the M0 "done when". This review discharges the lesson-02
"pass with changes" items as far as they bear on M0 (R1 verified below; R2/R3/R4 checked for
regression).

**Verdict: pass with changes (lesson 03).**

Every criterion in the lesson's acceptance block passes, `dotnet build` and `dotnet test` are
green on the host, and the fresh-container gate is genuinely green — `docker build` exit 0,
`docker run --rm shuttle-fleet-build` exit 0 with `grep -c 'Passed!'` = 2 — and I proved by
mutation that the gate can fail (below). One thing keeps this from a clean pass and keeps the
M0 row at `not started`: `docker/Dockerfile.build:6` adds a `RUN dotnet test` layer, which is
precisively the alternative **decision record 04 §3 rejected** — *"Why tests at `docker run`
and not a second `RUN` layer … Baking the test run into the image would bury the suite's
verdict in cache internals."* The record fixes the shape and the record is binding. The fix is
one line, no test or gate failure anywhere.

---

## Commands run, verbatim key output

Exit codes read directly, never through a pipe, except where the lesson itself pipes
(`| wc -l`) and the lesson's own `mktemp` capture pattern.

### From the repository root

**Phase 1 — the five files**

```
$ for f in docker/compose.yml docker/mosquitto.conf docker/Dockerfile.build .dockerignore docker/README.md; do
    test -f "$f" || echo "missing: $f"; done                        (no output)
```

**Phase 2 — compose parses and declares the decided stack**

```
$ docker compose -f docker/compose.yml config >/dev/null            exit 0
$ docker compose -f docker/compose.yml config --services            exit 0
broker
database
$ grep -q '^name: shuttle-fleet$' docker/compose.yml                exit 0
$ for s in broker database; do grep -qE "^[[:space:]]*$s:" docker/compose.yml || echo "no service: $s"; done
(no output)
$ grep -q 'image: eclipse-mosquitto:2.0' docker/compose.yml         exit 0
$ grep -q 'image: postgres:17' docker/compose.yml                   exit 0
$ ! grep -q ':latest' docker/compose.yml                            exit 0
$ grep -q 'mosquitto.conf' docker/compose.yml                       exit 0
$ grep -q '127.0.0.1' docker/compose.yml                            exit 0
$ grep -Fq '${' docker/compose.yml                                  exit 0
$ for v in MQTT_HOST_PORT DB_HOST_PORT SHUTTLE_DB_USER SHUTTLE_DB_PASSWORD SHUTTLE_DB_NAME; do
    grep -q "\${$v" docker/compose.yml || echo "missing var: $v"; done
(no output)
```

**Phase 3 — the stack runs, stays up, stops cleanly** (ports 1883/5432 were **free** on this
host — `ss -ltn` showed no listener, exit 1 — so I ran the plain defaults, no overrides needed)

```
$ docker compose -f docker/compose.yml up -d                        exit 0
$ sleep 5
$ docker compose -f docker/compose.yml ps
NAME                       IMAGE                   STATUS         PORTS
shuttle-fleet-broker-1     eclipse-mosquitto:2.0   Up 5 seconds   127.0.0.1:1883->1883/tcp
shuttle-fleet-database-1   postgres:17             Up 5 seconds   127.0.0.1:5432->5432/tcp
$ docker compose -f docker/compose.yml ps -q | wc -l                prints 2
$ docker compose -f docker/compose.yml down                         exit 0
```

**Phase 4 — no address or service name in code**

```
$ grep -rniE 'broker|mqtt|localhost|127\.0\.0\.1|:1883|:5432|postgres|Host=' src --include='*.cs'
exit 1 — prints nothing (judge by output)
```

**Phase 5 — the fresh-container build** (lesson's own `mktemp` pattern)

```
$ T=$(mktemp)
$ docker build -f docker/Dockerfile.build -t shuttle-fleet-build . >"$T" 2>&1
build-exit=0
#10 [6/6] RUN dotnet test src/ShuttleFleet.slnx -v m   CACHED
#11 naming to docker.io/library/shuttle-fleet-build:latest done
$ rm -f "$T"
$ for f in 'FROM mcr.microsoft.com/dotnet/sdk:10.0' 'dotnet build' 'dotnet test' 'global.json'; do
    grep -qF "$f" docker/Dockerfile.build || echo "Dockerfile missing: $f"; done
(no output)
$ U=$(mktemp)
$ docker run --rm shuttle-fleet-build >"$U" 2>&1
run-exit=0
$ grep -c 'Passed!' "$U"                                            prints 2
Passed!  - Failed:     0, Passed:     2, … Contracts.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     1, … FleetManager.Tests.dll (net10.0)
$ rm -f "$U"
```

The build's layers were all `CACHED` (content-addressed: valid only because the context bytes
match the last build) and **no image pull occurred** — the base `mcr.microsoft.com/dotnet/sdk:10.0`
resolved from local content. The suite's verdict came from `docker run`, which re-executes the
`CMD` on every run — and mutation M1b below proves that re-execution is real.

**Phase 6 — the .dockerignore promises**

```
$ for pat in '.git' 'bin/' 'obj/'; do grep -qF "$pat" .dockerignore || echo "dockerignore missing: $pat"; done
(no output)
$ for pat in 'global.json' 'docs/' 'src/'; do if grep -qF "$pat" .dockerignore; then echo "dockerignore wrongly excludes: $pat"; fi; done
(no output)
```

### From `src/`

```
$ dotnet build
Build succeeded.
    0 Warning(s)
    0 Error(s)
BUILD EXIT -> 0
```

```
$ T=$(mktemp); dotnet test > "$T" 2>&1                             exit 0
$ grep -c 'Passed!' "$T"                                            2
$ grep -c 'No test is available' "$T"                               0
Passed!  - Failed:     0, Passed:     2, … Contracts.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     1, … FleetManager.Tests.dll (net10.0)
```

### Lesson-02 discharge checks

```
$ grep -rn 'Hello, World!' src/FleetManager --include='*.cs'        exit 1 — no output
```

The R1 fix survived: lesson 02's acceptance now scopes the grep to `src/FleetManager`
(`docs/lessons/02-first-message-contract.md:824` and hints `:324`), and the scoped gate is
empty on the finished tree. The unscoped grep still prints the assertion line
(`StartTests.cs:25`), which is exactly why the scoping was the fix.

```
$ git diff 84633c6..27ac179 -- src/                                 (0 lines — src untouched)
$ git log --oneline 84633c6..27ac179 -- src/                        (empty)
```

R2 (no timeout) and R4 (9-space indent on `StartTests.cs:10`; no trailing newline — last byte
of the file is `}` with no `\n`): both unchanged, neither regressed. R3: both new commits carry
the `M0:` prefix (fixed), but see consider 3 for a new message-vs-diff mismatch.

---

## Acceptance criteria (lesson 03, run in full)

| # | Criterion (lesson wording) | Evidence | Result |
|---|---|---|---|
| 1 | Phase 1: five files exist, loop prints nothing | printed nothing | met |
| 2 | `docker compose config` exit 0 | exit 0 | met |
| 3 | `config --services` prints exactly `broker`, `database` | two lines, in that order, exit 0 | met |
| 4 | `grep -q '^name: shuttle-fleet$'` exit 0 | exit 0 | met |
| 5 | service loop prints nothing | printed nothing | met |
| 6 | pinned `eclipse-mosquitto:2.0` grep exit 0 | exit 0 | met |
| 7 | pinned `postgres:17` grep exit 0 | exit 0 | met |
| 8 | `! grep -q ':latest'` exit 0 | exit 0 | met |
| 9 | `mosquitto.conf` mounted, grep exit 0 | exit 0 | met |
| 10 | `127.0.0.1` loopback grep exit 0 | exit 0 | met |
| 11 | something overridable (`grep -Fq '${'`) exit 0 | exit 0 | met |
| 12 | five override vars loop prints nothing | printed nothing | met |
| 13 | `up -d` exit 0 | exit 0 | met |
| 14 | after `sleep 5`, both containers Up | `ps` shows broker and database `Up 5 seconds` with loopback ports published | met |
| 15 | `ps -q \| wc -l` prints 2 | printed 2 | met |
| 16 | `down` exit 0, nothing left | exit 0; `docker ps -a` showed only the pre-existing `hello-world` | met |
| 17 | Phase 4 grep prints nothing | printed nothing (exit 1) | met |
| 18 | `docker build … >"$T"` exit 0 | **exit 0** | met |
| 19 | four Dockerfile-fact greps print nothing | printed nothing | met |
| 20 | `docker run --rm shuttle-fleet-build` exit 0 | **exit 0** | met |
| 21 | `grep -c 'Passed!'` prints 2 | **prints 2** (2 + 1 tests, both projects) | met |
| 22 | .dockerignore present-word loop prints nothing | printed nothing | met |
| 23 | .dockerignore absent-word loop prints nothing | printed nothing | met |
| 24 | `dotnet build` exit 0 | exit 0, 0 warnings, 0 errors | met |
| 25 | `dotnet test` exit 0 | exit 0, `Passed!` count 2, no `No test is available` | met |

**25 of 25 met.** No criterion failed.

**Substance checks (beyond the greps)**

| Check | Evidence | Result |
|---|---|---|
| The stack actually starts a working broker and database, not just two Up processes | with the stack up: broker log `Opening ipv4 listen socket on port 1883` (the mounted config, i.e. not local-only); database log `database system is ready to accept connections`; a client container on the compose network published a retained message to `broker` and a second client received it — `ping`, `sub-exit=0` | met |
| The fresh-container gate can fail (not vacuous) — owner's Dockerfile shape | mutation M1a in a copy at `/tmp/opencode/mut03` (deleted): `StartTests` assertion changed to `Assert.Equal(999, …)`, lesson's own build command → **build-exit=1**, `Failed FleetManager_Starts_Successfully`, `Failed: 1` | met |
| The fresh-container gate can fail regardless of the extra `RUN` layer — record-04 shape | mutation M1b: same broken test, Dockerfile reduced to the record's shape (no `RUN dotnet test`) → build exit 0 but **`docker run` exit 1**, `grep -c 'Passed!'` = **1**, `Failed!` line present — the run step carries the verdict exactly as record 04 designed | met |
| Phase 3's count-2 gate can fail (not vacuous) | mutation M2: compose copy with `POSTGRES_PASSWORD` removed → `up -d` exit **0**, `ps -q \| wc -l` prints **1**, log `You must specify POSTGRES_PASSWORD` — the lesson's measured claim reproduced | met |
| `.dockerignore` really keeps host build output out of the image | dropped `src/Contracts/bin/POISON.txt` on the host, rebuilt (exit 0), `docker run --rm shuttle-fleet-build sh -c 'test ! -e src/Contracts/bin/POISON.txt'` → exit 0 (absent); host tree clean afterwards | met |
| Overrides work without editing the file | `MQTT_HOST_PORT=18830 DB_HOST_PORT=54330 docker compose … config` → `published: "18830"` and `"54330"`; plain config still 1883/5432 | met |
| `src/` untouched by both commits | `git diff 84633c6..27ac179 -- src/` empty; no commit in range touches `src/` | met |
| Owner's files match decision records 02/03 | compose pins, loopback posture, anonymous broker, no named volume for postgres (record 03 §4 says persistence waits for M3), `listener 1883` + `allow_anonymous true` (record 02 §4) | met |
| Owner's Dockerfile matches decision record 04 §3 | the five fixed facts are all present — but line 6 `RUN dotnet test` is the alternative the record explicitly rejected | **not met — should-fix 1** |
| `docker/README.md` carries the three required things | what lives here (and why services), both AGENTS.md commands, all five override vars with their defaults, plus a `:latest` sentence | met |
| Stretch goal (optional): healthcheck + `up -d --wait` | not attempted — no healthcheck in `compose.yml`. Optional, not counted against the verdict; the override half of the stretch accept passes (checked above) | not attempted |

### Lesson-quality check (my review of the lesson, not the owner's work)

- Three concepts declared — *image/Dockerfile*, *compose services*, *configuration not
  hard-coded wiring* — each with its own teaching section. At the cap, not over it.
- **Unsatisfiable criterion hunt:** none found. Every criterion the reference solution must
  pass, passed for this owner's honest solution — including the ones review 02's R1 lesson
  taught us to distrust (`grep -c 'Passed!'` = 2, the `.dockerignore` absence loop).
- **Vacuous-pass hunt:** one hole found and measured — see should-fix 2. Phase 3's
  count-of-running-containers catches a *dead* service but not a *live-but-useless* one; a
  compose file whose `mosquitto.conf` is mounted at the wrong target path passes the entire
  acceptance block while the broker refuses every remote connection. The lesson's own
  verification table measured the local-only failure mode, but no acceptance line re-checks it.
  Not owner-blocking (this owner's mount is correct — proven by the pub/sub roundtrip above).

---

## Findings

### Blocker

None. Tests pass, all 25 criteria met, the fresh-container gate is green and demonstrably
able to go red.

### Should-fix

**1 — `docker/Dockerfile.build:6`: `RUN dotnet test` implements the option decision record 04
§3 rejected.**

```
RUN dotnet build src/ShuttleFleet.slnx -v m
RUN dotnet test src/ShuttleFleet.slnx -v m     <-- line 6
CMD ["dotnet", "test", "src/ShuttleFleet.slnx", "-v", "m"]
```

Record 04 §3 fixes the shape: `dotnet build` at image-build time so a failing compile fails
the build, `dotnet test` as the `CMD` so `docker run`'s exit code is the suite's — and then
answers the exact question in a paragraph headed *"Why tests at `docker run` and not a second
`RUN` layer"*: the reviewer's two commands are two separate verdicts, and *"baking the test run
into the image would bury the suite's verdict in cache internals."* Line 6 is that second
`RUN` layer. The lesson's hints Tier-3 template does not have it either.

Why it matters in this project's terms: AGENTS.md makes the records binding — a decision that
carried its reasons must not be quietly overturned in a file. Concretely, it also muddies the
design you were asked to be able to explain: with line 6, a green `docker build` means
"compiles *and* passes" while the record wants it to mean "compiles", and the tests run twice
per verification (once cached into a layer, once for real at `docker run`).

Fix direction: **delete line 6** — nothing else changes, and every gate above still passes
(mutation M1b shows the record's shape still fails loudly when a test breaks). The only
alternative I would accept is the *professor* amending the record by appending a new entry
that links back to record 04 — records are the professor's file, not yours. **Do not edit
anything under `docs/decisions/`.**

**2 — lesson gate: Phase 3 cannot see an "Up" broker that is unreachable (lesson defect,
measured).** `docs/lessons/03-docker-services.md`, acceptance Phase 3. In a copy under
`/tmp/opencode` (deleted) I mounted the config at a wrong target
(`./mosquitto.conf:/mosquitto/wrong/mosquitto.conf:ro`): `up -d` exit 0, `ps -q | wc -l`
prints **2** — the gate's full pass — while the broker log says
`Starting in local only mode` and a client on the network gets `Error: Connection refused`,
exit 1. Phase 2's `grep -q 'mosquitto.conf'` only checks the string appears, and `ps` only
checks liveness. One cheap line would close it — grep the broker log for
`Opening ipv4 listen socket on port 1883` after the settle beat, or run a client probe.
Not blocking this owner: I verified their broker's real reachability directly (pub/sub
roundtrip, exit 0).

### Consider

**3 — commit `12b9b37`'s message describes files its own diff does not contain.** Its bullets
say "Introduced `docker/compose.yml`", "Created `docker/mosquitto.conf`", "Added
`docker/Dockerfile.build`", "Implemented `.dockerignore`", "Documented … in
`docker/README.md`" — all five are in the *next* commit, `27ac179`; `12b9b37`'s diff is only
the lesson and the three records. This is the review-02 R3 class again (message vs diff), in
new code. Both commits do carry the `M0:` prefix, which fixes the other half of R3.

**4 — R2 (no timeout around the child process in `StartTests.cs`) stands unchanged.** `src/`
is untouched by these commits, so this is not a regression; it is the same consider review 02
left, still due "before M2, not during a red build".

**5 — R4 (cosmetic slips in `StartTests.cs`) stands unchanged:** line 10 is indented with 9
spaces against the file's 8, and the file still ends without a trailing newline. Named so
they get studied, not scrolled past.

**6 — the stretch healthcheck was not attempted.** Optional, and M3's persistence record may
want to own the healthcheck design anyway; noted so it is a decision, not an omission.

### AGENTS.md compliance (checked)

- **Hard-coded names/topics/timeouts/retry counts/sizes in `docker/`:** none that aren't fixed
  by a record or the lesson. Judged per fact, as instructed: image tags
  (`eclipse-mosquitto:2.0`, `postgres:17`) are records 02/03's picks; `listener 1883` is
  record 02's posture (and mosquitto.conf does not do env substitution — the lesson fixes
  this line verbatim); service names `broker`/`database` and `name: shuttle-fleet` are the
  lesson's fixed facts; `127.0.0.1` is the records' loopback promise, while the **host port
  numbers are overridable** (`${MQTT_HOST_PORT:-1883}`, `${DB_HOST_PORT:-5432}`) exactly as
  the rule wants machine-specific values to be; database credentials default but override
  (`SHUTTLE_DB_*`). `WORKDIR /shuttle` and `src/ShuttleFleet.slnx` are repo structure, not
  machine configuration. No violation.
- **Nothing in `src/` written or changed by these commits:** diff empty (above); I read,
  built, tested, and grepped only.
- **Processes clean:** `docker compose -f docker/compose.yml down` after every `up`; the
  six anonymous volumes *my* runs created (timestamps 18:40:30 and 18:42:01 during this
  session) were removed with `docker volume rm`; mutation copy and mutation image
  `shuttle-fleet-build-mut03` deleted; `/tmp/opencode` empty; `dotnet build-server shutdown`
  run; no `dotnet run`/MSBuild node processes (`ps` count 0). Final `docker ps -a`: only the
  **pre-existing 9-day-old `hello-world` container**, left exactly as I found it (not mine).
  The 13 anonymous volumes that predate my session (created 17:03–18:27, before I started)
  and the ~2.97 GB buildx cache were left untouched — pre-existing state, not mine to prune.
- **No image pulls performed** by me: the build resolved `mcr.microsoft.com/dotnet/sdk:10.0`
  from local content (all layers `CACHED`), and broker/database images were already present.
- **Commit messages:** both new commits carry the `M0:` prefix (see consider 3 for the rest).

---

## What is solid

- The compose file is exactly the decided stack — right names, right pins, loopback-only
  publishes, overridable host ports — and it *works*: I did not stop at `ps` showing Up; a
  client container reached `broker` by service name and round-tripped a retained message, and
  postgres logged ready-to-accept before `down`.
- The fresh-container gate is real and can go red. Two mutations, two Dockerfile shapes, both
  caught (build exit 1; run exit 1 with `Passed!` count 1). The password-less compose
  mutation reproduced the lesson's `up -d`=0-but-count-1 measurement. The gate is not the
  vacuous check review 02's R1 taught us to fear.
- `.dockerignore` was tested by behaviour, not by reading: a poison file in the host's
  `bin/` did not exist inside the image.
- The lesson-02 R1 fix survived and is scoped correctly; `src/` never moved.
- One-line-per-concept commits with `M0:` prefixes, five files in one commit, docs in the
  other — small and reviewable.

---

## Explain-it questions — NON-BLOCKING

**The owner waived explain-it re-answers as a gate in review 01 (waiver recorded there,
2026-10-07), and review 02 kept them non-blocking. Nothing below blocks this verdict.**
Answer them out loud before the next lesson.

1. Walk the compose file top-down. For each of the two ports: where does the default come
   from, where does the override come from, and which three numbers in the line are *not*
   overridable on purpose — and which record made them so?
2. `docker compose up -d` exits 0 and the acceptance still runs `sleep 5` and counts
   containers. Using the password-less postgres measurement (mine and the lesson's), say what
   exit 0 did and did not promise, and what the count would print if the broker's config
   were mounted at the wrong path (I measured that one too — see should-fix 2).
3. Your Dockerfile runs `dotnet test` twice. Say what each run is *for*, what a reviewer
   learns from `docker build`'s exit code versus `docker run`'s, and then read record 04 §3's
   "not a second `RUN` layer" paragraph aloud and reconcile it with line 6.
4. My mutation changed one assertion and the build went red — but my mutation M1b removed
   line 6 first, and the *`docker run`* step went red instead. Why does the gate hold either
   way, and what does that say about where the "tests pass" verdict is supposed to live?
5. My Phase 5 build printed `CACHED` on every layer. Why is that still a fresh build — what
   does content-addressed caching mean, and what would have to change in `src/` to invalidate
   it? What did the poison-file test prove that the cache could not?

### Blocks to study (unclear, fragile, or copied-looking)

- `docker/Dockerfile.build:6` — the record-vs-file conflict. Be able to explain why it is
  there and what record 04 says, in one breath each.
- `docker/compose.yml:14-16` — `POSTGRES_*` names fed from `SHUTTLE_DB_*` names. Say why the
  file has two naming systems and which one belongs to postgres and which one belongs to us.
- `docker/mosquitto.conf` — both lines: what breaks without `listener`, what breaks (at M2)
  without `allow_anonymous`, and the exact log line that distinguishes local-only from
  listening.
- `docker/README.md:28` — the defaults line. Check each of the five defaults against the
  compose file yourself; a README that drifts from the file is the same failure as a contract
  page that drifts from the type.
- `git show 12b9b37 --stat` versus its message — consider 3. Write the message it should
  have had.

---

## Revisit list

- **Records as binding decisions.** When a file implements an option a record rejected, the
  fix is the file (or a new linked record) — never the record, and never silence. You now
  have a personal example.
- **Started vs ready, one more level.** You have measured dead-service (password) and, in
  this review, live-but-unreachable (wrong mount). Say what class of failure each check
  catches and what neither catches.
- **Content-addressed build cache.** Why `CACHED` lines do not undermine "fresh", what
  exactly invalidates a `COPY src/` layer, and why the poison test was still necessary.
- **Anonymous volumes.** postgres declares its own data volume; `down` without `-v` leaves it
  behind (13 on this machine predate this session). What that means for M3's "does down lose
  data" question, per record 03 §6.
- **Writing the gate from the failing side.** You have now seen both failure modes across
  two lessons: a gate the honest solution fails (02's R1) and a gate a broken solution passes
  (03's should-fix 2). State the property you want instead: *the honest solution passes, the
  realistic break fails.*
- Next up: **lesson 04, the first milestone lesson (M1)** — blocked only by the one-line
  should-fix above.

---

## End-product check

This is the piece the whole milestone rested on, and it is built to carry forward: the
compose stack is the same broker M2's round trip will wire (record 02 already names it), the
same database M3 will reconnect to (record 03 deferred the volume to exactly that lesson),
and `docker/Dockerfile.build` + `.dockerignore` are the build CI will eventually run (record
04 reserved that). Nothing here looks like something to throw away, and nothing hard-codes a
machine: every value that differs between laptops has an override. The residual risks are
named and small — one line that contradicts a record (should-fix 1), one hole in the lesson's
own gate that will matter the day a future owner's mount path drifts (should-fix 2), and the
unchanged R2/R4 items in `StartTests.cs` that M1 inherits. **M0 does not close in this
review** — its "done when" is now *substantively* proven (green build and green suite in a
fresh container, measured end to end), but the README flip requires records consistent with
the owner's files, and should-fix 1 is a one-line, same-day discharge from that.

---

## README

**Not changed.** The M0 row stays `not started` and no verification note is added, because
the flip conditions are not all met: should-fix 1 (`docker/Dockerfile.build:6` vs record 04
§3) means the owner's files are not yet consistent with the decision records. Every other
condition — 25/25 acceptance criteria, host build/test green, genuinely green fresh-container
build+test, AGENTS.md process and hard-coding rules — is met and documented above, so this is
a one-line fix from flipping. When it lands, the verification note should read:

> M0 verified 2026-10-08 by review 03 (`docs/reviews/03-docker-services.md`): from repo root
> `docker compose -f docker/compose.yml config --services` prints broker/database;
> `docker compose -f docker/compose.yml up -d` / `ps -q | wc -l` = 2 / `down`;
> `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` exit 0;
> `docker run --rm shuttle-fleet-build` exit 0 with `grep -c 'Passed!'` = 2; from `src/`,
> `dotnet build` and `dotnet test` exit 0.

---

## What I changed in this repository

- **Added:** `docs/reviews/03-docker-services.md` (this file). Uncommitted — I commit only
  when asked.
- **README.md:** not touched (reason above).
- **`src/`, `docker/`, `docs/lessons/`, `docs/decisions/`:** not touched. Mutation experiments
  ran against a copy in `/tmp/opencode/mut03`, now deleted along with its image; the six
  anonymous volumes my `up` runs created were removed; `git status --porcelain` empty after
  every build; `dotnet build-server shutdown` run; no broker, database, container, `dotnet
  run`, test host, or MSBuild node left running (`docker ps -a` shows only the pre-existing
  9-day-old `hello-world` container, which I left as I found it); `/tmp/opencode` empty.
