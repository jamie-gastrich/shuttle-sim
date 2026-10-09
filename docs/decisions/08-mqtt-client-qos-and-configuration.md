# Decision 08: The MQTT client, delivery guarantee, and configuration

**Date:** 2026-10-09
**Milestone:** M2 (Message contract and transport) — the C# client that speaks to Mosquitto, the delivery guarantee per message kind, and where addresses and topic names come from.
**Status:** Accepted. Written for lesson 05, which wires the first real round trip.
**Supersedes:** none. Record 02 fixed the broker (`eclipse-mosquitto:2.0`) and explicitly left *"the C# client library"*, *"delivery guarantees (QoS)"* and the topic/config details to M2. Record 07 fixed the wire messages and topics; this record fixes the client, the QoS, the code layout, and the configuration.
**Replaced by:** any later record that changes the client library, the QoS choices, or the layer boundary. The replacement links here.

---

## 1. What we are deciding

1. which **C# MQTT client library** the transport adapter uses, and its licence;
2. the **QoS (delivery guarantee)** per message family, with the reason;
4. the **project architecture** that keeps broker I/O out of decision logic;
5. where **addresses, topics, QoS, and ids come from** (configuration, not literals).

Not decided here: retries, ack timeouts, alarms, heartbeats (lesson 06 / record 09 and later); the SQL database and fleet state (M3); TLS and credentials (deferred by record 02, still deferred: the dev broker is anonymous on loopback).

---

## 1a. Survey: the MQTT client library

| | A: `MQTTnet` v5 (chosen) | B: `MQTTnet` v4 (LTS) | C: a raw `TcpClient` + hand-rolled MQTT |
|---|---|---|---|
| Licence | MIT (verified this session: nuspec `<license type="file">`, the shipped `LICENSE` is MIT, copyright .NET Foundation and Contributors) | MIT (`<license type="expression">MIT`, verified) | n/a |
| Maintenance | the active line (`github.com/dotnet/MQTTnet`) | older line, still patched | — |
| What it teaches | a real client library's connection, subscription, and QoS surface | same, older API | the wire protocol, and nothing about using it |
| Version this session | `5.2.0.1603` (latest on NuGet) | `4.3.7.1207` | — |

**Decision: `MQTTnet` 5.2.0.1603**, in a single project (`MqttMessaging`) behind our own interface. The library is the one the .NET ecosystem reaches for, both candidate lines are MIT (so licence is not a differentiator and the choice is maintenance and API currency), and because it is confined behind `IMessageBus`, swapping it later is one file. Rejected: hand-rolling the wire protocol — it buys no lesson this project needs and loses "a real client library has real reconnection and QoS behaviour". Rejected for now: v4 — it is a defensible LTS choice, recorded here as the fallback if v5's API proves unstable.

---

## 2. Decision: the broker I/O sits behind one interface

The spec asks that decision logic be tested **without a broker**. So the broker is reached only through an interface, in its own project.

```
Contracts        (v2 wire types)                      — no new references
Messaging        (IMessageBus, TopicScheme,
                  IdempotencyGuard, MessageEnvelopes,
                  MessagingOptions, MessageFamily)     — references Contracts only
MqttMessaging    (MqttMessageBus, MqttMessageBusOptions) — references Messaging + Contracts + MQTTnet
```

