using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
// Ruleset expansion controls mutate the process environment inherited by native evaluations.
[DoNotParallelize]
public sealed class CompilerArgumentRulesetTests
{
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task CaseVariantIncludeOrder_RetainsOfficialPhysicalAndLowercaseCycleIdentity(bool uppercaseFirst)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentRulesetTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var lower = Path.Combine(root, "case.ruleset");
            var upper = Path.Combine(root, "CASE.ruleset");
            var template = "<RuleSet Name=\"Child\" ToolsVersion=\"14.0\"><Rules AnalyzerId=\"Compiler\" RuleNamespace=\"Compiler\"><Rule Id=\"CS0168\" Action=\"ACTION\" /></Rules></RuleSet>";
            await File.WriteAllTextAsync(lower, template.Replace("ACTION", "Error", StringComparison.Ordinal));
            await File.WriteAllTextAsync(upper, template.Replace("ACTION", "Warning", StringComparison.Ordinal));
            var order = uppercaseFirst ? new[] { upper, lower } : new[] { lower, upper };
            await File.WriteAllTextAsync(Path.Combine(root, "root.ruleset"), "<RuleSet Name=\"Root\" ToolsVersion=\"14.0\">"
                + string.Join("", order.Select(path => "<Include Path=\"" + Path.GetFileName(path) + "\" Action=\"Default\" />")) + "</RuleSet>");
            var native = CSharpCommandLineParser.Default.Parse(["-ruleset:root.ruleset"], root, sdkDirectory: null);
            Assert.IsTrue(native.CompilationOptions.SpecificDiagnosticOptions.ContainsKey("CS0168"));
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) => Task.CompletedTask;
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            var receipt = await new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync).ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None);
            CollectionAssert.AreEquivalent(native.CompilationOptions.SpecificDiagnosticOptions.ToArray(), receipt.Arguments.CompilationOptions.SpecificDiagnosticOptions.ToArray());
            await receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            await File.WriteAllTextAsync(upper, template.Replace("ACTION", "None", StringComparison.Ordinal));
            await Assert.ThrowsExactlyAsync<RoslynMcp.Core.Services.PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Ruleset case fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public async Task IncludedRulesetEnvironmentOperand_RemainsBoundToPreparedExpansion()
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentRulesetTests), Guid.NewGuid().ToString("N"));
        var variable = "ROSLYN_RULESET_" + Guid.NewGuid().ToString("N");
        var previousValue = Environment.GetEnvironmentVariable(variable);
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await File.WriteAllTextAsync(Path.Combine(root, "child.ruleset"), "<RuleSet Name=\"Child\" ToolsVersion=\"14.0\"><Rules AnalyzerId=\"Compiler\" RuleNamespace=\"Compiler\"><Rule Id=\"CS0168\" Action=\"Error\" /></Rules></RuleSet>");
            await File.WriteAllTextAsync(Path.Combine(root, "root.ruleset"), "<RuleSet Name=\"Root\" ToolsVersion=\"14.0\"><Include Path=\"%" + variable + "%\" Action=\"Default\" /></RuleSet>");
            Environment.SetEnvironmentVariable(variable, "child.ruleset");
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct) => Task.CompletedTask;
            Task<byte[]?> Read(string path, CancellationToken ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            var receipt = await new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync).ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None);
            Assert.AreEqual(ReportDiagnostic.Error, receipt.Arguments.CompilationOptions.SpecificDiagnosticOptions["CS0168"]);
            await receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            Environment.SetEnvironmentVariable(variable, "another.ruleset");
            await Assert.ThrowsExactlyAsync<RoslynMcp.Core.Services.PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Ruleset environment fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => Environment.SetEnvironmentVariable(variable, previousValue)),
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("cycle")]
    [DataRow("duplicate")]
    [DataRow("suppressed")]
    [DataRow("dtd")]
    public async Task SecureRulesetReplay_PreservesOfficialIncludeSemantics(string shape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentRulesetTests), Guid.NewGuid().ToString("N"));
        string? stagePath = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var child = Path.Combine(root, "child.ruleset");
            var ruleset = Path.Combine(root, "root.ruleset");
            var childXml = "<RuleSet Name=\"Child\" ToolsVersion=\"14.0\"><Rules AnalyzerId=\"Compiler\" RuleNamespace=\"Compiler\"><Rule Id=\"CS0168\" Action=\"Error\" /></Rules>"
                + (shape == "cycle" ? "<Include Path=\"root.ruleset\" Action=\"Default\" />" : "") + "</RuleSet>";
            await File.WriteAllTextAsync(child, childXml);
            await File.WriteAllTextAsync(ruleset, shape == "dtd"
                ? "<!DOCTYPE RuleSet SYSTEM \"denied.dtd\"><RuleSet Name=\"Root\" ToolsVersion=\"14.0\" />"
                : "<RuleSet Name=\"Root\" ToolsVersion=\"14.0\"><Include Path=\"child.ruleset\" Action=\""
                    + (shape == "suppressed" ? "None" : "Default") + "\" />"
                    + (shape == "duplicate" ? "<Include Path=\"child.ruleset\" Action=\"Warning\" />" : "") + "</RuleSet>");
            var native = CSharpCommandLineParser.Default.Parse(["-ruleset:root.ruleset"], root, sdkDirectory: null);
            if (shape == "dtd") Assert.IsTrue(native.Errors.Any(error => error.Severity == DiagnosticSeverity.Error));
            else if (shape == "suppressed") Assert.IsFalse(native.CompilationOptions.SpecificDiagnosticOptions.ContainsKey("CS0168"));
            else Assert.AreEqual(ReportDiagnostic.Error, native.CompilationOptions.SpecificDiagnosticOptions["CS0168"]);
            var authorized = new HashSet<string>(FileSystemPath.Comparer);
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                foreach (var path in paths)
                {
                    if (Path.GetFileName(path) == "denied.dtd" || shape == "suppressed" && FileSystemPath.Comparer.Equals(path, child))
                        throw new UnauthorizedAccessException("Inactive ruleset inputs must not be opened.");
                    authorized.Add(path);
                }
                return Task.CompletedTask;
            }
            async Task<byte[]?> Read(string path, CancellationToken ct)
            {
                Assert.IsTrue(authorized.Contains(path));
                return await FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            }
            string CreateStage() => stagePath = CompilerArgumentInputContext.CreateStagePath();
            var receipt = await new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CreateStage, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync).ParseAsync(["-ruleset:root.ruleset"], CancellationToken.None);
            CollectionAssert.AreEquivalent(native.Errors.Select(error => error.Id).ToArray(), receipt.Arguments.Errors.Select(error => error.Id).ToArray());
            CollectionAssert.AreEquivalent(native.CompilationOptions.SpecificDiagnosticOptions.ToArray(), receipt.Arguments.CompilationOptions.SpecificDiagnosticOptions.ToArray());
            await receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
            if (shape is "cycle" or "duplicate")
            {
                Assert.IsTrue(receipt.Inputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, child)));
                await File.WriteAllTextAsync(child, childXml.Replace("Error", "Warning", StringComparison.Ordinal));
                await Assert.ThrowsExactlyAsync<RoslynMcp.Core.Services.PublicInvalidOperationException>(() => receipt.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None));
            }
            else Assert.IsFalse(receipt.Inputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, child)));
            Assert.IsFalse(Directory.Exists(Path.Combine(root, ".roslynmcp", "compiler-inputs"))
                && Directory.EnumerateFileSystemEntries(Path.Combine(root, ".roslynmcp", "compiler-inputs")).Any());
            if (shape != "dtd") Assert.IsNotNull(stagePath, "Valid ruleset replay must exercise its actual private staging namespace.");
            if (stagePath is not null) CompilerArgumentStageAssertions.AssertReclaimed(stagePath);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Ruleset fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => { if (stagePath is not null) TestFixtureFileSystem.DeleteDirectoryIfExists(stagePath); }),
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
