# Review 23 — Lesson 23: status push

**Reviewer:** Reviewer agent
**Commits under review:** `a90b3f7` ("M6: Lesson 23 First Attempt") and its fix `cee9694` ("M6: Lesson 23 corrections") · **Lesson material:** `f0c7a2a` ("M6: Lesson 23") and `b45d76d` ("M6: Lesson 23 Update", hints only)
**Lesson:** [23-status-push.md](../lessons/23-status-push.md) · **Decision:** [record 31](../decisions/31-status-push.md)
**Verdict:** **pass** (after the fix commit `cee9694`) — the one blocker is fixed and the fix is behaviour-neutral. Cold build of the committed tree: **0 Warning(s), 0 Error(s)**. **176/176** pure tests, **33/33** acceptance greps, **7/7** named facts, **6/6** "must go red" mutations still die.
**The first-round verdict was _not yet_** (one blocker: 4 × `CS8602` in `src/FleetManager/WmsStatusPusher.cs`). The original review is preserved unchanged below; the **[follow-up section](#follow-up-review--fix-commit-cee9694-parent-a90b3f7)** records the fix verification.

---

## Follow-up review — fix commit `cee9694` (parent `a90b3f7`)

**Verdict: pass.** The blocker is genuinely fixed, the fix is behaviour-neutral, and nothing else in the slice regressed. M6 stays `in progress` — this is one lesson, not the milestone.

**What the fix commit changed.** `git diff --name-only a90b3f7..cee9694` names three files: `docs/reviews/23-status-push.md` (this review, added by the owner), `src/FleetManager/WmsStatusPusher.cs`, and `src/FleetManager.Tests/WmsStatusPusherTests.cs`. **No other `src/` file moved.** The whitespace-ignoring diff of the two source files shows exactly four semantic edits and nothing else:

| File | Semantic change |
|---|---|
| `src/FleetManager/WmsStatusPusher.cs` | the guard widened to `if (!_board.TryGet(orderId, out var order) \|\| order is null)` — the blocker fix |
| | the rejected log line gained the status code: `rejected with {(int)response.StatusCode} {orderId} {order.State}` — issue 2 |
| | indentation normalised to the 4-space house through `SendPendingAsync` and `RunAsync`, `if (` given its space, trailing whitespace dropped, final newline added — issue 3 |
| `src/FleetManager.Tests/WmsStatusPusherTests.cs` | `namespace FleetManager.Tests;` added; the now-redundant `using FleetManager;` removed (the enclosing namespace resolves `WmsStatusPusher` — the build is clean, so the removal is safe) — issue 4 |

### 1. Cold build — the blocker, re-checked the way it hid last time

The first-round blocker was masked by a warm `bin/`/`obj/`: `dotnet build` in this workspace recompiles nothing, so it reported 0 warnings without proving anything. The fix was therefore checked against a **fresh copy of the committed tree with every `bin/` and `obj/` deleted**, never the workspace:

```bash
rm -rf /tmp/opencode/l23fix
mkdir -p /tmp/opencode/l23fix
git archive cee9694 src | tar -x -C /tmp/opencode/l23fix     # 0 bin/obj directories in the copy
cd /tmp/opencode/l23fix/src && dotnet build
```

Exact output:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:22.30
```

Exit code 0. **The four `CS8602` warnings are gone.** A second, independent cold pass — `dotnet build --no-incremental` in the same clean copy — reports the same:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:20.07
```

The workspace build (`cd src && dotnet build`) also reports `0 Warning(s) / 0 Error(s)`. **Blocker 1: FIXED** — the tree is back on the repo's warning-free gate that reviews 21, 22 and the baseline at `f0c7a2a` all held.

### 2. The pure suite

```bash
cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"
```

Exit code 0 — **Failed: 0, Passed: 176, Skipped: 0, Total: 176**. The total is unchanged from the first round, so the fix moved no test.

| Project | Passed | First round |
|---|---|---|
| FleetCore.Tests | 49 | 49 |
| FleetManager.Tests | 59 | 59 |
| ShuttleCore.Tests | 31 | 31 |
| Contracts.Tests | 8 | 8 |
| TrafficCore.Tests | 6 | 6 |
| Messaging.Tests | 10 | 10 |
| MqttMessaging.Tests | 3 | 3 |
| ShuttleSim.Tests | 10 | 10 |
| **Total** | **176** | **176** |

### 3. Diff scope — confirmed

- `git diff --name-only a90b3f7..cee9694 -- src/` → `src/FleetManager.Tests/WmsStatusPusherTests.cs`, `src/FleetManager/WmsStatusPusher.cs`. **Two files, both named in the assignment.** Nothing else under `src/` moved.
- `git diff -w a90b3f7..cee9694 -- src/…` (whitespace ignored) → the four semantic edits in the table above, and nothing else. The indentation churn is cosmetic, and the `-w` diff proves it is *only* cosmetic.
- The two files the fix did **not** touch but could have — `src/FleetCore/WmsOrderBoard.cs` (where the "right" fix, `[NotNullWhen(true)]`, belongs) and `src/FleetCore/WmsStatusOutbox.cs` — are byte-identical to the first round. The do-not-touch list held.

### 4. The guard edit does not change behaviour — proved, not assumed

The widened guard `if (!_board.TryGet(orderId, out var order) || order is null)` adds a clause that can only fire when `TryGet` returns `true` **and** hands back a null order. Two independent checks say that cannot happen:

1. **Direct probe** against the real board (`/tmp/opencode/l23probe3`, scratch, since deleted):

   ```text
   TryGet(present) -> True, order is null? False
   TryGet(absent)  -> False, order is null? True
   ```

   `WmsOrderBoard.TryGet` is `Dictionary<string, WmsOrder>.TryGetValue` under a lock, and the dictionary only ever holds a constructed `WmsOrder` — so `true ⇒ non-null`, and the guard's new clause is unreachable.

2. **Instrumented build** (`/tmp/opencode/mut_guard`, scratch, since deleted): the guard was rewritten to *throw* if `order` is ever null on a `true` return, and the whole suite was run. Exit code 0, **176/176 passed, the probe never fired.**

3. **The named facts, run one by one** — the three pusher facts and the four new facts all pass:

   | Fact | Project | Result |
   |---|---|---|
   | `Push_posts_the_current_word_to_the_status_route` | FleetManager.Tests | Passed (1/1) |
   | `Push_failure_keeps_the_order_pending_for_the_next_pass` | FleetManager.Tests | Passed (1/1) |
   | `Push_timeout_keeps_the_order_pending_and_does_not_escape` | FleetManager.Tests | Passed (1/1) |
   | `Order_status_route_is_pinned` | FleetCore.Tests | Passed (1/1) |
   | `NoteSync_Advanced_marks_the_order_pending` | FleetCore.Tests | Passed (1/1) |
   | `NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing` | FleetCore.Tests | Passed (1/1) |
   | `NoteSync_twice_marks_once` | FleetCore.Tests | Passed (1/1) |

### 5. Acceptance greps — re-verified after the fix commit

All **33** lesson-23 greps (sections 3–9), rebased onto `f0c7a2a` exactly as the lesson instructs: **33 PASS / 0 FAIL.** The three the fix commit could most plausibly have disturbed:

| Check | Result |
|---|---|
| 6.7 `! grep -qE "https?://" src/FleetManager/WmsStatusPusher.cs` (no URL in the pusher) | **PASS** — the new log line interpolates an `int` status code, not a URL |
| 5.4 `! grep -qE "HttpClient\|HttpRequest\|Results\|HttpContext" src/FleetCore/WmsStatusOutbox.cs` (FleetCore purity) | **PASS** — the outbox was not touched at all |
| 7.6 `test -z "$(git diff f0c7a2a -- src/FleetManager/Program.cs \| grep -E '^-[^-]')"` (no deletions in `Program.cs`) | **PASS** — `Program.cs` did not move |
| 8.1 the nine do-not-touch types diff empty | **PASS** |

Full list unchanged from the first round: 3.1–3.6, 4.1–4.2, 5.1–5.4, 6.1–6.7, 7.1–7.6, 8.1, 9.1–9.7 — all PASS.

### 6. Mutations re-run — no "must go red" mutation survived the fix

All six checks re-run in scratch copies under `/tmp/opencode` (workspace untouched):

| # | Mutation | Result |
|---|---|---|
| a | `NoteSync`: drop the `== Advanced` check | **KILLED** — `NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing` fails |
| b | drop `MarkPending` from the three failure paths | **KILLED** — `Push_failure_…` **and** `Push_timeout_…` fail (2 failed) |
| c | remove the `OperationCanceledException` catch | **KILLED** — `Push_timeout_…` fails |
| d1 | serialize with default (PascalCase) options | **KILLED** — `Push_posts_…` fails on `GetProperty("state")` |
| d2 | POST to `WmsRoutes.Orders` instead | **KILLED** — `Push_posts_…` fails on the path assert |
| e | `Claim` returns the copy without clearing | **KILLED** — `NoteSync_Advanced_marks_the_order_pending` fails |

The fix did not weaken a single fact.

### 7. Cleanliness

- `pgrep -af "dotnet run\|FleetManager.dll\|ShuttleSim.dll\|mosquitto\|postgres"` → nothing but the reviewer's own command line. `docker compose -f docker/compose.yml ps` → **no containers running**; nothing was started (the pure suite needs no broker or database).
- Scratch under `/tmp/opencode` (`l23fix`, `l23probe3`, `mut_a`…`mut_g`, `mut_guard`, and the captured output file) — **all deleted** after the run.
- `git status --porcelain` → **clean** apart from this review file. Nothing under `src/` was edited by the reviewer.

### Issue status after the fix

| # | Issue | Severity | Status | Attribution |
|---|---|---|---|---|
| 1 | 4 × `CS8602` in `WmsStatusPusher.cs` | **blocker** | **FIXED** | Owner. Guard widened inside the one file the lesson allows. Verified cold: 0/0. |
| 2 | "rejected" log dropped the reason | should-fix | **FIXED** | Owner. `rejected with {(int)response.StatusCode} {orderId} {order.State}` — id, word and reason on one line. |
| 3 | mixed indentation (three widths) | should-fix | **FIXED** | Owner. 4-space house through both methods, `if (` spaced, trailing whitespace dropped, final newline added. |
| 4 | test file had no namespace | should-fix | **FIXED** | Owner. `namespace FleetManager.Tests;` added; `using FleetManager;` correctly dropped. |
| 5 | uncaught exception in a pass kills the fleet manager and loses the claimed word | consider | **DEFERRED** | Not the owner's to fix: record 31 §8 pins exactly three failure shapes and the owner implemented them faithfully. Belongs to the **professor** (tighten the `Wms:BaseUrl` scheme check, and decide the loop's exception policy) before the execution slice arms it. Still dormant — nothing calls `NoteSync` in production. |
| 6 | "newest truth wins" is a convention, not a pinned fact | consider | **DEFERRED** | Belongs to the **execution slice** (one fact: mark `in_progress`, mark `done`, then send — the WMS must see one POST carrying `done`). |
| 7 | evidence gaps and small notes | consider | **PARTLY FIXED** | Fixed by this commit: the pusher's final newline and trailing whitespace. Still open, all cosmetic and none of them blocking: `WmsStatusOutbox.cs`, `WmsStatusOutboxTests.cs` and `WmsStatusPusherTests.cs` still end without a final newline; one whitespace-only line at `WmsStatusPusherTests.cs:38`; the blank line before each closing brace in `WmsStatusOutbox.cs:20-21, 31-32`; the three push tests still repeat the same twelve-line world setup instead of using one helper; and the five pusher log lines still carry no `status push: ` prefix, which is what the lesson's own shape shows. |
| — | **Lesson defect** (the professor's skeleton and hints §3.2 have the same `CS8602` hole) | — | **OPEN — for the professor** | See below. Not a student failure. |

### Lesson defect — the same hole is in the teaching material, and it should be corrected before the next slice

This is the one thing I would not leave unrecorded. The owner did **not** invent the nullable problem:

- The lesson's own `SendPendingAsync` shape (lesson lines 284–318) writes `if (!_board.TryGet(orderId, out var order)) { …; continue; }` and then dereferences `order.OrderId` / `order.State` — against the same `public bool TryGet(string orderId, out WmsOrder? order)` signature, which carries no `[NotNullWhen(true)]`.
- Hints §3.2 repeats it verbatim in the near-solution skeleton (hints lines 175–201).
- Record 31 §12 even measured the related idiom (`Assert.NotNull` carries `[NotNull]`, scratch `l23notnull`) — but never mentioned the `TryGet` out-parameter attribute.

A student who transcribes the lesson exactly gets four warnings and then has to find a way around a file they are forbidden to touch. The owner found the right way around — widen the guard, inside the one file the lesson opens — but that is a workaround for a hole in the material, not a lesson in itself.

**What I recommend (the professor's call, in the professor's files):**

1. **Fix the lesson skeleton and hints §3.2** to show the null-safe form — either `if (!_board.TryGet(orderId, out var order) || order is null)` or, better, the shape that shows the *contract* rather than the workaround: copy the two values you need into non-nullable locals (`var orderId = order.OrderId; var state = order.State;`) right after the guard.
2. **Decide, in a decision record (append-only — a new record, not an edit of 31), where the nullability contract of `WmsOrderBoard.TryGet` lives.** The right long-term home is `[NotNullWhen(true)]` on the out parameter, but `src/FleetCore/WmsOrderBoard.cs` is on lesson 23's do-not-touch list, so it has to be a later slice that opens that file. The record should say which slice owns it, so the next student is not handed the same trap.
3. **Make the acceptance command honest.** Lesson 23's criterion #1 is `cd src && dotnet build`, which is a no-op in a warm workspace. Either `dotnet build --no-incremental` or `dotnet clean && dotnet build` should be the stated command, or the lesson should say "from a clean tree". This is the second lesson running where an incremental build hid a real defect.

None of this is on the owner. It is a lesson defect, and it should be corrected before the next slice so the same trap is not set again.

### What is solid (follow-up)

- **The fix is minimal and exactly where the review said it belonged.** The guard was widened inside `WmsStatusPusher.cs` — the one file the lesson opens — and `WmsOrderBoard.cs` was left alone, which is the difference between respecting requirement 7 and quietly working around it.
- **The fix was verified the right way.** The owner ran the acceptance command cold rather than trusting the warm workspace, after being told exactly why the warm build lies.
- **The log line now carries the reason.** `rejected with 500 ORD-1 in_progress` tells a support engineer at 03:00 whether the WMS is unwell (5xx, wait) or has restarted and forgotten the order (404, intervene) — the exact split record 31 §8 names as the future permanent-rejection class.
- **The indentation is now one width.** The method reads top to bottom at one step, which is what makes the block structure explainable out loud.
- **The test file joined the project's namespace**, and `using FleetManager;` was removed only because the enclosing namespace now resolves it — a correct, minimal change with a clean build as proof.

### Explain-it check (follow-up round)

Answer these out loud, no notes. They are about the fix, not about the slice in general.

1. **The nullable contract.** The compiler could not see that `order` is non-null after `TryGet` returns `true`, even though it always is. Explain why in words — what does the method's *signature* promise, and what would it have to promise for the compiler to know? Then say what the difference is between a `TryGet` that returns `bool` and one that returns `bool` with an annotated out parameter, and why the compiler's caution is the right default even when the code happens to be safe today.
2. **The guard, and what it would cost if it could fire.** Your guard now reads `if (!_board.TryGet(orderId, out var order) || order is null)`. Say exactly what happens on each side of the `||`. Then: the clause is unreachable with today's board — say how you know that, and say what the pusher would do if the board *could* hand back a null order (log line, requeue or not, and why). Then say whether you would rather have the compiler tell you "this can be null" than have the runtime quietly agree with you.
3. **The reason in the log.** Before this fix, the rejected line read `rejected ORD-1 in_progress`. Now it reads `rejected with 500 ORD-1 in_progress`. Name the one thing the status code tells a support engineer that the id and the word do not, and point at the sentence in the contract page *you* published that says what the fleet manager does about a rejected word. Then say what the contract promises about the retry, and what that promise costs the WMS.

**Lines and blocks to study:**

- `src/FleetManager/WmsStatusPusher.cs:27` — the widened guard. Why does the `||` clause read the way it does, and what would change if the board were opened up later to add `[NotNullWhen(true)]`? (the clause becomes dead code — and then it should be deleted, not left as decoration)
- `src/FleetManager/WmsStatusPusher.cs:45` — the rejected line. Where do the id, the word and the reason sit, and why is the status code an `int` cast rather than the enum?
- `src/FleetManager/WmsStatusPusher.cs:24-58` — the whole method at one indentation width. Compare it against `FleetServeLoop.cs:39-45` and `WmsOrderBoard.cs:12-39`: which neighbour did you copy, and does your block structure now match theirs?
- `src/FleetManager.Tests/WmsStatusPusherTests.cs:1-5` — the namespace and the removed `using`. Why does `WmsStatusPusher` still resolve with no `using FleetManager;`? (name lookup walks out through the enclosing namespace) — and what would have happened if the class had been named `Tests`?
- `src/FleetManager.Tests/WmsStatusPusherTests.cs:38` — the whitespace-only line. What is the house rule about a blank line inside a block?

### Revisit list (follow-up)

- **Nullable reference types are a contract, not a suggestion.** `[NotNullWhen(true)]` on a `TryGet` out parameter is the idiom that makes "returned true ⇒ not null" visible to the compiler. Look again at `WmsOrderBoard.TryGet` and see exactly what it does *not* say — and note that the professor's own lesson and hints have the same hole, so this is a shared gap, not a private mistake.
- **A clean build is a gate, and an incremental build can hide it.** `dotnet build --no-incremental`, `dotnet clean`, and "a fresh copy with no `bin/`/`obj/`" are now three ways to check the same thing. Use one of them every time, not just when a review asks.
- **The lesson-material check.** When a lesson shows a code shape, treat it as code: read it for the warning it will produce on your machine. The professor's shape is a lesson too, and it had a defect.
- **The three-outcome log line.** id, word, reason. Now that one line carries all three, audit the other five `Console.WriteLine` sites in `FleetManager` against the same bar.

### End-product check (follow-up)

The fix does not move the slice forward or backward in the product; it makes it *honest*. What it buys is the thing a reviewer cares about most in an integration seam: the sender now fails loudly at build time rather than silently at 03:00, and the operator sees *why* a word was rejected, not just that it was. The structural risk flagged in the first-round end-product check is unchanged and still dormant — the pile has no production caller, so the uncaught-exception blast radius (issue 5) is still asleep, and it is still the thing the execution slice arms. The one thing this commit *does* change for the milestones ahead is the trust in the seam: the contract page, the pinned route, the camelCase body and the requeue-on-failure policy are now carried by code that compiles without a warning, which is what a WMS integration has to look like before the receiver and the dispatcher start talking to it.

---

# First-round review — commit `a90b3f7` (preserved unchanged)

*Everything below is the original review, written against `a90b3f7` before the fix. It is kept verbatim so the record shows what was found, not only what was found fixed. Its verdict was **not yet**; the verdict above is the current one.*

## What was checked, and how

| # | Command | Result |
|---|---|---|
| 1 | `git log --oneline -12`, `git status --porcelain` | HEAD `a90b3f7`; tree **clean**. `f0c7a2a` = lesson material (`docs/decisions/31-status-push.md`, `docs/lessons/23-status-push.md`, `23-status-push-hints.md`, `22-order-state-ladder-answers.md`); `b45d76d` = **hints only** (`docs/lessons/23-status-push-hints.md`, +415/−31). The owner's assignment work is `a90b3f7` alone: 9 files (357 insertions) — `docs/contract/wms-rest.md`, `src/FleetCore/WmsRoutes.cs`, `src/FleetCore/WmsStatusOutbox.cs` (new), `src/FleetCore.Tests/WmsContractLiteralsTests.cs`, `src/FleetCore.Tests/WmsStatusOutboxTests.cs` (new), `src/FleetManager/WmsStatusPusher.cs` (new), `src/FleetManager/Program.cs`, `src/FleetManager/appsettings.json`, `src/FleetManager.Tests/WmsStatusPusherTests.cs` (new). Exactly the file set the assignment names. |
| 2 | `dotnet --version` | `10.0.113` (lesson/record measured against `10.0.112`; no behavioural difference found) |
| 3 | `cd src && dotnet build` (warm `bin/`/`obj/`) | RC 0 — **0 Warning(s), 0 Error(s)** — but this is a **no-op**: nothing recompiles, so it proves nothing. See command 4. |
| 4 | `cd src && dotnet build --no-incremental` | RC 0 — **4 Warning(s), 0 Error(s)**: `CS8602: Dereference of a possibly null reference` at `src/FleetManager/WmsStatusPusher.cs:40, 44, 50, 55`. **This is the blocker.** |
| 5 | Fresh copy of the committed `src` with every `bin/`/`obj/` deleted (`/tmp/opencode/l23clean`), then plain `dotnet build` | RC 0 — **4 Warning(s), 0 Error(s)**, the same four. Confirms the warnings are in the **committed source**, not a stale-artifact artefact of this workspace. |
| 6 | Baseline: `git archive f0c7a2a src` → scratch `/tmp/opencode/l23base`, clean `dotnet build` | RC 0 — **0 Warning(s), 0 Error(s)**. The 4 warnings are **introduced by this lesson**; the repo's warning-free bar (also recorded in review 22: "0 Warning(s), 0 Error(s)") was held until this commit. |
| 7 | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **RC 0 — Failed: 0, Passed: 176, Skipped: 0, Total: 176.** Per project: FleetCore **49** (was 45, **+4**), FleetManager **59** (was 56, **+3**), ShuttleCore 31, Contracts 8, TrafficCore 6, Messaging 10, MqttMessaging 3, ShuttleSim 10. Baseline 169 → **176**, exactly the lesson's target (+7). |
| 8 | The seven new facts, run by name (`--filter "FullyQualifiedName~…"` for each of the seven) | **Passed: 4** (FleetCore.Tests) **+ 3** (FleetManager.Tests), Failed 0. The names are exact and the projects are right. |
| 9 | The 33 acceptance greps of lesson 23, sections 3–9 (the three `git diff` comparisons rebased onto `f0c7a2a`, the lesson-material commit, exactly as the lesson instructs) | **33/33 PASS** — full table below. |
| 10 | Contract transcription: record 31 §3's pinned markdown block extracted programmatically and compared with the new section in `docs/contract/wms-rest.md` | **Verbatim match** (no diff). Title `# WMS REST contract v2`, `Version: 2`, the section sits immediately above `## Changing this contract` with the `---` separator kept, and `git diff --name-only f0c7a2a -- docs/contract` names **only** `docs/contract/wms-rest.md`. |
| 11 | Mutation check, all seven rows of the lesson's table, run in scratch copies under `/tmp/opencode` (workspace untouched) | 5/5 "must go red" **killed** on the predicted tests; 2/2 "may survive" **survived 176/176**, as the lesson predicted. Table below. |
| 12 | Scratch probe (not committed, `/tmp/opencode`): the "newest truth wins" property — mark `in_progress`, then mark `done` **before** the send, then send | **One** POST, body `state = "done"`. The read-at-send rule works; no committed fact pins it (see issue 5). |
| 13 | Scratch probe (`/tmp/opencode/l23probe2`): `Uri.TryCreate(…, UriKind.Absolute, …)` on odd schemes; `PostAsync` with a non-http `BaseAddress` and with none | `ftp://`, `mailto:`, `file://` all **pass** the house URL validation; `PostAsync` then throws `NotSupportedException: The 'ftp' scheme is not supported.` / `InvalidOperationException: An invalid request URI was provided…` — **neither** is caught by the pusher's two catches. Basis for issue 4. |
| 14 | `grep -rn "NoteSync" src --include=*.cs \| grep -v Tests` | one hit — the definition at `src/FleetCore/WmsStatusOutbox.cs:8`. **No production caller**, exactly as record 31 §10 states and as the lesson requires. Same for `SyncFromTask`. |
| 15 | `grep -rn "Console.WriteLine" src/FleetManager --include=*.cs \| grep -v Tests` | 5 sites, all in `WmsStatusPusher.cs`; the rest of `FleetManager` has 6. House logging (record 31 §I) is `Console.WriteLine`, zero `ILogger` anywhere — matched. |
| 16 | `pgrep -af "dotnet run\|FleetManager.dll\|mosquitto\|postgres"`; `docker compose -f docker/compose.yml ps` | no stray run/broker/database processes; **no containers running**. Nothing was started — the pure suite needs no broker. |
| 17 | `git status --porcelain` after all work | **clean** — this review edited nothing under `src/`; the only new file is this review. |
| 18 | Scratch cleanup | `/tmp/opencode/l23mut`, `/tmp/opencode/l23clean`, `/tmp/opencode/l23base`, `/tmp/opencode/l23probe2` — all **deleted**. |

**Not run / not yet verified:** the `Category=Integration` complement (the Postgres integration suite) — this slice adds no integration trait and the pure gate is the lesson's gate. The Postgres/MQTT services were not started. The owner's own stretch-goal 8-thread lock probe (lesson "Stretch goal") is **not** attached as an owner artifact; the professor's measured probe stands in the lesson's verification table (3/3 pass with the lock, 3/3 crash without) — see issue 7.

---

## Acceptance criteria

### Lesson 23 requirements, one by one

- [x] **R1 `docs/contract/wms-rest.md` v1 → v2, one new section, verbatim** — title `# WMS REST contract v2` (`:1`), `Version: 2` (`:3`), the "Order status push (fleet manager → WMS)" section at `:90-113`, immediately above `## Changing this contract` with the `---` separator, matching record 31 §3's pinned block **byte for byte** (verified by programmatic diff, command 10). No other contract page moved (grep 3.4); `docs/contract/v4.md` and `src/Contracts/` are untouched (grep 3.6); `ContractVersion.Current` is still `4` (grep 3.5).
- [x] **R2 `src/FleetCore/WmsRoutes.cs` + the literal pin** — `public const string OrderStatus = "/api/order-status";` beside `Orders` (`WmsRoutes.cs:6`); `Order_status_route_is_pinned` added beside `Orders_route` in `WmsContractLiteralsTests.cs:13`, asserting the **literal** `"/api/order-status"` against the constant (not the constant against itself).
- [x] **R3 `src/FleetCore/WmsStatusOutbox.cs` (new)** — `public sealed class WmsStatusOutbox` (`:3`); `HashSet<string>` with `StringComparer.Ordinal` (`:5`); one `object _gate` (`:6`); `NoteSync(string taskId, OrderSyncOutcome outcome)` marks **iff** `Advanced` (`:8-12`), the other two outcomes are silent no-ops; `MarkPending` adds under the lock (`:14-21`); `Claim` copies, clears, returns the copy, in that order, under the lock (`:23-32`); `PendingCount` is a property **with a body** that takes the lock (`:34-43`) — the hints' Tier-1 trap (a `{ get; }` auto-property that always reads `0`) was avoided. Pure: no `HttpClient`/`HttpRequest`/`Results`/`HttpContext` (grep 5.4).
- [~] **R4 `src/FleetManager/WmsStatusPusher.cs` (new)** — the *behaviour* is exactly right: `public sealed class WmsStatusPusher` with the required ctor `(WmsOrderBoard, WmsStatusOutbox, HttpClient, TimeSpan)`, three `ArgumentNullException` guards and the `interval > TimeSpan.Zero` / `ArgumentOutOfRangeException` arm, **storing into `_interval`** (the hints' TRAP avoided) (`:14-20`); `SendPendingAsync` claims once outside the per-id work (`:24`), `TryGet` → log + `continue` with **no** requeue (`:27-31`), `static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)` (`:13`), `PostAsync(WmsRoutes.OrderStatus, …)` (`:37`), `IsSuccessStatusCode` → delivered log, else `MarkPending` + log (`:38-46`), `catch (HttpRequestException)` → `MarkPending` + log (`:48-52`), `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` → `MarkPending` + log (`:53-57`); `RunAsync` is the house `PeriodicTimer` loop with no try/catch (`:61-69`). No URL, no timeout literal, no interval literal (greps 6.2–6.7). **BUT the file emits 4 `CS8602` warnings (command 4/5) — see blocker 1.**
- [x] **R5 `src/FleetManager/Program.cs` — `RunServeAsync` only** — the three keys read and validated in the house shape at `:156-165` (missing → throw; not an absolute `Uri` → throw; both ints `< 1` → throw), **before** `await bus.ConnectAsync` at `:171`, so a bad URL fails before the bus connects as the lesson claims; the outbox, the one `using var HttpClient { BaseAddress = wmsUri, Timeout = … }` and the pusher built after the board (`:177-183`); `var pushTask = pusher.RunAsync(stopping.Token);` beside `loopTask` (`:195-196`); awaited after `loopTask` with the mirrored `catch (OperationCanceledException) when (stopping.IsCancellationRequested)` and the mirrored comment (`:240-247`). The diff against the lesson-material commit is **additions only** — grep 7.6 (`git diff | grep -E '^-[^-]'`) is empty — and every added line is inside `RunServeAsync` (`:129-257`). `RunMoveAsync` and `RunRecoverAsync` are untouched.
- [x] **R6 `src/FleetManager/appsettings.json`** — the `Wms` section with the three keys and the three values from record 31 §9 (`BaseUrl: http://127.0.0.1:5090`, `StatusTimeoutMs: 2000`, `StatusIntervalMs: 1000`). The file also gained the trailing newline it was missing.
- [x] **R7 "do not touch" list** — the nine reviewed types diff **empty** (grep 8.1), `src/Contracts/` and `docs/contract/v4.md` empty (3.6), no `WmsStatusPusher`/`Wms:Status` token in `TaskDispatcher.cs` / `RunLoop.cs` / `FleetServeLoop.cs` (7.5). Verified by `git diff f0c7a2a -- …`, not by eye.
- [x] **R8 the seven facts, in the right projects, none decoration** — see the table below; each one kills what the lesson says it kills, confirmed by the mutation run.

### Acceptance greps (lesson 23, sections 3–9 — 33 checks, all PASS)

| # | Check | Result |
|---|---|---|
| 3.1 | `grep -q "^Version: 2" docs/contract/wms-rest.md` | PASS |
| 3.2 | `grep -q "Order status push" docs/contract/wms-rest.md` | PASS |
| 3.3 | `grep -q "api/order-status" docs/contract/wms-rest.md` | PASS |
| 3.4 | `test "$(git diff --name-only f0c7a2a -- docs/contract)" = "docs/contract/wms-rest.md"` | PASS (only that page moved) |
| 3.5 | `grep -q "Current = 4" src/Contracts/MessageTypes.cs` | PASS |
| 3.6 | `git diff f0c7a2a -- src/Contracts docs/contract/v4.md` empty | PASS |
| 4.1 | `grep -q "OrderStatus" src/FleetCore/WmsRoutes.cs` | PASS |
| 4.2 | `grep -rq "Order_status_route_is_pinned" src/FleetCore.Tests` | PASS |
| 5.1 | `test -f src/FleetCore/WmsStatusOutbox.cs` | PASS |
| 5.2 | `grep -q "NoteSync" src/FleetCore/WmsStatusOutbox.cs` | PASS |
| 5.3 | `grep -q "lock (_gate)" src/FleetCore/WmsStatusOutbox.cs` | PASS (3 sites) |
| 5.4 | `! grep -qE "HttpClient\|HttpRequest\|Results\|HttpContext" …/WmsStatusOutbox.cs` | PASS |
| 6.1 | `test -f src/FleetManager/WmsStatusPusher.cs` | PASS |
| 6.2 | `grep -q "WmsRoutes.OrderStatus" …/WmsStatusPusher.cs` | PASS |
| 6.3 | `grep -q "JsonSerializerDefaults.Web" …/WmsStatusPusher.cs` | PASS |
| 6.4 | `grep -q "MarkPending" …/WmsStatusPusher.cs` | PASS |
| 6.5 | `grep -q "HttpRequestException" …/WmsStatusPusher.cs` | PASS |
| 6.6 | `grep -q "OperationCanceledException" …/WmsStatusPusher.cs` | PASS |
| 6.7 | `! grep -qE "https?://" …/WmsStatusPusher.cs` | PASS |
| 7.1 | `grep -q "Wms:BaseUrl" src/FleetManager/Program.cs` | PASS |
| 7.2 | `grep -q "Wms:StatusTimeoutMs" src/FleetManager/Program.cs` | PASS |
| 7.3 | `grep -q "Wms:StatusIntervalMs" src/FleetManager/Program.cs` | PASS |
| 7.4 | `grep -q '"Wms"' src/FleetManager/appsettings.json` | PASS |
| 7.5 | no `WmsStatusPusher\|Wms:Status` in `TaskDispatcher.cs` / `RunLoop.cs` / `FleetServeLoop.cs` | PASS |
| 7.6 | `test -z "$(git diff f0c7a2a -- src/FleetManager/Program.cs \| grep -E '^-[^-]')"` | PASS (**no** line deleted or changed) |
| 8.1 | the nine reviewed types diff empty | PASS (prints nothing) |
| 9.1 | `NoteSync_Advanced_marks_the_order_pending` in FleetCore.Tests | PASS |
| 9.2 | `NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing` in FleetCore.Tests | PASS |
| 9.3 | `NoteSync_twice_marks_once` in FleetCore.Tests | PASS |
| 9.4 | `Push_posts_the_current_word_to_the_status_route` in FleetManager.Tests | PASS |
| 9.5 | `Push_failure_keeps_the_order_pending_for_the_next_pass` in FleetManager.Tests | PASS |
| 9.6 | `Push_timeout_keeps_the_order_pending_and_does_not_escape` in FleetManager.Tests | PASS |
| 9.7 | `grep -rq 'GetProperty("state")' src/FleetManager.Tests` | PASS (`WmsStatusPusherTests.cs:74`) |

**33/33.**

### Record 31, decisions A–I, checked one by one

| Decision | Requirement | Result |
|---|---|---|
| A | the fleet manager POSTs over HTTP; not MQTT, not polling | **PASS** — `PostAsync` from `FleetManager`; no MQTT type, no contract change |
| B | REST page v1 → v2, one pinned section; MQTT stays 4 | **PASS** — verbatim (command 10); `ContractVersion.Current` = 4 |
| C | route is a pinned constant; body is the documented `WmsOrder`, camelCase via `JsonSerializerDefaults.Web` | **PASS** — `WmsRoutes.OrderStatus` + literal pin; `Json` options at `WmsStatusPusher.cs:13` |
| D | `NoteSync` marks **iff** `Advanced`; `AlreadyThere`/`NoOrder`/intake's `received` owe nothing | **PASS** — `WmsStatusOutbox.cs:8-12`; intake is untouched (the `201` body is still the only `received`) |
| E | one flag per order id under its own lock; `Claim` takes-and-empties; failures requeue; the word is read from the board at send time | **PASS** — `WmsStatusOutbox.cs`; the sender reads `_board.TryGet` at send time (`:27`) |
| F | one sender, one testable pass, one `PeriodicTimer` loop, one long-lived `HttpClient` | **PASS** — `SendPendingAsync` public and separate, `RunAsync` the loop, one `using var wmsClient` in `Program.cs:178-182` |
| G | every failure shape — non-2xx, refused, timed out — requeues and logs; no attempt cap | **PASS** — the three branches, each with `MarkPending` + one `Console.WriteLine` |
| H | three config keys, validated at startup, no literal in code | **PASS** — `Program.cs:156-165` before the bus connects; no URL/timeout/interval literal in the pusher |
| I | `Console.WriteLine`, one line per outcome, house style | **PASS in kind, one gap** — five sites, house style; the "rejected" line omits the reason (issue 2) |

### Mutation evidence (reviewer-run, scratch only, workspace untouched)

| # | Mutation | Lesson's prediction | Reviewer result | Catching test |
|---|---|---|---|---|
| a | `NoteSync`: drop the `== Advanced` check | must go red | **KILLED** — FleetCore 48/49 | `NoteSync_of_AlreadyThere_and_NoOrder_mark_nothing` (`PendingCount` 1, expected 0) — exactly as predicted |
| b | no `MarkPending` in any of the three failure paths | must go red | **KILLED** — FleetManager 57/59 | `Push_failure_keeps_the_order_pending_for_the_next_pass` **and** `Push_timeout_keeps_the_order_pending_and_does_not_escape` — exactly as predicted (the lesson named the first; the second also dies, which is a bonus) |
| c | remove the `OperationCanceledException` catch | must go red | **KILLED** — FleetManager 58/59 | `Push_timeout_keeps_the_order_pending_and_does_not_escape` — `SendPendingAsync` throws instead of returning — exactly as predicted |
| d1 | serialize with default (PascalCase) options | must go red | **KILLED** — FleetManager 58/59 | `Push_posts_the_current_word_to_the_status_route` — `GetProperty("state")` throws `KeyNotFoundException` |
| d2 | POST to `WmsRoutes.Orders` instead | must go red | **KILLED** — FleetManager 58/59 | `Push_posts_the_current_word_to_the_status_route` — the path assert fails |
| e | `Claim` returns the copy **without** clearing | must go red | **KILLED** — FleetCore 48/49 **and** FleetManager 57/59 | `NoteSync_Advanced_marks_the_order_pending` (the pile never empties) — exactly as predicted |
| f | `Program.cs` hard-codes `TimeSpan.FromSeconds(5)` for the timeout | may survive | **SURVIVED 176/176** | — as predicted. The grep `grep -q "Wms:StatusTimeoutMs" src/FleetManager/Program.cs` is the check, on purpose. **What it would do to a real WMS:** the client timeout stops bounding anything. A WMS that accepts the TCP connection and then never answers holds a request open for 5 s instead of 2 s; the pile drains 2.5× slower; and if the timeout were raised to, say, 30 s, one stuck WMS would hold a socket and a claim for half a minute, and `PendingCount` would read 0 (the pile is emptied by `Claim()`) while the truth was owed. The timeout is the only thing that stops a half-dead WMS from becoming a half-dead fleet manager. |
| g | delete `lock (_gate)` from all three pile methods | may survive | **SURVIVED 176/176** | — as predicted (the committed facts are single-threaded). **What it would do to a real WMS:** the pile eats flags. `HashSet<T>` is not thread-safe, and the lesson's own measurement (`/tmp/opencode/l23lock`, in the lesson's verification table) shows 3/3 runs crashing without the lock — `IndexOutOfRangeException` twice, `InvalidOperationException: … corrupted its state` once. A corrupted set does not raise; it *loses*. The word that vanishes from the pile is a word the WMS will never hear, and nothing in the log says so. The day the dispatcher thread starts marking while the serve-loop thread claims, that is a real, silent, per-pallet data-loss bug — not a crash. |

**Reading.** No "must go red" mutation survived, so no test in this slice is decoration. The suite kills the PascalCase body on the **key** (which is why the lesson insists on parsing), kills the missing requeue, kills the missing timeout catch, and kills the pile that never empties.

---

## Top issues, ranked

### 1. BLOCKER — the committed source does not build warning-free: 4 × `CS8602` in `WmsStatusPusher.cs`

`src/FleetManager/WmsStatusPusher.cs:40, 44, 50, 55`

```
src/FleetManager/WmsStatusPusher.cs(40,59): warning CS8602: Dereference of a possibly null reference.
src/FleetManager/WmsStatusPusher.cs(44,41): warning CS8602: Dereference of a possibly null reference.
src/FleetManager/WmsStatusPusher.cs(50,39): warning CS8602: Dereference of a possibly null reference.
src/FleetManager/WmsStatusPusher.cs(55,39): warning CS8602: Dereference of a possibly null reference.
    4 Warning(s) — 0 Error(s)
```

Lesson 23's acceptance criterion #1 is *"clean build, no warnings"*, and the lesson's own verification table records the baseline as **0 Warning(s), 0 Error(s)**. The baseline at `f0c7a2a` still builds 0/0 in a clean tree (command 6); this commit is the first to break that. Review 22 recorded the same bar being held. This is a regression on the repo's own gate, so it is a blocker, not a nit.

**Why the warning is there.** `WmsOrderBoard.TryGet` is declared

```csharp
public bool TryGet(string orderId, out WmsOrder? order)
```

with no `[NotNullWhen(true)]` on the out parameter (and no file in `src/` uses that attribute today). The C# compiler therefore cannot connect "the method returned `true`" with "`order` is not null", so every `order.OrderId` / `order.State` after the guard is a possible null dereference. The guard itself is correct — `if (!_board.TryGet(orderId, out var order)) { …; continue; }` — the *compiler* just is not allowed to assume what the method guarantees at runtime.

**Why it is not a live runtime bug.** `TryGet` returns `true` only when the dictionary had the key, and the dictionary never stores null, so `order` is non-null on every path that reaches line 40. The tests all pass. This is a *contract* the type system cannot see, and the repo's rule is that nullable warnings are things to fix (dotnet-style: *"Turn on nullable reference types and treat warnings as things to fix"*).

**Where the fix belongs.** Requirement 7 puts `src/FleetCore/WmsOrderBoard.cs` on the **do-not-touch** list, so the fix cannot be "add `[NotNullWhen(true)]` to `TryGet`" in this slice — that is the right fix, and it belongs to the slice that opens that file. The fix that belongs to *this* slice is inside `WmsStatusPusher.cs`, the one file this lesson lets you change: widen the guard so the compiler can see the null-check, or copy the two values you need into non-nullable locals immediately after the guard, and use those.

**How to verify it (and how it hid).** `cd src && dotnet build` in a workspace with warm `bin/`/`obj/` is a **no-op** — it recompiles nothing and reports 0 warnings, which is exactly how this passed unnoticed. Use `cd src && dotnet clean && dotnet build`, or `dotnet build --no-incremental`, or a fresh copy of `src/` with `bin/` and `obj/` deleted. Please re-run the acceptance command that way and paste the output.

**A lesson defect the professor should also fix.** The professor's own skeleton has the identical hole: the lesson's `SendPendingAsync` shape (lesson lines 284–318) and hints §3.2 both dereference `order` after the same guard, against the same `TryGet` signature. A student who transcribes the lesson exactly gets these four warnings. The lesson and record 31 should either show the null-safe form or state that `TryGet` needs `[NotNullWhen(true)]`. This is a lesson gap, not a student failure — but the student still has to leave the tree warning-free, so the fix lands on the owner.

### 2. SHOULD-FIX — the "rejected" log line drops the reason (the HTTP status code)

`src/FleetManager/WmsStatusPusher.cs:45`

```csharp
Console.WriteLine($"rejected {orderId} {order.State}");
```

Requirement 4 asks for "one `Console.WriteLine` line per outcome, carrying the order id and the word"; record 31 §10 asks for "the order id, the word, **and the reason**"; hints §3.2 says the rejected branch writes "one log line **with the status code**"; and the lesson's own shape is `rejected with {(int)response.StatusCode}`. The status code *is* the reason on that branch, and it is the one piece of information that separates "the WMS is unwell, leave it alone" from "the WMS restarted, forgot this order, and will never accept it again" — the exact scenario record 31 §8 names as the realistic permanent rejection. A support engineer reading `rejected ORD-1 in_progress` at 03:00 cannot tell those two apart, and the second one is the case that will eventually need attempts plus an alarm. One interpolation: `{(int)response.StatusCode}`.

(The other four lines are fine on this test: `pushed`/`unreachable`/`timeout` carry the id and the word, and the no-order line carries the id — there is no word to carry there, and the lesson's shape agrees.)

### 3. SHOULD-FIX — the indentation in `SendPendingAsync` is mixed, in three different steps

`src/FleetManager/WmsStatusPusher.cs:24-58`

```
          var ids = _outbox.Claim();          <- 10 spaces (method body should be 8)
          foreach (var orderId in ids)         <- 10
          {                                    <- 10
              if (!_board.TryGet(...))         <- 14
              {                                <- 14
                  Console.WriteLine(...);      <- 18
              try                              <- 14
              {                                <- 14
                var body = ...;                <- 16
                if(response.IsSuccessStatusCode) <- 16, and no space after `if`
                {
                    Console.WriteLine(...);    <- 20
```

Three indent widths (2-space steps in the outer half, 4-space steps in the inner half) inside one method, against a 4-space house — `FleetServeLoop.cs:39-45` and `WmsOrderBoard.cs:12-39` are the neighbours to copy. Two lines also carry trailing whitespace (`:32` and `:70`), and `if(` at `:38` is missing the space the house uses everywhere else. This is the "copy-adapt residue" review 22 flagged twice; it does not change behaviour, but the owner has to be able to read their own block structure aloud, and mixed steps make that harder than it needs to be.

### 4. SHOULD-FIX — the new test file has no namespace

`src/FleetManager.Tests/WmsStatusPusherTests.cs`

`internal sealed class StubHandler` and `public class WmsStatusPusherTests` sit in the **global** namespace. Every other file in that project (18 of 19) uses `namespace FleetManager.Tests;`; one uses `namespace FleetManager;`. A global-namespace `StubHandler` is a name any future test file can collide with, and the class has no project to hide it in. One line at the top: `namespace FleetManager.Tests;` (and then `using FleetManager;` can stay or go, as the compiler prefers).

### 5. CONSIDER — one uncaught exception in a pass would take the whole fleet manager down, and would silently lose the claimed word

`src/FleetManager/WmsStatusPusher.cs:22-59` and `src/FleetManager/Program.cs:240-247`

The two catches cover the three shapes record 31 measured. They do not cover everything, and the gap has a large blast radius:

1. `PostAsync` throws `NotSupportedException` ("The 'ftp' scheme is not supported.") for a non-http `BaseAddress`, and `InvalidOperationException` ("An invalid request URI was provided…") when `BaseAddress` is unset. Verified in scratch (command 13). Neither is an `HttpRequestException` or an `OperationCanceledException`, so both escape `SendPendingAsync`.
2. The exception escapes `RunAsync` and faults `pushTask`.
3. `Program.cs:242` awaits `pushTask` under `catch (OperationCanceledException) when (stopping.IsCancellationRequested)` — a different type, so it does **not** match. The exception propagates out of `RunServeAsync`, the process exits, and the alarm loop and the REST door go down with it.
4. Meanwhile the ids `Claim()` already took are **gone**: the pile was emptied and nothing put them back. The WMS never hears about those orders, and `PendingCount` reads 0, so nothing says they were owed.

**This is dormant today, and the execution slice is what arms it.** Nothing calls `NoteSync` in production (command 14), the pile is therefore always empty, and no POST is ever attempted — so today the path cannot be reached. It becomes reachable the moment the dispatcher starts noting transitions.

**And this is a lesson/record gap, not a student failure.** Record 31 §8 pins exactly three failure shapes, the lesson pins the same three, and the owner implemented them faithfully. The professor should decide, before the execution slice: (a) tighten the startup validation — the house shape `Uri.TryCreate(value, UriKind.Absolute, out var uri)` accepts `ftp://`, `mailto:` and `file://` (verified), so it is not "is this an http URL"; and/or (b) decide whether a catch-all that requeues and continues is right for a status word, given that "the truth is never given up on" is the record's own policy; and/or (c) guard the `await pushTask` in `Program` so one loop's failure cannot kill the process that runs the fleet. The owner should not have to invent any of this — but the owner should know the seam is there.

### 6. CONSIDER — the pile's central promise is verified working, but no fact pins it

The whole design rests on "the pile holds ids, the sender reads the word at send time, so the newest truth wins" (record 31 §6). Nothing in the committed suite would fail if the pile held bodies instead of ids — `Push_posts_the_current_word_to_the_status_route` asserts the body matches the board's state at the moment of the send, which is one transition, one word. I verified the real behaviour in scratch (command 12): mark `in_progress`, then mark `done` before the send, and the WMS receives **one** POST carrying `"state":"done"`. Correct — but unpinned. One fact in the execution slice ("the newest word wins") would make it a guarantee rather than a convention.

### 7. CONSIDER — evidence gaps and small notes

- **The stretch-goal probe output is not attached as an owner artifact** (the lesson asks for the 8-thread × 1000-id probe, three rounds with the lock and three without, output kept in the review notes). The professor's measured output stands in the lesson's verification table, and mutation (g) surviving was predicted, so this is an evidence gap, not a safety gap — but it is the second lesson in a row where the stretch artifact was not produced. Run it once in `/tmp/opencode` and paste the output.
- **The mutation (f)/(g) real-WMS notes the lesson asked for are not in any owner artifact.** The commit message is a single line with no body, and no file carries them. The lesson says: *"write down in your review notes what each would do to a real WMS."* The answers are in this review's mutation table above, but the point of the exercise is the owner producing them in their own words.
- **No log prefix.** All five pusher lines are bare (`pushed ORD-1 in_progress`). The lesson's own shape prefixes `status push: `. In the fleet manager's console — where the other six lines are a fixed banner or a raw MQTT payload — a pusher line has no anchor for a support engineer grepping for "why did the WMS not hear about ORD-1".
- **`PendingCount` reads 0 during a long pass.** `Claim()` empties the pile for the whole pass, so a pass that is slow (a WMS that accepts the connection and then stalls, N orders × the timeout) reports zero words owed while N words are in flight. M7's dashboard tile ("words the WMS is owed") will under-report. Related: the sender is **serial**, so an outage of N orders drains as N POSTs, each carrying (usually) the same newest word — contract-legal ("at least once … a newer push replaces what came before") but the mock-WMS slice should expect it.
- **Four of the new/changed files end without a trailing newline** (`WmsStatusOutbox.cs`, `WmsStatusPusher.cs`, `WmsStatusOutboxTests.cs`, `WmsStatusPusherTests.cs` — all end on `}`), and there is trailing whitespace on 2 + 4 + 1 lines respectively. Review 22 flagged the missing newline on two files; this is the third time.
- **The three push tests repeat the same twelve-line world setup** instead of one helper method (hints §3.5: *"Do it once, in a helper method in the test file"*). Copy-paste in a test file is how the four copies drift apart.
- **`Assert.Equal("application/json; charset=utf-8", …)`** is a stricter pin than the hint's `Assert.StartsWith("application/json", …)`. It passes today and pins more, but it pins a value the code under test does not control — a framework charset change would fail the test for a reason that has nothing to do with the sender.
- **The ctor's null and range guards are correct but unpinned by any test.** Not required by the lesson; worth one fact when the file is next opened.
- **`WmsStatusOutbox.cs` has a blank line before the closing brace of `MarkPending` and `Claim`** (`:20-21`, `:31-32`) and before the class brace (`:44`).

---

## What is solid

- **Record 31 is followed to the letter.** Route constant and literal pin, camelCase `Web` serializer, `NoteSync` iff `Advanced`, `Claim` copy-clear-return under one lock, the three failure shapes each requeue and log, no URL/timeout/interval literal in the pusher, three config keys validated at startup **before** the bus connects, `Console.WriteLine` per the house. Nothing had to be inferred; the decision record did its job and the code did not deviate by a token.
- **The purity split held.** `WmsStatusOutbox` is decision logic with no HTTP, no web types, no clock — unit-testable alone, and the only file in the slice that a WMS outage cannot reach. `WmsStatusPusher` holds the I/O. The separation the repo has been building since M0 is intact.
- **The contract page is a verbatim transcription.** I extracted record 31 §3's pinned block programmatically and compared it with the page: byte-for-byte identical, in the right place, with the `---` separator kept, the title and the version both bumped, and no other contract page touched. "Transcribe, do not redesign" was honoured.
- **The mutation table is honest, and I re-ran it rather than taking the word for it.** All seven mutations, in scratch, workspace untouched: five "must go red" died on exactly the predicted tests; the two "may survive" survived 176/176. No test in this slice is decoration — the JSON parse kills the PascalCase body on the *key*, the 500-then-200 stub kills the missing requeue, the delayed stub kills the missing timeout catch.
- **The two hints traps were avoided.** `PendingCount` is a property with a body that takes the lock (Tier 1's "`{ get; }` always reads 0" trap), and the ctor assigns `_interval = …` rather than `interval = …` (Tier 3's "the value is thrown away" trap). The test's `BaseAddress`, the two-step `Assign` → `Start` walk, and the `application/json; charset=utf-8` content type are all handled correctly.
- **`Program.cs` grew by 28 lines and lost none.** The diff is additions only, all inside `RunServeAsync`, with the three keys beside the other keys, the three objects after the board, and the `pushTask` block mirroring the `loopTask` block line for line. The `git diff | grep -E '^-[^-]'` check is empty.
- **One `HttpClient` for the process**, built from config, disposed — the same .NET habit the lesson-20 door already keeps.

---

## Explain-it check

Answer these out loud, no notes, before the next lesson. They go deeper than the lesson's own "Say it out loud" section, which you should also be able to answer cold.

1. **(The blocker, first.)** The compiler says `order` may be null at `WmsStatusPusher.cs:40, 44, 50, 55` — four times. Explain in words why the compiler cannot see that `order` is non-null after `if (!_board.TryGet(orderId, out var order)) { …; continue; }`, even though at runtime it always is. Then: what attribute on `WmsOrderBoard.TryGet` would tell the compiler the truth — and why does requirement 7 of this lesson forbid you from putting it there? What would you change **inside the one file this lesson lets you change**, and how would you prove the build is clean (hint: the acceptance command `dotnet build` does not do it in a warm workspace)?
2. **(Required — the timeout filter.)** Your catch is `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)`. Say exactly which exception type arrives on a client timeout (measure it, do not guess) and which arrives on Ctrl+C. Then say what happens to the push loop and to the fleet manager if you delete the `when` filter — **both** ways: with the filter removed, and with the filter inverted. Finish with: what does the pile look like in each case, and how many words has the WMS lost?
3. **(Required — why the body assertion must parse JSON.)** Your assertion is `doc.RootElement.GetProperty("state").GetString()`. Take the PascalCase body `{"OrderId":"ORD-1","Kind":"store","PalletId":"PLT-001","LaneId":"A01-L01","State":"in_progress","ReceivedAt":"2026-01-01T00:00:00Z"}` and explain, precisely, why `Assert.Contains("in_progress", body)` would still pass on it while `GetProperty("state")` throws `KeyNotFoundException`. Which part of the body changed, and which part did not? Then say what a WMS written against the contract page you published would do with that body — silently ignore it, reject it, or crash?
4. **(Deeper than lesson Q5 — the blast radius.)** Trace the three failure shapes you handled. Then name one exception that could still escape `SendPendingAsync`, say exactly what it does to `pushTask`, to `await pushTask` at `Program.cs:242`, and to the fleet manager as a whole. What is in the pile after that exception, and what has the WMS silently never heard? (There is more than one honest answer — "none, because nothing calls `NoteSync` yet" is one of them, and it is the reason this is a landmine for the next slice rather than a live bug. Say both halves.)
5. **(Deeper than lesson Q2 — the reason in the log.)** Your rejected line reads `rejected ORD-1 in_progress`. A support engineer reads that at 03:00 and has to decide whether to wait or to intervene. Name the one piece of information that separates "the WMS is unwell" from "the WMS restarted and forgot this order, and will never accept it again" — and point at the sentence in the contract page *you* published that says what the fleet manager does about the second case. What does the contract promise about the retry?
6. **(Deeper than lesson Q3 — the pile's promise.)** The pile stores ids; the sender reads the board at send time. Say out loud what the WMS would be told if the pile stored a *snapshot* of the body instead, in the specific window between the mark and the send. Which sentence in the contract page you published forbids that, and why does a mark-time snapshot look perfectly correct right up until the moment it is wrong?
7. **(The dashboard seam.)** `PendingCount` is described in the lesson as M7's "words the WMS is owed" tile. Name the one thing that makes it under-report during a long pass, say which method empties the pile and when, and say what the tile would show an operator during a WMS outage.

**Lines and blocks that look unclear, copied, or unexplained — study these:**

- `src/FleetManager/WmsStatusPusher.cs:24-58` — the indentation steps. Three different widths in one method. Which width is the house, and which two did you inherit from somewhere else? (issue 3)
- `src/FleetManager/WmsStatusPusher.cs:38` — `if(response.IsSuccessStatusCode)`. Where does the space after `if` go in this repo? (issue 3)
- `src/FleetManager/WmsStatusPusher.cs:29, 40, 45, 51, 56` — the five log lines. Which one is missing the reason, and what is the *house* rule about `Console.WriteLine` versus `ILogger` (record 31 §I says the house wins — know why before you argue it). (issue 2)
- `src/FleetManager/WmsStatusPusher.cs:70` and `src/FleetCore/WmsStatusOutbox.cs:20-21, 31-32, 44` — the blank lines before the closing braces. What is the house's rule for a block's last line? (issue 7)
- `src/FleetCore/WmsStatusOutbox.cs:34-43` — `PendingCount` written as a property with a body rather than the `{ get; }` one-liner the assignment's requirement 3 literally shows. Why did you make that choice, and what would the one-liner have done to the first assert of every pile fact? (The hints' Tier-1 trap — the lesson's own requirement text and the lesson's own code sample disagree here; the professor should fix the requirement wording.)
- `src/FleetManager.Tests/WmsStatusPusherTests.cs:1-45` — the stub handler, at namespace scope, with no namespace around it. Why is it `internal`, and what would a second test file in this project that also wants a stub do? (issue 4)
- `src/FleetManager.Tests/WmsStatusPusherTests.cs:49-129` — the same twelve-line world, three times. (issue 7)
- `src/FleetManager/Program.cs:176` and `:184` — the two blank lines the additions left behind around the new block. (issue 7)

---

## Revisit list

- **Nullable reference types are a contract, not a suggestion.** `[NotNullWhen(true)]` on a `TryGet` out parameter is the idiom that makes "returned true ⇒ not null" visible to the compiler. Go back to `WmsOrderBoard.TryGet` and see exactly what it does *not* say. (blocker 1)
- **A clean build is a gate, and an incremental build can hide it.** Learn `dotnet build --no-incremental` and `dotnet clean`, and understand why a stale `obj/` made four warnings disappear. This will bite again the moment you add a file and only run the tests. (blocker 1)
- **Catch what you can handle — and know the blast radius of what you do not.** Three shapes were measured; what else can a socket throw, and what does each one cost when it escapes a background loop into a process that also runs the fleet? (issue 5)
- **Configuration validation is only as strong as the check.** `Uri.TryCreate(value, UriKind.Absolute, …)` is not "is this an http URL" — it accepts `ftp://`, `mailto:` and `file://`. Know what your validators actually accept. (issue 5)
- **The "read at send time" pattern.** A queue that holds *where the truth lives*, not a copy of the truth. Where else in this repo does that pattern appear (the board, the task board), and what breaks when you copy the value instead? (issue 6)
- **Log lines are written for a support engineer at 03:00**: the id, the word, **and the reason**. Audit the repo's other log lines against that bar and see which ones would fail it.
- **`Console.WriteLine` versus `ILogger`.** Record 31 §I chose the house over the skill, on purpose ("the pusher the only structured logger in a console-printing codebase… is a worse seam than matching"). Know the trade-off so you can argue it when the dashboard arrives.
- **A background loop's exception policy is a design decision, not a detail.** When a loop's `await` can throw, decide deliberately whether the loop dies, the process dies, or the work is requeued. (issue 5)
- **The ctor-guard idiom and its trap:** `x = x > TimeSpan.Zero ? x : throw …` — assign to the **field**, not the parameter. (issue 7)
- **The contract page is the product.** You wrote it; the mock-WMS slice will implement against it; the "at least once" and "current place, not a history" sentences are promises you must be able to defend.

---

## End-product check

This slice builds the sending half of M6's "orders flow … and back": the pile is a real piece of the end product — a `FleetCore` class with one lock, one flag per order, and a `PendingCount` that is already the dashboard tile M7 needs — and the sender is a real `FleetManager` component whose failure policy ("the truth is never given up on") is the policy a WMS integration actually needs. The structural choices hold up for the milestones ahead: the contract page is versioned and pinned, the route is a constant, the body is the same `WmsOrder` the door already serves, and no MQTT type was invented for a hop that was never MQTT. The risks it introduces are narrow but real. Today the pile is orphaned — nothing calls `NoteSync` in production, exactly as the lesson and record 31 say, so the loop ticks once a second over an empty pile and the sender's blast radius (issue 5) is dormant. The execution slice is what arms both: the moment the dispatcher notes a transition, an uncaught exception in one pass can kill the whole fleet manager and silently drop the words it had claimed. That is the seam to close *before* the words start flowing, not after. The second risk is quieter: the pile's promise — newest truth wins, ids not bodies — is currently a convention verified only by my scratch probe, and it is the promise the mock-WMS receiver will be built against. Pin it with one fact while the shape is still fresh.

---

## README

**Not updated — and this is unchanged by the follow-up.** The verdict is now a **pass**, so the "no change on a failing review" reason no longer applies; the second reason does, and it is the same one as in review 22:

- The milestone table in `README.md` has **no per-lesson rows** (M0–M8 only), so there is no M6/lesson-23 row to move, and no per-lesson "verified by" note to add. The `Verification notes` section below the table carries one note per **completed milestone** (M5, M4, M3, M2, M1, M0); M6 has no note because M6 is not done.
- **M6's done-when is not met:** *"orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state."* Nothing in `serve` moves a task yet, there is no receiver on `/api/order-status`, no cancel, and the door-hardening slice is still owed. The M6 row therefore correctly stays **`in progress`** — and per AGENTS.md a milestone is never marked done on a single lesson's pass.

So `README.md` was left **byte-for-byte untouched**. Verified with `git status --porcelain` (only this review file modified) after writing it.

*First-round note (kept for the record):* the same section said "not updated" for two reasons then — the `not yet` verdict, and the reasons above. The verdict reason is now gone; the other two still hold.
