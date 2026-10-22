# Decision Record 32: The nullability contract of `WmsOrderBoard.TryGet` lives on the signature

**Date:** 2026-10-22
**Milestone:** M6 (WMS integration) — lesson 24: the execution slice.
**Status:** Accepted. Append-only. Written because review 23 found a hole in the teaching material and asked the professor to name the owner of the fix: *"Decide, in a decision record (append-only — a new record, not an edit of 31), where the nullability contract of `WmsOrderBoard.TryGet` lives… The record should say which slice owns it, so the next student is not handed the same trap."* ([review 23](../reviews/23-status-push.md), "Lesson defect"). Builds on [record 29](29-wms-order-to-task-mapping.md) §3 (task id = order id) and §5 (the pair lives in memory, lost together), and [record 31](31-status-push.md) §5 (the pile keys on `Advanced`) and §10 (the honest limits).
**Implements:** nothing new in the spec. It closes the last defect review 23 raised against lesson 23, and it fixes the trap before the execution slice sets it again: the execution slice is the first slice where a **production** caller depends on `TryGet` and on `SyncFromTask` in new places.
**Wire contract:** unchanged. `ContractVersion.Current` stays `4`; `src/Contracts/` and `docs/contract/` do not move. An attribute on a method signature is not a wire change.
**Replaced by:** any later record that changes `WmsOrderBoard.TryGet`'s shape (for example, a `TryGet` that can return `true` with a null order — see "What would change this"). Append-only.

---

## 1. What we are deciding

`WmsOrderBoard` hands out its orders with a `TryGet`:

```csharp
public bool TryGet(string orderId, out WmsOrder? order)
```

Two things are true at the same time:

1. **At runtime**, `true` always means "here is the order, and it is not null". Measured today — the dictionary only ever holds constructed `WmsOrder` values, never null.
2. **At compile time**, the compiler cannot see that. It sees `out WmsOrder?` and a `bool` with no link between them. So every `order.State` after the guard is a *possible* null dereference, and the compiler says so.

Measured today (probe `/tmp/opencode/l24probe`, against the committed tree):

```
TryGet(present) -> True, order is null? False
TryGet(absent)  -> False, order is null? True
```

and the same probe's build:

```
/tmp/opencode/l24probe/Program.cs(16,23): warning CS8602: Dereference of a possibly null reference. [/tmp/opencode/l24probe/l24probe.csproj]
    1 Warning(s)
    0 Error(s)
```

So the code is *correct* and the *contract* is *invisible*. This record decides where that contract is written down, so that the next slice is not handed the same trap.

---

## 2. Decision A: the contract lives on the signature — `[NotNullWhen(true)]` on the out parameter

**Decision A.** `WmsOrderBoard.TryGet` declares its promise in its own signature:

```csharp
public bool TryGet(string orderId, [NotNullWhen(true)] out WmsOrder? order)
```

Options:

- **A: annotate the out parameter with `[NotNullWhen(true)]`.** (chosen)
- **B: keep the widened call-site guard `if (!TryGet(id, out var order) || order is null)` at every call site.**
- **C: change the method to `WmsOrder? TryGet(string orderId)` and drop the `bool`.**
- **D: copy the values you need into non-nullable locals right after the guard.**

**Decision A.** Reasons:

1. **The signature is where a promise belongs.** `[NotNullWhen(true)]` says exactly one thing: "if this method returns `true`, the out parameter is not null". That is the truth (measured above), written where the compiler can read it.
2. **It repairs every call site at once.** The contract lives in one place, so a new caller cannot forget it. Option B and option D both push the contract into the callers — and a caller who forgets it gets a warning they will learn to ignore.
3. **It is the C# idiom for this exact shape.** `TryGet` is a framework-wide pattern (`Dictionary.TryGetValue`, `TryParse`); the framework annotates it the same way.
4. **It removes the noise instead of hiding it.** Option B's `|| order is null` is *dead code* against today's board — the review said so, and said it "should be deleted, not left as decoration" once the board is opened up. Option A deletes it by making it unnecessary.

**Rejected B, stated fairly.** It needs no change to `FleetCore`, it is one clause, and it is what the owner did in lesson 23 — the review passed that fix. It pays with a contract that lives in the wrong place: the *caller* now carries a promise the *callee* should be making, and the compiler still cannot check anything. It also grows: every future caller of `TryGet` pays the same tax.

