using System.Diagnostics;

namespace FleetManager.Tests;

public class StartTests
{
    [Fact]
    public void FleetManager_Starts_Successfully()
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = "run --project ../FleetManager",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        })?? throw new InvalidOperationException("Could not start FleetManager.");
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        Assert.NotEmpty(process.StandardOutput.ReadToEnd());
        Assert.DoesNotContain("error", process.StandardOutput.ReadToEnd());
    }
}