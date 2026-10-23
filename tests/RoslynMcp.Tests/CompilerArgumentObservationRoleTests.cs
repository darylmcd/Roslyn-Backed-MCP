using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentObservationRoleTests
{
    [TestMethod]
    public Task ActualResponseRulesetAndMetadataReads_RetainOverlappingRolesThroughResolution()
        => WithRootAsync(async root =>
        {
            var outer = Path.Combine(root, "outer.rsp");
            var nested = Path.Combine(root, "nested.rsp");
            var rules = Path.Combine(root, "Shared.ruleset");
            var included = Path.Combine(root, "Included.ruleset");
            await File.WriteAllTextAsync(outer, "@nested.rsp");
            await File.WriteAllTextAsync(nested, "/ruleset:Shared.ruleset /reference:Shared.ruleset");
            await File.WriteAllTextAsync(rules, "<RuleSet Name=\"Shared\" ToolsVersion=\"15.0\"><Include Path=\"Included.ruleset\" Action=\"Default\" /></RuleSet>");
            await File.WriteAllTextAsync(included, "<RuleSet Name=\"Included\" ToolsVersion=\"15.0\" />");
            var parser = CreateParser(root, new HashSet<string>(FileSystemPath.Comparer));
            var parsed = await parser.ParseAsync(["/nostdlib+", "@outer.rsp", "*.rsp", "*.ruleset"], CancellationToken.None);
            Assert.HasCount(1, parsed.Arguments.MetadataReferences, "This fixture isolates its one explicit reference from the compiler parser's implicit standard library.");
            Assert.AreEqual("Shared.ruleset", parsed.Arguments.MetadataReferences.Single().Reference);
            var configurations = await parser.CaptureInputsAsync([rules, nested], CompilerArgumentInputRole.AnalyzerConfig, CancellationToken.None);
            var augmented = parsed.WithInputs(configurations);
            var resolved = await parser.ResolveReferencesAsync(augmented, [], CancellationToken.None);

            AssertRole(resolved.Parsing, outer, CompilerArgumentInputRole.Response | CompilerArgumentInputRole.SourceMatch);
            AssertRole(resolved.Parsing, nested, CompilerArgumentInputRole.Response | CompilerArgumentInputRole.SourceMatch | CompilerArgumentInputRole.AnalyzerConfig);
            AssertRole(resolved.Parsing, included, CompilerArgumentInputRole.Ruleset | CompilerArgumentInputRole.SourceMatch);
            AssertRole(resolved.Parsing, rules, CompilerArgumentInputRole.Ruleset | CompilerArgumentInputRole.SourceMatch
                | CompilerArgumentInputRole.AnalyzerConfig | CompilerArgumentInputRole.MetadataProbe);
            Assert.AreEqual(4, resolved.Parsing.Inputs.Length, "Role overlap must not create duplicate physical input slots.");
            CollectionAssert.AreEquivalent(new[] { outer, nested, rules, included },
                resolved.Parsing.Inputs.Select(input => input.FilePath).ToArray());
            foreach (var file in new[] { outer, nested, rules, included })
                Assert.IsTrue(resolved.Parsing.CurrentGrantPaths.Contains(file, FileSystemPath.Comparer));
            await resolved.Parsing.AssertPhysicalCurrentAsync(new HashSet<string>(FileSystemPath.Comparer), CancellationToken.None);
            await File.AppendAllTextAsync(rules, "\n<!-- changed after the receipt -->");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                resolved.Parsing.AssertPhysicalCurrentAsync(new HashSet<string>(FileSystemPath.Comparer), CancellationToken.None));
        });

    [TestMethod]
    [DataRow("source")]
    [DataRow("recurse")]
    [DataRow("additional")]
    [DataRow("config")]
    [DataRow("embed")]
    public Task ActualWildcardReads_RecordTheirSpecificRoleWithoutChangingPhysicalGuards(string kind)
        => WithRootAsync(async root =>
        {
            var selected = Path.Combine(root, "selected.txt");
            await File.WriteAllTextAsync(selected, "public class Selected { }");
            var parser = CreateParser(root, new HashSet<string>(FileSystemPath.Comparer));
            var (argument, role) = kind switch
            {
                "source" => ("*.txt", CompilerArgumentInputRole.SourceMatch),
                "recurse" => ("/recurse:*.txt", CompilerArgumentInputRole.SourceMatch),
                "additional" => ("/additionalfile:*.txt", CompilerArgumentInputRole.AdditionalMatch),
                "config" => ("/analyzerconfig:*.txt", CompilerArgumentInputRole.AnalyzerConfig),
                "embed" => ("/embed:*.txt", CompilerArgumentInputRole.EmbeddedMatch),
                _ => throw new AssertFailedException("Unknown role fixture.")
            };
            var receipt = await parser.ParseAsync([argument], CancellationToken.None);
            AssertRole(receipt, selected, role);
            Assert.AreEqual(1, receipt.Inputs.Length);
            Assert.AreEqual(1, receipt.Directories.Length, "Role evidence must retain the original enumeration receipt.");
            await receipt.AssertPhysicalCurrentAsync(new HashSet<string>(FileSystemPath.Comparer), CancellationToken.None);
            await File.WriteAllTextAsync(selected, "public class Changed { }");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                receipt.AssertPhysicalCurrentAsync(new HashSet<string>(FileSystemPath.Comparer), CancellationToken.None));
        });

    [TestMethod]
    public Task ActualMetadataResolver_RetainsDirectoryMissingAndFileProbeRoles()
        => WithRootAsync(async root =>
        {
            var first = Path.Combine(root, "first");
            var second = Path.Combine(root, "second");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            var name = "OwnedReference.dll";
            var occupied = Path.Combine(root, name);
            Directory.CreateDirectory(occupied);
            var missing = Path.Combine(first, name);
            var selected = Path.Combine(second, name);
            await File.WriteAllTextAsync(selected, "resolver input bytes; no compiler image load is requested");
            var parser = CreateParser(root, new HashSet<string>(FileSystemPath.Comparer));
            var parsed = await parser.ParseAsync(["/nostdlib+", "/lib:first,second", "/reference:OwnedReference.dll"], CancellationToken.None);
            Assert.HasCount(1, parsed.Arguments.MetadataReferences, "This fixture isolates its one explicit reference from the compiler parser's implicit standard library.");
            Assert.AreEqual(name, parsed.Arguments.MetadataReferences.Single().Reference);
            var resolved = await parser.ResolveReferencesAsync(parsed, [], CancellationToken.None);
            foreach (var file in new[] { occupied, missing, selected })
                AssertRole(resolved.Parsing, file, CompilerArgumentInputRole.MetadataProbe);
            Assert.AreEqual(PhysicalInputKind.Directory, resolved.Parsing.Inputs.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, occupied)).Kind);
            Assert.AreEqual(PhysicalInputKind.Missing, resolved.Parsing.Inputs.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, missing)).Kind);
            Assert.AreEqual(PhysicalInputKind.File, resolved.Parsing.Inputs.Single(input => FileSystemPath.Comparer.Equals(input.FilePath, selected)).Kind);
            Assert.AreEqual(selected, resolved.References.Single().Path);
            await File.WriteAllTextAsync(missing, "a new earlier resolution candidate");
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() =>
                resolved.Parsing.AssertPhysicalCurrentAsync(new HashSet<string>(FileSystemPath.Comparer), CancellationToken.None));
        });

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task ActualRulesetProbe_RetainsMissingAndDirectoryRoles(bool directory)
        => WithRootAsync(async root =>
        {
            var selected = Path.Combine(root, "Selected.ruleset");
            if (directory) Directory.CreateDirectory(selected);
            var parser = CreateParser(root, new HashSet<string>(FileSystemPath.Comparer));
            var receipt = await parser.ParseAsync(["/ruleset:Selected.ruleset"], CancellationToken.None);
            AssertRole(receipt, selected, CompilerArgumentInputRole.Ruleset);
            Assert.AreEqual(directory ? PhysicalInputKind.Directory : PhysicalInputKind.Missing, receipt.Inputs.Single().Kind);
            await receipt.AssertPhysicalCurrentAsync(new HashSet<string>(FileSystemPath.Comparer), CancellationToken.None);
        });

    [TestMethod]
    [DataRow("source")]
    [DataRow("additional")]
    [DataRow("config")]
    [DataRow("embed")]
    public Task DirectOperand_DoesNotGainAnObservationOrARead(string kind)
        => WithRootAsync(async root =>
        {
            var source = Path.Combine(root, "Direct.cs");
            await File.WriteAllTextAsync(source, "class Direct { }");
            var reads = new HashSet<string>(FileSystemPath.Comparer);
            var parser = CreateParser(root, reads);
            var argument = kind switch
            {
                "source" => "Direct.cs",
                "additional" => "/additionalfile:Direct.cs",
                "config" => "/analyzerconfig:Direct.cs",
                "embed" => "/embed:Direct.cs",
                _ => throw new AssertFailedException("Unknown direct operand fixture.")
            };
            var receipt = await parser.ParseAsync([argument], CancellationToken.None);
            var selected = kind switch
            {
                "source" => receipt.Arguments.SourceFiles.Single().Path,
                "additional" => receipt.Arguments.AdditionalFiles.Single().Path,
                "config" => receipt.Arguments.AnalyzerConfigPaths.Single(),
                "embed" => receipt.Arguments.EmbeddedFiles.Single().Path,
                _ => throw new AssertFailedException("Unknown direct operand fixture.")
            };
            Assert.AreEqual(source, selected);
            Assert.IsFalse(reads.Contains(source), "Observation roles must not introduce source operand IO.");
            Assert.AreEqual(0, receipt.Observations.Length);
            Assert.AreEqual(0, receipt.Inputs.Length);
        });

    [TestMethod]
    public Task WithInputs_RejectsConflictingPhysicalIdentityBeforeCombiningRoles()
        => WithRootAsync(async root =>
        {
            var config = Path.Combine(root, "Generated.editorconfig");
            await File.WriteAllTextAsync(config, "root = true\n");
            var parser = CreateParser(root, new HashSet<string>(FileSystemPath.Comparer));
            var parsed = await parser.ParseAsync(["*.editorconfig"], CancellationToken.None);
            var captured = await parser.CaptureInputsAsync([config], CompilerArgumentInputRole.AnalyzerConfig, CancellationToken.None);
            var combined = parsed.WithInputs(captured).WithInputs(captured);
            AssertRole(combined, config, CompilerArgumentInputRole.SourceMatch | CompilerArgumentInputRole.AnalyzerConfig);
            Assert.AreEqual(1, combined.Inputs.Length);
            await File.WriteAllTextAsync(config, "root = false\n");
            var changed = await parser.CaptureInputsAsync([config], CompilerArgumentInputRole.AnalyzerConfig, CancellationToken.None);
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => combined.WithInputs(changed));
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => parsed.WithInputs(
                [new CompilerArgumentInputObservation(captured.Single().Input, (CompilerArgumentInputRole)0)]));
        });

    [TestMethod]
    [DataRow(0, false)]
    [DataRow(128, false)]
    [DataRow(129, false)]
    [DataRow(0, true)]
    [DataRow(128, true)]
    [DataRow(129, true)]
    public Task CaptureInputs_InvalidRoleRefusesBeforeAnyGrantOrRead(int value, bool empty)
        => WithRootAsync(async root =>
        {
            var calls = 0;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                calls++;
                return Task.CompletedTask;
            }
            Task<byte[]?> Read(string path, CancellationToken ct)
            {
                calls++;
                return Task.FromResult<byte[]?>(null);
            }
            var parser = new CompilerArgumentInputContext(root, Authorize, Read,
                (path, ct) => FileContentDigest.FromReaderAsync(path, Read, ct), CompilerArgumentInputContext.CreateStagePath,
                CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => parser.CaptureInputsAsync(
                empty ? [] : [Path.Combine(root, "not-read.editorconfig")], (CompilerArgumentInputRole)value, CancellationToken.None));
            Assert.AreEqual(0, calls, "Invalid roles must be refused before enumeration, authorization or physical reads.");
        });

    [TestMethod]
    [DataRow(0)]
    [DataRow(128)]
    [DataRow(129)]
    public Task Receipt_InvalidRoleRefusesZeroUnknownAndMixedFlags(int value)
        => WithRootAsync(async root =>
        {
            var config = Path.Combine(root, "Valid.editorconfig");
            await File.WriteAllTextAsync(config, "root = true\n");
            var parser = CreateParser(root, new HashSet<string>(FileSystemPath.Comparer));
            var parsed = await parser.ParseAsync(["*.editorconfig"], CancellationToken.None);
            var input = parsed.Inputs.Single();
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => parsed.WithInputs(
                [new CompilerArgumentInputObservation(input, (CompilerArgumentInputRole)value)]));
        });

    private static void AssertRole(CompilerArgumentParseReceipt receipt, string path, CompilerArgumentInputRole role)
        => Assert.AreEqual(role, receipt.Observations.Single(observation => FileSystemPath.Comparer.Equals(observation.Input.FilePath, path)).Roles);

    private static CompilerArgumentInputContext CreateParser(string root, HashSet<string> reads)
    {
        Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
        Task<byte[]?> Read(string path, CancellationToken ct)
        {
            reads.Add(path);
            return FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
        }
        return new(root, Authorize, Read,
            (path, ct) => FileContentDigest.FromReaderAsync(path, Read, ct), CompilerArgumentInputContext.CreateStagePath,
            CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
    }

    private static async Task WithRootAsync(Func<string, Task> action)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentObservationRoleTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            await action(root);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Compiler observation role fixture cleanup failed.", primary,
                OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }
}
