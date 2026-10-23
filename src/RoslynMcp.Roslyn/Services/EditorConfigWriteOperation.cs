using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>One config write bound to its actual recovery operation and held source transitions.</summary>
internal sealed class EditorConfigWriteOperation
{
    private readonly PhysicalMutationRecovery _operation;
    private readonly IWorkspaceRecoveryAuthority _authority;
    private readonly IWorkspaceMutationLease _lease;
    private readonly FileSnapshotDto _before;
    private readonly IPhysicalRestorationAuthority? _sourceAdmission;
    private readonly byte[]? _originalBytes;
    private readonly byte[] _desiredBytes;

    internal EditorConfigWriteOperation(PhysicalMutationRecovery operation, IWorkspaceRecoveryAuthority authority,
        IWorkspaceMutationLease lease, FileSnapshotDto before, byte[] desiredBytes, IPhysicalRestorationAuthority? sourceAdmission)
    {
        _operation = operation;
        _authority = authority;
        _lease = lease;
        _before = before;
        _sourceAdmission = sourceAdmission;
        _originalBytes = before.OriginalText is null ? null : before.OriginalBytes.ToArray();
        _desiredBytes = desiredBytes.ToArray();
        authority.AssertKnownFileRead(operation, before);
    }

    internal string WorkspaceId => _operation.WorkspaceId;
    internal string Path => _before.FilePath;
    internal byte[]? OriginalBytes => _originalBytes?.ToArray();
    internal byte[] DesiredBytes => _desiredBytes.ToArray();
    internal bool Applied { get; private set; }
    internal bool HasRetainedTransitions => _operation.HasRetainedSourceTransitions;

    internal async Task AuthorizeCurrentAsync(CancellationToken ct)
    {
        await _authority.AuthorizePathsAsync(WorkspaceId, [Path], ct).ConfigureAwait(false);
        _authority.AssertKnownFileRead(_operation, _before);
    }

    internal byte[]? ReadCurrentPhysical()
    {
        _authority.AssertKnownFileRead(_operation, _before);
        byte[]? bytes;
        try { bytes = PhysicalInputReader.ReadAllBytes(Path); }
        catch (FileNotFoundException) { bytes = null; }
        catch (DirectoryNotFoundException) { bytes = null; }
        _authority.AssertKnownFileRead(_operation, _before);
        return bytes;
    }

    internal async Task ApplyAsync(CancellationToken ct)
    {
        if (Applied) throw InvalidOperationErrors.Internal("The config operation has already applied its intended bytes.");
        _lease.BeforeWrite(FileSnapshotDto.FromExistingBytes(Path, _desiredBytes));
        await _operation.TransitionSourceAsync(Path, _originalBytes, _desiredBytes,
            PhysicalSourceTransitionMode.PreserveExistingObject, _authority, _sourceAdmission, ct).ConfigureAwait(false);
        Applied = true;
    }

    internal async Task CompensateAsync(CancellationToken ct)
    {
        if (!Applied) throw InvalidOperationErrors.Internal("A config inverse requires its completed forward transition.");
        await _operation.TransitionSourceAsync(Path, _desiredBytes, _originalBytes,
            PhysicalSourceTransitionMode.PreserveExistingObject, _authority, null, ct).ConfigureAwait(false);
        Applied = false;
    }
}
