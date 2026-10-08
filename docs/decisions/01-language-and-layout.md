# Decision 01: Language and repository layout

**Date:** 2026-10-07
**Milestone:** M0 (Repo and tooling)
**Status:** Accepted. Owner still needs to install the .NET SDK and build M0.
**Supersedes:** none. This is the first decision record.
**Replaced by:** any later record that changes these choices must say so and link back here. Records are append-only.

---

## 1. What we are deciding

Two things:

1. The language for the fleet manager and the shuttle simulator.
2. The repository layout: the solution, the projects, and how they reference each other.

The spec says C#/.NET is the default and that decision record 01 confirms it.
This record does that check honestly.
We keep the default only if the reasons hold up.
We change it only for real reasons, not for fashion.

The dashboard (Vue, a JavaScript framework) is already fixed by the spec.
Python tooling is allowed by the spec.
Those are not re-decided here.

---

## 2. The current environment (verified facts)

The .NET SDK is **not installed** on this machine right now.
`dotnet --version` fails with "command not found" (exit 127).
The folder `~/.dotnet` only holds an old `corefx` folder. No SDK, no `dotnet` binary.

The pieces that are ready:

| Tool | Version | How checked |
|---|---|---|
| Docker | 29.8.1 (build 4a63305) | `docker --version` |
| Docker Compose | v5.5.1 | `docker compose version` |
| Python | 3.14.4 | `python3 --version` |
| pytest | 9.0.2 | `python3 -m pytest --version` |
| mypy | 1.19.1 | `python3 -m mypy --version` |
| NuGet feed | reachable (HTTP 200) | `curl -s -o /dev/null -w '%{http_code}' https://api.nuget.org/v3/index.json` |

Not ready: pip (no `pip` or `pip3` command, `python3 -m pip` says "No module named pip").
Not present: Node.js and npm.

So today, Python costs zero setup and .NET costs a setup step.
We picked .NET anyway. Section 4 says why.

---

## 3. Survey 1: the language

Three options. We compare them on this project's real needs.

### Option A: C# / .NET 10

The spec's default.
The owner's strongest statically typed language, per the screening answers.

### Option B: Python 3.14

Already installed. pytest and mypy already installed.
Python type hints exist, but nothing forces them at build time.
The contract would be checked at run time, not compile time.

### Option C: TypeScript / Node.js

Shares a language with the Vue dashboard.
But Node.js is not installed.
TypeScript types would help, but only after a new toolchain is set up.

### Comparison

| | C# / .NET 10 | Python 3.14 | TypeScript / Node |
|---|---|---|---|
| Setup today | SDK missing, owner installs | already installed | Node missing |
| Owner's stated strength | strong in C# | not stated | strong in JavaScript |
| Contract checked when? | at build time | at run time (mypy exists, not enforced by the build) | at build time, if we set up the TS toolchain |
| Test tooling | `dotnet test`, templates ship with the SDK | pytest 9.0.2 installed | nothing installed |
| Support horizon | .NET 10 LTS, supported to Nov 14 2028 | current release | Node LTS: not yet verified |

---

## 4. Why we chose C# / .NET 10

### Reason 1: the spec default and the owner's strength

The spec names C#/.NET as the default.
The owner's screening says C# is a strength.
So the learning budget goes to fleet software, not to a new language.
This project's hard part is fleet thinking, not syntax.

### Reason 2: the message contract gets enforced by the compiler

The message contract is the source of truth.
Two programs (fleet manager and shuttle simulator) and the tests must agree on every message shape.
In C#, a wrong field name or type stops the build.
In Python, it would only fail at run time, or when a separate checker runs.
Build-time agreement is worth real money in a fleet system.

### Reason 3: JSON support ships inside the framework

The contract is JSON.
`System.Text.Json` is built into the .NET shared framework since .NET Core 3.0.
So messages need no third-party package at all.
Verified in the official .NET docs (see the verification table).

### Reason 4: .NET 10 is the current LTS, .NET 8 and 9 end soon

