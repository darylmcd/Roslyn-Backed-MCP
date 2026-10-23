using Microsoft.Win32.SafeHandles;
using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Helpers;

internal enum PhysicalSourceTransitionMode { FreshReplacement, PreserveExistingObject }

/// <summary>Captures the named source before validating and publishing a no-replace transition.</summary>
internal sealed class PhysicalSourceTransition : IDisposable
{
    private readonly PhysicalDirectoryScope _parent;
    private readonly PhysicalDirectoryScope _custodyParent;
    private readonly PhysicalSourceCustody _custodyAuthority;
    private readonly PhysicalDirectoryScope _custody;
    private readonly string _scopeName;
    private readonly string _sourceName;
    private SafeFileHandle? _captured;
    private SafeFileHandle? _preparedHandle;
    private bool _displaced;
    private bool _prepared;
    private bool _preparedInParent;
    private bool _preparedAdmitted;
    private string? _temporaryName;
    private byte[]? _restoreContent;
    private bool _released;
    private readonly Action<string> _namespaceRetained;
    internal string SourcePath { get; }
    internal string CustodyPath => _custody.Path;
    internal bool RequiresRecovery => _displaced || _prepared;

    private PhysicalSourceTransition(string sourcePath, PhysicalDirectoryScope parent,
        PhysicalSourceCustody custodyAuthority, PhysicalDirectoryScope custody, string scopeName, Action<string> namespaceRetained)
    {
        SourcePath = sourcePath;
        _sourceName = Path.GetFileName(sourcePath);
        _parent = parent;
        _custodyAuthority = custodyAuthority;
        _custodyParent = custodyAuthority.Parent;
        _custody = custody;
        _scopeName = scopeName;
        _namespaceRetained = namespaceRetained;
    }

