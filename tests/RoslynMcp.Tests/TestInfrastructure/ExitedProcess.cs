using System.Diagnostics;

namespace RoslynMcp.Tests;

/// <summary>Supplies a process id proven dead, for tests that need a pid no live process owns.</summary>
internal static class ExitedProcess
{
    /// <summary>
    /// Runs <c>dotnet --version</c> to completion and returns its <see cref="Process"/>. The caller keeps it
    /// undisposed through its assertions: on Windows the held handle keeps the pid from being recycled.
    /// Never hardcode a "dead" pid; any number may belong to a live process.
    /// </summary>
    public static Process StartAndAwaitExit()
    {
        var child = Process.Start(new ProcessStartInfo("dotnet", "--version")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;
        child.StandardOutput.ReadToEnd();
        child.WaitForExit();
        return child;
    }
}
