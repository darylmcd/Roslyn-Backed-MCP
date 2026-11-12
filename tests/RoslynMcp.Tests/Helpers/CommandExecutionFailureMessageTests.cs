using RoslynMcp.Core.Models;

namespace RoslynMcp.Tests.Helpers;

[TestClass]
public sealed class CommandExecutionFailureMessageTests
{
    [TestMethod]
    public void Describe_IncludesExitCodeEarlyKillReasonAndBothStreams()
    {
        var result = new CommandExecutionDto(
            "dotnet", ["restore", "a.csproj"], "/work", "a.csproj", ExitCode: 137, Succeeded: false,
            DurationMs: 42, StdOut: "OUT-TEXT", StdErr: "ERR-TEXT", EarlyKillReason: "matched MSB3027");

        var message = result.Describe("context-label");

        StringAssert.Contains(message, "context-label");
        StringAssert.Contains(message, "dotnet restore a.csproj");
        StringAssert.Contains(message, "exitCode: 137");
        StringAssert.Contains(message, "earlyKillReason: matched MSB3027");
        StringAssert.Contains(message, "OUT-TEXT");
        StringAssert.Contains(message, "ERR-TEXT");
    }

    [TestMethod]
    public void Describe_ReportsNoneWhenNotEarlyKilled()
    {
        var result = new CommandExecutionDto("dotnet", [], "/w", "t", 0, true, 1, "", "");

        StringAssert.Contains(result.Describe(), "earlyKillReason: <none>");
    }
}
