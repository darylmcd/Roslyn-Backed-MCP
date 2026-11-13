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
    public void Describe_KeepsOnlyTheLastStreamCharsWithTruncationMarker()
    {
        var max = CommandExecutionFailureMessage.MaxStreamChars;
        var stdout = "HEAD-MARK" + new string('x', max) + "TAIL-MARK";
        var result = new CommandExecutionDto("dotnet", [], "/w", "t", 1, false, 1, stdout, "small-err");

        var message = result.Describe();

        var dropped = stdout.Length - max;
        StringAssert.Contains(message, $"... [truncated {dropped} chars]");
        StringAssert.Contains(message, "TAIL-MARK");
        Assert.DoesNotContain("HEAD-MARK", message);
        StringAssert.Contains(message, "small-err");
        Assert.IsLessThan(2 * max, message.Length);
    }

    [TestMethod]
    public void Tail_ReturnsShortTextUnchanged()
    {
        Assert.AreEqual("abc", CommandExecutionFailureMessage.Tail("abc", 3));
        Assert.AreEqual(string.Empty, CommandExecutionFailureMessage.Tail(null));
    }

    [TestMethod]
    public void Describe_ReportsNoneWhenNotEarlyKilled()
    {
        var result = new CommandExecutionDto("dotnet", [], "/w", "t", 0, true, 1, "", "");

        StringAssert.Contains(result.Describe(), "earlyKillReason: <none>");
    }
}
