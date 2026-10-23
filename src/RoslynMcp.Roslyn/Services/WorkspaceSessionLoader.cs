using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// Creates a fresh <see cref="MSBuildWorkspace"/> with optional MSBuild global-property
/// overrides, attaches a <see cref="WorkspaceDiagnosticsSink"/> for workspace-failed events,
/// opens the requested solution or project, and retargets file-based analyzer references onto
/// the shadow-copy loader. Extracted from <see cref="WorkspaceManager.LoadIntoSessionAsync"/>
/// so the per-load workspace-construction concern is independently testable and the
/// manager retains the atomic swap-on-success pattern that
/// <c>autoreload-cascade-stdio-host-crash</c> depends on (readers always observe either the
/// prior loaded workspace OR the fully initialized new workspace, never a half-initialized
/// session).
/// </summary>
/// <remarks>
/// Not sealed so <c>WorkspaceSessionLoaderFailureTests</c> can derive a test double that
/// simulates a partial-load failure without touching disk. <c>InternalsVisibleTo</c> on
/// <c>RoslynMcp.Roslyn</c> exposes the type to the test assembly.
/// </remarks>
internal class WorkspaceSessionLoader
{
    internal const string SupportedPathCorrection = "Path must identify a solution (.sln/.slnx) or C# project (.csproj).";

