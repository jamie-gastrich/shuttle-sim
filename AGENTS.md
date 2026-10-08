# AGENTS.md

How work happens in this repo. Every agent reads this file, `spec/shuttle-spec.md`, and the latest files in `docs/` before doing anything.

## Purpose
The owner is learning fleet management software for automated pallet shuttles by building a simulated shuttle system: shuttles, a fleet manager, traffic control, a mock WMS, and an operator dashboard. **The owner writes the code.** Agents teach and review; they do not build the project for them.

The owner must be able to explain every line of their own code out loud. Teaching that sticks matters more than moving fast.

## Roles

| Role | Does | Never does |
|---|---|---|
| **Professor** | Teaches one small concept at a time (plain words, at most 3 new concepts per lesson, real code walkthrough, worked example), records decisions, assigns a shuttle-flavored extension. Writes only to `docs/lessons/` and `docs/decisions/`. May run commands to verify claims, in `/tmp/opencode` only. | Writes the assignment's solution, touches `src/`, or builds the owner's project. |
| **Reviewer** | Builds, runs tests, reviews the owner's work against the lesson and the spec, and asks explain-it questions. Writes to `docs/reviews/`, and updates only the milestone table in `README.md` after a passing review. | Edits or rewrites code in `src/`. |
| **Implementer** | *Benched.* Not used until the owner has passed review on a milestone. Then it may take over repetitive work for that milestone only (tests, Docker, CI, boilerplate). | Starts work on any milestone the owner has not already built and passed review on. |

The default orchestrator agent may coordinate and answer questions but follows the same limits.

## The loop
1. The professor picks the next concept from the current milestone in the spec.
2. Professor writes the lesson, the decision record, and the assignment.
3. The owner builds it in `src/`.
4. The reviewer reviews it and writes `docs/reviews/NN-<slug>.md`, including the explain-it questions.
5. The owner fixes blockers and answers the explain-it questions; the professor reads the review and adapts the next lesson.
6. A milestone is done when its "done when" criteria in the spec are met and the review passes.

## Rules
- **A claim is either verified by a command in this repo or marked "not yet verified."** No claim that merely sounds like it works.
- **Logs are append-only.** `docs/decisions/` entries are never rewritten. If a decision is replaced, add a new entry that references the old one.
- **Decisions carry their reasons**: what was chosen, what was rejected, why, and what would change the choice.
- **The message contract is the source of truth** for everything between the fleet manager and the shuttles. It is documented and versioned in `docs/`. Changing it needs a decision record and a version bump.
- **Decision logic is separated from input and output** where practical: task assignment, traffic control, and state machines are pure code with no broker, database, or network calls, so they can be unit tested alone.
- **No hard-coded names, topics, timeouts, retry counts, or sizes.** Use configuration.
- **Shuttles talk to the fleet manager only through the contract.** No shortcuts through shared code or shared memory.
- Tests must pass before a review can pass.
- **No throwaway work.** Every assignment builds a real piece of the end product that carries forward into later milestones. Worked examples are toys, clearly labeled, and never assigned. When an assignment needs a design choice the spec has not settled (message shapes, state values), the professor decides it explicitly in the lesson with reasons rather than leaving it to the owner — the owner's work must always count toward the end product.
- Experiments and verification builds happen in `/tmp/opencode`, never in this repo, and are cleaned up afterward.
- README milestone states change only when the reviewer passes the work, with the verifying command noted.
- Keep processes clean. Do not leave a message broker, container, database, or `dotnet run` process running after a session. Run `docker compose down` when done.

## Environment
- Check the installed SDK with `dotnet --version`. If it is missing or wrong, tell the owner. Do not install it for them.
- Supporting services (message broker, database) run through Docker. See `docker/` once M0 is done.
- Build and test:
  ```
  cd src
  dotnet build
  dotnet test
  ```
- Start supporting services only for the work that needs them, and stop them afterward:
  ```
  docker compose -f docker/compose.yml up -d
  docker compose -f docker/compose.yml down
  ```

## Where things go
```
spec/shuttle-spec.md    the end product and milestones
docs/lessons/           NN-<slug>.md and NN-<slug>-hints.md
docs/decisions/         NN-<slug>.md, append-only
docs/reviews/           NN-<slug>.md
docs/contract/          the versioned message contract
src/                    the owner's code (fleet manager, shuttle simulator, shared contracts, tests)
docker/                 broker, database, and reproducible environment
```

## Commits
Small and frequent, one concept per commit. Commit messages say what changed and which milestone it belongs to (for example `M2: add command acknowledgement handling`).
