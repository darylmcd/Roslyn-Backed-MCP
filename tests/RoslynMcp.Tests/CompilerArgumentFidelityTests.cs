using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentFidelityTests
{
    [TestMethod]
    [DataRow("source")]
    [DataRow("recurse")]
    [DataRow("additionalfile")]
    [DataRow("analyzerconfig")]
    [DataRow("embed")]
    [DataRow("lib")]
    [DataRow("noconfig")]
    public async Task RewrittenInputs_RetainOfficialParserDiagnosticsAndPaths(string shape)
    {
        await InFixtureAsync(async (root, createStage) =>
        {
            var existing = Path.Combine(root, "with=equals");
            Directory.CreateDirectory(existing);
            await File.WriteAllTextAsync(Path.Combine(root, "inner.rsp"), "/noconfig\n/define:NESTED\n");
            await File.WriteAllTextAsync(Path.Combine(root, "outer.rsp"), "@inner.rsp\n");
            var arguments = new[] { shape switch
            {
                "source" => "with=equals/*.cs",
                "lib" => "-lib:with=equals/",
                "noconfig" => "@outer.rsp",
                _ => "-" + shape + ":with=equals/*.cs"
            } };
            var native = CSharpCommandLineParser.Default.Parse(arguments, root, RuntimeEnvironment.GetRuntimeDirectory());
            if (shape is "source" or "additionalfile" or "analyzerconfig" or "embed")
                Assert.IsTrue(native.Errors.Any(error => error.Id == "CS2001"));
            if (shape == "noconfig") Assert.IsTrue(native.Errors.Any(error => error.Id == "CS2023"));
            if (shape == "lib") Assert.IsTrue(native.ReferencePaths.Any(path => FileSystemPath.Comparer.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)), existing)));
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) => Task.CompletedTask;
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            string? stagePath = null;
            string CreateStage() => stagePath = createStage();
            var receipt = await new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CreateStage, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync).ParseAsync([.. arguments], CancellationToken.None);
            CollectionAssert.AreEquivalent(native.Errors.Select(error => error.Id).ToArray(), receipt.Arguments.Errors.Select(error => error.Id).ToArray());
            CollectionAssert.AreEqual(native.ReferencePaths.ToArray(), receipt.Arguments.ReferencePaths.ToArray());
            CollectionAssert.AreEquivalent(native.ParseOptions.PreprocessorSymbolNames.ToArray(), receipt.Arguments.ParseOptions.PreprocessorSymbolNames.ToArray());
            await receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            Assert.IsFalse(Directory.Exists(Path.Combine(root, ".roslynmcp")), "Parser staging must not create source namespaces.");
            AssertStageClean(stagePath);
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task MixedRulesetAndRecursiveSourceReceipt_CleansOnlyExactOwnedInputs(bool injectForeign)
    {
        await InFixtureAsync(async (root, createStage) =>
        {
            var source = Path.Combine(root, "Entry.cs");
            await File.WriteAllTextAsync(source, "public class Entry { }");
            await File.WriteAllTextAsync(Path.Combine(root, "root.ruleset"), "<RuleSet Name=\"Root\" ToolsVersion=\"14.0\" />");
            string? foreign = null;
            string? stagePath = null;
            var mutations = 0;
            string CreateStage() => stagePath = createStage();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (injectForeign && foreign is null && HasStagedRuleset(paths, root, stagePath))
                {
                    var directory = Path.Combine(stagePath!, "foreign");
                    Directory.CreateDirectory(directory);
                    foreign = Path.Combine(directory, "keep.txt");
                    File.WriteAllText(foreign, "owned-stage-bytes");
                    Assert.AreEqual("owned-stage-bytes", File.ReadAllText(foreign));
                    mutations++;
                }
                return Task.CompletedTask;
            }
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            var context = new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CreateStage, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            if (injectForeign)
            {
                Exception? failure = null;
                try { await context.ParseAsync(["-ruleset:root.ruleset", "-recurse:*.cs"], CancellationToken.None); }
                catch (Exception caught) { failure = caught; }
                Assert.AreEqual(1, mutations, "The actual source grant must inject an ordinary entry into the issued stage.");
                Assert.IsNotNull(foreign);
                Assert.IsInstanceOfType<PublicInvalidOperationException>(failure);
                Assert.IsFalse(File.Exists(foreign), "Subsequent ordinary contents belong to the admitted stage namespace.");
                AssertStageClean(stagePath);
            }
            else
            {
                var receipt = await context.ParseAsync(["-ruleset:root.ruleset", "-recurse:*.cs"], CancellationToken.None);
                Assert.AreEqual(source, receipt.Arguments.SourceFiles.Single().Path);
                Assert.IsFalse(Directory.Exists(Path.Combine(root, ".roslynmcp")));
                AssertStageClean(stagePath);
                await receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
                var later = Path.Combine(root, ".roslynmcp", "foreign");
                Directory.CreateDirectory(later);
                await File.WriteAllTextAsync(Path.Combine(later, "Later.cs"), "public class Later { }");
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
            }
            Assert.AreEqual("public class Entry { }", await File.ReadAllTextAsync(source));
        });
    }

    [TestMethod]
    public async Task UnexpectedStageAlias_RefusesAndPreservesExternalSentinel()
    {
        await InFixtureAsync(async (root, createStage) =>
        {
            var external = Path.Combine(root, "external");
            Directory.CreateDirectory(external);
            var sentinel = Path.Combine(external, "keep.txt");
            await File.WriteAllTextAsync(sentinel, "external-owned-bytes");
            await File.WriteAllTextAsync(Path.Combine(root, "root.ruleset"), "<RuleSet Name=\"Root\" ToolsVersion=\"14.0\" />");
            string? stagePath = null;
            string? alias = null;
            var mutations = 0;
            string CreateStage() => stagePath = createStage();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (alias is null && HasStagedRuleset(paths, root, stagePath))
                {
                    alias = Path.Combine(stagePath!, "foreign");
                    if (OperatingSystem.IsWindows()) WindowsDirectoryJunction.Create(alias, external);
                    else Directory.CreateSymbolicLink(alias, external);
                    Assert.IsTrue((File.GetAttributes(alias) & FileAttributes.ReparsePoint) != 0);
                    Assert.AreEqual("external-owned-bytes", File.ReadAllText(Path.Combine(alias, "keep.txt")));
                    mutations++;
                }
                return Task.CompletedTask;
            }
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            var context = new CompilerArgumentInputContext(root, Authorize, Read,
                (path, ct) => FileContentDigest.FromReaderAsync(path, Read, ct), CreateStage, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            Exception? failure = null;
            try { await context.ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None); }
            catch (Exception caught) { failure = caught; }
            Assert.AreEqual(1, mutations, "A real alias must be installed in the issued stage before the verdict.");
            Assert.IsNotNull(alias);
            Assert.IsInstanceOfType<PublicInvalidOperationException>(failure);
            Assert.AreEqual("external-owned-bytes", await File.ReadAllTextAsync(sentinel));
            Assert.IsFalse(Directory.Exists(alias));
            AssertStageClean(stagePath);
        });
    }

    [TestMethod]
    public async Task StageCleanupFailure_PreservesPrimaryAndReportsActualNativeFailure()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("Windows mandatory delete sharing is required for this cleanup failure control.");
        await InFixtureAsync(async (root, createStage) =>
        {
            await File.WriteAllTextAsync(Path.Combine(root, "root.ruleset"), "<RuleSet Name=\"Root\" ToolsVersion=\"14.0\"><Rules AnalyzerId=\"Compiler\" RuleNamespace=\"Compiler\"><Rule Id=\"CS0168\" Action=\"Error\" /></Rules></RuleSet>");
            string? stagePath = null;
            string? stagedRuleset = null;
            FileStream? blocker = null;
            var mutations = 0;
            var expected = new InvalidOperationException("Primary source authorization failure.");
            Exception? primary = null;
            string CreateStage() => stagePath = createStage();
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (blocker is null && HasStagedRuleset(paths, root, stagePath))
                {
                    stagedRuleset = Directory.GetFiles(stagePath!, "*.ruleset").Single();
                    blocker = new FileStream(stagedRuleset, FileMode.Open, FileAccess.Read, FileShare.Read);
                    Assert.ThrowsExactly<IOException>(() => File.Delete(stagedRuleset));
                    var native = CSharpCommandLineParser.Default.Parse(["-ruleset:\"" + stagedRuleset + "\""], root, RuntimeEnvironment.GetRuntimeDirectory());
                    Assert.AreEqual(Microsoft.CodeAnalysis.ReportDiagnostic.Error, native.CompilationOptions.SpecificDiagnosticOptions["CS0168"]);
                    mutations++;
                    throw expected;
                }
                return Task.CompletedTask;
            }
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            try
            {
                var context = new CompilerArgumentInputContext(root, Authorize, Read,
                    (path, ct) => FileContentDigest.FromReaderAsync(path, Read, ct), CreateStage, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
                Exception? failure = null;
                try { await context.ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None); }
                catch (Exception caught) { failure = caught; }
                Assert.AreEqual(1, mutations, "Native delete refusal and official parser read must precede the primary failure.");
                Assert.IsNotNull(stagedRuleset);
                Assert.IsTrue(File.Exists(stagedRuleset));
                Assert.AreSame(expected, failure);
                Assert.IsNotNull(failure);
                var outcomes = failure.Data[OwnedResourceCleanup.OutcomeDataKey] as string[];
                Assert.IsNotNull(outcomes);
                Assert.IsTrue(outcomes.Any(outcome => outcome.StartsWith("CompilerParserStage:", StringComparison.Ordinal)));
            }
            catch (Exception failure) { primary = failure; throw; }
            finally
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Native stage blocker release failed.", primary,
                    CleanupFailureCollector.FromAction(() => blocker?.Dispose()));
            }
        });
    }

    private static bool HasStagedRuleset(IReadOnlyList<string> paths, string root, string? stagePath)
        => paths.Any(path => FileSystemPath.Comparer.Equals(path, Path.Combine(root, "root.ruleset")))
            && stagePath is not null && Directory.Exists(stagePath)
            && Directory.GetFiles(stagePath, "*.ruleset").Length > 0;

    private static void AssertStageClean(string? stagePath)
    {
        Assert.IsNotNull(stagePath, "The context must issue an explicit private stage coordinate.");
        CompilerArgumentStageAssertions.AssertReclaimed(stagePath);
    }

    private static async Task InFixtureAsync(Func<string, Func<string>, Task> body)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentFidelityTests), Guid.NewGuid().ToString("N"));
        string? stagePath = null;
        string CreateStage() => stagePath = CompilerArgumentInputContext.CreateStagePath();
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await body(root, CreateStage);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Compiler fidelity fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => { if (stagePath is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(stagePath); }),
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
