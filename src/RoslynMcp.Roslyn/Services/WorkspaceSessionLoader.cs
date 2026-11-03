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
    internal WorkspaceResourceDisposer? ResourceDisposerForTesting { get; set; }
    internal Action<ForkAdmissionExecution?>? AdmissionDisposalForTesting { get; set; }

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
    public virtual async Task<(MSBuildWorkspace Workspace, AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease Lease)> CreateAndOpenAsync(
        string workspaceId,
        string path,
        IDictionary<string, string>? globalProperties,
        WorkspaceDiagnosticsSink diagnostics,
        ILogger logger,
        CancellationToken ct)
    {
        MsBuildInitializer.EnsureInitialized();
        var forkConfig = ForkAdmissionMaterial.Find(path);
        var admissionExecution = forkConfig is null ? null : await ForkAdmissionExecution.CreateAsync(forkConfig, null, ct).ConfigureAwait(false);
        MSBuildWorkspace? workspace = null;
        AnalyzerReferenceIsolation.AnalyzerShadowLoaderLease? analyzerLease = null;
        var returningWorkspace = false;
        var nativeStarted = false;
        var disposer = ResourceDisposerForTesting ?? WorkspaceResourceDisposer.Default;
        List<Exception> CleanupOwnedResources() => WorkspaceResourceDisposer.DisposeOrdered(
            () =>
            {
                var abandoned = workspace;
                workspace = null;
                disposer.DisposeWorkspace(abandoned);
            },
            () =>
            {
                var abandoned = analyzerLease;
                analyzerLease = null;
                disposer.DisposeLease(abandoned);
            });
        try
        {
            var effectiveProperties = admissionExecution?.Properties(globalProperties) ?? globalProperties;
            // selfhosted-shadow-copy-analyzer-reference-test-fails: when caller passes MSBuild
            // global properties (e.g. Configuration=Release), thread them into MSBuildWorkspace
            // so ProjectReference OutputItemType="Analyzer" entries resolve against the right
            // bin/<config>/ tree. Without this, the default Configuration=Debug evaluation drops
            // analyzer references on Release-only checkouts (CI runners) because the Debug
            // TargetPath does not exist on disk.
            workspace = (effectiveProperties is { Count: > 0 })
                ? MSBuildWorkspace.Create(effectiveProperties)
                : MSBuildWorkspace.Create();
            var nativeFailureCount = 0;
            if (admissionExecution is not null)
                workspace.RegisterWorkspaceFailedHandler(args =>
                {
                    if (args.Diagnostic.Kind == Microsoft.CodeAnalysis.WorkspaceDiagnosticKind.Failure
                        && !WorkspaceDiagnosticSeverityClassifier.IsKnownSdkWarning(args.Diagnostic.Message))
                        Interlocked.Increment(ref nativeFailureCount);
                });
            if (globalProperties is { Count: > 0 })
            {
                logger.LogInformation(
                    "Workspace {WorkspaceId}: created MSBuildWorkspace with {Count} global property override(s): {Properties}",
                    workspaceId,
                    globalProperties.Count,
                    string.Join(", ", globalProperties.Select(kv => $"{kv.Key}={kv.Value}")));
            }

            diagnostics.AttachTo(workspace, workspaceId, logger);

            if (path.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            {
                nativeStarted = true;
                await workspace.OpenSolutionAsync(path, cancellationToken: ct).ConfigureAwait(false);
            }
            else if (path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                nativeStarted = true;
                await workspace.OpenProjectAsync(path, cancellationToken: ct).ConfigureAwait(false);
            }
            else
            {
                throw new PublicArgumentException(SupportedPathCorrection, nameof(path));
            }

            admissionExecution?.Complete();
            admissionExecution?.ThrowIfRefused();
            if (admissionExecution is not null && Volatile.Read(ref nativeFailureCount) != 0)
                throw new PublicInvalidOperationException(
                    "The retained fork could not be loaded without native build errors. Correct the project build errors and retry.",
                    InvalidOperationErrors.Internal("The native fork load reported an invocation-local failure."));

            analyzerLease = AnalyzerReferenceIsolation.RetargetFileReferencesToShadowLoader(
                workspace.CurrentSolution,
                workspaceId,
                logger);
            if (analyzerLease.RetargetedReferenceCount > 0)
            {
                logger.LogInformation(
                    "Workspace {WorkspaceId}: retargeted {Count} analyzer reference(s) to shadow-copy loaders.",
                    workspaceId,
                    analyzerLease.RetargetedReferenceCount);
            }

            returningWorkspace = true;
            return (workspace, analyzerLease);
        }
        catch
        {
            // Partial-load failure: dispose the half-initialized workspace (and any analyzer
            // shadow lease created before the throw — lease after workspace, so Roslyn has
            // released the analyzer references first) before propagating, so the caller does
            // not have to track ownership of a return-value-not-yet-handed-out.
            if (!nativeStarted)
            {
                try { admissionExecution?.Complete(); }
                catch (Exception completionFailure)
                {
                    logger.LogError(completionFailure, "Fork admission completion after failed workspace load failed");
                }
            }
            var cleanupFailures = CleanupOwnedResources();
            if (cleanupFailures.Count > 0)
                logger.LogError(new AggregateException(cleanupFailures),
                    "Disposing resources after failed workspace load failed; the original load failure is preserved");
            throw;
        }
        finally
        {
            Exception? admissionCleanupFailure = null;
            try
            {
                if (AdmissionDisposalForTesting is { } disposeAdmission)
                    disposeAdmission(admissionExecution);
                else
                    admissionExecution?.Dispose();
            }
            catch (Exception failure) { admissionCleanupFailure = failure; }
            if (admissionCleanupFailure is not null)
                logger.LogError(admissionCleanupFailure,
                    "Disposing fork admission execution after workspace load failed");
            if (admissionExecution?.CleanupWarning is { } warning)
                logger.LogWarning("{Warning}", warning);
            if (returningWorkspace && (admissionCleanupFailure is not null || admissionExecution?.CleanupWarning is not null))
            {
                var cleanupFailures = CleanupOwnedResources();
                if (cleanupFailures.Count > 0)
                    logger.LogError(new AggregateException(cleanupFailures),
                        "Disposing resources after fork admission cleanup failed");
                if (admissionCleanupFailure is not null) throw admissionCleanupFailure;
                throw InvalidOperationErrors.Internal(admissionExecution!.CleanupWarning!);
            }
        }
    }
}
