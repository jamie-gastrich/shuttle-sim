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

---
---

# Re-review — 2026-10-08: the fixes, re-verified

**Appended** on 2026-10-08. The fail verdict and every line above this one stand as written;
nothing above was edited. This section is the record of the owner's fixes and the outcome.

**Re-verified at:** commit `e18a8af` (`M0: Remove unnecessary using directives …`), branch
`master`, `git status --porcelain` empty (exit 0) before I started. Since `bafe1bb` (the
commit under review) exactly four files moved: `docs/contract/v1.md` (stray `4` removed),
`src/Contracts/ShuttleStatus.cs` (unused using removed), `src/FleetManager.Tests/StartTests.cs`
(the rewrite), and this review file. No lesson or decision file changed, so the lesson-quality
check above still stands as written — the one new defect below (R1) is in the lesson's
acceptance block, which I re-ran rather than re-read.

**Verdict change: `not yet` → `pass with changes` (lesson 02).**

- The blocker (issue 1) is cleared: `dotnet build` exit **0**, `dotnet test` exit **0**,
  `grep -c 'Passed!'` = **2**, `grep -c 'No test is available'` = **0**. Two runs, identical.
- Should-fix 2, 3 and 4 are cleared, each measured below. Consider 5 is cleared; consider 6 is
  only partly cleared.
- One acceptance line still prints output — **the lesson's own grep against the lesson's own
  snippet** — issue R1. That is a lesson defect, not a code defect: the owner's `StartTests.cs`
  is the hints' Tier-3 option-A shape in substance. **No `src/` change is requested**, and
  gaming the grep would be worse than the mismatch. Hence *pass with changes*, not *pass*.

## Commands re-run, verbatim key output

Exit codes read directly, never through a pipe (the lesson's own `mktemp` pattern wherever
output is captured).

### From the repository root

```
$ DOC=docs/contract/v1.md
$ test -f "$DOC"                                              exit 0
$ V=$(basename "$DOC" .md); V=${V#v}                          V=1
$ grep -qE "^Version: ${V}\$" "$DOC"                          exit 0
$ grep -q '```json' "$DOC"                                    exit 0
$ grep -q '^## ShuttleStatus$' docs/contract/v1.md            exit 0
$ test -f src/Contracts/ShuttleStatus.cs                      exit 0
$ grep -rq 'public record ShuttleStatus' src/Contracts --include='*.cs'   exit 0
$ test -f src/Contracts.Tests/ShuttleStatusTests.cs           exit 0
$ for f in ShuttleId State Position Timestamp FaultReason; do grep -q "$f" docs/contract/v1.md || echo "page missing: $f"; done
(no output)
$ for f in ShuttleId State Position Timestamp FaultReason; do grep -q "$f" src/Contracts.Tests/ShuttleStatusTests.cs 2>/dev/null || echo "test missing: $f"; done
(no output)
```

Placeholders:

```
$ find src -name 'Class1.cs' -o -name 'UnitTest1.cs'          (no output)
$ grep -rn 'Test1' src --include='*.cs'                       exit 1 (no match)
$ grep -rn 'Hello, World!' src --include='*.cs'               exit 0 — PRINTS ONE LINE, see R1
src/FleetManager.Tests/StartTests.cs:25:        Assert.DoesNotContain("Hello, World!", output);
$ for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do grep -q 'Assert' "$f" || echo "$f"; done
(no output)
$ grep -n 'path`4' docs/contract/v1.md                        exit 1 — stray `4` gone
```

Type, roundtrip, environment:

```
$ find src/Contracts -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -not -name 'Class1.cs'
src/Contracts/ShuttleStatus.cs                                exit 0
$ grep -rqE 'public[[:space:]]' src/Contracts --include='*.cs'   exit 0
$ grep -rq 'JsonSerializer' src --include='*.cs'                 exit 0
$ dotnet --version                                            10.0.112, exit 0
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
$ T=$(mktemp); dotnet test > "$T" 2>&1                       exit 0        <-- was 1
$ grep -c 'Passed!' "$T"                                      2             <-- was 1
$ grep -c 'No test is available' "$T"                         0
$ grep -c '\[FAIL\]' "$T"                                     0
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 125 ms - Contracts.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration:  65 ms - FleetManager.Tests.dll (net10.0)
```

Second run of the same command, to prove it is not a flake: exit **0**, `Passed!` count **2**
again —

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 113 ms - Contracts.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration:  56 ms - FleetManager.Tests.dll (net10.0)
```

A third run from a different working directory, below, gave the same.

