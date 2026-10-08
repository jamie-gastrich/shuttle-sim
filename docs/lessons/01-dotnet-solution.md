# Lesson 01: Project references and the .NET skeleton

**Milestone:** M0 (Repo and tooling) · **Language:** C#/.NET · **Prerequisite:** none. This is the first lesson.

**Concepts this assignment requires:**

- [ ] **Project references** — one project's compiled code, handed to another project.
      Taught in [What a project reference is](#what-a-project-reference-is) and
      [The one-way graph](#the-one-way-graph).
      Used by: every reference edge in the assignment below.

That is the only new concept. The `dotnet` commands, solution files, and xUnit (the test
framework the templates wire in) are a refresher card, not a lesson:
[Scaffolding commands (a refresher)](#scaffolding-commands-a-refresher).

Style note: no C#/.NET style skill is installed in this repo. This lesson teaches toward the
conventions the SDK templates themselves write: `namespace X;` on the first line (a
file-scoped namespace), `Nullable` turned on, PascalCase type names, and test names shaped
like `Method_condition_expectation`. We follow the templates so the owner can match official
Microsoft docs line for line.

---

## Where this fits

Milestone M0 is "repo and tooling." It sounds boring. It is the part that saves you later.

Here is the real problem it solves. A shuttle and the fleet manager are two programs.
They must agree on every message. Wrong field name means a pallet goes to the wrong lane.
AGENTS.md has a rule: *shuttles talk to the fleet manager only through the contract.*

A rule written in a markdown file is easy to break. A rule the compiler enforces is hard to break.
So we make the rule physical: two programs can only see each other's code through one shared
project, and the projects point one way.

This lesson builds the empty skeleton: one solution, four projects, four reference edges,
a pinned SDK, and a green `dotnet test`. Lesson 02 puts the first real message type into
`Contracts` and writes the first real tests. Nothing here is throwaway; M0 ships on this frame.

Honest note about real warehouses: a real product team might ship the contract between
repositories as a versioned package. We keep everything in one repo. One build, one test run,
easier to explain. Not measured — just a stated trade-off.

---

## Verification table

Everything below I ran this session. Scratch work lived in `/tmp/opencode/lesson01-scratch/`
and gets cleaned up; nothing was written to `src/`.

| Claim | How I checked | Result |
|---|---|---|
| The installed SDK is 10.0.112 | `dotnet --version` in this repo | `10.0.112` |
| The `globaljson` template takes a version and a roll-forward policy | `dotnet new globaljson --help` | `--sdk-version` and `--roll-forward` listed |
| SDK 10 creates `.slnx` by default | `dotnet new sln --help` | `Default: slnx` |
| The scaffolding commands make the record-01 layout | full run in `/tmp/opencode/lesson01-scratch/transcript` | every command exit 0; 4 projects + `ShuttleFleet.slnx` |
| Bare `dotnet build` in `src/` builds all four projects | same run | exit 0, `Build succeeded.`, `0 Warning(s)`, `0 Error(s)` |
| Bare `dotnet test` runs both test projects | same run | exit 0; each reports `Passed! ... Total: 1` |
| Bare `dotnet sln list` lists the projects (one solution in the folder) | same run | exit 0; prints all four `.csproj` paths |
| Root `global.json` governs `dotnet --version` in `src/` | ran it in the root and in `src/` of a scratch repo | `10.0.112` both times |
| A missing `ProjectReference` fails the build **when code uses the type** | removed the edge from a toy, built | exit 1, `error CS0246: The type or namespace name 'Pallet' could not be found` |
| Adding the edge fixes it | `dotnet add ... reference ...`, then build and test | exit 0; `Passed! ... Total: 1` |
| A missing edge does **not** fail an empty skeleton | removed `FleetManager`'s edge to `Contracts`, built | exit 0, `Build succeeded.` — the grep check matters today |
| A project reference gives one type, one assembly | test asserted `typeof(Pallet).Assembly.GetName().Name` | referenced version: `Dock.Core` (not the test assembly) |
| A copied file drifts in silence | copied `Pallet.cs`, renamed a field in one copy, re-ran tests | exit 0, `Passed!` — the test kept testing the stale copy |
| A linked file (`<Compile Include>`) builds too, but compiles its own copy per project | same test on the linked version | exit 0; assembly name was `Rail.Core.Tests` |
| The duplicate-file check finds planted copies | planted two copies, ran the `md5sum`/`uniq` check | listed both pairs; empty after removal; empty on untouched templates |
| The xUnit template ships a placeholder test with an empty body | read generated `UnitTest1.cs`, then ran `dotnet test` | body is empty; still counts as `Passed! ... Total: 1` |
| xUnit template package list (record 01 §11 said "not yet verified") | read the generated `.csproj` | `coverlet.collector` 6.0.4, `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4 |

Not yet verified:

- The M0 container build. No Dockerfile exists yet; that is M0 work.
- Whether every editor we might use reads `.slnx`. Carried over from record 01 §12.
- What lesson 02's real tests will be. Lesson 02 is not written yet.

---

## Options survey

There are three ways to let two projects use the same C# type. All three compile.
They differ in what they hide from you.

One word first: an **assembly** is one compiled output — the file a project produces when
the build finishes.

| | A: project reference | B: copy the file | C: linked file (`<Compile Include>`) |
|---|---|---|---|
| Builds? | yes (measured) | yes (measured) | yes (measured) |
| How many copies of the type? | one, in one assembly (measured) | one per copy, forever | one per project assembly (measured) |
| Silent drift? | no — one source file | yes, the dangerous case (measured) | no — same physical file |
| Shows the one-way graph? | yes — the edge is a line in the `.csproj`, greppable | no | no |
| Tooling | `dotnet add <proj> reference <proj>` (ran it) | hand copy | hand-written XML |
| Usual place | in-repo project sharing, the standard .NET tool | quick hacks, legacy code | shared-project style, niche |

How real fleet systems do it: not measured. What is measured is that option A is the tool
Microsoft ships and runs for this exact job.

**Option B, the trap.** I copied `Pallet.cs` into a test project, renamed `WeightKg` to
`GrossKg` in only one of the two files, and re-ran the tests. Exit 0. `Passed!`.
The test never noticed, because it was testing its own stale copy. That is the failure this
whole lesson exists to prevent.

**Option C, the halfway house.** A linked file is one physical file compiled into two projects.
No drift, but each project builds its own assembly copy of the type. Measured: the test project
held its own `Pallet`. Also, nothing in the XML says "the graph points one way," so the contract
rule stays invisible.

---

## Decision

**Chosen: project references, one way only, exactly the four edges in
[decision record 01, section 9](../decisions/01-language-and-layout.md).**

Rejected: copying shared files (silent drift, measured above) and linked files (two assembly
copies, no visible graph).

This lesson writes no new decision record. Record 01 §9 already fixed the layout and the edges;
re-recording it would duplicate the log. The copy-versus-reference reasons live in the survey
above. If we ever change the edges — for example, ship `Contracts` as a package to a second
repository — that change gets its own new record linking back to 01.

---

## Lecture

Today we are going to learn about project references: what one is, why copying a shared file
is a trap, and why the fleet's reference graph only points one way.

### What a project reference is

A **project reference** is a line in a project file that says: "before you build me, build
that other project, and let me use its types."

A **project file** is the small XML file with the project's name, like `Contracts.csproj`.
It tells the build tool how to turn `.cs` files into a DLL. A **DLL** is the compiled file
a project produces; other programs load it instead of reading your source.

Think of a loading bay. Two forklifts need the same load plan. You can hang one plan on the
wall and point at it. Or you can photocopy it for each forklift. Both work today. Only the
wall copy stays true tomorrow.

Here is the line the CLI writes for you:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Contracts\Contracts.csproj" />
  </ItemGroup>
```

- `<ItemGroup>` is a basket. It holds a list of things of one kind.
- `<ProjectReference>` says what kind: a reference to another project in this repo. Not a
  NuGet package, which would be code downloaded from a package feed.
- `Include="..\Contracts\Contracts.csproj"` is the path. `..` means "up one folder, then
  into `Contracts`." The path starts from the project file that contains the line.
- This line sits inside `FleetManager.csproj`. So `FleetManager` may use types from
  `Contracts`, and the build builds `Contracts` first, automatically.

Where this is used later: every milestone. At M2, `Contracts` holds the real message types.
The fleet manager and the shuttle simulator both build against that one project. A field
rename that breaks one side breaks the build, before any shuttle moves.

### Why copying the file is a trap

Copying compiles. That is what makes it dangerous.

I ran the experiment. Two projects each had their own `Pallet.cs`. Tests passed. Then I
changed `WeightKg` to `GrossKg` in only one copy. Tests still passed — exit 0, `Passed!`.
The build saw two healthy projects. The test was checking a fossil.

With a project reference there is one source file. Change it, and every project that
references it rebuilds against the change. The compiler finds the mismatch for you.

Which project gets the line? The one that needs to see the other. `FleetManager` needs
`Contracts`, so the line lives in `FleetManager.csproj`. Direction matters; that is the
next section.

### The one-way graph

Decision record 01 §9 draws the whole graph:

```
Contracts.Tests    -> Contracts
FleetManager.Tests -> FleetManager, Contracts
FleetManager       -> Contracts
```

(The two `ShuttleSim` edges arrive at M1.)

Read it as a floor plan. `Contracts` is the middle of the warehouse. Apps point at it.
Nothing points at an app except its own test project. The apps never point at each other.

This is the AGENTS.md contract rule made physical:

- The fleet manager cannot reach into the shuttle's code, because there is no edge.
- If someone adds a forbidden edge, it is one line of XML in a `.csproj`. The reviewer's
  grep below finds it.

One honest wrinkle, measured: today, with empty projects, you can delete an edge and
`dotnet build` still exits 0. The build only complains once code uses the missing type
(CS0246, measured). So right now the grep is the check that counts. From lesson 02 on,
the compiler joins in.

The check:

```bash
grep -rn "ProjectReference" --include=*.csproj .
```

In my scratch run it printed exactly four lines, one per edge, each naming the
`.csproj` that holds the line and the `.csproj` it points at.

### Scaffolding commands (a refresher)

You have 8 years of .NET behind you. This is a card, not a lesson: the commands, run in
order, with the output I actually saw. No CLI trivia is graded.

Run the pin at the repo root. It writes `global.json`:

```bash
dotnet new globaljson --sdk-version 10.0.112 --roll-forward latestFeature --output .
```

```json
{
  "sdk": {
    "rollForward": "latestFeature",
    "version": "10.0.112"
  }
}
```

`version` is the SDK to use. `rollForward` says what to do when that exact SDK is missing.
`latestFeature` means: take the highest installed 10.0.x at or above the pin, or fail.
I read that wording off `dotnet new globaljson --help` this session. The pin is
configuration, so the assignment refers to it by filename and field, never to a number
typed twice.

Then, inside `src/`:

```bash
dotnet new sln -n ShuttleFleet
dotnet new classlib  -n Contracts
dotnet new console   -n FleetManager
dotnet new xunit     -n Contracts.Tests
dotnet new xunit     -n FleetManager.Tests
```

`classlib` makes a class library: a project with no start button, it only holds types.
`console` makes a program you start from a terminal. `xunit` makes a test project.

Observed: each prints `The template "..." was created successfully.` followed by a restore.
`dotnet new sln` writes `ShuttleFleet.slnx` (`.slnx` is the default format on SDK 10, per
`dotnet new sln --help`).

Now the solution learns about the projects, and the projects learn about each other:

```bash
dotnet sln ShuttleFleet.slnx add Contracts/Contracts.csproj FleetManager/FleetManager.csproj \
  Contracts.Tests/Contracts.Tests.csproj FleetManager.Tests/FleetManager.Tests.csproj
dotnet add FleetManager/FleetManager.csproj      reference Contracts/Contracts.csproj
dotnet add Contracts.Tests/Contracts.Tests.csproj reference Contracts/Contracts.csproj
dotnet add FleetManager.Tests/FleetManager.Tests.csproj reference FleetManager/FleetManager.csproj
dotnet add FleetManager.Tests/FleetManager.Tests.csproj reference Contracts/Contracts.csproj
```

Observed, first command: ``Project `Contracts/Contracts.csproj` added to the solution.``
(and three more). Observed, the rest: ``Reference `..\Contracts\Contracts.csproj` added to the project.``

Adding to the solution is not the same as referencing. The solution makes a project part of
the build. A reference makes another project's types visible. The assignment needs both.

Two commands finish the loop:

```bash
dotnet build   # observed: exit 0, Build succeeded., 0 Warning(s), 0 Error(s)
dotnet test    # observed: exit 0, Passed! ... Contracts.Tests.dll and ... FleetManager.Tests.dll
```

`dotnet test` needs at least one test to mean anything. The xUnit template already writes
one for you — `UnitTest1.cs` with an empty body. It passes. That is fine for today and
nothing more. See the note below.

### Note: the tests moved to lesson 02

Decision record 01 §10 says: *each test project holds at least one real test of something
real. Exactly which tests is lesson 01's assignment.*

The plan changed after that record was filed. Records are append-only, so record 01 stands
as written, and this note carries the change:

- **Lesson 01** (this one): `dotnet test` must run and pass with both projects wired.
  The template placeholder is a flagged placeholder.
- **Lesson 02**: the first real message type lands in `Contracts`, and both test projects
  get real tests of it. The placeholders get deleted there.

M0's "done when" is unaffected. M0 is not done after this lesson; it is done after lesson 02
plus the Docker work. The reviewer will check this note against record 01 §10.

### Recap

A project reference is one line in a `.csproj` that hands another project's compiled code
to this project.

Copying the shared file also compiles, and that is the trap: two copies drift, and the build
never says a word. Measured.

The fleet's graph points one way. Apps point at `Contracts`. Only tests point at apps. That
is the contract rule in AGENTS.md, turned into something the compiler can eventually catch.

The scaffolding is a fixed command list. The proof is four commands: `dotnet build`,
`dotnet test`, `dotnet sln list`, and the grep that shows the edges.

---

## Worked example

A toy: two projects, one type, one test. It proves the mechanism. It is **not** the
assignment — different names, no solution file, no `global.json`, two projects instead of four.

Source files:

```
/tmp/opencode/lesson01-scratch/toy/
  Dock.Core/
    Dock.Core.csproj
    Pallet.cs
  Dock.Core.Tests/
    Dock.Core.Tests.csproj
    PalletTests.cs
```

Step 1 — scaffold, and delete the template stubs:

```bash
mkdir -p /tmp/opencode/lesson01-scratch/toy && cd /tmp/opencode/lesson01-scratch/toy
dotnet new classlib -n Dock.Core
dotnet new xunit    -n Dock.Core.Tests
rm Dock.Core/Class1.cs Dock.Core.Tests/UnitTest1.cs
```

Step 2 — the type, `Dock.Core/Pallet.cs`:

```csharp
namespace Dock.Core;

public record Pallet(string Id, int WeightKg);
```

`namespace` is the folder address of the type. `record` is a plain data type with equality
built in. Two properties: an ID and a weight.

Step 3 — the test, `Dock.Core.Tests/PalletTests.cs`. Note: no reference exists yet.

```csharp
namespace Dock.Core.Tests;

public class PalletTests
{
    [Fact]
    public void Pallet_keeps_its_id()
    {
        var pallet = new Pallet("PLT-001", 850);
        Assert.Equal("PLT-001", pallet.Id);
    }
}
```

`[Fact]` marks a method as a test. `Assert.Equal` fails the test if the two values differ.

Step 4 — build it. This is the missing reference, felt:

```bash
dotnet build Dock.Core.Tests/Dock.Core.Tests.csproj
```

Observed (exit 1). The real line starts with my absolute path; I trimmed it to the part
that matters:

```
Dock.Core.Tests/PalletTests.cs(8,26): error CS0246: The type or namespace name 'Pallet' could not be found (are you missing a using directive or an assembly reference?)
```

The build printed that line once while compiling, then repeated it in the failure summary
after `Build FAILED.` Exit code was 1.

Step 5 — the one command that fixes it:

```bash
dotnet add Dock.Core.Tests/Dock.Core.Tests.csproj reference Dock.Core/Dock.Core.csproj
```

Observed: ``Reference `..\Dock.Core\Dock.Core.csproj` added to the project.``

Step 6 — look at what it wrote into `Dock.Core.Tests/Dock.Core.Tests.csproj`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\Dock.Core\Dock.Core.csproj" />
  </ItemGroup>
```

That is the whole mechanism. One line, one direction: tests see core, core never sees tests.

Step 7 — prove it:

```bash
dotnet build Dock.Core.Tests/Dock.Core.Tests.csproj   # observed: exit 0, Build succeeded.
dotnet test  Dock.Core.Tests/Dock.Core.Tests.csproj   # observed: exit 0
```

Observed test output:

```
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 8 ms - Dock.Core.Tests.dll (net10.0)
```

Your output will show your own directory path where mine shows
`/tmp/opencode/lesson01-scratch/toy`. Everything else matches.

---

## Say it out loud

Answer these without notes, the way you would in an interview:

1. `dotnet add FleetManager/FleetManager.csproj reference Contracts/Contracts.csproj` writes
   one line into a `.csproj`. What does that line say, and which project's types become
   visible to which?
2. Both test projects need `Contracts`. Do `Contracts.Tests` and `FleetManager.Tests` need a
   reference to each other? Why, or why not?
3. You delete the `ProjectReference` line from `FleetManager.csproj` right now. What happens
   on the next build, and when will you actually find out?
4. Someone copies a shared file into two projects to "just make it build." What failure does
   that hide, and what measured experiment in this lesson proves it?
5. How does this graph make the rule "shuttles talk to the fleet manager only through the
   contract" physical instead of just words in `AGENTS.md`?

---

## Assignment

Build the M0 skeleton from [decision record 01, section 9](../decisions/01-language-and-layout.md).
This is your code. Type it, run it, be able to explain every line.

### Requirements

1. `global.json` at the repository root. It pins the SDK version installed on this machine
   and sets `rollForward` to `latestFeature`. Create it with the `globaljson` template
   (command in the refresher above).
2. `src/ShuttleFleet.slnx` — one solution, created inside `src/`.
3. Four projects inside `src/`, all made with SDK templates:

   | Project | Template | Kind |
   |---|---|---|
   | `Contracts` | `classlib` | class library |
   | `FleetManager` | `console` | console app |
   | `Contracts.Tests` | `xunit` | test project |
   | `FleetManager.Tests` | `xunit` | test project |

4. All four projects added to the solution.
5. Exactly these edges, and no others. Record 01 §9 writes them as three lines; that is
   four edges, because `FleetManager.Tests` takes two:

   ```
   Contracts.Tests     -> Contracts
   FleetManager.Tests  -> FleetManager, Contracts
   FleetManager        -> Contracts
   ```

   The edges come from the record, not from my scratch run. You add them.
6. No `.cs` file copied from one project into another. Sharing happens only through the edges.
7. Each test project keeps at least one passing test so `dotnet test` means something.
   The template's `UnitTest1.cs` is acceptable **for lesson 01 only**. Leave it in place —
   deleting it would make `dotnet test` empty. Lesson 02 replaces it with a real test of a
   real message type. The file name is the flag the reviewer looks for.

### Acceptance criteria (all command-checkable)

From the repository root:

```bash
test -f global.json                                  # exits 0
grep -q '"rollForward": "latestFeature"' global.json # exits 0
dotnet --version                                     # prints the version pinned in global.json
```

From `src/` (check exit codes directly, never through a pipe):

```bash
dotnet build     # exit 0
dotnet test      # exit 0, and the output runs tests from BOTH test project DLLs
dotnet sln list  # exit 0, prints all four .csproj paths
```

The graph, from `src/`:

```bash
grep -rn "ProjectReference" --include=*.csproj .
```

Must print exactly four lines, matching the four edges in requirement 5 — one line per edge,
each in the `.csproj` of the project that receives the reference.

No copied source, from `src/`:

```bash
find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -exec md5sum {} + | sort | uniq -w32 -D
```

Must print nothing. Any output means two `.cs` files share identical content; check whether a
file was copied between projects.

Why the grep and not the build? Measured: with empty projects, deleting an edge still gives
`Build succeeded.` The grep is the check that works today. After lesson 02 adds code that uses
`Contracts`, a missing edge becomes CS0246 and the compiler takes over.

### Stretch goal

Write a small edge checker (Python is allowed for tooling per the spec). It reads the allowed
edge list from a config file — not from constants inside the script — walks every `.csproj`
under `src/`, and exits 1 if it finds an edge outside the list.

Accept: exits 0 on your tree; exits 1 after you plant a forbidden edge (for example,
`Contracts` pointing at `FleetManager`), and exits 0 again after you remove it.

### Not in this assignment

`ShuttleSim` and `ShuttleSim.Tests` arrive at M1 (record 01 §9). The Docker setup and the
first real message type are separate M0 work — the message type is lesson 02.
