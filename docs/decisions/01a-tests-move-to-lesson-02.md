# Decision 01a: The real tests move from lesson 01 to lesson 02

**Date:** 2026-10-07
**Milestone:** M0 (Repo and tooling)
**Status:** Accepted. Owner approved this addendum on 2026-10-07.
**Amends:** [decision record 01, section 10](01-language-and-layout.md). This is an addendum, not a rewrite. Record 01 is unchanged on disk. Where record 01 §10 and this record disagree, this record is the current plan.
**Replaced by:** any later record that moves the tests a second time must say so and link back here. Records are append-only.

---

## 1. What changed

Record 01 §10 says:

> Two test projects exist at M0, so "test projects" (plural, per the spec) is true.
> Each holds at least one real test of something real.
> Exactly which tests is lesson 01's assignment, not this record.

The plan changed after that record was filed. The new plan:

- **Lesson 01** (project references and the .NET skeleton): `dotnet test` must run and pass
  with both test projects wired. The xUnit template's `UnitTest1.cs` stays in place. It is a
  flagged placeholder, not a real test.
- **Lesson 02** (the first real message type): the message type lands in `Contracts`, both
  test projects get real tests of it, and the placeholder files are deleted there.

M0 does not change. The spec's M0 "done when" still requires one real message type defined
and unit tested. Lesson 02 delivers that, plus the Docker work. M0 was never done after
lesson 01.

The plan first appeared in writing in the lesson note
[docs/lessons/01-dotnet-solution.md, section "Note: the tests moved to lesson 02"](../lessons/01-dotnet-solution.md).
That note stays as written. This record is the log entry the note was missing.

---

## 2. Why this record exists

Review 01 raised this as item **C1**: a plan change lived only in a lesson, while
`docs/decisions/` still said the old plan. Two documents disagreed, and only one of them was
in the append-only log.

AGENTS.md is explicit:

> **Logs are append-only.** `docs/decisions/` entries are never rewritten. If a decision is
> replaced, add a new entry that references the old one.

So the fix is a new entry that points at record 01. Not an edit to record 01. This file is
that entry.

---

## 3. Why the plan changed

Three reasons, in order of weight.

### Reason 1: one concept per lesson

AGENTS.md caps a lesson at **3 new concepts**. Lesson 01 already teaches one, and it says so
at the top: project references. On top of that sits the whole scaffold — solution, four
projects, four edges, the pin, the four acceptance commands.

Adding "design the first message type and write real tests of it" would put a second idea
into the same sitting. The scaffold is long already. Splitting keeps lesson 01 finishable in
one sitting and keeps its one concept honest.

### Reason 2: there was nothing real to test yet

A real test needs a real thing to test. At lesson 01 time the contract has no message types
in it. Record 01 §10 knew this and deliberately left the question open ("Exactly which tests
is lesson 01's assignment, not this record").

So the tests and the message type move together to lesson 02. One lesson, one idea: here is
the first message, here is the test that proves it round-trips.

The placeholder stays until then, and it is flagged by file name so nobody mistakes a green
run for proof. Lesson 01 measured the trap: an empty test body still prints
`Passed! ... Total: 1`. Review 01's S2 says the same thing — an empty test "passes for the
wrong reason."

### Reason 3: nothing in M0 is lost

M0 closes when its "done when" criteria are met, not when lesson 01 ends. The spec needs the
real message type and its tests before M0 closes. Lesson 02 already owns the message type
(lesson 01 says so in "Where this fits"). Moving the tests to the same lesson keeps them
inside M0 and keeps the review gate honest: after lesson 02, a green `dotnet test` means
something.

---

## 4. Rejected options

| Option | Why rejected |
|---|---|
| Keep the real tests in lesson 01, as record 01 §10 was written | Lesson 01 would carry two ideas (project references, plus the first contract message and its tests) and run past one sitting. AGENTS.md caps lessons at 3 concepts; the scaffold already fills the time. |
| Move the tests to lesson 03 or later | Review 01's S2: until the placeholders go, every green `dotnet test` is worth nothing, and "M0 cannot close with them in place." The later they move, the longer the M0 gate rests on a green light that means nothing. |
| Edit record 01 §10 so the log reads the new plan | Forbidden. AGENTS.md: `docs/decisions/` entries are never rewritten. An edited log hides the history of a decision, which is the whole point of keeping one. |

---

## 5. What would change this choice

- If lesson 02 turns out to be too big for one sitting, split it. Move the overflow with a
  new addendum (next suffix: `01b`) linked here, so the log stays the source of the plan.
- If the owner wants a real test *before* the message type exists — for example a test that
  `FleetManager` starts, logs, and exits cleanly, which is record 01 §10's other half — a new
  record can pull that test into an earlier lesson.
- If the spec's M0 "done when" changes, revisit which lesson closes M0.

---

## 6. Why this file is named `01a` and not `02`

The spec already spends numbers 02 and 03:

- `spec/shuttle-spec.md`, Constraints: "Messages travel over MQTT. The mock WMS talks to the
  fleet manager over REST. **Decision record 02** confirms the choices."
- `spec/shuttle-spec.md`, Constraints: "Fleet state lives in a SQL database. **Decision
  record 03** picks the database."

Those references must keep pointing at the right files. So:

- A record that **amends** record 01 takes a letter suffix: `01a`, `01b`, …
- `02` stays reserved for the message-transport decision, `03` for the database decision.
- The next **standalone** decision gets `04`.

That is the numbering rule for this directory until a record says otherwise.

---

## 7. Verification table

Everything below I checked by running a command or reading a file in this repository.

| Claim | How checked | Result |
|---|---|---|
| Record 01 §10 assigns the real tests to lesson 01 | read `docs/decisions/01-language-and-layout.md` | "Exactly which tests is lesson 01's assignment, not this record" |
| The plan change lived only in a lesson | read `docs/lessons/01-dotnet-solution.md` | section "Note: the tests moved to lesson 02" |
| Append-only rule and the "add a new entry" fix | read `AGENTS.md`, Rules | "Logs are append-only … add a new entry that references the old one" |
| The spec reserves 02 for transport and 03 for the database | read `spec/shuttle-spec.md`, Constraints | "Decision record 02 confirms the choices"; "Decision record 03 picks the database" |
| A lesson may carry at most 3 new concepts | read `AGENTS.md`, Roles table | "at most 3 new concepts per lesson" |
| M0 needs one real message type, unit tested | read `spec/shuttle-spec.md`, M0 | "one real message type defined and unit tested" |
| An empty template test reports a pass | lesson 01 verification table (measured in `/tmp/opencode`) | body empty; still `Passed! ... Total: 1` |
| The reviewer asked for this record | read `docs/reviews/01-dotnet-solution.md` | item C1: "A two-line addendum record referencing 01 would make the log authoritative" |
| M0 also needs Docker and a fresh-container build before it closes | read `docs/reviews/01-dotnet-solution.md`, end-product check | "M0 remains open pending lessons 02–03" |

Not yet verified:

- Whether lesson 02's assignment, when written, keeps to one sitting. Lesson 02 does not
  exist yet.
- Whether the placeholder tests are actually gone after lesson 02. Nothing has been built
  for that yet.

---

## 8. What this record unblocks

- Lesson 02's assignment can carry the first message type and both real tests without
  contradicting the log.
- Review 01's C1 is answered: the change now lives in `docs/decisions/`.
- The reviewer checks M0's test rule against this record plus the spec's M0 "done when",
  not against record 01 §10 alone.
