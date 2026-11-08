using System.Security.Cryptography;
using System.Text;
using RoslynMcp.Core.Services;
using RoslynMcp.Roslyn.Helpers;

namespace RoslynMcp.Roslyn.Services;

/// <summary>
/// ADR 0033: the state of one composite-preview target file on disk when the preview was stored.
/// A composite apply that finds the file in any other state (and not already in the state the
/// mutation would produce) refuses instead of overwriting a change it never saw.
/// </summary>
/// <param name="FilePath">The fully qualified target path.</param>
/// <param name="Exists">Whether the file existed. A file the composite creates is recorded absent.</param>
/// <param name="Sha256">
/// Lowercase hex SHA-256 of the raw file bytes, <see langword="null"/> exactly when the file was
/// absent. Raw bytes, not text: a BOM, encoding or line-ending change is an edit.
/// </param>
/// <remarks>
/// Captured from the disk when <c>Store</c> runs, not from the bytes the preview was computed from.
/// The comparison precedes the write by milliseconds and the atomic writer has no compare-and-swap,
/// so an edit inside that window is not detected. Both limits are recorded in ADR 0033.
/// </remarks>
public sealed record CompositeSourceFingerprint(string FilePath, bool Exists, string? Sha256)
{
    /// <summary>
    /// Fingerprints each distinct target of <paramref name="mutations"/> once (platform path
    /// identity, first occurrence wins, mirroring the undo snapshot).
    /// </summary>
    public static IReadOnlyList<CompositeSourceFingerprint> Capture(IReadOnlyList<CompositeFileMutation> mutations)
    {
        var seen = new HashSet<string>(FileSystemPath.Comparer);
        var fingerprints = new List<CompositeSourceFingerprint>(mutations.Count);
        foreach (var mutation in mutations)
        {
            var path = Path.GetFullPath(mutation.FilePath);
            if (seen.Add(path))
            {
                // A target that exists but cannot be read cannot be fingerprinted, so the preview cannot
                // promise to detect its drift: refuse it now, with a message that names the file only.
                if (!TryRead(path, out var bytes))
                {
                    throw new PublicInvalidOperationException(
                        $"The composite preview could not be stored: '{Path.GetFileName(path)}' exists but cannot be read " +
                        "(it is not a file, or access is denied), so a later change to it could not be detected. " +
                        "Fix its permissions or remove the conflicting entry, then re-issue the preview.");
                }

                fingerprints.Add(Of(path, bytes));
            }
        }

        return fingerprints;
    }

    /// <summary>
    /// Returns the file NAME (never the absolute path: public messages must not carry one) of the
    /// first target that no longer matches its fingerprint, or <see langword="null"/> when none drifted.
    /// A target already in the state its mutation would produce (a written file whose text equals the
    /// new content, a deleted file that is gone) is not drift: overwriting it destroys nothing, and it
    /// keeps an in-memory retry after a partial apply working.
    /// </summary>
    public static string? FindDrift(
        IReadOnlyList<CompositeSourceFingerprint> fingerprints,
        IReadOnlyList<CompositeFileMutation> mutations)
    {
        foreach (var fingerprint in fingerprints)
        {
            // A target that can no longer be read is a change nobody can vouch for: drift, refused with
            // the token and payload untouched (this runs under the claim lock, so nothing may escape raw).
            if (!TryRead(fingerprint.FilePath, out var current))
            {
                return Path.GetFileName(fingerprint.FilePath);
            }

            if (Of(fingerprint.FilePath, current) == fingerprint || IsAlreadyApplied(fingerprint.FilePath, current, mutations))
            {
                continue;
            }

            return Path.GetFileName(fingerprint.FilePath);
        }

        return null;
    }

    private static bool IsAlreadyApplied(string path, byte[]? current, IReadOnlyList<CompositeFileMutation> mutations)
    {
        // The last mutation of a path decides what the composite leaves behind.
        CompositeFileMutation? final = null;
        foreach (var mutation in mutations)
        {
            if (FileSystemPath.Comparer.Equals(Path.GetFullPath(mutation.FilePath), path))
            {
                final = mutation;
            }
        }

        if (final is null)
        {
            return false;
        }

        if (final.DeleteFile)
        {
            return current is null;
        }

        return current is not null && string.Equals(DecodeText(current), final.UpdatedContent ?? string.Empty, StringComparison.Ordinal);
    }

    private static CompositeSourceFingerprint Of(string path, byte[]? bytes) =>
        new(path, bytes is not null, bytes is null ? null : Convert.ToHexStringLower(SHA256.HashData(bytes)));

    private static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes, writable: false), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Reads the file. <paramref name="bytes"/> is <see langword="null"/> when it does not exist (a
    /// readable answer). Returns <see langword="false"/> when it exists but cannot be read: a
    /// directory in its place, access denied, or a sharing violation.
    /// </summary>
    private static bool TryRead(string path, out byte[]? bytes)
    {
        try
        {
            // FileShare.Delete matches the claim path's readers: never make a concurrent rename fail.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            bytes = buffer.ToArray();
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            bytes = null;
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            bytes = null;
            return false;
        }
    }
}