    internal static async Task ApplyAsync(string path, byte[]? expected, byte[]? desired, PhysicalSourceTransitionMode mode,
        Func<PhysicalDirectoryScope, string, string, CancellationToken, Task<PhysicalSourceCustody>> acquireCustody,
        Func<IReadOnlyList<string>, CancellationToken, Task> authorize,
        Action<PhysicalSourceTransition> retain, Action<string> namespaceRetained, Func<string, CancellationToken, PhysicalDirectoryScope> acquireParent,
        Services.PhysicalSourceAdmission? sourceAdmission, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        var parentPath = Path.GetDirectoryName(path) ?? throw new IOException("A source transition requires a parent directory.");
        var scopeName = ".rmcp-transition-" + Guid.NewGuid().ToString("N");
        var temporaryName = ".rmcp-prepared-" + Guid.NewGuid().ToString("N");
        // Grant source coordinates before acquisition. Administrative custody admission
        // has its own held receipt and is followed by a fresh source comparison.
        await authorize([path, parentPath, Path.Combine(parentPath, temporaryName)], ct).ConfigureAwait(false);
        Services.WorkspaceManager.EnsurePhysicalPath(path);
        if (expected is null && desired is null)
        {
            var current = await Services.PhysicalInputIdentity.CaptureAsync(path, ct).ConfigureAwait(false);
            if (current.Exists) throw Conflict();
            return;
        }
        // This records a possible creation effect, never ownership for directory deletion.
        // If acquisition fails without changing the namespace, native abort proof clears it.
        if (!Directory.Exists(parentPath)) namespaceRetained(parentPath);
        var parent = acquireParent(parentPath, ct);
        PhysicalSourceCustody? custodyAuthority = null;
        PhysicalDirectoryScope? acquiredCustody = null;
        PhysicalSourceTransition? transition = null;
        var retained = false;
        Exception? primaryFailure = null;
        try
        {
            // Refuse callback drift before introducing a custody namespace.
            using (var initial = parent.OpenTransitionFileIfPresent(Path.GetFileName(path), writable: false, ct))
            {
                if (!await MatchesAsync(initial, expected, ct).ConfigureAwait(false)) throw Conflict();
                if (!SameBytes(expected, desired)) RefuseReadOnly(initial);
            }
            if (SameBytes(expected, desired)) return;
            custodyAuthority = await acquireCustody(parent, scopeName, temporaryName, ct).ConfigureAwait(false);
            custodyAuthority.AssertCurrent();
            var custodyParent = custodyAuthority.Parent;
            if (!ReferenceEquals(custodyAuthority.SourceParent, parent)
                || custodyAuthority.LeafName != scopeName || !parent.SameFilesystem(custodyParent))
                throw new IOException("Source custody requires the exact issued leaf on the source filesystem.");
            await custodyAuthority.AuthorizeAsync([path, parentPath, custodyParent.Path, custodyAuthority.Path,
                Path.Combine(parentPath, temporaryName)], ct).ConfigureAwait(false);
            if (sourceAdmission is not null)
                await sourceAdmission.AssertCurrentAsync([path, parentPath, Path.Combine(parentPath, temporaryName)], ct).ConfigureAwait(false);
            // Custody admission can invoke current policy. Recheck the selected source
            // after its final callback before introducing a leaf or moving any bytes.
            using (var currentSource = parent.OpenTransitionFileIfPresent(Path.GetFileName(path), writable: false, ct))
                if (!await MatchesAsync(currentSource, expected, ct).ConfigureAwait(false)) throw Conflict();
            custodyAuthority.AssertAdmissionCurrent();
            try
            {
                acquiredCustody = custodyParent.CreateOwnedChildScope(scopeName);
                custodyAuthority.BindLeaf(parent, acquiredCustody);
            }
            catch
            {
                // Native creation may succeed before acquiring its directory witness fails.
                // Record a possible namespace effect; this does not admit unknown contents.
                namespaceRetained(Path.Combine(custodyParent.Path, scopeName));
                throw;
            }
            transition = new(path, parent, custodyAuthority, acquiredCustody, scopeName, namespaceRetained);
            acquiredCustody = null; // Only an admitted leaf can enter automatic compensation.
            custodyAuthority = null; // Transfer the exact administrative receipt with the transition.
            transition._temporaryName = temporaryName;
            await transition.ApplyCoreAsync(expected, desired, mode, ct).ConfigureAwait(false);
            transition.ReleaseEmptyScope();
        }
        catch (Exception failure)
        {
            primaryFailure = failure;
            if (transition is not null)
            {
                try
                {
                    await transition.RestoreCapturedAsync().ConfigureAwait(false);
                    transition.ReleaseEmptyScope();
                }
                catch (Exception restoreFailure)
                {
                    retain(transition);
                    retained = true;
                    transition = null;
                    throw new AggregateException("A physical source transition failed and its captured entry requires recovery.", failure, restoreFailure);
                }
            }
            throw;
        }
        finally
        {
            OwnedResourceCleanup.DisposePreservingFailure("Source transition cleanup failed.", primaryFailure,
                transition is not null ? transition : retained ? null : parent, acquiredCustody, custodyAuthority);
        }
    }

