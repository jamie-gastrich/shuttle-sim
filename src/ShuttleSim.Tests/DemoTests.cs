using System.Diagnostics;
using System.Text.Json;
using Contracts;

namespace ShuttleSim.Tests;

public class DemoTests
{
    private const int DemoTimeoutMs = 30_000;

    [Fact]
    public async Task Demo_prints_a_status_line_per_step()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "ShuttleSim.dll");
        var psi = new ProcessStartInfo { FileName = "dotnet", RedirectStandardOutput = true };
        psi.ArgumentList.Add(dll);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var exited = process.WaitForExit(DemoTimeoutMs);
        if (!exited) process.Kill(entireProcessTree: true);
        Assert.True(exited, $"the demo did not exit within {DemoTimeoutMs} ms");
        var output = await stdout;

        var statuses = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => JsonSerializer.Deserialize<ShuttleStatus>(line))
            .ToList();

        Assert.Equal(7, statuses.Count);
        Assert.Equal(
            new[] { "idle", "loading", "idle", "moving", "idle", "unloading", "idle" },
            statuses.Select(s => s!.State).ToArray());
        Assert.All(statuses, s => Assert.Equal("SH-01", s!.ShuttleId));
        Assert.Equal("A01-L01", statuses[3]!.Position);
        Assert.Equal("A02-L02", statuses[^1]!.Position);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 14, DateTimeKind.Utc), statuses[^1]!.Timestamp);
    }
}