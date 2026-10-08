# Hints for lesson 02: the first message contract

Open a tier only when you are stuck. Stop reading as soon as the tier gets you moving.

---

## Tier 1 — nudge

- Three concepts, three artifacts: a **page** in `docs/contract/`, a **type** in
  `Contracts`, and a **test** that proves they agree. Ask which of the three is missing or
  still a placeholder before you touch the terminal.
- Run the assignment's "placeholders are gone" block first. It prints what to delete today:
  three paths, four lines, one line, two paths. Work until all four print nothing.
- A roundtrip test passes and you still feel it proves nothing? That feeling is the third
  concept. The worked example shows the fix: a second test that asserts the field names in
  the JSON text. Measured: a field rename slipped past the roundtrip test and was caught by
  that one.
- `FleetManager` is almost empty, so "something real to test" feels hard. The lecture names
  two honest options; pick either. Do not invent features just to have something to test.
- If `dotnet test` exits 0 but one `Passed!` line is missing, read the output again — a
  project with no tests prints `No test is available` and still gives exit 0. Measured.
- The version line must be plain: `Version: 1`, on its own line. I measured that
  `**Version:** 1` fails the check. Decoration is not styling here, it is a broken checker.
- Stuck on the message's shape? The contract page comes first. Write the field table for a
  message you actually understand, then make the type match it.

---

## Tier 2 — direction

- Order of work, and why this order:

  1. `docs/contract/v1.md` — decide the message and its fields. This is the agreement.
  2. `src/Contracts/` — one `public` type whose JSON form matches the page exactly,
     including casing.
  3. `src/Contracts.Tests/` — the roundtrip test and the wire-shape test.
  4. `src/FleetManager/Program.cs` — replace the template lines with one start line, exit 0.
  5. `src/FleetManager.Tests/` — one real test, from one of the two options.
  6. Delete `Class1.cs` and both `UnitTest1.cs`.
  7. Run the acceptance block, one command at a time, reading each exit code directly.

  Writing the page first stops you from designing a test and then inventing a message to
  fit it.

- The three artifacts have three jobs: the **page** describes (people read it), the **type**
  enforces (the compiler checks it), the **test** proves (it can fail). If you find yourself
  changing the page to match a failing test, stop and ask which one is actually wrong.
- The graph does not move. This lesson adds no project and no edge, so the reference grep
  must still print exactly four lines. If a fifth appears, you added a project.
- `FleetManager.Tests` options again, with the trade-off spelled out:
  - **Run the skeleton:** the test project already references `FleetManager`, so the built
    app DLL is copied next to your test DLL. The test starts it, reads its exit code and
    its output, and asserts 0 / non-empty / no `Hello, World!`. Heavier, but it tests the
    real program — which is what record 01 §10 asks for.
  - **Extract a pure function:** pull one tiny piece of the startup line out of `Program.cs`
    and assert on the string. Lighter, but `Program.cs` itself goes untested, so say that
    out loud when you justify the choice.
- Two tests, two jobs: roundtrip proves nothing is lost, wire-shape proves the field names
  still match the page. You need both; the worked example's measured rename is the reason.
- `mktemp` in the acceptance block exists so the output of `dotnet test` can be grepped
  without losing the exit code. Run `dotnet test > file 2>&1` and read `$?` before any grep
  touches the file.

---

## Tier 3 — near-solution

Names of the tools, not the code. The message, its fields, the page's wording, and the
tests themselves are yours to write.

- **Message type:** `public record` in its own file under `src/Contracts/`, namespace
  `Contracts`. The compiler will force it `public` the moment `Contracts.Tests` uses it.
- **Roundtrip test:** `System.Text.Json.JsonSerializer.Serialize(value)` then
  `JsonSerializer.Deserialize<T>(json)`, then `Assert.Equal(sent, received)` — record
  equality does the comparing.
- **Wire-shape test:** `JsonSerializer.Serialize(value)` then
  `Assert.Contains("\"FieldName\"", json)` for each name the page documents.
- **Assertion sweep:** every `[Fact]` needs at least one `Assert.` line —
  `Assert.Equal`, `Assert.Contains`, `Assert.True` all count; an empty body does not.
- **Running the skeleton from a test** (option a): `System.Diagnostics.Process` with a
  `ProcessStartInfo` whose `RedirectStandardOutput` is true; the DLL sits next to your test
  assembly, which is `AppContext.BaseDirectory`; join it with `Path.Combine(...,
  "FleetManager.dll")`. Read `p.ExitCode` and the captured output, then assert. Measured in
  scratch: works with no extra NuGet package, and it failed while `Hello, World!` was still
  in `Program.cs`.
- **Contract-page checks:** the assignment prints them. Copy them as written — `V` is read
  from the filename on purpose, so no version number is typed twice.
- **Stretch goal:** the example payload is inside a fenced block in a plain markdown file;
  read the file as text, take the text between the `json` fence markers, and run it through
  `JsonSerializer.Deserialize<T>`. Say out loud how your test finds the repo root.

What is deliberately *not* given here: which message you define, what fields it has, what
the page says beyond its structure, and the text of any test. Those are the assignment, and
you have to be able to explain every line of them out loud.