`Messaging` is **domain-free**: it knows topics, ids, and the bus interface, and nothing about shuttles or racks. The MQTT adapter is the *only* project that references `MQTTnet`. `ShuttleCore` still references nothing; `ShuttleEndpoint` (the shuttle's command handling) lives with the shuttle app (`ShuttleSim`), not in the transport library, so the transport never depends on the domain.

```csharp
public interface IMessageBus : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task PublishAsync(string topic, string payload, MessageFamily family, CancellationToken cancellationToken);
    Task SubscribeAsync(string topicFilter, MessageFamily family,
        Func<MessageReceived, CancellationToken, Task> handler, CancellationToken cancellationToken);
}
```

`MessageFamily` is `{ Command, Ack, Status }`; the family selects the QoS (section 3) and nothing else. `MessageReceived` is `(string Topic, string Payload)` — the adapter surfaces the raw bytes; the domain parses them. This is what lets `Messaging.Tests` and `ShuttleSim.Tests` drive the logic with an in-memory fake bus, and `MqttMessaging.Tests` prove the adapter against an in-process broker.

---

## 3. Survey: the delivery guarantee (QoS)

MQTT offers three levels. The messaging-contract skill requires: *"Pick the delivery guarantee per message type, and write down why. 'At least once' delivery means duplicates can happen, so idempotency is required."*

| | A: QoS 0 (at most once) | B: QoS 1 (at least once, chosen) | C: QoS 2 (exactly once) |
|---|---|---|---|
| A command can be lost with the link | yes | no | no |
| Duplicates possible | no (but loss) | **yes** — verified this session on the real broker | no, per hop |
| Cost | cheapest | one ack hop | four-way handshake |
| Does the app still need idempotency? | n/a (loss instead) | yes | yes — MQTT's "exactly once" is per link, not across a reconnect and a re-publish |

**Decision: QoS 1 for all three families** (command, ack, status), configurable per family. At-least-once is the honest floor for a control message: losing a command is worse than repeating it, and once the choice is "no loss", duplicates are the price and **idempotency is mandatory** — which is exactly the concept lesson 05 teaches. QoS 2 is rejected as the default: it is four times the handshake and still does not remove the need for an application-level dedupe across reconnect. QoS 0 is rejected for commands and acks; it is defensible for high-rate status later, which is why the number is per-family configuration rather than a constant.

I verified on the real broker this session that one QoS-1 publish of the same message delivered twice is delivered **twice** to the subscriber (at-least-once), and that a deeper topic (`…/ack/extra`) does not match the one-level wildcard.

---

## 4. Decision: configuration, not literals

AGENTS.md: *"No hard-coded names/topics/timeouts/retry counts/sizes."* So the root, the host, the port, the client id, the QoS numbers, the idempotency cache size, and the shuttle id come from configuration. Each app reads `appsettings.json` and then environment variables, which override the file:

```jsonc
{
  "Messaging": {
    "Host": "127.0.0.1", "Port": 1883, "ClientId": "fleet-manager",
    "TopicRoot": "shuttle", "FleetManagerId": "fleet-manager",
    "IdempotencyCacheSize": 1000,
    "Qos": { "Command": 1, "Ack": 1, "Status": 1 }
  },
  "Shuttle": { "Id": "SH-01", "StartLane": "A01-L01" },
  "FleetManager": { "RoundTripTimeoutMs": 10000 }
}
```

The file is copied to the app's output; `AddEnvironmentVariables()` layers over it so a container or a shell can override any key without editing a file. `MessagingOptions` binds the `Messaging` section; `MqttMessageBusOptions` binds the same section into the adapter.

`FleetManager:RoundTripTimeoutMs` is a **driver** bound: the manager's one-shot round-trip run gives up after this long. It is *not* the reliability policy — command retry and the ack timeout are lesson 06 and record 09. Bounding the wait is also the R2 lesson from review 03 restated in a process, so it is taught, not smuggled: a `CancellationTokenSource` timer, no bare `Thread.Sleep`, no unmetered wait.

---

## 5. Rejected options, and why

- **MQTT calls from `ShuttleCore` or `Contracts`.** Would put I/O in the decision logic and a broker in the pure test suite. Rejected; `ShuttleCore` still references nothing, and `Messaging` cannot see `ShuttleCore`.
- **A single console project doing everything.** Fastest to write, impossible to test without a broker, and it repeats the very coupling M1 removed. Rejected.
- **Hard-coding `127.0.0.1:1883` and the topic root in code.** Banned by AGENTS.md, and the first thing that breaks when the broker moves into a container or the port changes. Rejected; configuration is the source.
- **A process-wide singleton bus.** Hides the seam the fake bus needs. Rejected: the bus is a constructor argument.

---

## 6. Verification table

Checked this session (2026-10-09); scratch in `/tmp/opencode/m2-verify/`, deleted afterwards.

| Claim | How checked | Result |
|---|---|---|
| SDK present | `dotnet --version` | `10.0.112` |
| `MQTTnet` latest version | `dotnet package search MQTTnet` | `5.2.0.1603` |
| `MQTTnet` v5 is MIT | inspect the nuspec and the shipped `LICENSE` in the NuGet cache | `<license type="file">LICENSE`; LICENSE is MIT, .NET Foundation and Contributors |
| `MQTTnet` v4 is MIT | inspect the v4 nuspec | `<license type="expression">MIT</license>` |
| An in-process broker works with no Docker | `MQTTnet.Server` on an **ephemeral port** (`WithDefaultEndpointPort(0)`, then read `DefaultEndpointOptions.Port` after `StartAsync`), publish + subscribe | received 1 message; port discovered from the options object |
| `v5` has no `GetDefaultEndpoint()` | reflection / compile against `MQTTnet.Server` 5.2.0.1603 | absent; the bound port is on the server *options* |
| Configuration file plus environment override binds | `Microsoft.Extensions.Configuration` `Json` + `EnvironmentVariables`; bind a nested section with a QoS enum | bound; `Messaging__Port=9999` overrode the file's 1883 |

Not yet verified:

- The adapter and the apps the owner writes — the assignment.
- Reconnection behaviour under a dropped broker — M5's territory; recorded as not-yet-verified rather than guessed.
- TLS and credentials — deferred by record 02; the dev broker stays anonymous on loopback.

---

## 7. What would change this choice

- If `MQTTnet` v5 shows a blocking defect, fall back to v4 (`IMessageBus` makes it one file); new record.
- If a second language client appears (a Python tool), the wire contract is unchanged; only this client choice is C#-specific.
- If status volume makes QoS 1 wasteful, lower the **status** family to QoS 0 — the per-family config already allows it; new record to justify it.

---

## 8. What this record unblocks

- Lesson 05 can name the two new projects, the `IMessageBus` interface, the config keys, and the QoS decision its acceptance checks.
- The fake-bus tests in `Messaging.Tests` and `ShuttleSim.Tests` can exist without Docker.
- Lesson 06 starts from a working QoS-1 link and adds retry/timeout on top instead of re-deciding transport.