**Rejected C.** `TryGet` means "did I find it?", and the `bool` is that answer. Dropping it makes `TryGet` return a nullable order, which is just `GetOrDefault` with a worse name, and it changes every call site in the codebase (the pusher, the lesson's own shapes) for no gain.

**Rejected D.** Copying `var orderId = order.OrderId;` into locals is the same defect as B with an extra step, and it re-introduces the warning the moment the copy line is wrong.

**What would change this:** if the board could ever hand back `true` with a null order — for example if a future "cancelled order" were stored as a null placeholder — then the annotation would be a lie and must be removed, and the call-site guard (option B) becomes the truth. Today the dictionary holds only constructed `WmsOrder` values, so that cannot happen without a change to `Submit`.

---

## 3. Decision B: the execution slice owns `src/FleetCore/WmsOrderBoard.cs`

**Decision B.** The file `src/FleetCore/WmsOrderBoard.cs` is opened by **the execution slice (lesson 24)**, and by no other slice until then. Inside it, this slice changes exactly two things:

1. It adds `using System.Diagnostics.CodeAnalysis;` and the `[NotNullWhen(true)]` attribute — one line on the signature.
2. It adds **nothing else**. No new method, no new field, no behaviour change.

**Why this slice:**

1. **The execution slice is the first slice with new production callers of this file.** Lesson 23's pusher already calls `TryGet`, and the execution slice adds two more production callers: the transition line (which calls `SyncFromTask`) and the tick (which reads the order's lane). Each new caller is a new chance to trip the same hole.
2. **The transition line is the reason the file is open anyway.** `SyncFromTask` lives in `WmsOrderBoard`. The slice that first calls it in production is the slice that owns the file's contract.
3. **It is one line, in a file the slice already owns.** The alternative — deferring the attribute to a later slice — hands the next student the same trap the review told us to remove, and leaves the production code carrying a `|| order is null` clause that a future reader cannot explain.

**What this slice also does with the file:** nothing. The board's behaviour, its lock, its `Submit` rules and its ladder are untouched.

**The rule for the slice that opens a file:** if a file carries a contract that the compiler cannot see, and the slice that opens the file is the slice that adds the callers, then the slice that opens the file is the slice that fixes the contract. Deciding this per file, in a record, is the point.

---

## 4. The rule at the call site until the attribute lands

Between lesson 23 and the execution slice, the only production caller is the pusher, and it already uses the widened guard:

```csharp
if (!_board.TryGet(orderId, out var order) || order is null)
```

That line is correct today and stays correct after the attribute lands. The execution slice may simplify it to `if (!_board.TryGet(orderId, out var order))` — **only** once the attribute is in the same commit, and only if the build stays warning-free. The lesson says which order to do it in: the attribute first, then the simplification, then the cold build.

---

## 5. Verification (measured 2026-10-22)

| # | Claim | How | Result |
|---|---|---|---|
| 1 | `true` never comes with a null order | probe `/tmp/opencode/l24probe` against the committed tree | `TryGet(present) -> True, order is null? False` / `TryGet(absent) -> False, order is null? True` |
| 2 | The compiler cannot see that truth today | same probe, `dotnet build` | **1 Warning(s)** — `CS8602` at `Program.cs(16,23)` |
| 3 | The attribute removes the warning at the same call site | scratch copy of `src/FleetCore` with `[NotNullWhen(true)]` added (`/tmp/opencode/l24probe2`), same probe program, `dotnet build` | **0 Warning(s), 0 Error(s)** — the call site was not changed |
| 4 | The committed tree builds clean | fresh copy of `src` (`git archive HEAD src`) in `/tmp/opencode/l24base`, `dotnet build` | **0 Warning(s), 0 Error(s)** |
| 5 | The pure suite is green before the slice | `cd src && dotnet test ShuttleFleet.slnx -v m --filter "Category!=Integration"` | **Failed: 0, Passed: 176, Skipped: 0, Total: 176** |

Scratch directories `l24probe`, `l24probe2`, `l24base` are deleted after the lesson is written.

---

## 6. What this record unblocks

- The execution slice (lesson 24) can add production callers to `WmsOrderBoard` without the nullability trap, and the trap is removed for good, not worked around.
- The next reviewer can check one grep — `grep -q "NotNullWhen" src/FleetCore/WmsOrderBoard.cs` — instead of reading every call site.
- The lesson-23 material defect (the skeleton and hints §3.2 that dereference `order` after a plain guard) is fixed in the lesson files themselves, so a student who transcribes lesson 23 today gets a warning-free build.

**What this record does not do.** It does not change how the pusher behaves, it does not change the REST contract, and it does not change the MQTT contract. It is one attribute and one rule about who owns a file.
