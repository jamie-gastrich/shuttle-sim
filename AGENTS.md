# AGENTS.md

How work happens in this repo. Every agent reads this file, `spec/shuttle-spec.md`, and the latest files in `docs/` before doing anything.

## Purpose
The owner is learning fleet management software for automated pallet shuttles by directing the build of a simulated shuttle system: shuttles, a fleet manager, traffic control, a mock WMS, and an operator dashboard.

The professor teaches. The implementer builds each lesson's assignment. The owner reviews the lesson and the code, signs off, and then sends the work to the reviewer. The owner does not have to write the code, but **the owner must be able to explain every line of it out loud.** That is the goal, and the owner's review gate below exists to protect it. Teaching that sticks matters more than moving fast.

## Roles

| Role | Does | Never does |
|---|---|---|
| **Owner** | Directs the work, reads each lesson, reviews the implementer's code, answers the explain-it questions, signs off, commits, and submits the work to the reviewer. | Skips the review gate. |
| **Professor** | Teaches one small concept at a time (plain words, at most 3 new concepts per lesson, real code walkthrough, worked example), records decisions, and writes the assignment the implementer will build. Writes only to `docs/lessons/` and `docs/decisions/`. May run commands to verify claims, in `/tmp/opencode` only. | Writes the assignment's solution, touches `src/`, or builds the project. |
| **Implementer** | Builds the lesson's assignment in `src/` (and `docker/`), tests it, verifies it with real commands, and writes a build report. Fixes review findings when the owner sends them. | Works without a lesson, goes beyond the assignment, changes the contract without a decision record, writes lessons, decisions, or reviews, marks milestones done, commits, or pushes. |
| **Reviewer** | Builds, runs tests, and reviews the implementer's work (after the owner has signed off) against the lesson, the decision record, and the spec. Asks explain-it questions. Writes to `docs/reviews/`, and updates only the milestone table in `README.md` after a passing review. | Edits or rewrites code in `src/`. |

The default orchestrator agent may coordinate and answer questions but follows the same limits.

## The loop
1. The professor picks the next concept from the current milestone in the spec.
2. Professor writes the lesson, the decision record, and the assignment, with the files the implementer may touch and what is out of scope.
3. **Owner reads the lesson.** If it is unclear, too big, or wrong, it goes back to the professor.
4. The implementer builds the assignment, runs the checks, and writes `docs/builds/NN-<slug>-report.md`. It does not commit or push.
5. **Owner review gate** (below). The owner signs off in the report.
6. The owner commits and sends the work to the reviewer, who writes `docs/reviews/NN-<slug>.md`, including explain-it questions.
7. The owner sends any blockers to the implementer to fix, then reviews the fixes. The professor reads the review and adapts the next lesson.
8. A milestone is done when its "done when" criteria in the spec are met and the review passes.

## Owner review gate
Before work goes to the reviewer, the owner does all of this:
1. Read the lesson. Does it follow the plain-words and 3-concept limits? Does the assignment match what the spec needs?
2. Read the diff for every file in the report's file list.
3. Start with the report's "Study first" spots. Explain each one out loud without looking at the answer.
4. Run the build and test commands yourself and see them pass.
5. Answer the lesson's "Say it out loud" questions.
6. Add a sign-off line to the report: `Owner sign-off: <date>, explained and verified`.

If the owner cannot explain a piece, the owner asks the professor for a walkthrough of it before going on. The reviewer refuses work whose report has no sign-off.

## Rules
- **A claim is either verified by a command in this repo or marked "not yet verified."** No claim that merely sounds like it works.
- **Logs are append-only.** `docs/decisions/` entries are never rewritten. If a decision is replaced, add a new entry that references the old one.
- **Decisions carry their reasons**: what was chosen, what was rejected, why, and what would change the choice.
- **The message contract is the source of truth** for everything between the fleet manager and the shuttles. It is documented and versioned in `docs/contract/`. Changing it needs a decision record and a version bump. The implementer changes the contract only to apply a change that a decision record specifies. If an assignment seems to need any other change, the implementer stops and reports.
- **Decision logic is separated from input and output** where practical: task assignment, traffic control, and state machines are pure code with no broker, database, or network calls, so they can be unit tested alone.
- **No hard-coded names, topics, timeouts, retry counts, or sizes.** Use configuration.
- **Shuttles talk to the fleet manager only through the contract.** No shortcuts through shared code or shared memory.
- Tests must pass before a review can pass.
- **No throwaway work.** Every assignment builds a real piece of the end product that carries forward into later milestones. Worked examples are toys, clearly labeled, and never assigned. When an assignment needs a design choice the spec has not settled (message shapes, state values), the professor decides it explicitly in the lesson with reasons rather than leaving it to the implementer.
- **Implementer code stays simple.** Plain names, short methods, comments that explain why. No clever abstractions and no dependencies the assignment did not allow, because the owner has to explain every line.
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
docs/builds/            NN-<slug>-report.md (the implementer's build report, with the owner's sign-off)
docs/reviews/           NN-<slug>.md
docs/contract/          the versioned message contract
src/                    the code (fleet manager, shuttle simulator, shared contracts, tests)
docker/                 broker, database, and reproducible environment
```

## Commits
Small and frequent, one concept per commit. Commit messages say what changed and which milestone it belongs to (for example `M2: add command acknowledgement handling`). The owner commits, after the review gate. Agents do not commit or push.
