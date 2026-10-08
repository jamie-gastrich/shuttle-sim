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
- The message is not open for design. The assignment fixes it: `ShuttleStatus`, five
  fields, in the field table under Requirement 1. Start with the page — write each meaning
  and the example payload in your own words — then make the type match the page.
- Do not "improve" the message by renaming or adding fields. The five fields are the
  contract. Changing any one of them is a version bump with a decision record. That rule
  is the lesson; a surprise sixth field is also a surprise reviewer question.

---

## Tier 2 — direction

- Order of work, and why this order:

  1. `docs/contract/v1.md` — write the page for `ShuttleStatus` from the assignment's
     field table. This is the agreement.
  2. `src/Contracts/` — one `public` type whose JSON form matches the page exactly,
     including casing.
  3. `src/Contracts.Tests/` — the roundtrip test and the wire-shape test.
  4. `src/FleetManager/Program.cs` — replace the template lines with one start line, exit 0.
  5. `src/FleetManager.Tests/` — one real test, from one of the two options.
  6. Delete `Class1.cs` and both `UnitTest1.cs`.
  7. Run the acceptance block, one command at a time, reading each exit code directly.

  Writing the page first means the type and the tests only have to match one thing: the
  page. The page only has to match one thing: the assignment's field table.

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

Copy-paste shapes. Every block below is a **template**: the shape is given, the typing is
not — type every line yourself. The message is already fixed by the assignment:
`ShuttleStatus`, with the five fields in its field table, so the blocks below use the real
names. What is still yours to write: the page's meaning sentences and example values, the
values your tests build, and the wording of `<your start line>`. Every C# block below was
typed into a scratch project on 2026-10-08 and built as written: `dotnet test` gave
`Passed: 2` for the contract tests, `Passed: 2` for option B's `FleetManager` program with
both test classes, and `Passed: 1` for option A's program with its own test — all exit 0.
So if a build fails, you are fixing a typing slip or a renaming slip — not my syntax.

### The contract page — `docs/contract/v1.md`

The message, its five fields, and the required flags are fixed by the assignment. Write
the meaning sentences and the example values in your own words. Leave the `Version: 1`
line, the three-backtick fence markers, and the section heading exactly as written — the
acceptance greps read those, and I measured a decorated version line (`**Version:** 1`)
fails them.

````
# Message contract v1

Version: 1

## ShuttleStatus

Sent by a shuttle to the fleet manager.

| Field | JSON type | Meaning | Required |
|---|---|---|---|
| `ShuttleId` | string | <your wording of the assignment's meaning> | yes |
| `State` | string | <your wording of the assignment's meaning> | yes |
| `Position` | string | <your wording of the assignment's meaning> | yes |
| `Timestamp` | string | <your wording of the assignment's meaning> | yes |
| `FaultReason` | string | <your wording of the assignment's meaning> | no |

```json
{"ShuttleId":"<your value>","State":"<your value>","Position":"<your value>","Timestamp":"<ISO 8601 UTC>","FaultReason":<null or a reason>}
```

---

## Changing this contract

Changing a message in this file needs a decision record in `docs/decisions/` and a
version bump: the new shape gets a new page (`v2.md`) and `v1.md` stays, so a payload
written for version 1 can still be read.
````

Measured on that skeleton, exactly as printed: `test -f` 0, version grep 0, fence grep 0,
section grep 0, and the assignment's page field loop prints nothing. The `<...>` cells do
not trip any check; they just do not say anything yet.

### The message type — `src/Contracts/ShuttleStatus.cs`

The name and the five fields are fixed by the assignment; their C# types are in the
assignment's table. Type this record in — with your own comments if you want them. The
`DateTime` and `string?` come from the C# column of the assignment's table: `DateTime`
serializes as an ISO 8601 string, and `string?` is how C# says "this field may be empty."

```csharp
namespace Contracts;

// SHAPE — type this in. The assignment fixes the name and the five fields.
public record ShuttleStatus(
    string ShuttleId,
    string State,
    string Position,
    DateTime Timestamp,
    string? FaultReason);
```

