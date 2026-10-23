using System.Collections.Immutable;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativePreparationFinalClosureTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task PreparationAuthority_LastGrantCannotChangeAnAlreadyCheckedAuthoredInput(bool preparedOutputGrant)
    {
        var ct = TestContext.CancellationToken;
        var root = Path.Combine(TestTempRoot.Current, nameof(NativePreparationFinalClosureTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Preparation.csproj");
            var first = Path.Combine(root, "A.cs");
            var second = Path.Combine(root, "B.cs");
            var outputOne = Path.Combine(root, "obj", "owned-one.txt");
            var outputTwo = Path.Combine(root, "obj", "owned-two.txt");
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}", ct);
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />", ct);
            await File.WriteAllTextAsync(first, "public sealed class A { }", ct);
            await File.WriteAllTextAsync(second, "public sealed class B { }", ct);
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup><Compile Include="A.cs" /><Compile Include="B.cs" /></ItemGroup>
                  <Target Name="WriteOwnedPreparationOutputs" BeforeTargets="CoreCompile">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/obj/owned-one.txt" Lines="owned one" Overwrite="true" />
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/obj/owned-two.txt" Lines="owned two" Overwrite="true" />
                  </Target>
                </Project>
                """, ct);
            var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var restored = await new DotnetCommandRunner().RunAsync(root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo"], properties), ct);
            Assert.IsTrue(restored.Succeeded, restored.StdErr + restored.StdOut);
            RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
            var policy = new PreparationPolicy(project, first, second, outputOne, outputTwo, preparedOutputGrant);
            Exception? refused = null;
            if (preparedOutputGrant)
            {
                var epoch = await NativeCompilerContextCertificate.Epoch.CaptureAsync("preparation-final-closure", project,
                    properties, policy, SupplementalPackageReadAuthority.None, ct);
                Assert.IsInstanceOfType<NativeCompilerContextCertificate.Epoch.CertifiedEpoch>(epoch);
                await epoch.PrepareOutputsAsync("preparation-final-closure", policy, ct);
                Assert.IsTrue(File.Exists(outputOne) && File.Exists(outputTwo), "Both actual native WriteLinesToFile tasks must execute.");
                Assert.IsTrue(policy.SawOutputBatch,
                    "Both native writer receipts must reach the same actual prepared-output authorization batch.");
                policy.Armed = true;
                try { await epoch.AssertPreparationCurrentAsync("preparation-final-closure", policy, ct); }
                catch (Exception failure) { refused = failure; }
            }
            else
            {
                var graph = EvaluatedBuildGraph.FromRoots([project], properties, ImmutableDictionary<string, byte[]>.Empty, ct);
                var nativeSources = graph.Nodes.SelectMany(node => node.DocumentFiles).ToHashSet(FileSystemPath.Comparer);
                Assert.IsTrue(nativeSources.Contains(first) && nativeSources.Contains(second), "Both ordered inputs must be real native Compile members.");
                ImmutableArray<PhysicalInputIdentity> baseline =
                    [await PhysicalInputIdentity.CaptureAsync(first, ct), await PhysicalInputIdentity.CaptureAsync(second, ct)];
                var protectedInputs = new[] { first, second }.ToImmutableHashSet(FileSystemPath.Comparer);
                policy.Armed = true;
                try { await NativeOutputPreparation.AssertInputsAsync(
                    "preparation-final-closure", graph, baseline, protectedInputs, protectedInputs, policy, ct); }
                catch (Exception failure) { refused = failure; }
            }
            Assert.AreEqual(1, policy.Mutations, "The precise later grant must exercise the authored-input mutation.");
            StringAssert.Contains(await File.ReadAllTextAsync(first, ct), "changed during final preparation grant");
            Assert.AreEqual(typeof(PublicInvalidOperationException), refused?.GetType(),
                "A preparation grant cannot invalidate an earlier authored-input proof.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Preparation closure fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class PreparationPolicy(string project, string first, string second, string outputOne, string outputTwo,
        bool preparedOutputGrant) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal bool Armed { get; set; }
        internal bool SawOutputBatch { get; private set; }
        internal int Mutations { get; private set; }

        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _owned.ValidateAsync(workspaceId, paths, ct);
            var outputBatch = paths.Contains(outputOne, FileSystemPath.Comparer) && paths.Contains(outputTwo, FileSystemPath.Comparer)
                && !paths.Contains(first, FileSystemPath.Comparer) && !paths.Contains(second, FileSystemPath.Comparer)
                && !paths.Contains(project, FileSystemPath.Comparer);
            SawOutputBatch |= outputBatch;
            var selectedBoundary = preparedOutputGrant ? outputBatch
                : paths.Count == 1 && FileSystemPath.Comparer.Equals(paths[0], second);
            if (!Armed || Mutations != 0 || !selectedBoundary) return;
            Mutations++;
            await File.AppendAllTextAsync(first, "\n// changed during final preparation grant\n", ct);
        }
    }
}
