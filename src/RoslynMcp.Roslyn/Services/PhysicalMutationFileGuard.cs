using RoslynMcp.Core.Services;

namespace RoslynMcp.Roslyn.Services;

/// <summary>Checks exact physical byte ownership through required live-authorized readers and writers.</summary>
internal static class PhysicalMutationFileGuard
{
    internal static async Task AssertMatchesAsync(string path, byte[]? expected,
        Func<string, CancellationToken, Task<byte[]?>> readAuthorized, CancellationToken ct)
    {
        var actual = await readAuthorized(path, ct).ConfigureAwait(false);
        if (!Matches(expected, actual))
            throw new PublicInvalidOperationException("Physical source bytes changed before the operation could finish; reload or recover the operation before retrying.");
    }

    internal static async Task RestoreEntryIfOwnedAsync(string path, byte[]? entry, byte[]? intendedPost,
        Func<string, CancellationToken, Task<byte[]?>> readAuthorized,
        Func<string, byte[]?, byte[]?, CancellationToken, Task> transitionAuthorized, CancellationToken ct)
    {
        var actual = await readAuthorized(path, ct).ConfigureAwait(false);
        if (Matches(entry, actual)) return;
        if (!Matches(intendedPost, actual))
            throw new PublicInvalidOperationException("Compensation found an external file edit. Its bytes were preserved; recover the conflicted operation before further mutations.");
        await transitionAuthorized(path, actual, entry, ct).ConfigureAwait(false);
    }

    private static bool Matches(byte[]? expected, byte[]? actual) => expected is null
        ? actual is null : actual is not null && expected.AsSpan().SequenceEqual(actual);
}

internal delegate Task SourceTransitionWriter(string path, byte[]? expected, byte[]? desired,
    PhysicalMutationRecovery operation, IWorkspaceRecoveryAuthority authority,
    CancellationToken ct);