The compiler will force it `public` the moment `Contracts.Tests` uses it, but write
`public` yourself.

### The two tests — `src/Contracts.Tests/ShuttleStatusTests.cs`

Both blocks go in **one** file, typed before you run anything. The values below are
examples that compile as written — pick your own shuttle id, lane, state, time, and fault
reason, and keep the field names exactly as they are. The second test must assert **all
five** names: the acceptance block checks that every one of them appears in this file.

```csharp
using System.Text.Json;
using Contracts;

namespace Contracts.Tests;

public class ShuttleStatusTests
{
    [Fact]
    public void ShuttleStatus_survives_a_json_roundtrip()
    {
        var sent = new ShuttleStatus("SH-01", "idle", "A02-L04",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), null);
        var json = JsonSerializer.Serialize(sent);
        var received = JsonSerializer.Deserialize<ShuttleStatus>(json);
        Assert.Equal(sent, received);
    }
```

```csharp
    [Fact]
    public void ShuttleStatus_json_keeps_the_agreed_field_names()
    {
        var json = JsonSerializer.Serialize(new ShuttleStatus("SH-01", "fault", "A02-L04",
            new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc), "pallet not detected"));
        Assert.Contains("\"ShuttleId\"", json);
        Assert.Contains("\"State\"", json);
        Assert.Contains("\"Position\"", json);
        Assert.Contains("\"Timestamp\"", json);
        Assert.Contains("\"FaultReason\"", json);
    }
}
```

First block: the roundtrip — record equality does the comparing. Second: the wire shape —
assert the JSON text carries each name your page documents. You need both; lesson 02
measured a field rename slipping past the roundtrip test. Every `[Fact]` needs at least
one `Assert.` line: `Assert.Equal`, `Assert.Contains`, `Assert.NotEmpty`,
`Assert.DoesNotContain` all count, an empty body does not.

### `FleetManager/Program.cs` — start and exit 0

**You decide:** the wording of your start line. One line, it says what started, it is not
`Hello, World!`. No logging framework, no configuration — those are later lessons.

Option A, the line straight in the file (you test the program by running it):

```csharp
// TEMPLATE — replace the text in quotes with your start line.
Console.WriteLine("<your start line>");
return 0;
```

Option B, the line pulled into a small function (you test the function directly). A type
declaration goes after the statements — the statements are the lines at the top of
`Program.cs` that run in order:

```csharp
// TEMPLATE — replace the text in quotes and the names with yours.
Console.WriteLine(YourStartup.YourStartLine());
return 0;

public static class YourStartup
{
    public static string YourStartLine() => "<your start line>";
}
```

### `FleetManager.Tests` — one real test, pick option A or B

**You decide:** which option, and the test names. Option A, **run the skeleton**:
`Process` starts a second program, `ProcessStartInfo` is the bundle of settings for it.
`AppContext.BaseDirectory` is the folder your test DLL lives in, and the built
`FleetManager.dll` sits beside it because the project reference copies it there. Measured:
the `.dll` plus the two sidecar files `dotnet` needs to run it (`.deps.json`,
`.runtimeconfig.json`) all land there, so `dotnet FleetManager.dll` runs — no extra NuGet
package.

```csharp
using System.Diagnostics;

namespace FleetManager.Tests;

public class StartTests
{
    [Fact]
    public void FleetManager_starts_exits_zero_with_real_output()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "FleetManager.dll");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add(dll);

        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        Assert.Equal(0, p.ExitCode);
        Assert.NotEmpty(output);
        Assert.DoesNotContain("Hello, World!", output);
    }
}
```

Measured: with `Hello, World!` back in `Program.cs` this test **failed** (exit 1,
`Assert.DoesNotContain() Failure: Sub-string found`); with the template start line it
passed (exit 0).

Option B, **test the pure function** — a tiny piece of code that takes input, returns a
value, and touches nothing else. It needs option B's `Program.cs` above, because
`YourStartup` lives there. **You decide:** what you assert about the line.

