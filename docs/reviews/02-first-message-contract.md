# Review 02: the first message contract (lesson 02)

**Date:** 2026-10-08
**Reviewed:** commit `bafe1bb` (`Implement ShuttleStatus message contract and associated tests;
remove unused files`), on top of `3f3017b` (revised lesson 02). Branch `master` in sync with
`origin/master`, working tree clean before I started (`git status --porcelain` empty, exit 0).
**Scope:** lesson 02 only. This is *not* an M0 milestone pass — see the end-product check.

**Verdict: not yet (fail for lesson 02).**

One command in the lesson's own acceptance block fails, and it fails for a real reason:
`dotnet test` exits **1**. AGENTS.md is explicit — *tests must pass before a review can
pass* — so nothing else below can lift the verdict. The contract page, the `ShuttleStatus`
type, and the two contract tests are all where they should be and are correct; the failure
is isolated to `FleetManager.Tests/StartTests.cs`.

---

## Commands run, verbatim key output

Exit codes read directly, never through a pipe, except where the lesson itself pipes
(`| wc -l`, and the `md5sum | sort | uniq` chain).

### From the repository root

```
$ DOC=docs/contract/v1.md
$ test -f "$DOC"                                exit 0
$ V=$(basename "$DOC" .md); V=${V#v}            V=1
$ grep -qE "^Version: ${V}\$" "$DOC"            exit 0
$ grep -q '```json' "$DOC"                      exit 0

$ grep -q '^## ShuttleStatus$' docs/contract/v1.md                 exit 0
$ test -f src/Contracts/ShuttleStatus.cs                           exit 0
$ grep -rq 'public record ShuttleStatus' src/Contracts --include='*.cs'   exit 0
$ test -f src/Contracts.Tests/ShuttleStatusTests.cs                exit 0
$ for f in ShuttleId State Position Timestamp FaultReason; do grep -q "$f" docs/contract/v1.md || echo "page missing: $f"; done
(no output)
$ for f in ShuttleId State Position Timestamp FaultReason; do grep -q "$f" src/Contracts.Tests/ShuttleStatusTests.cs 2>/dev/null || echo "test missing: $f"; done
(no output)
```

Placeholders gone:

```
$ find src -name 'Class1.cs' -o -name 'UnitTest1.cs'
(no output)
$ grep -rn 'Test1' src --include='*.cs'                              exit 1 (no match)
$ grep -rn 'Hello, World!' src --include='*.cs'                      exit 1 (no match)
$ for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do grep -q 'Assert' "$f" || echo "$f"; done
(no output)
```

Type and roundtrip where they belong:

```
$ find src/Contracts -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -not -name 'Class1.cs'
src/Contracts/ShuttleStatus.cs                                      exit 0
$ grep -rqE 'public[[:space:]]' src/Contracts --include='*.cs'      exit 0
$ grep -rq 'JsonSerializer' src --include='*.cs'                    exit 0
$ dotnet --version                                                  exit 0, printed 10.0.112
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
$ T=$(mktemp); dotnet test > "$T" 2>&1        exit 1        <-- FAILS
$ grep -c 'Passed!' "$T"                      1             <-- must be 2
$ grep -c 'No test is available' "$T"         0
```

Verbatim failure:

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 136 ms - Contracts.Tests.dll (net10.0)
[xUnit.net 00:00:00.73]     FleetManager.Tests.StartTests.FleetManager_Starts_Successfully [FAIL]
  Failed FleetManager.Tests.StartTests.FleetManager_Starts_Successfully [330 ms]
  Error Message:
   Assert.Equal() Failure: Values differ
Expected: 0
Actual:   1
  Stack Trace:
     at FleetManager.Tests.StartTests.FleetManager_Starts_Successfully() in /home/jamie/shuttle-sim/src/FleetManager.Tests/StartTests.cs:line 20

Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 364 ms - FleetManager.Tests.dll (net10.0)
```

Run a second time to prove it is deterministic, not a flake: **exit 1 again**, `Passed!` count
**1** again, same `[FAIL]` on `FleetManager_Starts_Successfully`.

