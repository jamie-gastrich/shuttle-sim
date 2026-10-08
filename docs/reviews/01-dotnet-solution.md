# Review 01: .NET solution skeleton (lesson 01)

**Date:** 2026-10-07
**Reviewed:** commits `5e156f2` (`M0: pin the SDK with global.json`) and `95538a9`
(`M0: add solution skeleton with contracts and test projects`), branch `master` in sync
with `origin/master`, working tree clean before I started.
**Scope:** lesson 01 only. This is *not* an M0 milestone pass — see the end-product check.

**Verdict: pass** (lesson 01).

No blockers. Two should-fix / consider items are repo- and record-hygiene, neither of them
a lesson-01 requirement.

---

## What I ran, and what happened

All commands run from a clean tree; exit codes checked directly, never through a pipe.

### Repository root

```
$ test -f global.json                                   exit 0
$ grep -q '"rollForward": "latestFeature"' global.json  exit 0
$ dotnet --version                                      exit 0, printed 10.0.112
```

`global.json` as committed:

```json
{
  "sdk": {
    "rollForward": "latestFeature",
    "version": "10.0.112"
  }
}
```

Pinned version `10.0.112` matches `dotnet --version` exactly. The file sits at the repo
root, so the SDK search-upward behaviour from record 01 §12 governs commands run in `src/`.

### From `src/`

```
$ dotnet build     exit 0
Build succeeded.
    0 Warning(s)
    0 Error(s)
Contracts -> .../src/Contracts/bin/Debug/net10.0/Contracts.dll
Contracts.Tests -> .../src/Contracts.Tests/bin/Debug/net10.0/Contracts.Tests.dll
FleetManager -> .../src/FleetManager/bin/Debug/net10.0/FleetManager.dll
FleetManager.Tests -> .../src/FleetManager.Tests/bin/Debug/net10.0/FleetManager.Tests.dll
```

```
$ dotnet test      exit 0
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 29 ms - Contracts.Tests.dll (net10.0)
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 16 ms - FleetManager.Tests.dll (net10.0)
```

Both test project DLLs ran — the lesson's "output runs tests from BOTH test project DLLs"
criterion is met.

```
$ dotnet sln list  exit 0
Project(s)
----------
Contracts.Tests/Contracts.Tests.csproj
Contracts/Contracts.csproj
FleetManager.Tests/FleetManager.Tests.csproj
FleetManager/FleetManager.csproj
```

All four projects, exactly one solution (`src/ShuttleFleet.slnx`), no fifth project.

### The graph check

```
$ grep -rn "ProjectReference" --include=*.csproj .     exit 0, exactly four lines
./FleetManager/FleetManager.csproj:4:    <ProjectReference Include="..\Contracts\Contracts.csproj" />
./Contracts.Tests/Contracts.Tests.csproj:22:    <ProjectReference Include="..\Contracts\Contracts.csproj" />
./FleetManager.Tests/FleetManager.Tests.csproj:22:    <ProjectReference Include="..\FleetManager\FleetManager.csproj" />
./FleetManager.Tests/FleetManager.Tests.csproj:23:    <ProjectReference Include="..\Contracts\Contracts.csproj" />
```

Read line by line against record 01 §9: `FleetManager -> Contracts`,
`Contracts.Tests -> Contracts`, `FleetManager.Tests -> FleetManager` (line 22),
`FleetManager.Tests -> Contracts` (line 23). Four edges, four lines, each in the
consuming project's `.csproj`. No edge points at an app except its own test project; the
two apps do not reference each other; no `ShuttleSim` edges exist yet (correct — M1).

### The duplicate-source check

```
$ find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -exec md5sum {} + | sort | uniq -w32 -D
(no output)
```

Nothing printed. No `.cs` file is a copy of another.

### Structure and rule checks

- Record 01 §9 layout: `ShuttleFleet.slnx` + `Contracts` (classlib), `FleetManager`
  (console, `<OutputType>Exe</OutputType>`), `Contracts.Tests` and `FleetManager.Tests`
  (xunit, `IsPackable=false`, the four template packages). Matches.
- All four projects made with SDK templates: `net10.0`, `ImplicitUsings`, `Nullable`
  enabled, file-scoped namespaces, `Using Include="Xunit"` in the test projects. Matches
  the lesson's stated style (template conventions — no C#/ .NET style skill is installed
  in this repo, and none of the installed skills — `cpp-style`, `python-style`, `ros2`,
  `gazebo-sim` — applies to C#).
