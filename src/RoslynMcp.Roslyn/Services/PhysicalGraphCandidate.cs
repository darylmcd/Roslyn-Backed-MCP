using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Owns a read-only native graph candidate without publishing or applying workspace changes.</summary>
internal sealed class PhysicalGraphCandidate : IDisposable
{
    private readonly IDisposable _workspace;
    private readonly IDisposable _lease;
    private readonly MsBuildCommandProperties.Redactor _redactor;
    private bool _disposed;
    private PhysicalGraphCandidate(Solution solution, IDisposable workspace, IDisposable lease,
        MsBuildCommandProperties.Redactor redactor, NativeCompilerContextCertificate context,
        NativeSourceAdmission.SourceAdmissionReceipt? sourceAdmission)
    {
        Solution = solution;
        _workspace = workspace;
        _lease = lease;
        _redactor = redactor;
        Context = context;
        _sourceAdmission = sourceAdmission;
    }
    internal Solution Solution { get; }
    internal NativeCompilerContextCertificate Context { get; }
    private readonly NativeSourceAdmission.SourceAdmissionReceipt? _sourceAdmission;
    internal Task AssertSourceCurrentAsync(PhysicalMutationRecovery operation, IWorkspacePathPolicy policy,
        IReadOnlyList<string> sourceCoordinates, CancellationToken ct)
    {
        if (_disposed) throw new WorkspaceMutationOwnershipException("The source candidate is already released.");
        return (_sourceAdmission ?? throw new WorkspaceMutationOwnershipException("The candidate has no issued source admission."))
            .AssertCurrentAsync(operation, Solution, Context, policy, sourceCoordinates, ct);
    }
    internal static async Task<PhysicalGraphCandidate> LoadAsync(string id, string path, IDictionary<string, string> globals,
        IWorkspacePathPolicy pathPolicy, WorkspaceSessionLoader loader, ILogger logger, NativeSourceAdmission? sourceAdmission, CancellationToken ct)
    {
        ForkInvocationPathPolicy.AssertPolicyReceiver(pathPolicy);
        var redactor = new MsBuildCommandProperties.Redactor(MsBuildCommandProperties.Append([], new Dictionary<string, string>(globals)));
        var loaded = await loader.CreateAndOpenAsync(id, path, globals, pathPolicy, new WorkspaceDiagnosticsSink(256), logger, SupplementalPackageReadAuthority.None, sourceAdmission, ct).ConfigureAwait(false);
        try
        {
            var projection = await EvaluatedProjectGraph.WithPersistedReferenceMetadataAsync(id, loaded.Context, pathPolicy, ct)
                .ConfigureAwait(false);
            return new(projection.Solution, loaded.Workspace, loaded.Lease, redactor, loaded.Context, loaded.SourceAdmission);
        }
        catch (Exception failure)
        {
            try { OwnedResourceCleanup.Dispose("Native graph candidate loading and cleanup failed.", failure, loaded.Workspace, loaded.Lease); }
            catch (Exception cleanup) { throw redactor.Failure(cleanup); }
            throw redactor.Failure(failure);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { OwnedResourceCleanup.Dispose("Failed to release native graph candidate resources.", null, _workspace, _lease); }
        catch (Exception failure) { throw _redactor.Failure(failure); }
    }
}