```
$ L=$(mktemp); dotnet test --list-tests > "$L" 2>&1    exit 0
$ grep -c 'Test1$' "$L"                                0
    Contracts.Tests.ShuttleStatusTests.ShuttleStatus_survives_a_json_roundtrip
    Contracts.Tests.ShuttleStatusTests.ShuttleStatus_json_keeps_the_agreed_field_names
    FleetManager.Tests.StartTests.FleetManager_Starts_Successfully
```

Graph, from `src/` — unchanged from review 01, edge for edge:

```
$ grep -rn "ProjectReference" --include=*.csproj . | wc -l        4
./FleetManager/FleetManager.csproj:4:    <ProjectReference Include="..\Contracts\Contracts.csproj" />
./Contracts.Tests/Contracts.Tests.csproj:22:    <ProjectReference Include="..\Contracts\Contracts.csproj" />
./FleetManager.Tests/FleetManager.Tests.csproj:22:    <ProjectReference Include="..\FleetManager\FleetManager.csproj" />
./FleetManager.Tests/FleetManager.Tests.csproj:23:    <ProjectReference Include="..\Contracts\Contracts.csproj" />
```

```
$ find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -exec md5sum {} + | sort | uniq -w32 -D
(no output)
```

Skeleton runs:

```
$ F=$(mktemp); dotnet run --project FleetManager/FleetManager.csproj > "$F" 2>&1    exit 0
$ test -s "$F"                                                                       exit 0
$ ! grep -q 'Hello, World!' "$F"                                                     exit 0
Fleet Manager Coming Online
```

---

## Diagnosis of the failure (root cause, measured)

The child process in `StartTests` is started with

```csharp
Arguments = "run --project ../FleetManager",
```

a path relative to the **current working directory of the test host**. I measured what that
directory is, with a scratch probe in `/tmp/opencode` (deleted afterwards):

```
CWD=[/tmp/opencode/cwd-probe/App.Tests/bin/Debug/net10.0]
```

So `../FleetManager` resolves against the test **output** folder, not the test project
folder. Running the same command by hand from each candidate directory:

```
== from src/FleetManager.Tests (project dir) ==    exit 0   Fleet Manager Coming Online
== from src/FleetManager.Tests/bin/Debug/net10.0 == exit 1   The provided file path does not exist: ../FleetManager.
== from src ==                                      exit 1   The provided file path does not exist: ../FleetManager.
```

The test host's directory is the second one, so the child `dotnet run` exits **1** before the
fleet manager ever starts, and `Assert.Equal(0, process.ExitCode)` fails. Deterministic, not
environmental: two runs, two identical failures.

The lesson's own option-A snippet avoids exactly this by locating the **prebuilt** DLL
instead of asking for a path:

```csharp
var dll = Path.Combine(AppContext.BaseDirectory, "FleetManager.dll");
```

`AppContext.BaseDirectory` is where the test DLL lives, so it cannot depend on the directory
the runner happened to start in.

---

## Acceptance criteria (revised lesson 02)