- Hard-coded names / topics / timeouts / retry counts / sizes: none present. There is no
  configuration yet to get wrong; `Program.cs` is still the template string.
- Nothing in `src/` was written or edited by me. I built, tested, read, and grepped only;
  I restored the tree afterwards (see issue S1 for why restoration was needed).

---

## Acceptance criteria (lesson 01)

| # | Requirement | Evidence | Result |
|---|---|---|---|
| 1 | `global.json` at repo root, pins installed SDK, `rollForward` `latestFeature`, made with the `globaljson` template | `test -f` exit 0; `grep -q` exit 0; file content shown above; `dotnet --version` = `10.0.112` = pin | met |
| 2 | `src/ShuttleFleet.slnx`, one solution, created in `src/` | file exists, is the only solution in `src/`, `dotnet sln list` exit 0 | met |
| 3 | Four projects, correct templates | `.csproj` contents: classlib / console `Exe` / two xunit with template packages | met |
| 4 | All four in the solution | `dotnet sln list` prints all four | met |
| 5 | Exactly the four edges from record 01 §9, no others | grep output: exactly 4 lines, all four edges, no fifth | met |
| 6 | No `.cs` copied between projects | duplicate-source check printed nothing | met |
| 7 | Each test project keeps one passing test; `UnitTest1.cs` left in place | both `UnitTest1.cs` files present with `[Fact] Test1()`; `dotnet test` exit 0, `Total: 1` from each DLL | met |
| — | Acceptance commands: `dotnet build` 0, `dotnet test` 0 (both DLLs), `dotnet sln list` 0 | recorded above | met |

