using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

public sealed class CompositePreviewStore : BoundedStore<CompositePreviewStore.Entry>, ICompositePreviewStore
{
    private readonly PersistentCompositeStorage? _diskBackend;
    private readonly CompositeWorkspaceIdentityResolver? _identityResolver;

    public CompositePreviewStore(int maxEntries = 20, TimeSpan ttl = default) : base(maxEntries, ttl)
    {
        _diskBackend = null;
        _identityResolver = null;
    }

    /// <summary>
    /// Item 6 (v1.18): construct with disk persistence. Activated by the host when
    /// <see cref="PreviewStoreOptions.PersistDirectory"/> is set so cross-process clients can
    /// redeem tokens. The resolver is required: a persisted record is bound to a solution path
    /// (ADR 0032), and only the resolver can map it onto a session of the redeeming host.
    /// </summary>
    public CompositePreviewStore(
        int maxEntries,
        TimeSpan ttl,
        PersistentCompositeStorage diskBackend,
        CompositeWorkspaceIdentityResolver identityResolver) : base(maxEntries, ttl)
    {
        _diskBackend = diskBackend ?? throw new ArgumentNullException(nameof(diskBackend));
        _identityResolver = identityResolver ?? throw new ArgumentNullException(nameof(identityResolver));
    }

    public string Store(string workspaceId, int workspaceVersion, string description, IReadOnlyList<CompositeFileMutation> mutations)
    {
        // Resolve the portable identity before anything is stored: a session that cannot be
        // identified must not leave a half-persisted token behind.
        var identity = _identityResolver?.IdentityOf(workspaceId);
        // ADR 0033: fingerprint the targets as they are NOW, so a redemption can tell a file edited in
        // between from one it is about to write. Captured before StoreEntry for the same reason as the
        // identity: a preview whose targets cannot be read must not leave a token behind.
        var fingerprints = CompositeSourceFingerprint.Capture(mutations);
        var entry = new Entry(workspaceId, workspaceVersion, description, mutations, DateTime.UtcNow, identity, fingerprints);
        var token = StoreEntry(entry);
        // Write-through to disk so a separate process can redeem this token immediately.
        _diskBackend?.Write(token, entry);
        return token;
    }

    /// <inheritdoc />
    /// <remarks>
    /// With a disk backend the record is claimed only after the redeeming host's session has been
    /// resolved and, when <paramref name="gatedWorkspaceId"/> is given, found to still be the session
    /// the caller took its write gate on. Either refusal happens before the payload is moved, so the
    /// one-time token survives. The returned workspace id is the LOCAL session id. A target file that
    /// changed on disk since <see cref="Store"/> (ADR 0033) is refused with
    /// <see cref="PreviewSourceChangedException"/> the same way: before the payload is moved on the
    /// persisted path, and before the entry is returned on the in-memory path.
    /// </remarks>
    public (string WorkspaceId, int WorkspaceVersion, string Description, IReadOnlyList<CompositeFileMutation> Mutations)? Retrieve(
        string token,
        string? gatedWorkspaceId)
    {
        Entry? entry;
        if (_diskBackend is not null)
        {
            // The persistent record is the shared one-time authority. Never fall back to the
            // creator process's in-memory copy after another host wins the disk claim.
            entry = _diskBackend.TryClaim(token, record => AdmitRecord(token, record, gatedWorkspaceId));
            if (entry is not null)
            {
                base.Invalidate(token);
            }
        }
        else
        {
            // RetrieveEntry does not consume: the orchestrator invalidates after a successful apply, so
            // a drift refusal here leaves the token redeemable once the file matches the preview again.
            entry = RetrieveEntry(token);
            if (entry is not null)
            {
                ThrowIfSourceChanged(token, entry);
            }
        }

        if (entry is null) return null;
        return (entry.WorkspaceId, entry.WorkspaceVersion, entry.Description, entry.Mutations);
    }

    private string AdmitRecord(string token, Entry record, string? gatedWorkspaceId)
    {
        // Runs under the claim lock and before the payload is moved. Never take the BoundedStore
        // gate here: the resolver talks to the workspace manager only.
        var local = _identityResolver!.Resolve(record);
        if (gatedWorkspaceId is not null && !string.Equals(local, gatedWorkspaceId, StringComparison.Ordinal))
        {
            throw new PreviewTokenStaleException(
                token,
                $"Preview token '{token}' was not consumed: the workspace sessions on this host changed between validation and redemption. Retry apply_composite.");
        }

        ThrowIfSourceChanged(token, record);
        return local;
    }

    /// <summary>
    /// A record without fingerprints (format 1 or 2, written before ADR 0033) is redeemed unchecked.
    /// </summary>
    private static void ThrowIfSourceChanged(string token, Entry entry)
    {
        if (entry.SourceFingerprints is null)
        {
            return;
        }

        var changed = CompositeSourceFingerprint.FindDrift(entry.SourceFingerprints, entry.Mutations);
        if (changed is not null)
        {
            throw new PreviewSourceChangedException(token, changed);
        }
    }

    /// <summary>
    /// With a disk backend the persisted record decides: the answer is the local session that would
    /// redeem it, and a record no local session can redeem is refused with the specific reason
    /// (not loaded, ambiguous, creator not live) instead of a generic stale-token message.
    /// </summary>
    public new string? PeekWorkspaceId(string token)
    {
        if (_diskBackend is null)
        {
            return base.PeekWorkspaceId(token);
        }

        var record = _diskBackend.TryPeek(token);
        return record is null ? null : _identityResolver!.Resolve(record);
    }

    public IReadOnlyList<string>? PeekChangedPaths(string token)
    {
        // With a disk backend the persisted record is the authority Retrieve claims, and it sits in
        // a directory other hosts (or anything with write access to it) can edit: the creator's
        // in-memory copy is not what would be written, so it must not stand in for it. A missing,
        // already-claimed or unreadable record is "unknown", which the caller refuses.
        var entry = _diskBackend is not null ? _diskBackend.TryPeek(token) : RetrieveEntry(token);
        return entry?.Mutations.Select(m => m.FilePath).ToArray();
    }

    public new void Invalidate(string token)
    {
        base.Invalidate(token);
        _diskBackend?.Delete(token);
    }

    /// <param name="WorkspaceIdentity">
    /// The normalized solution/project path of the creating session (ADR 0032); the portable key a
    /// redeeming host matches against its own sessions. <see langword="null"/> for in-memory entries
    /// and for format-1 records, which carried only the creator's session id.
    /// </param>
    /// <param name="SourceFingerprints">
    /// The state of every target file when the preview was stored (ADR 0033), checked at redemption.
    /// <see langword="null"/> only for format 1 and 2 records, which predate it and redeem unchecked.
    /// </param>
    public sealed record Entry(
        string WorkspaceId,
        int WorkspaceVersion,
        string Description,
        IReadOnlyList<CompositeFileMutation> Mutations,
        DateTime CreatedAt,
        string? WorkspaceIdentity,
        IReadOnlyList<CompositeSourceFingerprint>? SourceFingerprints) : IBoundedStoreEntry;
}
