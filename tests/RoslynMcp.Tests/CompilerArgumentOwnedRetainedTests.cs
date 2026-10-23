using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
// Ruleset expansion controls mutate the process environment inherited by native evaluations.
[DoNotParallelize]
public sealed class CompilerArgumentOwnedRetainedTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task OwnedReceipt_GrantsKnownTraversalBeforeReadingLockedEarlierInput()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("This denial-order control requires native Windows sharing.");
        await RunAsync(async fixture =>
        {
            var receipt = await fixture.ParseAsync();
            var blocker = fixture.Own(new FileStream(fixture.Response, FileMode.Open, FileAccess.Read, FileShare.None));
            var native = await Assert.ThrowsExactlyAsync<IOException>(() => PhysicalInputReader.ReadAllBytesAsync(fixture.Response, fixture.Token));
            Assert.IsTrue((native.HResult & 0xffff) is 32 or 33);
            var denial = new UnauthorizedAccessException("The retained traversal is no longer approved.");
            var denials = 0;
            fixture.Policy.Callback = paths =>
            {
                if (!paths.Contains(fixture.KnownChild, FileSystemPath.Comparer)) return;
                denials++;
                throw denial;
            };
            Exception? actual = null;
            try { await fixture.Parsing.AssertCurrentAsync(receipt, fixture.Token); }
            catch (Exception failure) { actual = failure; }
            Assert.AreEqual(1, denials, "The actual known traversal grant must precede the independently locked earlier response read. Actual=" + actual?.GetType().Name);
            Assert.AreSame(denial, actual);
            GC.KeepAlive(blocker);
        });
    }

    [TestMethod]
    public async Task OwnedReceipt_LastKnownTraversalGrantCannotMutateEarlierHiddenBytes()
    {
        await RunAsync(async fixture =>
        {
            var receipt = await fixture.ParseAsync();
            var mutations = 0;
            fixture.Policy.Callback = paths =>
            {
                if (mutations != 0 || !paths.Contains(fixture.KnownChild, FileSystemPath.Comparer)) return;
                mutations++;
                File.AppendAllText(fixture.Response, "\n/define:CHANGED_AFTER_GRANT\n");
            };
            Exception? actual = null;
            try { await fixture.Parsing.AssertCurrentAsync(receipt, fixture.Token); }
            catch (Exception failure) { actual = failure; }
            Assert.AreEqual(1, mutations, "The real retained traversal callback must execute before the verdict.");
            StringAssert.Contains(await File.ReadAllTextAsync(fixture.Response, fixture.Token), "CHANGED_AFTER_GRANT");
            Assert.AreEqual(typeof(PublicInvalidOperationException), actual?.GetType());
        });
    }

    [TestMethod]
    public async Task OwnedReceipt_NewTraversalRefusesWithoutAcquiringItsAuthority()
    {
        await RunAsync(async fixture =>
        {
            var receipt = await fixture.ParseAsync();
            var child = Path.Combine(fixture.Sources, "new-child");
            Directory.CreateDirectory(child);
            var sentinel = Path.Combine(child, "Sentinel.cs");
            var bytes = System.Text.Encoding.UTF8.GetBytes("public sealed class Sentinel { }");
            await File.WriteAllBytesAsync(sentinel, bytes, fixture.Token);
            var unknownGrants = 0;
            fixture.Policy.Callback = paths =>
            {
                if (!paths.Any(path => FileSystemPath.Comparer.Equals(path, child) || FileSystemPath.IsStrictDescendant(child, path))) return;
                unknownGrants++;
                throw new UnauthorizedAccessException("Unknown child cannot gain a retained grant.");
            };
            FileStream? blocker = null;
            if (OperatingSystem.IsWindows())
            {
                blocker = fixture.Own(new FileStream(sentinel, FileMode.Open, FileAccess.Read, FileShare.None));
                var native = await Assert.ThrowsExactlyAsync<IOException>(() => PhysicalInputReader.ReadAllBytesAsync(sentinel, fixture.Token));
                Assert.IsTrue((native.HResult & 0xffff) is 32 or 33);
            }
            Exception? actual = null;
            try { await fixture.Parsing.AssertCurrentAsync(receipt, fixture.Token); }
            catch (Exception failure) { actual = failure; }
            Assert.AreEqual(0, unknownGrants, "Frozen discovery must refuse before requesting authority for an unobserved child.");
            Assert.AreEqual(typeof(PublicInvalidOperationException), actual?.GetType());
            if (blocker is not null) fixture.Forget(blocker);
            blocker?.Dispose();
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(sentinel, fixture.Token));
        });
    }

    [TestMethod]
    public async Task OwnedReceipt_StableRevalidationRetainsOfficialArgumentsAndDirectoryState()
    {
        await RunAsync(async fixture =>
        {
            var receipt = await fixture.ParseAsync();
            var resolved = await fixture.Parsing.ResolveReferencesAsync(receipt, [], fixture.Token);
            Assert.HasCount(1, resolved.References);
            Assert.AreEqual(CompilerArgumentReferenceKind.MetadataFile, resolved.References[0].Kind);
            Assert.IsTrue(FileSystemPath.Comparer.Equals(Path.Combine(fixture.Root, "OwnedReference.dll"), resolved.References[0].Path));
            receipt = resolved.Parsing;
            var grants = new List<string[]>();
            fixture.Policy.Callback = paths => grants.Add(paths.ToArray());
            await fixture.Parsing.AssertCurrentAsync(receipt, fixture.Token);
            Assert.HasCount(1, grants, "One real complete retained grant precedes callback-free physical proof.");
            CollectionAssert.AreEquivalent(receipt.CurrentGrantPaths.ToArray(), grants[0]);
            CollectionAssert.Contains(grants[0], fixture.KnownChild);
            CollectionAssert.Contains(grants[0], fixture.Response);
            CollectionAssert.Contains(receipt.Arguments.ParseOptions.PreprocessorSymbolNames.ToArray(), "ORIGINAL");
            Assert.IsTrue(receipt.Arguments.SourceFiles.Any(source => FileSystemPath.Comparer.Equals(source.Path, fixture.HiddenSource)));
            Assert.AreEqual("/define:ORIGINAL\n", await File.ReadAllTextAsync(fixture.Response, fixture.Token));
        });
    }

    [TestMethod]
    public async Task GenericContext_RetainsSuppliedReaderAndExactPolicyFailure()
    {
        await RunAsync(async fixture =>
        {
            var reads = 0;
            var denied = false;
            var denial = new UnauthorizedAccessException("The supplied policy retains its exact refusal.");
            Task Authorize(IReadOnlyList<string> paths, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                if (denied) throw denial;
                return Task.CompletedTask;
            }
            Task<byte[]?> Read(string path, CancellationToken ct)
                => FileSnapshotCapture.ReadIfPresentAsync(path, File.ReadAllBytesAsync, ct);
            Task<FileContentDigest?> Digest(string path, CancellationToken ct)
            {
                reads++;
                return FileContentDigest.FromReaderAsync(path, Read, ct);
            }
            var generic = new CompilerArgumentInputContext(fixture.Root, Authorize, Read, Digest,
                CompilerArgumentInputContext.CreateStagePath, CompilerArgumentFixtureAuthority.NoNativeTraversalAsync);
            var receipt = await generic.ParseAsync(["@first.rsp"], fixture.Token);
            var before = reads;
            await generic.AssertCurrentAsync(receipt, fixture.Token);
            Assert.IsTrue(reads > before, "Generic supplied readers must remain observable during retained validation.");
            before = reads;
            denied = true;
            var actual = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => generic.AssertCurrentAsync(receipt, fixture.Token));
            Assert.AreSame(denial, actual);
            Assert.AreEqual(before, reads, "A real policy refusal precedes supplied-reader callbacks.");
        });
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OwnedReceipt_RulesetEnvironmentKeepsBeforeAndAfterGrantProof(bool changedBeforeGrant)
    {
        await RunAsync(async fixture =>
        {
            var variable = "ROSLYN_RETAINED_RULESET_" + Guid.NewGuid().ToString("N");
            var previous = Environment.GetEnvironmentVariable(variable);
            fixture.Own(OwnedResourceCleanup.FromAction(() => Environment.SetEnvironmentVariable(variable, previous)));
            Environment.SetEnvironmentVariable(variable, "first-included.ruleset");
            const string rules = "<RuleSet Name=\"Included\" ToolsVersion=\"15.0\" />";
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "first-included.ruleset"), rules, fixture.Token);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "second-included.ruleset"), rules, fixture.Token);
            await File.WriteAllTextAsync(Path.Combine(fixture.Root, "environment.ruleset"),
                "<RuleSet Name=\"Root\" ToolsVersion=\"15.0\"><Include Path=\"%" + variable + "%\" Action=\"Default\" /></RuleSet>", fixture.Token);
            await File.AppendAllTextAsync(fixture.Response, "/ruleset:environment.ruleset\n", fixture.Token);
            var receipt = await fixture.ParseAsync();
            CollectionAssert.Contains(receipt.CurrentGrantPaths.ToArray(), Path.Combine(fixture.Root, "first-included.ruleset"));
            if (changedBeforeGrant) Environment.SetEnvironmentVariable(variable, "second-included.ruleset");
            var grants = 0;
            var mutations = 0;
            fixture.Policy.Callback = paths =>
            {
                grants++;
                if (changedBeforeGrant) throw new UnauthorizedAccessException("An already-stale environment must refuse before this policy callback.");
                if (mutations != 0 || !paths.Contains(fixture.KnownChild, FileSystemPath.Comparer)) return;
                mutations++;
                Environment.SetEnvironmentVariable(variable, "second-included.ruleset");
            };
            Exception? actual = null;
            try { await fixture.Parsing.AssertCurrentAsync(receipt, fixture.Token); }
            catch (Exception failure) { actual = failure; }
            Assert.AreEqual(changedBeforeGrant ? 0 : 1, mutations);
            if (changedBeforeGrant) Assert.AreEqual(0, grants, "Existing expansion refusal precedes real policy denial.");
            Assert.AreEqual("second-included.ruleset", Environment.GetEnvironmentVariable(variable));
            Assert.AreEqual(typeof(PublicInvalidOperationException), actual?.GetType());
        });
    }

    [TestMethod]
    public async Task OwnedReceipt_PhysicalSharingFailureRetainsPublicClassification()
    {
        if (!OperatingSystem.IsWindows()) Assert.Inconclusive("This physical failure control requires native Windows sharing.");
        await RunAsync(async fixture =>
        {
            var receipt = await fixture.ParseAsync();
            fixture.Own(new FileStream(fixture.Response, FileMode.Open, FileAccess.Read, FileShare.None));
            var native = await Assert.ThrowsExactlyAsync<IOException>(() => PhysicalInputReader.ReadAllBytesAsync(fixture.Response, fixture.Token));
            Assert.IsTrue((native.HResult & 0xffff) is 32 or 33);
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => fixture.Parsing.AssertCurrentAsync(receipt, fixture.Token));
        });
    }

    [TestMethod]
    public async Task OwnedReceipt_CallerCancellationRetainsExactToken()
    {
        await RunAsync(async fixture =>
        {
            var receipt = await fixture.ParseAsync();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            OperationCanceledException? actual = null;
            try { await fixture.Parsing.AssertCurrentAsync(receipt, cancelled.Token); }
            catch (OperationCanceledException failure) { actual = failure; }
            Assert.IsNotNull(actual);
            Assert.AreEqual(cancelled.Token, actual.CancellationToken);
        });
    }

    private async Task RunAsync(Func<Fixture, Task> body)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(CompilerArgumentOwnedRetainedTests), Guid.NewGuid().ToString("N"));
        Fixture? fixture = null;
        CancellationTokenSource? budget = null;
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(root);
            budget = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            budget.CancelAfter(TimeSpan.FromMinutes(2));
            fixture = await Fixture.CreateAsync(root, budget.Token);
            await body(fixture);
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Owned retained parser fixture cleanup failed.", primary,
                fixture, budget, OwnedResourceCleanup.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class Policy : IWorkspacePathPolicy
    {
        private readonly OwnedTestWorkspacePathPolicy _owned = new();
        internal Action<IReadOnlyList<string>>? Callback { get; set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await _owned.ValidateAsync(workspaceId, paths, ct);
            Callback?.Invoke(paths);
        }
    }

    private sealed class Fixture(string root, CancellationToken token, Policy policy,
        MSBuildWorkspace workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease lease, CompilerArgumentInputContext parsing) : IDisposable
    {
        private readonly List<IDisposable> _resources = [];
        internal string Root => root;
        internal CancellationToken Token => token;
        internal Policy Policy => policy;
        internal CompilerArgumentInputContext Parsing => parsing;
        internal string Response => Path.Combine(root, "first.rsp");
        internal string Sources => Path.Combine(root, "sources");
        internal string KnownChild => Path.Combine(Sources, "known-child");
        internal string HiddenSource => Path.Combine(KnownChild, "Hidden.cs");
        internal T Own<T>(T value) where T : IDisposable { _resources.Add(value); return value; }
        internal void Forget(IDisposable value) => _resources.Remove(value);
        internal async Task<CompilerArgumentParseReceipt> ParseAsync()
        {
            var receipt = await parsing.ParseAsync(["-nostdlib+", "@first.rsp", "-recurse:sources/*.cs", "-reference:OwnedReference.dll"], token);
            Assert.HasCount(1, receipt.Arguments.MetadataReferences, "Only the controlled explicit reference may reach the resolver.");
            Assert.AreEqual("OwnedReference.dll", receipt.Arguments.MetadataReferences[0].Reference);
            CollectionAssert.Contains(receipt.CurrentGrantPaths.ToArray(), KnownChild);
            CollectionAssert.Contains(receipt.CurrentGrantPaths.ToArray(), Response);
            Assert.IsTrue(receipt.Inputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, Response)));
            Assert.IsFalse(receipt.Inputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, KnownChild)),
                "The old initial input union must omit this retained traversed directory; it is discovered only by the later directory recheck.");
            return receipt;
        }
        public void Dispose() => OwnedResourceCleanup.Dispose("Owned retained parser owners could not be released.", null,
            _resources.AsEnumerable().Reverse().Concat<IDisposable>([workspace, lease]).ToArray());

        internal static async Task<Fixture> CreateAsync(string root, CancellationToken token)
        {
            var project = Path.Combine(root, "Retained.csproj");
            await File.WriteAllTextAsync(Path.Combine(root, "global.json"), "{}", token);
            await File.WriteAllTextAsync(Path.Combine(root, "Directory.Build.props"), "<Project />", token);
            await File.WriteAllTextAsync(Path.Combine(root, "Entry.cs"), "public sealed class Entry { }", token);
            await File.WriteAllTextAsync(Path.Combine(root, "first.rsp"), "/define:ORIGINAL\n", token);
            await File.WriteAllBytesAsync(Path.Combine(root, "OwnedReference.dll"), await File.ReadAllBytesAsync(typeof(object).Assembly.Location, token), token);
            Directory.CreateDirectory(Path.Combine(root, "sources", "known-child"));
            await File.WriteAllTextAsync(Path.Combine(root, "sources", "known-child", "Hidden.cs"), "public sealed class Hidden { }", token);
            await File.WriteAllTextAsync(project, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit>
                    <EnableDefaultItems>false</EnableDefaultItems><EnableNETAnalyzers>false</EnableNETAnalyzers>
                  </PropertyGroup>
                  <ItemGroup><Compile Include="Entry.cs" /></ItemGroup>
                </Project>
                """, token);
            var properties = OwnedNativeLoadContext.Properties(project, new Dictionary<string, string>());
            var restored = await new DotnetCommandRunner().RunAsync(root, project,
                MsBuildCommandProperties.Append(["restore", project, "--nologo"], properties), token);
            Assert.IsTrue(restored.Succeeded, restored.StdErr + restored.StdOut);
            var policy = new Policy();
            var loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("owned-retained-parser", project, properties,
                policy, new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, token);
            Exception? primary = null;
            try
            {
                Assert.AreEqual(NativeCompilerContextAvailability.Certified, loaded.Context.Availability);
                var issued = await loaded.Context.NormalizeAsync("owned-retained-parser", policy, token);
                var nativeProject = issued.Solution.Projects.Single();
                var graph = EvaluatedBuildGraph.Source(loaded.Context.NativeSolution, token);
                var nativeFiles = graph.Nodes.SelectMany(node => node.DocumentFiles.Concat(node.ArtifactFiles).Concat(node.PathEvidence.InputFiles))
                    .ToHashSet(FileSystemPath.Comparer);
                Assert.IsFalse(nativeFiles.Contains(Path.Combine(root, "first.rsp")), "The retained response must be hidden from the ordinary native file epoch.");
                Assert.IsFalse(nativeFiles.Contains(Path.Combine(root, "sources", "known-child", "Hidden.cs")));
                var factory = typeof(NativeCompilerContextCertificate).GetMethod("CreateParsingContext", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?? throw new AssertFailedException("The actual product parsing-context factory is required.");
                var parsing = (CompilerArgumentInputContext)(factory.Invoke(loaded.Context, ["owned-retained-parser", nativeProject, policy])
                    ?? throw new AssertFailedException("The actual product parsing-context factory returned no context."));
                return new(root, token, policy, loaded.Workspace, loaded.Lease, parsing);
            }
            catch (Exception failure)
            {
                primary = failure;
                throw;
            }
            finally
            {
                if (primary is not null) OwnedResourceCleanup.DisposePreservingFailure("Owned native parser creation failed.", primary, loaded.Workspace, loaded.Lease);
            }
        }
    }
}