Official support policy (fetched this session, page updated September 8 2026):

- .NET 10: LTS, released Nov 11 2025, supported to Nov 14 2028.
- .NET 9: STS, support ends Nov 10 2026.
- .NET 8: LTS, support ends Nov 10 2026.

Both 8 and 9 die within a month of this record.
Starting a new project on a version that ends next month is a bad lesson.
If we go .NET at all, we go .NET 10.
.NET 11 is not generally available yet (RC1 go-live, per the same policy page).

### Reason 5: a container build path exists and was verified

`docker manifest inspect mcr.microsoft.com/dotnet/sdk:10.0` succeeds.
So M0's "clean build in a fresh container" has a real image to use.
The image tags 8.0 and 9.0 also exist; we use the 10.x tag to match the SDK.

### The costs, stated plainly

- The SDK must be installed by the owner. This record cannot do it (repository rule).
- First `dotnet build` restores packages from nuget.org. The feed was reachable today (HTTP 200).
  A future offline machine would need a package cache.
- The owner is "comfortable-but-cautious" with the dotnet CLI.
  Lesson 01 will teach the exact commands, small steps.
- Nothing about .NET could be run in this session. No SDK.
  Every claim about behavior on this machine is taken from official docs and is marked "not yet verified" where it is.

---

## 5. Rejected languages and why

### Python — rejected

The strongest argument for Python is setup: it is already here.
But the contract is the center of this project.
Python would move every contract error to run time.
The owner's stated strengths are C# and JavaScript, not Python.
The spec's default stands.

Python is still allowed for tooling (scenario runner, log analyzer) per the spec.

### TypeScript / Node.js — rejected

The strongest argument is sharing a language with the dashboard.
But Node is not installed, and the backend would follow a second, newer toolchain.
The spec already splits the system: C# for the fleet side, JavaScript for the UI side.
We keep that split.

---

## 6. What would change this choice

- If the .NET SDK cannot be installed in the target build environment (for example, a locked-down container), Python becomes the pragmatic pick.
- If the owner decides the portfolio goal is a Python or Node stack, a new record supersedes this one.
- If .NET 10 support ends before this project is done (it ends Nov 14 2028), move to the then-current LTS in a new record.

---

## 7. Survey 2: the repository layout

First, the requirement: M0 asks for "a solution with a shared contracts project and test projects".
The layout must make `cd src && dotnet build && dotnet test` work.
Those commands are in AGENTS.md and README.md.

### Option A: one solution in `src/`, one folder per project

```
src/
  ShuttleFleet.slnx
  Contracts/
  FleetManager/
  Contracts.Tests/
  FleetManager.Tests/
```

Test projects sit next to the projects they test: `X` and `X.Tests`.

### Option B: one solution in `src/`, tests in a separate top-level `tests/` folder

Tests live outside `src/`.
The solution in `src/` would reference projects in `../tests`.

### Option C: two solutions, one per side

One solution for the fleet manager side.
One solution for the shuttle side.
Both reference `Contracts` by a shared path.

### Comparison

| | A: one solution, pairs | B: tests outside src | C: two solutions |
|---|---|---|---|
| Build and test commands | `dotnet build` and `dotnet test` in `src/`, as AGENTS.md writes them | commands still work, paths get ugly | two builds, two test runs |
| Where do tests live | next to the code they test | far from the code | next to the code |
| Contract agreement | enforced by one build | enforced by one build | only if the shared reference survives |
| Teaches | one clean dependency graph | layout gymnastics | cross-solution reference hacks |

---

## 8. Why we chose Option A

- One solution. One build. One test run. Simple to explain.
- Bare `dotnet build` and `dotnet test` search the current directory for a project or solution (official docs, verified).
  With exactly one solution in `src/`, the AGENTS.md commands work as written.
  (What happens with two solutions in one folder is unspecified; we never create that situation.)
- `X.Tests` next to `X` matches Microsoft's official unit-testing tutorial (verified).
  The owner can follow that tutorial and the layout lines up.
