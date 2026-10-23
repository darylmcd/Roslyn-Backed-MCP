using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class NativeCompilerFinalClosureTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    [DataRow("denied-before-read", "nested-response")]
    [DataRow("current", "nested-response")]
    [DataRow("normalize", "nested-response")]
    [DataRow("selected", "nested-response")]
    [DataRow("current", "included-ruleset")]
    [DataRow("normalize", "included-ruleset")]
    [DataRow("selected", "included-ruleset")]
    [DataRow("current", "analyzer-directory-member")]
    [DataRow("normalize", "analyzer-directory-member")]
    [DataRow("selected", "analyzer-directory-member")]
    public async Task RetainedCompilerAuthority_FinalSourceGrantCannotChangeHiddenInputs(string boundary, string mutation)
    {
        if (boundary == "denied-before-read" && !OperatingSystem.IsWindows())
            Assert.Inconclusive("The denial-order control requires a native Windows sharing lock.");
        if (mutation == "analyzer-directory-member" && !OperatingSystem.IsWindows())
            Assert.Inconclusive("Analyzer-directory receipts belong to the Windows owned-image contract.");

        var ct = TestContext.CancellationToken;
        var root = Path.Combine(TestTempRoot.Current, nameof(NativeCompilerFinalClosureTests), Guid.NewGuid().ToString("N"));
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            var project = Path.Combine(root, "Closure.csproj");
            var source = Path.Combine(root, "Entry.cs");
            var nested = Path.Combine(root, "nested.rsp");
            var included = Path.Combine(root, "included.ruleset");
            var analyzers = Path.Combine(root, "analyzers");
            Directory.CreateDirectory(analyzers);
            var analyzerBytes = await File.ReadAllBytesAsync(typeof(Compilation).Assembly.Location, ct);
            await File.WriteAllBytesAsync(Path.Combine(analyzers, "OwnedCompilerLibrary.dll"), analyzerBytes, ct);
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}", ct);
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />", ct);
            await File.WriteAllTextAsync(source, "public sealed class Entry { }", ct);
            await File.WriteAllTextAsync(Path.Combine(root, "outer.rsp"), "@nested.rsp\n", ct);
            await File.WriteAllTextAsync(nested, "/ruleset:root.ruleset\n", ct);
            await File.WriteAllTextAsync(Path.Combine(root, "root.ruleset"),
                "<RuleSet Name=\"Root\" ToolsVersion=\"15.0\"><Include Path=\"included.ruleset\" Action=\"Default\" /></RuleSet>", ct);
            await File.WriteAllTextAsync(included, "<RuleSet Name=\"Included\" ToolsVersion=\"15.0\" />", ct);
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit>
                    <CompilerResponseFile>outer.rsp</CompilerResponseFile>
                  </PropertyGroup>
                  <ItemGroup>
                    <Analyzer Include="analyzers/OwnedCompilerLibrary.dll" />
                    <None Remove="*.rsp;*.ruleset;analyzers/**" />
                  </ItemGroup>
                </Project>
                """, ct);
            var hidden = mutation switch
            {
                "nested-response" => nested,
                "included-ruleset" => included,
                _ => analyzers
            };
            var policy = new ClosingPolicy(project, source, hidden, boundary, async token =>
            {
                if (mutation == "nested-response") await File.AppendAllTextAsync(nested, "# changed after hidden-input proof\n", token);
                else if (mutation == "included-ruleset") await File.AppendAllTextAsync(included, "<!-- changed after hidden-input proof -->", token);
                else await File.WriteAllBytesAsync(Path.Combine(analyzers, "NewMatchingLibrary.dll"), analyzerBytes, token);
            });
            var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var restore = await new DotnetCommandRunner().RunAsync(root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo"], properties), ct);
            Assert.IsTrue(restore.Succeeded, restore.StdErr + restore.StdOut);
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("final-compiler-closure", project, properties,
                policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, ct);
            Exception? loadFailure = null;
            FileStream? blocker = null;
            try
            {
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, loaded.Context.Availability);
                var issued = await loaded.Context.NormalizeAsync("final-compiler-closure", policy, ct);
                CollectionAssert.Contains(loaded.Context.FinalRestoreGrantPaths(issued.Solution).ToArray(), hidden,
                    "The mutated coordinate must have an actual retained parser/analyzer receipt.");
                var graph = EvaluatedBuildGraph.Source(loaded.Context.NativeSolution, CancellationToken.None);
                var nativeFiles = graph.Nodes.SelectMany(node => node.DocumentFiles.Concat(node.ArtifactFiles)
                    .Concat(node.PathEvidence.InputFiles)).ToHashSet(FileSystemPath.Comparer);
                Assert.IsFalse(nativeFiles.Contains(hidden), "The hidden receipt must not be covered by the final native file digest batch.");
                var completeGrants = new List<string[]>();
                policy.ObserveRetainedGrant = paths => completeGrants.Add(paths.ToArray());
                try
                {
                    await loaded.Context.AssertCurrentAsync(issued.Solution, "final-compiler-closure", policy, ct);
                }
                finally { policy.ObserveRetainedGrant = null; }
                Assert.HasCount(1, completeGrants, "The actual policy-only current boundary must issue one complete retained grant.");
                CollectionAssert.Contains(completeGrants[0], project);
                CollectionAssert.Contains(completeGrants[0], source);
                CollectionAssert.Contains(completeGrants[0], hidden);
                if (boundary == "denied-before-read")
                {
                    blocker = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
                    var nativeLock = await Assert.ThrowsExactlyAsync<IOException>(() => PhysicalInputReader.ReadAllBytesAsync(source, ct));
                    Assert.IsTrue((nativeLock.HResult & 0xffff) is 32 or 33,
                        "The actual retained source must independently refuse native reads before arming the denial.");
                }
                policy.Arm(completeGrants[0]);
                Exception? refused = null;
                try
                {
                    if (boundary == "normalize") _ = await loaded.Context.NormalizeAsync("final-compiler-closure", policy, ct);
                    else if (boundary == "selected") await loaded.Context.AuthorizeCurrentInputsAsync(issued.Solution,
                        "final-compiler-closure", policy, [project, source], ct);
                    else await loaded.Context.AssertCurrentAsync(issued.Solution, "final-compiler-closure", policy, ct);
                }
                catch (Exception failure) { refused = failure; }
                if (boundary == "denied-before-read")
                {
                    Assert.AreEqual(1, policy.Denials,
                        "A complete retained-input grant must reach the hidden refusal before reading the locked source. Observed=" + refused?.GetType().Name);
                    Assert.AreSame(policy.Refusal, refused);
                    Assert.AreEqual(0, policy.Mutations);
                    return;
                }
                Assert.AreEqual(1, policy.Mutations,
                    "The real source grant must mutate the hidden input only after its own receipt was observed.");
                if (mutation == "analyzer-directory-member")
                    CollectionAssert.AreEqual(analyzerBytes, await File.ReadAllBytesAsync(Path.Combine(analyzers, "NewMatchingLibrary.dll"), ct));
                else
                    StringAssert.Contains(await File.ReadAllTextAsync(hidden, ct), "changed after hidden-input proof");
                Assert.AreEqual(typeof(PublicInvalidOperationException), refused?.GetType(),
                    "A mutation after hidden-input validation must invalidate retained compiler authority.");
            }
            catch (Exception failure) { loadFailure = failure; throw; }
            finally { OwnedResourceCleanup.Dispose("Final compiler closure load cleanup failed.", loadFailure, blocker, loaded.Workspace, loaded.Lease); }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Final compiler closure fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class ClosingPolicy(string project, string source, string hidden, string boundary,
        Func<CancellationToken, Task> mutate) : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        private bool _hiddenObserved;
        private HashSet<string>? _completeGrant;
        internal Action<IReadOnlyList<string>>? ObserveRetainedGrant { get; set; }
        private bool Armed { get; set; }
        internal void Arm(IReadOnlyList<string> completeGrant)
        {
            Assert.IsTrue(_hiddenObserved, "The hidden input must have an actual policy-observed retained receipt before arming.");
            _completeGrant = completeGrant.ToHashSet(FileSystemPath.Comparer);
            Armed = true;
        }
        internal int Mutations { get; private set; }
        internal int Denials { get; private set; }
        internal UnauthorizedAccessException Refusal { get; } = new("The retained hidden compiler input is no longer authorized.");

        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _owned.ValidateAsync(workspaceId, paths, ct);
            ObserveRetainedGrant?.Invoke(paths);
            if (!Armed)
            {
                if (paths.Contains(hidden, FileSystemPath.Comparer)) _hiddenObserved = true;
                return;
            }
            if (Mutations != 0) return;
            if (boundary == "denied-before-read")
            {
                if (!paths.Contains(hidden, FileSystemPath.Comparer)) return;
                Assert.IsTrue(paths.Contains(project, FileSystemPath.Comparer));
                Assert.IsTrue(paths.Contains(source, FileSystemPath.Comparer));
                Denials++;
                throw Refusal;
            }
            if (boundary is "current" or "normalize")
            {
                if (!_completeGrant!.SetEquals(paths)) return;
                Mutations++;
                await mutate(ct);
                return;
            }
            if (paths.Contains(hidden, FileSystemPath.Comparer)) _hiddenObserved = true;
            if (!_hiddenObserved || paths.Contains(hidden, FileSystemPath.Comparer)
                || !paths.Contains(project, FileSystemPath.Comparer) || !paths.Contains(source, FileSystemPath.Comparer)) return;
            if (boundary == "selected" && paths.Count != 2) return;
            Mutations++;
            await mutate(ct);
        }
    }
}
