# Lesson 05: The message contract and the broker boundary

**Milestone:** M2 (Message contract and transport) · **Tooling:** .NET 10 + Eclipse Mosquitto 2.0 · **Prerequisite:** [Lesson 04: The warehouse model and one shuttle](04-warehouse-model-and-one-shuttle.md)

**Concepts this assignment requires:**

- [ ] **A message that knows itself** — every message on the wire carries a small envelope
      (contract version, kind, a unique id, the id of the message it answers, sender, time) so a
      receiver can version it, route it, and match a reply to its cause. Taught in
      [A message that knows itself](#a-message-that-knows-itself). Fixed by decision record 07;
      used by `src/Contracts/` and by the mock WMS in M6.
- [ ] **The broker is a process boundary** — the fleet manager and the shuttle are two programs
      that do not call each other; one publishes, the other subscribes through a broker. Topics
      are the routes, built in one place from configuration, and at-least-once delivery is the
      cost and the point. Taught in [The broker is between them](#the-broker-is-between-them).
      Fixed by records 07 and 08; this is the seam every later milestone crosses.
- [ ] **Same command, once** — because at-least-once delivery can deliver a message twice, the
      receiver must remember the *effects* it has already applied and not apply them again.
      Taught in [Same command, once](#same-command-once). Fixed by record 09; M5's recovery and
      M3's task tracking both stand on it.

Three new concepts. That is the cap.

Three words for later, not today: **retry** (resending a command nobody acknowledged — lesson 06,
this milestone), **alarm and recovery** (the rest of M2's message set, contract v3, a later
lesson), **heartbeat** (a periodic status that proves a shuttle is alive — M5). All three belong
to records linked in [Options survey](#options-survey).

---

## Where this fits

M1 was one program that called itself: a rack, a shuttle, and seven method calls. M2 is the
milestone where the shuttle stops being a function and becomes a **peer across a broker**. The
spec says:

> **M2: Message contract and transport.** Define the commands, status messages, alarms, and
> recovery messages. Add MQTT topics. The shuttle speaks the contract. A fleet manager skeleton
> connects, sends a command, and receives status. Handle duplicate messages, lost messages, and
> timeouts.
> *Done when:* a command, an acknowledgement, and status updates make a full round trip, and tests
> show a repeated command does not run twice and a lost acknowledgement is retried.

The spec parts M2 into two ideas: *define the contract*, and *make it travel*. This lesson does
the first round trip and nothing more:

1. **Contract v2.** `ShuttleStatus` already exists (v1). v2 adds an envelope and three messages:
   a **move command**, its **acknowledgement**, and an enveloped **status**. Alarms and recovery
   are a *later* M2 lesson and a *later* contract version — this lesson does not invent them.
2. **Transport and round trip.** Two programs, one broker, one command, one ack, one status.
3. **Idempotency.** At-least-once delivery repeats messages; the shuttle must move once anyway.

**What this lesson deliberately leaves for lesson 06:** the *sending* half of reliability. A
command that is never acknowledged, the timeout that notices, the retry that resends it, and the
alarm when retries run out. This lesson builds the receiver and the round trip that lesson 06
will retry against. **M2 does not close here.**

Three words for the near future, not today: **retry**, **timeout**, **alarm** — all lesson 06,
record 09's sequel.

---

## Where this fits, in one picture

```
 FleetManager  app                 broker                ShuttleSim  app
 ┌──────────────┐    publish   ┌────────────┐  deliver  ┌──────────────┐
 │ move cmd ────┼─────────────▶│ shuttle/   │──────────▶│ ShuttleEndpoint
 │              │   QoS 1      │ SH-01/     │  QoS 1    │   │ start move
 │ subscribe ───┼──────────────│ command    │           │   │ publish ack
 │ shuttle/+/ack│◀── ack ──────│            │◀──────────┼───┘ publish status
 │ shuttle/+/.. │    status    └────────────┘           └──────────────┘
 └──────────────┘
```

The fleet manager and the shuttle never reference each other's types and never share a process.
The only things they share are `Contracts` (the shapes on the wire) and the broker.

---

## Check your understanding

These are from lessons you already did; if any is not quick, reread before starting.

1. Lesson 02 said the message contract is the source of truth and changing it needs a record and a
   version bump. `v1.md` holds exactly one message. If you now wrap it in an envelope, is that a
   *change* to `ShuttleStatus`? What file records the new version, and who may write it?
2. Lesson 04 taught fault is a sink and time is passed in. When a command arrives, what is the
   shuttle's `now` — and why would `Thread.Sleep` be banned again in a networked app?
3. Review 03 left R2 in `FleetManager.Tests/StartTests.cs`: a child process waited on with no
   bound. If a broker is down, what does an unbounded wait look like in production? The fix for
   the test and the reason for `FleetManager:RoundTripTimeoutMs` are the same idea.
4. The M1 demo prints `ShuttleStatus` as JSON. If the same five-field payload is now nested inside
   a `status` message, in which project does the *unwrapped* type live, and what shape changes?

---

## Verification table

Everything below was checked by running a command on **2026-10-09**. The JSON round trip and the
two-step parse ran in `/tmp/opencode/m2-verify/Scratch`; the broker checks ran against a
throwaway `eclipse-mosquitto:2.0` started from `/tmp/opencode/m2-verify/compose.yml`; the
pure-logic gate proof ran in `/tmp/opencode/gates`. All three are deleted with the scratch. On
this repo I ran read-only commands; no experiment touched `src/`.

| Claim | How I checked | Result |
|---|---|---|
| Installed tools | `dotnet --version`; `docker info --format '{{.ServerVersion}}'` | `10.0.112`; daemon up |
| `MQTTnet` is the right client, and it is MIT | `dotnet package search MQTTnet`; read the v5 nuspec + shipped `LICENSE`, and the v4 nuspec | latest `5.2.0.1603`; v5 nuspec `<license type="file">`, `LICENSE` = MIT; v4 `<license type="expression">MIT` |
| QoS 1 really can deliver a duplicate | QoS-1 publish of one message twice; count on the subscriber | **received twice** — dedupe is mandatory |
| A one-level wildcard does not over-match | subscribe `shuttle/+/ack`; publish to `shuttle/SH-01/ack` and `…/ack/extra` | first received, second not |
| Retained messages reach a late subscriber | publish retained, subscribe after | delivered with `Retain=true`; clearing works |
| The v2 envelope round-trips | serialize/deserialize `MoveCommandMessage`, `CommandAckMessage`, `ShuttleStatusMessage` with default options | fields survive, PascalCase wire names preserved |
| A reply can be matched to its cause | `CorrelationId` = the command's `MessageId`; compare on receive | matched |
| The kind can be read before the body | parse `Envelope.MessageType`, then deserialize the concrete type | works for all three kinds |
| An in-process broker works for tests, no Docker | `MQTTnet.Server` on an **ephemeral port** (`WithDefaultEndpointPort(0)`, then read `ServerOptions.DefaultEndpointOptions.Port` after `StartAsync`) | round trip `received=1`; port discovered |
| The config file + env override bind | bind the `Messaging` section, including a nested QoS enum; run with `Messaging__Port=9999` | bound; override took effect |
| A bounded recent-id cache is satisfiable **and** mutation-sensitive | reference `TopicScheme`/`IdempotencyGuard`, 6 tests, then 3 mutations | 6/6 pass; FIFO→LIFO, a filter typo, and "always duplicate" each turned a test **red** |
| `v1.md` stays untouched | `grep -qE '^Version: 1$' docs/contract/v1.md` | exit 0 |

Not yet verified:

- Any file the owner is about to write — this assignment *specifies* v2 and the transport; it is
  not a report on an implementation.
- Whether the owner's machine can run the broker and the two apps together (Phase 5) — the same
  Docker readiness lesson 03 measured.
- Retry, ack timeout, alarms, recovery, heartbeats — later M2/M5 lessons by design.

---

## Options survey

Three records, because M2's "define the messages" and "make it travel" raise three real design
questions: *what does a message carry*, *how is broker I/O kept out of the logic*, and *how does a
receiver avoid doing work twice*. The records carry the full reasons; here is the compressed
version.

### Survey 1: how a message carries its metadata and routes (→ [record 07](../decisions/07-message-contract-v2-envelope-and-topics.md))

| | A: a nested envelope + `{root}/{shuttle}/{kind}` topics (chosen) | B: fields flattened into each message | C: metadata in the topic only |
|---|---|---|---|
| A reply pointing at its cause | a `CorrelationId` field | a field per message | impossible — MQTT has no reply-to |
| Read kind/version before the body | yes | no | kind yes, id/time no |
| One place to add a field | the envelope | every message | n/a |

**Decision 1: an `Envelope` record nested in every message; topics built by one `TopicScheme` as
`{root}/{shuttleId}/{kind}`.** Records are fixed: `MessageEnvelope`, `MoveCommandMessage`,
`CommandAckMessage`, `ShuttleStatusMessage`, the `MessageTypes` constants, and
`ContractVersion.Current = 2`. `ShuttleStatus` itself is unchanged — v2 wraps it.

### Survey 2: how the broker stays out of the decision logic (→ [record 08](../decisions/08-mqtt-client-qos-and-configuration.md))

| | A: an `IMessageBus` interface + an MQTT adapter (chosen) | B: MQTT calls inside `ShuttleCore` | C: one console app doing everything |
|---|---|---|---|
| Test the logic with no broker | yes (a fake bus) | no | no |
| Swap the client later | one file | everywhere | — |
| `ShuttleCore` stays pure | yes | broken | broken |

**Decision 2: a domain-free `Messaging` project (`IMessageBus`, `TopicScheme`,
`IdempotencyGuard`, `MessageEnvelopes`), an `MqttMessaging` adapter that is the only project
referencing `MQTTnet`, and decision logic tested through a fake bus.** QoS 1 (at-least-once) for
all three families; host, port, root, ids, and QoS come from `appsettings.json` + environment
variables, never literals.

### Survey 3: how the shuttle avoids doing the work twice (→ [record 09](../decisions/09-command-acknowledgement-and-idempotency.md))

| | A: a bounded cache of recent accepted ids (chosen) | B: rely on MQTT QoS 2 | C: ignore duplicates |
|---|---|---|---|
| Duplicates across a reconnect | handled | **not** handled (QoS 2 is per link) | "moves twice" |
| Remember *effects*, not messages | yes | no | no |
| Memory | bounded, from config | — | n/a |

**Decision 3: an `IdempotencyGuard` that records the id of a command only when it was *accepted*;
a duplicate of an accepted command is re-acknowledged but not re-run; a rejected command is not
recorded, so a retry re-checks.** The acknowledgement (`commandAck`) is the reply that makes this
visible, matched by `CorrelationId`.

---

## The lecture

### A message that knows itself

Look at the v1 message one more time:

```json
{"ShuttleId":"SH-01","State":"idle","Position":"A01-L01","Timestamp":"2026-01-01T00:00:00Z","FaultReason":null}
```

It says what the shuttle is doing. It does **not** say which version of the contract it follows,
which kind of message it is, which specific message it is, or what it is a reply to. In M1 that
did not matter: one process made the message and read it. The moment a second process reads it,
each missing fact becomes a question:

- *Which version is this?* M6 adds messages; an old shuttle must not choke on a new field.
- *What kind is it?* The fleet manager reads commands, acks, and statuses from the same broker.
- *Which message is it?* A duplicate must be recognised as a duplicate (concept 3).
- *What does it answer?* An acknowledgement must be matched to the command it answers.

The answer is one small record that rides on the front of every message — an **envelope**:

```csharp
public sealed record MessageEnvelope(
    int ContractVersion,     // 2
    string MessageType,      // "moveCommand" | "commandAck" | "shuttleStatus"
    string MessageId,        // a fresh GUID, per message
    string? CorrelationId,   // the MessageId this one answers, or null
    string SenderId,         // "fleet-manager" or the shuttle id
    DateTime SentAt);        // UTC
```

Two habits make it work.

**The body is a separate record.** The envelope is not copied into three places; it is a property
of each message:

```csharp
public sealed record MoveCommandMessage(MessageEnvelope Envelope, string ShuttleId, string ToLane);
public sealed record CommandAckMessage(MessageEnvelope Envelope, bool Accepted, string? Reason);
public sealed record ShuttleStatusMessage(MessageEnvelope Envelope, ShuttleStatus Status);
```

So a receiver can parse the envelope alone, learn the version and the kind, and *then* commit to
parsing the body — which is exactly what lets an unknown kind be ignored instead of crashing a
shuttle. (Verified this session: parse `Envelope`, switch on `MessageType`, then deserialize.)

**The kind is a wire name, declared once.** `MessageTypes.MoveCommand` is `"moveCommand"`;
`ContractVersion.Current` is `2`. No string and no number is typed twice. `SenderId` on an
acknowledgement is the shuttle; `CorrelationId` is the command's `MessageId`. That is the whole
of "which command is this ack for".

`ShuttleStatus` does **not** change. That is a decision, not laziness: M1's demo, its test, and
the contract's first message all stay exactly as they were, and the manager parses the status
payload with the type it already has. v2 is *additive*.

---

### The broker is between them

In M1 the shuttle moved because `Program.cs` called `shuttle.StartMove(...)`. In M2 the shuttle is
a *different program*, and there is no call from one to the other. Something has to carry the
message, and that something is a **broker** (Mosquitto, per record 02).

The model is **publish/subscribe**, and it is genuinely different from a method call:

- A **publisher** does not name a receiver. It names a **topic** and drops the message in.
- A **subscriber** declares which topics it wants (a filter) and is handed whatever arrives on a
  matching topic.
- Nobody waits for anybody. The two programs are connected only by their agreement on topic names
  and payload shapes — their **contract**.

**Topics are routes, and they are built in one place.** MQTT topics are `/`-separated; the scheme
this project fixed is one branch per shuttle, one leaf per kind:

```
shuttle/SH-01/command     the fleet manager → SH-01
shuttle/SH-01/ack         SH-01 → the fleet manager
shuttle/SH-01/status      SH-01 → anyone listening
```

The manager wants *every* shuttle's acks and statuses, and MQTT has a one-level wildcard `+` for
exactly that: `shuttle/+/ack` matches `shuttle/SH-01/ack` and `shuttle/SH-02/ack`, and **not**
`shuttle/SH-01/ack/extra` (verified against the real broker). The shuttle subscribes to its own
single command topic. A `#` multi-level wildcard exists and is not used here: one level is all the
scheme needs, and over-broad subscriptions are how a receiver gets messages it did not expect.

All of it is built by one type so no topic literal appears in decision code:

```csharp
public sealed class TopicScheme
{
    public TopicScheme(string topicRoot) { /* validate non-empty */ }
    public string Command(string shuttleId) => $"{_root}/{shuttleId}/command";
    public string Ack(string shuttleId)     => $"{_root}/{shuttleId}/ack";
    public string Status(string shuttleId)  => $"{_root}/{shuttleId}/status";
    public string AllAcks     => $"{_root}/+/ack";
    public string AllStatuses => $"{_root}/+/status";
}
```

`_root`, the host, the port, and the ids come from configuration: `appsettings.json`, overridden by
environment variables. That is AGENTS.md's "no hard-coded topics" made real — the same program
points at `127.0.0.1:1883` on a laptop and at `broker:1883` in a container by changing one value.

**The delivery guarantee is a choice, and the choice has a cost.** MQTT offers three:

- **QoS 0, at most once** — fire and forget. Fast, and a lost command is silently lost.
- **QoS 1, at least once** — the sender retries until the broker acknowledges; the receiver may
  get the message **more than once**. This is what we choose.
- **QoS 2, exactly once** — a four-way handshake; "exactly once" per link, not across a
  reconnect.

QoS 1 is right for a control message: losing a command is worse than repeating one. But the fine
print is a promise with a bill attached — **at-least-once means duplicates can really happen**.
This session proved it on the real broker: one message published twice at QoS 1 was delivered
**twice**. So the choice of QoS 1 *creates* the need for concept 3. That is not a footnote; it is
the causality this whole lesson turns on.

**The seam in code.** Nothing about shuttles or racks belongs in the transport, so the broker is
reached only through a small interface in a domain-free project:

```csharp
public interface IMessageBus : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken ct);
    Task SubscribeAsync(string topicFilter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken ct);
}
```

`MessageFamily` is `{ Command, Ack, Status }`; it selects the QoS and nothing else.
`MessageReceived` is just `(Topic, Payload)` — the adapter hands back bytes, the domain decides
what they mean. Behind this interface is `MqttMessageBus` (the only place `MQTTnet` appears);
in front of it, in tests, is a fake that keeps messages in a list. That is how the shuttle's
protocol logic gets tested with no broker, no Docker, and no network — the same "decision logic
away from I/O" rule as lesson 04, one boundary out.

---

## The broker is between them (part 2): the round trip

Put the two programs on the two ends and the round trip is four steps:

1. The manager builds a `MoveCommandMessage`, serializes it, and publishes it to
   `shuttle/SH-01/command` at QoS 1.
2. The shuttle receives it, validates the envelope, checks the command is for **its** id, runs it
   through the state machine, and publishes a `CommandAckMessage` to `shuttle/SH-01/ack` with
   `CorrelationId` = the command's `MessageId`.
3. The shuttle publishes a `ShuttleStatusMessage` (state `moving`) to `shuttle/SH-01/status`.
4. When the move completes, it publishes the `idle` status. The manager, subscribed to
   `shuttle/+/ack` and `shuttle/+/status`, matches the ack by correlation and reads the statuses.

One deliberate simplification, stated so it is not a surprise: the shuttle **does not wait** for
the travel time. It receives the command with an event time `now`, starts the move at `now`,
publishes `moving`, computes `arrival = now + TravelTimeTo(toLane)` (the additive method this
lesson adds to `Shuttle`), completes the move at `arrival`, and publishes `idle` at `arrival`. Two
messages with two timestamps, and the process never slept — lesson 04's "time is an input",
carried out of the machine and onto the wire. A real shuttle would take real seconds and report as
it goes; the sim reports the same two facts immediately. Lesson 06's timeout is the reason this
distinction matters: a real manager cannot tell "shuttle is thinking" from "shuttle is gone"
without one.

---

## Same command, once

Concept 2 ended with a promise with a bill: QoS 1 delivers at least once, so the same command can
arrive twice. If the shuttle treats the second delivery as a new command, it starts the move
again — harmless here, catastrophic for `StartLoad` later, where "load the pallet" run twice is
two pallets or a crash. **Idempotency** is the property that says: *processing the same message
again has no additional effect.*

How do you know it is the same message? You gave every message a globally unique `MessageId`
(concept 1). So the receiver keeps a small set of the ids it has already handled and, when one
arrives again, does not do the work.

The decision that makes this correct — and the one a reviewer will ask about — is **what you
remember**:

> The guard remembers **effects**, not messages.

Concretely: a command is added to the cache **only when it is accepted**. Then

- a duplicate of an **accepted** command is **acknowledged again but not run again**. The manager
  that never received the first ack still learns the effect happened;
- a duplicate of a **rejected** command is **reprocessed** — it was never recorded, so the checks
  run again. A rejection is not a completed effect; remembering it would hide the case where the
  obstruction has since cleared.

The cache is bounded. A real system cannot remember every id forever, so it keeps the most recent
N (from configuration) and forgets the oldest first:

```csharp
public sealed class IdempotencyGuard
{
    public IdempotencyGuard(int capacity) { /* capacity >= 1 */ }
    public bool IsDuplicate(string messageId);   // seen recently?
    public void MarkCompleted(string messageId);  // record an *accepted* id; evict oldest at capacity
}
```

A duplicate that arrives after its id has been evicted is treated as new. That is honest: memory
is finite, the window is a choice, and the number is configuration. The lesson's job is to make
you able to say *why* the window exists and what happens at its edge — not to pretend it is
infinite.

Order matters, and it is the last thing to notice. The receiver validates before it trusts
(version, kind, recipient), and checks the guard **before** it touches the state machine. That
ordering is what makes "arrives twice" safely boring: by the time the second copy reaches the
guard, the first has already recorded the effect.

---

### Recap

An **envelope** rides on every message: version, kind, unique id, the id it answers, sender, time.
The body is a separate record; `ShuttleStatus` is unchanged from v1.

The **broker** is the boundary. Topics are routes built by `TopicScheme` from configuration; the
manager subscribes `{root}/+/ack` and `{root}/+/status`, the shuttle its own command topic. QoS 1
means at-least-once, which *really* repeats messages. The broker sits behind `IMessageBus`, so the
logic on either side is tested with a fake and the client lives in one adapter.

**Idempotency** makes the repeat safe: remember accepted ids, re-ack a duplicate without re-running
it, reprocess rejections. The window is bounded and configured.

---

## Worked example

A toy: a **doorbell** shared by two players. It is **not** the assignment — no shuttle, no topics,
no MQTT — and it exists to show the two hard ideas at a size you can hold in one glance: a topic
*match* and an *idempotent* handler. Do not copy it. The toy stays a toy.

Everything below ran in `/tmp/opencode/gates` and was deleted afterwards. It is a tiny library
plus xUnit tests; the two classes are a `TopicScheme` (the same shape as the real one) and an
`IdempotencyGuard` (FIFO eviction at capacity).

```csharp
public sealed class TopicScheme(string root)
{
    public string Command(string shuttleId) => $"{root}/{shuttleId}/command";
    public string ManagerAckSubscription => $"{root}/+/ack";
}

public sealed class IdempotencyGuard(int capacity)
{
    private readonly Queue<string> _order = new();
    private readonly HashSet<string> _completed = new(capacity);

    public bool IsDuplicate(string id) => _completed.Contains(id);
    public void MarkCompleted(string id)
    {
        if (_completed.Add(id)) _order.Enqueue(id);
        while (_order.Count > capacity) _completed.Remove(_order.Dequeue());
    }
}
```

Six tests, all green. The two that matter:

```csharp
Assert.Equal("shuttle/+/ack", new TopicScheme("shuttle").ManagerAckSubscription);

var guard = new IdempotencyGuard(capacity: 2);
guard.MarkCompleted("cmd-1"); guard.MarkCompleted("cmd-2"); guard.MarkCompleted("cmd-3");
Assert.False(guard.IsDuplicate("cmd-1"));   // evicted: the oldest is gone
Assert.True(guard.IsDuplicate("cmd-3"));    // the newest is remembered
```

Observed (exit 0):

```
Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6 - Gates.Tests.dll
```

Then three mutations, one at a time, each a realistic slip:

| Mutation | What went red |
|---|---|
| evict the **newest** id instead of the oldest | `Fifo_eviction_forgets_oldest_at_capacity` |
| manager filter `…/ack` typed as `…/ack/` | `Manager_subscriptions_use_level_wildcard_for_shuttle` |
| `IsDuplicate` always returns `true` | `Unknown_message_is_not_a_duplicate` (and the FIFO test) |

A green suite proves the code passes the tests; the mutations prove the tests would *notice* a
wrong one. That is the reviewer's standard, and it is why a bounded cache with two tests beats a
clever one with none. Your shuttle is this toy with a real broker, a real state machine, and
`MessageId`s that come off the wire.

---

## Say it out loud

Answer these without notes before you start the assignment.

1. A command and its acknowledgement are two messages. What field lets the fleet manager know the
   ack is *for* the command it sent, and why is the envelope the only place that field could
   live? (Hint: what does an MQTT topic *not* carry?)
2. QoS 1 promises delivery but repeats messages. Walk the causal chain: why does choosing QoS 1
   *oblige* you to build concept 3, and what would change if you chose QoS 0 or QoS 2 instead?
   (QoS 2 is the trap — say why it does not remove the obligation.)
3. A duplicate of an **accepted** command is re-acknowledged but not re-run. A duplicate of a
   **rejected** command is reprocessed. Why the asymmetry? What real event (an obstruction
   clearing) is it protecting, and what would go wrong if both were remembered?
4. The shuttle receives a command and publishes `moving` at `now` and `idle` at
   `now + TravelTimeTo(toLane)`, with no `Thread.Sleep`. Which lesson-04 rule is this, and what
   does a real fleet manager lose by not seeing the shuttle in between?
5. The `IdempotencyGuard` forgets the oldest id at capacity. Construct a sequence of commands where
   a very late duplicate is treated as new, and say out loud whether that is acceptable and why the
   window is configuration rather than a constant.

---

## The assignment

Build the first M2 slice: contract v2, the broker boundary, and one idempotent round trip. Four
new projects, two app changes, and six test files. Read every requirement; several of the fixed
names are greppable gates, and the milestone is only half-built when you finish — lesson 06 adds
retry and timeout.

### Requirements

1. **Contract v2, in `Contracts` (no new reference).** Add the files below. Do **not** change
   `ShuttleStatus` or `docs/contract/v1.md`:
   - `src/Contracts/MessageEnvelope.cs`:
     `public sealed record MessageEnvelope(int ContractVersion, string MessageType, string MessageId, string? CorrelationId, string SenderId, DateTime SentAt);`
   - **`src/Contracts/MessageTypes.cs`** — `public static class MessageTypes` with
     `const string MoveCommand = "moveCommand"`, `CommandAck = "commandAck"`,
     `ShuttleStatus = "shuttleStatus"`; and `public static class ContractVersion` with
     `public const int Current = 2;`.
   - **`src/Contracts/MoveCommandMessage.cs`** — `public sealed record
     MoveCommandMessage(MessageEnvelope Envelope, string ShuttleId, string ToLane)`.
   - **`src/Contracts/CommandAckMessage.cs`** — `public sealed record
     CommandAckMessage(MessageEnvelope Envelope, bool Accepted, string? Reason)`.
   - **`src/Contracts/ShuttleStatusMessage.cs`** — `public sealed record
     ShuttleStatusMessage(MessageEnvelope Envelope, ShuttleStatus Status)`.
2. **`src/Messaging/` — a domain-free class library** (`net10.0`, nullable, implicit usings) that
   references **`Contracts` only**. Files, with the record 08 shapes:
   - `MessageFamily.cs` — `enum MessageFamily { Command, Ack, Status }`;
   - `MessageReceived.cs` — `record MessageReceived(string Topic, string Payload)`;
   - `IMessageBus.cs` — the interface in [The broker is between them](#the-broker-is-between-them);
   - `MessagingOptions.cs` — `record MessagingOptions(string Host, int Port, string ClientId,
     string TopicRoot, string FleetManagerId, int IdempotencyCacheSize)`;
   - `TopicScheme.cs` — `Command(shuttleId)`, `Ack(shuttleId)`, `Status(shuttleId)`, plus the two
     manager filters. Validate a non-empty root. **No topic literal may appear outside this file.**
   - `MessageEnvelopes.cs` — a factory: `Create(messageType, senderId, sentAt, correlationId = null)`
     returning a `MessageEnvelope` with `ContractVersion.Current`, a fresh `Guid` string as
     `MessageId`, and the given correlation;
   - `IdempotencyGuard.cs` — bounded, FIFO; `IsDuplicate` and `MarkCompleted` (record 09).
3. **`src/MqttMessaging/` — the MQTT adapter** (`net10.0`), referencing `Messaging`, `Contracts`,
   and **`MQTTnet` 5.2.0.1603** (the only project that may reference it):
   - `MqttQualityOfService.cs` — `enum MqttQualityOfService { AtMostOnce = 0, AtLeastOnce = 1,
     ExactlyOnce = 2 }` (or bind MQTTnet's enum; either is fine as long as the number comes from
     config);
   - `MqttMessageBusOptions.cs` — `record MqttMessageBusOptions(string Host, int Port,
     string ClientId, QosOptions Qos)` with `record QosOptions(MqttQualityOfService Command,
     MqttQualityOfService Ack, MqttQualityOfService Status)`;
   - `MqttMessageBus.cs` — `IMessageBus` over `MQTTnet`: `ConnectAsync` builds and starts a
     `MqttClient`; `PublishAsync` serializes nothing (it takes a `payload` string) and publishes at
     the family's QoS; `SubscribeAsync` registers a handler that raises `MessageReceived(topic,
     payload)`. `DisposeAsync` disconnects.
4. **`src/Messaging.Tests/` and `src/MqttMessaging.Tests/`** — xUnit, the `dotnet new xunit`
   template. `Messaging.Tests` references `Messaging` and `Contracts`; `MqttMessaging.Tests`
   references `MqttMessaging`, `Messaging`, and `Contracts`. Delete both `UnitTest1.cs`.
   The MQTT test must start an **in-process** `MQTTnet.Server` (no Docker), read the bound port
   from the server options after start, and prove a real publish/subscribe round trip.
5. **`MqttMessageBusTests.cs`** (`src/MqttMessaging.Tests/`) — at least:
   - `Publishes_and_subscribes_over_an_in_process_broker` — start a `MQTTnet.Server` on an
     ephemeral port, connect the bus, subscribe, publish a payload, and assert it arrives;
   - `Subscribe_receives_only_the_matching_topic` — the `+` wildcard does not over-match.
6. **`ShuttleCore.Shuttle` gains one method** — `public TimeSpan TravelTimeTo(string lane)` — the
   arrival arithmetic the endpoint needs, computed from the current position. It changes no rule;
   it exposes one that already exists inside `CompleteMove`. This is the only `ShuttleCore`
   change this lesson allows.
7. **`src/ShuttleSim/ShuttleEndpoint.cs` — the shuttle's side of the contract** (lives with the
   shuttle app, so the transport stays domain-free). Constructor takes the `Shuttle`,
   an `IMessageBus`, the `TopicScheme`, the shuttle id, and the `IdempotencyGuard`. One method,
   `HandleCommandAsync(string payload, DateTime now, CancellationToken ct)`, which:
   - parses the envelope and the `MoveCommandMessage`; ignores a malformed payload; a
     `ContractVersion` other than `Current` is rejected (drop it, publish nothing);
   - ignores a command whose `MessageType` is not `moveCommand` or whose `ShuttleId` is not this
     shuttle's id;
   - if the guard already knows the command's `MessageId`: publishes the **same** acknowledgement
     again (`Accepted = true`) and returns **without moving**;
   - otherwise calls `shuttle.StartMove(toLane, now)`; on `ShuttleRejectedException` publishes
     `CommandAckMessage` with `Accepted = false` and the exception message, and returns;
   - on success `MarkCompleted`, publish the accepted ack, publish a `moving` status at `now`,
     complete the move at `now + shuttle.TravelTimeTo(toLane)`, and publish an `idle` status
     there. No `Thread.Sleep`, no `Task.Delay`.
8. **`src/ShuttleSim/Program.cs`** keeps the M1 demo when run with **no arguments** (the seven
   `ShuttleStatus` lines and `ShuttleSim.Tests` stay green). Add a second mode, `serve`, which
   reads config, connects an `MqttMessageBus`, builds the 2×2×2 rack and `5/2/2` timing (scenario
   data), subscribes to the shuttle's command topic, runs the endpoint, and stays up until the
   process is stopped. The shuttle id and start lane come from configuration
   (`Shuttle:Id`, `Shuttle:StartLane`).
9. **`src/FleetManager/Program.cs`** keeps the M0 behaviour with **no arguments** (`Fleet Manager
   Coming Online`, exit 0). Add `move <shuttleId> <toLane>`: connect, subscribe
   `{root}/+/ack` and `{root}/+/status`, publish one `MoveCommandMessage` to the command topic,
   then wait — bounded by `FleetManager:RoundTripTimeoutMs` from configuration, using a
   `CancellationTokenSource` timer, never a bare sleep or an unbounded wait — for an ack whose
   `CorrelationId` matches, and for at least one status. Print each message it receives as one JSON
   line, then exit `0` if the ack and a status arrived, `1` otherwise.
10. **`appsettings.json`** in both apps, copied to output, with the record 08 keys; each app binds
   it and then `AddEnvironmentVariables()`. R2/R4 first: **fix `src/FleetManager.Tests/StartTests.cs`**
   to the bounded-wait pattern (a named timeout constant, `WaitForExit(<timeout>)`, kill the tree
   on overrun), remove the nine-space indent, and end the file with a newline. This is the review-03
   defect, due now, and the lesson's own manager is the reference.
11. **Tests, by name** — at least these, anchored for the acceptance:
   - `src/Contracts.Tests/EnvelopeTests.cs`:
     `Envelope_round_trips_with_contract_version_and_correlation`,
     `Message_kind_is_read_from_the_envelope_before_the_body`;
   - `src/Messaging.Tests/TopicSchemeTests.cs`:
     `TopicScheme_builds_a_topic_branch_per_shuttle`,
     `TopicScheme_manager_filters_use_a_single_level_wildcard`;
   - `src/Messaging.Tests/IdempotencyGuardTests.cs`:
     `IdempotencyGuard_reports_a_seen_id_as_duplicate`,
     `IdempotencyGuard_forgets_the_oldest_id_at_capacity`;
   - `src/Messaging.Tests/MessageEnvelopesTests.cs`:
     `MessageEnvelopes_generate_a_unique_id_and_version_two`;
   - `src/ShuttleSim.Tests/ShuttleEndpointTests.cs` (add a `Messaging` project reference and drive a
     **fake** `IMessageBus`, no broker):
     `Duplicate_move_command_is_acknowledged_but_not_run_twice`,
     `Move_command_for_another_shuttle_is_ignored`,
     `Rejected_move_is_acknowledged_as_not_accepted`,
     `Accepted_move_reports_moving_then_idle`.
   Every `[Fact]` carries at least one `Assert`.
12. **`docs/contract/v2.md`** — the new version, written by **you** (the professor never edits the
    contract). It declares `Version: 2`, documents `MessageEnvelope`, `MoveCommandMessage`,
    `CommandAckMessage`, and `ShuttleStatusMessage`, and either copies the unchanged
    `ShuttleStatus` section forward or references `v1.md` for it. `v1.md` is not edited.

### Acceptance criteria

Run from the repo root unless a phase says `src/`. Exit codes are read directly; printed numbers
are asserted, not admired. **Read every line's purpose before you run it** — a gate you do not
understand is a gate you will pass by accident.

```bash
# Phase 1 — four new projects, twelve total, the right edges, no forbidden ones
for p in Messaging Messaging.Tests MqttMessaging MqttMessaging.Tests; do
  test -f "src/$p/$p.csproj" || echo "missing project: $p"; done        # nothing
grep -c '<Project Path' src/ShuttleFleet.slnx                            # prints 12
edge() { grep -q "ProjectReference Include=\"..\\\\$1\\\\$1.csproj\"" "src/$2/$2.csproj" \
  || echo "missing edge: $2 -> $1"; }
edge Contracts Messaging
edge Contracts Messaging.Tests
edge Messaging Messaging.Tests
edge Contracts MqttMessaging
edge Messaging MqttMessaging
edge Contracts MqttMessaging.Tests
edge Messaging MqttMessaging.Tests
edge MqttMessaging MqttMessaging.Tests
edge Messaging ShuttleSim
edge MqttMessaging ShuttleSim
edge Messaging ShuttleSim.Tests
edge Messaging FleetManager
edge MqttMessaging FleetManager                                      # prints nothing
! grep -q 'ProjectReference' src/ShuttleCore/ShuttleCore.csproj      # exit 0: still pure
for bad in MqttMessaging ShuttleCore ShuttleSim FleetManager; do
  ! grep -q "..\\\\$bad\\\\$bad.csproj" src/Messaging/Messaging.csproj || echo "Messaging must not see $bad"
done                                                                 # nothing
! grep -q 'MQTTnet' src/Messaging/Messaging.csproj                   # exit 0: adapter-only
grep -q 'MQTTnet' src/MqttMessaging/MqttMessaging.csproj             # exit 0
find src -name '*.cs' -not -path '*/bin/*' -not -path '*/obj/*' -exec md5sum {} + \
  | sort | uniq -w32 -D                                              # nothing: no copied code
```

The count is exactly **twelve** projects. The forbidden-edge loop is the important half: `Messaging`
must not see the adapter, the domain, or the apps; `ShuttleCore` must still reference nothing.

```bash
# Phase 2 — contract v2 exists, v1 is untouched, the fixed names exist
grep -qE '^Version: 2$' docs/contract/v2.md                          # exit 0
grep -qE '^Version: 1$' docs/contract/v1.md                          # exit 0: v1 not edited
for s in MessageEnvelope MoveCommandMessage CommandAckMessage ShuttleStatusMessage; do
  grep -rq "$s" src/Contracts --include='*.cs' || echo "missing type: $s"; done   # nothing
grep -rq 'const int Current = 2' src/Contracts --include='*.cs'      # exit 0
for k in moveCommand commandAck shuttleStatus; do
  grep -q "const string [A-Za-z]* *= *\"$k\"" src/Contracts/MessageTypes.cs \
    || echo "missing wire name: $k"; done                     # nothing
grep -qE '^Version: 2' docs/contract/v2.md                           # exit 0
grep -q 'MessageEnvelope' docs/contract/v2.md                        # exit 0
grep -q 'CommandAckMessage' docs/contract/v2.md                      # exit 0
```

```bash
# Phase 2 rules — no literals where configuration belongs; no sleeps
grep -rnE 'Thread\.Sleep|Task\.Delay' src --include='*.cs' \
  --exclude-dir='*.Tests' --exclude-dir='obj' --exclude-dir='bin'    # nothing in src app code
grep -rnE '"shuttle/[A-Za-z0-9+_/-]+"' src --include='*.cs' \
  --exclude-dir='*.Tests' --exclude-dir='obj' --exclude-dir='bin'    # nothing: no topic literals in app code
grep -rniE '127\.0\.0\.1|localhost|:1883|Host=' src --include='*.cs' \
  --exclude-dir='*.Tests' --exclude-dir='obj' --exclude-dir='bin'    # nothing: addresses are config
grep -rn 'WaitForExit()' src/FleetManager.Tests --include='*.cs'     # nothing: R2 fixed
grep -q 'WaitForExit(' src/FleetManager.Tests/StartTests.cs          # exit 0: a bounded wait
grep -qE 'Timeout' src/FleetManager.Tests/StartTests.cs              # exit 0: a named bound
grep -rq '127\.0.0.1\|1883' src/FleetManager/appsettings.json src/ShuttleSim/appsettings.json  # exit 0: config lives in config
find src -name 'Class1.cs' -o -name 'UnitTest1.cs'                   # nothing
for f in $(grep -rl '\[Fact\]' src --include='*.cs'); do
  grep -q 'Assert' "$f" || echo "$f"; done                            # nothing: no empty tests
```

The `..\\$1\\$1.csproj` in `edge` is a literal backslash, not a regex — ProjectReference paths
use Windows separators even on Linux, so the grep must escape them. The sleep, topic-literal,
and address gates above use `--exclude-dir='*.Tests'` because tests legitimately assert whole
topics (`TopicSchemeTests`), connect an in-process broker on loopback, and may wait; only app
code is forbidden them. The sharpest case is the topic-literal gate: the *root* `"shuttle"` and
the single segment strings live inside `TopicScheme` and in `appsettings.json`; what it forbids
is a whole topic (`"shuttle/SH-01/ack"`) typed into app code. The `MQTTnet` check runs the
other way: exactly one project may see it.

```bash
# Phase 2b — R2/R4 are actually fixed (review 03's items)
grep -q 'WaitForExit(' src/FleetManager.Tests/StartTests.cs          # exit 0
! grep -qE '[^ ]WaitForExit\(\)' src/FleetManager.Tests/StartTests.cs # exit 0: no bare WaitForExit()
grep -nE '^         [^ ]' src/FleetManager.Tests/StartTests.cs        # nothing: no nine-space indent
tail -c1 src/FleetManager.Tests/StartTests.cs | od -An -c | grep -q '\\n'  # exit 0: trailing newline
```

```bash
# Phase 3 — from src/: build, run the suite, walk the new names
cd src
dotnet build                                                          # exit 0, 0 warnings
T=$(mktemp); dotnet test > "$T" 2>&1                                  # exit 0 (read $? here)
grep -c 'Passed!' "$T"                                                # prints 6
grep -c 'No test is available' "$T"                                   # prints 0
rm -f "$T"
L=$(mktemp); dotnet test --list-tests > "$L" 2>&1
for t in TopicScheme_builds_a_topic_branch_per_shuttle \
         TopicScheme_manager_filters_use_a_single_level_wildcard \
         IdempotencyGuard_reports_a_seen_id_as_duplicate \
         IdempotencyGuard_forgets_the_oldest_id_at_capacity \
         MessageEnvelopes_generate_a_unique_id_and_version_two \
         Duplicate_move_command_is_acknowledged_but_not_run_twice \
         Move_command_for_another_shuttle_is_ignored \
         Rejected_move_is_acknowledged_as_not_accepted \
         Accepted_move_reports_moving_then_idle \
         Publishes_and_subscribes_over_an_in_process_broker \
         Subscribe_receives_only_the_matching_topic \
         Envelope_round_trips_with_contract_version_and_correlation \
         Message_kind_is_read_from_the_envelope_before_the_body; do
  grep -qE "\\.$t\$" "$L" || echo "missing test: $t"                  # nothing
done
rm -f "$L"
cd ..
```

The `Passed!` count is **6** — one line per test project: `Contracts.Tests`, `ShuttleCore.Tests`,
`ShuttleSim.Tests`, `FleetManager.Tests`, `Messaging.Tests`, `MqttMessaging.Tests`. If it prints
5, one project has no tests or was not added to the solution; the anchored name walk names the
missing ones.

```bash
# Phase 4 — no regression: the M1 demo and the manager's start line still hold
cd src
F=$(mktemp); dotnet run --project ShuttleSim/ShuttleSim.csproj > "$F" 2>&1   # exit 0
grep -c '"ShuttleId"' "$F"                                            # prints 7 (M1 demo unchanged)
rm -f "$F"
G=$(mktemp); dotnet run --project FleetManager/FleetManager.csproj > "$G" 2>&1   # exit 0
grep -q 'Fleet Manager Coming Online' "$G"                            # exit 0
rm -f "$G"
cd ..
```

```bash
# Phase 5 — the round trip over the real broker (start, move, assert, stop)
docker compose -f docker/compose.yml up -d broker                     # exit 0
sleep 2
cd src && dotnet build ShuttleFleet.slnx >/dev/null 2>&1 && cd ..
S=$(mktemp)
dotnet src/ShuttleSim/bin/Debug/net10.0/ShuttleSim.dll serve > "$S" 2>&1 &
SHUTTLE_PID=$!
sleep 3                                                              # let the shuttle subscribe
M=$(mktemp)
dotnet src/FleetManager/bin/Debug/net10.0/FleetManager.dll move SH-01 A01-L02 > "$M" 2>&1
MOVE_EXIT=$?
kill "$SHUTTLE_PID" 2>/dev/null; wait "$SHUTTLE_PID" 2>/dev/null
docker compose -f docker/compose.yml down >/dev/null 2>&1
echo "move exit=$MOVE_EXIT"                                          # prints move exit=0
grep -q '"MessageType":"commandAck"' "$M"                             # exit 0
grep -q '"Accepted":true' "$M"                                        # exit 0
grep -q '"State":"moving"' "$M"                                       # exit 0
grep -q '"State":"idle"' "$M"                                         # exit 0
rm -f "$S" "$M"
```

The `sleep 3` is the honest wart of this lesson: with no retry yet, the manager's single publish is
lost if the shuttle has not subscribed. Lesson 06 removes the sleep by resending until the ack
arrives. Until then, the ordering is the test's job and the timeout at the shell (`sleep` + the
manager's own bound) is what keeps a bad run from hanging.

```bash
# Phase 6 — the contract's v1 is untouched and the fresh container still passes
grep -qE '^Version: 1$' docs/contract/v1.md                           # exit 0
grep -rq 'public record ShuttleStatus' src/Contracts --include='*.cs' # exit 0
T=$(mktemp); docker build -f docker/Dockerfile.build -t shuttle-fleet-build . >"$T" 2>&1
                                                                      # exit 0
rm -f "$T"
U=$(mktemp); docker run --rm shuttle-fleet-build >"$U" 2>&1           # exit 0
grep -c 'Passed!' "$U"                                                # prints 6
rm -f "$U"
```

### Stretch goal

Make the acknowledgement **carry the status it implies**, without adding a concept. Add a field to
`CommandAckMessage` — no: the contract's ack is deliberately small (record 09). Instead, add a
small pure function `MessageEnvelopes.CorrelationOf` (or a method on `ShuttleEndpoint`) that,
given a received message, returns the id it answers, and write one test that shows the manager can
match an ack to a command using only the envelope. The point: correlation is a property of the
envelope, provable with no broker. Accept: the six new test files stay green and your new test
fails if `CorrelationId` is left `null` on the ack.

### Not in this assignment

- **Retry, ack timeout, alarm when retries run out** — lesson 06. This lesson's manager publishes
  once and bounds its wait; that is a driver bound, not the policy, and the `sleep 3` above exists
  only because retry is not here yet.
- **Alarm and recovery messages, heartbeats** — later M2 lessons and M5, contract v3.
- **Task queue, persistence, database** — M3, record 03.
- **Traffic, segments, reservations** — M4.
- **WMS REST** — M6. **Dashboard** — M7. **Load test** — M8.
- **TLS, credentials, ACL** — deferred by record 02; the dev broker stays anonymous on loopback.

**Carried from review 04, for the reviewer's list:** **R2/R4** in
`src/FleetManager.Tests/StartTests.cs`, now folded into requirement 8 and Phase 2b. **C1** (the
`Rack.LaneId`/`SlotId` vs the lesson's `LaneIds`/`SlotIds`) and **C2** (weaker `RackTests`) remain
open from review 04; the professor flagged them in [record 05](../decisions/05-shuttle-core-and-rack-model.md)
and they stay on the reviewer's list, not silently dropped.

---

## Closing M2's first slice

Read the spec's M2 line once more: *"a command is acknowledged and retried … a round trip."* This
lesson makes **acknowledged** and **round trip** true over a real broker with a real contract, and
it makes the retry *safe* by building idempotency first. It does **not** make **retried** true —
that is lesson 06, and until then the `sleep 3` in Phase 5 is honest about the gap.

Who says "done"? Not you, and not I. AGENTS.md gives the reviewer that line: the milestone table
in `README.md` moves only when the review passes, with the verifying command written beside it. The
reviewer will run the block above against your tree, delete one guard or rename one topic to watch
a gate go red, and ask you to explain a line. Be ready to say, without notes, why the ack carries a
`CorrelationId`, why `ShuttleCore` still references nothing, and why achieving QoS 1 forces you to
build the idempotency guard in the same lesson.
