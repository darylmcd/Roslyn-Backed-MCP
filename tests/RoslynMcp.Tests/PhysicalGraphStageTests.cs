using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;
using RoslynMcp.Roslyn.Services;
using RoslynMcp.Tests.Helpers;

namespace RoslynMcp.Tests;

[TestClass]
public sealed class PhysicalGraphStageTests
{
    [TestMethod]
    [DataRow("recursive-item")]
    [DataRow("native-exists")]
    public async Task StagePrivateNamespace_PreservesActualNativeSourceObserversDuringAndAfterCleanup(string shape)
    {
        Fixture? fixture = null;
        PhysicalGraphCandidate? current = null;
        PhysicalGraphStage? stage = null;
        NativeNamespaceObserverLoader? observer = null;
        Exception? primary = null;
        try
        {
            fixture = await Fixture.CreateAsync(false, false, false);
            await NativeNamespaceObservationFixture.AddAsync(fixture.ProjectB, fixture.Root, shape, "staging");
            var globals = OwnedNativeLoadContext.Properties(fixture.LoadedPath, new Dictionary<string, string>());
            var before = NativeNamespaceObservationFixture.AssertBaseline(fixture.ProjectB, fixture.Root, shape, "staging", globals);
            current = await PhysicalGraphCandidate.LoadAsync("namespace-observer", fixture.LoadedPath,
                globals, new OwnedTestWorkspacePathPolicy(), new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            var sourceBytes = await File.ReadAllBytesAsync(fixture.ProjectB);
            observer = new(fixture.ProjectB, globals);
            stage = await fixture.StageAsync(current.Solution, new OwnedTestWorkspacePathPolicy(), observer);
            stage.EnterReadAuthority();
            NativeNamespaceObservationFixture.AssertUnchanged(before, fixture.ProjectB, globals);
            if (shape == "recursive-item")
            {
                var candidate = stage.Expected.Solution.Projects.Single(project => project.Name == "B");
                Assert.AreSequenceEqual(before.AdditionalFiles.Select(stage.MapFromSource).Order(FileSystemPath.Comparer).ToArray(),
                    candidate.AdditionalDocuments.Select(document => document.FilePath!).Order(FileSystemPath.Comparer).ToArray());
            }
            var completedStage = stage;
            completedStage.Dispose();
            stage = null;
            NativeNamespaceObservationFixture.AssertUnchanged(before, fixture.ProjectB, globals);
            Assert.AreSequenceEqual(sourceBytes, await File.ReadAllBytesAsync(fixture.ProjectB));
            Assert.AreEqual<PhysicalDirectoryCleanupOutcome?>(OperatingSystem.IsWindows() ? PhysicalDirectoryCleanupOutcome.Removed : PhysicalDirectoryCleanupOutcome.Retained,
                completedStage.CleanupOutcome, "POSIX retained empty stage coordinates must preserve the same native source observations.");
        }
        catch (Exception failure)
        {
            primary = failure;
            if (observer is not null)
                foreach (var observation in observer.Observations)
                    Console.WriteLine("Native source observation at candidate load: flag=" + observation.Flag
                        + " inputs=" + string.Join(" | ", observation.AdditionalFiles));
            throw;
        }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native observer stage cleanup failed.", primary, stage, current, fixture); }
    }

    private sealed class NativeNamespaceObserverLoader(string sourceProject, IReadOnlyDictionary<string, string> globals) : WorkspaceSessionLoader
    {
        internal List<NativeNamespaceObservationFixture.Observation> Observations { get; } = [];
        public override Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease,
            NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission)> CreateAndOpenAsync(string workspaceId, string path,
            IDictionary<string, string>? globalProperties, IWorkspacePathPolicy pathPolicy,
            WorkspaceDiagnosticsSink diagnostics, ILogger logger, SupplementalPackageReadAuthority supplementalPackages, NativeSourceAdmission? sourceAdmission, CancellationToken ct)
        {
            Observations.Add(NativeNamespaceObservationFixture.Capture(sourceProject, globals));
            return base.CreateAndOpenAsync(workspaceId, path, globalProperties, pathPolicy, diagnostics, logger, supplementalPackages, sourceAdmission, ct);
        }
    }