```csharp
namespace FleetManager.Tests;

public class StartLineTests
{
    [Fact]
    public void YourStartLine_says_what_started()
    {
        var line = YourStartup.YourStartLine();
        Assert.NotEmpty(line); // TEMPLATE: add what you want to prove about the line
        Assert.DoesNotContain("Hello, World!", line);
    }
}
```

Both options are honest: the first starts the real program, the second tests only the
function. Say out loud which you picked and what it does not cover.

### Acceptance snippets you will retype

Run from the repository root, one line at a time, reading each exit code directly — never
through a pipe.

```bash
# Contract page: file exists, version line matches its filename, example payload is there
DOC=docs/contract/v1.md
test -f "$DOC"; echo $?                                            # 0
V=$(basename "$DOC" .md); V=${V#v}                                 # V comes from the filename
grep -qE "^Version: ${V}\$" "$DOC"; echo $?                        # 0
grep -q '```json' "$DOC"; echo $?                                  # 0
```

```bash
# The fixed message exists, in all three artifacts
grep -q '^## ShuttleStatus$' docs/contract/v1.md; echo $?           # 0
test -f src/Contracts/ShuttleStatus.cs; echo $?                     # 0
grep -rq 'public record ShuttleStatus' src/Contracts --include='*.cs'; echo $?   # 0
test -f src/Contracts.Tests/ShuttleStatusTests.cs; echo $?          # 0
for f in ShuttleId State Position Timestamp FaultReason; do
  grep -q "$f" docs/contract/v1.md || echo "page missing: $f"       # prints nothing
done
for f in ShuttleId State Position Timestamp FaultReason; do
  grep -q "$f" src/Contracts.Tests/ShuttleStatusTests.cs 2>/dev/null || echo "test missing: $f"
done                                                                # prints nothing
```

```bash
# Placeholders are gone — all four print nothing (judge by output, not exit code)
find src -name 'Class1.cs' -o -name 'UnitTest1.cs'
grep -rn 'Test1' src --include='*.cs'
grep -rn 'Hello, World!' src --include='*.cs'
for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do grep -q 'Assert' "$f" || echo "$f"; done
```

```bash
# From src/: build, both test DLLs really ran, no Test1 left
dotnet build                                                        # exit 0
T=$(mktemp); dotnet test > "$T" 2>&1; echo $?                       # exit 0 — read it here
grep -c 'Passed!' "$T"                                              # prints 2
grep -c 'No test is available' "$T"                                 # prints 0
L=$(mktemp); dotnet test --list-tests > "$L" 2>&1; echo $?          # exit 0
grep -c 'Test1$' "$L"                                               # prints 0
rm -f "$T" "$L"
```

```bash
# The skeleton runs
F=$(mktemp)
dotnet run --project FleetManager/FleetManager.csproj > "$F" 2>&1; echo $?   # 0
test -s "$F"; echo $?                                                        # 0: it printed something
! grep -q 'Hello, World!' "$F"; echo $?                                      # 0: template string gone
rm -f "$F"
```

Measured on the templates in a scratch project on 2026-10-08 (not your repo, so your
numbers come from your own run): `Passed!` count `2`, `No test is available` count `0`,
`Test1$` count `0`, `dotnet run` exit `0`, output `<your start line>`. `grep -c` prints
`0` and exits 1 on zero matches — read the printed number.

### Stretch

The example payload is text inside a fence in a plain markdown file. Read
`docs/contract/v1.md` as text, take the text between the fence markers of the `json` code
fence, and run it through `JsonSerializer.Deserialize<ShuttleStatus>(json)`. Say out loud
how your test finds `docs/contract/` from inside `bin/`, and what breaks if the folder
moves.

---

What is deliberately *not* given here: the wording of the page's meaning sentences, the
example payload's values, the values your tests build, which FleetManager test option you
pick, and the wording of your start line. The message itself is no longer open: the
assignment fixes `ShuttleStatus` and its five fields, and the shapes above use those real
names. Type every line yourself, fill every `<...>` with your own words and values, and be
able to explain every line of your version out loud.