| # | Criterion (lesson wording) | Evidence | Result |
|---|---|---|---|
| 1 | `test -f "$DOC"` exit 0 | exit 0 | met |
| 2 | version grep `^Version: 1$` exit 0 (content matches filename) | exit 0, `V=1` | met |
| 3 | ```` ```json ```` fence grep exit 0 | exit 0 | met |
| 4 | `^## ShuttleStatus$` section grep exit 0 | exit 0 | met |
| 5 | `src/Contracts/ShuttleStatus.cs` exists; `public record ShuttleStatus` grep exit 0 | both exit 0 | met |
| 6 | `src/Contracts.Tests/ShuttleStatusTests.cs` exists | exit 0 | met |
| 7 | page field loop (5 names) prints nothing | printed nothing | met |
| 8 | test field loop (5 names) prints nothing | printed nothing | met |
| 9 | `find` for `Class1.cs`/`UnitTest1.cs` prints nothing | printed nothing | met |
| 10 | `grep -rn 'Test1'` prints nothing | printed nothing (exit 1) | met |
| 11 | `grep -rn 'Hello, World!'` prints nothing | printed nothing (exit 1) | met |
| 12 | every `[Fact]` file contains an `Assert` | loop printed nothing | met |
| 13 | `find src/Contracts …` prints at least one path | `src/Contracts/ShuttleStatus.cs` | met |
| 14 | `public` grep exit 0 | exit 0 | met |
| 15 | `JsonSerializer` grep exit 0 | exit 0 | met |
| 16 | `dotnet build` exit 0 | exit 0, 0 warnings, 0 errors | met |
| 17 | `dotnet test` exit 0 | **exit 1** | **not met** |
| 18 | `grep -c 'Passed!'` prints 2 | **prints 1** | **not met** |
| 19 | `grep -c 'No test is available'` prints 0 | prints 0 | met |
| 20 | `dotnet test --list-tests` exit 0, `grep -c 'Test1$'` prints 0 | exit 0, prints 0 | met |
| 21 | exactly 4 `ProjectReference` lines | 4, identical to review 01 | met |
| 22 | duplicate-source `md5sum`/`uniq -D` check prints nothing | printed nothing | met |
| 23 | skeleton: `dotnet run` exit 0, output non-empty, no `Hello, World!` | 0 / 0 / 0, output `Fleet Manager Coming Online` | met |
| 24 | Requirement 7: graph unchanged, no new project/edge, no `.cs` copied | 4 edges, md5 check empty, `dotnet sln list` = the same four projects | met |

**Substance checks (beyond the greps)**

| Check | Evidence | Result |
|---|---|---|
| `ShuttleStatus` is `public record` in `src/Contracts/ShuttleStatus.cs` with exactly the specified fields and types (`string ShuttleId, string State, string Position, DateTime Timestamp, string? FaultReason`) | file read, matches the assignment table field for field and in order | met |
| Wire casing = C# property names, no naming policy, no attributes | no attributes in the file; mutation test below shows the names as asserted | met |
| Roundtrip test present in `ShuttleStatusTests.cs` | `ShuttleStatus_survives_a_json_roundtrip`, `Assert.Equal(sent, received)` | met |
| Wire-shape test present, asserts **all five** names | `ShuttleStatus_json_keeps_the_agreed_field_names`, five `Assert.Contains` | met |
| Tests are real (can fail) | mutation test in `/tmp/opencode` on a copy: renamed `Position` → `LanePos` in the **type only**, `dotnet test` → **exit 1**, `ShuttleStatus_json_keeps_the_agreed_field_names [FAIL]`, `Not found: ""Position""`, `Failed: 1, Passed: 1`. Roundtrip passed, wire-shape caught it — exactly the lesson's measured behaviour | met |
| `FleetManager.Tests/StartTests.cs` holds one real test (option A, child process) | one `[Fact]` with three `Assert`s, launches the app as a child process | met in kind, **fails in behaviour** (see issue 1) |
| Contract page ↔ type ↔ tests agree on names, casing, types | page table read against the record and the test file; additionally, a scratch program in `/tmp/opencode` extracted the page's JSON fence and deserialized it into the repo's `ShuttleStatus`: `DESERIALIZE -> ShuttleStatus { ShuttleId = SH-01, State = idle, Position = A02-L04, … FaultReason = }`, re-serializing to `{"ShuttleId":"SH-01","State":"idle","Position":"A02-L04","Timestamp":"2026-10-08T12:00:00Z","FaultReason":null}` | met |
| Graph unchanged / no `.cs` is a copy | four edges byte-identical to review 01; `uniq -D` empty | met |

### Lesson-quality check (my review of the lesson, not the owner's work)

- Three concepts declared — *contract as a versioned artifact*, *first real message type*,
  *a real unit test* — and all three have their own teaching section. Exactly at the cap, not
  over it. No lesson defect here.
