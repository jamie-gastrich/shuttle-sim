# Answers for lesson 22 — the explain-it questions and the deferred notes

**Milestone:** M6 (WMS integration) · **Supplies:** [review 22](../reviews/22-order-state-ladder.md)'s deferred items — the six explain-it questions, the five "study these" lines, and the mutation (e)/(f) real-WMS notes
**Professor-supplied, 2026-10-20.** The owner deferred this material by explicit decision, recorded in review 22's "Owner's deferral decision" section. These are model answers. Use them to check yourself, not to memorize: cover the file, answer each question out loud, then read.

**How to use this file.** The questions live in [review 22](../reviews/22-order-state-ladder.md), unchanged, in the "Explain-it check" section. Answer first. Every fact below was checked in this repo today (2026-10-20); line numbers come from the current files.

---

## Block 1 — the six explain-it questions

### 1. Check order: what does the mutated code do with `"LOCAL-7"`?

Question (review 22, item 1): why does mutation (d) — the task lookup moved before the order lookup — stay green on the first half of the no-order test, and what would throw in production?

In the mutated code, `_tasks.Get("LOCAL-7")` runs first. `"LOCAL-7"` **is** in the task board — the test added it, one line above (`WmsOrderLadderTests.cs:95`). So `Get` succeeds and returns the task. The order lookup then finds no order and returns `NoOrder`. The first half stays green, because its outcome is still `NoOrder`. (With the pre-fix discarded value at old line 96 it could not have caught anything anyway; only the second half bites.)

`"never-existed"` is in **neither** board. The mutated `_tasks.Get` throws `KeyNotFoundException` with the message `unknown task never-existed` (`TaskBoard.cs:20-21`) instead of returning `NoOrder`.

In production, that is the exception a local-move task id would hit if the two lines were swapped: `KeyNotFoundException` escaping the sync. Nothing catches it. Measured today: the only catches around the serve loops are the two `OperationCanceledException` blocks at `Program.cs:216` and `:224`; the only domain catch in the loop path is `ShuttleRejectedException` at `RunLoop.cs:139`. So the throw unwinds the serve loop and the fleet manager dies.

Order-first is the fix: every missing id becomes a clean no-op. The throwing lookup only runs when an order proved the pair should exist — and if the task is then missing, the throw is the pair backstop (record 30 §5), and it stays loud.

### 2. Literals vs constants: what does the literal pin that the constant cannot?

Question (review 22, item 2): why is `Assert.Equal("in_progress", …)` stricter than `Assert.Equal(WmsOrderStates.InProgress, …)`, and defend or condemn `Rank`'s literal arms.

`Assert.Equal(WmsOrderStates.InProgress, WmsOrderLadder.ForTask(TaskState.InProgress))` compares the constant **to itself**. If someone changes the constant's *value* — say `"in_progress"` becomes `"moving"` — both sides move together. The test stays green. The wire word changed under the WMS's feet with no red test.

The literal `"in_progress"` fixes what the wire must carry, whatever the constants say. It pins the constant's value *and* the ladder's output in one assertion.

**`Rank`'s literal arms (`WmsOrderLadder.cs:17-20`): smell, not bug.** Defend: a *value* rename of a constant makes `Rank`'s `_ =>` arm throw `ArgumentOutOfRangeException` at runtime for the new word — but the ladder tests' literal pins go red first, so tests catch it before production does. A *name* rename does not break `Rank`'s build at all (the value is unchanged), which is harmless. Condemn: constant patterns would compile-fail on a name rename — and they compile fine; measured in scratch today: `Words.Received => 0` builds and runs. Compile-time beats runtime. Verdict: align when the file is next touched. The cancel slice must touch it anyway: `Rank` has no `cancelled` arm, so the `_ =>` throws for a cancelled order (measured — the file has five words in `WmsOrderStates`, four in `Rank`).

### 3. Why `Assigned` maps to `received`, not `in_progress`

