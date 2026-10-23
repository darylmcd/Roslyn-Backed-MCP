using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentStageOwnershipTests
{
    private const string Rules = "<RuleSet Name=\"Root\" ToolsVersion=\"14.0\"><Rules AnalyzerId=\"Compiler\" RuleNamespace=\"Compiler\"><Rule Id=\"CS0168\" Action=\"Error\" /></Rules></RuleSet>";
    private const string Sentinel = "Unrelated replacement namespace must survive.";

    [TestMethod]
    public async Task ParserStage_IsPrivateAndNeverRequestsSourceMutationAuthority()
    {
        await InFixtureAsync(async root =>
        {
            await File.WriteAllTextAsync(Path.Combine(root, "root.ruleset"), Rules);
            var stagingRoot = Path.Combine(root, ".roslynmcp", "compiler-inputs");
            var stage = CompilerArgumentInputContext.CreateStagePath();
            Exception? primary = null;
            try
            {
                Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
                {
                    if (paths.Any(path => FileSystemPath.Comparer.Equals(path, stage)
                        || FileSystemPath.IsStrictDescendant(stage, path)
                        || FileSystemPath.Comparer.Equals(path, stagingRoot)
                        || FileSystemPath.IsStrictDescendant(stagingRoot, path)))
                        throw new UnauthorizedAccessException("Source authority does not grant parser staging mutations.");
                    return Task.CompletedTask;
                }
                var receipt = await CreateContext(root, Authorize, () => stage).ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None);
                Assert.AreEqual(ReportDiagnostic.Error, receipt.Arguments.CompilationOptions.SpecificDiagnosticOptions["CS0168"]);
                Assert.IsFalse(Directory.Exists(Path.Combine(root, ".roslynmcp")), "Parsing must not create staging parents in the source tree.");
                if (OperatingSystem.IsWindows()) Assert.IsFalse(Directory.Exists(stage), "Windows must genuinely remove the issued stage.");
                else
                {
                    Assert.IsTrue(Directory.Exists(stage), "POSIX retains the exact owned empty directory.");
                    Assert.HasCount(0, Directory.GetFileSystemEntries(stage));
                }
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                // The test knows this fresh issued coordinate; remove only its empty remainder.
                await CleanupFailureCollector.RunAfterFailureAsync("Private parser stage cleanup failed.", primary,
                    CleanupFailureCollector.FromAction(() => { if (Directory.Exists(stage)) Directory.Delete(stage, recursive: false); }));
            }
        });
    }

    // The old unowned source stage deleted a same-byte replacement file. After
    // private empty-object admission, contents belong to the service namespace;
    // the protected replacement boundary is its root and published ancestor route.
    [TestMethod]
    [DataRow("file")]
    [DataRow("directory")]
    [DataRow("link")]
    [DataRow("ancestor")]
    public async Task CleanupAfterPrimaryFailure_PreservesReplacementNamespaceAndExactFailure(string replacementKind)
    {
        await InFixtureAsync(async root =>
        {
            var source = Path.Combine(root, "source");
            var parent = Path.Combine(root, "stage-parent");
            var stage = Path.Combine(parent, "issued");
            var retained = Path.Combine(root, "retained");
            var external = Path.Combine(root, "external");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(external);
            await File.WriteAllTextAsync(Path.Combine(source, "root.ruleset"), Rules);
            await File.WriteAllTextAsync(Path.Combine(external, "preserve.txt"), Sentinel);
            var primary = new InvalidOperationException("primary parser authority refusal");
            var attempted = false;
            var blocked = false;
            var replaced = false;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (!attempted && Directory.Exists(stage))
                {
                    attempted = true;
                    try { Directory.Move(replacementKind == "ancestor" ? parent : stage, retained); }
                    catch (IOException) when (OperatingSystem.IsWindows()) { blocked = true; throw primary; }
                    if (replacementKind == "file") File.WriteAllText(stage, Sentinel);
                    else if (replacementKind == "link") Directory.CreateSymbolicLink(stage, external);
                    else
                    {
                        Directory.CreateDirectory(stage);
                        File.WriteAllText(Path.Combine(stage, "preserve.txt"), Sentinel);
                    }
                    replaced = true;
                    throw primary;
                }
                return Task.CompletedTask;
            }
            var observed = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                CreateContext(source, Authorize, () => stage).ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None));
            Assert.AreSame(primary, observed, "Cleanup must retain the exact primary failure.");
            Assert.IsTrue(attempted && (blocked || replaced), "The actual replacement must execute or be prevented by the held Windows route.");
            Assert.AreEqual(Sentinel, await File.ReadAllTextAsync(Path.Combine(external, "preserve.txt")));
            if (replaced)
            {
                Assert.AreEqual(Sentinel, await File.ReadAllTextAsync(replacementKind == "file" ? stage : Path.Combine(stage, "preserve.txt")));
                var original = replacementKind == "ancestor" ? Path.Combine(retained, "issued") : retained;
                Assert.HasCount(0, Directory.GetFileSystemEntries(original), "Cleanup must reclaim the original owned contents through its held descriptor.");
            }
            else Assert.IsFalse(Directory.Exists(stage), "Windows must remove the original stage after its route prevented replacement.");
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PublishedStageRoute_RefusesRootOrAncestorReplacementBeforeParserRead(bool ancestor)
    {
        await InFixtureAsync(async root =>
        {
            var source = Path.Combine(root, "source");
            var parent = Path.Combine(root, "stage-parent");
            var stage = Path.Combine(parent, "issued");
            var retained = Path.Combine(root, "retained");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(parent);
            await File.WriteAllTextAsync(Path.Combine(source, "root.ruleset"), Rules);
            var attempted = false;
            var blocked = false;
            var replaced = false;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (!attempted && Directory.Exists(stage))
                {
                    attempted = true;
                    try { Directory.Move(ancestor ? parent : stage, retained); }
                    catch (IOException) when (OperatingSystem.IsWindows()) { blocked = true; return Task.CompletedTask; }
                    Directory.CreateDirectory(stage);
                    File.WriteAllText(Path.Combine(stage, "preserve.txt"), Sentinel);
                    replaced = true;
                }
                return Task.CompletedTask;
            }
            CompilerArgumentParseReceipt? receipt = null;
            PublicInvalidOperationException? refusal = null;
            try { receipt = await CreateContext(source, Authorize, () => stage).ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None); }
            catch (PublicInvalidOperationException failure) { refusal = failure; }
            Assert.IsTrue(attempted && (blocked || replaced));
            if (replaced)
            {
                Assert.IsNotNull(refusal, "The held parent alone cannot prove the published absolute stage route.");
                Assert.IsNull(receipt);
                Assert.AreEqual(Sentinel, await File.ReadAllTextAsync(Path.Combine(stage, "preserve.txt")));
            }
            else
            {
                Assert.IsNull(refusal);
                Assert.AreEqual(ReportDiagnostic.Error, receipt!.Arguments.CompilationOptions.SpecificDiagnosticOptions["CS0168"]);
            }
        });
    }

    [TestMethod]
    public async Task RecursiveSourceContainingStageCandidate_RefusesBeforeCreatingStage()
    {
        await InFixtureAsync(async root =>
        {
            var stage = Path.Combine(root, "never-created-stage");
            await File.WriteAllTextAsync(Path.Combine(root, "root.ruleset"), Rules);
            await File.WriteAllTextAsync(Path.Combine(root, "Entry.cs"), "class Entry { }");
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) => Task.CompletedTask;
            var refusal = await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                CreateContext(root, Authorize, () => stage).ParseAsync(["-ruleset:root.ruleset", "-recurse:*.cs"], CancellationToken.None));
            StringAssert.Contains(refusal.PublicMessage, "outside every recursive source traversal");
            Assert.IsFalse(Directory.Exists(stage), "Disjointness must be proved before any stage creation effect.");
            Assert.IsFalse(Directory.Exists(Path.Combine(root, ".roslynmcp")));
        });
    }

    private static CompilerArgumentInputContext CreateContext(string root,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize, Func<string> stagePathFactory)
    {
        Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
        return new(root, authorize, Read, (path, ct) => FileContentDigest.FromReaderAsync(path, Read, ct), stagePathFactory,
            CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
    }

    private static async Task InFixtureAsync(Func<string, Task> body)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentStageOwnershipTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await body(root);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Parser stage ownership fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