- The decision content (contract form = markdown page; first message = `ShuttleStatus`;
  casing = property names) is argued with rejected options and reasons in the lesson, in the
  spirit AGENTS.md demands of a decision record, and the lesson explicitly states why no
  record is needed *yet* (nothing is being changed; `v1.md` was zero bytes). I buy that
  reasoning. If the first M2 edit touches `ShuttleStatus`, that is when a record and a bump
  are due.
- The message shape was **specified**, not left open — the revision fixed the field table.
  That was the right call: the owner's build shows no design churn.

---

## Findings

### Blocker

**1 — `dotnet test` exits 1: the FleetManager child process is launched from a
CWD-relative path, so the acceptance criteria fail outright.**
`src/FleetManager.Tests/StartTests.cs:13` — `Arguments = "run --project ../FleetManager"`.
Root cause and evidence in the diagnosis section above: the test host's working directory is
`bin/Debug/net10.0`, `../FleetManager` does not exist there, `dotnet run` exits 1, and the
first `Assert` fails. Two runs, identical result. Why it matters in this project's terms:
this is the M0 gate — "clean build and tests pass" — and a red suite means nothing can be
certified until it is green. Beyond the gate: a test whose result depends on which directory
the runner happened to start in is exactly the kind of check an operator-support engineer
learns to ignore, and ignored checks are how a stuck shuttle's failure goes unnoticed later.
Fix direction (your code, your call): either start the **prebuilt** DLL the way the lesson's
snippet does (`Path.Combine(AppContext.BaseDirectory, "FleetManager.dll")`, which is
CWD-independent by construction), or resolve the project path from a known anchor instead of
`..`. Note also that `dotnet run` *builds* the app from inside the test run — a nested MSBuild
inside the test phase — which the prebuilt-DLL form avoids entirely.

### Should-fix

**2 — the FleetManager test cannot fail on the regression it exists to guard.**
Two separate defects, both in `StartTests.cs:20-22`:

- The lesson's option A says assert *exit 0, output not empty, and **no `Hello, World!`***.
  The third assertion is missing. Reverting `Program.cs` to the template string would satisfy
  all three assertions the owner did write (exit 0, output non-empty, and see next point).
- `process.StandardOutput.ReadToEnd()` is called **twice**. The first call drains the stream
  to EOF; the second returns the empty string. Measured in a scratch probe:

  ```
  first  = [Hello, World!]  len=14
  second = []  len=0
  DoesNotContain("error", second) -> True
  ```

  So `Assert.DoesNotContain("error", …)` is vacuous — it passes on `""` no matter what the
  program printed.

Why it matters: this is the precise failure the whole lesson is built around — a green light
that proves nothing, which someone later trusts. A test that stays green when the template
string comes back gives false assurance about the one requirement it was written for.

**3 — `WaitForExit()` before draining the pipe, with stderr redirected and never read.**
`StartTests.cs:19`. The parent waits for the child to exit while the child's only output
channel is a pipe nobody is reading. Today the fleet manager prints one line, well under the
pipe buffer, so it does not bite. The moment the start output grows past the buffer — or
`FleetManager` writes anything to stderr, which is redirected at line 15 and never read — the
child blocks on a full pipe, the parent blocks in `WaitForExit()`, and the test **hangs**
instead of failing. A hung suite is worse than a failed one: it holds the whole run hostage
and gives no signal. The lesson's snippet reads output first, then `WaitForExit()`; follow
that ordering (or use `ReadToEndAsync`/an event).

