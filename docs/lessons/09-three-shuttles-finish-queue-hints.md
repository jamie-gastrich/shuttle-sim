# Hints for Lesson 09: Three shuttles finish a queue; state survives a restart

## Spoiler warnings
Each heading says what you need to find or do. Try the hints in order; the first line that helps wins.

## 1. Assignment loop structure
Where does the fleet manager take the next task and give it to a shuttle?
- Check `src/FleetManager/Program.cs` — the `RunMoveAsync` method shows how commands are sent.
- The assignment loop should live near the board and repo: `TaskBoard.NextQueued()` + `FleetTask.Assign()` + `PostgresFleetTaskRepository.SaveAsync()`.
- Look for a method named something like `AssignNextQueued` or `DispatchTasks` in `FleetManager`.

## 2. Round-robin shuttle order
If you have three shuttles (SH-01, SH-02, SH-03) and tasks numbered 1–9, which shuttle gets which task?
- The simplest correct policy is round-robin: task 1 → SH-01, task 2 → SH-02, task 3 → SH-03, task 4 → SH-01, etc.
- Use the shuttle index: `shuttles[taskIndex % shuttles.Length]`. No policy document needed for M3.

## 3. End-to-end test: three shuttles + 9 tasks
The test needs at least 9 tasks (3 per shuttle) with mixed states. What states are needed?
- 3 queued tasks (assigned first, then completed by each shuttle)
- 3 in-progress tasks (after assign, before complete)
- You can have some tasks already done or failed from prior runs, but the test should start with a mix.

## 4. Restart survival: new TaskBoard + LoadAll
After the three shuttles finish, the proof that state survives restart:
- Persist via `repo.SaveAllAsync(expected, ct)`
- Create a fresh `var board = new TaskBoard()`
- Load via `var loaded = await repo.LoadAllAsync(ct)`
- Add each loaded task to the board in order
- Assert `board.Count == 9` and `board.NextQueued()` returns the oldest queued task

## 5. Connection string from configuration
The test must never hard-code a connection string. Use the same pattern as `RestartSurvivalTests.BuildConnectionString()`:
- Check `SHUTTLE_DB_CONNECTION_STRING` env var first
- Else build from `SHUTTLE_DB_HOST`, `DB_HOST_PORT`, `SHUTTLE_DB_USER`, `SHUTTLE_DB_PASSWORD`, `SHUTTLE_DB_NAME`
- These env var names match `docker/README.md` and `docker/compose.yml`

## 6. Trait and container filter
- The test carries `[Trait("Category", "Integration")]`
- The fresh-container command runs: `dotnet test ... --filter "Category!=Integration"`
- With the database up, run: `--filter "Category=Integration"` to prove the test executed

## 7. What the acceptance greps verify
- Phase 1: assignment loop code exists in FleetManager
- Phase 2: pure tests pass without DB (container RC 0, 7 Passed!)
- Phase 3: with DB, integration test passes (RC 0, "Passed <test name>")
- Phase 4: contract still v2, no v3.md

## 8. What is NOT required (stay focused)
- Do not implement traffic reservations (M4). This lesson is about assignment + persistence.
- Do not implement fault recovery/reassignment (M5). `Done` and `Failed` are terminal.
- Do not implement WMS integration (M6) or dashboard (M7).
- Do not change the contract version.

---
Stuck? These hints cover the key ideas without giving solution code. The acceptance criteria in the lesson file are the authoritative spec.