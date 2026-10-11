# Decision 12: Blank inputs and the two error categories in `FleetTask`

**Date:** 2026-10-11
**Milestone:** M3 (Fleet manager core) — a small append-only clarification of [record 11](11-task-lifecycle-and-queue.md).
**Status:** Accepted. Written to fix review 07's lesson defects L2 and C1 ("blank" undefined; the blank-input exception type open). It does **not** change record 11's states, transitions, or queue; it pins two facts record 11 left open.
**Supersedes:** none. Extends record 11 §5 and §7.
**Replaced by:** any later record that changes the blank-input rule, the exception types, or the argument order. Lesson 08 (assignment + SQL) links here when it catches task errors.
**Wire contract:** unchanged. This is pure in-memory code; no message changes. `docs/contract/v2.md` stays `Version: 2`.

---

## 1. What was open

Record 11 §7 lists two blank-input guards — "`shuttleId` is not blank" and "`reason` is not blank" — and says:

> *"A wrong call throws `TaskRejectedException`."*

It does not say what "blank" means, and it does not name the exception a blank guard throws. Lesson 07's first draft read that sentence broadly and showed `TaskRejectedException("shuttle id required")`, while the implemented code throws `ArgumentException`. Review 07 (C1, L2) asked for a deliberate choice, recorded.

---

## 2. Decision

1. **Blank means `string.IsNullOrWhiteSpace`.** The empty string `""` and a string of only whitespace (`"   "`) both count as blank. This applies to the constructor's `taskId` and `toLane`, `Assign`'s `shuttleId`, and `Fail`'s `reason`.
2. **A blank input throws `ArgumentException`.** The constructor passes the parameter name as the second argument: `new ArgumentException("TaskId cannot be empty.", nameof(taskId))`. `Assign` (blank `shuttleId`) and `Fail` (blank `reason`) throw the same type.
3. **A move from the wrong state throws `TaskRejectedException`.** So does a backwards clock (`EnsureInOrder`). That is the domain's named refusal.

The code that satisfies this record uses `IsNullOrWhiteSpace` for all four guards and `ArgumentException` for the three blank-input guards (constructor, `Assign`, `Fail`).

---

## 3. Why two categories

- **A blank argument is a caller bug.** The value handed to the method is invalid. .NET reserves `ArgumentException`/`ArgumentNullException` for exactly that, and support tooling reads `ParamName` to find the bad call. There is no task state to blame.
- **A wrong-state move is a domain-rule violation.** The task's place in its lifecycle does not allow the move. `TaskRejectedException` is the domain's named refusal, matching `ShuttleRejectedException` from record 06.
- **Keeping them apart keeps lesson 08's loop honest.** The assignment loop will catch `TaskRejectedException` to map a refused move onto `Fail`. A blank id — a bug in the manager's own code — should surface as an `ArgumentException`, not be swallowed as if the task had refused the work.

---

## 4. Rejected options

- **`TaskRejectedException` for blank inputs** (what lesson 07's first draft showed). It blurs "I called this wrong" with "the task refused the move," and it makes the wrong-call tests assert the wrong thing.
- **Leaving blank undefined (only `IsNullOrEmpty`).** A whitespace-only id (`"   "`) passes, then becomes an apparently empty database primary key (lesson 08) and an empty dashboard label (M7) — exactly the failure review 07's S1 probe showed.
- **One exception type for everything.** A single `catch` site could not tell a caller bug from a domain refusal. Grouping by *category* (bad input vs. bad transition) keeps them apart without inventing a type per reason, which record 11 already rejected.

---

## 5. What would change this choice

- **Lesson 08 decides all task errors must funnel through one catch site** → a new record links here and defines the wrapper.
- **A blank id must be tolerated as a real value** (for example, tasks with auto-assigned ids) → a new record, because `IsNullOrWhiteSpace` would refuse it.
- **The `.NET` guidance or the house style moves to `ArgumentNullException` for null and `ArgumentException` only for shape** → a new record; the teaching distinction above is unchanged.

---

## 6. What this record unblocks

- Lesson 07 can state one rule for "blank" and one exception type per guard, so a whitespace-only id cannot slip past on a literal reading of "empty."
- Lesson 08 starts from a decided error taxonomy: catch `TaskRejectedException` for domain refusals, let `ArgumentException` bubble for programmer error.

---

## 7. Verification

Read-only on 2026-10-11, against the owner's code at `eed2b4c`:

- `src/FleetCore/FleetTask.cs` uses `string.IsNullOrWhiteSpace` for all four blank-input guards (lines 8, 10, 29, 61) and throws `ArgumentException` for the three of them. The constructor passes `nameof(taskId)` and `nameof(toLane)` as the second argument (lines 9, 11).
- `src/FleetCore.Tests/TaskLifecycleTests.cs` asserts `Assert.Throws<ArgumentException>` for the blank shuttle id and the blank reason (lines 122, 132).
- Not covered here: the wire/spelling rule (record 11 §6), which lesson 07's Phase 3b content gate now tests.