**4 — typo in the source-of-truth page: `docs/contract/v1.md:15`.**
`` `… otherwise `null` or absent, e.g. `blocked path`4, `lost communication`, `pallet not
detected` `` — a stray `4` sits between `blocked path` and the comma. It fails none of the
greps (they check field names, not prose), which is exactly why it needs a human eye. AGENTS.md
makes this page the source of truth for everything between the fleet manager and the shuttles;
a garbled example in the first message's field table gets read, quoted, and copied into
`v2.md`.

### Consider

**5 — `using System.Text.Json;` in `src/Contracts/ShuttleStatus.cs:1` is unused.**
The build shows 0 warnings (unused usings are not build warnings without
`EnforceCodeStyleInBuild`), so nothing catches it. The point is architectural rather than
cosmetic: the contract type should be plain data. The moment `Contracts` imports the
serializer, the contract assembly starts owning a serialization policy, and at M2 that policy
(camera naming, timestamps, null handling) becomes a decision record with a version bump.
Keep the serializer at the ends (the transport and the tests), not in the middle.

**6 — commit messages do not name the milestone.**
AGENTS.md asks for messages like `M0: …`. The two commits under review (`3f3017b`,
`bafe1bb`) do not carry the prefix, unlike the lesson-01 commits. Trivial to fix going
forward; it is how a reviewer finds the milestone in `git log` without opening the diff.

### AGENTS.md compliance (checked)

- **Hard-coded names/topics/timeouts/retry counts/sizes:** `grep -rniE 'mqtt|topic|timeout|
  retry|broker|localhost|tcp://|:1883' src` → no matches (exit 1). Nothing to configure yet.
  Judged carefully, as instructed: the strings `"idle"`, `"fault"` etc. that appear in the
  page, the record's `State` column, and the test payloads are **message data** — the spec's
  own M1 state values carried on the wire — not configuration. They are not a violation.
  The `"dotnet"` filename and `"../FleetManager"` argument in `StartTests` are test
  scaffolding of the same kind the lesson's own snippet hard-codes (`FleetManager.dll`), so I
  am not scoring them as a config violation; their problem is issue 1's CWD fragility, not
  hard-coding.
- **Forbidden reference edges:** none. Four edges, all four in record 01 §9; no edge points at
  an app except its own test project; the two apps still do not reference each other; no
  `ShuttleSim` edges.
- **Shuttles talk only through the contract:** not yet at stake — there is no shuttle project
  and no transport. Nothing bypasses `Contracts`.
- **Decision logic purity:** not yet at stake; no assignment, traffic, or state-machine logic
  exists until M1. The record in `Contracts` is data, not decision logic.
- **Nothing in `src/` written by an agent:** I read, built, tested, and grepped only. My
  scratch experiments lived in `/tmp/opencode` and are deleted. `git status --porcelain` is
  empty after my builds — the `.gitignore` fix from review 01 issue S1 works, and my builds
  dirty no tracked file.
- **Processes:** no broker or container running (`docker ps -q` → 0 containers), no
  `dotnet run` left behind; I shut down the MSBuild node processes my builds started.
  `/tmp/opencode` is empty.

---

## What is solid

- The contract is genuinely three artifacts that agree: page, type, and tests. I did not just
  grep them — I fed the page's own example payload into the compiled `ShuttleStatus` and it
  deserialized and re-serialized to the documented wire shape.
- Both contract tests are real and behave exactly as the lesson claims: the mutation test
  turned the wire-shape test red while the roundtrip stayed green, in this owner's code, not
  the lesson's toy.
- The graph did not move — four edges, byte-identical to review 01, and the duplicate-source
  check still prints nothing.
- All five placeholders are gone (`Class1.cs`, both `UnitTest1.cs`, `Test1`, `Hello, World!`),
  and `--list-tests` prints three named tests that say what they prove.
- The skeleton does what record 01 §10 asked: prints a start line, exits 0, no template string.

---

## Explain-it questions — NON-BLOCKING

**The owner waived explain-it re-answers as a gate in review 01 (waiver recorded there,
2026-10-07). Nothing below blocks this verdict or any later lesson.** Listed for practice
and for the reviewer to re-ask cold at a later milestone, per the waiver's own terms.

1. Your tech lead says "commands are what the fleet manager sends — M0 should have defined
   the command message first, not a status." Why did this milestone define `ShuttleStatus`
   instead? Name the two things M2's command lesson needs that a status message does not,
   and the one field this message gets from M1's own spec.
2. `System.Text.Json` is serializing with default options, so the wire names are `ShuttleId`,
   `State`, … in PascalCase — while VDA 5050, the published AGV standard, uses camelCase. Why
   is our wire casing what it is, what would it take to change it, and who would break?
