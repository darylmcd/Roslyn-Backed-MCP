using RoslynMcp.Core.Models;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class TestRunOutputReaderTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReportedFileOutsideInvocation_DoesNotBecomeCurrentResults(bool successfulHistoricalReport)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(TestRunOutputReaderTests), Guid.NewGuid().ToString("N"));
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var invocation = Path.Combine(root, "invocation");
            owner = PhysicalDirectoryScope.CreateOwnedRoot(invocation);
            var historical = Path.Combine(root, "historical", "previous.trx");
            WriteTrx(historical, "PreviousInvocation", failed: !successfulHistoricalReport);
            var before = File.ReadAllBytes(historical);
            var execution = new CommandExecutionDto("dotnet", ["test"], root, Path.Combine(root, "Test.slnx"),
                0, true, 1, $"Results File: {historical}\n", "");

            var result = await TestRunOutputReader.ParseAsync(owner, execution, CancellationToken.None);

            Assert.AreEqual(0, result.Total, "A path printed by the process cannot grant access or prove this invocation produced the report.");
            Assert.IsEmpty(result.Failures, "Unbound external report contents must not enter the public result.");
            CollectionAssert.AreEqual(before, File.ReadAllBytes(historical));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await CleanupAsync(root, owner, primary); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MultipleFrameworkResults_AggregateReportedPathsWithoutDuplicatesOrHistoricalFiles(bool mixedLayout)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(TestRunOutputReaderTests), Guid.NewGuid().ToString("N"));
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var explicitDirectory = Path.Combine(root, "invocation");
            owner = PhysicalDirectoryScope.CreateOwnedRoot(explicitDirectory);
            var first = Path.Combine(mixedLayout ? explicitDirectory : Path.Combine(explicitDirectory, "net9.0"), "first.trx");
            var second = Path.Combine(explicitDirectory, "net10.0", "second.trx");
            WriteTrx(first, "Net9Failure", failed: true);
            WriteTrx(second, "Net10Pass", failed: false);
            WriteTrx(Path.Combine(root, "historical", "ignored.trx"), "HistoricalFailure", failed: true);
            var firstRelative = Path.GetRelativePath(root, first);
            var stdout = $"Results File: {first}\nResults File: {firstRelative}\nResults File: \"{second}\"\nResults File: {second}\n";
            var execution = new CommandExecutionDto("dotnet", ["test"], root, Path.Combine(root, "Test.slnx"), 1, false, 1, stdout, "");
            var result = await TestRunOutputReader.ParseAsync(owner, execution, CancellationToken.None);
            Assert.AreEqual(2, result.Total);
            Assert.AreEqual(1, result.Failed);
            Assert.AreEqual(1, result.Passed);
            Assert.HasCount(1, result.Failures);
            Assert.AreEqual("Net9Failure", result.Failures[0].DisplayName);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await CleanupAsync(root, owner, primary); }
    }

    [TestMethod]
    public async Task CancelledCollection_DoesNotConsumeReportsOrChangeCancellationIdentity()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(TestRunOutputReaderTests), Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(root, "invocation"));
            var report = Path.Combine(owner.Path, "result.trx");
            WriteTrx(report, "Unconsumed", failed: true);
            var before = await File.ReadAllBytesAsync(report);
            cancellation.Cancel();
            var execution = new CommandExecutionDto("dotnet", ["test"], root, Path.Combine(root, "Test.slnx"), 0, true, 1, "", "");
            var failure = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() =>
                TestRunOutputReader.ParseAsync(owner, execution, cancellation.Token));
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(report));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await CleanupAsync(root, owner, primary); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ReplacedPublishedRootOrAncestor_RefusesForeignReports(bool replaceAncestor)
    {
        if (OperatingSystem.IsWindows())
            Assert.Inconclusive("Windows holds namespace pins; POSIX substitution is exercised on Linux.");
        var root = Path.Combine(TestTempRoot.Current, nameof(TestRunOutputReaderTests), Guid.NewGuid().ToString("N"));
        var parent = Path.Combine(root, "parent");
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(parent);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(parent, "invocation"));
            WriteTrx(Path.Combine(owner.Path, "current.trx"), "Current", failed: false);
            var issued = replaceAncestor ? parent : owner.Path;
            Directory.Move(issued, issued + "-detached");
            var foreign = Path.Combine(owner.Path, "foreign.trx");
            WriteTrx(foreign, "Foreign", failed: true);
            var before = await File.ReadAllBytesAsync(foreign);
            var execution = new CommandExecutionDto("dotnet", ["test"], root, Path.Combine(root, "Test.slnx"), 0, true, 1, "Results File: " + foreign, "");
            await Assert.ThrowsExactlyAsync<IOException>(() => TestRunOutputReader.ParseAsync(owner, execution, CancellationToken.None));
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(foreign));
            // Restore this test's issued route before normal owner teardown. The
            // foreign control remains intact in a separate owned fixture directory.
            Directory.Move(issued, issued + "-foreign");
            Directory.Move(issued + "-detached", issued);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await CleanupAsync(root, owner, primary); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task LinkedReportOrNestedDirectory_RefusesBeforeConsumingTarget(bool directoryLink)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(TestRunOutputReaderTests), Guid.NewGuid().ToString("N"));
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(root, "invocation"));
            var external = Path.Combine(root, "external", "private.trx");
            WriteTrx(external, "Foreign", failed: true);
            var before = await File.ReadAllBytesAsync(external);
            try
            {
                if (directoryLink) Directory.CreateSymbolicLink(Path.Combine(owner.Path, "linked"), Path.GetDirectoryName(external)!);
                else File.CreateSymbolicLink(Path.Combine(owner.Path, "linked.trx"), external);
            }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows())
            {
                Assert.Inconclusive("This Windows token cannot create the symbolic-link control; Linux runs it.");
            }
            catch (IOException exception) when (OperatingSystem.IsWindows() && (exception.HResult & 0xffff) == 1314)
            {
                Assert.Inconclusive("Windows rejected symbolic-link creation with ERROR_PRIVILEGE_NOT_HELD; Linux runs this control.");
            }
            var execution = new CommandExecutionDto("dotnet", ["test"], root, Path.Combine(root, "Test.slnx"), 0, true, 1, "Results File: " + external, "");
            var refusal = await Assert.ThrowsExactlyAsync<IOException>(() => TestRunOutputReader.ParseAsync(owner, execution, CancellationToken.None));
            StringAssert.Contains(refusal.Message, "physical regular files and directories");
            CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(external));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally { await CleanupAsync(root, owner, primary); }
    }

    private static ValueTask CleanupAsync(string root, PhysicalDirectoryScope.OwnedRoot? owner, Exception? primary)
        => CleanupFailureCollector.RunAfterFailureAsync("Report collector fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => owner?.Delete(CancellationToken.None)),
            CleanupFailureCollector.FromAction(() => owner?.Dispose()),
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));

    private static void WriteTrx(string path, string name, bool failed)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"""
            <TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <Results><UnitTestResult testName="{name}" outcome="{(failed ? "Failed" : "Passed")}"><Output><ErrorInfo>
                <Message>controlled failure</Message></ErrorInfo></Output></UnitTestResult></Results>
              <ResultSummary outcome="{(failed ? "Failed" : "Passed")}"><Counters total="1" passed="{(failed ? 0 : 1)}" failed="{(failed ? 1 : 0)}" notExecuted="0" /></ResultSummary>
            </TestRun>
            """);
    }
}