Question (review 22, item 3): what would the WMS have been told on the M5 fault-before-movement path, if `Assigned` mapped to `in_progress`?

Trace it. Assign → sync → the order becomes `in_progress` → (once status push is live) the sender pushes `in_progress` to the WMS. A pallet that has not moved is announced as moving. That word cannot be unsaid.

Then M5's designed recovery path runs: the shuttle faults before movement, so `Requeue` fires (`Assigned → Queued`, record 25). Sync again: the ladder's word for `Queued` is `received` (rank 0), stored is `in_progress` (rank 1). Not equal. Not above. `OrderRejectedException` — thrown on a **legal path**, at the transition line. The backstop fires on the recovery path itself. The serve loop dies, or, if someone "fixes" it by catching, the order freezes at the wrong word forever.

Sharing one rung is what saves this. `Queued` and `Assigned` both map to `received`, so requeue means target == stored → `AlreadyThere` → silence. The task ladder's one backwards step is invisible to the order. That is the promise "the order ladder only ever climbs" made cheap.

### 4. No sequence reaches the refusal — prove it — and why `OrderRejectedException`?

Question (review 22, item 4): name a sequence that reaches the refused branch using only the public `FleetTask` API (record 30 says none exists), then explain the exception type.

No sequence exists. Five guards, all read today:

| Guard | Line | Rule |
|---|---|---|
| `Assign` | `FleetTask.cs:31` | only from `Queued` |
| `Start` | `:44` | only from `Assigned` |
| `Complete` | `:53` | only from `InProgress` |
| `Fail` | `:63` | rejects `Done` and `Failed` |
| `Requeue` | `:93` | only from `Assigned` |

The refusal needs target ≠ stored **and** rank(target) ≤ rank(stored). Two ways to get there:

- **The task walked down past a shared row.** The only down-move is `Assigned → Queued`, and both share the `received` rung. The equality check catches it first; rank never runs.
- **Equal rank, different word.** The pair is `done` ↔ `failed` (both rung 2). Reaching it needs a task flip between `Done` and `Failed`: `Fail` refuses a `Done` task, `Complete` refuses anything but `InProgress`. No path.

Hence mutation (f) survives 45/45. It is a backstop, like `TaskBoard.Add`'s duplicate check.

**Why not `TaskRejectedException`:** record 12 §3 says the assignment loop catches `TaskRejectedException` and turns a refused move into a `Fail`. That is an operational handler. A ladder desync is a bug in *our* mapping, not a task refusing work. Wearing the task's name, the bug would be caught and converted into a `Fail` — disguised as an operation. (Measured today: nothing in `src/` catches `TaskRejectedException` at all yet; the rule is fixed for the execution slice to inherit.)

**What the WMS waits for, if the exception is swallowed:** the sync throws *before* the write. No write → no `Advanced` → no `MarkPending` → no push. The pallet moved; the WMS never hears `in_progress` or `done`. Ever.

### 5. Why three outcomes, not `void` or `bool` — and why stored?

Question (review 22, item 5): what would a `void` sync lose, a `bool` lose, and why does storing the state make the `Advanced` moment exist?

A `void` sync gives the caller no signal. The call site must then mark **always** (the WMS gets a POST on every transition line, including every `AlreadyThere` — silence turned into chatter) or **never** (nothing is ever pushed). Both are wrong.

