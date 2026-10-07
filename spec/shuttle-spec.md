# Shuttle Fleet Sim: End-Product Spec

## Goal
Build a simulation of an automated pallet shuttle system and the software that runs it. Simulated shuttles move pallets through rack lanes. A fleet manager takes work orders from a mock warehouse system, assigns tasks, controls traffic, tracks health, recovers from faults, and shows everything on an operator dashboard.

This is a learning and portfolio project modeled on the software side of a pallet shuttle product: fleet management, task execution, traffic control, diagnostics, operator interfaces, and integration with WCS, WMS, and ERP systems. It is built so the owner can explain every part of it, and why it works that way, to an engineering team.

## Constraints
- **C#/.NET** is the default language for the fleet manager and the simulated shuttles. Decision record 01 confirms it. The operator dashboard uses JavaScript/TypeScript (Vue). Python may be used for tooling, such as a scenario runner or a log analyzer. Each milestone's assignment says which language it uses.
- Docker for reproducible builds and for the supporting services (message broker, database) from M0 onward.
- No real hardware. Each simulated shuttle plays the role of the vehicle's onboard controller. It talks to the fleet manager only through the documented message contract, never by calling its code directly.
- The **message contract** (commands, status, alarms, recovery) is documented, versioned, and the source of truth. Changing it needs a decision record and a version bump.
- Messages travel over MQTT. The mock WMS talks to the fleet manager over REST. Decision record 02 confirms the choices.
- Fleet state lives in a SQL database. Decision record 03 picks the database.
- Decision logic is kept separate from input and output (pure functions for task assignment, traffic control, and state machines) so it can be unit tested without a broker or database.
- Every part is testable in isolation.

## Non-goals (for now)
- Real PLC hardware, real motor control, or real vehicle physics beyond simple timing.
- Optimal scheduling. A correct, explainable approach beats a clever one.
- Production-grade security or UI. A simple dashboard is enough.

## System overview
| Area | What it does | Typical interfaces |
|---|---|---|
| Warehouse model | Aisles, lanes, slots, and pallets as data | in-memory model, SQL |
| Shuttle simulator | Plays the onboard controller: moves, loads, unloads, reports, faults | MQTT in (commands), MQTT out (status, alarms) |
| Message contract | The agreed messages between fleet manager and shuttles | versioned JSON schemas, topic names |
| Fleet manager | Task queue, assignment, state tracking, health, recovery | MQTT, REST, SQL |
| Traffic control | Makes sure two shuttles never use the same space at the same time; avoids deadlock | internal, driven by fleet state |
| Mock WMS/WCS | Sends store and retrieve orders and receives status | REST |
| Operator dashboard | Shows shuttles, tasks, and alarms; sends pause and recovery commands | REST, web socket or polling |
| Diagnostics | Logs, alarms with severity, health history | SQL, logs |

## Milestones
Each milestone is a vertical slice: it runs end to end before the next one starts.

**M0: Repo and tooling.** Repo layout, solution with a shared contracts project and test projects, a Docker setup for the broker and database, and one real message type defined and unit tested.
*Done when:* clean build and tests pass in a fresh container.

**M1: Warehouse model and one shuttle.** A rack model (lanes, slots, pallets). A single simulated shuttle with an explicit state machine (idle, moving, loading, unloading, fault) and simple timing. Driven by local calls only, with no network yet.
*Done when:* a scripted sequence moves a pallet from one slot to another, and every state transition is covered by a test.

**M2: Message contract and transport.** Define the commands, status messages, alarms, and recovery messages. Add MQTT topics. The shuttle speaks the contract. A fleet manager skeleton connects, sends a command, and receives status. Handle duplicate messages, lost messages, and timeouts.
*Done when:* a command, an acknowledgement, and status updates make a full round trip, and tests show a repeated command does not run twice and a lost acknowledgement is retried.

**M3: Fleet manager core.** A task queue with a clear lifecycle (queued, assigned, in progress, done, failed). Assign tasks to one shuttle, then several. Save state in SQL.
*Done when:* a queue of tasks is finished by three shuttles, and fleet state survives a fleet manager restart.

**M4: Traffic control.** Reserve lane segments so two shuttles never occupy the same space. Detect or avoid deadlock.
*Done when:* a stress scenario with four or more shuttles runs with no collisions and no permanent deadlock, and the rule used is written down and explained in a decision record.

**M5: Faults and recovery.** Heartbeats and timeouts. Faults such as blocked path, lost communication, and pallet not detected. Alarms with severity. Reassign a failed shuttle's work. Recovery that an operator acknowledges.
*Done when:* every injected fault leads to the right alarm and the right recovery, and the saved state never loses or duplicates a pallet.

**M6: WMS integration.** A mock WMS submits store and retrieve orders over REST, receives status updates, and can cancel orders. It also sends bad input and replies slowly or not at all.
*Done when:* orders flow from the WMS to completed pallets and back, and a WMS outage or a bad order does not corrupt fleet state.

**M7: Operator dashboard and diagnostics.** A small web dashboard shows the rack map, shuttle states, task queue, and alarms. The operator can pause, resume, and acknowledge recovery.
*Done when:* a full scenario, including an injected fault, is run and recovered from the dashboard alone.

**M8 (stretch).** Pick one: an OPC UA or Modbus TCP gateway that carries the same contract for one shuttle, a Python scenario runner and log analyzer, or a load test with measured limits.

## What this maps to
| Milestone | Skill an engineering team would look for |
|---|---|
| M2 | Defining commands, status, alarms, and recovery with controls engineers |
| M3, M4 | Fleet management, task execution, traffic control |
| M5 | Root-cause thinking, error handling, recovery, operational stability |
| M6 | WCS/WMS/ERP integration |
| M7 | Operator interfaces and diagnostics |

## Learning method
For every concept inside a milestone, the professor:
1. Surveys up to three options and compares them.
2. Writes a decision record picking one for this project, with reasons and what would change the choice.
3. Teaches the chosen approach in plain words, at most 3 new concepts per lesson.
4. Assigns a build task with acceptance criteria. No solution code.

The student builds it, then the reviewer critiques it against this spec. The implementer agent is not used until the student has passed review on a milestone. The student must be able to explain every line of their own code out loud.

## Definition of done (every milestone)
- Builds in the Docker environment, tests pass.
- Interfaces and messages are documented and match the contract version.
- Reviewed, with blockers resolved.
- Decision records and lessons are filed in `docs/`.
