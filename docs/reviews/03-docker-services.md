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

---

# Re-review — 2026-10-08: the fixes, re-verified; M0 closes

**Re-verified at:** commit `78b9a6a` (`M0: Add review documentation for Docker services and
fresh-container build (lesson 03)`), branch `master`, `git status --porcelain` empty
(exit 0) before I started. Since `27ac179` (the tip under the first review) exactly five
files moved, all inside `78b9a6a`: `docker/Dockerfile.build` (one line deleted — should-fix
1), `docs/lessons/03-docker-services.md` and `docs/lessons/03-docker-services-hints.md`
(the professor's revision for should-fix 2 and the R1-class sweep),
`docs/decisions/04-fresh-container-build.md` (§8 addendum), and this review file.
`git diff 84633c6..HEAD -- src/` is still **0 lines** — `src/` has never moved since before
lesson 03 began.

The professor's caveat is closed here: the revised gates were previously verified only
against a *copy* of the owner's tree. Everything below was run against **this committed
tree**, from the directories the block names, with exit codes read directly (mktemp pattern
where output is captured).

**Verdict change: `pass with changes` → `pass` (lesson 03). M0 closes.**

- **Should-fix 1 discharged, verified:** `docker/Dockerfile.build` is exactly six lines and
  record 04 §3's shape — one `RUN` (`dotnet build`), the suite as `CMD`, no `RUN dotnet
  test` layer. The old Phase 5 gate that forced the rejected shape is gone: within the
  acceptance block (lesson lines 497–660) there is **no quoted `'dotnet test'` needle at
  all** (`grep -cF "'dotnet test'"` = 0), and the honest record-shaped file passes every
  revised Phase 5 gate (below) — where the removed gate exits **1** on the same file
  (`grep -qF 'dotnet test' docker/Dockerfile.build` → exit 1), reconfirming the R1 defect
  was real.
- **Should-fix 2 discharged, verified:** the revised Phase 3 probes were run against the
  committed tree — both exit 0 on the honest stack — and I re-ran **both** of my original
  failure scenarios against them in a copy at `/tmp/opencode/rereview03` (deleted): each
  passes every old gate and each is now caught (tables below). The lesson-quality defect is
  fixed at the gate, not just documented.
- **28 of 28** command-checkable criteria in the revised acceptance block pass. The block
  changed shape (Phase 2's loopback line, Phase 3's two probes, Phase 5's two image probes,
  Phase 6's anchored greps), so this is a fresh table, not the old one re-quoted.
- New findings: two non-blocking **considers** (below), neither owner-blocking, neither a
  regression. No blocker, no should-fix.

## Commands re-run, verbatim key output

### From the repository root

**Phase 1 — the five files** — loop printed nothing (exit codes not judged, output is).

**Phase 2**

```
$ docker compose -f docker/compose.yml config >/dev/null            exit 0
$ docker compose -f docker/compose.yml config --services            exit 0
database
broker
$ grep -q '^name: shuttle-fleet$' docker/compose.yml                exit 0
$ for s in broker database; do … ; done                             (no output)
$ grep -q 'image: eclipse-mosquitto:2.0' …                          exit 0
$ grep -q 'image: postgres:17' …                                    exit 0
$ ! grep -q ':latest' …                                             exit 0
$ grep -q 'mosquitto.conf' …                                        exit 0
$ docker compose … config | grep -c 'host_ip: 127\.0\.0\.1'         prints 2
$ grep -Fq '${' …                                                   exit 0
$ for v in '${MQTT_HOST_PORT:-1883}' … five full strings …; done     (no output)
```

`--services` printed **database first** this run (the first review's run printed broker
first) — the order instability the revision flags is real on this machine, and the
name-judging wording is what makes the gate stable.

**Phase 3** (1883/5432 free — `ss -ltn` no listener, exit 1 — plain defaults again)

```
$ docker compose -f docker/compose.yml up -d                        exit 0
$ sleep 5
$ docker compose -f docker/compose.yml ps -q | wc -l                prints 2
$ docker run --rm --network shuttle-fleet_default eclipse-mosquitto:2.0 \
    mosquitto_pub -h broker -t acceptance/probe -m ok               exit 0
  (broker log, same beat: "New client connected … as auto-…")
$ docker run --rm --network shuttle-fleet_default postgres:17 \
    pg_isready -h database -U "${SHUTTLE_DB_USER:-shuttle}" \
    -d "${SHUTTLE_DB_NAME:-shuttle}"                                exit 0
  (database:5432 - accepting connections)
$ docker compose -f docker/compose.yml down                         exit 0
```

**Phase 4** — grep exit 1, prints nothing (judged by output, as the block says).

**Phase 5**

```
$ T=$(mktemp); docker build -f docker/Dockerfile.build -t shuttle-fleet-build . >"$T" 2>&1
build exit=0
$ grep -qF 'FROM mcr.microsoft.com/dotnet/sdk:10.0' docker/Dockerfile.build    exit 0
$ grep -qF 'dotnet build' docker/Dockerfile.build                              exit 0
$ docker run --rm shuttle-fleet-build sh -c 'test -f global.json'              exit 0
$ docker run --rm shuttle-fleet-build sh -c 'find src -name "*.dll" | grep -q .'  exit 0
$ U=$(mktemp); docker run --rm shuttle-fleet-build >"$U" 2>&1                  exit 0
$ grep -c 'Passed!' "$U"                                         prints 2
Passed!  - Failed: 0, Passed: 2, … Contracts.Tests.dll (net10.0)
Passed!  - Failed: 0, Passed: 1, … FleetManager.Tests.dll (net10.0)
```

**Phase 6** — three anchored present-greps and the three-word absent-loop: all printed
nothing. `src/`: `dotnet build` exit 0 (0 warnings, 0 errors), `dotnet test` exit 0,
`grep -c 'Passed!'` = 2, `grep -c 'No test is available'` = 0.

## Revised acceptance table (re-review)

| # | Criterion (revised lesson wording) | Evidence | Result |
|---|---|---|---|
| 1 | Phase 1: five files exist, loop prints nothing | printed nothing | met |
| 2 | `config` exit 0 | exit 0 | met |
| 3 | `--services` prints exactly `broker` and `database` (judge names, not order) | two lines, database/broker order this run, exit 0 | met |
| 4 | `grep -q '^name: shuttle-fleet$'` exit 0 | exit 0 | met |
| 5 | service loop prints nothing | printed nothing | met |
| 6 | `eclipse-mosquitto:2.0` pin grep exit 0 | exit 0 | met |
| 7 | `postgres:17` pin grep exit 0 | exit 0 | met |
| 8 | `! grep -q ':latest'` exit 0 | exit 0 | met |
| 9 | `mosquitto.conf` mount grep exit 0 | exit 0 | met |
| 10 | `config \| grep -c 'host_ip: 127\.0\.0\.1'` prints 2 | printed 2 | met |
| 11 | `grep -Fq '${'` exit 0 | exit 0 | met |
| 12 | five **full-string** `${VAR:-default}` loop prints nothing | printed nothing | met |
| 13 | `up -d` exit 0 | exit 0 | met |
| 14 | after `sleep 5`, count prints 2 | printed 2; `ps` shows both Up with loopback publishes | met |
| 15 | `mosquitto_pub` probe exit 0 | exit 0; broker log shows the client connect | met |
| 16 | `pg_isready` probe exit 0 | exit 0; `accepting connections` | met |
| 17 | `down` exit 0, nothing left | exit 0; `docker ps -a` only the pre-existing `hello-world` | met |
| 18 | Phase 4 grep prints nothing | printed nothing (exit 1) | met |
| 19 | `docker build … >"$T"` exit 0 | exit 0 | met |
| 20 | `FROM` grep exit 0 | exit 0 | met |
| 21 | `dotnet build` grep exit 0 | exit 0 | met |
| 22 | `test -f global.json` in-image probe exit 0 | exit 0 | met |
| 23 | compiled-DLL in-image probe exit 0 | exit 0 | met |
| 24 | `docker run --rm shuttle-fleet-build` exit 0 | exit 0 | met |
| 25 | `grep -c 'Passed!'` prints 2 | printed 2 (2 + 1 tests) | met |
| 26 | Phase 6: three anchored `.dockerignore` greps print nothing | printed nothing | met |
| 27 | Phase 6: absent-word loop prints nothing | printed nothing | met |
| 28 | from `src/`: `dotnet build` / `dotnet test` exit 0 | exit 0 / exit 0, `Passed!` count 2, no `No test is available` | met |

**28 of 28 met.** No criterion failed.

## The two failure-scenario re-runs (against the *new* gates, in a copy)

Copy at `/tmp/opencode/rereview03` (from `git archive HEAD`, deleted after), stack brought
up per scenario, probes run exactly as the block writes them.

| Scenario | Old gates | New gate that catches it | Measured |
|---|---|---|---|
| (a) wrong broker mount (`./mosquitto.conf:/mosquitto/wrong/mosquitto.conf:ro`) — the should-fix 2 case | `config` 0, mount-string grep 0, `up -d` 0, count prints **2** — full pass, and the broker log **still prints** `Opening ipv4 listen socket on port 1883` (so even a log-grep gate would have been fooled) | `mosquitto_pub` from a second container | **exit 1**, `Error: Connection refused` — caught |
| (b) postgres unreachable (`command: ["postgres", "-c", "listen_addresses="]`) | `config` 0, `up -d` 0, count prints **2**, log prints `database system is ready to accept connections` (twice) | `pg_isready -h database` from a second container | **exit 2**, `database:5432 - no response` — caught |

Both scenarios leave every *old* acceptance line green. The probes are the only gates in
the block that distinguish "process up" from "service answers" — exactly the hole
should-fix 2 named, closed.

## Mutation evidence: the revised gates can fail

All against the copy, all with the block's own commands (adapted only in image tag where a
mutated image would have collided with `shuttle-fleet-build`):

| Mutation | Gate | Result |
|---|---|---|
| `RUN dotnet build` line deleted from the Dockerfile | `docker build` | exit **0** (nothing to compile — the professor's claim reproduced), while `grep -qF 'dotnet build'` exits **1** and the DLL probe exits **1**; the `global.json` probe still exits 0, so each probe catches its own fact |
| `COPY global.json ./` → `COPY global.json /tmp/` | in-image `test -f global.json` | build exit 0, probe exit **1** — the pin-outside-`WORKDIR` break caught |
| broker port `"127.0.0.1:…"` → `"0.0.0.0:…"` | `config \| grep -c 'host_ip: 127\.0\.0\.1'` | prints **1**, not 2 — caught (and the no-IP variant fails `config` outright, printing 0) |
| `${MQTT_HOST_PORT:-1883}` → `${MQTT_HOST_PORT}` (no default) | five-full-string override loop | prints `missing override: ${MQTT_HOST_PORT:-1883}` — caught; this is the "name without default passed the old loop" measurement, reconfirmed |
| `.dockerignore` with `bin/` instead of `**/bin/`; `.github` instead of `.git` | anchored Phase 6 greps | both exit **1** — while the old substring greps (`grep -qF 'bin/'`, `grep -qF '.git'`) exit **0** on the same broken files — the old gates were vacuous, the new ones are not; the honest three-line file passes both anchored forms |
| prior review's M1b (broken test assertion, record-shaped Dockerfile) | `docker run` + `Passed!` count | already proven in the first review; the verdict still lives at `docker run`, unchanged by this revision |

The honest solution passes every gate (full block run, above); each realistic break I
constructed fails at least one. No vacuous-pass hole found in the new gates except the one
minor case below.

## Consistency checks

- **Decision 04 is append-only:** `git diff 12b9b37..HEAD -- docs/decisions/04-…` contains
  **no deleted lines** — §1–§7 byte-identical; §8's only touch above its own body is the
  trailing newline that lets text follow the old last line (content unchanged). The §8
  addendum's account of the old gate is a fair paraphrase (the removed gate grepped the
  literal `'dotnet test'` as one needle of a `for` list, lesson `12b9b37` line 549); its
  substance — the honest file fails that gate, the record rejects the shape that passes it
  — is re-verified above.
- **Record 04 §3 + §8 vs the Dockerfile:** exactly one `RUN`, and it is `dotnet build`;
  tests at run time via `CMD`; five lines total, no sixth. Consistent.
- **Records 02/03 vs `compose.yml`/`mosquitto.conf`:** image tags `eclipse-mosquitto:2.0`
  and `postgres:17`; `listener 1883` + `allow_anonymous true` (record 02 §4); loopback
  publishes; `SHUTTLE_DB_*` env wiring with defaults; no named volume (record 03 §4 defers
  persistence to M3). Unchanged since review 03, still consistent.
- **Hints vs lesson:** the shared gate lines (both probes, the count, the Dockerfile
  template) match verbatim, mechanically compared; the hints' Tier-3 Dockerfile template is
  byte-identical to the owner's file. The hints' Phase 2 peek block is a labelled subset,
  not a competing gate.
- **`src/` untouched:** `git diff 84633c6..HEAD -- src/` = 0 lines; no commit in range
  touches `src/`. Lesson-02's R1 fix still holds (scoped `Hello, World!` grep exits 1).
- **Hard-coding:** the probes' `shuttle-fleet_default` network name follows from the
  lesson-fixed `name: shuttle-fleet` (not machine configuration); `acceptance/probe` is
  check traffic the lesson owns; host ports and DB credentials remain overridable. No
  violation.
- **Commit messages (R3 class):** every commit in range carries the `M0:` prefix. See
  consider A.

## Findings (re-review)

### Blocker

None.

### Should-fix

None. The first review's two are discharged and verified above.

### Consider

**A — commit `78b9a6a`'s message describes one of the five files its diff contains
(review-02 consider-3 class, new instance).** The message says "Add review documentation…";
the diff also carries the owner's one-line Dockerfile fix, the professor's lesson and hints
revision, and the decision-04 addendum. Three actors' changes under one "review
documentation" message — the prefix is right, the description is not. Not a code defect;
flagged so the pattern gets named rather than normalized.

**B — the loopback count does not forbid a third publish (lesson vacuity, measured,
non-blocking).** `config | grep -c 'host_ip: 127\.0\.0\.1'` prints **2** on a compose copy
where the broker additionally publishes `0.0.0.0:8080:8080` — the gate checks "two
loopback publishes exist", not "only loopback publishes exist". This owner's file has
exactly the two fixed publishes (verified above), so nothing blocks; a one-line future
lesson tweak would close it (e.g. also assert `grep -c 'host_ip'` = 2, or count
`0\.0\.0\.0` = 0). For the professor.

**C — R2 and R4 stand unchanged** (`StartTests.cs` no timeout; 9-space indent at line 10;
no trailing newline): `src/` never moved, so no regression — the same considers review 02
left, still due before M2.

**D — the stretch healthcheck was not attempted** (optional; unchanged).

### Explain-it questions — NON-BLOCKING (review 01 waiver still in force)

Answer out loud before lesson 04:

1. Your probe line says `--network shuttle-fleet_default` and `-h broker`, but your host
   uses `127.0.0.1`. Where does the name `broker` resolve from, why does the probe container
   need that flag, and why can't the host just use `broker`?
2. Your Dockerfile has one `RUN` and one `CMD`. Say exactly what a green `docker build`
   promises and what a green `docker run` promises — then say which of the two a CI job
   would need for the suite's verdict, and why record 04 §3 refuses to merge them.
3. The wrong-mount broker prints `Opening ipv4 listen socket on port 1883` just like the
   healthy one (I measured both). What fact does `mosquitto_pub` from a second container
   establish that the log cannot — and what is the `pg_isready` equivalent for postgres?
4. Record 04 gained an §8 instead of an edit. Say what remains true above §8, why the old
   gate lost the argument even though it was in the lesson, and what a future record must
   do before the `RUN`/`CMD` split can change.
5. Consider B: the loopback count printed 2 while a third port sat on `0.0.0.0`. Say what
   property the gate checks, what property you wanted, and the one line you would add.

### Blocks to study

- The two probe lines in the lesson and hints — know why each image was chosen (the stack
  already pulled them; no host tool beyond `docker`).
- `docker/Dockerfile.build` — six lines; be able to map each to a record 04 §3 bullet and
  to say which gate fails if you delete it (I measured all six deletions' class above).
- The anchored `.dockerignore` greps — say what `^\*\*/bin/?$` protects that `bin/` did
  not, with the measured broken files.
- `git show 78b9a6a --stat` versus its message — consider A. Write the message it should
  have had (hint: it is really three commits).

## README

**Changed.** All flip conditions are met — lesson 01 passed; lesson 02's "pass with
changes" items discharged (R1 re-verified above; R2/R3/R4 were considers, no regression);
lesson 03 now passes clean; the spec's M0 "done when" (*clean build and tests pass in a
fresh container*) is proven end to end on the committed tree, including by mutation. The M0
row moves to `done` and the verification note below it records the exact commands.

## Cleanup

- Every `up` followed by `down`; the repo-context run's leftover postgres anonymous
  volumes (three, created 19:43:49Z during this session) removed; scratch-stack runs used
  `down -v`. The 16 anonymous volumes that predate this session (17:03–18:27 and the
  professor's 19:39 batch) were left untouched.
- `docker ps -a`: only the pre-existing 9-day-old `hello-world`, left as found.
- Mutation images `shuttle-fleet-build-mutc` / `-mutd` deleted; the copy at
  `/tmp/opencode/rereview03` and all scratch files deleted; `dotnet build-server shutdown`
  run; no `dotnet run`, test host, or MSBuild node left.
- `git status --porcelain` after this review: only this file and `README.md`.

## What is next

**M0 is closed.** Lesson 04 — M1, the warehouse model and one shuttle — is unblocked. The
owner carries into M1: R2 (a timeout around the child process in `StartTests.cs`) and R4
(cosmetic slips there), both small and both named three reviews running; the explain-it
questions above; and the habit this milestone earned — every gate you meet, run against the
failing version once, so you know which failure it catches.