A `bool` collapses three meanings into two. It can say "changed / not changed" but not "no order owns this task." Those are different for the caller: a local-move path must know there was no order, not just "false." The only value that means *owed* is `Advanced`; a bool that maps `NoOrder` → false hides which of the two silences happened. Three outcomes mirrors `SubmitOutcome` (record 28's `Created`/`AlreadyThere`/`Conflict`) — one outcome vocabulary per seam.

**Stored, not derived** (record 30 §3's Decision B) is what makes the split exist at all. The sync *replaces* the stored record and reports `Advanced` on the write, so the moment of change is a value the caller receives. A derived state never changes; there is no moment. The sender would have to poll and compare — a trigger wearing a disguise.

### 6. Where does the first production call go, and what breaks if you batch?

Question (review 22, item 6): where does the first production `SyncFromTask` land, and what breaks concretely if the execution slice batches syncs?

The first production call lands in the **execution slice**: after each task transition — `AssignNext`/`Start`/`Complete`/`Fail`/`Requeue` — on the very next line, per record 30 §4's fixed rule. After this lesson's slice, the line after that is `outbox.NoteSync(taskId, outcome)`. The dispatcher call sites are the ones the execution slice wires into `RunServeAsync`.

If the slice batches syncs per tick instead:

1. **The one-line window widens to the batch.** A crash mid-batch leaves tasks moved and orders stale. After a restart the boards are gone together (record 29 §5), so there is no half-state — but the promise the WMS was given ("the word follows the task, one line later") becomes "eventually, maybe."
2. **With persistence** (record 29 §7, same slice), a crash mid-batch leaves the SQL task rows ahead of the SQL order rows, and nothing re-runs the sync on restore. Completed pallets the WMS never hears about. M6's "orders flow … and back" fails on the "back."
3. **Batching buys nothing.** The sync is in-memory — measured: no I/O in `WmsOrderBoard` — and `MarkPending` is a `HashSet` add. Record 30 §4 already said it: "there is no reason to batch it."

---

## Block 2 — the "study these" lines

### `WmsOrderLadderTests.cs:96`, the old discarded value

The line was `board.SyncFromTask("LOCAL-7");` because that is the lazy first draft: the call compiles, the test passes, nothing pins the outcome. A sync that returned `AlreadyThere` for an orderless task would pass. The test's *name* says the call "returns NoOrder"; the body did not check.

Where it came from: writing the call is easier than asserting it. What to change it to: `Assert.Equal(OrderSyncOutcome.NoOrder, board.SyncFromTask("LOCAL-7"));` — which is exactly what commit `8b9e137` did (review 22, follow-up F3). Lesson: whenever a method's name claims what it returns, pin the return value.

### `WmsOrderLadder.cs:17-20`, `Rank`'s literal arms

Answered in Block 1, item 2: smell, not bug; tests catch a value rename first; constant patterns compile (measured today) and would catch a name rename at build time; align when the file is next touched — the cancel slice must touch it anyway for the missing `cancelled` arm.

### `WmsOrderBoard.cs:60-61`, `<=` and not `<`

With `<` alone, equal rank with a *different* word slips through the rank check. The equality check only catches identical words. The specific pair: `done` ↔ `failed`, both rung 2. A `done`→`failed` write would then be stored.

Which M5 path makes that pair reachable: **none today.** `Fail` refuses a `Done` task (`FleetTask.cs:63`); `Complete` refuses anything but `InProgress` (`:53`). The M5 path you might suspect — reassign (record 25) — stops at `Assigned`, and `Assigned`'s word equals `Queued`'s, so it is caught by the **equality** check, not by rank at all. So `<=` is a backstop for a pair no current path reaches. Mutation (f) surviving is the proof. If a future slice reopens finished work, `<=` is what refuses the rewrite until the ladder itself is deliberately extended.

### `WmsOrderBoard.cs:10`, the 7-space continuation

The house continuation width is **8** (review 22, issue 5). This shape keeps surviving because the review named the constructor line's stray leading space — which was fixed — while the continuation line kept its dedented shape. Editors keep existing indentation when you edit the line above. It is copy-adapt residue: the `=>`-ctor shape was copied from somewhere that indented differently, and each fix touches only the flagged line.

Fix it properly the next time the file is touched: re-indent the whole statement, not the line the review named.

### `WmsOrderLadderTests.cs:11-16`, why the compiler accepts `order.State`

`Assert.NotNull` in xUnit 2.9.3 carries a `[NotNull]` post-condition annotation. After the call, the compiler treats `order` as non-null. Measured today in a scratch project: the deref after `Assert.NotNull` produced **no** warning; the same deref without it produced:

```text
warning CS8602: Dereference of a possibly null reference.
```

The build still succeeded — RC 0, 1 Warning, 0 Errors — because this repo sets no warnings-as-errors (measured: no `TreatWarningsAsErrors` in any `.csproj`).

Delete the `Assert.NotNull` line and you get that warning, plus at runtime an `NullReferenceException` at the `.State` read if the impossible ever happens — instead of a clean assert failure pointing at the real problem. Today `TryGet` returning true with a null value is impossible; the assert is belt-and-braces, and it is also what keeps the helper warning-free.

---

## Block 3 — the mutation (e)/(f) real-WMS notes

Required by review 22, issue 2. Supplied here per the owner's deferral decision, in the professor's words. When you run the mutations yourself, write your own version — that was the exercise.

### Mutation (e): a lost update you can see

Delete `lock (_gate)` from `SyncFromTask` (and `Submit`, for the same disease). Two things go wrong, and a real WMS sees both.

**First, the dictionary can corrupt.** Measured today on the outbox's identical shape: 8 threads marking and claiming one shared `HashSet`, no lock — **3 of 3 runs crashed**, with `IndexOutOfRangeException` or `InvalidOperationException: … corrupted its state` (which one varies by run). On the transition line, that crash kills the serve loop, and *every* order stops getting words. The whole fleet goes mute at once.

**Second, the quieter version: the word moves backwards on its own.** `SyncFromTask` is check-then-write: read the stored word, compare, write. Two threads syncing the same order while its task moves twice (say a retry and the normal path racing) can interleave as: A reads `received`, computes `in_progress`; B reads `received`, computes `done` and writes; A writes `in_progress` **on top of** `done`. The board now says `in_progress` for a finished task. The pusher reads it at send time — this lesson's rule — and tells the WMS the order went back to `in_progress` after `done`. The refusal that is supposed to make backwards writes impossible is itself inside the check-then-write; without the lock, it refuses nothing.

So the lock is not a performance detail. It is part of the ladder's promise that the word only climbs. And the WMS's view: an order that was acknowledged with a `201`, then went quiet, then lied about where it is — a lost update you can see.

### Mutation (f): a word moving backwards you cannot unsay

Delete the backwards refusal (write the target even when rank says no). Today this changes nothing measurable — Block 1, item 4 proved no legal path reaches the branch. That is what "backstop" means: it exists for the day the rule is broken by a *future* edit — someone adds a reopen path, edits the ladder table, mis-ranks a word in `Rank`.

Without the refusal, the moment that bug exists, the sync writes the downgrade — and this lesson's sender pushes it on the next tick. The WMS operator sees `done`, closes the order, frees the lane, the next system books the pallet away… then `in_progress` reappears. Or a failed order flips to `done` and inventory is decremented for a pallet that was never put away.

You cannot unsend it. At-least-once delivery means the first wrong word has already been acted on. Worse than the single lie: the ladder's promise — "this stream only ever climbs" — is what made every *earlier* message trustworthy. One backwards word makes every prior update suspect. "Was the `done` I acted on real?" Trust in the stream dies even though only one word was wrong.

That is why the refusal throws instead of returning a quiet outcome. A backstop you cannot hear is a backstop that does not exist.

---

## On the deferred stretch-probe evidence (review 22, issue 3)

The owner's own copy of the 8-thread probe output is still theirs to produce — that was the stretch goal, and it remains owed when the stretch is run. The professor's measured evidence for the **same shape on this lesson's outbox** (8 threads marking, one claimer draining, 3 runs each way) is in [lesson 23](23-status-push.md)'s verification table and stretch section, dated 2026-10-20: with the lock, 3/3 rounds claimed exactly 8000; without, 3/3 crashed (`IndexOutOfRangeException` twice, `InvalidOperationException` once, RC 134). Run your own when you do the stretch. Keep your numbers.
