using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using NuGet.Versioning;
using RoslynMcp.Core.Models;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

public sealed partial class TestRunnerService
{
    private static readonly ConditionalWeakTable<TestRunPlan, TestDependencyReceipt> DependencyReceipts = [];
    private sealed record TestSourceAuthority(NativeCompilerContextCertificate Certificate, Solution Solution);
    private sealed record TestDependencyReceipt(InstalledPackageAuthority Packages, NativeCompilerContextCertificate? Certificate, Solution Solution,
        TestSourceAuthority? SourceAuthority, NativeBuildMembership Membership, RestoreInputAuthority? RestoreInputs);
    private sealed record TestExecutionPlan(bool RequiresMtpNative, ImmutableArray<string> NativeArguments,
        bool NoRestore, ImmutableArray<DotnetConfigurationIdentity> ExecutionInputs, InstalledPackageAuthority Packages, NativeCompilerContextCertificate? Certificate,
        TestSourceAuthority? SourceAuthority, NativeBuildMembership Membership, RestoreInputAuthority? RestoreInputs);

    private static Project ResolveLoadedProject(Solution solution, ProjectStatusDto status)
    {
        Project[] matches = [.. solution.Projects.Where(project => project.Name == status.Name
            && FileSystemPath.Comparer.Equals(project.FilePath, status.FilePath))];
        return matches.Length == 1 ? matches[0]
            : throw new PublicInvalidOperationException("The selected test project has no unique loaded evaluation context. Reload with an explicit target framework.");
    }

    private static bool ProjectRequiresMtpNativeExecution(ProjectStatusDto status, Solution solution, CancellationToken ct)
    {
        MsBuildMetadataHelper.RestoreInputs input = EvaluatedBuildGraph.Resolve(ResolveLoadedProject(solution, status), ct).Inputs;
        var packages = input.Packages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var classic = input.UseVSTest || packages.Contains("Microsoft.NET.Test.Sdk")
            && (packages.Contains("MSTest.TestAdapter") || packages.Contains("NUnit3TestAdapter") || packages.Contains("xunit.runner.visualstudio"));
        return packages.Contains("TUnit") || input.IsTestingPlatformApplication && !classic;
    }