    /// <summary>
    /// Creates a new <see cref="MSBuildWorkspace"/>, attaches the diagnostics sink, opens
    /// the supplied solution or project path, and retargets file-based analyzer references
    /// onto per-lease shadow-copy loaders. The caller owns disposal of the returned workspace
    /// AND analyzer-shadow lease on both success (transferred to the session) and failure
    /// (the manager's finally block disposes the partial workspace/lease if this method throws
    /// after returning, but the throw-before-return case is the loader's responsibility). The
    /// lease is never null: platforms/configurations where isolation cannot run get a no-op
    /// empty lease (analyzer-shadow-loader-lifecycle).
    /// </summary>
    public virtual async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease,
        NativeCompilerContextCertificate Context, NativeSourceAdmission.SourceAdmissionReceipt? SourceAdmission)> CreateAndOpenAsync(
        string workspaceId,
        string path,
        IDictionary<string, string>? globalProperties,
        IWorkspacePathPolicy pathPolicy,
        WorkspaceDiagnosticsSink diagnostics,
        ILogger logger,
        SupplementalPackageReadAuthority supplementalPackages,
        NativeSourceAdmission? sourceAdmission, CancellationToken ct)
    {
        ForkInvocationPathPolicy.AssertPolicyReceiver(pathPolicy);
        MsBuildInitializer.EnsureInitialized();
        // selfhosted-shadow-copy-analyzer-reference-test-fails: when caller passes MSBuild
        // global properties (e.g. Configuration=Release), thread them into MSBuildWorkspace
        // so ProjectReference OutputItemType="Analyzer" entries resolve against the right
        // bin/<config>/ tree. Without this, the default Configuration=Debug evaluation drops
        // analyzer references on Release-only checkouts (CI runners) because the Debug
        // TargetPath does not exist on disk.
        var workspace = (globalProperties is { Count: > 0 })
            ? MSBuildWorkspace.Create(globalProperties)
            : MSBuildWorkspace.Create();
        var redactor = new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], workspace.Properties));
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? analyzerLease = null;
        try
        {
            if (!path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                throw new PublicArgumentException(SupportedPathCorrection, nameof(path));
            var epoch = await NativeCompilerContextCertificate.Epoch.CaptureAsync(workspaceId, path,
                new Dictionary<string, string>(workspace.Properties), pathPolicy, supplementalPackages, ct).ConfigureAwait(false);
            if (sourceAdmission is not null)
                await sourceAdmission.AssertInitialAsync(workspaceId, epoch, pathPolicy, ct).ConfigureAwait(false);
            // One private producer phase precedes the authoritative epoch. It may
            // create only declared outputs; authored/dependency inputs remain frozen.
            await epoch.PrepareOutputsAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
            var preparationEpoch = epoch;
            var entryProperties = new Dictionary<string, string>(workspace.Properties);
            epoch = await NativeCompilerContextCertificate.Epoch.CaptureAsync(workspaceId, path,
                entryProperties, pathPolicy, supplementalPackages, ct).ConfigureAwait(false);
            epoch = preparationEpoch.ReconcileRecaptured(epoch, ct);
            var candidateDiagnostics = diagnostics.CreateCandidate();
            candidateDiagnostics.AttachTo(workspace, workspaceId, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);
            await OpenNativeAsync(workspace, path, ct).ConfigureAwait(false);
            await preparationEpoch.AssertPreparationCurrentAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
            var afterOpen = await NativeCompilerContextCertificate.Epoch.CaptureAsync(workspaceId, path,
                entryProperties, pathPolicy, supplementalPackages, ct).ConfigureAwait(false);
            afterOpen = preparationEpoch.ReconcileRecaptured(afterOpen, ct);
            if (epoch.IsEquivalentAdmission(afterOpen, ct))
            {
                // The first native snapshot spans one unchanged complete admission.
                // Retain its workspace and diagnostics; no second Open is necessary.
                candidateDiagnostics.ForwardTo(diagnostics, workspaceId, logger);
            }
            else
            {
                // A permitted preparation transition requires one strict final Open.
                // Failures never select this branch, and there is no retry loop.
                var discarded = workspace;
                workspace = globalProperties is { Count: > 0 }
                    ? MSBuildWorkspace.Create(globalProperties) : MSBuildWorkspace.Create();
                OwnedResourceCleanup.Dispose("Native output preparation workspace cleanup failed.", null, discarded);
                epoch = afterOpen;
                diagnostics.AttachTo(workspace, workspaceId, logger);
                await OpenNativeAsync(workspace, path, ct).ConfigureAwait(false);
            }
            if (globalProperties is { Count: > 0 })
            {
                logger.LogInformation(
                    "Workspace {WorkspaceId}: created MSBuildWorkspace with {Count} global property override(s).",
                    workspaceId,
                    globalProperties.Count);
            }

            await preparationEpoch.AssertPreparationCurrentAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
            var nativeOriginal = workspace.CurrentSolution;
            var context = await epoch.CompleteAsync(workspaceId, nativeOriginal, pathPolicy, ct).ConfigureAwait(false);
            context = await context.CaptureAnalyzerInputsAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
            // Establish compiler-context availability before installing any product
            // analyzer loader. The manager consumes this same issued projection.
            await context.PrepareProjectionAsync(workspaceId, pathPolicy, ct).ConfigureAwait(false);
            analyzerLease = await context.InstallAnalyzerIsolationAsync(workspaceId, pathPolicy, logger, ct).ConfigureAwait(false);
            if (analyzerLease.RetargetedReferenceCount > 0)
            {
                logger.LogInformation(
                    "Workspace {WorkspaceId}: retargeted {Count} analyzer reference(s) to shadow-copy loaders.",
                    workspaceId,
                    analyzerLease.RetargetedReferenceCount);
            }

            var admissionReceipt = sourceAdmission is null ? null
                : await sourceAdmission.IssueAsync(workspaceId, epoch, context, pathPolicy, ct).ConfigureAwait(false);
            return (workspace, analyzerLease, context, admissionReceipt);
        }
        catch (Exception failure)
        {
            // Partial-load failure: dispose the half-initialized workspace (and any analyzer
            // shadow lease created before the throw — lease after workspace, so Roslyn has
            // released the analyzer references first) before propagating, so the caller does
            // not have to track ownership of a return-value-not-yet-handed-out.
            var failures = new List<Exception> { failure };
            try { workspace.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            try { analyzerLease?.Dispose(); } catch (Exception cleanup) { failures.Add(cleanup); }
            if (failures.Count > 1)
                throw redactor.Failure(new AggregateException("Workspace load cleanup failed.", failures));
            if (redactor.HasValues) throw redactor.Failure(failure);
            throw;
        }
    }
    protected virtual async Task OpenNativeAsync(MSBuildWorkspace workspace, string path, CancellationToken ct)
    {
        if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            await workspace.OpenProjectAsync(path, cancellationToken: ct).ConfigureAwait(false);
        else
            await workspace.OpenSolutionAsync(path, cancellationToken: ct).ConfigureAwait(false);
    }
}
