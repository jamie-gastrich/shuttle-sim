using FleetCore;

namespace FleetCore.Tests;

public class PauseStateTests
{
    [Fact]
    public void Pause_is_idempotent()
    {
        var state = new PauseState();

        state.Pause("SH-01");
        state.Pause("SH-01");

        Assert.Single(state.Snapshot());
    }

    [Fact]
    public void Resume_on_unpaused_id_does_nothing()
    {
        var state = new PauseState();

        state.Resume("SH-01");

        Assert.False(state.IsPaused("SH-01"));
    }

    [Fact]
    public void PauseAll_pauses_every_listed_shuttle()
    {
        var state = new PauseState();
        var shuttleIds = new[] { "SH-01", "SH-02", "SH-03" };

        state.PauseAll(shuttleIds);

        Assert.All(shuttleIds, shuttleId => Assert.True(state.IsPaused(shuttleId)));
    }

    [Fact]
    public void ResumeAll_resumes_every_listed_shuttle()
    {
        var state = new PauseState();
        var shuttleIds = new[] { "SH-01", "SH-02", "SH-03" };
        state.PauseAll(shuttleIds);

        state.ResumeAll(shuttleIds);

        Assert.All(shuttleIds, shuttleId => Assert.False(state.IsPaused(shuttleId)));
    }

    [Fact]
    public void Blank_and_null_ids_are_ignored()
    {
        var state = new PauseState();

        state.Pause(null!);
        state.Pause(" ");
        state.Resume(null!);
        state.PauseAll(new string[] { "SH-01", null!, "" });
        state.ResumeAll(null);

        Assert.Equal(new[] { "SH-01" }, state.Snapshot());
        Assert.False(state.IsPaused(null!));
        Assert.False(state.IsPaused(" "));
    }

    [Fact]
    public async Task Snapshot_is_consistent_under_concurrency()
    {
        var state = new PauseState();
        const int threads = 4;
        const int operations = 1000;
        var tasks = new Task[threads];

        for (var thread = 0; thread < threads; thread++)
        {
            var id = thread;
            tasks[thread] = Task.Run(() =>
            {
                var shuttleId = $"SH-{id}";
                for (var operation = 0; operation < operations; operation++)
                {
                    state.Pause(shuttleId);
                    _ = state.IsPaused(shuttleId);
                    if (operation % 2 == 0) state.Resume(shuttleId);
                    _ = state.Snapshot();
                }
                state.Pause(shuttleId);
            });
        }

        await Task.WhenAll(tasks);

        var snapshot = state.Snapshot();
        Assert.Equal(threads, snapshot.Count);
        Assert.Equal(threads, snapshot.Distinct(StringComparer.Ordinal).Count());
    }
}
