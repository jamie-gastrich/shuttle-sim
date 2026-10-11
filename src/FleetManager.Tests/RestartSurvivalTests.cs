using System.Diagnostics;
using FleetCore;
using Npgsql;

namespace FleetManager.Tests;

public class RestartSurvivalTests
{
    private static readonly string ConnectionString = BuildConnectionString();

    [Fact]
    public async Task State_survives_restart_after_persisting_tasks()
    {
        await EnsureDatabaseIsRunningAsync();
        var repo = new PostgresFleetTaskRepository(ConnectionString);
        await repo.LoadAllAsync(CancellationToken.None);
        await ResetDatabaseAsync();

        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var queuedA = new FleetTask("task-queued-a", "A01-L01", start.AddTicks(7));
        var queuedB = new FleetTask("task-queued-b", "A01-L02", start.AddMinutes(1));

        var assigned = new FleetTask("task-assigned", "A01-L03", start.AddMinutes(2));
        assigned.Assign("SH-01", start.AddMinutes(2).AddSeconds(1));

        var inProgress = new FleetTask("task-in-progress", "A01-L04", start.AddMinutes(3));
        inProgress.Assign("SH-02", start.AddMinutes(3).AddSeconds(1));
        inProgress.Start(start.AddMinutes(3).AddSeconds(2));

        var done = new FleetTask("task-done", "A01-L05", start.AddMinutes(4));
        done.Assign("SH-03", start.AddMinutes(4).AddSeconds(1));
        done.Start(start.AddMinutes(4).AddSeconds(2));
        done.Complete(start.AddMinutes(4).AddSeconds(3));

        var failed = new FleetTask("task-failed", "A01-L06", start.AddMinutes(5));
        failed.Fail("rack empty", start.AddMinutes(5).AddSeconds(1));

        var expected = new[] { queuedA, queuedB, assigned, inProgress, done, failed };

        await repo.SaveAllAsync(expected, CancellationToken.None);

        var reloaded = (await repo.LoadAllAsync(CancellationToken.None)).ToList();
        var boardAfterRestart = new TaskBoard();
        foreach (var task in reloaded)
            boardAfterRestart.Add(task);

        Assert.Equal(expected.Length, boardAfterRestart.Count);
        Assert.Equal(new[]
        {
            "task-queued-a",
            "task-queued-b",
            "task-assigned",
            "task-in-progress",
            "task-done",
            "task-failed",
        }, reloaded.Select(task => task.TaskId).ToArray());

        foreach (var expectedTask in expected)
        {
            var actual = boardAfterRestart.Get(expectedTask.TaskId);
            Assert.Equal(expectedTask.TaskId, actual.TaskId);
            Assert.Equal(expectedTask.ToLane, actual.ToLane);
            Assert.Equal(expectedTask.CreatedAt, actual.CreatedAt);
            Assert.Equal(expectedTask.State, actual.State);
            Assert.Equal(expectedTask.AssignedShuttleId, actual.AssignedShuttleId);
            Assert.Equal(expectedTask.FailureReason, actual.FailureReason);
        }

        Assert.Equal("task-queued-a", boardAfterRestart.NextQueued()!.TaskId);
        Assert.Equal("task-queued-b", boardAfterRestart.Get("task-queued-b").TaskId);
    }

    private static string BuildConnectionString()
    {
        var configuredConnectionString = Environment.GetEnvironmentVariable("SHUTTLE_DB_CONNECTION_STRING");
        if (!string.IsNullOrWhiteSpace(configuredConnectionString))
            return configuredConnectionString;

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Environment.GetEnvironmentVariable("SHUTTLE_DB_HOST") ?? "127.0.0.1",
            Username = Environment.GetEnvironmentVariable("SHUTTLE_DB_USER") ?? "shuttle",
            Password = Environment.GetEnvironmentVariable("SHUTTLE_DB_PASSWORD") ?? "shuttle",
            Database = Environment.GetEnvironmentVariable("SHUTTLE_DB_NAME") ?? "shuttle",
        };

        var configuredPort = Environment.GetEnvironmentVariable("DB_HOST_PORT");
        if (!string.IsNullOrWhiteSpace(configuredPort))
        {
            if (!int.TryParse(configuredPort, out var port))
                throw new InvalidOperationException("DB_HOST_PORT must be a valid integer.");

            builder.Port = port;
        }

        return builder.ConnectionString;
    }

    private static async Task EnsureDatabaseIsRunningAsync()
    {
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                return;
            }
            catch (NpgsqlException)
            {
                if (attempt == 20)
                    throw;

                await StartDatabaseAsync();
                await Task.Delay(1000);
            }
        }
    }

    private static async Task StartDatabaseAsync()
    {
        var repoRoot = FindRepoRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "compose -f docker/compose.yml up -d database",
            WorkingDirectory = repoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start docker compose for the database test dependency.");

        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            var stderr = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"docker compose failed to start the database: {stderr}");
        }
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "docker", "compose.yml")))
                return directory.FullName;

            directory = directory.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private static async Task ResetDatabaseAsync()
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        await using (var deleteCommand = new NpgsqlCommand("DELETE FROM fleet_tasks", connection))
        {
            await deleteCommand.ExecuteNonQueryAsync();
        }
    }
}