# Lesson 06: The acknowledgement timeout and the retry that closes M2

**Milestone:** M2 (Message contract and transport) · **Language:** C#/.NET 10 · **Prerequisite:** [Lesson 05: The message contract and the broker boundary](05-message-contract-and-transport.md) · **Decision:** [record 10](../decisions/10-command-retry-and-ack-timeout.md) (the sequel to [record 09](../decisions/09-command-acknowledgement-and-idempotency.md))

**Concepts this assignment requires:**

- [ ] **The acknowledgement timeout** — the sender waits a bounded time for the ack, so a lost ack becomes
      a *fact* instead of a silence. Taught in
      [The acknowledgement timeout](#the-acknowledgement-timeout). Fixed by
      [record 10 §5](../decisions/10-command-retry-and-ack-timeout.md); built as a pure `RetryPolicy`.
- [ ] **Retry the same message** — on a timeout the sender re-sends the *identical* command, same
      `MessageId`, and the receiver's guard makes that safe. Taught in
      [Retry the same message](#retry-the-same-message). Fixed by
      [record 10 §4](../decisions/10-command-retry-and-ack-timeout.md); M3 reuses this sender.
- [ ] **Bounded tries and giving up** — a fixed number of attempts, then a definite failure, never an
      unbounded wait. Taught in [Bounded tries and giving up](#bounded-tries-and-giving-up). Fixed by
      [record 10 §8](../decisions/10-command-retry-and-ack-timeout.md); M5 turns the failure into a
      wire alarm.

Three new concepts. That is the cap.

Three words for later, not today: **alarm message** (a wire message with a severity an operator
acknowledges — contract v3, the alarms-and-recovery lesson), **heartbeat** (a periodic "I am alive"
from a shuttle — M5), and **exponential backoff** (spacing retries out further and further — the
change trigger in [record 10 §11](../decisions/10-command-retry-and-ack-timeout.md)).

---

## Where this fits

Lesson 05 made the shuttle a peer across a broker and built the **receiver's** half of reliability:
if the same command arrives twice, the work still runs once. This lesson builds the **sender's**
half: what the fleet manager does when the ack never comes.

The spec says M2 is done when two things are true:

> *"a command, an acknowledgement, and status updates make a full round trip, and tests show a
> repeated command does not run twice **and a lost acknowledgement is retried**."*

Lesson 05 proved the runtime round trip and the "does not run twice" half (review 05 mutation-proved
it). The second half — **retried** — is here. When lesson 06 passes review, M2's done-when is met.

Read the two halves together, because they only work as a pair:

| Half | Owner | Question it answers | Where it lives |
|---|---|---|---|
| Receiver (lesson 05) | the shuttle | "the same command arrived twice — did I move twice?" | `IdempotencyGuard` (record 09) |
| Sender (lesson 06) | the fleet manager | "no ack came — did I give up too soon?" | `RetryPolicy` + `ReliableCommandSender` (record 10) |

The sender resends. The receiver remembers. Neither works without the other. That is the whole
lesson in two sentences, and the rest of this page makes them real.

### Where this fits, in one picture

```
 attempt 1            attempt 2            attempt 3
 fleet manager        fleet manager        fleet manager
   │ publish cmd X      │ publish cmd X      │ publish cmd X
   │                    │                    │
   ▼                    ▼                    ▼
 ~~~~ack lost~~~~     ~~~~ack lost~~~~     ──▶ shuttle: guard! re-ack X
   │                    │                    │
   │ wait AckTimeout    │ wait AckTimeout    │ accepted ack arrives
   ▼                    ▼                    ▼
 backoff (500 ms) ──▶ backoff (500 ms) ──▶ done
```

Every try sends the **same** `MessageId` `X`. The shuttle runs the move once, on the first copy it
sees, and re-acks every later copy. The manager stops as soon as one ack comes back.

---

## Check your understanding

These are from lesson 05. If any is not quick, reread before starting.

1. An ack carries `CorrelationId` = the command's `MessageId`. Why is the envelope the only place
   that link can live, and what happens to the manager if it cannot match a reply to its command?
2. The guard records an id only when the command was **accepted**, and it is checked **before** the
   state machine. Trace what the shuttle does when it receives the same accepted command twice.
3. `FleetManager:RoundTripTimeoutMs` already bounds the one-shot `move` from lesson 05. In this
   lesson it becomes the bound on the *whole retry loop*. Why is an unbounded wait the same defect
   in a process as it was in the review-03 test?
4. Lesson 04 said "time is an input" and banned `Thread.Sleep` in the shuttle. This lesson adds a
   real wait — the backoff — in the **manager**. Why is that not the same banned thing? (Say who is
   waiting and for what.)

---

## Verification table

Everything below was checked by running a command or reading an installed file on **2026-10-10**.
The retry mechanism ran in a throwaway console toy at `/tmp/opencode/retrytoy`; the async timeout
facts ran in `/tmp/opencode/timeoutprobe`. Both are deleted afterwards. Read-only commands and file
reads were run against this repo; no experiment touched `src/`.

| Claim | How I checked | Result |
|---|---|---|
| Installed tools | `dotnet --version`; `docker --version` | `10.0.112`; Docker `29.8.1` |
| A same-id resend is retried, and the work still runs once | toy: the link drops the first reply; the sender resends the same id | `delivered after 2 attempt(s)`; ids reused `note-1, note-1`; receiver ran **1** time |
| A bounded sender gives up and reports failure | toy: the link never replies; `maxAttempts: 3` | `gave up after 3 attempt(s)`; receiver still ran **1** time |
| A **new** id per attempt makes the work run twice | toy mutation: `id + "-" + attempt` | ids `note-1-1, note-1-2`; receiver ran **2** times |
| A per-attempt wait can be bounded and detected | `await never.Task.WaitAsync(TimeSpan.FromMilliseconds(200))` | `TimeoutException after 216 ms` |
| A timer token cancels the same wait | `await never.Task.WaitAsync(new CancellationTokenSource(200ms).Token)` | `OperationCanceledException after 200 ms` |
| A pre-cancelled token wins over the timeout | `WaitAsync(TimeSpan.FromSeconds(5), cancelled.Token)` | `OperationCanceledException` |
| A zero backoff runs instantly (so tests do not sleep) | `await Task.Delay(TimeSpan.Zero)` | returned in `1 ms` |
| The bus has no retry knob to inherit | read `src/Messaging/IMessageBus.cs` | `PublishAsync(topic, payload, family, ct)` only |
| The receiver already dedupes by `MessageId` | read `src/Messaging/IdempotencyGuard.cs` and `src/ShuttleSim/ShuttleEndpoint.cs`; review 05c mutation-proved it | guard checked **before** `StartMove`; duplicate re-acked, not re-run |
| The contract stays v2 (no wire change) | read `docs/contract/v2.md` | `Version: 2`; kinds `moveCommand`/`commandAck`/`shuttleStatus` only |
| The overall bound kept its key and default | read `src/FleetManager/Program.cs` and `src/FleetManager/appsettings.json` | `FleetManager:RoundTripTimeoutMs = 10000`, validated positive |

Not yet verified:

- Any file the owner is about to write — this assignment *specifies* the retry; it is not a report on
  an implementation.
- A **lost-ack retry over a real broker** — the deterministic proof is the fake-bus test; the
  real-broker phase proves the integrated round trip and the give-up path.
- Exponential backoff with jitter — deferred on purpose ([record 10, Survey B](../decisions/10-command-retry-and-ack-timeout.md)).
- The wire alarm, recovery, and heartbeats — later M2/M5 lessons, contract v3.

---

## Options survey

Two real design questions here: *where does the retry logic live*, and *how does it space the
tries*. The full reasons are in [record 10](../decisions/10-command-retry-and-ack-timeout.md); here
is the compressed version.

### Survey 1: where the retry logic lives (→ [record 10 §3](../decisions/10-command-retry-and-ack-timeout.md))

| | A: a pure `RetryPolicy` + a `ReliableCommandSender` in the app (chosen) | B: a generic retrier inside `Messaging` | C: inline in `Program.cs` |
|---|---|---|---|
| Test with no broker | yes — pure policy, fake bus | yes | no — logic sits in `Main` |
| Numbers from config | a pure record built from config | same | literals, unless extracted |
| Same shape as lesson 05 | yes — policy pure, protocol type with its app | the transport library grows scheduling | no |

**Decision 1: a pure `RetryPolicy` record in `Messaging`, and a `ReliableCommandSender` in
`FleetManager` that loops around `IMessageBus`.** The policy is pure and unit-testable. The loop
needs a bus, so it lives with the fleet-manager app, exactly as `ShuttleEndpoint` lives with
`ShuttleSim`. `FleetManager.Tests` gains a direct `Messaging` reference and drives the loop with a
fake bus.

### Survey 2: the backoff shape (→ [record 10, Survey B](../decisions/10-command-retry-and-ack-timeout.md))

| | A: constant, from config (chosen) | B: exponential with jitter | C: retry immediately |
|---|---|---|---|
| Behaviour when the shuttle is briefly gone | a few evenly spaced tries | backs off harder; the standard for many clients | hammers the broker |
| Testability | exact; zero backoff runs instantly | needs a jitter source or fake clock | exact |
| Fits M2 (one manager, one command) | yes | more machinery than needed | a busy-wait in message form |

**Decision 2: constant backoff, `BackoffMs` from config (default 500).** Exponential backoff with
jitter is the right change when many managers share a broker; M2 has one manager, so the simple
shape is enough and easy to prove. The change trigger is recorded.

---

## The lecture

Today we are going to learn about three things: **the acknowledgement timeout**, **retrying the same
message**, and **giving up after a few tries**.

### The acknowledgement timeout

In lesson 05 the manager published one command and waited. If no ack came, it waited until the
overall bound ran out and then returned `1`. That is honest, but it has a hole. The manager cannot
tell **"the shuttle is slow"** from **"the ack was lost."** Both look the same: silence. And a
manager that cannot tell them apart can never decide to try again.

The fix is a small clock on each try. You send the command, then wait a bounded time — the
**acknowledgement timeout** — for the matching ack. If the timer runs out first, you now *know*
something: this try did not get an answer. That is the signal the retry needs.

A real-world picture: you phone a warehouse desk to confirm an order. You let it ring ten times.
No answer, so you hang up and call again. You do not hold the line forever, and you do not assume
the order is placed just because you dialled.

The timeout is a value, not a magic number, so it lives in configuration. The whole retry rule is
one small immutable record, pure and free of any broker:

```csharp
// src/Messaging/RetryPolicy.cs — pure, no I/O, no broker
public sealed record RetryPolicy(int MaxAttempts, TimeSpan AckTimeout, TimeSpan Backoff)
{
    public RetryPolicy
    {
        if (MaxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        if (AckTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(AckTimeout));
        if (Backoff < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(Backoff));
    }
}
```

Three fields, three questions answered: how many sends (`MaxAttempts`), how long one try waits
(`AckTimeout`), and how long between tries (`Backoff`). Because the type is pure, its rules can be
tested in microseconds with no broker, no Docker, and no clock.

The per-try wait in code is one line, and I measured its behaviour this session:

```csharp
// wait for the ack, but only for AckTimeout
try
{
    var ack = await waiter.Task.WaitAsync(policy.AckTimeout, cancellationToken);
    return ...;                      // an ack arrived
}
catch (TimeoutException)
{
    // no ack for THIS try; the retry loop decides what is next
}
```

Measured: `await never.Task.WaitAsync(TimeSpan.FromMilliseconds(200))` on a task that never
completes threw `TimeoutException after 216 ms`. Measured the other branch too: passing a
`CancellationToken` from a `CancellationTokenSource(200ms)` made the same call throw
`OperationCanceledException after 200 ms`. Two different throws for two different meanings — *this
try timed out* versus *the whole operation was cancelled* — and the code must treat them
differently. That distinction is exactly the two clocks in
[record 10 §5](../decisions/10-command-retry-and-ack-timeout.md): a small clock per try
(`AckTimeoutMs`), and the big clock from lesson 05 (`RoundTripTimeoutMs`) over the whole job.

Where you will meet this again: M5's heartbeat is the same idea aimed the other way. There, the
manager waits for a periodic "I am alive" and declares a shuttle unknown if the window passes
without one.

---

### Retry the same message

The timeout told you the try was lost. Now you send again. The single most important decision is
**what you send**, and it is not obvious. You send the **identical** message: same `MessageId`, same
`SentAt`, same body. Not a copy with a fresh id.

Why is that safe? Because lesson 05 built the guard. When the shuttle sees the same `MessageId` a
second time, `IdempotencyGuard.IsDuplicate` is true, so the endpoint **re-acks and returns before
`StartMove`**. The move runs once. The manager that never saw the first ack finally learns the
command took effect. This is the two halves meeting.

Why is it *required*? Imagine you send a fresh id each try. To the guard it is a brand-new command.
The shuttle tries to run it a second time. For today's `StartMove` that is only rejected ("cannot
start a move while Moving"). For a later `StartLoad` it is two pallets loaded, or a crash. The id is
what makes a resend a resend and not a second job.

A real-world picture: when a dispatcher re-sends a work order, they keep the **same order number**.
The forklift operator sees the same number and knows "this is the job I already have," not "a second
job." A new number would start a second job.

I measured this exact failure in the toy (`/tmp/opencode/retrytoy`). The honest sender reused one id
and the receiver ran the note **once**:

```
delivered after 2 attempt(s)
ids the sender reused: note-1, note-1
receiver ran the note 1 time(s)
```

Then I changed one thing — a new id per attempt — and the receiver ran it **twice**:

```
delivered after 2 attempt(s)
ids the sender reused: note-1-1, note-1-2
receiver ran the note 2 time(s)
```

One changed character, one double-run. That is why the envelope is built **once, before the loop**,
and every attempt publishes that same object:

```csharp
// FleetManager/ReliableCommandSender.cs — shape, not the whole body
var envelope = command.Envelope;          // built once, by the caller
for (var attempt = 1; attempt <= policy.MaxAttempts; attempt++)
{
    await bus.PublishAsync(topics.Command(command.ShuttleId),
        JsonSerializer.Serialize(command), MessageFamily.Command, cancellationToken);
    // ... wait AckTimeout for the ack; on success return; on timeout, back off and loop
}
```

Because `command` never changes, `JsonSerializer.Serialize(command)` produces the same bytes every
time. The `MessageId` is frozen for the life of this command.

Between tries the sender waits `Backoff` — the constant backoff from Survey 2. The change trigger
for something smarter (exponential with jitter) is many clients on one broker; one manager does not
need it yet. For tests, `Backoff` is set to zero, and I measured that `await Task.Delay(TimeSpan.Zero)`
returned in about 1 ms, so a retry test runs instantly.

**No wire change.** This is worth saying out loud, because "reliability feature" sounds like a
contract change and it is not. Contract v2 already has the two things a resend needs: a stable
`MessageId` and a `CorrelationId` on the ack. The retry changes *how often* the same v2 message is
sent, not *what* is in it. So `docs/contract/v2.md` stays at `Version: 2`, and there is no `v3.md`
from this lesson.

---

### Bounded tries and giving up

The last piece is the brake. The sender tries at most `MaxAttempts` times. If the last try also
times out, it **stops** and reports failure. It never loops forever.

An unbounded retry loop is an unbounded wait wearing a nicer name. That is the same defect the
reviewer called **R2** in `FleetManager.Tests/StartTests.cs` — a wait with no bound. In a test it
hangs a build; in a fleet manager it hangs a work order forever, and no operator ever learns the
command was lost. A fleet needs a definite "this did not land" so a higher layer can reassign the
work.

A real-world picture: you phone the desk three times. If all three go unanswered, you stop dialling,
write down "could not confirm," and escalate to a supervisor. You do not stay on the phone all
afternoon.

The result of a send is one small type, so the caller never has to guess what happened:

```csharp
// FleetManager/ReliableCommandSender.cs
public enum CommandSendStatus { Acknowledged, Rejected, NoAcknowledgement }

public sealed record CommandSendResult(
    CommandSendStatus Status,
    int Attempts,
    CommandAckMessage? Acknowledgement);
```

Now the three endings are clear:

- **`Acknowledged`** — an ack with `Accepted = true` arrived. Stop; the command took effect once.
- **`Rejected`** — an ack with `Accepted = false` arrived. Stop; report the reason. **Do not retry.**
- **`NoAcknowledgement`** — every try timed out. Stop; report failure.

The middle case is a decision worth saying twice. A **rejected** ack is not a **lost** ack. It is a
definitive answer: the shuttle got the command and refused it (an unknown lane, for example).
Resending it just re-sends the same refusal. So this lesson retries only when the ack is *missing*.
Deciding to re-issue a refused command in case the world changed is a task-lifecycle policy for M3,
not a transport rule.

The full loop, then, is small enough to read in one glance. This is the shape; you write every line:

```csharp
// FleetManager/ReliableCommandSender.cs — the loop
for (var attempt = 1; attempt <= policy.MaxAttempts; attempt++)
{
    await bus.PublishAsync(commandTopic, payload, MessageFamily.Command, ct);
    try
    {
        var ack = await waiter.Task.WaitAsync(policy.AckTimeout, ct);
        return ack.Accepted
            ? new CommandSendResult(CommandSendStatus.Acknowledged, attempt, ack)
            : new CommandSendResult(CommandSendStatus.Rejected, attempt, ack);
    }
    catch (TimeoutException)
    {
        if (attempt < policy.MaxAttempts)
            await Task.Delay(policy.Backoff, ct);   // constant backoff, from config
    }
}
return new CommandSendResult(CommandSendStatus.NoAcknowledgement, policy.MaxAttempts, null);
```

When the loop ends in `NoAcknowledgement`, the fleet manager prints a clear failure line (shuttle
id, command id, attempts) and exits non-zero. That is what "raise an alarm" means at M2: a definite,
visible failure. The **wire** alarm — an `AlarmMessage` with a severity an operator must acknowledge
— is contract v3 and arrives with the alarms-and-recovery lesson. Turning it into a wire message now
would be a contract change for a consumer (the dashboard) that does not exist until M7.

One more honest difference between the sim and a warehouse: in this sim a lost ack is rare and the
shuttle is never actually offline for long. In a real warehouse the link drops, the shuttle drives
into a dead spot, and the manager may retry a command the shuttle already ran. The guard is what
makes that safe, and the retry is what stops the manager from giving up while the shuttle is merely
out of range.

---

### Recap

An **acknowledgement timeout** is a small clock on each try. It turns silence into a decision:
*this try got no answer.* That is the only way the sender can know a lost ack from a slow shuttle.

**Retrying the same message** is safe because the receiver's guard remembers the id. The envelope
is built once, so every try carries the same `MessageId`; a fresh id would be a second command and
would run the work twice. No wire change — contract v2 already has what a resend needs.

**Bounded tries and giving up** keep the loop finite. `MaxAttempts` sends, then a definite
`NoAcknowledgement` result; a rejected ack stops immediately because it is an answer, not a
silence. The operator-facing alarm is a wire message for later.

---

## Worked example

A toy: a **courier who keeps re-reading the same note** until it is signed for. It is **not** the
assignment — no shuttle, no broker, no MQTT, no real waiting — and it exists to show the retry and
the idempotent receiver at a size you can hold in one glance. Do not copy it. The toy stays a toy.

Everything below ran in `/tmp/opencode/retrytoy` and was deleted afterwards. It is one console
project.

`Program.cs` (complete):

```csharp
// A toy: a courier keeps re-reading the SAME note id until it is signed for.
// It is not the assignment. No shuttle, no broker, no real waiting.

var receiver = new Receiver();
var link = new Link(repliesToDrop: 1);
var attempts = Sender.SendSameId(link, receiver, "note-1", maxAttempts: 3);
Console.WriteLine($"delivered after {attempts} attempt(s)");
Console.WriteLine($"ids the sender reused: {string.Join(", ", link.SeenIds)}");
Console.WriteLine($"receiver ran the note {receiver.Runs} time(s)");

var receiver2 = new Receiver();
var link2 = new Link(repliesToDrop: 99);
var attempts2 = Sender.SendSameId(link2, receiver2, "note-2", maxAttempts: 3);
Console.WriteLine(attempts2 == -1
    ? "gave up after 3 attempt(s)"
    : $"delivered after {attempts2} attempt(s)");
Console.WriteLine($"receiver ran the note {receiver2.Runs} time(s)");

// A receiver that runs each note id at most once (the idempotency guard, in miniature).
public sealed class Receiver
{
    private readonly HashSet<string> _done = new();
    public int Runs { get; private set; }

    public bool Receive(string id)
    {
        if (_done.Contains(id)) return false; // duplicate: re-ack, but do not run
        _done.Add(id);
        Runs++;
        return true;
    }
}

// A flaky link that drops the first `repliesToDrop` replies (lost acknowledgements).
public sealed class Link
{
    private int _repliesToDrop;
    public List<string> SeenIds { get; } = new();

    public Link(int repliesToDrop) => _repliesToDrop = repliesToDrop;

    public bool DeliverAndReply(Receiver receiver, string id)
    {
        SeenIds.Add(id);
        receiver.Receive(id);                 // the command always arrives
        if (_repliesToDrop > 0)
        {
            _repliesToDrop--;
            return false;                     // ... but the acknowledgement is lost
        }
        return true;
    }
}

public static class Sender
{
    public static int SendSameId(Link link, Receiver receiver, string id, int maxAttempts)
    {
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (link.DeliverAndReply(receiver, id))
                return attempt;
            // In the real sender a backoff wait goes here.
        }
        return -1; // gave up
    }
}
```

Run it:

```bash
cd /tmp/opencode/retrytoy
dotnet run
```

Observed output (exit 0):

```
delivered after 2 attempt(s)
ids the sender reused: note-1, note-1
receiver ran the note 1 time(s)
gave up after 3 attempt(s)
receiver ran the note 1 time(s)
```

Read the first scenario: the link drops the first reply, so the sender tries again with the **same**
`note-1`. The second reply arrives, delivery takes **2** attempts, and the receiver ran the note
**once** — its guard kept the duplicate from running. The second scenario: the link never replies,
the sender stops after **3** attempts, and the receiver still ran the note **once**, because every
later copy was a duplicate.

Now the mutation that shows the toy means something. Change the sender to use a **new id each
attempt**:

```csharp
if (link.DeliverAndReply(receiver, $"{id}-{attempt}"))
    return attempt;
```

Observed output:

```
delivered after 2 attempt(s)
ids the sender reused: note-1-1, note-1-2
receiver ran the note 2 time(s)
gave up after 3 attempt(s)
receiver ran the note 3 time(s)
```

The receiver now runs the note **twice**. A green run proves the toy passes its checks; the mutation
proves the checks would *notice* the wrong id. That is the reviewer's standard, and it is why one
changed character is worth measuring.

Your `ReliableCommandSender` is this toy with a real broker, a real `Pipeline`-free `async` wait
(`Task.WaitAsync(AckTimeout)`), a real `Task.Delay(Backoff)`, and ids that come off the envelope.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. The manager sends a command and gets no ack. What does the **acknowledgement timeout** tell it
   that the lesson-05 overall timeout did not? Why can a sender with only one timeout never decide
   to resend?
2. Why must a resend carry the **same `MessageId`**? Walk both directions: what the receiver's guard
   does with the same id, and what it would do with a fresh id on a later `StartLoad`.
3. A rejected ack (`Accepted = false`) is an answer; a missing ack is a silence. Why does the sender
   retry only the silence? What would go wrong if it retried the rejection too?
4. Retry adds a real wait (`Task.Delay(Backoff)`) in the fleet manager, but lesson 04 banned
   `Thread.Sleep` in the shuttle. Say who waits and for what in each case, and why they are not the
   same rule.
5. When `MaxAttempts` is reached with no ack, the manager exits non-zero and logs a line. Why is
   that better than retrying forever, and what is the difference between this local failure and the
   contract-v3 `AlarmMessage` that comes later?

---

## The assignment

Build the sending half of M2: a pure retry policy, a reliable command sender, and the config that
feeds them. This closes the spec's M2 done-when once review passes. Three new files, four config
keys, one project-reference change, and at least six tests.

### Requirements

1. **`src/Messaging/RetryPolicy.cs`** — pure, domain-free, no I/O:
   ```csharp
   public sealed record RetryPolicy(int MaxAttempts, TimeSpan AckTimeout, TimeSpan Backoff);
   ```
   Validate: `MaxAttempts >= 1`, `AckTimeout > TimeSpan.Zero`, `Backoff >= TimeSpan.Zero`. Throw
   `ArgumentOutOfRangeException` with the parameter name otherwise.

2. **`src/FleetManager/ReliableCommandSender.cs`** — the loop, in the fleet-manager app (not in
   `Messaging`, which stays free of command scheduling):
   - `public enum CommandSendStatus { Acknowledged, Rejected, NoAcknowledgement }`
   - `public sealed record CommandSendResult(CommandSendStatus Status, int Attempts, CommandAckMessage? Acknowledgement);`
   - `public sealed class ReliableCommandSender` with:
     - a constructor taking `IMessageBus`, `TopicScheme`, and `RetryPolicy`;
     - `public Task OnAckReceivedAsync(MessageReceived message, CancellationToken cancellationToken)`
       — parse the `CommandAckMessage`, match its `Envelope.CorrelationId` to a waiting command, and
       complete that wait; ignore an ack it cannot parse or does not recognise;
     - `public Task<CommandSendResult> SendAsync(MoveCommandMessage command, CancellationToken cancellationToken)`
       which:
       - subscribes a waiter for `command.Envelope.MessageId` **once**, before the loop;
       - for `attempt = 1 .. MaxAttempts`: publishes the **same** `command` to
         `topics.Command(command.ShuttleId)` at `MessageFamily.Command` (serialize the same object
         every time — do not rebuild the envelope inside the loop);
       - waits for the correlated ack with `waiter.Task.WaitAsync(policy.AckTimeout, cancellationToken)`;
       - on an ack with `Accepted = true` returns `Acknowledged`; with `Accepted = false` returns
         `Rejected` (no retry); on `TimeoutException`, if attempts remain, `await Task.Delay(policy.Backoff, cancellationToken)`
         and loop;
       - after the last attempt with no ack returns `NoAcknowledgement` with `Attempts = MaxAttempts`;
       - removes the waiter before returning, and lets a caller `cancellationToken` cancellation
         propagate as `OperationCanceledException`.

3. **Config** — `src/FleetManager/appsettings.json`, under the existing `FleetManager` section, add:
   ```jsonc
   "FleetManager": {
     "RoundTripTimeoutMs": 10000,
     "AckTimeoutMs": 2000,
     "MaxAttempts": 3,
     "BackoffMs": 500
   }
   ```
   Read them in `src/FleetManager/Program.cs`, validate them (all positive; `MaxAttempts >= 1`),
   and build a `RetryPolicy` from the milliseconds values. **No literal timeouts, retry counts, or
   topic strings in code** (AGENTS.md).

4. **Rewrite the `move` command in `src/FleetManager/Program.cs`** to use the sender:
   - keep the no-argument M0 behaviour (`Fleet Manager Coming Online`, exit 0);
   - build the `MoveCommandMessage` (and its envelope) **once**;
   - subscribe `topics.AllAcks` and route to `sender.OnAckReceivedAsync`, and subscribe
     `topics.AllStatuses` and keep the lesson-05 status handling;
   - create one overall `CancellationTokenSource` with `CancelAfter(RoundTripTimeoutMs)` and pass its
     token to `SendAsync` and to the status wait;
   - on `Acknowledged`: wait for at least one status from that shuttle, bounded by the overall token;
     print each received message as one JSON line; exit 0 if the status arrived, otherwise exit 1;
   - on `Rejected`: print the rejection reason to stderr, exit 1, **no retry**;
   - on `NoAcknowledgement`: print a failure line (shuttle id, command id, attempts), exit 1;
   - on the overall cancellation: exit 1.
   - The sender emits the contract messages; `Program.cs` still owns the console output.

5. **`src/FleetManager.Tests/FleetManager.Tests.csproj`** — add a direct project reference to
   `..\Messaging\Messaging.csproj` (the same edge `ShuttleSim.Tests` has, so the fake bus and
   `RetryPolicy` are visible without going through the app). Keep the existing `FleetManager` and
   `Contracts` references, and keep `StartTests.cs` green.

6. **`src/FleetManager.Tests/ReliableCommandSenderTests.cs`** — drive the sender with a **fake**
   `IMessageBus` that records published `(Topic, Payload, Family)` and (for the ack cases) can invoke
   `sender.OnAckReceivedAsync` with a crafted `CommandAckMessage`. No broker. At least:
   - `Lost_acknowledgement_is_retried_with_the_same_message_id` — the fake drops the first ack and
     acks the second publish; assert the result is `Acknowledged`, `Attempts == 2`, and **both**
     published `moveCommand` payloads carry the **same** `Envelope.MessageId`;
   - `Retries_stop_after_the_configured_attempts` — the fake never acks; assert the result is
     `NoAcknowledgement`, `Attempts == MaxAttempts`, and exactly `MaxAttempts` publishes happened;
   - `Rejected_acknowledgement_is_not_retried` — the fake acks `Accepted = false` on the first
     publish; assert `Rejected`, `Attempts == 1`, and exactly **one** publish;
   - `Accepted_acknowledgement_stops_retrying` — the fake acks accepted on the first publish; assert
     `Acknowledged`, `Attempts == 1`, exactly one publish.
   Use `AckTimeout` of a few milliseconds and `Backoff = TimeSpan.Zero` so the suite stays fast.
   Every `[Fact]` carries at least one `Assert`.

7. **`src/Messaging.Tests/RetryPolicyTests.cs`** — pure tests, no bus:
   - `RetryPolicy_rejects_fewer_than_one_attempt`;
   - `RetryPolicy_rejects_a_non_positive_ack_timeout`;
   - `RetryPolicy_rejects_a_negative_backoff`.

8. **Optional but recommended, from review 05:** clear **S1** (replace the fixed `Task.Delay(250)`
   in `Subscribe_receives_only_the_matching_topic` with a `TaskCompletionSource` hand-off) and **S2**
   (the file/class are already `MqttMessageBusTests`). Both sit on the seam this lesson extends.
   These are not blockers and are not part of the M2 done-when.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers are
asserted, not admired. **Read every line's purpose before you run it.**

```bash
# Phase 1 — the new types exist and the project graph stays right
test -f src/Messaging/RetryPolicy.cs || echo "missing RetryPolicy.cs"          # nothing
test -f src/FleetManager/ReliableCommandSender.cs || echo "missing sender.cs"  # nothing
grep -q 'sealed record RetryPolicy' src/Messaging/RetryPolicy.cs               # exit 0
grep -q 'enum CommandSendStatus' src/FleetManager/ReliableCommandSender.cs     # exit 0
grep -q 'record CommandSendResult' src/FleetManager/ReliableCommandSender.cs   # exit 0
grep -q 'ProjectReference Include="..\\Messaging\\Messaging.csproj"' \
  src/FleetManager.Tests/FleetManager.Tests.csproj                             # exit 0
grep -c '<Project Path' src/ShuttleFleet.slnx                                  # prints 12 (no new project)
grep -q 'MQTTnet' src/Messaging/Messaging.csproj && echo "Messaging must stay MQTT-free"  # nothing
```

```bash
# Phase 2 — all config values come from configuration, none from code
for k in AckTimeoutMs MaxAttempts BackoffMs RoundTripTimeoutMs; do
  grep -q "$k" src/FleetManager/appsettings.json || echo "missing config key: $k"; done  # nothing
grep -rnE '"shuttle/[A-Za-z0-9+_/-]+"' src --include='*.cs' \
  --exclude-dir='*.Tests' --exclude-dir='obj' --exclude-dir='bin'              # nothing: no topic literals
grep -rniE '127\.0\.0\.1|localhost|:1883|Host=' src --include='*.cs' \
  --exclude-dir='*.Tests' --exclude-dir='obj' --exclude-dir='bin'              # nothing: addresses are config
# The shuttle and the pure logic still NEVER sleep. Only the manager's retry backoff may wait.
grep -rnE 'Thread\.Sleep|Task\.Delay' src/ShuttleCore src/ShuttleSim src/Contracts src/Messaging \
  --include='*.cs' --exclude-dir='*.Tests' --exclude-dir='obj' --exclude-dir='bin'  # nothing
grep -rn 'Task.Delay' src/FleetManager --include='*.cs' --exclude-dir='*.Tests' \
  --exclude-dir='obj' --exclude-dir='bin' | grep -c 'ReliableCommandSender.cs'  # prints 1
grep -q 'policy.Backoff' src/FleetManager/ReliableCommandSender.cs             # exit 0: the delay is configured
grep -rn 'WaitForExit()' src/FleetManager.Tests --include='*.cs'               # nothing: R2 stays fixed
for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do
  grep -q 'Assert' "$f" || echo "$f"; done                                     # nothing: no empty tests
```

The sleep gate now has **two halves**, and the split is the point. The **shuttle side**
(`ShuttleCore`, `ShuttleSim`, plus the pure `Contracts`/`Messaging`) still never sleeps: time is an
input, lesson 04's rule. The **manager side** is allowed exactly one `Task.Delay`, and the gate
proves it is in `ReliableCommandSender.cs` and uses `policy.Backoff` — a configured wait, not a
hard-coded one. If you write `Task.Delay(500)` anywhere, or a `Task.Delay` in the shuttle, the gate
goes red.

```bash
# Phase 3 — from src/: build, run the suite, walk the new names
cd src
dotnet build                                                                  # exit 0, 0 warnings
T=$(mktemp); dotnet test > "$T" 2>&1                                          # exit 0 (read $? here)
grep -c 'Passed!' "$T"                                                        # prints 6
grep -c 'No test is available' "$T"                                           # prints 0
rm -f "$T"
L=$(mktemp); dotnet test --list-tests > "$L" 2>&1
for t in Lost_acknowledgement_is_retried_with_the_same_message_id \
         Retries_stop_after_the_configured_attempts \
         Rejected_acknowledgement_is_not_retried \
         Accepted_acknowledgement_stops_retrying \
         RetryPolicy_rejects_fewer_than_one_attempt \
         RetryPolicy_rejects_a_non_positive_ack_timeout \
         RetryPolicy_rejects_a_negative_backoff; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"; done                     # nothing
rm -f "$L"
cd ..
```

The `Passed!` count stays **6** — the retry adds tests to two existing project suites
(`Messaging.Tests`, `FleetManager.Tests`); it does not add a project. The master spec's M2
done-when is not proven by a single test here; it is the pair of
`Duplicate_move_command_is_acknowledged_but_not_run_twice` (lesson 05, receiver) and
`Lost_acknowledgement_is_retried_with_the_same_message_id` (this lesson, sender).

```bash
# Phase 4 — no regression: the M1 demo and the manager's start line still hold
cd src
F=$(mktemp); dotnet run --project ShuttleSim/ShuttleSim.csproj > "$F" 2>&1     # exit 0
grep -c '"ShuttleId"' "$F"                                                    # prints 7 (M1 demo unchanged)
rm -f "$F"
G=$(mktemp); dotnet run --project FleetManager/FleetManager.csproj > "$G" 2>&1 # exit 0
grep -q 'Fleet Manager Coming Online' "$G"                                    # exit 0
rm -f "$G"
cd ..
```

```bash
# Phase 5a — the round trip over the real broker, with NO pre-sleep (lesson 05 had `sleep 3`)
docker compose -f docker/compose.yml up -d broker                             # exit 0
sleep 2
cd src && dotnet build ShuttleFleet.slnx >/dev/null 2>&1 && cd ..
S=$(mktemp)
dotnet src/ShuttleSim/bin/Debug/net10.0/ShuttleSim.dll serve > "$S" 2>&1 &
SHUTTLE_PID=$!
M=$(mktemp)
dotnet src/FleetManager/bin/Debug/net10.0/FleetManager.dll move SH-01 A01-L02 > "$M" 2>&1
MOVE_EXIT=$?
kill "$SHUTTLE_PID" 2>/dev/null; wait "$SHUTTLE_PID" 2>/dev/null
docker compose -f docker/compose.yml down >/dev/null 2>&1
echo "move exit=$MOVE_EXIT"                                                   # prints move exit=0
grep -q '"MessageType":"commandAck"' "$M"                                     # exit 0
grep -q '"Accepted":true' "$M"                                                # exit 0
grep -q '"State":"moving"' "$M"                                               # exit 0
grep -q '"State":"idle"' "$M"                                                 # exit 0
grep -c '"State":"moving"' "$S"                                               # prints 1: ran once, not twice
grep -c '"State":"idle"' "$S"                                                 # prints 1: ran once, not twice
rm -f "$S" "$M"
```

The `sleep 3` is gone: the sender now resends until the ack arrives, so the ordering that lesson 05
had to fake is the sender's job. The two `grep -c` lines on `$S` are the idempotency check under a
possible duplicate delivery — the shuttle publishes exactly one `moving` and one `idle`, never a
second pair, even if the manager tried more than once.

```bash
# Phase 5b — prove a lost command is retried over the real broker (manager first, shuttle second)
docker compose -f docker/compose.yml up -d broker                             # exit 0
sleep 2
cd src && dotnet build ShuttleFleet.slnx >/dev/null 2>&1 && cd ..
# A shorter ack timeout and more attempts, overridden by environment (the app layers env over the file)
export FleetManager__AckTimeoutMs=1000 FleetManager__MaxAttempts=6 FleetManager__BackoffMs=500
M2F=$(mktemp)
dotnet src/FleetManager/bin/Debug/net10.0/FleetManager.dll move SH-01 A01-L02 > "$M2F" 2>&1 &
MANAGER_PID=$!
sleep 2                                                                        # manager's first attempts land with no shuttle
S2=$(mktemp)
dotnet src/ShuttleSim/bin/Debug/net10.0/ShuttleSim.dll serve > "$S2" 2>&1 &
SHUTTLE_PID=$!
wait "$MANAGER_PID"; MOVE2_EXIT=$?
kill "$SHUTTLE_PID" 2>/dev/null; wait "$SHUTTLE_PID" 2>/dev/null
docker compose -f docker/compose.yml down >/dev/null 2>&1
unset FleetManager__AckTimeoutMs FleetManager__MaxAttempts FleetManager__BackoffMs
echo "move exit=$MOVE2_EXIT"                                                   # prints move exit=0
grep -q '"Accepted":true' "$M2F"                                               # exit 0
grep -c '"State":"moving"' "$S2"                                               # prints 1: retried but ran once
rm -f "$M2F" "$S2"
```

Phase 5b is the real-broker retry proof: the manager starts when no shuttle is listening, so its
first deliveries are lost, and the shuttle comes up a moment later. Retry carries the command in.
The shuttle still runs it once. **Make the sender log each attempt** (for example `attempt 2 of 6`
to stderr) so the log shows the resend happened; the acceptance greps the manager output for it.
This phase is timing-based by nature — the deterministic lost-ack proof is the fake-bus test in
Phase 3, and 5b is the integration check on top.

```bash
# Phase 6 — the contract is untouched and the fresh container still passes
grep -qE '^Version: 2$' docs/contract/v2.md                                   # exit 0
grep -qE '^Version: 1$' docs/contract/v1.md                                   # exit 0
test ! -f docs/contract/v3.md || echo "no wire change: v3.md should not exist"  # nothing
T=$(mktemp); docker build -f docker/Dockerfile.build -t shuttle-fleet-build . >"$T" 2>&1
                                                                              # exit 0
rm -f "$T"
U=$(mktemp); docker run --rm shuttle-fleet-build >"$U" 2>&1                   # exit 0
grep -c 'Passed!' "$U"                                                        # prints 6
rm -f "$U"
```

### Stretch goal

Make the **retry schedule** a pure, testable function instead of an inline `Task.Delay`. Add a method
to `RetryPolicy` — for example `public TimeSpan DelayBefore(int attempt)` — that returns the backoff
for the next attempt, and have `ReliableCommandSender` call it. Write one test that asserts the
delay for each attempt without sleeping at all, and one mutation note: if `DelayBefore` returns the
same value for every attempt, the test still passes (constant backoff is the current rule) — so the
test is about *the schedule being where the policy says*, not about growth. The point: a future
exponential backoff changes one pure function and one test, not the loop. Accept: all test suites
stay green and the loop reads as `await Task.Delay(policy.DelayBefore(attempt + 1), ct)`.

Accept-as-stretch (only if Phase 5b is flaky on your machine): replace the fixed `sleep 2` with a
short loop that waits for the shuttle's `listening on` line before the manager gives up. That is a
test-harness improvement, not a product change, and it is the same "deterministic hand-off" lesson
review 05 gave for `Task.Delay(250)`.

### Not in this assignment

- **The `AlarmMessage` on the wire, recovery messages, heartbeats** — later M2/M5 lessons, contract
  v3. At M2 the give-up is the local failure outcome in requirement 4.
- **Exponential backoff with jitter** — deferred; change trigger in
  [record 10, Survey B](../decisions/10-command-retry-and-ack-timeout.md).
- **Task queue, persistence, database** — M3, record 03.
- **Traffic, segments, reservations** — M4. **WMS REST** — M6. **Dashboard** — M7. **Load test** — M8.
- **TLS, credentials, ACL** — deferred by record 02; the dev broker stays anonymous on loopback.
- **S1/S2** from review 05 remain should-fix, not blockers; requirement 8 offers them.

**Carried from review 04, for the reviewer's list:** **C1** (`Rack.LaneId`/`SlotId` vs the lesson's
`LaneIds`/`SlotIds`) and **C2** (weaker `RackTests`) remain open; **C3** (the rejected-command
reprocess branch has no test) and **C4** (`src/ShuttleCore/Shuttle.cs` has no trailing newline)
remain; none are this lesson's blockers.

---

## Closing M2

Read the spec's M2 line once more: *"tests show a repeated command does not run twice **and a lost
acknowledgement is retried**."* Lesson 05 made the first true. This lesson makes the second true,
and it makes them work **together**: the sender resends the same id, the receiver's guard refuses to
run it twice. When this passes review, every part of the M2 done-when is met.

Who says "done"? Not you, and not I. AGENTS.md gives the reviewer that line: the milestone table in
`README.md` moves only when the review passes, with the verifying command written beside it. The
reviewer must verify the **whole** M2 done-when, not just this lesson's half: the round trip, the
no-double-run (review 05's mutation), and the lost-ack retry (the fake-bus test plus Phase 5b), with
`v1.md` and `v2.md` untouched and both idempotency mutations still red when broken. Be ready to say,
without notes, why a resend keeps its `MessageId`, why the sender retries a silence but not a
rejection, and why `Messaging` still cannot see `MQTTnet`.