```
$ L=$(mktemp); dotnet test --list-tests > "$L" 2>&1          exit 0
$ grep -c 'Test1$' "$L"                                       0
    Contracts.Tests.ShuttleStatusTests.ShuttleStatus_survives_a_json_roundtrip
    Contracts.Tests.ShuttleStatusTests.ShuttleStatus_json_keeps_the_agreed_field_names
    FleetManager.Tests.StartTests.FleetManager_Starts_Successfully
```

Graph and duplicates:

```
$ grep -rn "ProjectReference" --include=*.csproj . | wc -l    4
./FleetManager/FleetManager.csproj:4:    <ProjectReference Include="..\Contracts\Contracts.csproj" />
./Contracts.Tests/Contracts.Tests.csproj:22:    <ProjectReference Include="..\Contracts\Contracts.csproj" />
./FleetManager.Tests/FleetManager.Tests.csproj:22:    <ProjectReference Include="..\FleetManager\FleetManager.csproj" />
./FleetManager.Tests/FleetManager.Tests.csproj:23:    <ProjectReference Include="..\Contracts\Contracts.csproj" />
$ find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -exec md5sum {} + | sort | uniq -w32 -D
(no output)
$ dotnet sln list      Contracts.Tests, Contracts, FleetManager.Tests, FleetManager — the same four
```

Skeleton:

```
$ F=$(mktemp); dotnet run --project FleetManager/FleetManager.csproj > "$F" 2>&1   exit 0
$ test -s "$F"                                                                     exit 0
$ ! grep -q 'Hello, World!' "$F"                                                   exit 0
Fleet Manager Coming Online
```

AGENTS.md compliance greps:

```
$ grep -rniE 'mqtt|topic|timeout|retry|broker|localhost|tcp://|:1883' src --include='*.cs'
exit 1 (no matches)
$ git status --porcelain                                       (no output) after every build
```

### CWD independence — issue 1's root cause, re-measured

A probe test in a throwaway **copy** of `src/` under `/tmp/opencode` (deleted afterwards),
reporting the test host's own directories, with the invoker's CWD one level above `src`:

```
invoker CWD => /tmp/opencode/mut02
CWD=[/tmp/opencode/mut02/src/FleetManager.Tests/bin/Debug/net10.0]
BASE=[/tmp/opencode/mut02/src/FleetManager.Tests/bin/Debug/net10.0/]
```

The test-host CWD is still the bin directory — the exact directory that killed the old
`../FleetManager` path. `AppContext.BaseDirectory` resolves to that same directory as an
**absolute** path, so the child's DLL location cannot depend on where the run started.

Behavioural confirmation — the real, unmodified suite invoked from `/tmp`:

```
$ cd /tmp/opencode/rerun02
$ dotnet test /home/jamie/shuttle-sim/src/ShuttleFleet.slnx        exit 0
Passed!  - Failed:     0, Passed:     2, … Contracts.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     1, … FleetManager.Tests.dll (net10.0)
```

`FleetManager_Starts_Successfully` passed from `src/` and from `/tmp` (three green runs of
the suite across this session). **Fixed.**

## Re-verification of every item flagged in review 02

