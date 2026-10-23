using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class CompilerArgumentNativeProjectReferenceTests
{
    [TestMethod]
    [DataRow("complete")]
    [DataRow("missing")]
    [DataRow("ambiguous")]
    [DataRow("excess")]
    public void TaskMetadataAliasOccurrences_RetainProducerIdentityWithoutDroppingAdditionalArguments(string shape)
    {
        var path = Path.Combine(TestTempRoot.Current, "Metadata.dll");
        var producer = new NativeCompilerTaskInputs.Reference(path, path, "", ["global", "reviewed"], false, MetadataImageKind.Assembly);
        var first = new CompilerArgumentReference(path, new MetadataReferenceProperties(MetadataImageKind.Assembly), CompilerArgumentReferenceKind.MetadataFile);
        var second = new CompilerArgumentReference(path, new MetadataReferenceProperties(MetadataImageKind.Assembly, ["reviewed"]), CompilerArgumentReferenceKind.MetadataFile);
        ImmutableArray<NativeCompilerTaskInputs.Reference> producers = shape == "ambiguous" ? [producer, producer] : [producer];
        ImmutableArray<CompilerArgumentReference> arguments = shape switch
        {
            "missing" => [first],
            "excess" => [first, second, second],
            _ => [first, second]
        };
        if (shape is "missing" or "ambiguous")
            Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerTaskInputs.ReconcileReferenceOccurrences(producers, arguments, TestTempRoot.Current));
        else
        {
            var result = NativeCompilerTaskInputs.ReconcileReferenceOccurrences(producers, arguments, TestTempRoot.Current);
            Assert.HasCount(shape == "excess" ? 2 : 1, result);
            Assert.AreEqual(producer, result[0]);
            if (shape == "excess") CollectionAssert.AreEqual(new[] { "reviewed" }, result[1].Aliases.ToArray(),
                "An extra response-file reference remains a separate input for the native metadata equivalence check.");
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task RealCscAliasSwitches_RetainOneExactNativeReference(bool embed)
    {
        var root = Path.Combine(TestTempRoot.Current, "native-alias-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var output = Path.Combine(root, "Unbuilt.dll");
            var arguments = NativeCscReferenceArguments.Generate(output, "global,reviewed", embed);
            var parsedReferences = CompilerArgumentInputContext.TaskReferenceArguments(arguments, root);
            Assert.HasCount(2, parsedReferences, "The installed Csc must demonstrate its one-switch-per-alias emission contract.");
            var taskReference = new NativeCompilerTaskInputs.Reference(output, output, "", ["global", "reviewed"], embed, MetadataImageKind.Assembly);
            NativeCompilerTaskInputs.AssertReferenceArguments([taskReference], parsedReferences, root);
            var policy = new OwnedTestWorkspacePathPolicy();
            var context = new CompilerArgumentInputContext(root, (paths, ct) => policy.ValidateAsync("native-alias", paths, ct), (path, ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct), (path, ct) => FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadDigestAsync, ct), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            var parsed = await context.ParseAsync(arguments, CancellationToken.None);
            Assert.HasCount(2, parsed.Arguments.MetadataReferences, "The isolated alias protocol explicitly disables implicit standard-library references.");
            var original = new CompilerArgumentReference(output, new MetadataReferenceProperties(MetadataImageKind.Assembly, ["global", "reviewed"], embed), CompilerArgumentReferenceKind.NativeProject);
            var resolved = await context.ResolveReferencesAsync(parsed, [original], CancellationToken.None);
            Assert.HasCount(1, resolved.References);
            Assert.AreEqual(original, resolved.References.Single());
            Assert.IsFalse(resolved.Parsing.Inputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, output)), "An independently matched source CompilationReference must not consume its output PE.");
            await File.WriteAllBytesAsync(output, [1]);
            await context.AssertCurrentAsync(resolved.Parsing, CancellationToken.None);
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native alias fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    [DataRow("missing")]
    [DataRow("excess")]
    [DataRow("case")]
    [DataRow("embed")]
    [DataRow("kind")]
    [DataRow("ambiguous")]
    public async Task NativeAliasOccurrences_RejectLossMutationAndAmbiguousOwnership(string change)
    {
        var root = Path.Combine(TestTempRoot.Current, "native-alias-refusal-" + Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var output = Path.Combine(root, "Unbuilt.dll");
            var arguments = new List<string>
            {
                "-reference:" + output,
                "-reference:reviewed=" + output
            };
            if (change == "missing")
                arguments.RemoveAt(1);
            if (change == "excess")
                arguments.Add(arguments[1]);
            if (change == "case")
                arguments[1] = "-reference:Reviewed=" + output;
            if (change == "embed")
                arguments[1] = "-link:reviewed=" + output;
            arguments.Insert(0, "-nostdlib+");
            var taskReference = new NativeCompilerTaskInputs.Reference(output, output, "", ["global", "reviewed"], false, change == "kind" ? MetadataImageKind.Module : MetadataImageKind.Assembly);
            if (change != "ambiguous")
                Assert.ThrowsExactly<PublicInvalidOperationException>(() => NativeCompilerTaskInputs.AssertReferenceArguments([taskReference], CompilerArgumentInputContext.TaskReferenceArguments(arguments, root), root));
            if (change != "kind")
            {
                var policy = new OwnedTestWorkspacePathPolicy();
                var context = new CompilerArgumentInputContext(root, (paths, ct) => policy.ValidateAsync("native-alias", paths, ct), (path, ct) => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct), (path, ct) => FileSnapshotCapture.ReadIfPresentAsync(path, PhysicalInputReader.ReadDigestAsync, ct), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
                var parsed = await context.ParseAsync([..arguments], CancellationToken.None);
                Assert.IsTrue(parsed.Arguments.MetadataReferences.All(reference => FileSystemPath.Comparer.Equals(reference.Reference, output)), "Only the controlled native operand may participate in this alias refusal fixture.");
                var original = new CompilerArgumentReference(output, new MetadataReferenceProperties(MetadataImageKind.Assembly, ["global", "reviewed"]), CompilerArgumentReferenceKind.NativeProject);
                ImmutableArray<CompilerArgumentReference> population = change == "ambiguous" ? [original, original] : [original];
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => context.ResolveReferencesAsync(parsed, population, CancellationToken.None));
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native alias fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }


    [TestMethod]
    [DataRow("missing")]
    [DataRow("denied")]
    [DataRow("unmatched")]
    [DataRow("alias-case")]
    [DataRow("interop")]
    [DataRow("module")]
    public async Task NativeProjectOutput_RequiresExactRoleWithoutConsumingItsPhysicalImage(string shape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentNativeProjectReferenceTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var output = Path.Combine(root, "Unbuilt.dll");
            var approvals = new HashSet<string>(FileSystemPath.Comparer);
            var outputReads = 0;
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                foreach (var path in paths)
                {
                    if (shape == "denied" && FileSystemPath.Comparer.Equals(path, output))
                        throw new UnauthorizedAccessException("Synthetic native output denial.");
                    approvals.Add(path);
                }

                return Task.CompletedTask;
            }

            Task<byte[]?> Read(string path, CancellationToken ct)
            {
                Assert.IsTrue(approvals.Contains(path));
                if (FileSystemPath.Comparer.Equals(path, output))
                    outputReads++;
                return FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            }

            var context = new CompilerArgumentInputContext(root, Authorize, Read, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            var parsed = await context.ParseAsync(["-nostdlib+", "-reference:FixtureAlias=" + output], CancellationToken.None);
            var properties = new MetadataReferenceProperties(shape == "module" ? MetadataImageKind.Module : MetadataImageKind.Assembly, shape == "module" ? [] : [shape == "alias-case" ? "fixturealias" : "FixtureAlias"], embedInteropTypes: shape == "interop");
            var population = shape == "unmatched" ? System.Collections.Immutable.ImmutableArray<CompilerArgumentReference>.Empty : [new CompilerArgumentReference(output, properties, CompilerArgumentReferenceKind.NativeProject)];
            if (shape == "denied")
            {
                await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => context.ResolveReferencesAsync(parsed, population, CancellationToken.None));
                Assert.AreEqual(0, outputReads);
            }
            else if (shape != "missing")
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => context.ResolveReferencesAsync(parsed, population, CancellationToken.None));
            else
            {
                var resolved = await context.ResolveReferencesAsync(parsed, population, CancellationToken.None);
                Assert.AreEqual(CompilerArgumentReferenceKind.NativeProject, resolved.References.Single().Kind);
                Assert.IsFalse(resolved.Parsing.Inputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, output)));
                await File.WriteAllBytesAsync(output, [1]);
                await resolved.Parsing.AssertCurrentAsync(Authorize, (digestPath, digestToken) => FileContentDigest.FromReaderAsync(digestPath, Read, digestToken), CancellationToken.None);
                Assert.AreEqual(0, outputReads, "The source CompilationReference never consumes a later output PE.");
            }
        }
        catch (Exception failure)
        {
            primary = failure;
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native project reference fixture cleanup failed.", primary, CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    [TestMethod]
    public void ReferenceIdentity_PreservesCaseSensitiveAliasesAndIndependentPathRules()
    {
        var path = Path.Combine(TestTempRoot.Current, "Assembly.dll");
        var original = NativeCompilerTaskInputs.ReferenceKey(path, ["Alias"], false, MetadataImageKind.Assembly, TestTempRoot.Current);
        var otherAlias = NativeCompilerTaskInputs.ReferenceKey(path, ["alias"], false, MetadataImageKind.Assembly, TestTempRoot.Current);
        Assert.IsFalse(NativeCompilerTaskInputs.ReferenceIdentity.Comparer.Equals(original, otherAlias));
        var otherPath = NativeCompilerTaskInputs.ReferenceKey(path.ToUpperInvariant(), ["Alias"], false, MetadataImageKind.Assembly, TestTempRoot.Current);
        Assert.AreEqual(FileSystemPath.Comparer.Equals(path, path.ToUpperInvariant()), NativeCompilerTaskInputs.ReferenceIdentity.Comparer.Equals(original, otherPath));
    }
}
