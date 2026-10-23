using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentIndirectIoTests
{
    [TestMethod]
    [DataRow("additionalfile")]
    [DataRow("analyzerconfig")]
    [DataRow("embed")]
    public async Task MatchingIndirectPatterns_PreserveOfficialPathsAndBindBytesAndMembership(string option)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentIndirectIoTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var extension = option switch { "analyzerconfig" => ".editorconfig", "embed" => ".cs", _ => ".txt" };
            var input = Path.Combine(root, "with=equals" + extension);
            var bytes = System.Text.Encoding.UTF8.GetBytes("original compiler input");
            await File.WriteAllBytesAsync(input, bytes);
            var argument = "-" + option + ":*" + extension;
            var native = CSharpCommandLineParser.Default.Parse([argument], root, sdkDirectory: null);
            string[] Paths(CSharpCommandLineArguments arguments) => option switch
            {
                "analyzerconfig" => arguments.AnalyzerConfigPaths.ToArray(),
                "embed" => arguments.EmbeddedFiles.Select(file => file.Path).ToArray(),
                _ => arguments.AdditionalFiles.Select(file => file.Path).ToArray()
            };
            CollectionAssert.AreEqual(new[] { input }, Paths(native));
            var authorized = new HashSet<string>(FileSystemPath.Comparer);
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) { authorized.UnionWith(paths); return Task.CompletedTask; }
            Task<byte[]?> Read(string path, CancellationToken ct)
            {
                Assert.IsTrue(authorized.Contains(path));
                return FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            }
            var receipt = await new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync).ParseAsync([argument], CancellationToken.None);
            CollectionAssert.AreEqual(Paths(native), Paths(receipt.Arguments));
            await receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            await File.WriteAllTextAsync(input, "changed compiler input");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
            await File.WriteAllBytesAsync(input, bytes);
            await receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root, "Added" + extension), "another compiler input");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Indirect pattern fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("ruleset")]
    [DataRow("recurse")]
    [DataRow("lib")]
    [DataRow("libequals")]
    public async Task ArgumentInducedIo_RequiresCurrentApprovalBeforeOfficialParser(string kind)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentIndirectIoTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var nested = Path.Combine(root, "nested.ruleset");
            var directory = Path.Combine(root, kind == "libequals" ? "with=equals" : "inputs");
            Directory.CreateDirectory(directory);
            var source = Path.Combine(directory, "Input.cs");
            await File.WriteAllTextAsync(source, "public class Input { }");
            await File.WriteAllTextAsync(nested, "<RuleSet Name=\"Nested\" ToolsVersion=\"14.0\"><Rules AnalyzerId=\"Compiler\" RuleNamespace=\"Compiler\"><Rule Id=\"CS0168\" Action=\"Error\" /></Rules></RuleSet>");
            await File.WriteAllTextAsync(Path.Combine(root, "root.ruleset"), "<RuleSet Name=\"Root\" ToolsVersion=\"14.0\"><Include Path=\"nested.ruleset\" Action=\"Default\" /></RuleSet>");
            var argument = kind switch { "ruleset" => "-ruleset:root.ruleset", "recurse" => "-recurse:inputs/*.cs", _ => "-lib:" + Path.GetFileName(directory) };
            var native = CSharpCommandLineParser.Default.Parse([argument], root, sdkDirectory: null);
            switch (kind)
            {
                case "ruleset": Assert.AreEqual(ReportDiagnostic.Error, native.CompilationOptions.SpecificDiagnosticOptions["CS0168"]); break;
                case "recurse": Assert.AreEqual(source, native.SourceFiles.Single().Path); break;
                case "lib":
                case "libequals": CollectionAssert.Contains(native.ReferencePaths.ToArray(), directory); break;
            }
            var denied = kind == "ruleset" ? nested : directory;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                if (paths.Any(path => FileSystemPath.Comparer.Equals(path, denied)))
                    throw new UnauthorizedAccessException("Synthetic argument input denial.");
                return Task.CompletedTask;
            }
            async Task<byte[]?> Read(string path, CancellationToken ct) => await File.ReadAllBytesAsync(path, ct);
            var context = new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => context.ParseAsync([argument], CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Indirect compiler input fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task RecursiveSourcePatterns_BindDirectoryMembershipAcrossParsing()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentIndirectIoTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var source = Path.Combine(root, "Initial.cs");
            await File.WriteAllTextAsync(source, "public class Initial { }");
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) => Task.CompletedTask;
            async Task<byte[]?> Read(string path, CancellationToken ct) => await File.ReadAllBytesAsync(path, ct);
            var context = new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            var receipt = await context.ParseAsync(["-recurse:*.cs"], CancellationToken.None);
            Assert.AreEqual(source, receipt.Arguments.SourceFiles.Single().Path);
            await receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            await File.WriteAllTextAsync(Path.Combine(root, "Added.cs"), "public class Added { }");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Recursive compiler input fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