- The reference graph below is the only rule we need.
  It makes the contract rule (AGENTS.md) mechanical: apps can only see each other through `Contracts`.

---

## 9. The layout

```
src/
  ShuttleFleet.slnx        the one solution (created by `dotnet new sln`)
  Contracts/               the shared message contract, a class library
  FleetManager/            the fleet manager, a console app
  Contracts.Tests/         tests for Contracts (xUnit)
  FleetManager.Tests/      tests for FleetManager logic (xUnit)
  ShuttleSim/              the shuttle simulator, a console app   (added at M1)
  ShuttleSim.Tests/        tests for ShuttleSim (xUnit)           (added at M1)
```

Project references, one way only:

```
Contracts.Tests    -> Contracts
FleetManager.Tests -> FleetManager, Contracts
ShuttleSim.Tests   -> ShuttleSim,   Contracts     (at M1)
FleetManager       -> Contracts
ShuttleSim         -> Contracts                  (at M1)
```

Nothing references FleetManager or ShuttleSim except their own test projects.
FleetManager and ShuttleSim never reference each other.
That is the contract rule made physical.
A build failure is now the first line of defense against contract drift.

**Question: does the M1 state machine get its own project?**
It might. AGENTS.md separates decision logic from input and output.
At M0 there is no decision logic, so no project for it yet.
The rule: when the first pure logic lands (M1), it goes in a class library, not in the app project.
Which library, and its name, is decided by the record that introduces it.

---

## 10. Which projects are created at M0

M0 creates: the solution, `Contracts`, `FleetManager`, `Contracts.Tests`, `FleetManager.Tests`.

- `Contracts` holds the one real message type M0 must define and unit test.
- `FleetManager` is a skeleton console app at M0. It starts, logs, and exits cleanly.
  It proves the tooling runs end to end.
- Two test projects exist at M0, so "test projects" (plural, per the spec) is true.
  Each holds at least one real test of something real.
  Exactly which tests is lesson 01's assignment, not this record.

`ShuttleSim` and `ShuttleSim.Tests` arrive at M1, when there is a shuttle to simulate.

## 11. Test projects and the test framework

- Rule: one test project per source project. `X` gets `X.Tests`.
- Framework: xUnit, from the `xunit` template that ships with the SDK.

Grounds, all verified this session:

- `xunit`, `nunit`, and `mstest` are preinstalled SDK templates.
  So no template install step is needed for any of them.
- Microsoft's official "Unit testing C# with dotnet test and xUnit" tutorial uses `dotnet new xunit`.
  It shows the exact `X` / `X.Tests` layout this record chose.
  The owner can read official docs that match our layout.
- That tutorial says the xunit template wires in four NuGet packages:
  `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`.
  The exact list this machine's SDK generates is **not yet verified** (no SDK here yet).

Test runner: `dotnet test` uses VSTest by default (official docs, verified).
We do not opt into Microsoft.Testing.Platform (MTP) at M0.
There is no need to import that concept yet.

---

## 12. Version pinning and the container

- `global.json` sits at the repository root.
  The SDK searches from the current directory upward through ancestors (official docs, verified).
  So one root file governs commands run in `src/` and anywhere below.