| # | Item | Status | Evidence |
|---|---|---|---|
| 1 (blocker) | CWD-relative `run --project ../FleetManager` → `dotnet test` exit 1 | **fixed** | `dotnet test` exit 0 from `src/` and from `/tmp`; probe above shows the bin-dir CWD and the absolute `BaseDirectory` anchor |
| 2 (should-fix) | third assertion missing; a second `ReadToEnd()` made `DoesNotContain` vacuous | **fixed** | one `ReadToEnd()` into `output`; `Assert.DoesNotContain("Hello, World!", output)` present; mutations A and B below fail independently |
| 3 (should-fix) | `WaitForExit()` before draining; stderr redirected and never read → latent hang | **fixed** | read-then-wait, matching the lesson snippet; `RedirectStandardError` is no longer set at all (the old code's unread `RedirectStandardError = true` is visible in `git diff bafe1bb HEAD`), so no unread pipe exists; mutation C pushes 343,890 B of stdout and 293,890 B of stderr through both channels with no hang |
| 4 (should-fix) | stray `4` in `docs/contract/v1.md:15` | **fixed** | removed in `3c81a3b`; ``grep -n 'path`4'`` exits 1 |
| 5 (consider) | unused `using System.Text.Json;` in `ShuttleStatus.cs` | **fixed** | removed in `e18a8af`; `git diff bafe1bb HEAD -- src/Contracts/ShuttleStatus.cs` is exactly those two lines; the contract type is data only |
| 6 (consider) | commit messages missing the milestone prefix | **partly** | `3c81a3b` and `e18a8af` carry `M0:`; `65d770a` does not; and `e18a8af`'s message contradicts its own diff (R3) |

### The three questions you asked me to classify, answered with measurements

All mutations below ran against a **copy** of `src/` in `/tmp/opencode` (deleted); the repo's
`src/` was never edited, and every run compiled the owner's `StartTests.cs` unchanged.

**A — are the assertions able to fail? Yes, each on its own input.**

```
Program.cs := Console.WriteLine("Hello, World!"); return 0;
dotnet test => exit 1
Assert.DoesNotContain() Failure: Sub-string found
        ↓ (pos 0)
String: "Hello, World!\n"                     <-- read from a live pipe, not ""

Program.cs := return 0;                       (prints nothing)
dotnet test => exit 1
Assert.NotEmpty() Failure: Collection was empty
  at FleetManager.Tests.StartTests.FleetManager_Starts_Successfully() ... StartTests.cs:line 24
```

`Assert.NotEmpty(output)` is not the vacuous check the old double-read produced: empty output
turns it red, and non-empty output is proven by mutation A printing
`String: "Hello, World!\n"`. The assertions can fail.

**B — imperative-pipe caution: FIXED.** Not "still a should-fix", not "still a consider".
Three measured facts:

1. The order is now `ReadToEnd()` **then** `WaitForExit()` — the lesson snippet's order. The
   read blocks until the child closes stdout (the child closes it at exit), so the pipe is
   drained as it fills and can never jam.
2. `RedirectStandardError` is gone entirely. The old code redirected stderr and never read
   it; stderr now inherits the test host's stream, so there is no second pipe at all.
3. Volume test — `Program.cs` writing 5000 stdout lines **and** 5000 stderr lines: 343,890 B
   and 293,890 B, i.e. **5.2× and 4.5× the 64 KB pipe buffer**: `dotnet test` **exit 0**,
   `Passed: 1`, `Duration: 110 ms`. The single-line-buffer case that made this a should-fix
   cannot occur.

**C — `WaitForExit`/`ReadToEnd` ordering: correct.** Read first, wait second. The old
wait-then-read-twice code is gone in the same diff (`git diff bafe1bb HEAD`).

**Residual, reclassified as a consider (R2): no timeout anywhere.** A child that never exits
hangs the run instead of failing it:

```
Program.cs := Thread.Sleep(Timeout.Infinite);
timeout 25 dotnet test ... => exit 124      (my kill, not the suite, ended it)
```

That is **not** issue 3 surviving — issue 3 was a pipe deadlock at larger output, and mutation
C disproves it. This is the ordinary "how long do you wait" question, harmless at M0 with a
two-line program, real the day the child does more.

## Acceptance criteria (re-run in full)

| # | Criterion (lesson wording) | Evidence | Result |
|---|---|---|---|
| 1 | `test -f "$DOC"` exit 0 | exit 0 | met |
| 2 | version grep `^Version: 1$` exit 0 | exit 0, `V=1` | met |
| 3 | ```` ```json ```` fence grep exit 0 | exit 0 | met |
| 4 | `^## ShuttleStatus$` section grep exit 0 | exit 0 | met |
| 5 | type file exists; `public record ShuttleStatus` grep exit 0 | both exit 0 | met |
| 6 | `ShuttleStatusTests.cs` exists | exit 0 | met |
| 7 | page field loop prints nothing | printed nothing | met |
| 8 | test field loop prints nothing | printed nothing | met |
| 9 | `find` for `Class1.cs`/`UnitTest1.cs` prints nothing | printed nothing | met |
| 10 | `grep -rn 'Test1'` prints nothing | printed nothing (exit 1) | met |
| 11 | `grep -rn 'Hello, World!'` prints nothing | **prints one line, exit 0** — the line is the lesson's own assertion | **not met — lesson defect, R1** |
| 12 | every `[Fact]` file contains an `Assert` | loop printed nothing | met |
| 13 | `find src/Contracts …` prints at least one path | `src/Contracts/ShuttleStatus.cs` | met |
| 14 | `public` grep exit 0 | exit 0 | met |
| 15 | `JsonSerializer` grep exit 0 | exit 0 | met |
| 16 | `dotnet build` exit 0 | exit 0, 0 warnings, 0 errors | met |
| 17 | `dotnet test` exit 0 | **exit 0** (was 1) | **met** |
| 18 | `grep -c 'Passed!'` prints 2 | **prints 2** (was 1) | **met** |
| 19 | `grep -c 'No test is available'` prints 0 | prints 0 | met |
| 20 | `--list-tests` exit 0, `grep -c 'Test1$'` prints 0 | exit 0, prints 0 | met |
| 21 | exactly 4 `ProjectReference` lines | 4, same four as review 01 | met |
| 22 | duplicate-source check prints nothing | printed nothing | met |
| 23 | skeleton: `dotnet run` exit 0, non-empty, no `Hello, World!` | 0 / 0 / 0, `Fleet Manager Coming Online` | met |
| 24 | requirement 7: graph unchanged, no new project/edge, no `.cs` copied | 4 edges, `sln list` same four, md5 check empty | met |

23 of 24 met. The one miss is a check the lesson's own reference snippet cannot satisfy (R1).

**Substance checks (re-run):**

| Check | Evidence | Result |
|---|---|---|
| `ShuttleStatus` shape unchanged and matches the page | `git diff bafe1bb HEAD` touches no field of the record; the page's JSON fence parsed (python3) → keys `ShuttleId, State, Position, Timestamp, FaultReason` in contract order, equal to the record's five properties | met |
| Both contract tests unchanged and real | `ShuttleStatusTests.cs` is byte-identical to the commit under review (the using was removed by `3c81a3b` then restored by `e18a8af`); `Passed: 2` | met |
| `StartTests.cs` is the lesson's option-A shape | read against hints Tier 3: same `AppContext.BaseDirectory` anchor, same `ArgumentList`, same read-then-wait, same three asserts; only the test name differs, and the lesson leaves naming to the owner | met |
| Tests can fail | mutations A and B: exit 1 each, the correct assertion named in the failure | met |
| Contract triangle agrees | fence payload keys == record fields (checked above); section/version/fence greps exit 0 | met |
| `dotnet run` returns the app's own exit code | exit 0, output `Fleet Manager Coming Online` | met |

## Findings after re-review

### Should-fix

**R1 — the lesson's acceptance block contradicts the lesson's own reference snippet (a lesson
defect, not an owner defect).**
The assignment says all four placeholder greps "must be empty afterwards", but
`grep -rn 'Hello, World!' src --include='*.cs'` now prints
`src/FleetManager.Tests/StartTests.cs:25: Assert.DoesNotContain("Hello, World!", output);`
because hints Tier 3 hands the owner exactly that line as option A — and the lesson's own
verification table records the professor measuring it ("with `Hello, World!` back in
`Program.cs` … `Assert.DoesNotContain() Failure: Sub-string found`"). The professor's own
scratch fixture, dropped into `src/`, would fail the professor's own gate.
Why it matters here: an acceptance block that cannot be satisfied honestly trains exactly the
behaviour a fleet-support culture must never learn — working around the check instead of
fixing the thing, and then trusting the check anyway. The owner **cannot** fix this in
`src/`: deleting the assertion reopens should-fix 2, and string-splicing the literal to fool
the grep is worse than the mismatch. Fix belongs in the lesson — scope the grep (for example
`grep -rn 'Hello, World!' src/FleetManager`, or exclude test assertions) or reword the
criterion to "no template string outside an assertion" — then re-measure the lesson's
verification table row. **Do not change `StartTests.cs` for this.**

### Consider

**R2 — no timeout around the child process.** Measured: a child that never exits hangs
`dotnet test` (exit 124 under my 25 s kill). Same shape as the lesson's snippet, so this is
not a deviation — but it is the next question a reviewer asks once the child does anything
real, and the tooling is already in the box (`[Fact(Timeout = …)]` in xUnit v2, or a
`CancellationTokenSource` around the read). Decide it before M2, not during a red build.

**R3 — commit message hygiene, two ways.** `65d770a` has no milestone prefix (AGENTS.md wants
`M0: …`, which its two neighbours carry). Worse, `e18a8af` claims to remove a
`System.Text.Json` using *from `ShuttleStatusTests`* while its diff **adds** one there — the
removal from the test file was the mistake `3c81a3b` made, and `e18a8af` repaired it but
announced the opposite. Why it matters: `git log` is the audit trail you read instead of
every diff; a message that contradicts its own patch costs a puzzled hour every time someone
bisects.

**R4 — two cosmetic slips in `StartTests.cs`, named so they get studied, not scrolled
past:** line 10 is indented with 9 spaces against the file's 8, and the file ends without a
trailing newline (`git diff` prints `\ No newline at end of file`). Neither affects
behaviour; both are fingerprints of code typed fast and not read back — the habit this
project exists to break.

## What is solid

- The blocker was fixed at the root, not worked around: the DLL is anchored to the directory
  the test DLL lives in, so review 02's failure mode cannot recur from any working directory
  — measured from two.
- The three assertions are now demonstrably able to fail, each on its own input — measured
  four ways, including the large-output case the old ordering could not have survived.
- The graph still has not moved: four edges, byte-identical to review 01, duplicate-source
  check empty, `sln list` unchanged.
- The contract page, type, and tests still agree, and the page's prose typo is gone.

## Explain-it questions — still NON-BLOCKING (review 01 waiver applies)

Nothing below blocks this verdict. Answer them before the next lesson, out loud.

1. Your lead reads `StartTests.cs` and asks: "why is the DLL path built from
   `AppContext.BaseDirectory` instead of `../FleetManager` — what directory did the old path
   resolve against, and why does that directory never move?"
2. "Talk me through lines 20–21. What breaks if you swap them? What would break if you also
   set `RedirectStandardError = true` without reading it? You ran a 5000-line test — what
   exactly did that prove, and what did it not prove?"
3. "Which of the three asserts fires first if the program prints nothing? If it prints the
   template string? If it exits 3? And why did a reviewer care that the old code called
   `ReadToEnd()` twice?"

### Blocks to study (unclear, fragile, or copied-looking)

- `src/FleetManager.Tests/StartTests.cs:10` and the last byte of the file — the 9-space
  indent and the missing trailing newline. Diff your file against the hints snippet and say
  which characters are yours.
- `src/FleetManager.Tests/StartTests.cs:14` — `FileName = "dotnet"`. Where does that
  executable come from, what happens on a machine where it is not on `PATH`, and why does the
  lesson accept that assumption at M0 but not later?
- `git show e18a8af` — a commit message that contradicts its own diff. Say what it should
  have said.
- `git show 65d770a` — no `M0:` prefix while its neighbours carry one.
- The acceptance line `grep -rn 'Hello, World!' src --include='*.cs'` versus hints Tier 3's
  assertion: find both by eye, and say which one you would change and why you must not change
  the assertion.

## Revisit list

- **Pipe discipline, now that you have it right:** read-then-wait, redirect only what you
  drain, what a 64 KB buffer does to an ordering mistake — your 5000-line run is the evidence;
  be able to narrate it.
- **Timeouts as the sibling of ordering:** why a correct read can still hang forever, and
  where a timeout belongs in a test that starts a process.
- **Writing the gate:** how an acceptance check gets written so the reference solution passes
  it — you now know what it feels like from the failing side (R1).
- **Vacuous assertions:** you have a measured before (double `ReadToEnd` → `""`) and after
  (mutations A and B). State the three properties — asserts, can fail, says what it proves —
  against both versions.
- **Commit messages as an audit trail:** milestone prefix, and a message that matches the
  diff.
- Next up: **lesson 03, Docker services and the fresh-container build** — the last piece of M0.

## End-product check

The interface this lesson introduced is unchanged and now behaves: `ShuttleStatus` on a
versioned page, enforced by the type, proven by two tests that can fail, plus a start-up
check that really exercises the program it claims to exercise. Nothing here needs reworking
for M1's shuttle or M2's MQTT round trip — the page/type/test triangle is exactly the
mechanism that keeps those honest once messages travel. The residual risks are small and
named: a start-up test with no timeout (R2) will matter the day the fleet manager does more
than print a line, and R1 means the acceptance block is not yet the trustworthy gate it is
supposed to be — which counts more than it sounds, because M0's "done when" is verified with
these very commands. **M0 still does not close here:** `docker/` is empty, and the spec's
M0 "done when" needs a clean build and passing tests in a fresh container — lesson 03.

## README

**Not changed.** The M0 row stays `not started`: no Dockerfile, no `docker/compose.yml`, no
container build has been run — and this verdict is *pass with changes*, not a clean pass.
Nothing reached `done`, so no verification note is added and no other line of the README was
touched.

## What I changed in this repository (re-review)

- **Appended:** this "Re-review" section to `docs/reviews/02-first-message-contract.md`.
  Everything above it is exactly as review 02 left it. Uncommitted — I commit only when asked.
- **README.md:** not touched.
- **`src/`:** not touched. Mutations ran against a copy in `/tmp/opencode`, now deleted;
  `git status --porcelain` empty after all builds; `dotnet build-server shutdown` run; no
  broker, container, `dotnet run`, test host, or MSBuild node process left running
  (`docker ps -q` → 0 containers).
