# Hints for lesson 01: project references and the .NET skeleton

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- Decision record 01, section 9 has the exact layout and the exact edges. That section is the
  assignment's source of truth. Read it once before touching the terminal.
- The whole assignment is four steps: pin the SDK, create the solution, create the projects,
  wire the edges. Ask yourself which step you are on.
- If `dotnet build` passes but the edge grep shows fewer than four lines, the problem is the
  graph, not the build. Empty projects never complain about a missing edge — I measured that.
- The duplicate-file check printing nothing is the goal. If it prints two lines, two files
  share content. Ask which one got copied.
- Do not skip `dotnet sln add`. "In the solution" and "referenced by" are two different
  relationships, and the assignment needs both.

---

## Tier 2 — direction

- Order of work, all inside the repo unless noted:

  1. Repo root: the `globaljson` template writes `global.json` for you. Do not hand-type it.
  2. Inside `src/`: `dotnet new sln -n ShuttleFleet`, then the four project templates
     (`classlib`, `console`, `xunit`, `xunit`).
  3. Inside `src/`: `dotnet sln ShuttleFleet.slnx add` with all four `.csproj` paths.
  4. Inside `src/`: one `dotnet add ... reference ...` command per edge, four in total.

- Direction of an edge: the reference line lands in the project that *needs to see* the other.
  `FleetManager` needs `Contracts`, so the line belongs in `FleetManager/FleetManager.csproj`.
  Check your work with `grep -rn "ProjectReference" --include=*.csproj .` and read each line
  as a sentence: "this project references that project."
- Which project needs which edge is spelled out in record 01 §9. Count your grep output:
  four edges at M0, and the two `ShuttleSim` edges do not exist yet.
- Each test project needs exactly two things to make `dotnet test` meaningful: the xUnit
  packages the template already gave it, and a reference to the code it tests.
- If `dotnet --version` prints a number that is not in `global.json`, check that the file
  sits at the repository root, not inside `src/`. The SDK searches upward from where you run.

---

## Tier 3 — near-solution

The command sequence, in order. This is scaffolding only — the graph you end up with is what
gets reviewed, and it must match record 01 §9.

```bash
# 1. repo root
dotnet new globaljson --sdk-version <installed-version> --roll-forward latestFeature --output .

# 2. inside src/
dotnet new sln -n ShuttleFleet
dotnet new classlib -n Contracts
dotnet new console  -n FleetManager
dotnet new xunit    -n Contracts.Tests
dotnet new xunit    -n FleetManager.Tests

# 3. inside src/
dotnet sln ShuttleFleet.slnx add <all four .csproj paths>

# 4. inside src/ — one command per edge, four commands
dotnet add <consumer-project> reference <project-it-needs>
```

`<installed-version>` is what `dotnet --version` prints on your machine. `<consumer-project>`
is the project whose `.csproj` should end up holding the line.

Then run the four acceptance commands from the lesson (`dotnet build`, `dotnet test`,
`dotnet sln list`, the edge grep) and compare the grep output line by line against the edge
list in record 01 §9. If a line is missing, you skipped an edge. If a fifth line appears, you
added one the record does not list — remove it.

What is deliberately *not* given here: the decisions. Which projects exist, which edges are
legal, why the graph points one way, and why copying a file is worse than referencing it.
Those are the explain-it part of the review, and they are yours to say out loud.