- It pins the exact SDK the owner installs, for example `10.0.x`, with `rollForward: latestFeature`.
  Pin exactly what the owner installs; do not guess "10.0.12" blindly.
  (10.0.12 is the latest patch per the policy page, but the owner's install is not yet verified.)
- The M0 container build uses `mcr.microsoft.com/dotnet/sdk:10.0` (verified to exist).
  The image tag must match the pinned SDK major/minor.
  The Dockerfile itself is M0 work, not this record.

Solution file format:

- The .NET 10 SDK creates `.slnx` files by default (official docs, verified).
  We accept that default: `ShuttleFleet.slnx`.
- `dotnet build` and `dotnet sln add/list/remove` treat `.slnx` as first class (official docs, verified).
- Whether every editor we might use reads `.slnx` is **not yet verified**.
  Fallback if something chokes: re-create with `--format sln`.
  That flag exists since SDK 9.0.200 (official docs, verified). One command, one new file.

---

## 13. Verification table

Claims verified this session, and how:

| Claim | How checked | Result |
|---|---|---|
| No .NET SDK installed | `dotnet --version` | "command not found", exit 127 |
| `~/.dotnet` has no SDK | `ls -la ~/.dotnet` and `ls ~/.dotnet/sdk` | only `corefx/`; no `sdk/`, no `dotnet` binary |
| Docker 29.8.1 | `docker --version` | "Docker version 29.8.1, build 4a63305" |
| Compose v5.5.1 | `docker compose version` | "Docker Compose version v5.5.1" |
| SDK images exist | `docker manifest inspect mcr.microsoft.com/dotnet/sdk:{8.0,9.0,10.0}` | all exit 0 |
| NuGet feed reachable | `curl -w '%{http_code}' https://api.nuget.org/v3/index.json` | 200 |
| Python 3.14.4 | `python3 --version` | "Python 3.14.4" |
| pytest and mypy present | `python3 -m pytest --version`, `python3 -m mypy --version` | pytest 9.0.2, mypy 1.19.1 |
| No pip | `python3 -m pip --version`; `command -v pip pip3` | "No module named pip"; not found |
| Node absent | `command -v node npm` | not found |
| Templates ship with the SDK | official docs, `dotnet-new-sdk-templates` page | `sln`, `classlib`, `console`, `xunit`, `nunit`, `mstest`, `globaljson` listed |
| SDK 10 defaults to `.slnx` | official docs, `dotnet-new` and `dotnet-new-sdk-templates` pages | "Starting with .NET 10, the default format is slnx"; `--format` since 9.0.200 |
| `dotnet sln add/list/remove` | official docs, `dotnet-sln` page | commands exist; examples use `.slnx` |
| Bare `dotnet build` searches the current dir | official docs, `dotnet-build` page | "If a file isn't specified, MSBuild searches the current directory for a project or solution"; builds "all of its dependencies"; restore is implicit |
| `dotnet test` builds the solution and runs tests | official docs, `dotnet-test` page | stated; VSTest is the default runner |
| `System.Text.Json` built into the framework | official docs, `system-text-json` overview | "built-in as part of the shared framework for .NET Core 3.0 and later" |
| .NET 10 LTS, 8/9 end dates | official support policy page, fetched this session (updated Sept 8 2026) | 10: LTS to Nov 14 2028; 9: ends Nov 10 2026; 8: ends Nov 10 2026; .NET 11 RC1 is go-live only |
| xUnit tutorial layout and packages | official docs, "Unit testing C# in .NET using dotnet test and xUnit" | layout `X` + `X.Tests`; packages `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector` |
| `global.json` search behavior | official docs, `global-json` page | "searches up through ancestor directories" from the current directory |
| Owner's language strengths | screening answers given to the professor, 2026-10-07 | self-reported: strong in C#, JavaScript, backends |

Not yet verified (no SDK on this machine; do not treat as facts):

- The output of `dotnet --version` after the owner installs the SDK.
- The exact package list the installed SDK's `dotnet new xunit` generates.
- That the Docker build of this repo completes (no Dockerfile exists yet; it is M0 work).
- Whether every editor we might use reads `.slnx`.
- What `dotnet build` does if two solutions sit in one folder. We never create that.
- Behavior of the app under a real message broker. That is decision record 02 (MQTT) and M2 territory.

---

## 14. What this record unblocks

- Lesson 01 can teach the dotnet CLI with a target layout that is already decided.
- The M0 assignment has a fixed shape: solution, `Contracts`, `FleetManager`, two test projects, one real message type, Docker for broker and database.
- Message transport (MQTT vs. alternatives) is decision record 02.
- The database is decision record 03.
- This record's layout is the ground the reviewer checks M0 against: one solution, one build, tests that pass, nothing referencing an app except its tests.