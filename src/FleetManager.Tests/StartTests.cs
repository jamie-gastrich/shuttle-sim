using System.Diagnostics;

namespace FleetManager.Tests;

public class StartTests
{
    [Fact]
    public void FleetManager_Starts_Successfully()
    {
         var dll = Path.Combine(AppContext.BaseDirectory, "FleetManager.dll");

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
        };
        psi.ArgumentList.Add(dll);

        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();

        Assert.Equal(0, p.ExitCode);
        Assert.NotEmpty(output);
        Assert.DoesNotContain("Hello, World!", output);
    }
}