3. You rename `Position` to `LanePos` in the record. The roundtrip test still passes. Why
   does it pass, which test catches the rename, and why can't you ship with only one of the
   two?

### Blocks to study (unclear, fragile, or copied-looking)

- `src/FleetManager.Tests/StartTests.cs:10-23` — the whole test. Four separate things to be
  able to explain out loud: the CWD-relative `../FleetManager`, why `WaitForExit()` comes
  before the reads, why the second `ReadToEnd()` returns `""`, and why
  `DoesNotContain("error", …)` is not the assertion the lesson asked for.
- `src/Contracts/ShuttleStatus.cs:1` — the unused `using System.Text.Json;`. Say why the
  contract type does not need it.
- `docs/contract/v1.md:15` — the stray `4`. Find it by eye; no command in the acceptance
  block can.
- `src/FleetManager/Program.cs` — `return 0;` after `Console.WriteLine`. Say what "logs"
  means at M0 versus at M5, and where the real logging will live.
- The `| wc -l` and `md5sum | sort | uniq -w32 -D` pipelines in the acceptance block — say
  why those two are allowed to be piped while every exit code is read directly.

---

## Revisit list

- **Working-directory independence of a test that starts another process.** Where the CWD of
  a test host comes from, and why `AppContext.BaseDirectory` is the anchor that does not move.
- **Pipe discipline for child processes.** Read the output, then wait; redirect only what you
  drain; what a full pipe buffer does to a `WaitForExit()`.
- **Vacuous assertions.** A second read of a consumed stream; more generally, the three
  properties that make a test real (asserts / can fail / says what it proves) applied to an
  assertion you did not measure.
- **The three-artifact contract loop** — page describes, type enforces, test proves — and
  what the version bump costs when any one of the three changes.
- **When a decision record is and is not required.** The lesson argues the first content of
  `v1.md` needs no record but the first *change* does; be able to draw that line, because M2
  will test it.
- Next up: lesson 03, Docker services and the fresh-container build — the last piece of M0.

---

## End-product check

This is the first real message of the fleet's contract, and it is shaped right for what
follows: `ShuttleStatus` is consumed by M1's shuttle, M2's round trip, M3's fleet-state
tracking, M5's heartbeats (`Timestamp`) and alarms (`FaultReason`), and M7's dashboard, so
nothing here looks like something to throw away. The page/type/test triangle is exactly the
mechanism that keeps a two-program agreement honest once MQTT makes the link physical at M2,
and the versioning rule on the page is what will hold when the contract grows commands,
alarms, and recovery. The risk this piece introduces is concentrated in one place: the
`FleetManager.Tests` child-process check is currently both **failing** and, once fixed by
path alone, **still incapable of catching the regression it was written for**. If it ships in
that state, M0's "tests pass" evidence includes a test that means nothing — the same defect
review 01 raised about placeholders, one level up. Fix it and M0's code-and-tests half is
genuinely done; **M0 itself stays open** regardless, because the spec's M0 "done when" also
requires the Docker setup for broker and database and a clean build **in a fresh container**,
both of which are lesson 03 and neither of which exists yet (`docker/` is still empty).

---

## README

**Not changed.** The M0 row stays `not started`. The spec's M0 "done when" — clean build and
tests pass **in a fresh container** — is not met: no Dockerfile, no `docker/compose.yml`, no
container build has been run. This review also does not pass lesson 02, so there are two
independent reasons not to touch the table. No verification note added; nothing reached
`done`.

---

## What I changed in this repository

- **Added:** `docs/reviews/02-first-message-contract.md` (this file). Uncommitted — I commit
  only when asked.
- **README.md:** not touched.
- **`src/`:** not touched. My builds left the tree clean (`git status --porcelain` empty
  before I started and after every build); scratch experiments ran in `/tmp/opencode` and were
  deleted; no broker, container, `dotnet run`, or MSBuild node process left running.