Lesson-quality check (my review of the lesson itself, not the owner's work): the lesson
declares exactly **one** new concept — project references — and teaches it in "What a
project reference is" and "The one-way graph", so it is inside the 3-concept limit and
every item on its "Concepts this assignment requires" list was actually taught. The
scaffolding, solution file, and xUnit material is explicitly labelled a refresher card,
which is fair for an owner with 8 years of C#. The lesson carries a verification table,
a worked example measured in `/tmp/opencode`, an assignment with command-checkable
acceptance criteria, and a hints file with three tiers. One defect is filed under issue
C1 below.

---

## Top issues, ranked

**No blockers.** Every lesson-01 requirement and acceptance criterion is met by a command
I ran myself.

**S1 — should-fix: no `.gitignore`; 273 of 291 tracked files are `bin/`/`obj/` build
artifacts.**
`git ls-files | grep -cE '/(bin|obj)/'` → **273**, out of 291 tracked files total. There is
no `.gitignore` in the repo. My own `dotnet build` modified **47 tracked files** (`.dll`,
`.pdb`, `AssemblyInfo.cs`, `project.assets.json`, …); I ran `git restore .` afterwards to
leave the tree as the owner pushed it. Why it matters, in this project's terms: this is the
silent-drift failure from today's lesson moved into the repository — a stale committed
`Contracts.dll` can mask a source change, every build dirties the review diff, and two
people touching generated files will merge-conflict on binaries. It also undermines M0's
"clean build and tests pass in a fresh container": a fresh clone is not clean when it
carries 273 generated files. This is squarely M0 "repo and tooling" scope, but it was not
in lesson 01's requirement list, so it is a should-fix for now, not a lesson-01 failure.
Fix is one `.gitignore` plus `git rm -r --cached` on the artifact folders — owner's call,
owner's typing.

**S2 — should-fix: template placeholders are still the only code.**
`Contracts/Class1.cs` (`public class Class1 {}`), `FleetManager/Program.cs`
(`Console.WriteLine("Hello, World!")`), and the two empty `UnitTest1.Test1()` bodies.
This is explicitly permitted for lesson 01 — the lesson names `UnitTest1.cs` as the flag
the reviewer looks for, and moving the real tests to lesson 02 is the lesson's stated
plan. Why it still ranks: record 01 §10 says each test project holds "at least one real
test of something real" and that `FleetManager` "starts, logs, and exits cleanly" — an
empty `Test1` passes for the wrong reason (the lesson measured this itself), and
`Hello, World!` does not log. If these survive past lesson 02, every green `dotnet test`
is worth nothing, and an operator looking at fleet-manager logs at M5 finds nothing. Both
must go in lesson 02; M0 cannot close with them in place.

**C1 — consider (for the professor, not the owner): a plan change lives only in a lesson.**
Record 01 §10 states the real tests are "lesson 01's assignment". Lesson 01's "Note: the
tests moved to lesson 02" moves them to lesson 02 and says "records are append-only, so
record 01 stands as written". That is honest, but AGENTS.md requires that changes to
decided things get a decision record linking back. Right now the two documents disagree
and only one of them is in `docs/decisions/`. A two-line addendum record referencing 01
would make the log authoritative. Lesson/decision defect, not a student failure.

**C2 — consider (for the professor): the `dotnet --version` acceptance criterion is
slightly loose.**
The lesson says `dotnet --version` "prints the version pinned in global.json". With
`rollForward: latestFeature` the SDK prints whatever accepted version it resolves — on a
machine holding only `10.0.200`, it prints `10.0.200`, not `10.0.112`, and the criterion
would read as failed. True on this machine today; brittle as a written check. Worth
rewording to "prints a version accepted by `global.json`" in a later lesson.

**C3 — consider: `docker/` exists but is empty and untracked, and `docs/contract/` is
empty.**
Expected at this point — Docker setup and the first real message type are lessons 02/03.
Recorded here only so the gap is on the record: AGENTS.md already points at
`docker/compose.yml`, which does not exist yet. Neither is a lesson-01 issue; both are
M0 blockers for later.

---

## What is solid

- The four-edge graph is exactly record 01 §9 — no fifth edge, no shortcut between the
  two apps, no `ShuttleSim` edges sneaked in early. The contract rule is now one line of
  XML per edge, greppable, as the lesson promised.
- Every command in the lesson's acceptance criteria was run by me with the documented exit
  code and the documented output. Nothing had to be taken on trust.
- The solution contains exactly four projects and one `.slnx`; no stray project, no second
  solution, no tests-excluded-from-solution mistake.
- Commits are small, two of them, one concept each, messages naming the milestone
  (`M0:`) exactly as AGENTS.md's commit rule asks.
- The duplicate-source check prints nothing — no copied `.cs` anywhere, so the drift trap
  this lesson exists to prevent is not present.

---

## Explain-it check

Answer these **out loud, without notes**, before the next lesson. Questions 1–5 come from
lesson 01's "Say it out loud" section; question 6 is from this review. Do not write code —
just talk.

1. You ran `dotnet add FleetManager/FleetManager.csproj reference Contracts/Contracts.csproj`.
   What exact line did that write into the file, and which project's types became visible
   to which? Which way does the arrow point, and why does direction matter?

2. Both test projects need to see `Contracts`. Do `Contracts.Tests` and
   `FleetManager.Tests` need a reference to each other? Why, or why not?

3. You delete the `<ProjectReference>` line from `FleetManager.csproj` right now and run
   `dotnet build`. What happens, when do you actually find out something is broken, and
   which check catches it *today*?

4. Someone copies a shared `.cs` file into two projects "just to make it build". What
   failure does that hide, and what measured experiment from today's lesson proves it?

5. How does this four-line reference graph make the rule "shuttles talk to the fleet
   manager only through the contract" physical instead of just words in `AGENTS.md`?

6. Your repository has 273 of 291 tracked files under `bin/` and `obj/`, and no
   `.gitignore`. My `dotnet build` changed 47 tracked files. Why does committing build
   output cause the same class of problem as copying a shared source file — and what
   single file stops it?

### Blocks to study (unclear, placeholder, or copied-looking)

- `src/Contracts/Class1.cs` — template stub, empty class, name `Class1`. Know why it is
  still here and what replaces it in lesson 02.
- `src/FleetManager/Program.cs` — the template's `Hello, World!` and its `aka.ms` comment.
  Record 01 §10 wants a skeleton that "starts, logs, and exits cleanly"; say what "logs"
  will mean here and where the logging setup will live.
- `src/Contracts.Tests/UnitTest1.cs` and `src/FleetManager.Tests/UnitTest1.cs` — empty
  `[Fact]` bodies that pass. The lesson measured that an empty test reports
  `Passed! ... Total: 1`. Explain why that is a green light that means nothing, and why
  the file name is the flag I check.
- The `<ProjectReference Include="..\Contracts\Contracts.csproj" />` lines — why the
  backslashes and the `..`, and why the path is written relative to the `.csproj` that
  contains it, not to the repo.
- The leading `﻿` byte (a UTF-8 BOM) at the start of the two test `.csproj` files, and the
  `<ItemGroup>` ordering in `FleetManager.csproj`. Cosmetic, template-generated — but say
  out loud why you know it is harmless rather than guessing.
- `src/ShuttleFleet.slnx` — read it and explain the difference between a project being
  *in the solution* and one project *referencing* another.

---

## Revisit list

- Direction of a project reference and the four M0 edges (record 01 §9). This is load-bearing
  for every later milestone; at M1 it grows to six edges with `ShuttleSim`.
- Why the grep is the real check while the projects are empty, and when the compiler
  (CS0246) takes over — lesson 02 starts using `Contracts` from app code.
- `global.json` semantics: `version` vs `rollForward: latestFeature`, and why the file has
  to sit at the repo root.
- What belongs in git versus what the build generates (`.gitignore`, `git rm --cached`).
- Where changes get recorded: lesson notes vs append-only decision records (issue C1).
- Next up: lesson 02's real message type and real tests, then Docker for broker/database —
  both required before M0 can close.

---

## End-product check

This skeleton is the physical form of the spec's central constraint: two programs that can
only agree on messages through one shared, compiler-enforced project. The four edges are
the right shape for the end product — at M1 `ShuttleSim` joins pointing only at
`Contracts`, at M2 `Contracts` holds the real versioned message types, and from then on a
contract change that breaks one side breaks the build before any pallet moves. The risk
this piece introduces is not structural but procedural: with `bin/`/`obj/` committed and
placeholder tests still in place, the two mechanisms this milestone exists to provide — a
trustworthy diff and a test suite that means something — are both currently
non-functional, and both are scheduled to be fixed in lesson 02. Messaging, state
machines, concurrency, broker/database failure behaviour, and configuration are all
correctly absent at this lesson; there is nothing yet to judge them against, and no
judgeable decision logic exists until M1. **M0 remains open** pending lessons 02–03: no
Docker setup for broker/database, no real message type, and no fresh-container build have
been done or verified.

---

## README

Not changed. The M0 row stays `not started`, because the spec's M0 "done when" criteria
(clean build and tests in a fresh container; Docker setup for broker and database; one
real message type defined and unit tested) are not met — only the lesson-01 portion of M0
exists. No verification note added, since no milestone reached `done`.

---

## Explain-it answers

**Recorded:** 2026-10-07, answers received verbatim after the review above. The verdict
above (**pass**, lesson 01) is **not changed** by this section — it grades the oral
answers only, as the explain-it check requires.

Grades: `correct` / `partially correct` / `incorrect` / `not answered`.
Verdict per question: `pass` / `needs re-answer`.

| # | Grade | Verdict |
|---|---|---|
| 1 | partially correct | **needs re-answer** |
| 2 | partially correct | pass |
| 3 | partially correct | **needs re-answer** |
| 4 | partially correct | **needs re-answer** |
| 5 | not answered | **needs re-answer** |
| 6 | partially correct | **needs re-answer** |

### Per question

**Q1 — partially correct, needs re-answer.** The line is quoted correctly and the
visibility direction is right: `Contracts`' types become available inside `FleetManager`
(lesson 01 § "What a project reference is", the bullets explaining what sits inside
`FleetManager.csproj`). What is missing is the rest of the question: the arrow stated
explicitly (`FleetManager -> Contracts`, the consumer points at the consumed) and *why*
direction matters — no edge means the fleet manager cannot reach the shuttle's code, and a
forbidden edge is one line of XML the reviewer's grep finds (lesson 01 § "The one-way
graph", the two bullets: "The fleet manager cannot reach into the shuttle's code, because
there is no edge"). Re-answer the "why" clause only.

**Q2 — partially correct, pass.** The principle is right (a reference exists only where
the other project's types are actually used, so there is no cycle), but the question asked
about `Contracts.Tests` ↔ `FleetManager.Tests` and the answer addressed `Contracts` ↔
`FleetManager`. Same one-sentence reasoning applies to the pair that was asked; say it for
that pair out loud once (lesson 01 § "The one-way graph"). Not worth a re-answer cycle on
its own.

**Q3 — partially correct, needs re-answer.** Correct that today's build still succeeds and
the break only surfaces once code uses the missing type (CS0246 — lesson 01 verification
table rows "A missing `ProjectReference` fails the build **when code uses the type**" and
"A missing edge does **not** fail an empty skeleton"). The question's third part — which
check catches it *today* — was not answered: it is the `grep -rn "ProjectReference"
--include=*.csproj .` (lesson 01 § "The one-way graph": "right now, with empty projects,
you can delete an edge and `dotnet build` still exits 0 … So right now the grep is the
check that counts"). Also note the hedge ("I think"): this one is measured in the lesson,
so state it flatly. Re-answer: name the check.

**Q4 — partially correct, needs re-answer.** "It will build stale data" gestures at the
right failure but needs saying precisely: the build succeeds *and* the tests pass while
testing the stale copy, so nothing ever reports the mismatch — silent drift (lesson 01 §
"Options survey", "Option B, the trap"). The measured experiment was not identified: a
`Pallet.cs` copied into two projects, `WeightKg` renamed to `GrossKg` in one copy only,
`dotnet test` still exit 0, `Passed!` (verification table row "A copied file drifts in
silence"). Re-read § "Options survey — Option B, the trap" and re-answer both halves.

**Q5 — not answered ("I'm not sure"). Judged a covered gap, not a blocker.** Nothing in
the code depends on this answer and the lesson teaches it directly in one place, so it
does not fail the build or the lesson verdict — but it is the lesson's central idea, so it
must be answered before lesson 02. Review: lesson 01 § "Where this fits" (the paragraph
"A rule written in a markdown file is easy to break. A rule the compiler enforces is hard
to break") and § "The one-way graph" (the floor-plan reading plus the two bullets on the
forbidden edge and the grep). Then re-answer: how do four lines of XML enforce what
AGENTS.md only asks for?

**Q6 — partially correct, needs re-answer.** The single file is right: `.gitignore`
(review issue S1, which is where the question came from). The mechanism was guessed with a
question mark. Say it in one sentence: a committed `bin/`/`obj/` DLL can be older than the
source it claims to be built from, so a build or a review diff works from a fossil — the
same silent staleness as a copied `.cs`, which is why one `dotnet build` dirtied 47
tracked files (review issue S1). Re-answer the first half.

### Overall verdict on the explain-it answers

**Lesson 01's explain-it requirement is NOT yet satisfied.** One of six passes outright
(Q2); five need re-answer (Q1, Q3, Q4, Q5, Q6). None of the failures is a code or test
failure, and none reverses the `pass` verdict above — but the re-answers are required
before lesson 02 opens, because every one of these five points is load-bearing for the
contract work in lessons 02–03 (a wrong mental model of the edge graph is exactly how a
forbidden reference or a silent contract drift gets committed later).

Re-read, in one sitting, then answer 1, 3, 4, 5, 6 out loud without notes:

1. Lesson 01 § "The one-way graph" and § "Where this fits" → Q1 (the "why"), Q5, and half
   of Q3 (the grep).
2. Lesson 01 § "Options survey" (Option B) and the verification-table rows on copying → Q4.
3. This review, issue S1 → Q6.

### Waiver (2026-10-07)

The owner waived the re-answers: "dont make the explain-it part required, move on for now
to lesson 02." Recorded here so the log stays honest.

- **The grades stand as recorded above.** The table and per-question verdicts are not
  changed, deleted, or softened — Q1, Q3, Q4, Q5, Q6 remain `needs re-answer` on the
  record, and Q2 remains the only outright pass.
- **Lesson 01's `pass` verdict stands** and was never contingent on the re-answers; nothing
  else in this review changes.
- **The re-answer requirement is dropped as a gate.** Re-answers will not block lesson 02
  or any later lesson from opening, and no review will cite this section as a reason to
  hold work up.
- **The un-answered items are flagged as open understanding gaps.** The reviewer may
  re-ask any of them at a later milestone review, without warning and without a new
  assignment round. **Q5 — the contract-boundary idea (how four lines of XML enforce what
  AGENTS.md only asks for) — is the primary open gap**: it is the lesson's central idea
  and it was never answered at all, and it is the mental model the M1–M2 contract work
  rests on. Q1, Q3, Q4, and Q6 remain secondary open gaps on the same terms.

Owner's decision, owner's call; noted, not argued. The gap stays open either way.
