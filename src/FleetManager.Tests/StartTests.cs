using System.Diagnostics;

namespace FleetManager.Tests;

public class StartTests
{
    
    private const int StartTimeoutMs = 30_000;

    [Fact]
    public async Task FleetManager_Starts_Successfully()
    {
        var dll = Path.Combine(AppContext.BaseDirectory, "FleetManager.dll");
        var psi = new ProcessStartInfo { FileName = "dotnet", RedirectStandardOutput = true };
        psi.ArgumentList.Add(dll);

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var exited = process.WaitForExit(StartTimeoutMs);
        if (!exited) process.Kill(entireProcessTree: true);
        Assert.True(exited, $"the demo did not exit within {StartTimeoutMs} ms");
        var output = await stdout;

        Assert.Equal(0, process.ExitCode);
        Assert.NotEmpty(output);
        Assert.DoesNotContain("Hello, World!", output);
    }
}