    [TestMethod]
    [DataRow("normal")]
    [DataRow("root")]
    [DataRow("ancestor")]
    public async Task StageDisposal_RetainsIssuedDirectoryAuthorityAcrossOrdinaryReplacement(string replacement)
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary1 = null;
        try
        {
            var source = await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken);
            PhysicalGraphStage stage = await fixture.StageAsync(fixture.Source, new RecordingPolicy(), new WorkspaceSessionLoader());
            Exception? stageFailure = null;
            try
            {
                stage.EnterReadAuthority();
                var issued = IssuedStageDirectory(fixture.Root, stage.Expected.Solution.FilePath!);
                StageDirectoryReplacement change = ReplaceStageDirectory(issued, Path.Combine(fixture.Root, "retained-stage"), replacement);
                Exception? cleanup = null;
                try { stage.Dispose(); }
                catch (Exception failure) { cleanup = failure; }
                AssertStageCleanup(change, cleanup);
                Assert.AreSequenceEqual(source, await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken));
            }
            catch (Exception failure) { stageFailure = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage cleanup failed.", stageFailure, stage); }

        }
        catch (Exception failure) { cleanupPrimary1 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary1, fixture); }
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("ancestor")]
    public async Task StageFailureCleanup_RetainsIssuedDirectoryAuthorityAcrossOrdinaryReplacement(string replacement)
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary2 = null;
        try
        {
            var source = await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken);
            var loader = new ReplacingFailedCandidateLoader(fixture.Root, replacement);
            Exception? observed = null;
            try
            {
                PhysicalGraphStage unused = await fixture.StageAsync(fixture.Source, new RecordingPolicy(), loader);
                unused.Dispose();
            }
            catch (Exception failure) { observed = failure; }
            Assert.IsNotNull(observed);
            Assert.IsNotNull(loader.Replacement, "The failure must occur after the actual staged baseline load.");
            Assert.IsTrue(ContainsFailure(observed, loader.Failure), "Cleanup must retain the primary native candidate failure.");
            AssertStageCleanup(loader.Replacement, null, checkFailure: false);
            Assert.AreSequenceEqual(source, await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken));

        }
        catch (Exception failure) { cleanupPrimary2 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary2, fixture); }
    }

    [TestMethod]
    [DataRow("root")]
    [DataRow("ancestor")]
    public async Task StageRebuild_RetainsIssuedDirectoryAuthorityAcrossOrdinaryReplacement(string replacement)
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary4 = null;
        try
        {
            var source = await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken);
            var policy = new ReplacingRebuildPolicy(fixture.Root, fixture.ProjectA, replacement);
            Exception? observed = null;
            try
            {
                PhysicalGraphStage stage = await fixture.StageAsync(fixture.Source, policy, new WorkspaceSessionLoader());
                Exception? cleanupPrimary5 = null;
                try
                {
                    stage.EnterReadAuthority();

                }
                catch (Exception failure) { cleanupPrimary5 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary5, stage); }
            }
            catch (Exception failure) { observed = failure; }
            if (policy.Replacement is null && observed is not null)
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(observed).Throw();
            Assert.IsNotNull(policy.Replacement, "Replacement must occur at the complete approved desired-layout inventory, after baseline/discovery.");
            AssertStageCleanup(policy.Replacement, observed);
            Assert.AreSequenceEqual(source, await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken));

        }
        catch (Exception failure) { cleanupPrimary4 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary4, fixture); }
    }

    [TestMethod]
    public async Task StageRebuild_UsesFreshCoordinateForNativeExistsAndGlobs()
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary6 = null;
        try
        {
            var xml = System.Xml.Linq.XDocument.Load(fixture.ProjectB);
            Assert.IsNotNull(xml.Root);
            xml.Root.Add(new System.Xml.Linq.XElement("PropertyGroup",
                new System.Xml.Linq.XAttribute("Condition", "Exists('$(MSBuildProjectDirectory)/stale-structure')"),
                new System.Xml.Linq.XElement("DefineConstants", "STALE_STRUCTURE")),
                new System.Xml.Linq.XElement("ItemGroup", new System.Xml.Linq.XElement("AdditionalFiles",
                    new System.Xml.Linq.XAttribute("Include", "stale-structure/**/*.txt"))));
            await File.WriteAllTextAsync(fixture.ProjectB, xml.ToString(), TestContext.CancellationToken);
            var sourceBytes = await File.ReadAllBytesAsync(fixture.ProjectB, TestContext.CancellationToken);
            PhysicalGraphCandidate current = await PhysicalGraphCandidate.LoadAsync("fresh-stage", fixture.LoadedPath,
                OwnedNativeLoadContext.Properties(fixture.LoadedPath, new Dictionary<string, string>()),
                new OwnedTestWorkspacePathPolicy(), new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            Exception? cleanupPrimary7 = null;
            try
            {
                var policy = new StaleRebuildPolicy(fixture.Root, fixture.ProjectB);
                PhysicalGraphStage stage = await fixture.StageAsync(current.Solution, policy, new WorkspaceSessionLoader());
                Exception? stageFailure = null;
                try
                {
                    stage.EnterReadAuthority();
                    Assert.IsNotNull(policy.PriorIssued, "The native baseline and discovery must finish before stale private directories are introduced.");
                    Assert.IsNotNull(policy.StaleDirectory);
                    var rebuilt = IssuedStageDirectory(fixture.Root, stage.Expected.Solution.FilePath!);
                    Assert.AreNotEqual(policy.PriorIssued, rebuilt, "Rebuild must allocate a fresh exclusive coordinate, including when cleanup retains empty directories.");
                    Project b = stage.Expected.Solution.Projects.Single(project => project.Name == "B");
                    Assert.IsFalse(Directory.Exists(Path.Combine(Path.GetDirectoryName(b.FilePath!)!, "stale-structure")));
                    CollectionAssert.DoesNotContain(((CSharpParseOptions)b.ParseOptions!).PreprocessorSymbolNames.ToArray(), "STALE_STRUCTURE");
                    Assert.DoesNotContain(document => document.Name == "stale.txt", b.AdditionalDocuments);
                    Assert.AreEqual(!OperatingSystem.IsWindows(), Directory.Exists(policy.StaleDirectory),
                        "POSIX keeps the retired directory skeleton; Windows removes the owned directory namespace.");
                    Assert.IsFalse(File.Exists(Path.Combine(policy.StaleDirectory, "stale.txt")), "Both platforms reclaim original file contents.");
                    Assert.AreSequenceEqual(sourceBytes, await File.ReadAllBytesAsync(fixture.ProjectB, TestContext.CancellationToken));
                }
                catch (Exception failure) { stageFailure = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage cleanup failed.", stageFailure, stage); }
                Assert.AreEqual<PhysicalDirectoryCleanupOutcome?>(OperatingSystem.IsWindows() ? PhysicalDirectoryCleanupOutcome.Removed : PhysicalDirectoryCleanupOutcome.Retained,
                    stage.CleanupOutcome);

            }
            catch (Exception failure) { cleanupPrimary7 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary7, current); }

        }
        catch (Exception failure) { cleanupPrimary6 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary6, fixture); }
    }

    [TestMethod]
    public async Task OwnedCopyScope_NestedRevocationPreservesParentAndRefusesInheritedContinuation()
    {
        Fixture? fixture = null;
        PhysicalDirectoryScope.OwnedRoot? outer = null, inner = null;
        IDisposable? principal = null, outerScope = null, innerScope = null;
        Exception? primary = null;
        try
        {
            fixture = await Fixture.CreateAsync(false, false, false);
            var policy = new RecordingPolicy();
            var operation = OwnedGraphStageAuthority.Create("copy-scope", fixture.Source, policy, StageParent(fixture.Root));
            var layout = await PhysicalGraphLayout.CreateAsync("copy-scope", fixture.LoadedPath, fixture.Source, policy, CancellationToken.None);
            outer = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root + "-private", "outer"));
            inner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root + "-private", "inner"));
            principal = operation.Policy.Enter(operation);
            outerScope = operation.EnterOwnedCopy(outer, layout);
            innerScope = operation.EnterOwnedCopy(inner, layout);
            var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var inherited = Task.Run(async () =>
            {
                await resume.Task;
                await operation.Policy.ValidateAsync("copy-scope", [layout.MapTo(inner.Path, fixture.ProjectA)], CancellationToken.None);
            });
            innerScope.Dispose();
            innerScope = null;
            resume.SetResult();
            await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => inherited);
            await operation.Policy.ValidateAsync("copy-scope", [layout.MapTo(outer.Path, fixture.ProjectA)], CancellationToken.None);
            Assert.Contains(call => call.Paths.Contains(fixture.ProjectA, FileSystemPath.Comparer), policy.ApprovedCalls);
            Assert.IsFalse(policy.ApprovedCalls.Any(call => call.Paths.Any(path => FileSystemPath.IsStrictDescendant(outer.Path, path))),
                "Private copy authority must renew original coordinates rather than publish a copied prefix grant.");
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.Dispose("Owned copy scope fixture cleanup failed.", primary,
                outer, inner, innerScope, outerScope, principal, fixture);
        }
    }

    [TestMethod]
    public async Task OwnedCopyScope_RefusesDescendantAliasBeforeAnyOriginalTargetGrant()
    {
        Fixture? fixture = null;
        PhysicalDirectoryScope.OwnedRoot? owner = null;
        IDisposable? principal = null, copyScope = null;
        Exception? primary = null;
        try
        {
            fixture = await Fixture.CreateAsync(false, false, false);
            var policy = new RecordingPolicy();
            var operation = OwnedGraphStageAuthority.Create("copy-alias", fixture.Source, policy, StageParent(fixture.Root));
            var layout = await PhysicalGraphLayout.CreateAsync("copy-alias", fixture.LoadedPath, fixture.Source, policy, CancellationToken.None);
            owner = PhysicalDirectoryScope.CreateOwnedRoot(Path.Combine(fixture.Root + "-private", "alias-owner"));
            var alias = Path.Combine(owner.Path, "aliased.cs");
            try { File.CreateSymbolicLink(alias, fixture.ProjectA); }
            catch (Exception failure) when (OperatingSystem.IsWindows() && failure is IOException or UnauthorizedAccessException)
            { Assert.Inconclusive("The Windows account cannot create a symbolic link: " + failure.Message); }
            principal = operation.Policy.Enter(operation);
            copyScope = operation.EnterOwnedCopy(owner, layout);
            var count = policy.AttemptedCalls.Count;
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                operation.Policy.ValidateAsync("copy-alias", [alias], CancellationToken.None));
            Assert.AreEqual(count, policy.AttemptedCalls.Count, "A known private alias cannot request authority for its target.");
            Assert.IsTrue(File.Exists(fixture.ProjectA));
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            OwnedResourceCleanup.Dispose("Owned copy alias fixture cleanup failed.", primary, owner, copyScope, principal, fixture);
        }
    }

    private sealed class StaleRebuildPolicy(string root, string project) : IWorkspacePathPolicy
    {
        internal string? PriorIssued { get; private set; }
        internal string? StaleDirectory { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
            if (PriorIssued is not null || !paths.Contains(project, FileSystemPath.Comparer)) return;
            var old = CurrentIssuedStage(root);
            if (old is null || !File.Exists(Path.Combine(old, ".graph-discovery", "Stage.slnx"))) return;
            var copied = Directory.EnumerateFiles(old, "B.csproj", SearchOption.AllDirectories).Single();
            PriorIssued = old;
            StaleDirectory = Path.Combine(Path.GetDirectoryName(copied)!, "stale-structure", "deep");
            Directory.CreateDirectory(StaleDirectory);
            File.WriteAllText(Path.Combine(StaleDirectory, "stale.txt"), "private baseline bytes");
        }
    }

    private sealed record StageDirectoryReplacement(string Issued, string Original, string? ReplacementMarker);

    private static string StageParent(string root) => Path.Combine(root + "-private", "roslynmcp-staging");

    private static string? CurrentIssuedStage(string root)
    {
        var authority = ForkInvocationPathPolicy.ActiveAuthority;
        if (authority is null) return null;
        var field = typeof(ForkInvocationAuthority).GetField("_ownedCopies",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var local = field.GetValue(authority)!;
        var scope = local.GetType().GetProperty("Value")!.GetValue(local);
        if (scope is null) return null;
        var owner = (PhysicalDirectoryScope.OwnedRoot)scope.GetType().GetProperty("Owner",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(scope)!;
        return FileSystemPath.Comparer.Equals(Path.GetDirectoryName(owner.Path), StageParent(root)) ? owner.Path : null;
    }

    private static string IssuedStageDirectory(string fixtureRoot, string stagedPath)
    {
        var staging = Path.GetFullPath(StageParent(fixtureRoot));
        var current = Path.GetDirectoryName(Path.GetFullPath(stagedPath));
        while (current is not null && !FileSystemPath.Comparer.Equals(Path.GetDirectoryName(current), staging))
            current = Path.GetDirectoryName(current);
        Assert.IsNotNull(current, "The staged native entry must be inside the actual staging namespace.");
        Assert.AreEqual(staging, Path.GetDirectoryName(current), "Replacement must select the direct staging child.");
        Assert.IsTrue(Guid.TryParseExact(Path.GetFileName(current), "N", out _),
            "Replacement must select the issued GUID root, not a nested mapped content directory.");
        return current;
    }

    private static StageDirectoryReplacement ReplaceStageDirectory(string issued, string retained, string replacement)
    {
        File.WriteAllText(Path.Combine(issued, "owned-cleanup-input.txt"), "owned staged bytes");
        if (replacement == "normal") return new(issued, issued, null);
        var ancestor = replacement == "ancestor";
        try { Directory.Move(ancestor ? Path.GetDirectoryName(issued)! : issued, retained); }
        catch (IOException) when (OperatingSystem.IsWindows()) { return new(issued, issued, null); }
        var original = ancestor ? Path.Combine(retained, Path.GetFileName(issued)) : retained;
        Directory.CreateDirectory(issued);
        var marker = Path.Combine(issued, "unrelated-replacement.txt");
        File.WriteAllText(marker, "unrelated replacement bytes");
        Assert.AreEqual("owned staged bytes", File.ReadAllText(Path.Combine(original, "owned-cleanup-input.txt")));
        Assert.AreEqual((FileAttributes)0, File.GetAttributes(issued) & FileAttributes.ReparsePoint,
            "The replacement must be an ordinary directory, not a filesystem link.");
        return new(issued, original, marker);
    }

    private static void AssertStageCleanup(StageDirectoryReplacement change, Exception? cleanup, bool checkFailure = true)
    {
        if (change.ReplacementMarker is { } marker)
        {
            Assert.IsTrue(File.Exists(marker), "Cleanup must not delete an ordinary replacement of its issued name.");
            Assert.AreEqual("unrelated replacement bytes", File.ReadAllText(marker));
            Assert.IsFalse(File.Exists(Path.Combine(change.Original, "owned-cleanup-input.txt")),
                "Cleanup must act on its retained original directory identity.");
            if (checkFailure && cleanup is not null)
            {
                IEnumerable<Exception> failures = cleanup is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : new[] { cleanup }.AsEnumerable();
                Assert.IsTrue(failures.All(failure => failure is IOException or UnauthorizedAccessException or PublicInvalidOperationException));
            }
        }
        else
        {
            if (checkFailure) Assert.IsNull(cleanup);
            if (OperatingSystem.IsWindows())
            {
                Assert.IsFalse(Directory.Exists(change.Issued), "Windows must remove the original owned directory namespace.");
            }
            else
            {
                Assert.IsTrue(Directory.Exists(change.Issued), "POSIX must report retention of its directory skeleton.");
                Assert.IsEmpty(Directory.EnumerateFiles(change.Issued, "*", SearchOption.AllDirectories), "Retained skeletons must contain no original file bytes.");
            }
        }
    }

    private static bool ContainsFailure(Exception observed, Exception expected) => ReferenceEquals(observed, expected)
        || observed is AggregateException aggregate && aggregate.InnerExceptions.Any(failure => ContainsFailure(failure, expected))
        || observed.InnerException is { } inner && ContainsFailure(inner, expected);

    private sealed class ReplacingFailedCandidateLoader(string root, string replacement) : WorkspaceSessionLoader
    {
        private int _calls;
        internal Exception Failure { get; } = new PublicInvalidOperationException("Synthetic staged candidate failure.");
        internal StageDirectoryReplacement? Replacement { get; private set; }
        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease,
            NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission)> CreateAndOpenAsync(string workspaceId, string path,
            IDictionary<string, string>? globalProperties, IWorkspacePathPolicy pathPolicy,
            WorkspaceDiagnosticsSink diagnostics, ILogger logger, SupplementalPackageReadAuthority supplementalPackages, NativeSourceAdmission? sourceAdmission, CancellationToken ct)
        {
            if (++_calls == 2)
            {
                Replacement = ReplaceStageDirectory(IssuedStageDirectory(root, path), Path.Combine(root, "retained-failed-stage"), replacement);
                throw Failure;
            }
            return await base.CreateAndOpenAsync(workspaceId, path, globalProperties, pathPolicy, diagnostics, logger, supplementalPackages, sourceAdmission, ct);
        }
    }

    private sealed class ReplacingRebuildPolicy(string root, string project, string replacement) : IWorkspacePathPolicy
    {
        internal StageDirectoryReplacement? Replacement { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
            if (Replacement is not null || !paths.Contains(project, FileSystemPath.Comparer)) return;
            var issued = CurrentIssuedStage(root);
            if (issued is null || !File.Exists(Path.Combine(issued, ".graph-discovery", "Stage.slnx"))) return;
            Replacement = ReplaceStageDirectory(issued, Path.Combine(root, "retained-rebuilt-stage"), replacement);
        }
    }

    private static readonly string[] sourceArray = new[] { "ExecutionEntryCounts", "ExecutionNodeCounts", "ExecutionIdentityMismatchComponents", "ExecutionContextMismatchComponents" };

    [TestMethod]
    public async Task TextSolutionStage_PreservesNativeBackslashAndLiteralPercentMembers()
    {
        Fixture? fixture = null;
        PhysicalGraphStage? stage = null;
        Exception? primary = null;
        var diagnosticScope = new AsyncLocal<object?>();
        var diagnosticOwner = new object();
        var differences = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var compilerReceipts = new System.Collections.Concurrent.ConcurrentQueue<CompilerArgumentParseReceipt>();
        var phase = "source";
        var subscribed = false;
        EventHandler<System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs> observe = (_, args) =>
        {
            if (ReferenceEquals(diagnosticScope.Value, diagnosticOwner)
                && args.Exception is NativeSemanticContextMismatchException mismatch)
            {
                differences.Enqueue(phase + ": " + mismatch.Message);
                foreach (var receipt in mismatch.Parsing) compilerReceipts.Enqueue(receipt);
            }
        };
        try
        {
            diagnosticScope.Value = diagnosticOwner;
            AppDomain.CurrentDomain.FirstChanceException += observe;
            subscribed = true;
            fixture = await Fixture.CreateAsync(false, false, true);
            Assert.AreEqual(".sln", Path.GetExtension(fixture.LoadedPath));
            Assert.HasCount(2, fixture.Source.ProjectIds);
            Assert.IsTrue(fixture.Source.Projects.Any(project => FileSystemPath.Comparer.Equals(project.FilePath, fixture.ProjectB)),
                "The actual native source load must resolve the backslash member to the literal percent filename before staging.");
            var source = new[] { fixture.LoadedPath, fixture.ProjectA, fixture.ProjectB }.ToDictionary(path => path, File.ReadAllBytes);
            phase = "stage";
            stage = await fixture.StageAsync(fixture.Source, new RecordingPolicy(), new WorkspaceSessionLoader());
            stage.EnterReadAuthority();
            var projected = stage.Expected.Solution;
            Assert.HasCount(2, projected.ProjectIds);
            var issued = IssuedStageDirectory(fixture.Root, projected.FilePath!);
            foreach (var original in fixture.Source.Projects)
            {
                var staged = projected.Projects.Single(project => FileSystemPath.Comparer.Equals(stage.Expected.MapPath(project.FilePath!), original.FilePath));
                Assert.AreNotEqual(original.FilePath, staged.FilePath);
                Assert.IsTrue(File.Exists(staged.FilePath));
                Assert.IsTrue(FileSystemPath.IsStrictDescendant(issued, staged.FilePath!));
            }
            var a = projected.Projects.Single(project => FileSystemPath.Comparer.Equals(stage.Expected.MapPath(project.FilePath!), fixture.ProjectA));
            CollectionAssert.Contains(((CSharpParseOptions)a.ParseOptions!).PreprocessorSymbolNames.ToArray(), "ORIGINAL_A");
            var b = projected.Projects.Single(project => FileSystemPath.Comparer.Equals(stage.Expected.MapPath(project.FilePath!), fixture.ProjectB));
            Assert.AreEqual(NullableContextOptions.Enable, ((CSharpCompilationOptions)b.CompilationOptions!).NullableContextOptions);
            StringAssert.Contains(b.FilePath!, "%3B");
            foreach (var file in source) CollectionAssert.AreEqual(file.Value, await File.ReadAllBytesAsync(file.Key));
        }
        catch (Exception failure)
        {
            primary = failure;
            foreach (var difference in differences.Distinct(StringComparer.Ordinal))
                Console.WriteLine("Actual text solution native context: " + difference);
            if (fixture is not null)
            {
                foreach (var receipt in compilerReceipts)
                {
                    var parsed = receipt.Arguments;
                    var projects = fixture.Source.Projects.Where(project => FileSystemPath.Comparer.Equals(
                        Path.GetDirectoryName(project.FilePath), parsed.BaseDirectory)).ToArray();
                    if (projects.Length != 1 || projects[0].CompilationOptions is not CSharpCompilationOptions native) continue;
                    var project = projects[0];
                    if (project.AssemblyName != parsed.CompilationName)
                        Console.WriteLine("Owned text fixture AssemblyName: semantic=" + project.AssemblyName + "; compiler=" + parsed.CompilationName);
                    foreach (var property in new[] { "ModuleName", "MainTypeName", "ScriptClassName", "OutputKind", "OptimizationLevel",
                        "CheckOverflow", "AllowUnsafe", "Platform", "NullableContextOptions", "WarningLevel", "GeneralDiagnosticOption",
                        "ConcurrentBuild", "Deterministic", "MetadataImportOptions", "ReportSuppressedDiagnostics", "PublicSign", "DelaySign" })
                    {
                        var member = typeof(CSharpCompilationOptions).GetProperty(property);
                        if (member is null) continue;
                        var semanticValue = member.GetValue(native);
                        var compilerValue = member.GetValue(parsed.CompilationOptions);
                        if (!Equals(semanticValue, compilerValue))
                            Console.WriteLine("Owned text fixture " + property + ": semantic=" + semanticValue + "; compiler=" + compilerValue);
                    }
                }
            }
            throw;
        }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Text solution stage fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => { if (subscribed) AppDomain.CurrentDomain.FirstChanceException -= observe; }),
                CleanupFailureCollector.FromAction(() => diagnosticScope.Value = null),
                CleanupFailureCollector.FromAction(() => stage?.Dispose()), CleanupFailureCollector.FromAction(() => fixture?.Dispose()));
        }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OwnedPreOverlay_ReevaluatesNativeGraphAndPreservesUnrelatedCurrentConfiguration(bool absoluteMembership)
    {
        Fixture fixture = await Fixture.CreateAsync(absoluteMembership, false, false);
        Exception? cleanupPrimary8 = null;
        try
        {
            var sourceA = File.ReadAllBytes(fixture.ProjectA);
            var sourceB = File.ReadAllBytes(fixture.ProjectB);
            var policy = new RecordingPolicy();
            string stagedPath;
            {
                PhysicalGraphStage stage = await fixture.StageAsync(fixture.Source, policy, new WorkspaceSessionLoader());
                Exception? cleanupPrimary9 = null;
                try
                {
                    stage.EnterReadAuthority();
                    Solution expected = stage.Expected.Solution;
                    try { stage.Expected.ExecutionIdentity.AssertEquivalent(fixture.Source, CancellationToken.None); }
                    catch (PhysicalGraphMismatchException failure)
                    {
                        IEnumerable<string> safe = sourceArray.Where(key => failure.Data.Contains(key)).Select(key => key + "=" + failure.Data[key]);
                        throw new AssertFailedException("Staged execution identity mismatch: " + string.Join(";", safe));
                    }
                    Project a = expected.Projects.Single(project => project.Name == "A");
                    Project b = expected.Projects.Single(project => project.Name == "B");
                    CollectionAssert.Contains(((CSharpParseOptions)a.ParseOptions!).PreprocessorSymbolNames.ToArray(), "ORIGINAL_A");
                    CollectionAssert.DoesNotContain(((CSharpParseOptions)a.ParseOptions!).PreprocessorSymbolNames.ToArray(), "APPLIED_A");
                    Assert.AreEqual(NullableContextOptions.Enable, ((CSharpCompilationOptions)b.CompilationOptions!).NullableContextOptions);
                    Assert.IsTrue((await b.Documents.Single(document => document.Name == "B.cs").GetTextAsync(TestContext.CancellationToken)).ToString().Contains("CurrentB", StringComparison.Ordinal));
                    Assert.AreEqual(fixture.ProjectA, stage.Expected.MapPath(a.FilePath!));
                    stagedPath = expected.FilePath!;
                    Assert.Contains(call => call.Paths.Contains(fixture.ProjectA, FileSystemPath.Comparer), policy.ApprovedCalls,
                        "The private destination must renew its mapped original source grant.");
                    Assert.AreEqual(fixture.ProjectA, stage.Expected.MapPath(a.FilePath!));
                    Assert.AreSequenceEqual(sourceA, File.ReadAllBytes(fixture.ProjectA));
                    Assert.AreSequenceEqual(sourceB, File.ReadAllBytes(fixture.ProjectB));
                }
                catch (Exception failure) { cleanupPrimary9 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary9, stage); }
            }
            Assert.IsFalse(File.Exists(stagedPath), "Disposing the authority must release its isolated staged tree.");
            Assert.AreSequenceEqual(sourceA, File.ReadAllBytes(fixture.ProjectA));
            Assert.AreSequenceEqual(sourceB, File.ReadAllBytes(fixture.ProjectB));

        }
        catch (Exception failure) { cleanupPrimary8 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary8, fixture); }
    }

    [TestMethod]
    public async Task PolicyRejection_HappensBeforeAnyStagingDirectoryOrSourceWrite()
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary10 = null;
        try
        {
            var source = File.ReadAllBytes(fixture.ProjectA);
            var policy = new RecordingPolicy { Reject = true };
            await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() => fixture.StageAsync(fixture.Source, policy, new WorkspaceSessionLoader()));
            Assert.Contains(call => call.Paths.Count > 0, policy.AttemptedCalls);
            Assert.IsEmpty(policy.ApprovedCalls);
            Assert.IsFalse(Directory.Exists(StageParent(fixture.Root)));
            Assert.AreSequenceEqual(source, File.ReadAllBytes(fixture.ProjectA));

        }
        catch (Exception failure) { cleanupPrimary10 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary10, fixture); }
    }

    [TestMethod]
    public async Task BaselineDocumentMismatch_RefusesBeforeOwnedOverlayAndCleansCandidate()
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary11 = null;
        try
        {
            Document document = fixture.Source.Projects.Single(project => project.Name == "B").Documents.Single(item => item.Name == "B.cs");
            Solution inconsistent = fixture.Source.WithDocumentText(document.Id, SourceText.From("class InMemoryOnly {}"));
            var source = File.ReadAllBytes(fixture.ProjectA);
            await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => fixture.StageAsync(inconsistent, new RecordingPolicy(), new WorkspaceSessionLoader()));
            var staging = StageParent(fixture.Root);
            AssertStageContentsReclaimed(staging);
            Assert.AreSequenceEqual(source, File.ReadAllBytes(fixture.ProjectA));

        }
        catch (Exception failure) { cleanupPrimary11 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary11, fixture); }
    }

    [TestMethod]
    public async Task CandidateLoadCancellation_PreservesTokenAndCleansOnlyItsStage()
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary12 = null;
        try
        {
            using var cancellation = new CancellationTokenSource();
            var failure = new OperationCanceledException(cancellation.Token);
            var loader = new CancelCandidateLoader(failure);
            var before = File.ReadAllBytes(fixture.ProjectA);
            OperationCanceledException observed = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => fixture.StageAsync(fixture.Source, new RecordingPolicy(), loader));
            Assert.AreSame(failure, observed);
            Assert.AreEqual(cancellation.Token, observed.CancellationToken);
            Assert.AreEqual(2, loader.Calls);
            var staging = StageParent(fixture.Root);
            AssertStageContentsReclaimed(staging);
            Assert.AreSequenceEqual(before, File.ReadAllBytes(fixture.ProjectA));

        }
        catch (Exception failure) { cleanupPrimary12 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary12, fixture); }
    }

    [TestMethod]
    [DataRow("OutputPath", "changed-output/")]
    [DataRow("IsTestProject", "true")]
    public async Task PostStageExecutionMutation_RefusesActualNativeCandidate(string property, string value)
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary13 = null;
        try
        {
            PhysicalGraphStage stage = await fixture.StageAsync(fixture.Source, new RecordingPolicy(), new WorkspaceSessionLoader());
            Exception? cleanupPrimary14 = null;
            try
            {
                stage.EnterReadAuthority();
                var restored = File.ReadAllText(fixture.ProjectA).Replace("APPLIED_A", "ORIGINAL_A", StringComparison.Ordinal);
                await File.WriteAllTextAsync(fixture.ProjectA, restored, TestContext.CancellationToken);
                {
                    PhysicalGraphCandidate valid = await PhysicalGraphCandidate.LoadAsync("stage-control", fixture.LoadedPath, OwnedNativeLoadContext.Properties(fixture.LoadedPath, new Dictionary<string, string>()),
                    new RecordingPolicy(), new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
                    Exception? cleanupPrimary15 = null;
                    try
                    {
                        stage.Expected.ExecutionIdentity.AssertEquivalent(valid.Solution, CancellationToken.None);
                    }
                    catch (Exception failure) { cleanupPrimary15 = failure; throw; }
                    finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary15, valid); }
                }

                await File.WriteAllTextAsync(fixture.ProjectA, restored.Replace("</PropertyGroup>",
                    $"<{property}>{value}</{property}></PropertyGroup>", StringComparison.Ordinal), TestContext.CancellationToken);
                PhysicalGraphCandidate changed = await PhysicalGraphCandidate.LoadAsync("stage-control", fixture.LoadedPath, OwnedNativeLoadContext.Properties(fixture.LoadedPath, new Dictionary<string, string>()),
                    new RecordingPolicy(), new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
                Exception? cleanupPrimary16 = null;
                try
                {
                    Assert.ThrowsExactly<PhysicalGraphMismatchException>(() => stage.Expected.ExecutionIdentity.AssertEquivalent(changed.Solution, CancellationToken.None));

                }
                catch (Exception failure) { cleanupPrimary16 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary16, changed); }

            }
            catch (Exception failure) { cleanupPrimary14 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary14, stage); }

        }
        catch (Exception failure) { cleanupPrimary13 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary13, fixture); }
    }

    [TestMethod]
    public async Task PostStageRuntimeOnlyAuthoredMutation_RefusesBeforePublicationEvenWhenEvaluatedConsumerIdentityMatches()
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary17 = null;
        try
        {
            PhysicalGraphStage stage = await fixture.StageAsync(fixture.Source, new RecordingPolicy(), new WorkspaceSessionLoader());
            Exception? cleanupPrimary18 = null;
            try
            {
                stage.EnterReadAuthority();
                await File.WriteAllTextAsync(fixture.ProjectA, File.ReadAllText(fixture.ProjectA).Replace("APPLIED_A", "ORIGINAL_A", StringComparison.Ordinal), TestContext.CancellationToken);
                await stage.Expected.AssertBuildInputsAsync("stage-input-control", new RecordingPolicy(), CancellationToken.None);
                var currentB = File.ReadAllText(fixture.ProjectB);
                await File.WriteAllTextAsync(fixture.ProjectB, currentB.Replace("</PropertyGroup>",
                    "<RuntimeOnlyAuthoredSetting>changed-after-stage</RuntimeOnlyAuthoredSetting></PropertyGroup>", StringComparison.Ordinal), TestContext.CancellationToken);
                PhysicalGraphCandidate changed = await PhysicalGraphCandidate.LoadAsync("stage-control", fixture.LoadedPath, OwnedNativeLoadContext.Properties(fixture.LoadedPath, new Dictionary<string, string>()),
                    new RecordingPolicy(), new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
                Exception? cleanupPrimary19 = null;
                try
                {
                    stage.Expected.ExecutionIdentity.AssertEquivalent(changed.Solution, CancellationToken.None);
                    await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => stage.Expected.AssertBuildInputsAsync(
                        "stage-input-control", new RecordingPolicy(), CancellationToken.None));

                }
                catch (Exception failure) { cleanupPrimary19 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary19, changed); }

            }
            catch (Exception failure) { cleanupPrimary18 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary18, stage); }

        }
        catch (Exception failure) { cleanupPrimary17 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary17, fixture); }
    }

    [TestMethod]
    [DataRow("project.assets.json")]
    [DataRow("B.csproj.nuget.dgspec.json")]
    public async Task CopiedRestoreInputAbsence_IsBoundUntilNativePublication(string artifactName)
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary20 = null;
        try
        {
            var artifact = Path.Combine(Path.GetDirectoryName(fixture.ProjectB)!, "obj", artifactName);
            Assert.IsFalse(File.Exists(artifact), "The fixture must establish a genuinely missing restore input.");
            PhysicalGraphStage stage = await fixture.StageAsync(fixture.Source, new RecordingPolicy(), new WorkspaceSessionLoader());
            Exception? cleanupPrimary21 = null;
            try
            {
                stage.EnterReadAuthority();
                Assert.IsTrue(stage.Expected.BuildInputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, artifact) && !input.Exists));
                await File.WriteAllTextAsync(fixture.ProjectA, File.ReadAllText(fixture.ProjectA).Replace("APPLIED_A", "ORIGINAL_A", StringComparison.Ordinal), TestContext.CancellationToken);
                await stage.Expected.AssertBuildInputsAsync("stage-input-control", new RecordingPolicy(), CancellationToken.None);
                Directory.CreateDirectory(Path.GetDirectoryName(artifact)!);
                await File.WriteAllTextAsync(artifact, "{}", TestContext.CancellationToken);
                await Assert.ThrowsExactlyAsync<PublicInvalidOperationException>(() => stage.Expected.AssertBuildInputsAsync(
                    "stage-input-control", new RecordingPolicy(), CancellationToken.None));

            }
            catch (Exception failure) { cleanupPrimary21 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary21, stage); }

        }
        catch (Exception failure) { cleanupPrimary20 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary20, fixture); }
    }

    [TestMethod]
    public void SlnxTransport_PreservesExplicitAndDerivedIdsAndRelocatesDependencyReferences()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        var root = Path.Combine(TestTempRoot.Current, "slnx-identity-source");
        var destination = Path.Combine(TestTempRoot.Current, "slnx-identity-stage");
        var a = Path.Combine(root, "A.csproj");
        var b = Path.Combine(root, "B.csproj");
        const string explicitId = "11111111-2222-3333-4444-555555555555";
        var original = new System.Xml.Linq.XDocument(new System.Xml.Linq.XElement("Solution",
            new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Path", a), new System.Xml.Linq.XAttribute("Id", explicitId)),
            new System.Xml.Linq.XElement("Project", new System.Xml.Linq.XAttribute("Path", b),
                new System.Xml.Linq.XElement("BuildDependency", new System.Xml.Linq.XAttribute("Project", a)))));
        var bytes = Encoding.UTF8.GetBytes(original.ToString());
        var copy = bytes.ToArray();
        var relocated = SolutionFileMutationPlanner.RelocateDefinition(Path.Combine(root, "Graph.slnx"), bytes,
            path => Path.Combine(destination, Path.GetRelativePath(root, path)),
            path => Path.Combine(destination, Path.GetRelativePath(root, path)), BuildPathRelocation.KnownPathProperties, []);
        var result = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(relocated));
        XElement[] projects = [.. result.Root!.Elements("Project")];
        Assert.AreEqual(explicitId, projects[0].Attribute("Id")!.Value);
        Assert.IsTrue(Guid.TryParse(projects[1].Attribute("Id")?.Value, out Guid derived) && derived != Guid.Empty);
        Assert.AreEqual(Path.Combine(destination, "A.csproj"), projects[0].Attribute("Path")!.Value);
        Assert.AreEqual(Path.Combine(destination, "B.csproj"), projects[1].Attribute("Path")!.Value);
        Assert.AreEqual(Path.Combine(destination, "A.csproj"), projects[1].Element("BuildDependency")!.Attribute("Project")!.Value);
        Assert.AreSequenceEqual(copy, bytes);
    }

    [TestMethod]
    public void SolutionConfigurationTransport_NormalizesOnlyOfficialPathSlotsAndPreservesAllOtherFields()
    {
        RoslynMcp.Roslyn.MsBuildInitializer.EnsureInitialized();
        var source = Path.Combine(TestTempRoot.Current, "context-source", "A.csproj");
        var staged = Path.Combine(TestTempRoot.Current, "context-stage", "A.csproj");
        string Configuration(string path, string selected, string build, string extra) =>
            new System.Xml.Linq.XElement("SolutionConfiguration",
                new System.Xml.Linq.XElement("ProjectConfiguration",
                    new System.Xml.Linq.XAttribute("Project", "{00000000-0000-0000-0000-000000000001}"),
                    new System.Xml.Linq.XAttribute("AbsolutePath", path),
                    new System.Xml.Linq.XAttribute("BuildProjectInSolution", build),
                    new System.Xml.Linq.XAttribute("PreservedCustomField", extra), selected)).ToString();
        string Normalize(string xml) => BuildPathRelocation.RelocateSolutionConfiguration(xml,
            path => FileSystemPath.Comparer.Equals(path, staged) ? source : path);
        var expected = Normalize(Configuration(source, "Debug|AnyCPU", "true", "opaque-value"));
        Assert.AreEqual(expected, Normalize(Configuration(staged, "Debug|AnyCPU", "true", "opaque-value")));
        Assert.AreNotEqual(expected, Normalize(Configuration(staged, "Release|AnyCPU", "true", "opaque-value")));
        Assert.AreNotEqual(expected, Normalize(Configuration(staged, "Debug|AnyCPU", "false", "opaque-value")));
        Assert.AreNotEqual(expected, Normalize(Configuration(staged, "Debug|AnyCPU", "true", "changed")));
        const string malformed = "<SolutionConfiguration synthetic-private-context";
        Assert.AreEqual(malformed, Normalize(malformed));
    }

    [TestMethod]
    public async Task ExecutionIdentity_TracksEveryCapturedGlobalWithoutPrintingPrivateValues()
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary22 = null;
        try
        {
            const string sentinel = "synthetic-private-graph-context%3B-7c14";
            Dictionary<string, string> properties = OwnedNativeLoadContext.Properties(fixture.LoadedPath, new Dictionary<string, string> { ["UnknownExecutionContext"] = sentinel });
            PhysicalGraphCandidate expected = await PhysicalGraphCandidate.LoadAsync("global-control", fixture.LoadedPath, properties,
                new RecordingPolicy(), new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            Exception? cleanupPrimary23 = null;
            try
            {
                var identity = EvaluatedExecutionIdentity.Capture(expected.Solution, static path => path, BuildPathRelocation.KnownPathProperties, CancellationToken.None);
                identity.AssertEquivalent(expected.Solution, CancellationToken.None);
                properties["UnknownExecutionContext"] = sentinel + "changed";
                PhysicalGraphCandidate changed = await PhysicalGraphCandidate.LoadAsync("global-control", fixture.LoadedPath, properties,
                    new RecordingPolicy(), new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
                Exception? cleanupPrimary24 = null;
                try
                {
                    PhysicalGraphMismatchException failure = Assert.ThrowsExactly<PhysicalGraphMismatchException>(() => identity.AssertEquivalent(changed.Solution, CancellationToken.None));
                    Assert.IsFalse(failure.ToString().Contains(sentinel, StringComparison.Ordinal));
                    Assert.IsFalse(identity.ToString().Contains(sentinel, StringComparison.Ordinal));

                }
                catch (Exception failure) { cleanupPrimary24 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary24, changed); }

            }
            catch (Exception failure) { cleanupPrimary23 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary23, expected); }

        }
        catch (Exception failure) { cleanupPrimary22 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary22, fixture); }
    }

    [TestMethod]
    public async Task NativeOutputPath_IsPartOfFullPhysicalGraphComparison()
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary25 = null;
        try
        {
            Project project = fixture.Source.Projects.Single(item => item.Name == "A");
            Assert.IsNotNull(project.OutputFilePath);
            Solution changed = fixture.Source.WithProjectOutputFilePath(project.Id, Path.Combine(fixture.Root, "different-output", "A.dll"));
            await Assert.ThrowsExactlyAsync<PhysicalGraphMismatchException>(() => SolutionFileMutationPlanner.AssertEquivalentAsync(fixture.Source,
                changed, static path => path, static path => path, true, BuildPathRelocation.KnownPathProperties, CancellationToken.None));

        }
        catch (Exception failure) { cleanupPrimary25 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary25, fixture); }
    }

    [TestMethod]
    public async Task UnresolvedAnalyzer_NativeStageUsesSharedProjectionWithoutWritingOrDroppingWarningPolicy()
    {
        Fixture fixture = await Fixture.CreateAsync(false, true, false);
        Exception? cleanupPrimary26 = null;
        try
        {
            Assert.HasCount(1, fixture.AnalyzerWarnings);
            Assert.AreEqual("WORKSPACE_UNRESOLVED_ANALYZER", fixture.AnalyzerWarnings[0].Id);
            Assert.IsEmpty(fixture.Source.Projects.SelectMany(project => project.AnalyzerReferences)
                .OfType<Microsoft.CodeAnalysis.Diagnostics.UnresolvedAnalyzerReference>());
            var before = File.ReadAllBytes(fixture.ProjectA);
            PhysicalGraphStage stage = await fixture.StageAsync(fixture.Source, new RecordingPolicy(), new WorkspaceSessionLoader());
            Exception? cleanupPrimary27 = null;
            try
            {
                stage.EnterReadAuthority();
                Assert.IsEmpty(stage.Expected.Solution.Projects.SelectMany(project => project.AnalyzerReferences)
                    .OfType<Microsoft.CodeAnalysis.Diagnostics.UnresolvedAnalyzerReference>());
                Assert.AreSequenceEqual(before, File.ReadAllBytes(fixture.ProjectA));
                Assert.IsTrue(File.ReadAllText(fixture.ProjectA).Contains("MissingAnalyzer.dll", StringComparison.Ordinal));

            }
            catch (Exception failure) { cleanupPrimary27 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary27, stage); }

        }
        catch (Exception failure) { cleanupPrimary26 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary26, fixture); }
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OwnedImportReactivation_UsesCurrentUnownedProjectAndApprovesExpansionBeforeCopy(bool denyExpansion)
    {
        var root = Path.Combine(TestTempRoot.Current, nameof(PhysicalGraphStageTests), Guid.NewGuid().ToString("N"));
        var entry = Path.Combine(root, "Entry");
        var a = Path.Combine(entry, "A", "A.csproj");
        var shared = Path.Combine(root, "Shared", "Old.props");
        var y = Path.Combine(root, "NewY", "Y.csproj");
        var yCode = Path.Combine(root, "NewY", "Y.cs");
        var solutionPath = Path.Combine(entry, "Recovery.slnx");
        var originalA = Encoding.UTF8.GetBytes("""
            <Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit></PropertyGroup><Import Project="../../Shared/Old.props" /></Project>
            """);
        Exception? primary = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(a)!);
            Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
            Directory.CreateDirectory(Path.GetDirectoryName(y)!);
            await File.WriteAllBytesAsync(a, originalA, TestContext.CancellationToken);
            await File.WriteAllTextAsync(shared, "<Project><PropertyGroup><DefineConstants>PRIOR_SHARED</DefineConstants></PropertyGroup></Project>", TestContext.CancellationToken);
            await File.WriteAllTextAsync(y, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>""", TestContext.CancellationToken);
            await File.WriteAllTextAsync(yCode, "public class CurrentY { public string? Value { get; set; } }", TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(entry, "A", "A.cs"), "public class A { }", TestContext.CancellationToken);
            await File.WriteAllTextAsync(solutionPath, """<Solution><Project Path="A/A.csproj" /></Solution>""", TestContext.CancellationToken);
            var policy = new ExpansionPolicy(Path.GetDirectoryName(y)!, denyExpansion);
            PhysicalGraphLayout originalLayout;
            {
                PhysicalGraphCandidate original = await PhysicalGraphCandidate.LoadAsync("reactivation", solutionPath, OwnedNativeLoadContext.Properties(solutionPath, new Dictionary<string, string>()),
                policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
                Exception? cleanupPrimary28 = null;
                try
                {
                    originalLayout = await PhysicalGraphLayout.CreateAsync("reactivation", solutionPath, original.Solution, policy, CancellationToken.None);
                }
                catch (Exception failure) { cleanupPrimary28 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary28, original); }
            }

            await File.WriteAllTextAsync(a, """<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><DefineConstants>APPLIED_A</DefineConstants><NuGetAudit>false</NuGetAudit></PropertyGroup></Project>""", TestContext.CancellationToken);
            await File.WriteAllTextAsync(shared, """
                <Project><PropertyGroup><DefineConstants>CURRENT_SHARED</DefineConstants></PropertyGroup><ItemGroup><ProjectReference Include="$(MSBuildThisFileDirectory)../NewY/Y.csproj" /></ItemGroup></Project>
                """, TestContext.CancellationToken);
            Dictionary<string, byte[]> sourceBytes = new[] { a, shared, y, yCode }.ToDictionary(path => path, File.ReadAllBytes);
            PhysicalGraphCandidate current = await PhysicalGraphCandidate.LoadAsync("reactivation", solutionPath, OwnedNativeLoadContext.Properties(solutionPath, new Dictionary<string, string>()),
                policy, new WorkspaceSessionLoader(), NullLogger.Instance, null, CancellationToken.None);
            Exception? cleanupPrimary29 = null;
            try
            {
                Assert.HasCount(1, current.Solution.ProjectIds);
                var layout = PhysicalGraphLayout.Union(await PhysicalGraphLayout.CreateAsync("reactivation", solutionPath, current.Solution, policy, CancellationToken.None), originalLayout);
                var stageOperation = OwnedGraphStageAuthority.Create("reactivation", current.Solution, policy, StageParent(entry));
                Task<PhysicalGraphStage> Stage() => PhysicalGraphStage.CreateAsync("reactivation", solutionPath, current.Solution, layout,
                    [FileSnapshotDto.FromExistingBytes(a, originalA)], EvaluatedProjectGraph.Properties(current.Solution),
                    new WorkspaceSessionLoader(), stageOperation.Policy, stageOperation, NullLogger.Instance, CancellationToken.None);
                if (denyExpansion)
                {
                    await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(Stage);
                    Assert.IsTrue(policy.RejectedExpansion);
                    AssertStageContentsReclaimed(StageParent(entry));
                }
                else
                {
                    PhysicalGraphStage stage = await Stage();
                    Exception? cleanupPrimary30 = null;
                    try
                    {
                        stage.EnterReadAuthority();
                        Project stagedA = stage.Expected.Solution.Projects.Single(project => project.Name == "A");
                        Project stagedY = stage.Expected.Solution.Projects.Single(project => project.Name == "Y");
                        CollectionAssert.Contains(((CSharpParseOptions)stagedA.ParseOptions!).PreprocessorSymbolNames.ToArray(), "CURRENT_SHARED");
                        CollectionAssert.DoesNotContain(((CSharpParseOptions)stagedA.ParseOptions!).PreprocessorSymbolNames.ToArray(), "PRIOR_SHARED");
                        Assert.AreEqual(NullableContextOptions.Enable, ((CSharpCompilationOptions)stagedY.CompilationOptions!).NullableContextOptions);
                        Assert.AreEqual(y, stage.Expected.MapPath(stagedY.FilePath!));
                        Assert.AreNotEqual(y, stagedY.FilePath);
                        Assert.IsTrue((await stagedY.Documents.Single(document => document.Name == "Y.cs").GetTextAsync(TestContext.CancellationToken)).ToString().Contains("CurrentY", StringComparison.Ordinal));
                        Assert.IsTrue(policy.ApprovedExpansion);

                    }
                    catch (Exception failure) { cleanupPrimary30 = failure; throw; }
                    finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary30, stage); }
                }
                foreach ((string? path, byte[]? bytes) in sourceBytes) Assert.AreSequenceEqual(bytes, File.ReadAllBytes(path));

            }
            catch (Exception failure) { cleanupPrimary29 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary29, current); }
        }
        catch (Exception failure) { primary = failure; throw; }
        finally
        {
            await CleanupFailureCollector.RunAfterFailureAsync("Native import reactivation fixture cleanup failed.", primary,
                CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
        }
    }

    private sealed class ExpansionPolicy(string projectDirectory, bool reject) : IWorkspacePathPolicy
    {
        internal bool ApprovedExpansion { get; private set; }
        internal bool RejectedExpansion { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
            if (!paths.Any(path => FileSystemPath.Comparer.Equals(path, projectDirectory) || FileSystemPath.IsStrictDescendant(projectDirectory, path))) return;
            if (reject)
            {
                RejectedExpansion = true;
                throw new UnauthorizedAccessException("Synthetic newly active project refusal.");
            }
            ApprovedExpansion = true;
        }
    }

    private sealed class CancelCandidateLoader(OperationCanceledException failure) : WorkspaceSessionLoader
    {
        internal int Calls { get; private set; }
        public override async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease, NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission)> CreateAndOpenAsync(
            string workspaceId, string path, IDictionary<string, string>? globalProperties,
            IWorkspacePathPolicy pathPolicy, WorkspaceDiagnosticsSink diagnostics, ILogger logger, SupplementalPackageReadAuthority supplementalPackages, NativeSourceAdmission? sourceAdmission, CancellationToken ct)
        {
            if (++Calls == 2) throw failure;
            return await base.CreateAndOpenAsync(workspaceId, path, globalProperties, pathPolicy, diagnostics, logger, supplementalPackages, sourceAdmission, ct);
        }
    }

    private static void AssertStageContentsReclaimed(string staging)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.IsFalse(Directory.Exists(staging) && Directory.EnumerateDirectories(staging).Any());
        }
        else
        {
            Assert.IsTrue(Directory.Exists(staging) && Directory.EnumerateDirectories(staging).Any(),
                "POSIX retains terminal staged directory skeletons after content cleanup.");
            Assert.IsEmpty(Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories));
        }
    }

    private sealed class RecordingPolicy : IWorkspacePathPolicy
    {
        internal bool Reject { get; init; }
        internal sealed record Call(string WorkspaceId, IReadOnlyList<string> Paths);
        private readonly System.Collections.Concurrent.ConcurrentQueue<Call> _attempted = new();
        private readonly System.Collections.Concurrent.ConcurrentQueue<Call> _approved = new();
        internal IReadOnlyList<Call> AttemptedCalls => _attempted.ToArray();
        internal IReadOnlyList<Call> ApprovedCalls => _approved.ToArray();
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            var call = new Call(workspaceId, Array.AsReadOnly(paths.ToArray()));
            _attempted.Enqueue(call);
            await new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
            if (Reject) throw new UnauthorizedAccessException("Synthetic stage inventory refusal.");
            _approved.Enqueue(call);
        }
    }

    [TestMethod]
    [DataRow("initial-planning")]
    [DataRow("initial-copy")]
    [DataRow("rebuilt-copy")]
    [DataRow("rebuilt-planning")]
    [DataRow("rebuilt-definitions")]
    public async Task CallbackRevocation_PrecedesExplicitStageEnumerationOrRead(string boundary)
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary31 = null;
        try
        {
            var before = await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken);
            var looseDefinition = Path.Combine(fixture.Root, "unimported-copy-only.props");
            await File.WriteAllTextAsync(looseDefinition, "<Project />", TestContext.CancellationToken);
            var looseBefore = await File.ReadAllBytesAsync(looseDefinition, TestContext.CancellationToken);
            PhysicalGraphLayout layout = await PhysicalGraphLayout.CreateAsync("stage-grant", fixture.LoadedPath, fixture.Source,
                new OwnedTestWorkspacePathPolicy(), CancellationToken.None);
            var policy = new StageReadRevocationPolicy(fixture.Root, fixture.ProjectA, fixture.ProjectB, looseDefinition, boundary);
            Exception? policyCleanupPrimary1 = null;
            try
            {
                var loader = new StageReadBoundaryLoader(fixture.Root, policy);
                UnauthorizedAccessException failure = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(async () =>
                {
                    var stageOperation = OwnedGraphStageAuthority.Create("stage-grant", fixture.Source, policy, StageParent(fixture.Root));
                    PhysicalGraphStage stage = await PhysicalGraphStage.CreateAsync("stage-grant", fixture.LoadedPath, fixture.Source,
                        layout, [], EvaluatedProjectGraph.Properties(fixture.Source), loader, stageOperation.Policy, stageOperation, NullLogger.Instance, CancellationToken.None);
                    Exception? cleanupPrimary32 = null;
                    try
                    {
                        stage.EnterReadAuthority();

                    }
                    catch (Exception failure) { cleanupPrimary32 = failure; throw; }
                    finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary32, stage); }
                });
                Assert.AreSame(policy.Refusal, failure);
                Assert.IsTrue(policy.Injected, "The real policy callback must revoke the earlier admitted coordinate at the selected boundary.");
                Assert.IsTrue(policy.Refused);
                if (boundary is "initial-planning" or "rebuilt-planning")
                {
                    Assert.DoesNotContain(policy.Sentinel, layout.InputFiles, FileSystemPath.Comparer,
                        "The sentinel is not a previously captured input; only enumeration can add it to our later policy inventory.");
                    Assert.IsFalse(policy.SentinelEnrolledAtRefusal,
                        "A directory grant must be renewed before PlanCopy discovers a previously unadmitted file.");
                    Assert.HasCount(boundary == "initial-planning" ? 0 : 1, policy.LoadedStages);
                }
                else
                {
                    Assert.HasCount(boundary == "initial-copy" ? 0 : 1, policy.LoadedStages,
                    "The exclusive-source trap must be armed after the selected stage is issued and before its native load.");
                }

                policy.ReleaseTrap();
                Assert.AreSequenceEqual(before, await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken));
                Assert.AreSequenceEqual(looseBefore, await File.ReadAllBytesAsync(looseDefinition, TestContext.CancellationToken));


            }
            catch (Exception failure) { policyCleanupPrimary1 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", policyCleanupPrimary1, policy); }
        }
        catch (Exception failure) { cleanupPrimary31 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary31, fixture); }
    }

    private sealed class StageReadRevocationPolicy(string root, string source, string secondProject, string looseDefinition, string boundary) : IWorkspacePathPolicy, IDisposable
    {
        private bool _initialInventory;
        private bool _rebuiltInventory;
        private FileStream? _trap;
        internal string Sentinel { get; } = Path.Combine(root, "late-copy-inventory.bin");
        internal List<string> LoadedStages { get; } = [];
        internal UnauthorizedAccessException Refusal { get; } = new("The exact staged source grant was revoked.");
        internal bool Injected { get; private set; }
        internal bool Refused { get; private set; }
        internal bool SentinelEnrolledAtRefusal { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
            var denied = boundary is "initial-planning" or "rebuilt-planning" ? root
                : boundary == "rebuilt-definitions" ? looseDefinition : source;
            if (Injected && paths.Contains(denied, FileSystemPath.Comparer))
            {
                Refused = true;
                SentinelEnrolledAtRefusal = paths.Contains(Sentinel, FileSystemPath.Comparer);
                throw Refusal;
            }
            if (Injected) return;
            if (boundary == "initial-planning")
            {
                if (paths.Contains(root, FileSystemPath.Comparer) && paths.Contains(source, FileSystemPath.Comparer))
                {
                    _initialInventory = true;
                }
                else if (_initialInventory && paths.Count == 1)
                {
                    File.WriteAllText(Sentinel, "owned nonsemantic copy inventory witness");
                    Injected = true;
                }
                return;
            }
            if (boundary == "rebuilt-planning")
            {
                // This exact set is issued for the discovered desired projects,
                // after native discovery and before explicit copy enumeration.
                if (LoadedStages.Count == 1 && !paths.Contains(root, FileSystemPath.Comparer)
                    && paths.Contains(source, FileSystemPath.Comparer)
                    && paths.Contains(secondProject, FileSystemPath.Comparer)
                    && paths.Contains(Path.GetDirectoryName(source)!, FileSystemPath.Comparer)
                    && paths.Contains(Path.GetDirectoryName(secondProject)!, FileSystemPath.Comparer))
                {
                    File.WriteAllText(Sentinel, "owned nonsemantic copy inventory witness");
                    Injected = true;
                }
                return;
            }
            var issuedStage = CurrentIssuedStage(root);
            if (issuedStage is null) return;
            if (boundary == "rebuilt-definitions")
            {
                if (LoadedStages.Count != 1) return;
                if (paths.Contains(looseDefinition, FileSystemPath.Comparer)
                    && !LoadedStages.Contains(issuedStage, FileSystemPath.Comparer))
                    _rebuiltInventory = true;
                else if (_rebuiltInventory && paths.Count == 1)
                {
                    _trap = new FileStream(looseDefinition, FileMode.Open, FileAccess.Read, FileShare.None);
                    Injected = true;
                }
                return;
            }
            var desiredLoads = boundary == "initial-copy" ? 0 : 1;
            if (LoadedStages.Count != desiredLoads) return;
            var issued = !LoadedStages.Contains(issuedStage, FileSystemPath.Comparer);
            if (!issued) return;
            // This callback follows exclusive custody creation. The next copy's
            // old unchecked read would fail sharing; renewal must refuse first.
            _trap = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
            Injected = true;
        }
        internal void ReleaseTrap() { _trap?.Dispose(); _trap = null; }
        public void Dispose() => ReleaseTrap();
    }

    private sealed class StageReadBoundaryLoader(string root, StageReadRevocationPolicy policy) : WorkspaceSessionLoader
    {
        public override Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease,
            NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission)> CreateAndOpenAsync(string workspaceId, string path,
            IDictionary<string, string>? globalProperties, IWorkspacePathPolicy pathPolicy,
            WorkspaceDiagnosticsSink diagnostics, ILogger logger, SupplementalPackageReadAuthority supplementalPackages, NativeSourceAdmission? sourceAdmission, CancellationToken ct)
        {
            policy.LoadedStages.Add(IssuedStageDirectory(root, path));
            return base.CreateAndOpenAsync(workspaceId, path, globalProperties, pathPolicy, diagnostics, logger, supplementalPackages, sourceAdmission, ct);
        }
    }


    [TestMethod]
    public async Task BuildReceipt_PackageCallbackCannotRevokeAnEarlierInputGrantBeforeHashing()
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary33 = null;
        try
        {
            var ordinary = new OwnedTestWorkspacePathPolicy();
            PhysicalGraphLayout layout = await PhysicalGraphLayout.CreateAsync("receipt-grant", fixture.LoadedPath, fixture.Source,
                ordinary, CancellationToken.None);
            var stageOperation = OwnedGraphStageAuthority.Create("receipt-grant", fixture.Source, ordinary, StageParent(fixture.Root));
            PhysicalGraphStage stage = await PhysicalGraphStage.CreateAsync("receipt-grant", fixture.LoadedPath, fixture.Source,
                layout, [], EvaluatedProjectGraph.Properties(fixture.Source), new WorkspaceSessionLoader(),
                stageOperation.Policy, stageOperation, NullLogger.Instance, CancellationToken.None);
            Exception? cleanupPrimary34 = null;
            try
            {
                stage.EnterReadAuthority();
                await stage.Expected.AssertBuildInputsAsync("receipt-grant", ordinary, CancellationToken.None);
                var source = fixture.ProjectA;
                var before = await File.ReadAllBytesAsync(source, TestContext.CancellationToken);
                Assert.IsTrue(stage.Expected.BuildInputs.Any(input => FileSystemPath.Comparer.Equals(input.FilePath, source)));
                Assert.IsNotEmpty(stage.Expected.Packages.AssetInputs,
                    "The actual native receipt must contain a selecting-assets callback after its full read-set grant.");
                var policy = new ReceiptCallbackPolicy(stage.Expected, source);
                Exception? policyCleanupPrimary2 = null;
                try
                {
                    Exception? observed = null;
                    try { await stage.Expected.AssertBuildInputsAsync("receipt-grant", policy, CancellationToken.None); }
                    catch (Exception failure) { observed = failure; }
                    Assert.IsTrue(policy.InitialReadSetGranted);
                    Assert.IsTrue(policy.Injected, "An actual selecting-assets callback must revoke the earlier document grant.");
                    if (OperatingSystem.IsWindows())
                        await Assert.ThrowsAsync<IOException>(() => PhysicalInputIdentity.CaptureAsync(source, CancellationToken.None));
                    Assert.AreSame(policy.Refusal, observed);
                    policy.ReleaseTrap();
                    Assert.AreSequenceEqual(before, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));


                }
                catch (Exception failure) { policyCleanupPrimary2 = failure; throw; }
                finally { OwnedResourceCleanup.DisposePreservingFailure("Native test resource cleanup failed.", policyCleanupPrimary2, policy); }
            }
            catch (Exception failure) { cleanupPrimary34 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary34, stage); }

        }
        catch (Exception failure) { cleanupPrimary33 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary33, fixture); }
    }

    private sealed class ReceiptCallbackPolicy(PhysicalGraphExpectation expected, string source) : IWorkspacePathPolicy, IDisposable
    {
        private readonly HashSet<string> _readSet = expected.Packages.PathsRequiringCurrentGrant(
            expected.BuildInputs.Select(input => input.FilePath)).ToHashSet(FileSystemPath.Comparer);
        private readonly HashSet<string> _assets = expected.Packages.AssetInputs.Select(input => input.FilePath)
            .ToHashSet(FileSystemPath.Comparer);
        private FileStream? _trap;
        internal UnauthorizedAccessException Refusal { get; } = new("The exact build receipt source grant was revoked.");
        internal bool InitialReadSetGranted { get; private set; }
        internal bool Injected { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
            if (Injected && paths.Contains(source, FileSystemPath.Comparer)) throw Refusal;
            if (_readSet.SetEquals(paths)) InitialReadSetGranted = true;
            if (!Injected && InitialReadSetGranted && _assets.Count > 0
                && paths.Count == _assets.Count && _assets.SetEquals(paths))
            {
                _trap = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None);
                Injected = true;
            }
        }
        internal void ReleaseTrap() { _trap?.Dispose(); _trap = null; }
        public void Dispose() => ReleaseTrap();
    }


    [TestMethod]
    public async Task ForkPlanning_RenewsDirectoryGrantAfterTheIssuedBuildReceiptCallback()
    {
        Fixture fixture = await Fixture.CreateAsync(false, false, false);
        Exception? cleanupPrimary35 = null;
        try
        {
            var ordinary = new OwnedTestWorkspacePathPolicy();
            PhysicalGraphLayout layout = await PhysicalGraphLayout.CreateAsync("fork-grant", fixture.LoadedPath, fixture.Source,
                ordinary, CancellationToken.None);
            var stageOperation = OwnedGraphStageAuthority.Create("fork-grant", fixture.Source, ordinary, StageParent(fixture.Root));
            PhysicalGraphStage observedStage = await PhysicalGraphStage.CreateAsync("fork-grant", fixture.LoadedPath, fixture.Source,
                layout, [], EvaluatedProjectGraph.Properties(fixture.Source), new WorkspaceSessionLoader(),
                stageOperation.Policy, stageOperation, NullLogger.Instance, CancellationToken.None);
            Exception? cleanupPrimary36 = null;
            try
            {
                observedStage.EnterReadAuthority();
                var sourceBefore = await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken);
                Assert.IsFalse(observedStage.Expected.Packages.PathsRequiringCurrentGrant(
                        observedStage.Expected.BuildInputs.Select(input => input.FilePath)).Contains(fixture.Root, FileSystemPath.Comparer),
                    "The renewed build-input grant must not itself grant the copy-root directory; this control isolates the later planner grant.");
                var policy = new ForkPlanningRevocationPolicy(fixture.Root, observedStage.Layout, observedStage.Expected);
                observedStage.Dispose();
                var forkRoot = Path.Combine(fixture.Root, ".roslynmcp", "forks", Guid.NewGuid().ToString("N"));
                var forkStageOperation = OwnedGraphStageAuthority.Create("fork-grant", fixture.Source, policy, StageParent(fixture.Root));
                UnauthorizedAccessException failure = await Assert.ThrowsExactlyAsync<UnauthorizedAccessException>(() =>
                    ForkGraphPreparation.CreateAsync("fork-grant", fixture.LoadedPath, fixture.Source, fixture.Source,
                        fixture.Source, layout, forkRoot, forkStageOperation.Policy, forkStageOperation, NullLogger.Instance, CancellationToken.None));
                Assert.AreSame(policy.Refusal, failure);
                Assert.IsTrue(policy.Injected, "The separately issued exact build receipt must follow the full planning-set grant.");
                Assert.IsFalse(policy.SentinelEnrolledAtRefusal,
                    "The fork planner must renew directory authority before discovering a newly created copy-only file.");
                Assert.DoesNotContain(policy.Sentinel, observedStage.Layout.InputFiles, FileSystemPath.Comparer);
                Assert.IsFalse(Directory.Exists(forkRoot), "Preparation may not publish the executable fork.");
                Assert.AreSequenceEqual(sourceBefore, await File.ReadAllBytesAsync(fixture.ProjectA, TestContext.CancellationToken));

            }
            catch (Exception failure) { cleanupPrimary36 = failure; throw; }
            finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary36, observedStage); }

        }
        catch (Exception failure) { cleanupPrimary35 = failure; throw; }
        finally { OwnedResourceCleanup.DisposePreservingFailure("Native stage resource cleanup failed.", cleanupPrimary35, fixture); }
    }

    private sealed class ForkPlanningRevocationPolicy(string root, PhysicalGraphLayout layout, PhysicalGraphExpectation expected) : IWorkspacePathPolicy
    {
        private readonly HashSet<string> _planningSet = layout.Packages.PathsRequiringCurrentGrant(
            layout.ContentRoots.Concat(layout.InputFiles)).ToHashSet(FileSystemPath.Comparer);
        private readonly HashSet<string> _readSet = expected.Packages.PathsRequiringCurrentGrant(
            expected.BuildInputs.Select(input => input.FilePath)).ToHashSet(FileSystemPath.Comparer);
        private bool _priorPlanningGrant;
        internal string Sentinel { get; } = Path.Combine(root, "late-fork-copy-inventory.bin");
        internal UnauthorizedAccessException Refusal { get; } = new("The exact fork source directory grant was revoked.");
        internal bool Injected { get; private set; }
        internal bool SentinelEnrolledAtRefusal { get; private set; }
        public async Task ValidateAsync(string workspaceId, IReadOnlyList<string> paths, CancellationToken ct)
        {
            await new OwnedTestWorkspacePathPolicy().ValidateAsync(workspaceId, paths, ct);
            if (Injected && paths.Contains(root, FileSystemPath.Comparer))
            {
                SentinelEnrolledAtRefusal = paths.Contains(Sentinel, FileSystemPath.Comparer);
                throw Refusal;
            }
            if (!Injected && _priorPlanningGrant && _readSet.SetEquals(paths))
            {
                File.WriteAllText(Sentinel, "owned nonsemantic fork copy inventory witness");
                Injected = true;
            }
            _priorPlanningGrant = _planningSet.SetEquals(paths);
        }
    }

    private sealed class Fixture(string root, string loadedPath, string projectA, string projectB,
        FileSnapshotDto beforeA, Solution source, IReadOnlyList<DiagnosticDto> warnings, IDisposable workspace, IDisposable lease) : IDisposable
    {
        internal string Root { get; } = root;
        internal string LoadedPath { get; } = loadedPath;
        internal string ProjectA { get; } = projectA;
        internal string ProjectB { get; } = projectB;
        internal Solution Source { get; } = source;
        internal IReadOnlyList<DiagnosticDto> AnalyzerWarnings { get; } = warnings;
        internal async Task<PhysicalGraphStage> StageAsync(Solution actual, IWorkspacePathPolicy policy, WorkspaceSessionLoader loader)
        {
            var operation = OwnedGraphStageAuthority.Create("stage-test", actual, policy, StageParent(Root));
            return await PhysicalGraphStage.CreateAsync("stage-test", LoadedPath, actual,
                await PhysicalGraphLayout.CreateAsync("stage-test", LoadedPath, actual, operation.Policy, CancellationToken.None), [beforeA],
                EvaluatedProjectGraph.Properties(actual), loader, operation.Policy, operation, NullLogger.Instance, CancellationToken.None);
        }
        internal static async Task<Fixture> CreateAsync(bool absoluteMembership, bool missingAnalyzer, bool textSolution)
        {
            var root = Path.Combine(TestTempRoot.Current, nameof(PhysicalGraphStageTests), Guid.NewGuid().ToString("N"));
            var a = Path.Combine(root, "A", "A.csproj");
            var b = Path.Combine(root, "B", textSolution ? "B%3B.csproj" : "B.csproj");
            var loadedPath = Path.Combine(root, textSolution ? "Stage.sln" : "Stage.slnx");
            string WithAnalyzer(string xml) => missingAnalyzer ? xml.Replace("</Project>", "<ItemGroup><Analyzer Include=\"MissingAnalyzer.dll\" /></ItemGroup></Project>", StringComparison.Ordinal) : xml;
            var before = Encoding.UTF8.GetBytes(WithAnalyzer(Project("<DefineConstants>ORIGINAL_A</DefineConstants>")));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "A"));
                Directory.CreateDirectory(Path.Combine(root, "B"));
                await File.WriteAllTextAsync(a, WithAnalyzer(Project("<DefineConstants>APPLIED_A</DefineConstants>")));
                await File.WriteAllTextAsync(b, Project("<Nullable>enable</Nullable>"));
                await File.WriteAllTextAsync(Path.Combine(root, "A", "A.cs"), "public class CurrentA { }");
                await File.WriteAllTextAsync(Path.Combine(root, "B", "B.cs"), "public class CurrentB { }");
                var definition = textSolution ? string.Join("\r\n",
                    "Microsoft Visual Studio Solution File, Format Version 12.00", "# Visual Studio Version 17",
                    "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"A\\A.csproj\", \"{11111111-1111-1111-1111-111111111111}\"",
                    "EndProject", "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"B\", \"B\\B%3B.csproj\", \"{22222222-2222-2222-2222-222222222222}\"",
                    "EndProject", "Global", "\tGlobalSection(SolutionConfigurationPlatforms) = preSolution",
                    "\t\tDebug|Any CPU = Debug|Any CPU", "\tEndGlobalSection",
                    "\tGlobalSection(ProjectConfigurationPlatforms) = postSolution",
                    "\t\t{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
                    "\t\t{11111111-1111-1111-1111-111111111111}.Debug|Any CPU.Build.0 = Debug|Any CPU",
                    "\t\t{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.ActiveCfg = Debug|Any CPU",
                    "\t\t{22222222-2222-2222-2222-222222222222}.Debug|Any CPU.Build.0 = Debug|Any CPU",
                    "\tEndGlobalSection", "EndGlobal", "")
                    : new XElement("Solution", new XElement("Project", new XAttribute("Path", absoluteMembership ? a : "A/A.csproj")),
                        new XElement("Project", new XAttribute("Path", absoluteMembership ? b : "B/B.csproj"))).ToString();
                await File.WriteAllTextAsync(loadedPath, definition);
                (MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease, NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission) loaded = await new WorkspaceSessionLoader().CreateAndOpenAsync("stage-source", loadedPath,
                    OwnedNativeLoadContext.Properties(loadedPath, new Dictionary<string, string>()),
                    new OwnedTestWorkspacePathPolicy(), new WorkspaceDiagnosticsSink(64), NullLogger.Instance, SupplementalPackageReadAuthority.None, null, CancellationToken.None);
                try
                {
                    UnresolvedAnalyzerReferenceStripper.Projection projection = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync("stage", loaded.Context, new OwnedTestWorkspacePathPolicy(), CancellationToken.None);
                    return new(root, loadedPath, a, b, FileSnapshotDto.FromExistingBytes(a, before),
                        projection.Solution, projection.Diagnostics, loaded.Workspace, loaded.Lease);
                }
                catch (Exception failure)
                {
                    OwnedResourceCleanup.Dispose("Stage fixture initialization and disposal failed.", failure, loaded.Workspace, loaded.Lease);
                    throw;
                }
            }
            catch (Exception failure)
            {
                await CleanupFailureCollector.RunAfterFailureAsync("Native stage fixture setup cleanup failed.", failure,
                    CleanupFailureCollector.FromAction(() => TestFixtureFileSystem.DeleteDirectoryIfExists(root)));
                throw;
            }
        }
        private static string Project(string configuration) => "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><NuGetAudit>false</NuGetAudit>"
            + configuration + "</PropertyGroup></Project>";
        public void Dispose() => OwnedResourceCleanup.Dispose("Stage fixture cleanup failed.", null, workspace, lease, new DirectoryCleanup(Root), new DirectoryCleanup(Root + "-private"));
    }
    private sealed class DirectoryCleanup(string path) : IDisposable
    {
        public void Dispose() => TestFixtureFileSystem.DeleteDirectoryIfExists(path);
    }

    public TestContext TestContext { get; set; }
}