    internal static PhysicalDirectoryScope OpenParent(string path, CancellationToken ct)
    {
        var ancestor = path;
        while (!Directory.Exists(ancestor))
            ancestor = Path.GetDirectoryName(ancestor) ?? throw new DirectoryNotFoundException("A source parent has no existing physical ancestor.");
        Services.WorkspaceManager.EnsurePhysicalPath(ancestor);
        var current = PhysicalDirectoryScope.OpenRoute(ancestor);
        try
        {
            var relative = Path.GetRelativePath(ancestor, path);
            foreach (var component in relative == "." ? [] : relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            {
                var next = current.OpenDirectoryForAccess(component, create: true, PhysicalDirectoryCreationMode.SourceDefault, ct)
                    ?? throw new IOException("A source parent could not be created physically.");
                current.Dispose();
                current = next;
            }
            return current;
        }
        catch { current.Dispose(); throw; }
    }

    private async Task ApplyCoreAsync(byte[]? expected, byte[]? desired, PhysicalSourceTransitionMode mode, CancellationToken ct)
    {
        _custodyAuthority.AssertCurrent();
        var preserve = mode == PhysicalSourceTransitionMode.PreserveExistingObject && expected is not null && desired is not null;
        if (desired is not null && !preserve)
        {
            // Inherit the same source-parent permissions as the original fresh writer.
            // A random name is not ownership: capture and validate it before admission.
            _preparedHandle = _parent.CreateTransitionFile(_temporaryName!, ct);
            _prepared = true;
            _preparedInParent = true;
            CapturePrepared(ct);
            if (!_custody.CurrentFileMatches("prepared", _preparedHandle)
                || !await MatchesAsync(_preparedHandle, [], ct).ConfigureAwait(false)) throw Conflict();
            _preparedAdmitted = true;
            await WriteContentAsync(_preparedHandle, desired, ct).ConfigureAwait(false);
        }
        _captured = _parent.OpenTransitionFileIfPresent(_sourceName, writable: preserve, ct);
        if (!await MatchesAsync(_captured, expected, ct).ConfigureAwait(false)) throw Conflict();
        RefuseReadOnly(_captured);
        if (_captured is not null)
        {
            _custodyAuthority.AssertCurrent();
            _parent.MoveFileNoReplace(_sourceName, _captured, _custody, "captured", ct);
            _displaced = true; // No cancellation or fallible work may precede this receipt.
            if (!_custody.CurrentFileMatches("captured", _captured)
                || !await MatchesAsync(_captured, expected, ct).ConfigureAwait(false)) throw Conflict();
        }
        if (desired is not null)
        {
            if (preserve)
            {
                // The issued original bytes are rollback authority before the first write.
                // Keep this inode, including its mode, alternate streams and hardlink effects.
                _restoreContent = expected!;
                await WriteContentAsync(_captured!, desired, ct).ConfigureAwait(false);
                if (!_custody.CurrentFileMatches("captured", _captured!)
                    || !await MatchesAsync(_captured, desired, ct).ConfigureAwait(false)) throw Conflict();
                _custodyAuthority.AssertCurrent();
                _custody.MoveFileNoReplace("captured", _captured, _parent, _sourceName, ct);
                _displaced = false;
                _restoreContent = null;
                if (!_parent.CurrentFileMatches(_sourceName, _captured!)) throw Conflict();
            }
            else
            {
                if (!_custody.CurrentFileMatches("prepared", _preparedHandle!)
                    || !await MatchesAsync(_preparedHandle, desired, ct).ConfigureAwait(false)) throw Conflict();
                _custodyAuthority.AssertCurrent();
                _custody.MoveFileNoReplace("prepared", _preparedHandle, _parent, _sourceName, ct);
                _prepared = false;
                if (!_parent.CurrentFileMatches(_sourceName, _preparedHandle!)) throw Conflict();
            }
        }
        if (_displaced)
        {
            _custodyAuthority.AssertCurrent();
            _custody.ReclaimAdmittedTransitionFile("captured", _captured!, CancellationToken.None);
            _displaced = false;
        }
        _captured?.Dispose();
        _captured = null;
        _preparedHandle?.Dispose();
        _preparedHandle = null;
    }

    // A captured unexpected object is never deleted. If a new source occupies the
    // destination, retain both entries and let the existing recovery owner resolve it.
    private async Task RestoreCapturedAsync()
    {
        _custodyAuthority.AssertCurrent();
        if (_displaced)
        {
            if (_restoreContent is not null)
            {
                if (!_custody.CurrentFileMatches("captured", _captured!)) throw Conflict();
                await WriteContentAsync(_captured!, _restoreContent, CancellationToken.None).ConfigureAwait(false);
                if (!await MatchesAsync(_captured, _restoreContent, CancellationToken.None).ConfigureAwait(false)) throw Conflict();
                _restoreContent = null;
            }
            _custodyAuthority.AssertCurrent();
            _custody.MoveFileNoReplace("captured", _captured, _parent, _sourceName, CancellationToken.None);
            _displaced = false;
        }
        _captured?.Dispose();
        _captured = null;
        if (_prepared)
        {
            CapturePrepared(CancellationToken.None);
            if (!_preparedAdmitted)
            {
                if (_custody.CurrentFileMatches("prepared", _preparedHandle!)
                    && await MatchesAsync(_preparedHandle, [], CancellationToken.None).ConfigureAwait(false))
                    _preparedAdmitted = true;
            }
            _custodyAuthority.AssertCurrent();
            if (_preparedAdmitted)
                _custody.ReclaimAdmittedTransitionFile("prepared", _preparedHandle!, CancellationToken.None);
            else
                _custody.MoveFileNoReplace("prepared", _preparedHandle, _parent, _temporaryName!, CancellationToken.None);
            _prepared = false;
        }
        _preparedHandle?.Dispose();
        _preparedHandle = null;
    }

    private void CapturePrepared(CancellationToken ct)
    {
        if (!_preparedInParent) return;
        _custodyAuthority.AssertCurrent();
        _parent.MoveFileNoReplace(_temporaryName!, _preparedHandle, _custody, "prepared", ct);
        _preparedInParent = false;
    }

    private async Task WriteContentAsync(SafeFileHandle file, byte[] bytes, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _custodyAuthority.AssertCurrent();
        RandomAccess.SetLength(file, 0);
        await RandomAccess.WriteAsync(file, bytes, 0, ct).ConfigureAwait(false);
        _custodyAuthority.AssertCurrent();
        RandomAccess.FlushToDisk(file);
    }

    private static void RefuseReadOnly(SafeFileHandle? file)
    {
        if (OperatingSystem.IsWindows() && file is not null && (File.GetAttributes(file) & FileAttributes.ReadOnly) != 0)
            throw new UnauthorizedAccessException("A read-only source cannot be changed by this operation.");
    }

    internal async Task ResolveAsync(Func<IReadOnlyList<string>, CancellationToken, Task> authorize, CancellationToken ct)
    {
        _custodyAuthority.AssertCurrent();
        await authorize([SourcePath, _parent.Path, Path.Combine(_parent.Path, _temporaryName!)], ct).ConfigureAwait(false);
        _custodyAuthority.AssertCurrent();
        await _custodyAuthority.AuthorizeAsync([SourcePath, _parent.Path, _custodyParent.Path, CustodyPath,
            Path.Combine(_parent.Path, _temporaryName!)], ct).ConfigureAwait(false);
        Services.WorkspaceManager.EnsurePhysicalPath(SourcePath);
        Services.WorkspaceManager.EnsurePhysicalPath(CustodyPath);
        ct.ThrowIfCancellationRequested();
        await RestoreCapturedAsync().ConfigureAwait(false);
        ReleaseEmptyScope();
        Dispose();
    }

    private void ReleaseEmptyScope()
    {
        if (_released) return;
        _custodyAuthority.AssertCurrent();
        if (_custody.Entries().Count != 0) throw new IOException("Source transition custody contains unowned entries and was retained.");
        _custodyAuthority.AssertCurrent();
        _custody.ReleaseNamespacePin();
        var outcome = _custodyParent.RemoveHeldChild(_scopeName, _custody, CancellationToken.None);
        if (outcome == PhysicalDirectoryCleanupOutcome.Removed) _custodyAuthority.MarkLeafRemoved(_custody);
        if (outcome == PhysicalDirectoryCleanupOutcome.Retained) _namespaceRetained(CustodyPath);
        _released = true;
    }

    internal static async Task<bool> MatchesAsync(SafeFileHandle? file, byte[]? expected, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (file is null || expected is null) return file is null && expected is null;
        var buffer = new byte[Math.Min(65536, Math.Max(1, expected.Length))];
        var offset = 0;
        while (offset < expected.Length)
        {
            var count = await RandomAccess.ReadAsync(file, buffer.AsMemory(0, Math.Min(buffer.Length, expected.Length - offset)), offset, ct).ConfigureAwait(false);
            if (count == 0 || !buffer.AsSpan(0, count).SequenceEqual(expected.AsSpan(offset, count))) return false;
            offset += count;
        }
        return await RandomAccess.ReadAsync(file, buffer.AsMemory(0, 1), offset, ct).ConfigureAwait(false) == 0;
    }

    private static bool SameBytes(byte[]? left, byte[]? right) => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);
    private static PublicInvalidOperationException Conflict() => new("Physical source bytes changed before the operation could finish; reload or recover the operation before retrying.");
    public void Dispose() => OwnedResourceCleanup.Dispose("Source transition handle release failed.", null, _captured, _preparedHandle, _custody,
        _custodyAuthority, _parent);
}
