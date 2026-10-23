using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeOutputReceiptAuthorityTests
{
    [TestMethod]
    [DataRow("WriteLinesToFile", "valid")]
    [DataRow("WriteCodeFragment", "valid")]
    [DataRow("WriteLinesToFile", "origin")]
    [DataRow("WriteCodeFragment", "origin")]
    [DataRow("WriteLinesToFile", "assembly")]
    [DataRow("WriteCodeFragment", "assembly")]
    [DataRow("WriteLinesToFile", "parameter")]
    [DataRow("WriteCodeFragment", "parameter")]
    [DataRow("WriteLinesToFile", "scope")]
    [DataRow("WriteCodeFragment", "scope")]
    [DataRow("WriteLinesToFile", "protected")]
    [DataRow("WriteCodeFragment", "protected")]
    [DataRow("WriteLinesToFile", "denied")]
    [DataRow("WriteCodeFragment", "denied")]
    [DataRow("WriteLinesToFile", "assembly-hash")]
    [DataRow("WriteCodeFragment", "assembly-hash")]
    [DataRow("WriteLinesToFile", "cancelled")]
    [DataRow("WriteCodeFragment", "cancelled")]
    public async Task SuccessfulWriterFacts_RequireOriginalContextAndCurrentExactOutputAuthority(string writerTask, string shape)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeOutputReceiptAuthorityTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Native.csproj");
            await File.WriteAllTextAsync(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup><Target Name=\"OwnedReceiptTarget\" /></Project>");
            var graph = EvaluatedBuildGraph.FromRoots([project], OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>()),
                ImmutableDictionary<string, byte[]>.Empty, CancellationToken.None);
            var node = graph.Nodes.Single(owner => !owner.IsOuterBuild);
            var assemblies = await NativeOutputPreparation.CaptureWriterAssembliesAsync(node, CancellationToken.None);
            Assert.IsTrue(assemblies.TryGetValue(writerTask, out var implementation), "The actual independently admitted SDK writer must be available.");
            var output = Path.Combine(root, shape == "scope" ? "undeclared" : "obj", "Receipt.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await File.WriteAllTextAsync(output, "owned output");
            var receipt = new NativeOutputProducerObserver.Receipt(project, "OwnedReceiptTarget",
                shape == "origin" ? Path.Combine(root, "Uncaptured.targets") : project,
                writerTask, shape == "assembly" ? Path.Combine(root, "Forged.dll") : implementation!.FilePath,
                shape == "parameter" ? "UnrelatedInput" : writerTask == "WriteCodeFragment" ? "OutputFile" : "File", output,
                NativeTaskExecutionIdentity.EscapedContextIdentity(NativeCompilerTaskInputs.EffectiveGlobals(node.Globals, node.TargetFramework), node.TaskRegistrations.ToolsVersion, CancellationToken.None),
                "Microsoft.Build.Tasks." + writerTask);
            var protectedInputs = shape == "protected" ? ImmutableHashSet.Create(FileSystemPath.Comparer, output)
                : ImmutableHashSet.Create<string>(FileSystemPath.Comparer);
            if (shape == "assembly-hash")
                assemblies = assemblies.SetItem(writerTask, implementation! with { ContentHash = [0] });
            using var cancellation = new CancellationTokenSource();
            if (shape == "cancelled") cancellation.Cancel();
            Task<ImmutableArray<PhysicalInputIdentity>> Admit() => NativeOutputPreparation.AdmitWriterOutputsAsync("writer-receipt",
                graph, node, protectedInputs, assemblies, [receipt], shape == "denied" ? new DeniedPolicy() : new OwnedTestWorkspacePathPolicy(), cancellation.Token);
            if (shape == "denied") await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(Admit);
            else if (shape == "assembly-hash") await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(Admit);
            else if (shape == "cancelled") await Assert.ThrowsAsync<OperationCanceledException>(Admit);
            else
            {
                var result = await Admit();
                Assert.HasCount(shape == "valid" ? 1 : 0, result);
                if (shape == "valid")
                {
                    Assert.AreEqual(output, result.Single().FilePath);
                    await File.WriteAllTextAsync(output, "changed output");
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => PhysicalInputIdentity.AssertCurrentAsync(result, CancellationToken.None));
                }
            }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native output receipt fixture cleanup failed.", primary,
            CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class DeniedPolicy : IWorkspacePathPolicy
    {
        public Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
            => throw new UnauthorizedAccessException("Synthetic current output denial.");
    }
}