    private async Task<TestExecutionPlan> ResolveTestExecutionPlanAsync(IReadOnlyList<ProjectStatusDto> projects,
        string? filter, string workspaceId, Solution solution,
        PreparedDotnetTestContext context, CancellationToken ct)
    {
        CancellationToken callerToken = ct;
        callerToken.ThrowIfCancellationRequested();
        var started = _timeProvider.GetTimestamp();
        EvaluatedBuildGraph.Graph invocationGraph = EvaluatedBuildGraph.Source(solution, ImmutableDictionary<string, byte[]>.Empty, ct);
        if (!context.UsesNativeMtp)
        {
            if (projects.Any(project => ProjectRequiresMtpNativeExecution(project, solution, ct)))
            {
                throw new PublicInvalidOperationException("A selected test project requires Microsoft.Testing.Platform, but the authorized global.json does not select it. "
                    + "Select an authorized Microsoft.Testing.Platform configuration, reload, and retry; use projectName to target one compatible project.");
            }
        }
        NativeCompilerContextCertificate? certificate = null;
        if (NativeCompilerContextCertificate.TryGet(solution, out _))
        {
            // An unavailable native certificate retains semantic reads only. Fresh
            // capture cannot renew its discarded load epoch into producer authority.
            certificate = NativeCompilerContextCertificate.Require(solution);
        }
        var sourceAuthority = certificate is null ? null : new TestSourceAuthority(certificate, solution);
        (ProjectStatusDto Status, RestoreInvocationPlanner.SelectedGraph Graph)[] selected = [.. projects.Select(selectedStatus => (Status: selectedStatus,
            Graph: RestoreInvocationPlanner.Select(ResolveLoadedProject(solution, selectedStatus), invocationGraph, ct)))];
        var selectedAssets = selected.SelectMany(item => item.Graph.Nodes).Select(node => node.Inputs.AssetsPath)
            .OfType<string>().ToHashSet(FileSystemPath.Comparer);
        var requiresRestore = certificate is null || certificate.InstalledPackages.AssetInputs
            .Any(input => !input.Exists && selectedAssets.Contains(input.FilePath));
        RestoreInputAuthority? restoredInputs = null;
        var plans = new List<(ImmutableArray<string> NativeArguments, ImmutableArray<DotnetConfigurationIdentity> ExecutionInputs, InstalledPackageAuthority Packages)>();
        var preflight = new List<(ProjectStatusDto Status, string[]? KnownTests, string[]? XunitMethods)>();
        var selectedNodes = selected.SelectMany(item => item.Graph.Nodes).ToHashSet();
        EvaluatedBuildGraph.Node[] restoreNodes = [.. invocationGraph.Nodes.Where(selectedNodes.Contains)];
        callerToken.ThrowIfCancellationRequested();
        var remaining = requiresRestore ? _options.BuildTimeout - _timeProvider.GetElapsedTime(started) : TimeSpan.Zero;
        if (requiresRestore && remaining <= TimeSpan.Zero)
            throw new TimeoutException("Test preparation restore exceeded its total time budget.");
        using var timeout = requiresRestore ? new CancellationTokenSource(remaining, _timeProvider) : null;
        using var phase = timeout is null ? null : CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeout.Token);
        if (phase is not null) ct = phase.Token;
        try
        {
            if (certificate is not null)
            {
                await certificate.AssertCurrentAsync(solution, workspaceId, _pathPolicy, ct).ConfigureAwait(false);
                await certificate.InstalledPackages.AssertCurrentAsync(
                    (paths, token) => _pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
            }
            foreach (var item in selected)
            {
                var protocols = await ValidateTestProtocolAsync(item.Status, item.Graph.Root.Inputs,
                    filter, workspaceId, context.UsesNativeMtp, ct).ConfigureAwait(false);
                preflight.Add((item.Status, protocols.KnownTests, protocols.XunitMethods));
            }
            if (requiresRestore)
            {
                // Freeze all selected closures before the first producer. Shared assets
                // receive their final snapshot only after every restore has completed.
                restoredInputs = await RestoreInputAuthority.CaptureAsync(
                    invocationGraph.Nodes, restoreNodes, solution,
                    (paths, token) => _pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
                NativeCompilerContextCertificate? originalCertificate = certificate;
                var sourceCertificate = certificate;
                IReadOnlyList<string> finalCertificateGrantPaths = sourceCertificate is null ? []
                    : sourceCertificate.FinalRestoreGrantPaths(solution);
                Task AssertFinalCertificateCurrentAsync(IReadOnlySet<string> transitionedOutputs, CancellationToken token) => sourceCertificate is null
                    ? Task.CompletedTask : sourceCertificate.AssertRestorePhysicalCurrentAsync(solution, transitionedOutputs, token);
                async Task AuthorizeProducerAsync(CancellationToken token)
                {
                    if (originalCertificate is not null)
                    {
                        // Never accept source drift as the new pre-restore baseline.
                        // After this first producer, selecting assets deliberately
                        // transition and the frozen authored/package proof takes over.
                        await originalCertificate.AssertCurrentAsync(solution, workspaceId, _pathPolicy, token).ConfigureAwait(false);
                        await originalCertificate.InstalledPackages.AssertCurrentAsync(
                            (paths, grantToken) => _pathPolicy.ValidateAsync(workspaceId, paths, grantToken), token).ConfigureAwait(false);
                    }
                    await restoredInputs.AssertCurrentAsync(token).ConfigureAwait(false);
                    originalCertificate = null;
                }
                await RestoreProjectsAsync(workspaceId, projects[0].FilePath, restoreNodes,
                    restoredInputs, AuthorizeProducerAsync, finalCertificateGrantPaths, AssertFinalCertificateCurrentAsync, started, ct).ConfigureAwait(false);
                await restoredInputs.AssertCurrentAsync(ct).ConfigureAwait(false);
                // A successful restore deliberately replaces selecting assets. The old
                // certificate still belongs to its old semantic snapshot; never renew it.
                certificate = null;
            }

            EvaluatedBuildGraph.Graph restoredGraph = requiresRestore
                ? EvaluatedBuildGraph.Source(solution, ImmutableDictionary<string, byte[]>.Empty, ct) : invocationGraph;
            foreach (var item in preflight)
            {
                ct.ThrowIfCancellationRequested();
                RestoreInvocationPlanner.SelectedGraph restoreGraph = RestoreInvocationPlanner.Select(
                    ResolveLoadedProject(solution, item.Status), restoredGraph, ct);
                MsBuildMetadataHelper.RestoreInputs input = restoreGraph.Root.Inputs;
                var names = input.Packages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var tunit = names.Contains("TUnit");
                var xunit = names.Any(ProjectMetadataParser.IsXunitV3Package);
                var bridge = names.Contains("MSTest.TestFramework") || names.Contains("MSTest") || names.Contains("NUnit");
                var knownTests = item.KnownTests;
                var xunitMethods = item.XunitMethods;
                ExecutionDependencySnapshot snapshot = await CaptureExecutionInputsAsync(workspaceId, restoreGraph, context.UsesNativeMtp, ct).ConfigureAwait(false);
                if (certificate is not null)
                    await certificate.AssertCurrentAsync(solution, workspaceId, _pathPolicy, ct).ConfigureAwait(false);
                if (!context.UsesNativeMtp)
                {
                    plans.Add(([], snapshot.Inputs, snapshot.Packages));
                    continue;
                }
                // Dependencies route the requested protocol; only the actual executable can
                // establish extension registration (including manual registration). Missing or
                // excluded reporters therefore return the host's real CLI/build failure.
                var reporter = "--report-trx";
                if (snapshot.Version("Microsoft.Testing.Extensions.TrxReport") is null && xunit)
                {
                    NuGetVersion[] versions = [.. names.Where(ProjectMetadataParser.IsXunitV3Package).Select(name => snapshot.Version(name)).OfType<NuGet.Versioning.NuGetVersion>()];
                    if (versions.Length > 0 && versions.All(version => version >= new NuGet.Versioning.NuGetVersion(3, 1, 0))) reporter = "--report-xunit-trx";
                }
                ImmutableArray<string>.Builder arguments = ImmutableArray.CreateBuilder<string>();
                arguments.Add(reporter);
                if (!string.IsNullOrWhiteSpace(filter))
                {
                    if (tunit || xunit)
                    {
                        if (tunit)
                        {
                            NuGetVersion? engine = snapshot.Version("TUnit.Engine");
                            arguments.Add("--treenode-filter");
                            arguments.Add(TreeNodeFilterTranslator.Translate(filter, engine is null ? null : [engine], knownTests));
                        }
                        else
                        {
                            arguments.Add("--filter-method");
                            arguments.AddRange(xunitMethods!);
                        }
                    }
                    else if (bridge)
                    {
                        arguments.Add("--filter");
                        arguments.Add(filter);
                    }
                    else
                    {
                        throw new PublicInvalidOperationException("The selected native test framework has no supported translation for this filter. Use an unfiltered invocation or target a framework with an established filter protocol.");
                    }
                }
                plans.Add((arguments.ToImmutable(), snapshot.Inputs, snapshot.Packages));
            }
            if (plans.Count == 0) throw new PublicInvalidOperationException("No compatible test projects were selected.");
            if (plans.Skip(1).Any(plan => !plan.NativeArguments.SequenceEqual(plans[0].NativeArguments, StringComparer.Ordinal)))
                throw new PublicInvalidOperationException("The selected native test projects require different reporter or filter protocols. Use projectName to run each compatible project separately.");
            var membership = NativeBuildMembership.Capture(EvaluatedBuildGraph.Source(solution, ImmutableDictionary<string, byte[]>.Empty, ct), ct);
            if (restoredInputs is not null)
                await restoredInputs.AssertCurrentAsync(ct).ConfigureAwait(false);
            var mutableRestorePaths = restoredInputs?.MutablePaths.ToHashSet(FileSystemPath.Comparer);
            var executionInputs = plans.SelectMany(plan => plan.ExecutionInputs)
                .Concat(restoredInputs is null ? [] : restoredInputs.Inputs.Where(input => input.Kind != PhysicalInputKind.Directory
                        && mutableRestorePaths!.Contains(input.FilePath))
                    .Select(input => new DotnetConfigurationIdentity(input.FilePath, input.Exists, input.ContentHash)))
                .GroupBy(input => input.FilePath, FileSystemPath.Comparer)
                .Select(group =>
                {
                    DotnetConfigurationIdentity first = group.First();
                    if (group.Any(input => first.Exists != input.Exists || !first.ContentHash.AsSpan().SequenceEqual(input.ContentHash.AsSpan())))
                        throw new PublicInvalidOperationException("Test execution inputs changed during preparation. Prepare the invocation again.");
                    return first;
                }).ToImmutableArray();
            if (certificate is not null)
                await certificate.AssertCurrentAsync(solution, workspaceId, _pathPolicy, ct).ConfigureAwait(false);
            var packages = InstalledPackageAuthority.Merge(plans.Select(plan => plan.Packages));
            if (restoredInputs is not null) packages = packages.RetainPackageFilesFrom(restoredInputs.Packages);
            await AssertFinalTestAuthorityAsync(workspaceId,
                new TestDependencyReceipt(packages, certificate, solution, sourceAuthority, membership, restoredInputs),
                context with { ExecutionInputs = executionInputs }, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new(context.UsesNativeMtp, plans[0].NativeArguments, true, executionInputs, packages, certificate, sourceAuthority, membership, restoredInputs);
        }
        catch (TimeoutException failure) when (callerToken.IsCancellationRequested)
        {
            throw new OperationCanceledException("Test preparation was canceled.", failure, callerToken);
        }
        catch (OperationCanceledException failure) when (callerToken.IsCancellationRequested)
        {
            if (failure.CancellationToken == callerToken) throw;
            throw new OperationCanceledException("Test preparation was canceled.", failure, callerToken);
        }
        catch (OperationCanceledException failure) when (timeout?.IsCancellationRequested == true)
        {
            throw new TimeoutException("Test preparation restore exceeded its total time budget.", failure);
        }
    }

    private async Task<(string[]? KnownTests, string[]? XunitMethods)> ValidateTestProtocolAsync(
        ProjectStatusDto status, MsBuildMetadataHelper.RestoreInputs input, string? filter,
        string workspaceId, bool usesNativeMtp, CancellationToken ct)
    {
        var names = input.Packages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tunit = names.Contains("TUnit");
        var xunit = names.Any(ProjectMetadataParser.IsXunitV3Package);
        var bridge = names.Contains("MSTest.TestFramework") || names.Contains("MSTest") || names.Contains("NUnit");
        var capable = tunit || input.IsTestingPlatformApplication
            || input.EnableMSTestRunner && (names.Contains("MSTest.TestFramework") || names.Contains("MSTest"))
            || input.EnableNUnitRunner && names.Contains("NUnit")
            || input.UseMicrosoftTestingPlatformRunner && xunit;
        if (usesNativeMtp && !capable)
        {
            throw new PublicInvalidOperationException("The authorized global.json selects Microsoft.Testing.Platform, but a selected project's evaluated configuration does not enable it. "
                + "Enable the framework's native MTP runner or use projectName to target a compatible project; do not mix a VSTest-only project into this invocation.");
        }

        string[]? knownTests = null;
        string[]? xunitMethods = null;
        if (usesNativeMtp && !string.IsNullOrWhiteSpace(filter))
        {
            if (tunit || xunit)
            {
                TestDiscoveryDto discovery = await _testDiscoveryService.DiscoverTestsAsync(workspaceId, ct).ConfigureAwait(false);
                knownTests = [.. discovery.TestProjects.Where(item => item.ProjectName == status.Name).SelectMany(item => item.Tests.Select(test => test.FullyQualifiedName))];
                if (tunit) TreeNodeFilterTranslator.ValidateShape(filter, knownTests);
                else xunitMethods = XunitMethods(filter, knownTests);
            }
            else if (!bridge)
            {
                throw new PublicInvalidOperationException("The selected native test framework has no supported translation for this filter. Use an unfiltered invocation or target a framework with an established filter protocol.");
            }
        }

        if (usesNativeMtp && !input.OutputType.Equals("Exe", StringComparison.OrdinalIgnoreCase) && !input.OutputType.Equals("WinExe", StringComparison.OrdinalIgnoreCase))
            throw new PublicInvalidOperationException("The selected native test project's evaluated output is not executable. Enable the framework's executable MTP runner, reload, and retry.");

        return (knownTests, xunitMethods);
    }

    private sealed class ExecutionDependencySnapshot(MsBuildMetadataHelper.RestoreInputs root, JsonElement assets,
        ImmutableArray<DotnetConfigurationIdentity> inputs, InstalledPackageAuthority packages)
    {
        internal ImmutableArray<DotnetConfigurationIdentity> Inputs { get; } = inputs;
        internal InstalledPackageAuthority Packages { get; } = packages;
        internal NuGet.Versioning.NuGetVersion? Version(string package) => ProjectMetadataParser.ReadResolvedPackageVersion(root, assets, package);
    }

    private async Task<ExecutionDependencySnapshot> CaptureExecutionInputsAsync(string workspaceId,
        RestoreInvocationPlanner.SelectedGraph graph, bool requiresNativeAssets, CancellationToken ct)
    {
        EvaluatedBuildGraph.Node node = graph.Root;
        IReadOnlyList<EvaluatedBuildGraph.Node> nodes = graph.Nodes;
        var assets = nodes.Select(item => item.Inputs.AssetsPath).OfType<string>().Distinct(FileSystemPath.Comparer).ToArray();
        await _pathPolicy.ValidateAsync(workspaceId, assets, ct).ConfigureAwait(false);
        ImmutableArray<DotnetConfigurationIdentity>.Builder result = ImmutableArray.CreateBuilder<DotnetConfigurationIdentity>();
        var documents = new Dictionary<string, JsonElement>(FileSystemPath.Comparer);
        var packages = new HashSet<string>(FileSystemPath.Comparer);
        var proofs = new List<InstalledPackageAuthority>();
        foreach (var path in assets)
        {
            await _pathPolicy.ValidateAsync(workspaceId, [path], ct).ConfigureAwait(false);
            WorkspaceManager.EnsurePhysicalPath(path);
            byte[] bytes;
            try { bytes = await PhysicalInputReader.ReadAllBytesAsync(path, ct).ConfigureAwait(false); }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
            {
                throw new PublicInvalidOperationException("The test project's restored assets could not be read. Restore the project and prepare the invocation again.");
            }
            try
            {
                using var document = JsonDocument.Parse(bytes);
                InstalledPackageAuthority proof = await BuildInputProvenance.ReadPackagesAsync(path, bytes, nodes.Select(item => Path.GetDirectoryName(item.ProjectPath)!).ToArray(), (paths, token) => _pathPolicy.ValidateAsync(workspaceId, paths, token), ct).ConfigureAwait(false);
                packages.UnionWith(proof.Files);
                proofs.Add(proof);
                documents.Add(path, document.RootElement.Clone());
            }
            catch (Exception failure) when (!WorkspacePathPolicyRefusal.IsDenied(failure)
                && (failure is JsonException or ArgumentException))
            {
                throw new PublicInvalidOperationException("The test project's restored assets do not establish valid dependency evidence. Restore and prepare the invocation again.");
            }
            result.Add(new(path, true, [.. SHA256.HashData(bytes)]));
        }

        var authored = nodes.Select(item => Path.GetDirectoryName(item.ProjectPath)!).Distinct(FileSystemPath.Comparer).ToArray();
        var provenance = new ImmutablePathProvenance(nodes.SelectMany(item => item.BuildInputs.ImmutableRoots), packages, authored);
        var paths = nodes.SelectMany(item => item.BuildInputs.Definitions).Where(path => !provenance.IsImmutable(path))
            .Concat(nodes.Select(item => item.ProjectPath)).Distinct(FileSystemPath.Comparer).ToArray();
        await _pathPolicy.ValidateAsync(workspaceId, paths, ct).ConfigureAwait(false);
        foreach (var path in paths)
        {
            PhysicalInputIdentity input = await PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false);
            result.Add(new(input.FilePath, input.Exists, input.ContentHash));
        }
        var hasRoot = node.Inputs.AssetsPath is { } rootPath && documents.ContainsKey(rootPath);
        if (requiresNativeAssets && !hasRoot)
            throw new PublicInvalidOperationException("The test project's evaluated context has no restored assets authority.");
        return new(node.Inputs, hasRoot ? documents[node.Inputs.AssetsPath!] : default, result.ToImmutable(), InstalledPackageAuthority.Merge(proofs));
    }

    private static string[] XunitMethods(string filter, IReadOnlyList<string> known)
    {
        if (filter.IndexOfAny(['&', '(', ')']) >= 0)
            throw new PublicInvalidOperationException("This xUnit native filter translator supports FullyQualifiedName equality or contains atoms joined by OR.");
        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var atom in filter.Split('|'))
        {
            (string? ns, string? type, string? method) = TreeNodeFilterTranslator.ParseAtom(atom, filter);
            var name = ns is null ? type + "." + method : ns + "." + type + "." + method;
            var matches = known.Where(candidate => atom.Contains('~') ? candidate.Contains(name, StringComparison.OrdinalIgnoreCase)
                : candidate.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 0)
                throw new PublicInvalidOperationException("The xUnit native filter does not identify a discovered test method. Discover tests and supply an exact FullyQualifiedName.");
            selected.UnionWith(matches);
        }
        return [.. selected.Order(StringComparer.Ordinal)];
    }
}
