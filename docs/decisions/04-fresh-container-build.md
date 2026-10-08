# Decision 04: The fresh-container build

**Date:** 2026-10-08
**Milestone:** M0 (Repo and tooling) — how the "clean build and all tests pass in a fresh container" requirement is delivered as a repeatable command.
**Status:** Accepted. Written for lesson 03, which assigns the Dockerfile.
**Supersedes:** none. Record 01, section 12 (naming and environment) already fixed the base image and said "the Dockerfile itself is M0 work"; this record is where that work's *shape* is logged.
**Replaced by:** a later record if the build moves to a runtime image, CI, or multi-stage build at a later milestone — any such record links back here. Records are append-only.

---

## 1. What we are deciding

M0's "done when" says the build must pass **in a fresh container** — meaning: no host `bin/`, `obj/`, NuGet cache, or installed-SDK luck can be responsible for a green suite. Everything must come from the container, starting from nothing. This record decides:

1. where the Dockerfile lives and what its build context is,
2. what runs at image-build time versus at run time,
3. what the `.dockerignore` excludes, and
4. the name under which the resulting image is tagged.

What this record does **not** decide: shipping the fleet manager as a *runtime* image. There is nothing to run outside the build until M2's first round trip, so a runtime image gets its own record then.

---

## 2. Survey: where the build lives

| | A: `docker/Dockerfile.build`, context = repo root (chosen) | B: root `Dockerfile`, context = repo root | C: a `build` service inside `docker/compose.yml` |
|---|---|---|---|
| AGENTS.md's "Where things go" | `docker/` is exactly "broker, database, and reproducible environment" | the root already holds spec/docs/src; a generic root Dockerfile has no home | compose is declared for the *services*; a one-shot build is not a running service |
| Fresh-container guarantee | build runs with a root context (`COPY global.json ./`, `COPY src/ ./src/`) | same | same, if guarded |
| Command to reproduce | one `docker build` with an explicit file | one `docker build` | one `docker compose run --build build` — needs a profile to stay out of `up -d`, i.e. a concept for zero benefit |
| Milestone trunking | M1/M3/CI reuse the same file | unclear home | tied into the compose lifecycle in a way that leaks |

---

## 3. Decision: `docker/Dockerfile.build`, context = repository root

The file lives in `docker/` (per AGENTS.md), but its **build context is the repository root**, so that `docker build -f docker/Dockerfile.build -t shuttle-fleet-build .` works from the repo root and the Dockerfile can copy `global.json` and `src/` — and only what `.dockerignore` lets through.

Line-level facts the lesson fixes (the owner writes the file, the record fixes the shape):

- Base image `mcr.microsoft.com/dotnet/sdk:10.0` — decided in record 01, section 12.
- `global.json` and `src/` are copied into the image *before* any `RUN` — the version pin the host relies on must be the very thing the container builds with. (Measured this session: the SDK inside `sdk:10.0` is 10.0.401, which satisfies the repo's pinned 10.0.112 with `rollForward: latestFeature` — the same guarantee the host has.)
- `dotnet build` runs **at image-build time** (`RUN`), so a failing compile fails the `docker build` itself.
- `dotnet test` is the image's **default command** (`CMD`), so `docker run --rm shuttle-fleet-build` reads as "run the suite in the fresh container" and its exit code is the suite's.

Why tests at `docker run` and not a second `RUN` layer: the reviewer's command should *be the product's own definition of done* — `docker build` answers "does it compile clean", `docker run` answers "do all tests pass", and both exit codes are visible without losing the intermediate image. Baking the test run into the image would bury the suite's verdict in cache internals.

## Decision: `.dockerignore` at the repository root

The context includes the root `.dockerignore`. It must exclude the two things that would make a "fresh" build actually stale: `.git` and the host's `**/bin/` and `**/obj/` directories. Everything the container needs is `global.json`, `src/`, and — because lesson 02's stretch test reads the contract page from the build output — `docs/`. The `.dockerignore` must therefore not exclude `src/`, `docs/`, or `global.json`. (Verified with a poison-file test: a `src/Contracts/bin/POISON.txt` on the host did not appear in the image.)

## Decision: image tag `shuttle-fleet-build`

A fixed, recognizable tag (floats, not per-milestone): `docker run --rm shuttle-fleet-build` must work for a reviewer who has just built it. The tag is the M0 build image; if CI ever needs to pin a specific build, that is CI's record.

---

## 4. Rejected shapes

- **Root `Dockerfile`** — rejected: AGENTS.md permanently assigns `docker/` to "message broker, database, and reproducible environment"; the build file belongs there.
- **Compose `build` service** — rejected: compose exists to run the project's *services*. A one-off compile+test inside `compose up` (or `run`) glues source verification into the infrastructure lifecycle and needs a profile to prevent it appearing under `up -d`. The M0 mental model is cleaner split — composers run things, the Dockerfile verifies things.
- **Devcontainer as the guarantee** — rejected: a devcontainer bind-mounts your working tree into a container; that is not a *fresh* build, it is the host tree with container SDKs. Useful later, different promise.

---

## 5. Verification table

Experiments ran in `/tmp/opencode/lesson03-scratch` against a `git archive HEAD` snapshot of this repo, and the images were deleted afterwards.

| Claim | How checked | Result |
|---|---|---|
| `mcr.microsoft.com/dotnet/sdk:10.0` exists | `docker manifest inspect` | exit 0 |
| The repo builds in a fresh container | `docker build -f docker/Dockerfile.build -t … .` on the reference shape | exit 0, "Build succeeded. 0 Warning(s) 0 Error(s)" |
| The suite passes in the fresh container | `docker run --rm shuttle-fleet-build` | exit 0; two "Passed!" lines — `Passed: 2` (Contracts.Tests) and `Passed: 1` (FleetManager.Tests) |
| The container's SDK satisfies the repo's `global.json` (10.0.112, rollForward latestFeature) | `docker run --rm … dotnet --version` | `10.0.401`, so `latestFeature` resolves it |
| `.dockerignore` keeps host build output out | dropped `src/Contracts/bin/POISON.txt` on the host, built | file absent from the image (`docker run --rm … sh -c 'test ! -e src/Contracts/bin/POISON.txt'` exit 0); the `bin/` that exists inside is the container's own fresh output |

Not yet verified:

- A runtime image for the fleet manager — not due until something is worth running (M2 or later); a later record links here.
- CI wiring of this build — later milestone, own record.
- Anything about the owner's actual Dockerfile — that is the assignment, and the reviewer measures it.

---

## 6. What would change this choice

- If the fleet manager becomes something to *deploy* (M2 round trip, then M7) → a runtime image and possibly a multi-stage Dockerfile arrive; new record links here and version-floats `shuttle-fleet-build` accordingly.
- If CI lands → the record that adds CI may pin build images by digest and link here.

---

## 7. What this record unblocks

- Lesson 03's assignment can hand the owner a Dockerfile *shape* with every fact that matters pinned (base image, copies, run-vs-run-command split, ignore file) — while leaving the file itself to the owner.
- Reviewer acceptance for the fresh build is a one-liner pair with honest exit codes.
- Record 01, section 12's "the Dockerfile itself is M0 work" promise now has its shape logged.