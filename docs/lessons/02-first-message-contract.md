# Lesson 02: The first message contract

**Milestone:** M0 (Repo and tooling) · **Language:** C#/.NET ·
**Prerequisite:** [Lesson 01: project references and the .NET skeleton](01-dotnet-solution.md)

**Concepts this assignment requires:**

- [ ] **The message contract as a versioned artifact** — one written page per contract
      version, in `docs/contract/`, carrying a version number.
      Taught in [The contract is a versioned page](#the-contract-is-a-versioned-page).
      Used by: this assignment, then every message from M2 on.
- [ ] **The first real message type in `Contracts`** — a C# type whose JSON form is the
      message. Taught in [The first real message type](#the-first-real-message-type).
      Used by: `Contracts`, then both apps at M2 and the shuttle simulator at M1.
- [ ] **A real unit test** — a test with an assertion that can actually fail.
      Taught in [A test that can fail](#a-test-that-can-fail).
      Used by: both test projects now, and every test you write from here on.

Three new concepts. That is the cap.

JSON serialization, C# records, and xUnit are skills you already have. This lesson shows
them as worked material, never as new concepts. Three words for later, not today:
**MQTT** (a publish-and-subscribe messaging protocol), a **topic** (the named channel a
message is published to), and an **endpoint** (an address you send to). All three belong to
decision record 02 — a short file in `docs/decisions/` that writes down what was chosen,
what was rejected, and why — at M2.

Style note: no C#/.NET style skill is installed in this repo (lesson 01 checked). This
lesson teaches toward the conventions the SDK writes (the .NET SDK is the installed
developer toolchain that built your skeleton): file-scoped namespaces,
PascalCase type names, and test names shaped like `Method_condition_expectation`.

---

## Where this fits

Milestone M0 is "repo and tooling." Lesson 01 built the empty frame. This lesson puts the
first real thing on it.

Here is the problem it solves. The fleet manager and the shuttle are two programs. They
will never share memory. They only exchange messages, written in JSON — the plain-text data
format you already know. The text of one message has a name: a **payload**.

So the two programs must agree on what a message looks like: which fields, which names,
which types.

That agreement has a name: the **message contract**. AGENTS.md makes it the source of
truth: *the message contract is the source of truth for everything between the fleet
manager and the shuttles. It is documented and versioned in `docs/`. Changing it needs a
decision record and a version bump.*

A rule in a markdown file is easy to break. Lesson 01 turned part of it into four lines of
XML the compiler can enforce. This lesson turns the rest into three artifacts that must
match each other:

1. a page in `docs/contract/` that says what the messages look like,
2. a C# type in `Contracts` that the compiler checks,
3. a test that proves the two agree.

Think of a rack drawing. The drawing says where the beam goes. A gauge checks the hole is
in the right place. The inspection sheet records that someone checked. Three things, one
truth. Delete any one and you are guessing.

Honest note about real warehouses: a real team often keeps the contract in a separate
repository and ships it as a versioned package, so two codebases can upgrade on different
days. We keep everything in one repo, one build, one test run. Easier to explain, and the
trade-off is stated rather than measured.

**After this lesson, M0 is still not done.** The spec's M0 "done when" also needs a clean
build and passing tests **in a fresh container**. That is lesson 03: Docker services and a
fresh-container build. Review 01 says the same thing — "M0 remains open pending lessons
02–03."

### Check what you already know

Answer these out loud before you read on. Treat anything you hesitate on as new material.

1. In the REST APIs you have built, how did the client and the server agree on the shape
   of a request body? Who wrote that agreement down, and where did it live?
2. Two teams ship code against the same payload. One team renames a field. What catches
   it, and when?
3. What makes a unit test real? Name a test you have seen that passes without proving
   anything.

---

## Verification table

Everything below I checked by running a command this session, or by reading a file.
Scratch work lived in `/tmp/opencode/lesson02-scratch/` and was deleted afterwards.
I never wrote to `src/`, never built it, and never started a process there.

| Claim | How I checked | Result |
|---|---|---|
| Installed SDK | `dotnet --version` in this repo | `10.0.112`, exit 0 |
| `docs/contract/` has no page yet | `find docs/contract -maxdepth 1 -name 'v*.md'` | no output, exit 0 |
| The contract-page checks on a missing page | the contract-page block in the assignment, run today | `test -f` exit **1**; both greps exit **2** with `No such file or directory` |
| The placeholders exist today | `find src -name 'Class1.cs' -o -name 'UnitTest1.cs'` | prints 3 paths: `src/Contracts/Class1.cs`, both `UnitTest1.cs` |
| `Test1` exists today | `grep -rn 'Test1' src --include='*.cs'` | 4 lines, exit 0 |
| The template string exists today | `grep -rn 'Hello, World!' src --include='*.cs'` | 1 line, exit 0 |
| The graph is four edges today | `grep -rn "ProjectReference" --include=*.csproj src \| wc -l` | `4` |
| `Contracts` holds only the stub today | `find src/Contracts -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -not -name 'Class1.cs'` | no output (without the `bin`/`obj` exclusions it prints 3 generated files — measured, that matters) |
| No `.cs` is a copy today | the `md5sum`/`uniq -D` check from lesson 01, in `src/` | no output |
| Every `[Fact]` file is checked for an assertion | the loop below, run on `src/` | prints both `UnitTest1.cs` files — they have no `Assert` |
| The same loop is empty when tests are real | run on the toy and on a template-only test project | nothing on the toy; `UnitTest1.cs` on the template project |
| A JSON roundtrip test passes in the toy | `dotnet test` in the walkthrough below | `Passed! ... Passed: 2`, exit 0 |
| `--list-tests` prints test names, not just counts | `dotnet test ... --list-tests` in the toy | two lines, `Stop.Core.Tests.ArrivalTests.Arrival_...` |
| A template project lists the placeholder name | `--list-tests` on a fresh `dotnet new xunit` project | `Ph.Tests.UnitTest1.Test1` |
| Renaming a field: roundtrip still passes, wire-shape test fails | `sed` rename in the toy, then `dotnet test` | `Failed: 1, Passed: 1`, exit 1 (restored afterwards: exit 0) |
| Two test projects: one `Passed!` line each | bare `dotnet test` on a two-project scratch solution | two `Passed!` lines, exit 0 |
| A test DLL with **no** tests still gives exit 0 | deleted the only test from one scratch project, ran bare `dotnet test` | `Passed!` count **1**, `No test is available` count **1**, **exit 0** |
| `dotnet run` returns the app's exit code | scratch console app: `return 3`, then clean | `3`, then `0` |
| A test project can launch its referenced app's DLL | scratch: xunit project running the console app as a child process | failed on the template string (exit 1), passed after a real start line (exit 0), **no NuGet package added** |
| `System.Text.Json` needs no package | the toy compiled with only the template's packages | `using System.Text.Json;` built fine |
| JSON schema tooling exists for .NET (a JSON schema is defined in the survey below) | `dotnet package search JsonSchema.Net` | `JsonSchema.Net` 9.4.0, `NJsonSchema` 11.6.1, exit 0 |
| The contract-page checks behave both ways | scratch `docs/contract/v1.md`: correct, wrong version, missing | `test -f` 0 / version grep 0 / fence grep 0; version grep **1** when the content said `Version: 2`; `test -f` **1** when deleted |
| A decorated version line fails the check | wrote `**Version:** 1` in the scratch page | `grep -qE '^Version: 1$'` exit **1** |
| VDA 5050 publishes a human document *and* JSON schemas | fetched `raw.githubusercontent.com/VDA5050/VDA5050/main/README.md` and the VDA 5050 v2.1.0 PDF text this session | README: "If there are any differences between the markdown document/JSON schemas on GitHub and the published document by the VDA, the PDF on the VDA website is valid"; PDF: "JSON schemas are available for validation in the public git repository" |

Three terms used in the table above, each defined again in the lecture: a **roundtrip test**
sends a value through JSON and back and compares the two; a **wire-shape test** checks the
field names in the JSON text; a **test DLL** is the compiled form of a test project — the
file `dotnet test` runs.

Not yet verified:

- The assignment itself. You have not written it, so nothing about it is a fact yet.
- `dotnet test` inside this repo's `src/` this session. I did not run it: a build dirties
  the tracked `bin/`/`obj/` files (review 01, issue S1). The lesson-01 numbers for `src/`
  come from review 01.
- The M0 container build. No Dockerfile yet — that is lesson 03.
- How many messages M2 will need, and whether the contract page still reads well with
  twenty of them.
- Behaviour under a real broker: dropped, duplicated, and delayed messages. M2.

---

## Options survey

There is a real choice here: **what form the contract takes**. Three options. All three are
real: each can be written today.

One word first: a **JSON Schema** is a machine-readable file that says which fields a JSON
payload must have and what type each one is. A validator can read it and reject a bad
payload without running your code.

| | A: markdown page per version | B: JSON Schema file | C: the C# type is the only contract |
|---|---|---|---|
| Who can read it | anyone, including a controls engineer or a PM | people who read schemas | C# developers only |
| Does it check itself? | no — tests must do that | yes — a validator rejects bad payloads | the compiler checks C# callers only; it never sees the bytes that actually travel |
| Main failure mode | page and code drift apart (two sources of truth) | schema and code drift apart (two sources of truth, unless the schema is generated) | nobody outside the repo can read the contract; the JSON shape is invisible |
| Cost at M0 | zero tooling | one validator package plus wiring | zero |
| How real fleet systems do it | VDA 5050 — the published interface standard for automated guided vehicles (AGVs) and fleet control — ships a human document with field tables, **and** a `json_schemas/` folder in git, **and** says the human document wins if they disagree (both fetched this session) | same row: VDA 5050 ships schemas per release | — |

How real fleet systems do it, in one line: **both**. The page for people, the schema for
machines, one written rule about which wins when they disagree.

Our spec already points the same way. The system-overview table in `spec/shuttle-spec.md`
says the message contract's typical interface is *"versioned JSON schemas, topic names"* —
that is the end product. M0 has one message type.

---

## Decision

**Chosen for M0: option A — one markdown page per contract version, in `docs/contract/`,
the first one named `v1.md`, carrying a plain `Version:` line.**

Rejected for now:

- **Option C (code only)** — rejected outright. AGENTS.md requires the contract to be
  documented and versioned in `docs/`. The C# type alone fails that rule.
- **Option B (JSON Schema)** — deferred, not rejected. Measured: the tooling exists
  (`JsonSchema.Net` 9.4.0 on NuGet, the .NET package store). It is deferred because with
  one message the page is
  the cheaper truth, and a schema plus a page plus a type is three sources to keep in step
  before we have proved we can keep two in step.

What brings option B back: a second language that must validate payloads without
compiling C# (the dashboard is JavaScript and will talk REST, so today it does not need
this); or enough messages that hand-checking the page against the types stops being
reliable. That decision gets its own record — spec numbering keeps `02` for transport and
`03` for the database, so a contract-form record would be `04` or later (numbering rule in
[decision record 01a, section 6](../decisions/01a-tests-move-to-lesson-02.md)).

This lesson writes no new decision record. AGENTS.md already fixes `docs/contract/` as the
home of the contract, and the spec already names JSON schemas as the end product. The page
structure below is a lesson convention — the same kind of thing as lesson 01's acceptance
commands. It becomes a decision record the moment it changes.

---

## Lecture

Today we are going to learn about the message contract as a versioned page, the first real
message type in `Contracts`, and a test that can actually fail.

### The contract is a versioned page

A **message contract** is the written agreement on what each message between two programs
looks like: the field names, the types, who sends it, and to whom.

Why write it down at all? Because the code is not the agreement. The code is one program's
*guess* at the agreement. Two programs, two guesses. The page is where the guesses get
checked.

Why **versioned**? Two programs upgrade on different days. A shuttle in the field might
read yesterday's messages while the fleet manager already sends today's. A version number
says which shape a payload uses. Warehouse version of it: every rack drawing has a
revision number in the corner. Nobody builds a beam from "the drawing" — they build from
"the drawing, revision 4."

The rule comes from AGENTS.md, quoted in *Where this fits* above: documented, versioned,
change needs a decision record and a version bump. The page is where that rule becomes
something you can point at.

The structure — this part is mine to specify, the content is yours:

1. The page lives at `docs/contract/v1.md`. One file per version. When the contract
   changes, you add `v2.md`. The old page stays, so an old payload can still be read.
2. Near the top, one plain line, on its own line, with nothing decorated around it:

   ```
   Version: 1
   ```

   Plain, because the check is a plain `grep -qE '^Version: …'`. I measured that
   `**Version:** 1` fails that grep. A version line the checker cannot read is a version
   line nobody reads.
3. One section per message. The section heading is the message name.
4. Inside each section: who sends it to whom, a table of fields (name, JSON type, what it
   means, required or optional), and one example payload inside a JSON code fence — the
   three-backtick fence whose first line reads `json`.
5. A closing note saying that changing the contract means a decision record and a version
   bump.

Forward reference: at M2 this same directory grows — commands, status messages, alarms,
recovery, and the topic names from decision record 02. The reviewer will then check the
page against the code. Real warehouse honesty: at M2 the link between the two programs is
wireless, so messages can
arrive twice, arrive late, or not arrive. The page describes the *shape* of a message; what
happens when one goes missing is a different lesson.

### The first real message type

The page describes. The type enforces.

A **message type** is a C# type in `src/Contracts` whose JSON form is the message. Decision
record 01, section 4, reason 2 is where this comes from: in C#, a wrong field name or a
wrong type stops the build. Two programs and the tests all compile against one type, so a
rename that breaks one side breaks the build *before any shuttle moves*.

You already know records and JSON. Here is the only mechanic you need, as a reminder —
these two calls are the whole core of every test you will write, whatever carries the
messages later:

```csharp
var json = JsonSerializer.Serialize(message);              // type -> text
var back = JsonSerializer.Deserialize<YourType>(json);     // text -> type
```

`Serialize` turns a value into the payload — the text that travels between the two
programs. `Deserialize` turns that payload back into a value. Measured: `System.Text.Json`
needs no NuGet package — it ships inside the .NET shared framework (decision record 01,
reason 3), and the toy below compiled with only the template's packages.

One detail that decides your field names: JSON field names are case-sensitive. `Route` and
`route` are two different fields to the program that reads them. Pick the casing once, write
it in the contract page, and make the type produce exactly that. Your wire-shape test below
is what keeps the two honest.

Where the type lives, and why it matters:

- It lives in `src/Contracts`, one file per message, `public`, with a `record` unless you
  have a reason not to (records give you equality for free, which is what makes a roundtrip
  test one `Assert.Equal`).
- Lesson 01's trap still applies. Copying the file into two projects compiles and drifts
  silently. Sharing happens only through the reference edges.
- The graph stays at four edges. This lesson adds no project and no edge, so
  `grep -rn "ProjectReference" --include=*.csproj .` still prints exactly four lines.
  Now that app code uses `Contracts`, a missing edge becomes `error CS0246` instead of a
  silent pass — lesson 01 measured the before, you will feel the after.

Where this is used later: M1 gives `ShuttleSim` its own project pointing at `Contracts`
(three more edges, seven in total). M2 puts the real command, status, alarm, and recovery
types in the same project and puts them on MQTT. Every later milestone reads this type.

### A test that can fail

A **real test** is a test that fails when the thing it claims to check is wrong.

That sounds obvious. Look at what you have now. Review 01's issue S2, quoting your own
template: `UnitTest1.Test1()` with an empty body, and lesson 01 measured that it reports
`Passed! ... Total: 1`. It passes because nothing in it can fail. A green light that means
nothing is worse than no light: someone trusts it.

Three things make a test real:

1. **It asserts.** Every `[Fact]` has at least one `Assert`. The check below finds a
   `[Fact]` file with no `Assert` in it:

   ```bash
   for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do grep -q 'Assert' "$f" || echo "$f"; done
   ```

   Measured: today it prints your two `UnitTest1.cs` files. It printed nothing on the toy
   below. It prints nothing after your assignment.
2. **It can fail.** You have seen it fail already, in the worked example below: rename a
   field, watch the test turn red, exit 1.
3. **It says what it proves.** `--list-tests` prints the names. `Test1` proves nothing;
   `Arrival_json_keeps_the_agreed_field_names` — a name from the toy below — proves
   something you could say out loud to a controls engineer.

Two kinds of test are worth writing for a message type, and the toy shows both:

- **roundtrip** — send a value through JSON and back, then assert the two values are
  equal. It proves nothing is lost in translation.
- **wire shape** — the shape of the payload as it travels between the two programs. Assert
  the JSON text itself contains the field names the contract page documents. It proves the
  payload still matches the agreement.

Why you need both, measured: in the worked example I renamed one property. The roundtrip
test **still passed** — a record that serializes and deserializes with its own new name is
perfectly self-consistent. Only the wire-shape test caught it. A roundtrip test alone would
have let a breaking change through with a green light.

**The skeleton has to be worth testing.** Decision record 01 §10 wants `FleetManager` to
"start, log, and exit cleanly," and review 01 says `Hello, World!` does not log. Minimal
version, no new concepts: replace the template lines with a line that says the fleet
manager started, and exit 0. "Logs" at M0 means "writes a line saying what started." A real
logging framework is M5 territory (diagnostics); do not import it now.

That gives `FleetManager.Tests` something real to hold. Two honest options — pick one and
be able to say why:

- **Run the skeleton and check it.** The test project already references `FleetManager`,
  so the built `FleetManager.dll` sits next to the test DLL. The test can launch it as a
  child process — a second program the test starts — and assert: exit code 0, output is not
  empty, and no `Hello, World!`.
  Measured in scratch: this works with no extra package, and it failed while the template
  string was there and passed after a real start line.
- **Extract one tiny pure function** — a small piece of code that takes input, returns a
  value, and touches nothing else — from the startup path (something that builds the line
  the program prints) and test it directly. Smaller and faster, but it tests the function,
  not the program.

A test that runs the real program, by starting it, is heavier than a test of one pure
function. That is a style choice, not a new concept: the concept is only "it must be able
to fail."

### Recap

The contract is a page in `docs/contract/` with a version number on it. People read it.

The message type is a C# record in `Contracts`. The compiler checks it.

The tests prove the page and the type still agree. They prove it by being able to fail —
measured, in three places: a field rename turned one test red, an empty test passes for the
wrong reason, and a test DLL with no tests still gives exit 0.

The placeholders go: `Class1.cs`, both `UnitTest1.cs`, the empty `Test1`, and the template
`Hello, World!`. Decision record 01a moved that deletion list here.

---

## Worked example

A toy: a bus stop display. It is **not** the assignment — different domain (transit, not
pallets), different names, no solution file, no `src/`, no `FleetManager`. It shows three
mechanics: a message type, a contract page, and two tests that can fail. Do not copy it.
Your message has to be one a shuttle fleet actually exchanges — the spec's M2 names the
families (commands, status, alarms, recovery) — and its name and fields are yours to choose.

Source files, all under `/tmp/opencode/lesson02-scratch/walkthrough/`:

```
docs/contract/v1.md
Stop.Core/Arrival.cs
Stop.Core.Tests/ArrivalTests.cs
```

Step 1 — scaffold and delete the template stubs:

```bash
mkdir -p /tmp/opencode/lesson02-scratch/walkthrough && cd /tmp/opencode/lesson02-scratch/walkthrough
dotnet new classlib -n Stop.Core
dotnet new xunit    -n Stop.Core.Tests
rm Stop.Core/Class1.cs Stop.Core.Tests/UnitTest1.cs
dotnet add Stop.Core.Tests/Stop.Core.Tests.csproj reference Stop.Core/Stop.Core.csproj
```

Observed: `The template "Class Library" was created successfully.`, then
`The template "xUnit Test Project" was created successfully.`, then a line confirming the
reference was added (it quotes the path you passed to `dotnet add`).

There is no solution file in the toy, so `dotnet test` alone refuses to guess. Measured:

```
MSBUILD : error MSB1003: Specify a project or solution file.
```

(exit 1). So every test command below names the test project. Your repo has one solution in
`src/`, so bare `dotnet test` works there — lesson 01 measured that.

Step 2 — the type, `Stop.Core/Arrival.cs`:

```csharp
namespace Stop.Core;

public record Arrival(string Route, int MinutesAway);
```

Three lines. A namespace (the folder address of the type) and a record with two properties.

Step 3 — the tests, `Stop.Core.Tests/ArrivalTests.cs`. Two chunks of **one file**: type
both into the same file before you run anything.

```csharp
using System.Text.Json;
using Stop.Core;

namespace Stop.Core.Tests;

public class ArrivalTests
{
    [Fact]
    public void Arrival_survives_a_json_roundtrip()
    {
        var sent = new Arrival("42", 7);
        var json = JsonSerializer.Serialize(sent);
        var received = JsonSerializer.Deserialize<Arrival>(json);
        Assert.Equal(sent, received);
    }
```

The first test builds a value, sends it through JSON, brings it back, and asserts the two
records are equal. Record equality does the comparing for you.

```csharp
    [Fact]
    public void Arrival_json_keeps_the_agreed_field_names()
    {
        var json = JsonSerializer.Serialize(new Arrival("42", 7));
        Assert.Contains("\"Route\"", json);
        Assert.Contains("\"MinutesAway\"", json);
    }
}
```

The second test asserts the JSON text contains the field names the contract page documents.
This is the one that catches a rename.

Step 4 — the contract page, `docs/contract/v1.md` (four backticks around it, because the
page itself contains a fenced block):

````
# Message contract v1

Version: 1

## Arrival

Sent by a bus to the stop display.

```json
{"Route":"42","MinutesAway":7}
```
````

Step 5 — run it:

```bash
dotnet test Stop.Core.Tests/Stop.Core.Tests.csproj
```

Observed (exit 0):

```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2, Duration: 82 ms - Stop.Core.Tests.dll (net10.0)
```

Step 6 — look at the names, not just the count:

```bash
dotnet test Stop.Core.Tests/Stop.Core.Tests.csproj --list-tests
```

Observed (exit 0), last three lines:

```
The following Tests are available:
    Stop.Core.Tests.ArrivalTests.Arrival_survives_a_json_roundtrip
    Stop.Core.Tests.ArrivalTests.Arrival_json_keeps_the_agreed_field_names
```

Step 7 — the contract-page checks, run from the toy root:

```bash
DOC=docs/contract/v1.md
test -f "$DOC"; echo $?
V=$(basename "$DOC" .md); V=${V#v}
grep -qE "^Version: ${V}\$" "$DOC"; echo $?
grep -q '```json' "$DOC"; echo $?
```

Observed: `0`, then `0`, then `0`. `V` is read out of the filename, so no number is typed
twice. When I changed the page to say `Version: 2`, the middle check printed `1`. When I
deleted the page, `test -f` printed `1`.

Step 8 — break it on purpose. Rename the property in the type only:

```bash
sed -i 's/MinutesAway/MinToArrival/g' Stop.Core/Arrival.cs
dotnet test Stop.Core.Tests/Stop.Core.Tests.csproj
```

Observed (exit 1). The `Stack Trace:` block was dropped from the paste below; its first
line only carried my absolute path. The failure message is the part that matters:

```
[xUnit.net 00:00:00.32]     Stop.Core.Tests.ArrivalTests.Arrival_json_keeps_the_agreed_field_names [FAIL]
  Failed Stop.Core.Tests.ArrivalTests.Arrival_json_keeps_the_agreed_field_names [47 ms]
  Error Message:
   Assert.Contains() Failure: Sub-string not found
String:    "{"Route":"42","MinToArrival":7}"
Not found: ""MinutesAway""

Failed!  - Failed:     1, Passed:     1, Skipped:     0, Total:     2, Duration: 91 ms - Stop.Core.Tests.dll (net10.0)
```

Read the two counts. One test failed. One test **passed** — the roundtrip test, because a
record that roundtrips under its own new name is self-consistent. The payload's shape
changed and the roundtrip never noticed. That is why you write both.

Step 9 — put it back:

```bash
sed -i 's/MinToArrival/MinutesAway/g' Stop.Core/Arrival.cs
dotnet test Stop.Core.Tests/Stop.Core.Tests.csproj
```

Observed (exit 0): `Passed! ... Passed: 2`.

None of the output above carries my directory, so your run should match line for line.
Timings differ: the `[xUnit.net 00:00:00.32]` prefix, the `[47 ms]` figures, and the
`Duration:` at the end are machine speed, not results.

---

## Say it out loud

Answer these without notes, the way you would in an interview:

1. The contract page and the C# type both describe the same message. Which one does the
   compiler check, which one does a person read, and what proves they still agree?
2. Why does a message need a version number? Two programs upgrade on different days —
   what exactly goes wrong without one?
3. You rename a field in the record. The roundtrip test still passes. Why does it pass,
   and which test catches the rename? Point at the measured output above.
4. Your teammate says "`dotnet test` exits 0, so we have proof." What did I measure this
   session that disproves that?
5. AGENTS.md says changing the contract needs a decision record and a version bump. What
   does `docs/contract/v1.md` have to do with that rule?

---

## Assignment

Write the first real message contract. This is your code. Type it, run it, be able to
explain every line.

### Requirements

1. **The contract page.** `docs/contract/v1.md`, built to the structure in
   [The contract is a versioned page](#the-contract-is-a-versioned-page): a plain
   `Version: 1` line on its own line, one section per message with direction and a field
   table, at least one example payload inside a JSON code fence, and the closing note about
   decision records and version bumps. The content — which message, which fields — is
   yours to choose.
2. **The first real message type.** A `public` type in `src/Contracts`, in its own file,
   that is not named `Class1`. Its JSON form must match the contract page exactly: same
   field names, same types. Serialize it with `System.Text.Json` — no new NuGet package
   for this (measured: it ships with the framework).
3. **Real tests in `Contracts.Tests`.** At least: one roundtrip test (serialize,
   deserialize, assert equal) and one wire-shape test (the JSON text carries the field
   names the page documents). Every `[Fact]` carries at least one `Assert`. Test names say
   what they prove.
4. **A real test in `FleetManager.Tests`.** Decision record 01 §10: each test project holds
   at least one real test of something real. Pick one of the two options in
   [A test that can fail](#a-test-that-can-fail) — run the skeleton, or test a tiny pure
   function — and say out loud why you picked it.
5. **`FleetManager` starts, logs, and exits cleanly.** Replace the template `Hello, World!`
   with a minimal line saying it started, and exit 0. Keep it minimal: no logging
   framework, no configuration file, no topics or timeouts — none of that exists yet, and
   inventing it now would put a fourth concept in this lesson. M2 introduces configuration
   with decision record 02.
6. **Delete the placeholders.** `src/Contracts/Class1.cs`, both `UnitTest1.cs` files (the
   empty `Test1()` with them), and the template lines in `src/FleetManager/Program.cs`.
   This is the list decision record 01a moved into this lesson.
7. **The graph does not change.** No new project, no new edge, no `.cs` file copied between
   projects. Exactly four `ProjectReference` lines in `src/`, the same four as today.

No MQTT, no topics, no endpoints, no broker: decision record 02 territory, M2.

### Acceptance criteria (all command-checkable)

From the repository root. Read every exit code directly, never through a pipe.

```bash
# Contract page exists, and its version line matches its own filename
DOC=docs/contract/v1.md
test -f "$DOC"                                   # exit 0
V=$(basename "$DOC" .md); V=${V#v}               # V comes from the filename
grep -qE "^Version: ${V}\$" "$DOC"               # exit 0 — content matches filename
grep -q '```json' "$DOC"                         # exit 0 — an example payload is there
```

Measured today: `test -f` exits 1, because the page does not exist yet, and the two greps
exit **2** with `No such file or directory` (grep's exit code for a missing file, not 1 for
"no match"). Run them again after you create the page.

```bash
# The placeholders are gone — all four print nothing
find src -name 'Class1.cs' -o -name 'UnitTest1.cs'
grep -rn 'Test1' src --include='*.cs'
grep -rn 'Hello, World!' src --include='*.cs'
for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do grep -q 'Assert' "$f" || echo "$f"; done
```

Today the first prints 3 paths, the second 4 lines, the third 1 line, the fourth 2 paths.
All four are measured above; all four must be empty afterwards. (`find` and `grep` exit 0
or 1 by match, so judge these by output, not by exit code.)

```bash
# The type and the JSON roundtrip are where they belong
find src/Contracts -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -not -name 'Class1.cs'
                                                              # prints at least one path
grep -rqE 'public[[:space:]]' src/Contracts --include='*.cs'   # exit 0: something public lives there
grep -rq 'JsonSerializer' src --include='*.cs'                 # exit 0: a JSON roundtrip test exists
```

Two measured cautions here. First, the `find` must exclude `bin/` and `obj/`: today
`src/Contracts/obj/` already holds three generated `.cs` files, so without the exclusions
the check prints files you did not write (measured). Second, `JsonSerializer` exits 1
today, and the `public` check passes today only because `Class1.cs` is still there — both
count only together with the `find` above.

From `src/` (`mktemp` makes a fresh temporary file to hold the output, so no exit code is
read through a pipe):

```bash
dotnet build                                    # exit 0

T=$(mktemp)
dotnet test > "$T" 2>&1                         # exit 0 — read $? here, before any grep
grep -c 'Passed!' "$T"                          # prints 2: one line per test project
grep -c 'No test is available' "$T"             # prints 0
rm -f "$T"

L=$(mktemp)
dotnet test --list-tests > "$L" 2>&1            # exit 0
grep -c 'Test1$' "$L"                           # prints 0: no placeholder names
rm -f "$L"
```

How to tell a real test from a placeholder, in three independent checks:

1. **Every test file asserts.** The `[Fact]`-with-no-`Assert` loop above prints nothing.
2. **Both test DLLs ran a test.** `grep -c 'Passed!'` prints 2. Measured: a test DLL with
   no tests prints `No test is available ...` instead of a `Passed!` line, and `dotnet test`
   **still exits 0** — so exit 0 alone proves nothing, and `grep -c 'No test is available'`
   printing 0 is the second half of the proof.
3. **No test is called `Test1`.** `--list-tests` prints the names, and the grep prints 0.

(`grep -c` prints `0` and exits 1 on zero matches. Read the printed number.)

The graph, still from `src/`:

```bash
grep -rn "ProjectReference" --include=*.csproj . | wc -l    # prints 4
find . -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -exec md5sum {} + | sort | uniq -w32 -D
                                                             # prints nothing
```

And the skeleton runs:

```bash
F=$(mktemp)
dotnet run --project FleetManager/FleetManager.csproj > "$F" 2>&1   # exit 0
test -s "$F"                                                        # exit 0: it printed something
! grep -q 'Hello, World!' "$F"                                      # exit 0: template string gone
rm -f "$F"
```

Measured: `dotnet run` returns the app's own exit code (a scratch app returning 3 gave
exit 3), so this check is the app's exit code, not the launcher's.

### Stretch goal

Make the page and the type check each other: add a test that reads the example payload out
of `docs/contract/v1.md` (the JSON code fence) and deserializes it into your
message type. Then break it on purpose.

Accept: `dotnet test` exits 0 with the page as written; after you edit the example's field
name to something the type does not have, `dotnet test` exits 1; after you revert, exit 0
again.

Say out loud while you build it: how does the test find `docs/contract/` from inside
`bin/`, and what breaks if the folder moves? That is a real question a reviewer may ask.

### Not in this assignment

MQTT, topics, endpoints, brokers (decision record 02, M2). The database (record 03, M3).
`ShuttleSim` and its test project (M1). Docker and the fresh-container build — **lesson 03**,
the piece that closes M0.
