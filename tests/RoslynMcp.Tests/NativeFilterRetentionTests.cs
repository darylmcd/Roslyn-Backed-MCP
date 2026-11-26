using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Host.Stdio;
using RoslynMcp.Host.Stdio.Tools;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeFilterRetentionTests
{
    private const string Filter = "/*/N/C/TAILLEAK";

    [TestMethod]
    [DataRow("--treenode-filter")]
    [DataRow("--TREENODE-FILTER=")]
    public void NativeArgumentAndEcho_ArePrivate(string option)
    {
        const string literal = "PRIVATE-ECHO";
        var execution = Execution(literal, literal, option);
        var projected = TestRunPublicProjection.CreateExecution(execution);
        Assert.AreEqual(new string('*', literal.Length), projected.StdOut);
        Assert.IsFalse(JsonSerializer.Serialize(projected, JsonDefaults.Indented).Contains(literal, StringComparison.Ordinal));
        Assert.AreEqual(literal, execution.StdOut);
        Assert.IsTrue(execution.Arguments.Any(a => a.Contains(literal, StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("Unknown")]
    [DataRow("BuildFailure")]
    [DataRow("FileLock")]
    [DataRow("Timeout")]
    [DataRow("TestReportFailure")]
    public void ParserTail_MasksBeforeItsOriginalCut(string kind)
    {
        var prefix = kind == "BuildFailure" ? "Build FAILED " : kind == "FileLock" ? "MSB3027 " : "";
        var source = prefix + Filter + new string('x', 1992);
        var execution = Execution(source, Filter, "--filter");
        var result = kind == "Timeout"
            ? DotnetOutputParser.BuildTimeoutResult(execution, "timeout")
            : DotnetOutputParser.ParseTestRun(execution, kind == "TestReportFailure" ? [WriteTrx(null, null, null, missing: true)] : []);
        if (kind == "TestReportFailure")
            result = DotnetOutputParser.ParseTestRun(execution with { Succeeded = true, ExitCode = 0 }, [WriteTrx(null, null, null, missing: true)]);
        Assert.AreEqual(kind, result.FailureEnvelope!.ErrorKind);
        Assert.AreEqual("TAILLEAK" + new string('x', 1992), result.FailureEnvelope.StdOutTail);
        var projected = TestRunPublicProjection.Create(result);
        Assert.AreEqual("********" + new string('x', 1992), projected.FailureEnvelope!.StdOutTail);
        Assert.AreEqual(projected.FailureEnvelope.StdOutTail, projected.FailureEnvelope.StdErrTail);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void TrxHeadsAndDisplayName_KeepOriginalCoordinates(bool withId)
    {
        const string dotted = "/*/Secret.Namespace/Class/Method";
        const string headFilter = "PRIVATE-HEAD";
        var message = new string('m', 496) + headFilter;
        var stack = new string('s', 1496) + headFilter;
        var execution = Execution("", dotted, "--filter") with { Arguments = ["--filter", dotted, "--treenode-filter", headFilter] };
        var result = DotnetOutputParser.ParseTestRun(execution, [WriteTrx(dotted, message, stack, withId)]);
        var raw = result.Failures.Single();
        Assert.AreEqual(withId ? dotted : "Namespace/Class/Method", raw.DisplayName);
        Assert.AreEqual(message[..500] + "... [truncated]", raw.Message);
        Assert.AreEqual(stack[..1500] + "... [truncated]", raw.StackTrace);
        var projected = TestRunPublicProjection.Create(result).Failures.Single();
        Assert.AreEqual(withId ? "<external-path>" : new string('*', raw.DisplayName.Length), projected.DisplayName);
        Assert.AreEqual(new string('m', 496) + "****... [truncated]", projected.Message);
        Assert.AreEqual(new string('s', 1496) + "****... [truncated]", projected.StackTrace);
    }

    [TestMethod]
    [DataRow(12000, "/*/N/C/TAILLEAK", 11992)]
    [DataRow(int.MaxValue, "ab", 0)]
    [DataRow(1, "ab", 0)]
    [Timeout(30000)]
    public async Task Runner_MasksBeforeCaptureWithoutChangingRawOrLiteralArguments(int limit, string filter, int padding)
    {
        var directory = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "child.ps1");
        var argvPath = Path.Combine(directory, "argv.json");
        await File.WriteAllTextAsync(scriptPath, "[IO.File]::WriteAllText($args[0], (ConvertTo-Json -Compress -InputObject @($args))); $text = $args[2] + ('x' * [int]$args[3]); [Console]::Out.Write($text); [Console]::Error.Write($text); exit 1");
        string[] arguments = ["-NoProfile", "-File", scriptPath, argvPath, "--treenode-filter", filter, padding.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var result = await new DotnetCommandRunner(limit).RunAsync(directory, "fixture", arguments, null, OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh", timeout.Token);
        var original = filter + new string('x', padding);
        var expectedRaw = original.Length > limit ? original[^limit..] : original;
        Assert.AreEqual(1, result.ExitCode);
        Assert.AreEqual(expectedRaw, result.StdOut);
        Assert.AreEqual(expectedRaw, result.StdErr);
        using var argv = JsonDocument.Parse(await File.ReadAllTextAsync(argvPath));
        Assert.AreEqual(filter, argv.RootElement[2].GetString());
        var expected = new string('*', filter.Length) + new string('x', padding);
        if (expected.Length > limit) expected = expected[^limit..];
        var projected = TestRunPublicProjection.CreateExecution(result);
        Assert.AreEqual(expected, projected.StdOut);
        Assert.AreEqual(expected, projected.StdErr);
    }

    [TestMethod]
    public void Paths_PreserveGeneratedMarkersAndActualTargetMasks()
    {
        var drive = Execution("C:\\Private\\unique.cs", "C", "--filter") with { WorkingDirectory = "C:/work", TargetPath = "C:/work/unique.cs" };
        Assert.AreEqual("<external-path>", TestRunPublicProjection.CreateExecution(drive).StdOut);
        var target = drive with { Arguments = ["--filter", "unique.cs"], StdOut = "prefixC:/work/unique.cs" };
        Assert.AreEqual("prefix*********", TestRunPublicProjection.CreateExecution(target).StdOut);
        Assert.AreEqual("C:\\Private\\unique.cs", drive.StdOut);
    }

    private static CommandExecutionDto Execution(string source, string filter, string option) =>
        new("dotnet", option.EndsWith('=') ? ["test", option + filter] : ["test", option, filter], ".", "Tests.csproj", 1, false, 1, source, source);

    [TestMethod]
    [DataRow("ababa!", "aba", "bab")]
    [DataRow("aaaaa!", "aaa", "aa")]
    [DataRow("xAbABAy", "aba", "BAB")]
    [DataRow("before😀AFTER!", "😀after", "BEFORE")]
    [DataRow("x𐐀𐐨y", "𐐨", "never")]
    [DataRow("partial-ab", "abc", "never")]
    [DataRow("a!", "a", "never")]
    public void Collector_AllChunkPartitionsMatchOriginalUnionAndTail(string source, string first, string second)
    {
        var filters = new TestFilterRedaction(["--filter", first, "--treenode-filter=" + second]);
        var expected = source.ToCharArray();
        foreach (var literal in new[] { first, second })
            for (var index = 0; index <= source.Length - literal.Length; index++)
                if (source.AsSpan(index, literal.Length).Equals(literal.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    Array.Fill(expected, '*', index, literal.Length);
        foreach (var limit in new[] { 1, 3, source.Length, int.MaxValue })
            for (var split = 0; split <= source.Length; split++)
                for (var next = split; next <= source.Length; next++)
                {
                    var collector = filters.CreateCollector(limit);
                    collector.Append(source[..split]);
                    collector.Append("");
                    collector.Append(source[split..next]);
                    collector.Append(source[next..]);
                    Assert.AreEqual(new string(expected.AsSpan(Math.Max(0, source.Length - limit))), collector.Complete(), $"{limit}/{split}/{next}");
                    Assert.ThrowsExactly<InvalidOperationException>(() => collector.Complete());
                    Assert.ThrowsExactly<InvalidOperationException>(() => collector.Append("late"));
                }
        Assert.AreEqual(new string(expected), filters.Mask(source));
    }

    [TestMethod]
    [DataRow("--filter")]
    [DataRow("--FILTER=")]
    [DataRow("--treenode-filter")]
    [DataRow("--TrEeNoDe-FiLtEr=")]
    public void Classifier_PreservesEmptyTrailingAndLiteralValues(string option)
    {
        const string literal = "a 'quoted' ☃\t b";
        var args = option.EndsWith('=') ? new[] { option + literal } : new[] { option, literal };
        var copy = args.ToArray();
        var filters = new TestFilterRedaction(args);
        Assert.AreEqual("!" + new string('*', literal.Length) + "!", filters.Mask("!" + literal + "!"));
        CollectionAssert.AreEqual(copy, args);
        Assert.IsFalse(new TestFilterRedaction(["--filter=", "--treenode-filter"]).HasFilters);
    }

    [TestMethod]
    public void Companions_AreCopiedByWithAndParticipateInRecordEqualityWithoutSerialization()
    {
        var raw = Execution("ab", "ab", "--filter");
        var safe = raw with { PublicStdOut = "**", PublicStdErr = "**" };
        Assert.AreEqual(safe, safe with { });
        Assert.AreNotEqual(raw, safe);
        Assert.AreEqual("**", (safe with { DurationMs = 5 }).PublicStdOut);
        var serialized = JsonSerializer.SerializeToElement(safe);
        Assert.IsFalse(serialized.EnumerateObject().Any(p => p.Name.StartsWith("Public", StringComparison.Ordinal)));
        Assert.AreEqual("ab", serialized.GetProperty("StdOut").GetString());
    }

    [TestMethod]
    public void Collector_Decoded4096BoundaryAndFinalPendingPreserveSourcePositions()
    {
        var source = new string('x', 4094) + "ababa" + "tail-ab";
        var filters = new TestFilterRedaction(["--filter", "aba", "--treenode-filter", "bab"]);
        var collector = filters.CreateCollector(12);
        collector.Append(source[..4096]);
        collector.Append(source[4096..]);
        Assert.AreEqual("*****tail-ab", collector.Complete());
    }

    [TestMethod]
    [DataRow(null, false, "unknown")]
    [DataRow(" ", false, "unknown")]
    [DataRow(" ", true, " ")]
    [DataRow("plain", false, "plain")]
    [DataRow("Outer.Secret.Method", false, "Method")]
    public void ParserLabelsAndGeneratedFallbacks_PreserveOriginalDecisions(string? name, bool withId, string expected)
    {
        var execution = Execution("", "unknown", "--filter");
        var result = DotnetOutputParser.ParseTestRun(execution, [WriteTrx(name, null, null, withId)]);
        Assert.AreEqual(expected, result.Failures.Single().DisplayName);
        Assert.AreEqual(expected, TestRunPublicProjection.Create(result).Failures.Single().DisplayName);
    }

    [TestMethod]
    public void Parser_GeneratedTruncationMarkerIsNeverRemasked()
    {
        var result = DotnetOutputParser.ParseTestRun(Execution("", "truncated", "--filter"),
            [WriteTrx("ordinary", new string('m', 496) + "truncated", null)]);
        Assert.AreEqual(new string('m', 496) + "****... [truncated]", TestRunPublicProjection.Create(result).Failures.Single().Message);
        Assert.AreEqual(new string('m', 496) + "trun... [truncated]", result.Failures.Single().Message);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    [Timeout(30000)]
    public async Task FilteredRunner_EarlyKillAndCancellationJoinOwnedPumps(bool cancel)
    {
        var directory = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "lifecycle.ps1");
        var ready = Path.Combine(directory, "ready");
        await File.WriteAllTextAsync(script, "[Console]::Out.Write('MSB3027');[Console]::Out.Flush();[Console]::Error.Write('MSB3027');[Console]::Error.Flush();[IO.File]::WriteAllText($args[0], 'ready');Start-Sleep -Seconds 15;exit 1");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var kills = 0;
        var runner = new DotnetCommandRunner(12000, process =>
        {
            Interlocked.Increment(ref kills);
            process.Kill(entireProcessTree: true);
        });
        var task = runner.RunAsync(directory, "fixture", ["-NoProfile", "-File", script, ready, "--treenode-filter", "MSB3027"],
            cancel ? null : [new EarlyKillPattern(new Regex("MSB3027", RegexOptions.CultureInvariant), "MSB3027")],
            OperatingSystem.IsWindows() ? "pwsh.exe" : "pwsh", cancellation.Token);
        try
        {
            if (cancel)
            {
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(ready) && !task.IsCompleted && DateTime.UtcNow < deadline)
                    await Task.Delay(10);
                Assert.IsTrue(File.Exists(ready), "The owned child must emit both streams before cancellation.");
                await cancellation.CancelAsync();
                await Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
            }
            else
            {
                var result = await task;
                Assert.AreEqual("MSB3027", result.EarlyKillReason);
                Assert.AreEqual("*******", TestRunPublicProjection.CreateExecution(result).EarlyKillReason);
                Assert.AreEqual(1, kills, "The original early-kill coordinator still wins once across both streams.");
                Assert.IsFalse(result.Succeeded);
            }
        }
        finally
        {
            await cancellation.CancelAsync();
            await ((Task)task).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    private static string WriteTrx(string? name, string? message, string? stack, bool withId = false, bool missing = false)
    {
        var directory = Path.Combine(TestTempRoot.Current, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "result.trx");
        if (missing) return path;
        var result = new XElement("UnitTestResult", new XAttribute("outcome", "Failed"),
            new XElement("Output", new XElement("ErrorInfo", new XElement("Message", message ?? "failure"), new XElement("StackTrace", stack ?? "stack"))));
        if (name is not null) result.Add(new XAttribute("testName", name));
        const string testId = "1a94a541-9c12-4b1c-9aa7-1c8f5b6ca2e3";
        if (withId) result.Add(new XAttribute("testId", testId));
        var testRun = new XElement("TestRun", new XElement("Results", result), new XElement("ResultSummary", new XAttribute("outcome", "Failed"),
            new XElement("Counters", new XAttribute("total", 1), new XAttribute("passed", 0), new XAttribute("failed", 1))));
        if (withId) testRun.Add(new XElement("TestDefinitions", new XElement("UnitTest", new XAttribute("id", testId),
            new XElement("TestMethod", new XAttribute("className", "Tests.Filtered"), new XAttribute("name", "Method")))));
        new XDocument(testRun).Save(path);
        return path;
    }
}
